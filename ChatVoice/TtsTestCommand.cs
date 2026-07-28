using Terraria.ModLoader;

namespace ChatVoice
{
	/// <summary>
	/// /tts hello world                 - speaks a line as yourself
	/// /tts as Carlos hola a todos      - speaks as an arbitrary username, which
	///                                    is how you audition voice assignment
	///                                    without a second player
	/// /tts install                     - retry the first-run data download
	/// </summary>
	public class TtsTestCommand : ModCommand
	{
		public override CommandType Type => CommandType.Chat;
		public override string Command => "tts";
		public override string Usage => "/tts [as <username>] <text>  |  /tts install";
		public override string Description => "Speak a line through Chat Voice.";

		public override void Action(CommandCaller caller, string input, string[] args)
		{
			if (args.Length == 0) {
				caller.Reply("Usage: " + Usage);
				return;
			}

			if (args.Length == 1 && args[0] == "install") {
				if (AssetInstaller.IsInstalling) {
					caller.Reply("[ChatVoice] A download is already running.");
				}
				else if (AssetInstaller.EverythingPresent()) {
					caller.Reply("[ChatVoice] Speech data is already installed.");
				}
				else {
					caller.Reply("[ChatVoice] Starting download ...");
					AssetInstaller.EnsureInstalled(Mod, force: true);
				}
				return;
			}

			string user = caller.Player?.name ?? "tester";
			int start = 0;

			if (args.Length >= 3 && args[0] == "as") {
				user = args[1];
				start = 2;
			}

			string text = string.Join(" ", args, start, args.Length - start);
			string lang = LanguageDetector.Detect(text);

			caller.Reply($"[ChatVoice] speaking as \"{user}\" ({lang})");
			PiperEngine.Enqueue(user, text);
		}
	}
}
