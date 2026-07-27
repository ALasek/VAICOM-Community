using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VAICOM.Standalone
{
    internal sealed class StandaloneProfileCommand
    {
        public StandaloneProfileCommand(string context, params string[] segments)
        {
            Context = context;
            Segments = segments;
        }

        public string Context { get; }
        public string[] Segments { get; }
    }

    internal static class StandaloneProfileCommandRouter
    {
        private static readonly string[] DigitWords =
        {
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"
        };

        private static readonly string[] NumberWords =
        {
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
            "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
            "twenty", "twenty one", "twenty two", "twenty three", "twenty four", "twenty five", "twenty six",
            "twenty seven", "twenty eight", "twenty nine", "thirty"
        };

        private static readonly Dictionary<string, string> MarkerDestinations =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["waypoint one"] = "Waypoint 1",
                ["waypoint two"] = "Waypoint 2",
                ["waypoint three"] = "Waypoint 3",
                ["way point one"] = "Waypoint 1",
                ["way point two"] = "Waypoint 2",
                ["way point three"] = "Waypoint 3",
                ["steerpoint one"] = "Steerpoint 1",
                ["steerpoint two"] = "Steerpoint 2",
                ["steerpoint three"] = "Steerpoint 3",
                ["steer point one"] = "Steerpoint 1",
                ["steer point two"] = "Steerpoint 2",
                ["steer point three"] = "Steerpoint 3",
                ["fixed point"] = "Fixed Point",
                ["initial point"] = "Initial Point",
                ["surface target"] = "Surface Target",
                ["home base"] = "Home Base",
                ["defense point"] = "Defense Point",
                ["hostile zone"] = "Hostile Zone"
            };

        private static readonly Regex LinkTune = new Regex(
            @"^link tune (?<a>zero|one|two|three|four|five|six|seven|eight|nine) (?<b>zero|one|two|three|four|five|six|seven|eight|nine) decimal (?<c>zero|one|two|three|four|five|six|seven|eight|nine)$",
            RegexOptions.Compiled);
        private static readonly Regex RadioTune = new Regex(
            @"^radio tune (?<a>zero|one|two|three) (?<b>zero|one|two|three|four|five|six|seven|eight|nine) (?<c>zero|one|two|three|four|five|six|seven|eight|nine) decimal (?<d>zero|one|two|three|four|five|six|seven|eight|nine)(?: (?<e>zero|two five|five zero|seven five))?$",
            RegexOptions.Compiled);
        private static readonly Regex RadioFrequency = new Regex(
            @"^select(?: (?<band>am|fm))? (?<a>zero|one|two|three) (?<b>zero|one|two|three|four|five|six|seven|eight|nine) (?<c>zero|one|two|three|four|five|six|seven|eight|nine) decimal (?<d>zero|one|two|three|four|five|six|seven|eight|nine)(?: (?<e>zero|two five|five zero|seven five))?$",
            RegexOptions.Compiled);
        private static readonly Regex TacanTune = new Regex(
            @"^(?:tacan|tac in|tack in|tay can) tune (?<band>x ray|yankee) (?<a>zero|one) (?<b>zero|one|two|three|four|five|six|seven|eight|nine) (?<c>zero|one|two|three|four|five|six|seven|eight|nine)$",
            RegexOptions.Compiled);
        private static readonly Regex LaserCode = new Regex(
            @"^laser code (?<a>five|six|seven) (?<b>one|two|three|four|five|six|seven|eight) (?<c>one|two|three|four|five|six|seven|eight)$",
            RegexOptions.Compiled);
        private static readonly Regex MarkerDestination = new Regex(
            @"^map marker (?<number>.+) to (?<destination>way ?point (?:one|two|three)|steer ?point (?:one|two|three)|fixed point|initial point|surface target|home base|defense point|hostile zone)$",
            RegexOptions.Compiled);
        private static readonly Regex MarkerGrid = new Regex(
            @"^map marker (?<number>.+) to grid$",
            RegexOptions.Compiled);
        private static readonly Regex MarkerNavigate = new Regex(
            @"^(?<action>fly|orbit) marker (?<number>.+)$",
            RegexOptions.Compiled);
        private static readonly Regex MarkerTrack = new Regex(
            @"^track marker (?<number>.+)$",
            RegexOptions.Compiled);
        private static readonly Regex Channel = new Regex(
            @"^select channel (?<number>.+)$",
            RegexOptions.Compiled);
        private static readonly Regex RadarSector = new Regex(
            @"^scan sector angels (?<altitude>.+) (?<preposition>at|for) (?<range>.+)$",
            RegexOptions.Compiled);

        public static bool TryMatch(string transcript, out StandaloneProfileCommand command)
        {
            string normalized = SpeechGrammar.Normalize(DeterministicAliasMatcher.NormalizeTranscript(transcript));
            command = null;

            if (normalized == "chatter") return Match("chatter", new[] { "Chatter" }, out command);
            if (normalized == "configuration") return Match("config", new[] { "Configuration" }, out command);
            if (normalized == "configuration window reset") return Match("config.resetwindow", new[] { "Configuration Window Reset" }, out command);

            Match match = MarkerGrid.Match(normalized);
            if (match.Success && TryBoundedNumber(match, "number", 0, 10, out string markerNumber))
                return Match("airio.map.navgrid", new[] { "Map Marker", markerNumber, "to Grid" }, out command);

            match = MarkerDestination.Match(normalized);
            if (match.Success && TryBoundedNumber(match, "number", 1, 10, out markerNumber))
            {
                return Match("airio.map.markers", new[]
                {
                    "Map Marker", markerNumber, "to", MarkerDestinations[match.Groups["destination"].Value]
                }, out command);
            }

            match = MarkerNavigate.Match(normalized);
            if (match.Success && TryBoundedNumber(match, "number", 0, 10, out markerNumber))
                return Match("airio.map.markers.navigate", new[] { match.Groups["action"].Value, "Marker", markerNumber }, out command);

            match = MarkerTrack.Match(normalized);
            if (match.Success && TryBoundedNumber(match, "number", 0, 10, out markerNumber))
                return Match("airio.map.markers.track", new[] { "Track Marker", markerNumber }, out command);

            match = LinkTune.Match(normalized);
            if (match.Success) return Match("airio.dev.dl.tune", Segments(match, "Link Tune", "a", "b", "decimal", "c"), out command);

            match = RadioTune.Match(normalized);
            if (match.Success) return Match("airio.dev.radio.tune", new[]
            {
                "Radio Tune", match.Groups["a"].Value, match.Groups["b"].Value, match.Groups["c"].Value,
                "decimal", match.Groups["d"].Value, match.Groups["e"].Value
            }, out command);

            match = RadioFrequency.Match(normalized);
            if (match.Success) return Match("dev.radio.setfrq", new[]
            {
                "Select", match.Groups["band"].Value, match.Groups["a"].Value, match.Groups["b"].Value,
                match.Groups["c"].Value, "decimal", match.Groups["d"].Value, match.Groups["e"].Value
            }, out command);

            match = TacanTune.Match(normalized);
            if (match.Success) return Match("airio.dev.tacan.tune", new[]
            {
                "TACAN Tune", match.Groups["band"].Value == "x ray" ? "x-ray" : "yankee",
                match.Groups["a"].Value, match.Groups["b"].Value, match.Groups["c"].Value
            }, out command);

            match = LaserCode.Match(normalized);
            if (match.Success) return Match("airio.dev.laser.code", Segments(match, "Laser Code", "a", "b", "c"), out command);

            match = Channel.Match(normalized);
            if (match.Success && TryBoundedNumber(match, "number", 1, 30, out string channelNumber))
                return Match("dev.radio.setchn", new[] { "Select Channel", channelNumber }, out command);

            match = RadarSector.Match(normalized);
            if (match.Success
                && TryNumber(match.Groups["altitude"].Value, out int altitude) && altitude >= 0 && altitude <= 70 && altitude % 5 == 0
                && TryNumber(match.Groups["range"].Value, out int range) && range >= 0 && range <= 150 && range % 5 == 0)
            {
                return Match("airio.dev.radar.sector", new[]
                {
                    "Scan Sector Angels", altitude.ToString(), match.Groups["preposition"].Value, range.ToString()
                }, out command);
            }

            if (TryRadarWithoutPreposition(normalized, out altitude, out range))
            {
                return Match("airio.dev.radar.sector", new[] { "Scan Sector Angels", altitude.ToString(), "", range.ToString() }, out command);
            }

            return false;
        }

        public static IEnumerable<string> GrammarPhrases()
        {
            yield return "chatter";
            yield return "configuration";
            yield return "configuration window reset";

            foreach (string number in NumberWords.Skip(1).Take(30)) yield return "select channel " + number;
            foreach (string number in NumberWords.Take(11))
            {
                yield return "map marker " + number + " to grid";
                yield return "fly marker " + number;
                yield return "orbit marker " + number;
                yield return "track marker " + number;
            }
            foreach (string number in NumberWords.Skip(1).Take(10))
            foreach (string destination in MarkerDestinations.Keys)
                yield return "map marker " + number + " to " + destination;

            foreach (string a in DigitWords)
            foreach (string b in DigitWords)
            foreach (string c in DigitWords)
                yield return $"link tune {a} {b} decimal {c}";

            string[] endings = { "zero", "two five", "five zero", "seven five" };
            foreach (string a in DigitWords.Take(4))
            foreach (string b in DigitWords)
            foreach (string c in DigitWords)
            foreach (string d in DigitWords)
            foreach (string ending in endings)
            {
                yield return $"radio tune {a} {b} {c} decimal {d}";
                yield return $"radio tune {a} {b} {c} decimal {d} {ending}";
                yield return $"select {a} {b} {c} decimal {d}";
                yield return $"select {a} {b} {c} decimal {d} {ending}";
                yield return $"select am {a} {b} {c} decimal {d}";
                yield return $"select am {a} {b} {c} decimal {d} {ending}";
                yield return $"select fm {a} {b} {c} decimal {d}";
                yield return $"select fm {a} {b} {c} decimal {d} {ending}";
            }

            foreach (string prefix in new[] { "tacan", "tac in", "tack in", "tay can" })
            foreach (string band in new[] { "x ray", "yankee" })
            foreach (string a in DigitWords.Take(2))
            foreach (string b in DigitWords)
            foreach (string c in DigitWords)
                yield return $"{prefix} tune {band} {a} {b} {c}";

            foreach (string a in DigitWords.Skip(5).Take(3))
            foreach (string b in DigitWords.Skip(1).Take(8))
            foreach (string c in DigitWords.Skip(1).Take(8))
                yield return $"laser code {a} {b} {c}";

            for (int altitude = 0; altitude <= 70; altitude += 5)
            foreach (string preposition in new[] { "at", "for" })
            for (int range = 0; range <= 150; range += 5)
            {
                if (preposition == "at") yield return $"scan sector angels {NumberPhrase(altitude)} {NumberPhrase(range)}";
                yield return $"scan sector angels {NumberPhrase(altitude)} {preposition} {NumberPhrase(range)}";
            }
        }

        private static bool Match(string context, string[] segments, out StandaloneProfileCommand command)
        {
            command = new StandaloneProfileCommand(context, segments);
            return true;
        }

        private static string[] Segments(Match match, string header, params string[] names)
        {
            return new[] { header }.Concat(names.Select(name => match.Groups[name].Success ? match.Groups[name].Value : name)).ToArray();
        }

        private static bool TryNumber(string phrase, out int number)
        {
            for (int value = 0; value <= 150; value++)
            {
                if (string.Equals(phrase, NumberPhrase(value), StringComparison.Ordinal))
                {
                    number = value;
                    return true;
                }
            }

            string[] words = phrase.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 1 && words.All(word => Array.IndexOf(DigitWords, word) >= 0))
            {
                return int.TryParse(string.Concat(words.Select(word => Array.IndexOf(DigitWords, word).ToString())), out number);
            }

            number = 0;
            return false;
        }

        private static bool TryBoundedNumber(Match match, string group, int minimum, int maximum, out string value)
        {
            if (TryNumber(match.Groups[group].Value, out int number) && number >= minimum && number <= maximum)
            {
                value = number.ToString();
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static bool TryRadarWithoutPreposition(string normalized, out int altitude, out int range)
        {
            const string prefix = "scan sector angels ";
            altitude = 0;
            range = 0;
            if (!normalized.StartsWith(prefix, StringComparison.Ordinal)) return false;

            string[] words = normalized.Substring(prefix.Length).Split(' ');
            for (int split = 1; split < words.Length; split++)
            {
                if (TryNumber(string.Join(" ", words.Take(split)), out altitude)
                    && altitude >= 0 && altitude <= 70 && altitude % 5 == 0
                    && TryNumber(string.Join(" ", words.Skip(split)), out range)
                    && range >= 0 && range <= 150 && range % 5 == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string NumberPhrase(int value)
        {
            if (value <= 30) return NumberWords[value];
            if (value < 100)
            {
                string tens = new[] { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" }[value / 10];
                return value % 10 == 0 ? tens : tens + " " + DigitWords[value % 10];
            }
            return value == 100 ? "one hundred" : "one hundred " + NumberPhrase(value - 100);
        }
    }
}
