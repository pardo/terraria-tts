using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace ChatVoice
{
	// Labels and tooltips live in Localization/en-US.hjson.
	public class ChatVoiceConfig : ModConfig
	{
		public override ConfigScope Mode => ConfigScope.ClientSide;

		public static ChatVoiceConfig Instance;

		[DefaultValue(true)]
		public bool Enabled { get; set; }

		[Range(0f, 1f)]
		[DefaultValue(0.85f)]
		[Slider]
		public float Volume { get; set; }

		[DefaultValue(false)]
		public bool ForceEnglish { get; set; }

		[DefaultValue(false)]
		public bool ReadOwnMessages { get; set; }

		[DefaultValue(false)]
		public bool ReadSystemMessages { get; set; }

		[Range(10, 500)]
		[DefaultValue(220)]
		public int MaxCharacters { get; set; }

		[Range(0f, 30f)]
		[DefaultValue(1.5f)]
		public float PerPlayerCooldown { get; set; }

		[Range(1, 20)]
		[DefaultValue(5)]
		public int MaxQueue { get; set; }

		[DefaultValue(false)]
		public bool Verbose { get; set; }

		public override void OnLoaded() => Instance = this;
	}
}
