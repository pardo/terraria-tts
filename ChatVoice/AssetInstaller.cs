using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using Terraria;
using Terraria.ModLoader;

namespace ChatVoice
{
	/// <summary>
	/// Fetches the runtime data the mod cannot ship inside the .tmod — the native
	/// libpiper stack and the voice models — on first launch.
	///
	/// This exists because tModLoader has no way to load native code out of a mod
	/// archive, so those files must end up as loose files under
	/// <see cref="PiperNative.DataDir"/>. Making the mod fetch them itself is the
	/// only way a player can install by dropping a single .tmod into Mods\.
	///
	/// Everything happens on a background thread. Chat notices are posted through
	/// <see cref="Main.QueueMainThreadAction"/>; nothing here touches game state.
	/// </summary>
	internal static class AssetInstaller
	{
		/// <summary>
		/// Release that carries the data assets. Pinned to a tag rather than
		/// "latest" so an older mod build keeps working after a new release.
		/// </summary>
		private const string BaseUrl =
			"https://github.com/pardo/terraria-tts/releases/download/data-v1/";

		private sealed class Component
		{
			public string Name;        // shown in chat
			public string FileName;    // asset in the release
			public string Sha256;      // lowercase hex, or "" to skip verification
			public int Mb;             // approximate, for the chat notice
			public Func<bool> Present; // already installed?
		}

		private static readonly Component[] Components = {
			new() {
				Name = "speech engine",
				FileName = "ChatVoice-native-win-x64.zip",
				Sha256 = "dfff8e81bd5f753276e4186ee1791585cb9ebca4e562add141dc6898e8ac62df",
				Mb = 14,
				Present = () => PiperNative.IsInstalled(),
			},
			new() {
				Name = "voice models",
				FileName = "ChatVoice-voices.zip",
				Sha256 = "5bed3ce957974b1aa3a33580c3bad3fb488c65b8c451173bab9a396d2314fe49",
				Mb = 137,
				Present = VoicesPresent,
			},
		};

		private static readonly object _gate = new();
		private static bool _running;
		private static bool _attempted;
		private static CancellationTokenSource _cts;
		private static Mod _mod;

		/// <summary>True while a download is in flight, so other code can stay quiet.</summary>
		public static bool IsInstalling
		{
			get { lock (_gate) return _running; }
		}

		public static bool EverythingPresent()
		{
			foreach (Component c in Components) {
				if (!c.Present())
					return false;
			}
			return true;
		}

		private static bool VoicesPresent()
		{
			if (!Directory.Exists(PiperNative.VoicesDir))
				return false;
			// Any .onnx at all is enough to consider the pack installed; a missing
			// individual model is reported per-language by PiperEngine.
			return Directory.GetFiles(PiperNative.VoicesDir, "*.onnx").Length > 0;
		}

		/// <summary>
		/// Starts a download if anything is missing. Safe to call repeatedly; only
		/// one run happens per session unless <paramref name="force"/> is set.
		/// </summary>
		public static void EnsureInstalled(Mod mod, bool force = false)
		{
			_mod = mod;

			lock (_gate) {
				if (_running)
					return;
				if (_attempted && !force)
					return;
				if (EverythingPresent())
					return;

				_running = true;
				_attempted = true;
				_cts = new CancellationTokenSource();
			}

			var thread = new Thread(() => Run(_cts.Token)) {
				IsBackground = true,
				Name = "ChatVoice asset download",
			};
			thread.Start();
		}

		public static void Cancel()
		{
			try { _cts?.Cancel(); } catch { }
			lock (_gate) _running = false;
		}

		// ------------------------------------------------------------------

		private static void Run(CancellationToken token)
		{
			var missing = new List<Component>();
			foreach (Component c in Components) {
				if (!c.Present())
					missing.Add(c);
			}

			try {
				if (missing.Count == 0)
					return;

				int totalMb = 0;
				foreach (Component c in missing)
					totalMb += c.Mb;

				Say($"[ChatVoice] First-time setup: downloading {totalMb} MB of speech data. " +
					"This happens once. You can keep playing.");

				foreach (Component c in missing) {
					token.ThrowIfCancellationRequested();
					Install(c, token);
				}

				Say("[ChatVoice] Setup complete — chat will now be read aloud.");
			}
			catch (OperationCanceledException) {
				// Unloading; say nothing.
			}
			catch (Exception ex) {
				_mod?.Logger.Error("ChatVoice: asset download failed: " + ex);
				Say("[ChatVoice] Download failed: " + ex.Message);
				Say("[ChatVoice] Retry with /tts install, or install by hand — see " +
					"https://github.com/pardo/terraria-tts");
			}
			finally {
				lock (_gate) _running = false;
			}
		}

