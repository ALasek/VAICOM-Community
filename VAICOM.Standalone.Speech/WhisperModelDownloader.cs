using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net.Ggml;

namespace VAICOM.Standalone.Speech
{
    public static class WhisperModelDownloader
    {
        public const string SmallEnglishModelFileName = "ggml-small.en.bin";

        public static async Task<string> DownloadSmallEnglishAsync(string destinationDirectory, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                throw new ArgumentException("A destination directory is required.", nameof(destinationDirectory));
            }

            Directory.CreateDirectory(destinationDirectory);
            var modelPath = Path.Combine(destinationDirectory, SmallEnglishModelFileName);
            if (File.Exists(modelPath))
            {
                return modelPath;
            }

            var temporaryPath = modelPath + "." + Guid.NewGuid().ToString("N") + ".download";
            try
            {
                using (var model = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.SmallEn, cancellationToken: cancellationToken).ConfigureAwait(false))
                using (var destination = File.Create(temporaryPath))
                {
                    await model.CopyToAsync(destination, 81920, cancellationToken).ConfigureAwait(false);
                }

                if (!File.Exists(modelPath))
                {
                    File.Move(temporaryPath, modelPath);
                }

                return modelPath;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }
}
