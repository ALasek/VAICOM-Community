using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using VAICOM.Database;
using VAICOM.Interfaces;

namespace VAICOM.Standalone
{
    public sealed class StandaloneVoiceAttackProxy : IStandaloneHostControl
    {
        private readonly Dictionary<string, string> textValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> booleanValues = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly IStandaloneHostControl hostControl;
        private string matchingCommandText = string.Empty;

        public StandaloneVoiceAttackProxy(
            string vaDirectory = null,
            string appsDirectory = null,
            string soundsDirectory = null,
            bool installDcsFiles = true,
            string dcsInstallPath = null,
            IStandaloneHostControl hostControl = null)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string defaultAppsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VAICOM-Standalone");
            SessionState = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["VA_DIR"] = vaDirectory ?? baseDirectory,
                ["VA_APPS"] = appsDirectory ?? defaultAppsDirectory,
                ["VA_SOUNDS"] = soundsDirectory ?? Path.Combine(baseDirectory, "Sounds")
            };
            State = new StandaloneState();
            Command = new StandaloneCommand();
            Dictation = new StandaloneDictation();
            Utility = new StandaloneUtility(this);
            VAVersion = new Version(1, 16);
            InstallDcsFiles = installDcsFiles;
            DcsInstallPath = dcsInstallPath;
            this.hostControl = hostControl;
        }

        public event Action<StandaloneLogEntry> LogWritten;
        public event Action<string> CommandExecuted;
        public event Action<string, string> TextChanged;
        public event Action<string, bool> BooleanChanged;

        public string Context { get; set; } = string.Empty;
        public IDictionary SessionState { get; }
        public Version VAVersion { get; set; }
        public bool IsStandalone => true;
        public bool InstallDcsFiles { get; }
        public string DcsInstallPath { get; }
        public string ProfileName { get; set; } = "VAICOM for DCS World";
        public string CommandText { get; private set; } = string.Empty;
        public StandaloneState State { get; }
        public StandaloneCommand Command { get; }
        public StandaloneDictation Dictation { get; }
        public StandaloneUtility Utility { get; }
        public IReadOnlyDictionary<string, string> TextValues => textValues;
        public IReadOnlyDictionary<string, bool> BooleanValues => booleanValues;

        public string ParseTokens(string token)
        {
            return string.Equals(token, "{CMD}", StringComparison.OrdinalIgnoreCase) ? matchingCommandText : string.Empty;
        }

        public string GetProfileName()
        {
            return ProfileName ?? string.Empty;
        }

        public string GetText(string name)
        {
            return name != null && textValues.TryGetValue(name, out string value) ? value : string.Empty;
        }

        public void SetText(string name, object value)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            textValues[name] = text;
            TextChanged?.Invoke(name, text);
        }

        public void SetBoolean(string name, object value)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            bool boolean = ToBoolean(value);
            booleanValues[name] = boolean;
            BooleanChanged?.Invoke(name, boolean);
        }

        public void ExecuteCommand(string command)
        {
            CommandExecuted?.Invoke(command ?? string.Empty);
        }

        public void WriteToLog(string message)
        {
            WriteToLog(message, string.Empty);
        }

        public void WriteToLog(string message, string color)
        {
            LogWritten?.Invoke(new StandaloneLogEntry(message ?? string.Empty, color ?? string.Empty));
        }

        public void SetTranscript(string transcript, IEnumerable<string> segments = null)
        {
            CommandText = transcript ?? string.Empty;
            matchingCommandText = DeterministicAliasMatcher.NormalizeTranscript(CommandText);
            Command.SetSegments(segments ?? CommandText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            if (!string.Equals(CommandText, matchingCommandText, StringComparison.OrdinalIgnoreCase))
            {
                WriteToLog("ASR normalized: '" + CommandText + "' -> '" + matchingCommandText + "'", "orange");
            }
            if (Dictation.IsOn())
            {
                Dictation.Append(CommandText);
            }
        }

        public string RecoverAlias(string category, string searchInput)
        {
            if (!string.Equals(category, "command", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (!Aliases.inputscancats.TryGetValue(category, out Dictionary<string, string> aliases))
            {
                return string.Empty;
            }

            AliasRecoveryResult result = DeterministicAliasMatcher.Match(category, searchInput, aliases, IsRecoveryCandidateAvailable);
            if (result.Accepted)
            {
                WriteToLog(
                    "ASR recovered " + category + ": '" + searchInput + "' -> '" + result.Alias
                    + "' (score " + result.Score + ", margin " + result.Margin + ")",
                    "orange");
                return result.Alias;
            }

            if (!string.IsNullOrEmpty(result.Alias) && result.Score >= result.MinimumScore - 5)
            {
                WriteToLog(
                    "ASR recovery rejected " + category + ": best '" + result.Alias + "' scored " + result.Score
                    + "/" + result.MinimumScore + " with margin " + result.Margin + "/" + result.RequiredMargin,
                    "orange");
            }
            return string.Empty;
        }

        private static bool IsRecoveryCandidateAvailable(KeyValuePair<string, string> candidate)
        {
            if (!Commands.Table.TryGetValue(candidate.Value, out Command command))
            {
                return false;
            }

            if (global::VAICOM.State.dcsrunning && command.isRIO() && !global::VAICOM.State.AIRIOactive)
            {
                return false;
            }

            string moduleId = global::VAICOM.State.currentmodule?.Id ?? string.Empty;
            if (global::VAICOM.State.dcsrunning
                && command.RecipientClass().Equals(Recipientclasses.WSO)
                && !moduleId.Equals("F-4E-45MC", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (global::VAICOM.State.dcsrunning
                && command.isGeorge()
                && !moduleId.StartsWith("AH-64D", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!global::VAICOM.State.have["recipient"] || command.isSpecial())
            {
                return true;
            }

            string recipientKey = global::VAICOM.State.currentkey["recipient"];
            return !Recipients.Table.TryGetValue(recipientKey, out Recipient recipient)
                || command.RecipientClass().Equals(recipient.RecipientClass());
        }

        public void SetLongPressInvoked(bool value)
        {
            Utility.LongPressInvoked = value;
        }

        public StandaloneHostSnapshot GetHostSnapshot()
        {
            return hostControl?.GetHostSnapshot() ?? new StandaloneHostSnapshot
            {
                Status = "Host controls unavailable",
                WhisperRuntime = "Unavailable",
                MicrophoneDeviceNumber = -1,
                Tx1Binding = "Not assigned",
                Tx2Binding = "Not assigned",
                Error = "This proxy was created without a standalone host controller."
            };
        }

        public IReadOnlyList<StandaloneMicrophoneInfo> GetMicrophones()
        {
            return hostControl?.GetMicrophones() ?? Array.Empty<StandaloneMicrophoneInfo>();
        }

        public IReadOnlyList<StandaloneSpeechBackendInfo> GetSpeechBackends()
        {
            return hostControl?.GetSpeechBackends() ?? Array.Empty<StandaloneSpeechBackendInfo>();
        }

        public void SelectMicrophone(int deviceNumber)
        {
            RequireHostControl().SelectMicrophone(deviceNumber);
        }

        public void SelectSpeechBackend(string id)
        {
            RequireHostControl().SelectSpeechBackend(id);
        }

        public void RefreshInputDevices()
        {
            RequireHostControl().RefreshInputDevices();
        }

        public void BeginPttBindingCapture(int tx)
        {
            RequireHostControl().BeginPttBindingCapture(tx);
        }

        public void ClearPttBinding(int tx)
        {
            RequireHostControl().ClearPttBinding(tx);
        }

        public void CancelPttBindingCapture()
        {
            RequireHostControl().CancelPttBindingCapture();
        }

        private IStandaloneHostControl RequireHostControl()
        {
            return hostControl ?? throw new InvalidOperationException("Standalone host controls are unavailable.");
        }

        private static bool ToBoolean(object value)
        {
            if (value is bool boolean)
            {
                return boolean;
            }

            if (value is string text)
            {
                if (bool.TryParse(text, out bool parsed))
                {
                    return parsed;
                }

                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) && number != 0;
            }

            try
            {
                return value != null && Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public sealed class StandaloneState
        {
            public event Action<bool> ListeningChanged;

            public bool GetListeningEnabled()
            {
                return ListeningEnabled;
            }

            public void SetListeningEnabled(bool enabled)
            {
                ListeningEnabled = enabled;
                ListeningChanged?.Invoke(enabled);
            }

            public bool ListeningEnabled { get; private set; } = true;
        }

        public sealed class StandaloneCommand
        {
            private readonly HashSet<string> commands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, bool> commandStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, bool> categoryStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            private string[] segments = Array.Empty<string>();

            public IReadOnlyDictionary<string, bool> CommandStates => commandStates;
            public IReadOnlyDictionary<string, bool> CategoryStates => categoryStates;

            public bool Exists(string command)
            {
                return !string.IsNullOrEmpty(command) && commands.Contains(command);
            }

            public string Segment(int index)
            {
                return index >= 0 && index < segments.Length ? segments[index] : "Not set";
            }

            public void SetSessionEnabled(string command, bool enabled)
            {
                if (!string.IsNullOrEmpty(command))
                {
                    commandStates[command] = enabled;
                }
            }

            public void SetSessionEnabledByCategory(string category, bool enabled)
            {
                if (!string.IsNullOrEmpty(category))
                {
                    categoryStates[category] = enabled;
                }
            }

            public void Register(string command)
            {
                if (!string.IsNullOrEmpty(command))
                {
                    commands.Add(command);
                }
            }

            internal void SetSegments(IEnumerable<string> values)
            {
                segments = values.Where(value => value != null).ToArray();
            }
        }

        public sealed class StandaloneDictation
        {
            private string buffer = string.Empty;

            public bool IsOn()
            {
                return IsEnabled;
            }

            public void Start(out string currentBuffer)
            {
                IsEnabled = true;
                currentBuffer = buffer;
            }

            public void Stop()
            {
                IsEnabled = false;
            }

            public void ClearBuffer(bool keepLastPhrase, out string clearedBuffer)
            {
                clearedBuffer = buffer;
                buffer = keepLastPhrase ? LastPhrase : string.Empty;
            }

            public bool IsEnabled { get; private set; }
            public string Buffer => buffer;
            public string LastPhrase { get; private set; } = string.Empty;

            internal void Append(string phrase)
            {
                if (string.IsNullOrEmpty(phrase))
                {
                    return;
                }

                LastPhrase = phrase;
                buffer = string.IsNullOrEmpty(buffer) ? phrase : buffer + Environment.NewLine + phrase;
            }
        }

        public sealed class StandaloneUtility
        {
            private static readonly Dictionary<string, string> Digits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["zero"] = "0", ["oh"] = "0", ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4",
                ["five"] = "5", ["six"] = "6", ["seven"] = "7", ["eight"] = "8", ["nine"] = "9",
                ["ten"] = "10", ["eleven"] = "11", ["twelve"] = "12", ["thirteen"] = "13", ["fourteen"] = "14",
                ["fifteen"] = "15", ["sixteen"] = "16", ["seventeen"] = "17", ["eighteen"] = "18", ["nineteen"] = "19",
                ["twenty"] = "20", ["thirty"] = "30", ["forty"] = "40", ["fifty"] = "50", ["sixty"] = "60",
                ["seventy"] = "70", ["eighty"] = "80", ["ninety"] = "90"
            };

            private static readonly Dictionary<string, long> Cardinals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["zero"] = 0, ["oh"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4,
                ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9,
                ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14,
                ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,
                ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60,
                ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90
            };

            private readonly StandaloneVoiceAttackProxy proxy;

            internal StandaloneUtility(StandaloneVoiceAttackProxy proxy)
            {
                this.proxy = proxy;
            }

            public bool LongPressInvoked { get; set; }

            public string ParseTokens(string token)
            {
                if (string.Equals(token, "{CMDLONGPRESSINVOKED}", StringComparison.OrdinalIgnoreCase))
                {
                    return LongPressInvoked ? "1" : "0";
                }

                if (string.Equals(token, "{DICTATION:NEWLINE}", StringComparison.OrdinalIgnoreCase))
                {
                    return proxy.Dictation.Buffer;
                }

                const string segmentPrefix = "{CMDSEGMENT:";
                if (token != null && token.StartsWith(segmentPrefix, StringComparison.OrdinalIgnoreCase) && token.EndsWith("}", StringComparison.Ordinal))
                {
                    string indexText = token.Substring(segmentPrefix.Length, token.Length - segmentPrefix.Length - 1);
                    return int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                        ? SegmentOrEmpty(index)
                        : string.Empty;
                }

                const string numberPrefix = "{TXTNUM:\"";
                if (token != null && token.StartsWith(numberPrefix, StringComparison.OrdinalIgnoreCase) && token.EndsWith("\"}", StringComparison.Ordinal))
                {
                    string source = token.Substring(numberPrefix.Length, token.Length - numberPrefix.Length - 2);
                    return ToDigits(string.Equals(source, "{CMD}", StringComparison.OrdinalIgnoreCase) ? proxy.CommandText : source);
                }

                return string.Empty;
            }

            private string SegmentOrEmpty(int index)
            {
                string segment = proxy.Command.Segment(index);
                if (string.Equals(segment, "Not set", StringComparison.Ordinal))
                {
                    return string.Empty;
                }

                string number = ToDigits(segment.Trim().Trim(',', '.', ';', ':', '!', '?'));
                return string.IsNullOrEmpty(number) ? segment : number;
            }

            private static string ToDigits(string source)
            {
                if (string.IsNullOrWhiteSpace(source))
                {
                    return string.Empty;
                }

                string[] words = source.Split(new[] { ' ', '-', ',' }, StringSplitOptions.RemoveEmptyEntries);
                int decimalIndex = Array.FindIndex(words, word =>
                    word.Equals("decimal", StringComparison.OrdinalIgnoreCase) ||
                    word.Equals("point", StringComparison.OrdinalIgnoreCase));
                string whole = ParseNumberWords(words, 0, decimalIndex < 0 ? words.Length : decimalIndex);
                if (whole == null)
                {
                    return string.Empty;
                }

                if (decimalIndex < 0)
                {
                    return whole;
                }

                string fraction = ParseNumberWords(words, decimalIndex + 1, words.Length, true);
                return fraction == null ? string.Empty : whole + "." + fraction;
            }

            private static string ParseNumberWords(string[] words, int start, int end, bool digitSequence = false)
            {
                if (start >= end)
                {
                    return string.Empty;
                }

                bool cardinal = !digitSequence && words.Skip(start).Take(end - start).Any(word =>
                    word.Equals("hundred", StringComparison.OrdinalIgnoreCase) ||
                    word.Equals("thousand", StringComparison.OrdinalIgnoreCase) ||
                    Cardinals.TryGetValue(word, out long value) && value >= 20);
                if (!cardinal)
                {
                    var values = new List<string>();
                    for (int index = start; index < end; index++)
                    {
                        string word = words[index];
                        if (Digits.TryGetValue(word, out string digit))
                        {
                            values.Add(digit);
                        }
                        else if (word.All(char.IsDigit))
                        {
                            values.Add(word);
                        }
                        else
                        {
                            return null;
                        }
                    }
                    return string.Concat(values);
                }

                long total = 0;
                long group = 0;
                for (int index = start; index < end; index++)
                {
                    string word = words[index];
                    if (Cardinals.TryGetValue(word, out long value))
                    {
                        group += value;
                    }
                    else if (word.Equals("hundred", StringComparison.OrdinalIgnoreCase))
                    {
                        group = Math.Max(1, group) * 100;
                    }
                    else if (word.Equals("thousand", StringComparison.OrdinalIgnoreCase))
                    {
                        total += Math.Max(1, group) * 1000;
                        group = 0;
                    }
                    else if (!word.Equals("and", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }
                }
                return (total + group).ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    public sealed class StandaloneLogEntry
    {
        public StandaloneLogEntry(string message, string color)
        {
            Message = message;
            Color = color;
        }

        public string Message { get; }
        public string Color { get; }
    }
}
