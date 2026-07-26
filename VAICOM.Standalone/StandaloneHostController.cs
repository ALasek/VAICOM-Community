using System;
using System.Collections.Generic;
using System.Linq;
using VAICOM.Interfaces;
using VAICOM.Standalone.Speech;

namespace VAICOM.Standalone
{
    internal sealed class StandaloneHostController : IStandaloneHostControl, IDisposable
    {
        private readonly object gate = new object();
        private readonly StandaloneHostSettingsStore store;
        private readonly PttInputService input;
        private StandaloneHostSettings settings;
        private Func<bool> isDcsConnected = () => false;
        private string status = "Starting";
        private string whisperRuntime = "Loading";
        private string error;
        private string microphoneName;
        private int capturingTx;
        private bool captureNeedsBaseline;
        private int microphoneVersion;
        private int bindingsVersion;

        public StandaloneHostController(string dataDirectory, CommandLineOptions options)
        {
            store = new StandaloneHostSettingsStore(dataDirectory);
            settings = store.Load(out bool firstRun, out string loadError);
            IsFirstRun = firstRun;
            error = loadError;

            bool changed = false;
            if (options.MicrophoneSpecified)
            {
                settings.MicrophoneDeviceNumber = options.Microphone;
                changed = true;
            }

            if (options.SpeechBackendSpecified)
            {
                settings.SpeechBackend = options.SpeechBackend;
                changed = true;
            }

            if (options.HasPttOptions && string.IsNullOrWhiteSpace(options.Text))
            {
                ApplyCommandLineBindings(options.GetPttBindings());
                changed = true;
            }

            if (firstRun || changed)
            {
                store.Save(settings);
            }

            microphoneName = ResolveMicrophoneName(settings.MicrophoneDeviceNumber);

            input = new PttInputService();
            if (!string.IsNullOrWhiteSpace(input.LastError))
            {
                error = input.LastError;
            }
        }

        public bool IsFirstRun { get; }

        public int MicrophoneDeviceNumber
        {
            get
            {
                lock (gate)
                {
                    return settings.MicrophoneDeviceNumber;
                }
            }
        }

        public string SpeechBackend
        {
            get
            {
                lock (gate)
                {
                    return settings.SpeechBackend;
                }
            }
        }

        public int MicrophoneVersion
        {
            get
            {
                lock (gate)
                {
                    return microphoneVersion;
                }
            }
        }

        public int BindingsVersion
        {
            get
            {
                lock (gate)
                {
                    return bindingsVersion;
                }
            }
        }

        public bool IsCapturingBinding
        {
            get
            {
                lock (gate)
                {
                    return capturingTx != 0;
                }
            }
        }

        public StandaloneHostSnapshot GetHostSnapshot()
        {
            StandaloneHostSettings snapshot;
            string snapshotStatus;
            string snapshotRuntime;
            string snapshotError;
            string snapshotMicrophoneName;
            int snapshotCapturingTx;
            Func<bool> dcsProvider;
            lock (gate)
            {
                snapshot = settings.Clone();
                snapshotStatus = status;
                snapshotRuntime = whisperRuntime;
                snapshotError = error;
                snapshotMicrophoneName = microphoneName;
                snapshotCapturingTx = capturingTx;
                dcsProvider = isDcsConnected;
            }

            string inputError = input.LastError;
            if (!string.IsNullOrWhiteSpace(inputError))
            {
                snapshotError = inputError;
            }

            return new StandaloneHostSnapshot
            {
                Status = snapshotStatus,
                IsDcsConnected = SafeGetDcsStatus(dcsProvider),
                WhisperRuntime = snapshotRuntime,
                SpeechBackendId = snapshot.SpeechBackend,
                MicrophoneDeviceNumber = snapshot.MicrophoneDeviceNumber,
                MicrophoneName = snapshotMicrophoneName,
                Tx1Binding = snapshot.Tx1?.DisplayName,
                Tx2Binding = snapshot.Tx2?.DisplayName,
                Tx1Connected = input.IsConnected(snapshot.Tx1),
                Tx2Connected = input.IsConnected(snapshot.Tx2),
                CapturingTx = snapshotCapturingTx,
                Error = snapshotError
            };
        }

