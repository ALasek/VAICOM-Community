using System.Collections.Generic;

namespace VAICOM.Interfaces
{
    public interface IStandaloneHostControl
    {
        StandaloneHostSnapshot GetHostSnapshot();
        IReadOnlyList<StandaloneMicrophoneInfo> GetMicrophones();
        IReadOnlyList<StandaloneSpeechBackendInfo> GetSpeechBackends();
        void SelectMicrophone(int deviceNumber);
        void SelectSpeechBackend(string id);
        void RefreshInputDevices();
        void BeginPttBindingCapture(int tx);
        void ClearPttBinding(int tx);
        void CancelPttBindingCapture();
    }

    public sealed class StandaloneHostSnapshot
    {
        public string Status { get; set; }
        public bool IsDcsConnected { get; set; }
        public string WhisperRuntime { get; set; }
        public string SpeechBackendId { get; set; }
        public int MicrophoneDeviceNumber { get; set; }
        public string MicrophoneName { get; set; }
        public string Tx1Binding { get; set; }
        public string Tx2Binding { get; set; }
        public bool Tx1Connected { get; set; }
        public bool Tx2Connected { get; set; }
        public int CapturingTx { get; set; }
        public string Error { get; set; }
    }

    public sealed class StandaloneSpeechBackendInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public sealed class StandaloneMicrophoneInfo
    {
        public int DeviceNumber { get; set; }
        public string Name { get; set; }
        public bool IsDefault { get; set; }
    }
}
