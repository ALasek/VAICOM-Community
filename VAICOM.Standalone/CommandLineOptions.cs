using System;
using System.Globalization;

namespace VAICOM.Standalone
{
    internal sealed class CommandLineOptions
    {
        public bool ShowHelp { get; private set; }
        public bool ListDevices { get; private set; }
        public bool DownloadModel { get; private set; }
        public bool SelfTest { get; private set; }
        public bool InitializeOnly { get; private set; }
        public bool OpenConfig { get; private set; }
        public bool InstallDcsFiles { get; private set; } = true;
        public string ModelPath { get; private set; }
        public string SpeechBackend { get; private set; }
        public bool SpeechBackendSpecified { get; private set; }
        public string DcsPath { get; private set; }
        public int Microphone { get; private set; } = -1;
        public bool MicrophoneSpecified { get; private set; }
        public int PttKey { get; private set; } = VirtualKeys.F13;
        public string PttKeyName { get; private set; } = "F13";
        public int Tx { get; private set; } = 1;
        public int? PttKeyTx1 { get; private set; }
        public string PttKeyTx1Name { get; private set; }
        public int? PttKeyTx2 { get; private set; }
        public string PttKeyTx2Name { get; private set; }
        public string Text { get; private set; }
        public bool HasPttOptions => legacyPttOptionSpecified || PttKeyTx1.HasValue || PttKeyTx2.HasValue;

        private bool legacyPttOptionSpecified;

        public static CommandLineOptions Parse(string[] args)
        {
            var options = new CommandLineOptions();
            for (var index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                switch (argument.ToLowerInvariant())
                {
                    case "-h":
                    case "--help":
                        options.ShowHelp = true;
                        break;
                    case "--list-devices":
                        options.ListDevices = true;
                        break;
                    case "--download-model":
                        options.DownloadModel = true;
                        break;
                    case "--self-test":
                        options.SelfTest = true;
                        break;
                    case "--initialize-only":
                        options.InitializeOnly = true;
                        break;
                    case "--open-config":
                        options.OpenConfig = true;
                        break;
                    case "--install-dcs-files":
                        options.InstallDcsFiles = true;
                        break;
                    case "--no-install-dcs-files":
                        options.InstallDcsFiles = false;
                        break;
                    case "--model":
                        options.ModelPath = Next(args, ref index, argument);
                        break;
                    case "--asr":
                        options.SpeechBackendSpecified = true;
                        string backend = Next(args, ref index, argument);
                        if (!SpeechBackendIds.IsKnown(backend))
                        {
                            throw new ArgumentException("--asr must be vosk, hybrid, or whisper.");
                        }
                        options.SpeechBackend = SpeechBackendIds.Normalize(backend);
                        break;
                    case "--dcs-path":
                        options.DcsPath = Next(args, ref index, argument);
                        break;
                    case "--mic":
                        options.MicrophoneSpecified = true;
                        options.Microphone = ParseInteger(Next(args, ref index, argument), argument);
                        break;
                    case "--ptt-key":
                        options.legacyPttOptionSpecified = true;
                        options.PttKeyName = Next(args, ref index, argument);
                        options.PttKey = VirtualKeys.Parse(options.PttKeyName);
                        break;
                    case "--tx":
                        options.legacyPttOptionSpecified = true;
                        string tx = Next(args, ref index, argument);
                        if (tx.StartsWith("TX", StringComparison.OrdinalIgnoreCase))
                        {
                            tx = tx.Substring(2);
                        }

                        options.Tx = ParseInteger(tx, argument);
                        if (options.Tx < 1 || options.Tx > 6)
                        {
                            throw new ArgumentException("--tx must be from 1 through 6.");
                        }
                        break;
                    case "--ptt-key-tx1":
                        options.PttKeyTx1Name = Next(args, ref index, argument);
                        options.PttKeyTx1 = VirtualKeys.Parse(options.PttKeyTx1Name);
                        break;
                    case "--ptt-key-tx2":
                        options.PttKeyTx2Name = Next(args, ref index, argument);
                        options.PttKeyTx2 = VirtualKeys.Parse(options.PttKeyTx2Name);
                        break;
                    case "--text":
                        options.Text = Next(args, ref index, argument);
                        break;
                    default:
                        throw new ArgumentException("Unknown argument: " + argument);
                }
            }

            if (options.Microphone < -1)
            {
                throw new ArgumentException("--mic must be -1 for the default device or a listed device number.");
            }

            bool hasExplicitPttBindings = options.PttKeyTx1.HasValue || options.PttKeyTx2.HasValue;
            if (hasExplicitPttBindings && options.legacyPttOptionSpecified)
            {
                throw new ArgumentException("Do not mix --ptt-key/--tx with --ptt-key-tx1/--ptt-key-tx2.");
            }

            if (options.PttKeyTx1.HasValue && options.PttKeyTx2.HasValue && options.PttKeyTx1 == options.PttKeyTx2)
            {
                throw new ArgumentException("TX1 and TX2 must use different PTT keys.");
            }

            if (options.legacyPttOptionSpecified && string.IsNullOrWhiteSpace(options.Text) && options.Tx > 2)
            {
                throw new ArgumentException("Standalone voice PTT currently supports TX1 and TX2; TX3 through TX6 remain available to --text.");
            }

            return options;
        }

        internal PttBinding[] GetPttBindings()
        {
            if (!PttKeyTx1.HasValue && !PttKeyTx2.HasValue)
            {
                return new[] { new PttBinding(PttKey, PttKeyName, Tx) };
            }

            var bindings = new PttBinding[(PttKeyTx1.HasValue ? 1 : 0) + (PttKeyTx2.HasValue ? 1 : 0)];
            int index = 0;
            if (PttKeyTx1.HasValue)
            {
                bindings[index++] = new PttBinding(PttKeyTx1.Value, PttKeyTx1Name, 1);
            }

            if (PttKeyTx2.HasValue)
            {
                bindings[index] = new PttBinding(PttKeyTx2.Value, PttKeyTx2Name, 2);
            }

            return bindings;
        }

        private static string Next(string[] args, ref int index, string argument)
        {
            if (++index >= args.Length)
            {
                throw new ArgumentException(argument + " requires a value.");
            }

            return args[index];
        }

        private static int ParseInteger(string value, string argument)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            {
                throw new ArgumentException(argument + " requires an integer.");
            }

            return result;
        }
    }
}
