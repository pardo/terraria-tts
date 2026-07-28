using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

// Exercises the same P/Invoke surface as ChatVoice\PiperNative.cs, outside
// Terraria, so marshalling can be tested without a game launch.
//
//   dotnet run -- [dataDir] [text]
//
// dataDir defaults to Documents\My Games\Terraria\tModLoader\ChatVoice.
//
// If you change PiperNative.cs, change it here too - the whole point is that
// the two declare the native API identically.

namespace PiperSmokeTest
{
	internal static class Program
	{
		private const string LIB = "piper";
		private const int PIPER_OK = 0;
		private const int PIPER_DONE = 1;

		private static string _nativeDir;

		[StructLayout(LayoutKind.Sequential)]
		public struct PiperAudioChunk
		{
			public IntPtr Samples;
			public UIntPtr NumSamples;
			public int SampleRate;
			[MarshalAs(UnmanagedType.I1)]
			public bool IsLast;

			public IntPtr Phonemes;
			public UIntPtr NumPhonemes;
			public IntPtr PhonemeIds;
			public UIntPtr NumPhonemeIds;
			public IntPtr Alignments;
			public UIntPtr NumAlignments;
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct PiperSynthesizeOptions
		{
			public int SpeakerId;
			public float LengthScale;
			public float NoiseScale;
			public float NoiseWScale;
		}

		[DllImport(LIB, EntryPoint = "piper_create", CallingConvention = CallingConvention.Cdecl)]
		private static extern IntPtr Create(
			[MarshalAs(UnmanagedType.LPUTF8Str)] string modelPath,
			[MarshalAs(UnmanagedType.LPUTF8Str)] string configPath,
			[MarshalAs(UnmanagedType.LPUTF8Str)] string espeakDataPath);

		[DllImport(LIB, EntryPoint = "piper_free", CallingConvention = CallingConvention.Cdecl)]
		private static extern void Free(IntPtr synth);

		[DllImport(LIB, EntryPoint = "piper_default_synthesize_options", CallingConvention = CallingConvention.Cdecl)]
		private static extern PiperSynthesizeOptions DefaultOptions(IntPtr synth);

		[DllImport(LIB, EntryPoint = "piper_synthesize_start", CallingConvention = CallingConvention.Cdecl)]
		private static extern int SynthesizeStart(IntPtr synth,
			[MarshalAs(UnmanagedType.LPUTF8Str)] string text,
			ref PiperSynthesizeOptions options);

		[DllImport(LIB, EntryPoint = "piper_synthesize_next", CallingConvention = CallingConvention.Cdecl)]
		private static extern int SynthesizeNext(IntPtr synth, out PiperAudioChunk chunk);

		[DllImport(LIB, EntryPoint = "piper_version", CallingConvention = CallingConvention.Cdecl)]
		private static extern IntPtr VersionPtr();

		private static int Main(string[] args)
		{
			string data = args.Length > 0 && args[0].Length > 0
				? args[0]
				: Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
					"My Games", "Terraria", "tModLoader", "ChatVoice");

			string text = args.Length > 1
				? args[1]
				: "hello there. this is a test of chat voice. it has three sentences.";

			_nativeDir = Path.Combine(data, "native");
			string voices = Path.Combine(data, "voices");
			string onnx = Path.Combine(voices, "en_US-libritts_r-medium.onnx");

			if (!File.Exists(onnx)) {
				Console.Error.WriteLine($"No voice model at {onnx}");
				return 2;
			}

			NativeLibrary.SetDllImportResolver(typeof(Program).Assembly, Resolve);

			Console.WriteLine($"piper_version   = {Marshal.PtrToStringUTF8(VersionPtr())}");
			Console.WriteLine($"sizeof(options) = {Marshal.SizeOf<PiperSynthesizeOptions>()} (expect 16)");
			Console.WriteLine($"sizeof(chunk)   = {Marshal.SizeOf<PiperAudioChunk>()} (expect 72)");

			IntPtr synth = Create(onnx, onnx + ".json", Path.Combine(_nativeDir, "espeak-ng-data"));
			Console.WriteLine($"piper_create    -> 0x{synth.ToInt64():X}");
			if (synth == IntPtr.Zero)
				return 1;

			try {
				// Risk point: 16-byte struct returned by value. Garbage here shows up
				// as bad audio rather than an error, so print it.
				PiperSynthesizeOptions opts = DefaultOptions(synth);
				Console.WriteLine($"defaults        -> speaker={opts.SpeakerId} length={opts.LengthScale} " +
								  $"noise={opts.NoiseScale} noise_w={opts.NoiseWScale}");

				Console.WriteLine($"text            = {text}");

				int rc = SynthesizeStart(synth, text, ref opts);
				Console.WriteLine($"synthesize_start-> {rc}");
				if (rc != PIPER_OK)
					return 1;

				long total = 0;
				int chunks = 0;

				while (true) {
					rc = SynthesizeNext(synth, out PiperAudioChunk chunk);
					Console.WriteLine($"  chunk {chunks}: rc={rc} num_samples={(ulong)chunk.NumSamples} " +
									  $"rate={chunk.SampleRate} is_last={chunk.IsLast}");

					if (rc < 0)
						break;

					// PIPER_DONE arrives *with* the final chunk. Consume before testing rc.
					total += (long)(ulong)chunk.NumSamples;
					chunks++;

					if (rc == PIPER_DONE || chunk.IsLast)
						break;

					if (chunks > 500) {
						Console.WriteLine("  RUNAWAY - giving up");
						break;
					}
				}

				Console.WriteLine($"TOTAL: {chunks} chunks, {total} samples, {total * 2} PCM16 bytes");
				return total > 0 ? 0 : 1;
			}
			finally {
				Free(synth);
			}
		}

		private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
		{
			if (name != LIB)
				return IntPtr.Zero;

			// onnxruntime first, by absolute path, exactly as PiperNative does.
			NativeLibrary.TryLoad(Path.Combine(_nativeDir, "onnxruntime.dll"), out _);

			foreach (string candidate in new[] { "piper.dll", "libpiper.dll", "libpiper.so", "libpiper.dylib" }) {
				string full = Path.Combine(_nativeDir, candidate);
				if (File.Exists(full) && NativeLibrary.TryLoad(full, out IntPtr handle))
					return handle;
			}

			throw new DllNotFoundException($"No libpiper in {_nativeDir}");
		}
	}
}