        public IReadOnlyList<StandaloneMicrophoneInfo> GetMicrophones()
        {
            var microphones = new List<StandaloneMicrophoneInfo>
            {
                new StandaloneMicrophoneInfo
                {
                    DeviceNumber = -1,
                    Name = "Windows default recording device",
                    IsDefault = true
                }
            };
            microphones.AddRange(MicrophoneDevices.Enumerate().Select(device => new StandaloneMicrophoneInfo
            {
                DeviceNumber = device.DeviceNumber,
                Name = device.Name
            }));
            return microphones;
        }

        public IReadOnlyList<StandaloneSpeechBackendInfo> GetSpeechBackends()
        {
            return new[]
            {
                new StandaloneSpeechBackendInfo { Id = SpeechBackendIds.Vosk, Name = "Vosk constrained (short commands)" },
                new StandaloneSpeechBackendInfo { Id = SpeechBackendIds.Hybrid, Name = "Vosk with Whisper fallback" },
                new StandaloneSpeechBackendInfo { Id = SpeechBackendIds.Whisper, Name = "Whisper only" }
            };
        }

        public void SelectMicrophone(int deviceNumber)
        {
            if (deviceNumber < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(deviceNumber));
            }

            lock (gate)
            {
                if (settings.MicrophoneDeviceNumber == deviceNumber)
                {
                    return;
                }

                StandaloneHostSettings updated = settings.Clone();
                updated.MicrophoneDeviceNumber = deviceNumber;
                store.Save(updated);
                settings = updated;
                microphoneName = ResolveMicrophoneName(deviceNumber);
                microphoneVersion++;
                error = string.Empty;
            }
        }

        public void SelectSpeechBackend(string id)
        {
            string normalized = SpeechBackendIds.Normalize(id);
            lock (gate)
            {
                if (settings.SpeechBackend == normalized)
                {
                    return;
                }

                StandaloneHostSettings updated = settings.Clone();
                updated.SpeechBackend = normalized;
                store.Save(updated);
                settings = updated;
                error = string.Empty;
            }
        }

        public void RefreshInputDevices()
        {
            lock (gate)
            {
                microphoneName = ResolveMicrophoneName(settings.MicrophoneDeviceNumber);
            }
            input.Refresh();
        }

        public void BeginPttBindingCapture(int tx)
        {
            ValidateTx(tx);
            lock (gate)
            {
                capturingTx = tx;
                captureNeedsBaseline = true;
                error = string.Empty;
            }
        }

        public void ClearPttBinding(int tx)
        {
            ValidateTx(tx);
            lock (gate)
            {
                StandaloneHostSettings updated = settings.Clone();
                SetBinding(updated, tx, PttInputBinding.None());
                store.Save(updated);
                settings = updated;
                bindingsVersion++;
                if (capturingTx == tx)
                {
                    capturingTx = 0;
                }
                error = string.Empty;
            }
        }

        public void CancelPttBindingCapture()
        {
            lock (gate)
            {
                capturingTx = 0;
                captureNeedsBaseline = false;
            }
        }

        public PttBinding[] GetPttBindings()
        {
            lock (gate)
            {
                var bindings = new List<PttBinding>(2);
                if (settings.Tx1 != null && settings.Tx1.Kind != PttInputKind.None)
                {
                    bindings.Add(new PttBinding(settings.Tx1.Clone(), 1));
                }
                if (settings.Tx2 != null && settings.Tx2.Kind != PttInputKind.None)
                {
                    bindings.Add(new PttBinding(settings.Tx2.Clone(), 2));
                }
                return bindings.ToArray();
            }
        }

