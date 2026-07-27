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
            bool usable = IsUsable(result);
            if (selected == SpeechBackendIds.Hybrid && !usable)
            {
                return await TranscribeWhisper(wav, true, cancellationToken).ConfigureAwait(false);
            }

            return new SpeechRecognitionResult
            {
                Text = usable ? result.Text : string.Empty,
                Engine = "Vosk",
                Confidence = result.Confidence,
                OmittedGrammarPhrases = result.OmittedGrammarPhrases
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

        private static bool IsUsable(VoskTranscriptionResult result)
        {
            return !string.IsNullOrWhiteSpace(result.Text)
                && result.Text.IndexOf("[unk]", StringComparison.OrdinalIgnoreCase) < 0
                && SpeechGrammar.IsValidRecognition(result.Text)
                && result.Confidence >= VoskFallbackThreshold;
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
