using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FuzzySharp;

namespace VAICOM.Standalone
{
    public sealed class AliasRecoveryResult
    {
        internal AliasRecoveryResult(string alias, string value, int score, int runnerUpScore, int minimumScore, int requiredMargin)
        {
            Alias = alias ?? string.Empty;
            Value = value ?? string.Empty;
            Score = score;
            RunnerUpScore = runnerUpScore;
            MinimumScore = minimumScore;
            RequiredMargin = requiredMargin;
        }

        public string Alias { get; }
        public string Value { get; }
        public int Score { get; }
        public int RunnerUpScore { get; }
        public int MinimumScore { get; }
        public int RequiredMargin { get; }
        public int Margin => Score - RunnerUpScore;
        public bool Accepted => !string.IsNullOrEmpty(Alias) && Score >= MinimumScore && Margin >= RequiredMargin;
    }

    public static class DeterministicAliasMatcher
    {
        private static readonly string[] DigitWords =
        {
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"
        };
        private static readonly Regex AbortTakeoffArtifactRegex = new Regex(
            @"\b(?:a\s+board|aboard|abort)\s+(?:to\s+)?take[\s-]+off\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RearmArtifactRegex = new Regex(
            @"\b(?:riam|ream|rear\s+arm)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex NumericSeparatorRegex = new Regex(@"(?<=\d)\s*[,./-]\s*(?=\d)", RegexOptions.Compiled);
        private static readonly Regex DigitRegex = new Regex(@"\d", RegexOptions.Compiled);
        private static readonly Regex WordRegex = new Regex(@"[a-z0-9]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex WhitespaceBeforePunctuationRegex = new Regex(@"\s+([,.;:!?])", RegexOptions.Compiled);

        public static string NormalizeTranscript(string transcript)
        {
            string corrected = AbortTakeoffArtifactRegex.Replace(transcript ?? string.Empty, "abort takeoff");
            corrected = RearmArtifactRegex.Replace(corrected, "rearm");
            string separated = NumericSeparatorRegex.Replace(corrected, " ");
            string expanded = DigitRegex.Replace(separated, match => " " + DigitWords[match.Value[0] - '0'] + " ");
            string collapsed = WhitespaceRegex.Replace(expanded, " ").Trim();
            return WhitespaceBeforePunctuationRegex.Replace(collapsed, "$1");
        }

        public static AliasRecoveryResult Match(
            string category,
            string transcript,
            IEnumerable<KeyValuePair<string, string>> aliases,
            Func<KeyValuePair<string, string>, bool> candidateFilter = null)
        {
            string[] inputTokens = Tokenize(NormalizeTranscript(transcript));
            var bestByValue = new Dictionary<string, ScoredAlias>(StringComparer.OrdinalIgnoreCase);

            if (inputTokens.Length == 0 || aliases == null)
            {
                return EmptyResult(category);
            }

            foreach (KeyValuePair<string, string> candidate in aliases)
            {
                if (candidateFilter != null && !candidateFilter(candidate))
                {
                    continue;
                }

                string alias = (candidate.Key ?? string.Empty).Replace("*", string.Empty).Trim();
                string[] aliasTokens = Tokenize(alias);
                int compactLength = string.Concat(aliasTokens).Length;
                if (aliasTokens.Length == 0 || compactLength < 6)
                {
                    continue;
                }

                int score = ScoreAlias(inputTokens, aliasTokens);
                var scored = new ScoredAlias(alias, candidate.Value, score, compactLength);
                if (!bestByValue.TryGetValue(scored.Value, out ScoredAlias current)
                    || scored.Score > current.Score
                    || scored.Score == current.Score && scored.Alias.Length > current.Alias.Length)
                {
                    bestByValue[scored.Value] = scored;
                }
            }

            ScoredAlias[] ranked = bestByValue.Values
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Alias.Length)
                .ThenBy(candidate => candidate.Alias, StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToArray();
            if (ranked.Length == 0)
            {
                return EmptyResult(category);
            }

            ScoredAlias best = ranked[0];
            int runnerUpScore = ranked.Length > 1 ? ranked[1].Score : 0;
            return new AliasRecoveryResult(
                best.Alias,
                best.Value,
                best.Score,
                runnerUpScore,
                MinimumScore(category, best.CompactLength),
                RequiredMargin(category));
        }

        private static AliasRecoveryResult EmptyResult(string category)
        {
            return new AliasRecoveryResult(string.Empty, string.Empty, 0, 0, MinimumScore(category, 0), RequiredMargin(category));
        }

        private static int ScoreAlias(string[] inputTokens, string[] aliasTokens)
        {
            string alias = string.Join(" ", aliasTokens);
            string compactAlias = string.Concat(aliasTokens);
            int minimumWindow = Math.Max(1, aliasTokens.Length - 1);
            int maximumWindow = Math.Min(inputTokens.Length, aliasTokens.Length + 2);
            int best = 0;

            for (int length = minimumWindow; length <= maximumWindow; length++)
            {
                for (int start = 0; start + length <= inputTokens.Length; start++)
                {
                    string[] windowTokens = inputTokens.Skip(start).Take(length).ToArray();
                    int spacedScore = Fuzz.Ratio(string.Join(" ", windowTokens), alias);
                    int compactScore = Fuzz.Ratio(string.Concat(windowTokens), compactAlias);
                    best = Math.Max(best, Math.Max(spacedScore, compactScore));
                }
            }

            return best;
        }

        private static string[] Tokenize(string value)
        {
            return WordRegex.Matches(value ?? string.Empty)
                .Cast<Match>()
                .Select(match => match.Value.ToLowerInvariant())
                .ToArray();
        }

        private static int MinimumScore(string category, int compactLength)
        {
            int score = compactLength >= 14 ? 84 : compactLength >= 10 ? 86 : compactLength >= 7 ? 89 : 92;
            return string.Equals(category, "recipient", StringComparison.OrdinalIgnoreCase) ? score + 2 : score;
        }

        private static int RequiredMargin(string category)
        {
            return string.Equals(category, "recipient", StringComparison.OrdinalIgnoreCase) ? 10 : 7;
        }

        private sealed class ScoredAlias
        {
            public ScoredAlias(string alias, string value, int score, int compactLength)
            {
                Alias = alias;
                Value = value ?? string.Empty;
                Score = score;
                CompactLength = compactLength;
            }

            public string Alias { get; }
            public string Value { get; }
            public int Score { get; }
            public int CompactLength { get; }
        }
    }
}
