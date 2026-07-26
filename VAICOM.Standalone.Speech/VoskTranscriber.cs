using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Newtonsoft.Json.Linq;
using Vosk;

namespace VAICOM.Standalone.Speech
{
    public sealed class VoskTranscriptionResult
    {
        internal VoskTranscriptionResult(string text, double confidence, int omittedGrammarPhrases)
        {
            Text = text ?? string.Empty;
            Confidence = confidence;
            OmittedGrammarPhrases = omittedGrammarPhrases;
        }

        public string Text { get; }
        public double Confidence { get; }
        public int OmittedGrammarPhrases { get; }
    }

    public sealed class VoskTranscriber : IDisposable
    {
        private readonly Model model;
        private readonly SemaphoreSlim transcriptionLock = new SemaphoreSlim(1, 1);
        private string sourceGrammar;
        private string filteredGrammar;
        private bool disposed;

        public VoskTranscriber(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                throw new ArgumentException("A model path is required.", nameof(modelPath));
            }

            if (!Directory.Exists(modelPath))
            {
                throw new DirectoryNotFoundException("Vosk model was not found: " + modelPath);
            }

            Vosk.Vosk.SetLogLevel(-1);
            model = new Model(modelPath);
        }

        public async Task<VoskTranscriptionResult> TranscribeAsync(
            Stream wav,
            string grammarJson,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (wav == null)
            {
                throw new ArgumentNullException(nameof(wav));
            }

            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(grammarJson))
            {
                throw new ArgumentException("A recognition grammar is required.", nameof(grammarJson));
            }
            await transcriptionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                int omittedGrammarPhrases;
                string usableGrammar = FilterGrammar(grammarJson, out omittedGrammarPhrases);
                return await Task.Run(() => Transcribe(wav, usableGrammar, omittedGrammarPhrases), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                transcriptionLock.Release();
            }
        }

        private VoskTranscriptionResult Transcribe(Stream wav, string grammarJson, int omittedGrammarPhrases)
        {
            if (wav.CanSeek)
            {
                wav.Position = 0;
            }

            using (var reader = new WaveFileReader(wav))
            {
                if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1 || reader.WaveFormat.BitsPerSample != 16)
                {
                    throw new InvalidDataException("Vosk requires 16 kHz mono 16-bit PCM audio.");
                }

                using (var recognizer = new VoskRecognizer(model, 16000.0f, grammarJson))
                {
                    recognizer.SetWords(true);
                    var results = new List<JObject>();
                    var buffer = new byte[4096];
                    int count;
                    while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (recognizer.AcceptWaveform(buffer, count))
                        {
                            AddResult(results, recognizer.Result());
                        }
                    }

                    AddResult(results, recognizer.FinalResult());
                    string text = string.Join(" ", results
                        .Select(result => (string)result["text"])
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
                    double[] confidences = results
                        .SelectMany(result => result["result"] as JArray ?? new JArray())
                        .Select(word => (double?)word["conf"] ?? double.NaN)
                        .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
                        .ToArray();
                    double confidence = confidences.Length == 0 ? 0 : confidences.Average();
                    return new VoskTranscriptionResult(Normalize(text), confidence, omittedGrammarPhrases);
                }
            }
        }

        private string FilterGrammar(string grammarJson, out int omittedGrammarPhrases)
        {
            if (string.Equals(sourceGrammar, grammarJson, StringComparison.Ordinal))
            {
                omittedGrammarPhrases = 0;
                return filteredGrammar;
            }

            var accepted = new JArray();
            int omitted = 0;
            foreach (JToken item in JArray.Parse(grammarJson))
            {
                string phrase = (string)item;
                if (string.Equals(phrase, "[unk]", StringComparison.Ordinal)
                    || (!string.IsNullOrWhiteSpace(phrase) && phrase.Split(' ').All(word => model.FindWord(word) >= 0)))
                {
                    accepted.Add(phrase);
                }
                else
                {
                    omitted++;
                }
            }

            sourceGrammar = grammarJson;
            filteredGrammar = accepted.ToString(Newtonsoft.Json.Formatting.None);
            omittedGrammarPhrases = omitted;
            return filteredGrammar;
        }

        private static void AddResult(ICollection<JObject> results, string json)
        {
            if (!string.IsNullOrWhiteSpace(json))
            {
                results.Add(JObject.Parse(json));
            }
        }

        private static string Normalize(string value)
        {
            return string.Join(" ", (value ?? string.Empty).Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            transcriptionLock.Wait();
            model.Dispose();
            transcriptionLock.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(VoskTranscriber));
            }
        }
    }
}
