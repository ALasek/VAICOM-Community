using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace VAICOM.Standalone.Speech
{
    public sealed class WhisperTranscriber : IDisposable
    {
        private readonly WhisperFactory factory;
        private readonly string language;
        private readonly SemaphoreSlim transcriptionLock = new SemaphoreSlim(1, 1);
        private bool disposed;

        public WhisperTranscriber(string modelPath, string language = "en")
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                throw new ArgumentException("A model path is required.", nameof(modelPath));
            }

            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException("Whisper model was not found.", modelPath);
            }

            if (string.IsNullOrWhiteSpace(language))
            {
                throw new ArgumentException("A language is required.", nameof(language));
            }

            this.language = language;
            factory = WhisperFactory.FromPath(modelPath);
        }

        public string RuntimeLibrary => RuntimeOptions.LoadedLibrary?.ToString() ?? "Unknown";

        public async Task<string> TranscribeAsync(Stream wav, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (wav == null)
            {
                throw new ArgumentNullException(nameof(wav));
            }

            ThrowIfDisposed();
            await transcriptionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                using (var processor = factory.CreateBuilder()
                    .WithLanguage(language)
                    .WithTemperature(0.0f)
                    .WithTemperatureInc(0.0f)
                    .Build())
                {
                    var text = new StringBuilder();
                    await foreach (var segment in processor.ProcessAsync(wav, cancellationToken).ConfigureAwait(false))
                    {
                        if (!string.IsNullOrWhiteSpace(segment.Text))
                        {
                            if (text.Length > 0)
                            {
                                text.Append(' ');
                            }

                            text.Append(segment.Text.Trim());
                        }
                    }

                    return Normalize(text.ToString());
                }
            }
            finally
            {
                transcriptionLock.Release();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            transcriptionLock.Wait();
            factory.Dispose();
            transcriptionLock.Dispose();
        }

        private static string Normalize(string value)
        {
            return string.Join(" ", value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(WhisperTranscriber));
            }
        }
    }
}
