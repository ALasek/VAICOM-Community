using System;
using System.IO;
using Newtonsoft.Json;

namespace VAICOM.Standalone
{
    internal sealed class StandaloneHostSettings
    {
        public int Version { get; set; } = 2;
        public int MicrophoneDeviceNumber { get; set; } = -1;
        public string SpeechBackend { get; set; } = SpeechBackendIds.Vosk;
        public PttInputBinding Tx1 { get; set; } = PttInputBinding.Keyboard(VirtualKeys.F13, "F13");
        public PttInputBinding Tx2 { get; set; } = PttInputBinding.Keyboard(VirtualKeys.Parse("F14"), "F14");

        public StandaloneHostSettings Clone()
        {
            return new StandaloneHostSettings
            {
                Version = Version,
                MicrophoneDeviceNumber = MicrophoneDeviceNumber,
                SpeechBackend = SpeechBackend,
                Tx1 = Tx1?.Clone(),
                Tx2 = Tx2?.Clone()
            };
        }
    }

    internal sealed class StandaloneHostSettingsStore
    {
        private readonly string path;

        public StandaloneHostSettingsStore(string dataDirectory)
        {
            path = Path.Combine(dataDirectory, "host-settings.json");
        }

        public StandaloneHostSettings Load(out bool firstRun, out string error)
        {
            firstRun = !File.Exists(path);
            error = string.Empty;
            if (firstRun)
            {
                return new StandaloneHostSettings();
            }

            try
            {
                var settings = JsonConvert.DeserializeObject<StandaloneHostSettings>(File.ReadAllText(path));
                if (settings == null)
                {
                    throw new InvalidDataException("The settings file was empty.");
                }

                Normalize(settings);
                return settings;
            }
            catch (Exception exception)
            {
                error = "Could not load host settings: " + exception.Message;
                return new StandaloneHostSettings();
            }
        }

        public void Save(StandaloneHostSettings settings)
        {
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            string backup = path + ".previous";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(settings, Formatting.Indented));
            try
            {
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, backup, true);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static void Normalize(StandaloneHostSettings settings)
        {
            settings.Version = 2;
            settings.SpeechBackend = SpeechBackendIds.Normalize(settings.SpeechBackend);
            settings.Tx1 = NormalizeBinding(settings.Tx1, VirtualKeys.F13, "F13");
            settings.Tx2 = NormalizeBinding(settings.Tx2, VirtualKeys.Parse("F14"), "F14");
        }

        private static PttInputBinding NormalizeBinding(PttInputBinding binding, int fallbackKey, string fallbackName)
        {
            if (binding == null)
            {
                return PttInputBinding.Keyboard(fallbackKey, fallbackName);
            }

            if (binding.Kind == PttInputKind.None)
            {
                return PttInputBinding.None();
            }

            if (binding.Kind == PttInputKind.Keyboard)
            {
                if (!VirtualKeys.IsBindable(binding.VirtualKey))
                {
                    return PttInputBinding.Keyboard(fallbackKey, fallbackName);
                }

                binding.KeyName = string.IsNullOrWhiteSpace(binding.KeyName)
                    ? VirtualKeys.GetName(binding.VirtualKey)
                    : binding.KeyName;
                return binding;
            }

            if (binding.Kind == PttInputKind.DirectInputButton &&
                Guid.TryParse(binding.DeviceInstanceGuid, out Guid ignored) &&
                binding.ButtonIndex >= 0)
            {
                binding.DeviceName = string.IsNullOrWhiteSpace(binding.DeviceName) ? "Controller" : binding.DeviceName;
                return binding;
            }

            return PttInputBinding.Keyboard(fallbackKey, fallbackName);
        }
    }
}