		private static void Install(Component c, CancellationToken token)
		{
			Directory.CreateDirectory(PiperNative.DataDir);
			string work = Path.Combine(PiperNative.DataDir, ".download");

			// A leftover work directory means a previous run died partway.
			if (Directory.Exists(work))
				Directory.Delete(work, true);
			Directory.CreateDirectory(work);

			try {
				string zip = Path.Combine(work, c.FileName);
				Download(BaseUrl + c.FileName, zip, c.Name, c.Mb, token);

				if (!string.IsNullOrEmpty(c.Sha256))
					VerifyHash(zip, c.Sha256, c.Name);

				Say($"[ChatVoice] Extracting {c.Name} ...");

				// Extract to a staging directory, then move the finished folders
				// into place, so a crash mid-extract never leaves a half-populated
				// native\ that IsInstalled() would accept.
				string staged = Path.Combine(work, "staged");
				Directory.CreateDirectory(staged);
				Extract(zip, staged);

				foreach (string dir in Directory.GetDirectories(staged)) {
					string dest = Path.Combine(PiperNative.DataDir, Path.GetFileName(dir));
					if (Directory.Exists(dest))
						Directory.Delete(dest, true);
					Directory.Move(dir, dest);
				}

				_mod?.Logger.Info($"ChatVoice: installed {c.Name}");
			}
			finally {
				try { Directory.Delete(work, true); } catch { }
			}
		}

		/// <summary>
		/// Extracts <paramref name="zipPath"/> under <paramref name="root"/>.
		///
		/// Hand-rolled rather than ZipFile.ExtractToDirectory because some zip
		/// writers (notably .NET Framework's, which is what Windows PowerShell
		/// uses) emit backslash separators, and those come out as one long
		/// filename instead of a directory tree. Entry paths are also checked
		/// against the root so a hostile archive cannot write outside it.
		/// </summary>
		private static void Extract(string zipPath, string root)
		{
			string rootFull = Path.GetFullPath(root);
			if (!rootFull.EndsWith(Path.DirectorySeparatorChar))
				rootFull += Path.DirectorySeparatorChar;

			using ZipArchive archive = ZipFile.OpenRead(zipPath);

			foreach (ZipArchiveEntry entry in archive.Entries) {
				string relative = entry.FullName.Replace('\\', '/');

				// Directory entry.
				if (relative.EndsWith("/")) {
					Directory.CreateDirectory(Path.Combine(rootFull, relative.Replace('/', Path.DirectorySeparatorChar)));
					continue;
				}

				string target = Path.GetFullPath(
					Path.Combine(rootFull, relative.Replace('/', Path.DirectorySeparatorChar)));

				if (!target.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
					throw new IOException($"Archive entry escapes the target directory: {entry.FullName}");

				Directory.CreateDirectory(Path.GetDirectoryName(target));
				entry.ExtractToFile(target, overwrite: true);
			}
		}

		private static void Download(string url, string dest, string label, int expectMb, CancellationToken token)
		{
			Say($"[ChatVoice] Downloading {label} (~{expectMb} MB) ...");

			using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
			http.DefaultRequestHeaders.Add("User-Agent", "ChatVoice-tModLoader");

			using HttpResponseMessage response =
				http.Send(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead, token);

			if (!response.IsSuccessStatusCode) {
				throw new IOException(
					$"{label}: server returned {(int)response.StatusCode} {response.ReasonPhrase}");
			}

			long total = response.Content.Headers.ContentLength ?? -1;

			using Stream src = response.Content.ReadAsStream(token);
			using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);

			byte[] buffer = new byte[81920];
			long done = 0;
			int nextReport = 25;
			int read;

			while ((read = src.Read(buffer, 0, buffer.Length)) > 0) {
				token.ThrowIfCancellationRequested();
				dst.Write(buffer, 0, read);
				done += read;

				if (total > 0) {
					int pct = (int)(done * 100 / total);
					if (pct >= nextReport) {
						Say($"[ChatVoice] {label}: {pct}%");
						nextReport += 25;
					}
				}
			}

			if (total > 0 && done != total)
				throw new IOException($"{label}: download truncated at {done} of {total} bytes");
		}

		private static void VerifyHash(string path, string expected, string label)
		{
			using var stream = File.OpenRead(path);
			byte[] hash = SHA256.HashData(stream);
			string actual = Convert.ToHexString(hash).ToLowerInvariant();

			if (actual != expected.ToLowerInvariant()) {
				throw new IOException(
					$"{label}: checksum mismatch (expected {expected}, got {actual}). " +
					"The download was corrupted or the release was changed.");
			}
		}

		private static void Say(string message)
		{
			_mod?.Logger.Info(message);
			try {
				Main.QueueMainThreadAction(() => Main.NewText(message, 255, 200, 120));
			}
			catch {
				// Headless or too early in load; the log line above is enough.
			}
		}
	}
}
