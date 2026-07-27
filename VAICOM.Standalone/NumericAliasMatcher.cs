using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VAICOM.Standalone
{
    internal static class NumericAliasMatcher
    {
        private static readonly string[] DigitWords =
        {
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"
        };

        private static readonly Regex NumberRun = new Regex(@"\d+", RegexOptions.Compiled);

        public static IEnumerable<string> GrammarVariants(string alias)
        {
            if (string.IsNullOrWhiteSpace(alias) || !NumberRun.IsMatch(alias))
            {
                yield break;
            }

            MatchCollection numbers = NumberRun.Matches(alias);
            var literals = new List<string>();
            int position = 0;
            foreach (Match number in numbers)
            {
                literals.Add(alias.Substring(position, number.Index - position));
                position = number.Index + number.Length;
            }
            literals.Add(alias.Substring(position));

            var phrases = new List<string> { literals[0] };
            for (int index = 0; index < numbers.Count; index++)
            {
                string[] alternatives = NumberVariants(numbers[index].Value).ToArray();
                phrases = phrases
                    .SelectMany(prefix => alternatives.Select(number => prefix + number + literals[index + 1]))
                    .Take(512)
                    .ToList();
            }

            foreach (string phrase in phrases
                .Select(SpeechGrammar.Normalize)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                yield return phrase;
            }
        }

        public static bool TryRecoverTranscript(string transcript, out string recovered)
        {
            IEnumerable<KeyValuePair<string, string>> aliases = global::VAICOM.Database.Aliases.inputscancats.Values
                .Where(category => category != null)
                .SelectMany(category => category);
            return TryRecoverTranscript(transcript, aliases, out recovered);
        }

        internal static bool TryRecoverTranscript(
            string transcript,
            IEnumerable<KeyValuePair<string, string>> aliases,
            out string recovered)
        {
            recovered = string.Empty;

            string[] input = Tokenize(DeterministicAliasMatcher.NormalizeTranscript(transcript));
            if (input.Length == 0)
            {
                return false;
            }

            var matches = new Dictionary<string, NumericMatch>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> alias in aliases ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                if (!NumberRun.IsMatch(alias.Key))
                {
                    continue;
                }

                foreach (string variant in GrammarVariants(alias.Key))
                {
                    string[] candidate = Tokenize(variant);
                    int start = FindSequence(input, candidate);
                    if (start < 0)
                    {
                        continue;
                    }

                    var match = new NumericMatch(alias.Key.Replace("*", string.Empty), start, candidate.Length);
                    if (!matches.TryGetValue(match.Alias, out NumericMatch current)
                        || match.Length > current.Length
                        || match.Length == current.Length && match.Alias.Length > current.Alias.Length)
                    {
                        matches[match.Alias] = match;
                    }
                }
            }

            NumericMatch[] ranked = matches.Values
                .OrderByDescending(match => match.Length)
                .ThenByDescending(match => match.Alias.Length)
                .ToArray();
            if (ranked.Length == 0
                || ranked.Length > 1 && ranked[0].Length == ranked[1].Length && ranked[0].Alias.Length == ranked[1].Alias.Length)
            {
                return false;
            }

            NumericMatch best = ranked[0];
            recovered = string.Join(" ", input.Take(best.Start)
                .Concat(Tokenize(SpeechGrammar.Normalize(best.Alias)))
                .Concat(input.Skip(best.Start + best.Length)));
            return true;
        }

        private static IEnumerable<string> NumberVariants(string digits)
        {
            var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                string.Join(" ", digits.Select(character => DigitWords[character - '0']))
            };

            if (digits.IndexOf('0') >= 0)
            {
                variants.Add(string.Join(" ", digits.Select(character => character == '0' ? "oh" : DigitWords[character - '0'])));
            }

            if (int.TryParse(digits, out int value))
            {
                if (digits.Length == 1 || digits[0] != '0')
                {
                    variants.Add(ToCardinal(value, false));
                    variants.Add(ToCardinal(value, true));
                }

                if (digits.Length == 3)
                {
                    int tail = value % 100;
                    if (tail >= 10)
                    {
                        string prefix = digits[0] == '0' ? "zero" : DigitWords[digits[0] - '0'];
                        variants.Add(prefix + " " + ToCardinal(tail, false));
                        if (digits[0] == '0')
                        {
                            variants.Add("oh " + ToCardinal(tail, false));
                        }
                    }
                }
            }

            return variants.Where(value => !string.IsNullOrWhiteSpace(value));
        }

        private static string ToCardinal(int value, bool includeAnd)
        {
            if (value < 10) return DigitWords[value];
            if (value < 20)
            {
                return new[]
                {
                    "ten", "eleven", "twelve", "thirteen", "fourteen",
                    "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"
                }[value - 10];
            }
            if (value < 100)
            {
                string tens = new[] { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" }[value / 10];
                return value % 10 == 0 ? tens : tens + " " + DigitWords[value % 10];
            }
            if (value < 1000)
            {
                string remainder = value % 100 == 0
                    ? string.Empty
                    : (includeAnd ? " and " : " ") + ToCardinal(value % 100, includeAnd);
                return DigitWords[value / 100] + " hundred" + remainder;
            }
            if (value < 1000000)
            {
                string remainder = value % 1000 == 0 ? string.Empty : " " + ToCardinal(value % 1000, includeAnd);
                return ToCardinal(value / 1000, includeAnd) + " thousand" + remainder;
            }
            return string.Empty;
        }

        private static string[] Tokenize(string value)
        {
            return Regex.Matches(SpeechGrammar.Normalize(value), @"[a-z0-9]+")
                .Cast<Match>()
                .Select(match => match.Value)
                .ToArray();
        }

        private static int FindSequence(string[] input, string[] candidate)
        {
            if (candidate.Length == 0 || candidate.Length > input.Length)
            {
                return -1;
            }

            for (int start = 0; start + candidate.Length <= input.Length; start++)
            {
                if (candidate.Select((word, index) => string.Equals(word, input[start + index], StringComparison.OrdinalIgnoreCase)).All(equal => equal))
                {
                    return start;
                }
            }
            return -1;
        }

        private sealed class NumericMatch
        {
            public NumericMatch(string alias, int start, int length)
            {
                Alias = alias;
                Start = start;
                Length = length;
            }

            public string Alias { get; }
            public int Start { get; }
            public int Length { get; }
        }
    }
}
