using System;

namespace VAICOM.Standalone
{
    internal static class SpeechBackendIds
    {
        public const string Vosk = "vosk";
        public const string Hybrid = "hybrid";
        public const string Whisper = "whisper";

        public static string Normalize(string value)
        {
            if (string.Equals(value, Hybrid, StringComparison.OrdinalIgnoreCase)) return Hybrid;
            if (string.Equals(value, Whisper, StringComparison.OrdinalIgnoreCase)) return Whisper;
            return Vosk;
        }

        public static bool IsKnown(string value)
        {
            return string.Equals(value, Vosk, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, Hybrid, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, Whisper, StringComparison.OrdinalIgnoreCase);
        }
    }
}
