using System;
using System.IO;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VAICOM.Standalone.Speech;

namespace VAICOM.Standalone
{
    internal static class StandaloneSelfTest
    {
        public static async Task RunAsync(string modelPath, TextWriter output)
        {
            OfflineVaicomSelfTest.Run(output);

            var devices = MicrophoneDevices.Enumerate();
            output.WriteLine("Microphones found: " + devices.Count);

            if (!File.Exists(modelPath))
            {
                output.WriteLine("Whisper model is not installed; optional Whisper transcription test skipped.");
                output.WriteLine("Standalone core self-test passed.");
                return;
            }

            using (var synthesized = new MemoryStream())
            {
                using (var synth = new SpeechSynthesizer())
                {
                    synth.SetOutputToWaveStream(synthesized);
                    synth.Speak("flight rejoin");
                }

                synthesized.Position = 0;
                using (var reader = new WaveFileReader(synthesized))
                using (var resampled = new MemoryStream())
                using (var transcriber = new WhisperTranscriber(modelPath))
                {
                    ISampleProvider source = reader.ToSampleProvider();
                    if (source.WaveFormat.Channels == 2)
                    {
                        source = new StereoToMonoSampleProvider(source);
                    }

                    var resampler = new WdlResamplingSampleProvider(source, PushToTalkRecorder.TranscriptionSampleRate);
                    WaveFileWriter.WriteWavFileToStream(resampled, resampler.ToWaveProvider16());
                    resampled.Position = 0;

                    string text = await transcriber.TranscribeAsync(resampled).ConfigureAwait(false);
                    output.WriteLine("Whisper runtime: " + transcriber.RuntimeLibrary);
                    output.WriteLine("Synthetic transcription: " + text);
                    if (text.IndexOf("flight", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        throw new InvalidOperationException("Whisper did not recognize the synthetic test phrase.");
                    }
                }
            }

            output.WriteLine("Full standalone self-test passed.");
        }
    }
}
