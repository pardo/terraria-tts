using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.ModLoader;

namespace ChatVoice
{
	/// <summary>
	/// Owns the libpiper synthesizers and the speech pipeline.
	///
	/// Threading:
	///   chat hook (any thread)  -> Enqueue() puts a line on _pending
	///   worker thread           -> libpiper synthesis, produces PCM bytes
	///   main game thread        -> PumpPlayback() turns PCM into a SoundEffect,
	///                              plays it, disposes it
	///
	/// Audio objects are created and destroyed only on the main thread. libpiper
	/// is called only from the worker, one request at a time.
	/// </summary>
	internal static class PiperEngine
	{
		// lang -> voice model filename stem
		private static readonly Dictionary<string, string> VoiceFiles = new() {
			[LanguageDetector.English] = "en_US-libritts_r-medium",
			[LanguageDetector.Spanish] = "es_ES-sharvard-medium",
		};

		private sealed class Line
		{
			public string User;
			public string Text;
		}

		private sealed class Clip
		{
			public byte[] Pcm16;
			public int SampleRate;
		}

		private sealed class Voice
		{
			public IntPtr Handle;
			public int NumSpeakers;
			public PiperNative.PiperSynthesizeOptions Defaults;
		}

		private static readonly BlockingCollection<Line> _pending = new(new ConcurrentQueue<Line>());
		private static readonly ConcurrentQueue<Clip> _ready = new();
		private static readonly Dictionary<string, Voice> _voices = new();
		private static readonly object _voiceLock = new();

		private static Thread _worker;
		private static CancellationTokenSource _cts;
		private static Mod _mod;
		private static bool _warnedMissing;

		private static SoundEffect _playingSource;
		private static SoundEffectInstance _playing;

		// ------------------------------------------------------------------
		// lifecycle
		// ------------------------------------------------------------------

		public static void Start(Mod mod)
		{
			_mod = mod;
			_cts = new CancellationTokenSource();

			PiperNative.Install();

			_worker = new Thread(WorkerLoop) {
				IsBackground = true,
				Name = "ChatVoice synthesis",
			};
			_worker.Start();
		}

		public static void Stop()
		{
			try {
				_cts?.Cancel();
				_pending.CompleteAdding();
			}
			catch { }

			try { _worker?.Join(2000); } catch { }

			lock (_voiceLock) {
				foreach (Voice v in _voices.Values) {
					if (v.Handle != IntPtr.Zero) {
						try { PiperNative.Free(v.Handle); } catch { }
					}
				}
				_voices.Clear();
			}

			DisposePlaying();
			while (_ready.TryDequeue(out _)) { }

			_worker = null;
			_mod = null;
			_warnedMissing = false;
		}

		// ------------------------------------------------------------------
		// intake
		// ------------------------------------------------------------------

		public static void Enqueue(string user, string text)
		{
			ChatVoiceConfig cfg = ChatVoiceConfig.Instance;
			if (cfg == null || _pending.IsAddingCompleted)
				return;

			if (_pending.Count >= cfg.MaxQueue) {
				if (cfg.Verbose)
					_mod?.Logger.Info($"ChatVoice: queue full, dropping line from {user}");
				return;
			}

			_pending.Add(new Line { User = user, Text = text });
		}

		// ------------------------------------------------------------------
		// worker thread
		// ------------------------------------------------------------------

		private static void WorkerLoop()
		{
			foreach (Line line in _pending.GetConsumingEnumerable()) {
				if (_cts.IsCancellationRequested)
					return;

				ChatVoiceConfig cfg = ChatVoiceConfig.Instance;
				if (cfg == null || !cfg.Enabled)
					continue;

				if (!PiperNative.IsInstalled()) {
					if (!AssetInstaller.IsInstalling)
						WarnMissingOnce();
					continue;
				}

				try {
					string text = TextCleaner.Clean(line.Text, cfg.MaxCharacters);
					if (text.Length == 0)
						continue;

					string lang = cfg.ForceEnglish
						? LanguageDetector.English
						: LanguageDetector.Detect(text);

					Voice voice = GetVoice(lang);
					if (voice == null)
						continue;

					VoiceParams vp = VoiceAssigner.For(line.User, voice.NumSpeakers);
					Clip clip = Synthesize(voice, text, vp);

					if (clip != null) {
						if (cfg.Verbose)
							_mod?.Logger.Info($"ChatVoice: [{line.User}] ({lang} {vp}) {text}");
						_ready.Enqueue(clip);
					}
				}
				catch (Exception ex) {
					_mod?.Logger.Warn("ChatVoice synthesis failed: " + ex);
				}
			}
		}

		private static Voice GetVoice(string lang)
		{
			lock (_voiceLock) {
				if (_voices.TryGetValue(lang, out Voice existing))
					return existing;

				if (!VoiceFiles.TryGetValue(lang, out string stem))
					return null;

				string onnx = Path.Combine(PiperNative.VoicesDir, stem + ".onnx");
				string json = onnx + ".json";

				if (!File.Exists(onnx)) {
					_mod?.Logger.Warn($"ChatVoice: voice model missing: {onnx}");

					// A download in flight will produce the file shortly, so don't
					// poison the cache - the next line should try again.
					if (AssetInstaller.IsInstalling)
						return null;

					WarnMissingOnce();
					_voices[lang] = null;   // don't retry every line
					return null;
				}

				_mod?.Logger.Info($"ChatVoice: loading {stem} ...");
				IntPtr handle = PiperNative.Create(
					onnx,
					File.Exists(json) ? json : null,
					PiperNative.EspeakDataDir);

				if (handle == IntPtr.Zero) {
					_mod?.Logger.Error($"ChatVoice: piper_create returned null for {stem}");
					_voices[lang] = null;
					return null;
				}

				var voice = new Voice {
					Handle = handle,
					NumSpeakers = ReadNumSpeakers(json),
					Defaults = PiperNative.DefaultOptions(handle),
				};

				_mod?.Logger.Info($"ChatVoice: {stem} ready ({voice.NumSpeakers} speakers)");
				_voices[lang] = voice;
				return voice;
			}
		}

