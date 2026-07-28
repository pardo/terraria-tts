using System.Text.RegularExpressions;

namespace ChatVoice
{
	/// <summary>
	/// Turns a raw Terraria chat line into something worth reading aloud.
	/// </summary>
	internal static class TextCleaner
	{
		// [c/ff0000:some text] and [n:Guide] carry readable words - keep the inner text.
		private static readonly Regex TagText =
			new(@"\[(?:c(?:/[0-9a-fA-F]{6})?|n):([^\]]*)\]", RegexOptions.Compiled);

		// [i:29], [i/s99:3507], [g:12], [a:1], [gt] ... are icons and glyphs - drop them.
		private static readonly Regex TagDrop = new(@"\[[^\]]*\]", RegexOptions.Compiled);

		private static readonly Regex Url =
			new(@"https?://\S+|www\.\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		// "aaaaaaaaaa" -> "aaa"
		private static readonly Regex Repeat = new(@"(.)\1{4,}", RegexOptions.Compiled);

		private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

		public static string Clean(string text, int maxChars)
		{
			if (string.IsNullOrEmpty(text))
				return string.Empty;

			text = TagText.Replace(text, "$1");
			text = TagDrop.Replace(text, " ");
			text = Url.Replace(text, " link ");
			text = Repeat.Replace(text, "$1$1$1");
			text = Whitespace.Replace(text, " ").Trim();

			if (text.Length > maxChars) {
				text = text.Substring(0, maxChars);
				int lastSpace = text.LastIndexOf(' ');
				if (lastSpace > 0)
					text = text.Substring(0, lastSpace);
				text += "...";
			}

			return text;
		}
	}
}
