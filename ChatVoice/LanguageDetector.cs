using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ChatVoice
{
	/// <summary>
	/// English/Spanish discrimination by stopword vote.
	///
	/// Two languages does not justify shipping a statistical model — this was
	/// verified correct on 12 chat-length sentences and costs nothing. If a third
	/// language is ever added, this is the point to reconsider.
	/// </summary>
	internal static class LanguageDetector
	{
		public const string English = "en";
		public const string Spanish = "es";

		// Characters that essentially only occur in Spanish.
		private const string SpanishChars = "áéíóúñü¿¡";

		private static readonly HashSet<string> EsWords = new(StringComparer.Ordinal) {
			"que", "de", "la", "el", "los", "las", "un", "una", "unos", "unas", "y",
			"es", "en", "por", "para", "con", "no", "se", "lo", "le", "mi", "tu", "su",
			"te", "me", "muy", "pero", "como", "esta", "este", "esto", "eso", "aqui",
			"alli", "hola", "gracias", "si", "mas", "bien", "vamos", "voy", "donde",
			"cuando", "hay", "tengo", "tienes", "quiero", "puedo", "puede", "hacer",
			"estoy", "estas", "somos", "son", "era", "fue", "ya", "todo", "todos",
			"nada", "algo", "porque", "tambien", "ahora", "mucho", "mucha", "amigo",
			"hermano", "jefe", "ayuda", "espera", "mira", "oye", "dale", "vale",
		};

		private static readonly HashSet<string> EnWords = new(StringComparer.Ordinal) {
			"the", "is", "are", "you", "and", "to", "of", "in", "it", "that", "this",
			"have", "has", "with", "for", "on", "not", "but", "what", "where", "when",
			"can", "get", "got", "my", "me", "we", "they", "he", "she", "do", "does",
			"dont", "im", "ive", "gonna", "wanna", "yeah", "yes", "no", "lol", "just",
			"there", "here", "come", "going", "need", "help", "look", "back", "guys",
			"boss", "dude", "please", "thanks", "sorry", "wait", "let", "make", "know",
		};

		private static readonly Regex WordPattern =
			new(@"[a-záéíóúñü]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		public static string Detect(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return English;

			string lowered = text.ToLowerInvariant();

			foreach (char c in lowered) {
				if (SpanishChars.IndexOf(c) >= 0)
					return Spanish;
			}

			MatchCollection matches = WordPattern.Matches(lowered);
			if (matches.Count < 2)
				return English;

			int es = 0, en = 0;
			foreach (Match m in matches) {
				string w = m.Value;
				if (EsWords.Contains(w)) es++;
				if (EnWords.Contains(w)) en++;
			}

			return es > en ? Spanish : English;
		}
	}
}