		/// <summary>
		/// The C API doesn't expose num_speakers, so read it out of the voice
		/// config. Regex rather than a JSON parser because the file also carries
		/// a speaker map with hundreds of entries and this is the only field
		/// needed — "num_speakers" appears exactly once, at the top level.
		/// </summary>
		private static int ReadNumSpeakers(string configPath)
		{
			try {
				if (!File.Exists(configPath))
					return 1;
				string content = File.ReadAllText(configPath);
				Match m = Regex.Match(content, "\"num_speakers\"\\s*:\\s*(\\d+)");
				if (m.Success && int.TryParse(m.Groups[1].Value, out int n) && n > 0)
					return n;
			}
			catch (Exception ex) {
				_mod?.Logger.Warn("ChatVoice: could not read num_speakers: " + ex.Message);
			}
			return 1;
		}

		private static Clip Synthesize(Voice voice, string text, VoiceParams vp)
		{
			PiperNative.PiperSynthesizeOptions opts = voice.Defaults;
			opts.SpeakerId = vp.SpeakerId;
			opts.LengthScale = vp.LengthScale;
			opts.NoiseWScale = vp.NoiseWScale;

			int rc = PiperNative.SynthesizeStart(voice.Handle, text, ref opts);
			if (rc != PiperNative.PIPER_OK) {
				_mod?.Logger.Warn($"ChatVoice: piper_synthesize_start returned {rc}");
				return null;
			}

			using var pcm = new MemoryStream();
			int sampleRate = 22050;

			while (true) {
				rc = PiperNative.SynthesizeNext(voice.Handle, out PiperNative.PiperAudioChunk chunk);

				if (rc < 0) {
					_mod?.Logger.Warn($"ChatVoice: piper_synthesize_next returned {rc}");
					break;
				}

				// PIPER_DONE arrives *with* the final chunk rather than after it,
				// so the chunk has to be consumed before the return code is
				// checked. Breaking on it first drops the last sentence of every
				// message - and since a short chat line is a single chunk, that
				// silently discarded all of the audio.
				int count = (int)chunk.NumSamples;
				if (count > 0 && chunk.Samples != IntPtr.Zero) {
					sampleRate = chunk.SampleRate > 0 ? chunk.SampleRate : sampleRate;

					float[] samples = new float[count];
					Marshal.Copy(chunk.Samples, samples, 0, count);

					// Piper hands back float PCM; SoundEffect wants 16-bit.
					byte[] buffer = new byte[count * 2];
					for (int i = 0; i < count; i++) {
						float s = samples[i];
						if (s > 1f) s = 1f;
						else if (s < -1f) s = -1f;
						short v = (short)(s * short.MaxValue);
						buffer[i * 2] = (byte)(v & 0xFF);
						buffer[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
					}
					pcm.Write(buffer, 0, buffer.Length);
				}

				if (rc == PiperNative.PIPER_DONE || chunk.IsLast)
					break;
			}

			byte[] data = pcm.ToArray();
			if (data.Length < 64) {
				// Used to be a silent return, which is how the dropped-final-chunk
				// bug above stayed invisible: no audio, no error, nothing in the log.
				_mod?.Logger.Warn(
					$"ChatVoice: synthesis produced no audio ({data.Length} bytes) for: {text}");
				return null;
			}

			return new Clip { Pcm16 = data, SampleRate = sampleRate };
		}

		private static void WarnMissingOnce()
		{
			if (_warnedMissing)
				return;
			_warnedMissing = true;

			string msg = $"[ChatVoice] Piper files not found. Expected them under: {PiperNative.DataDir}";
			_mod?.Logger.Warn(msg);
			Main.QueueMainThreadAction(() => Main.NewText(msg, 255, 200, 120));
		}

		// ------------------------------------------------------------------
		// main thread
		// ------------------------------------------------------------------

		public static void PumpPlayback()
		{
			ChatVoiceConfig cfg = ChatVoiceConfig.Instance;
			if (cfg == null)
				return;

			if (_playing != null) {
				if (_playing.State == SoundState.Stopped) {
					DisposePlaying();
				}
				else {
					_playing.Volume = Math.Clamp(cfg.Volume, 0f, 1f);
					return;
				}
			}

			if (!_ready.TryDequeue(out Clip clip))
				return;

			try {
				_playingSource = new SoundEffect(clip.Pcm16, clip.SampleRate, AudioChannels.Mono);
				_playing = _playingSource.CreateInstance();
				_playing.Volume = Math.Clamp(cfg.Volume, 0f, 1f);
				_playing.Play();
			}
			catch (Exception ex) {
				_mod?.Logger.Warn("ChatVoice playback failed: " + ex.Message);
				DisposePlaying();
			}
		}

		private static void DisposePlaying()
		{
			try { _playing?.Dispose(); } catch { }
			try { _playingSource?.Dispose(); } catch { }
			_playing = null;
			_playingSource = null;
		}
	}
}
