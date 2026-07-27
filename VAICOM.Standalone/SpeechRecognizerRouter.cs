using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VAICOM.Standalone.Speech;

namespace VAICOM.Standalone
{
    internal sealed class SpeechRecognitionResult
    {
        public string Text { get; set; }
        public string Engine { get; set; }
        public double? Confidence { get; set; }
        public bool UsedFallback { get; set; }
        public int OmittedGrammarPhrases { get; set; }
        public string OriginalText { get; set; }
    }

    internal sealed class SpeechRecognizerRouter : IDisposable
    {
        internal const double VoskFallbackThreshold = 0.72;

        private readonly object gate = new object();
        private readonly string whisperModelPath;
        private readonly string voskModelPath;
        private WhisperTranscriber whisper;
        private VoskTranscriber vosk;

        public SpeechRecognizerRouter(string whisperModelPath, string voskModelPath)
        {
            this.whisperModelPath = whisperModelPath;
            this.voskModelPath = voskModelPath;
        }

        public string RuntimeDescription
        {
            get
            {
                string voskState = Directory.Exists(voskModelPath) ? "Vosk ready" : "Vosk model missing";
                string whisperState = File.Exists(whisperModelPath) ? "Whisper ready" : "Whisper model optional";
                return voskState + "; " + whisperState;
            }
        }

        public async Task<SpeechRecognitionResult> TranscribeAsync(
            Stream wav,
            string backend,
            CancellationToken cancellationToken)
        {
            string selected = SpeechBackendIds.Normalize(backend);
            if (selected == SpeechBackendIds.Whisper)
            {
                return await TranscribeWhisper(wav, false, cancellationToken).ConfigureAwait(false);
            }

            string grammar = SpeechGrammar.BuildJson(out int ignored);
            Reset(wav);
            VoskTranscriptionResult result;
            try
            {
                result = await GetVosk().TranscribeAsync(wav, grammar, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (selected == SpeechBackendIds.Hybrid && !cancellationToken.IsCancellationRequested)
            {
                return await TranscribeWhisper(wav, true, cancellationToken).ConfigureAwait(false);
            }
            bool usable = TryResolve(result, out string resolvedText);
            if (selected == SpeechBackendIds.Hybrid && !usable)
            {
                return await TranscribeWhisper(wav, true, cancellationToken).ConfigureAwait(false);
            }

            return new SpeechRecognitionResult
            {
                Text = usable ? resolvedText : string.Empty,
                Engine = "Vosk",
                Confidence = result.Confidence,
                OmittedGrammarPhrases = result.OmittedGrammarPhrases,
                OriginalText = usable && !string.Equals(resolvedText, result.Text, StringComparison.OrdinalIgnoreCase)
                    ? result.Text
                    : string.Empty
            };
        }

        private async Task<SpeechRecognitionResult> TranscribeWhisper(
            Stream wav,
            bool usedFallback,
            CancellationToken cancellationToken)
        {
            Reset(wav);
            return new SpeechRecognitionResult
            {
                Text = await GetWhisper().TranscribeAsync(wav, cancellationToken).ConfigureAwait(false),
                Engine = "Whisper",
                UsedFallback = usedFallback
            };
        }

        internal static bool TryResolve(VoskTranscriptionResult result, out string resolvedText)
        {
            return TryResolve(result.Text, result.Confidence, out resolvedText);
        }

        internal static bool TryResolve(string text, double confidence, out string resolvedText)
        {
            resolvedText = string.Empty;
            if (string.IsNullOrWhiteSpace(text)
                || text.IndexOf("[unk]", StringComparison.OrdinalIgnoreCase) >= 0
                || confidence < VoskFallbackThreshold)
            {
                return false;
            }

            if (SpeechGrammar.IsValidRecognition(text))
            {
                resolvedText = text;
                return true;
            }

            return SpeechGrammar.TryRecoverRecognition(text, out resolvedText);
        }

        private static void Reset(Stream stream)
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }
        }

        private VoskTranscriber GetVosk()
        {
            lock (gate)
            {
                return vosk ?? (vosk = new VoskTranscriber(voskModelPath));
            }
        }

        private WhisperTranscriber GetWhisper()
        {
            lock (gate)
            {
                return whisper ?? (whisper = new WhisperTranscriber(whisperModelPath));
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                vosk?.Dispose();
                whisper?.Dispose();
            }
        }
    }
}
