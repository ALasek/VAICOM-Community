using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VAICOM.Standalone.Speech
{
    public sealed class PushToTalkRecorder : IDisposable
    {
        public const int DefaultDeviceNumber = -1;
        public const int CaptureSampleRate = 48000;
        public const int TranscriptionSampleRate = 16000;
        public static readonly TimeSpan MinimumDuration = TimeSpan.FromMilliseconds(250);

        private static readonly WaveFormat CaptureFormat = new WaveFormat(CaptureSampleRate, 16, 1);
        private readonly object gate = new object();
        private readonly WaveInEvent waveIn;
        private readonly List<byte[]> buffers = new List<byte[]>();
        private TaskCompletionSource<object> recordingStopped;
        private bool recording;
        private bool stopping;
        private bool disposed;

        public PushToTalkRecorder(int deviceNumber = DefaultDeviceNumber)
        {
            waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = CaptureFormat,
                BufferMilliseconds = 50
            };
            waveIn.DataAvailable += OnDataAvailable;
            waveIn.RecordingStopped += OnRecordingStopped;
        }

        public bool IsRecording
        {
            get
            {
                lock (gate)
                {
                    return recording;
                }
            }
        }

        public void Start()
        {
            lock (gate)
            {
                ThrowIfDisposed();
                if (recording || stopping)
                {
                    throw new InvalidOperationException("Recording is already active.");
                }

                buffers.Clear();
                recordingStopped = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                recording = true;
            }

            try
            {
                waveIn.StartRecording();
            }
            catch
            {
                lock (gate)
                {
                    recording = false;
                }

                throw;
            }
        }

        public async Task<MemoryStream> StopAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            Task stopTask;
            lock (gate)
            {
                ThrowIfDisposed();
                if (!recording || stopping)
                {
                    throw new InvalidOperationException("Recording is not active.");
                }

                stopping = true;
                stopTask = recordingStopped.Task;
            }

            try
            {
                waveIn.StopRecording();
                Task completed = await Task.WhenAny(
                    stopTask,
                    Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)).ConfigureAwait(false);
                if (completed != stopTask)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException("The recording device did not stop within five seconds.");
                }
                await stopTask.ConfigureAwait(false);
            }
            catch
            {
                lock (gate)
                {
                    recording = false;
                    stopping = false;
                }
                throw;
            }

            byte[] pcm;
            lock (gate)
            {
                stopping = false;
                pcm = CombineBuffers();
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (pcm.Length < CaptureFormat.AverageBytesPerSecond * MinimumDuration.TotalSeconds)
            {
                throw new InvalidOperationException("Recorded audio is too short to transcribe.");
            }

            var wav = ResampleToWav(pcm);
            if (wav.Length - 44 < TranscriptionSampleRate * 2 * MinimumDuration.TotalSeconds)
            {
                wav.Dispose();
                throw new InvalidOperationException("Recorded audio is too short to transcribe.");
            }

            return wav;
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                recording = false;
                stopping = false;
                recordingStopped?.TrySetCanceled();
            }

            waveIn.DataAvailable -= OnDataAvailable;
            waveIn.RecordingStopped -= OnRecordingStopped;
            waveIn.Dispose();
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            var copy = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, copy, 0, copy.Length);
            lock (gate)
            {
                if (recording || stopping)
                {
                    buffers.Add(copy);
                }
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            lock (gate)
            {
                recording = false;
                if (e.Exception != null)
                {
                    recordingStopped?.TrySetException(e.Exception);
                }
                else
                {
                    recordingStopped?.TrySetResult(null);
                }
            }
        }

        private byte[] CombineBuffers()
        {
            var length = 0;
            foreach (var buffer in buffers)
            {
                length += buffer.Length;
            }

            var combined = new byte[length];
            var offset = 0;
            foreach (var buffer in buffers)
            {
                Buffer.BlockCopy(buffer, 0, combined, offset, buffer.Length);
                offset += buffer.Length;
            }

            return combined;
        }

        internal static MemoryStream ResampleToWav(byte[] pcm)
        {
            using (var source = new RawSourceWaveStream(new MemoryStream(pcm, false), CaptureFormat))
            {
                var resampler = new WdlResamplingSampleProvider(source.ToSampleProvider(), TranscriptionSampleRate);
                var output = new MemoryStream();
                output.SetLength(44);

                var samples = new float[4096];
                int read;
                while ((read = resampler.Read(samples, 0, samples.Length)) > 0)
                {
                    for (var index = 0; index < read; index++)
                    {
                        var sample = samples[index] > 1f ? 1f : samples[index] < -1f ? -1f : samples[index];
                        var value = (short)Math.Round(sample * short.MaxValue);
                        output.WriteByte((byte)value);
                        output.WriteByte((byte)(value >> 8));
                    }
                }

                WriteWavHeader(output);
                output.Position = 0;
                return output;
            }
        }

        private static void WriteWavHeader(Stream stream)
        {
            var dataLength = checked((int)stream.Length - 44);
            stream.Position = 0;
            WriteAscii(stream, "RIFF");
            WriteInt32(stream, dataLength + 36);
            WriteAscii(stream, "WAVEfmt ");
            WriteInt32(stream, 16);
            WriteInt16(stream, 1);
            WriteInt16(stream, 1);
            WriteInt32(stream, TranscriptionSampleRate);
            WriteInt32(stream, TranscriptionSampleRate * 2);
            WriteInt16(stream, 2);
            WriteInt16(stream, 16);
            WriteAscii(stream, "data");
            WriteInt32(stream, dataLength);
        }

        private static void WriteAscii(Stream stream, string value)
        {
            foreach (var character in value)
            {
                stream.WriteByte((byte)character);
            }
        }

        private static void WriteInt16(Stream stream, short value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
        }

        private static void WriteInt32(Stream stream, int value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(PushToTalkRecorder));
            }
        }
    }
}
