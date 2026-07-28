using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Chat;
using Terraria.Localization;
using Terraria.ModLoader;

namespace ChatVoice
{
	/// <summary>
	/// Hooks the client-side chat display and hands every player message to the
	/// synthesis engine.
	/// </summary>
	public class ChatVoiceSystem : ModSystem
	{
		// "<PlayerName> the actual message"
		private static readonly Regex AuthorPrefix =
			new(@"^\s*<([^>]{1,40})>\s*(.*)$", RegexOptions.Compiled | RegexOptions.Singleline);

		private static readonly Dictionary<string, double> LastSpoken = new(StringComparer.OrdinalIgnoreCase);
		private static readonly Stopwatch Clock = Stopwatch.StartNew();

		// The first-run asset download is kicked off from the update loop rather
		// than Load(), because chat does not exist yet during mod loading and the
		// progress notices would go nowhere.
		private static bool _checkedAssets;

		private delegate void orig_DisplayMessage(NetworkText text, Color color, byte messageAuthor);

		public override void Load()
		{
			if (Main.dedServ)
				return;

			// MonoModHooks.Add is used instead of the generated On_ChatHelper events
			// because the generated class names move between tModLoader versions.
			MethodInfo target = typeof(ChatHelper).GetMethod(
				"DisplayMessage",
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
				null,
				new[] { typeof(NetworkText), typeof(Color), typeof(byte) },
				null);

			if (target == null) {
				Mod.Logger.Error("Could not find ChatHelper.DisplayMessage - chat will not be read aloud.");
				return;
			}

			MonoModHooks.Add(target, OnDisplayMessage);
			PiperEngine.Start(Mod);
		}

		public override void Unload()
		{
			AssetInstaller.Cancel();
			PiperEngine.Stop();
			LastSpoken.Clear();
			_checkedAssets = false;
		}

		private static void OnDisplayMessage(orig_DisplayMessage orig, NetworkText text, Color color, byte messageAuthor)
		{
			orig(text, color, messageAuthor);

			try {
				Handle(text, messageAuthor);
			}
			catch (Exception ex) {
				ModContent.GetInstance<ChatVoice>()?.Logger.Warn("ChatVoice failed to handle a message: " + ex);
			}
		}

		private static void Handle(NetworkText networkText, byte messageAuthor)
		{
			ChatVoiceConfig cfg = ChatVoiceConfig.Instance;
			if (cfg == null || !cfg.Enabled)
				return;

			string raw = networkText?.ToString();
			if (string.IsNullOrWhiteSpace(raw))
				return;

			string speaker = null;
			string body = raw;

			Match m = AuthorPrefix.Match(raw);
			if (m.Success) {
				speaker = m.Groups[1].Value;
				body = m.Groups[2].Value;
			}
			else if (messageAuthor < Main.maxPlayers && Main.player[messageAuthor] != null && Main.player[messageAuthor].active) {
				speaker = Main.player[messageAuthor].name;
			}

			// No identifiable player -> this is a system / server broadcast.
			if (speaker == null) {
				if (!cfg.ReadSystemMessages)
					return;
				speaker = "server";
			}

			if (!cfg.ReadOwnMessages && Main.LocalPlayer != null &&
				string.Equals(speaker, Main.LocalPlayer.name, StringComparison.OrdinalIgnoreCase))
				return;

			body = body.Trim();
			if (body.Length == 0 || body.StartsWith("/"))
				return;

			if (cfg.PerPlayerCooldown > 0f) {
				double now = Clock.Elapsed.TotalSeconds;
				if (LastSpoken.TryGetValue(speaker, out double last) && now - last < cfg.PerPlayerCooldown)
					return;
				LastSpoken[speaker] = now;
			}

			// Length limiting and markup stripping happen on the worker thread,
			// in TextCleaner.
			PiperEngine.Enqueue(speaker, body);
		}

		public override void PostUpdateEverything()
		{
			if (!_checkedAssets) {
				_checkedAssets = true;
				AssetInstaller.EnsureInstalled(Mod);
			}

			PiperEngine.PumpPlayback();
		}
	}
}
