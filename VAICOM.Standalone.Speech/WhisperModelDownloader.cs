using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net.Ggml;

namespace VAICOM.Standalone.Speech
{
    public static class WhisperModelDownloader
    {
        public const string SmallEnglishModelFileName = "ggml-small.en.bin";
        public const string SmallEnglishModelSha256 = "c6138d6d58ecc8322097e0f987c32f1be8bb0a18532a3f88f734d1bbf9c41e5d";

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
                VerifySmallEnglishModel(modelPath);
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

                VerifySmallEnglishModel(temporaryPath);

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

        public static void VerifySmallEnglishModel(string path)
        {
            string actual;
            using (var stream = File.OpenRead(path))
            using (var sha256 = SHA256.Create())
            {
                actual = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }

            if (!string.Equals(actual, SmallEnglishModelSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Whisper small.en model checksum mismatch. Expected " + SmallEnglishModelSha256 + ", got " + actual + ".");
            }
        }
    }
}
