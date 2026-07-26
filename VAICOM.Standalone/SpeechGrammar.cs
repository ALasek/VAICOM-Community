using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using VAICOM.Database;

namespace VAICOM.Standalone
{
    internal static class SpeechGrammar
    {
        private static readonly Regex NonWord = new Regex("[^a-z0-9' ]+", RegexOptions.Compiled);

        public static string BuildJson(out int phraseCount)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return BuildJsonSnapshot(out phraseCount);
                }
                catch (InvalidOperationException) when (attempt < 2)
                {
                }
            }
        }

        private static string BuildJsonSnapshot(out int phraseCount)
        {
            var phrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var category in Aliases.inputscancats.Values)
            {
                foreach (string alias in category.Keys)
                {
                    string normalized = Normalize(alias);
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        phrases.Add(normalized);
                    }
                }
            }

            phrases.Add("take one");
            phrases.Add("take two");
            phrases.Add("take three");
            phrases.Add("take four");
            phrases.Add("take five");
            phrases.Add("take six");
            phrases.Add("take seven");
            phrases.Add("take eight");
            phrases.Add("take nine");
            phrases.Add("take ten");
            phrases.Add(StandaloneSpecialCommands.GotItPhrase);
            phraseCount = phrases.Count;
            phrases.Add("[unk]");
            return JsonConvert.SerializeObject(phrases.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        }

        internal static string Normalize(string value)
        {
            string lowered = (value ?? string.Empty).ToLowerInvariant().Replace('-', ' ');
            return string.Join(" ", NonWord.Replace(lowered, " ")
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }

        public static bool IsValidRecognition(string value)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return IsValidRecognitionSnapshot(value);
                }
                catch (InvalidOperationException) when (attempt < 2)
                {
                }
            }
        }

        private static bool IsValidRecognitionSnapshot(string value)
        {
            string normalized = Normalize(value);
            if (string.IsNullOrWhiteSpace(normalized)) return false;
            if (StandaloneSpecialCommands.IsMatch(normalized)) return true;

            var aliases = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var category in Aliases.inputscancats)
            {
                foreach (string alias in category.Value.Keys)
                {
                    string phrase = Normalize(alias);
                    if (!aliases.TryGetValue(phrase, out HashSet<string> categories))
                    {
                        categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        aliases.Add(phrase, categories);
                    }
                    categories.Add(category.Key);
                }
            }

            if (aliases.ContainsKey(normalized)) return true;
            string[] words = normalized.Split(' ');
            for (int split = 1; split < words.Length; split++)
            {
                if (aliases.TryGetValue(string.Join(" ", words.Take(split)), out HashSet<string> left)
                    && aliases.TryGetValue(string.Join(" ", words.Skip(split)), out HashSet<string> right)
                    && ((left.Contains("command") && right.Any(category => !category.Equals("command", StringComparison.OrdinalIgnoreCase)))
                        || (right.Contains("command") && left.Any(category => !category.Equals("command", StringComparison.OrdinalIgnoreCase)))))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