        public bool IsInputDown(PttInputBinding binding)
        {
            return input.IsDown(binding);
        }

        public bool ConsumeConfigShortcutRequest()
        {
            return input.ConsumeConfigShortcutRequest();
        }

        public void ProcessBindingCapture()
        {
            int tx;
            lock (gate)
            {
                tx = capturingTx;
                if (tx == 0)
                {
                    return;
                }

                if (captureNeedsBaseline)
                {
                    input.SynchronizeEdges();
                    captureNeedsBaseline = false;
                    return;
                }
            }

            if (!input.TryGetPressedBinding(out PttInputBinding binding))
            {
                return;
            }

            lock (gate)
            {
                if (capturingTx != tx)
                {
                    return;
                }

                StandaloneHostSettings updated = settings.Clone();
                PttInputBinding other = tx == 1 ? updated.Tx2 : updated.Tx1;
                if (BindingsEqual(binding, other))
                {
                    error = binding.DisplayName + " is already assigned to TX" + (tx == 1 ? 2 : 1) + ".";
                    capturingTx = 0;
                    return;
                }

                SetBinding(updated, tx, binding);
                store.Save(updated);
                settings = updated;
                capturingTx = 0;
                bindingsVersion++;
                error = string.Empty;
            }
        }

        public void SetDcsStatusProvider(Func<bool> provider)
        {
            lock (gate)
            {
                isDcsConnected = provider ?? (() => false);
            }
        }

        public void SetStatus(string value)
        {
            lock (gate)
            {
                status = value ?? string.Empty;
            }
        }

        public void SetWhisperRuntime(string value)
        {
            lock (gate)
            {
                whisperRuntime = value ?? string.Empty;
            }
        }

        public void SetError(string value)
        {
            lock (gate)
            {
                error = value ?? string.Empty;
            }
        }

        public void Dispose()
        {
            input.Dispose();
        }

        private void ApplyCommandLineBindings(PttBinding[] bindings)
        {
            if (bindings.Length == 1 && bindings[0].Tx != 1 && bindings[0].Tx != 2)
            {
                throw new ArgumentException("The standalone Host tab only supports TX1 and TX2 bindings.");
            }

            foreach (PttBinding binding in bindings)
            {
                SetBinding(settings, binding.Tx, binding.Input.Clone());
            }
        }

        private static string ResolveMicrophoneName(int deviceNumber)
        {
            if (deviceNumber == -1)
            {
                return "Windows default recording device";
            }

            try
            {
                MicrophoneDevice device = MicrophoneDevices.Enumerate()
                    .FirstOrDefault(item => item.DeviceNumber == deviceNumber);
                return device?.Name ?? "Unavailable device " + deviceNumber;
            }
            catch
            {
                return "Device " + deviceNumber;
            }
        }

        private static bool SafeGetDcsStatus(Func<bool> provider)
        {
            try
            {
                return provider();
            }
            catch
            {
                return false;
            }
        }

        private static void SetBinding(StandaloneHostSettings target, int tx, PttInputBinding binding)
        {
            if (tx == 1)
            {
                target.Tx1 = binding;
            }
            else
            {
                target.Tx2 = binding;
            }
        }

        private static bool BindingsEqual(PttInputBinding left, PttInputBinding right)
        {
            if (left == null || right == null || left.Kind != right.Kind || left.Kind == PttInputKind.None)
            {
                return false;
            }

            return left.Kind == PttInputKind.Keyboard
                ? left.VirtualKey == right.VirtualKey
                : left.ButtonIndex == right.ButtonIndex &&
                  string.Equals(left.DeviceInstanceGuid, right.DeviceInstanceGuid, StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateTx(int tx)
        {
            if (tx != 1 && tx != 2)
            {
                throw new ArgumentOutOfRangeException(nameof(tx), "Only TX1 and TX2 can be configured here.");
            }
        }
    }
}
