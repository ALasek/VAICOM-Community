using System;
using VAICOM.Interfaces;

namespace VAICOM.Standalone
{
    public sealed class VaicomRuntime : IDisposable
    {
        private bool initialized;
        private bool disposed;

        public VaicomRuntime(StandaloneVoiceAttackProxy proxy = null)
        {
            Proxy = proxy ?? new StandaloneVoiceAttackProxy();
            Proxy.LogWritten += entry => LogWritten?.Invoke(entry);
            Proxy.CommandExecuted += command => CommandExecuted?.Invoke(command);
        }

        public event Action<StandaloneLogEntry> LogWritten;
        public event Action<string> CommandExecuted;

        public StandaloneVoiceAttackProxy Proxy { get; }
        public bool IsInitialized => initialized;
        public bool IsDcsConnected => global::VAICOM.State.dcsrunning;

        public void Initialize()
        {
            ThrowIfDisposed();
            VA_Plugin.VA_Init1(Proxy);
            initialized = true;
        }

        public void Invoke(string context)
        {
            ThrowIfDisposed();
            Proxy.Context = context ?? string.Empty;
            VA_Plugin.VA_Invoke1(Proxy);
        }

        public void PressPtt(int channel, bool longPress = false)
        {
            InvokePtt(channel, true, longPress);
        }

        public void ReleasePtt(int channel, bool longPress = false)
        {
            InvokePtt(channel, false, longPress);
        }

        public void SubmitTranscript(string transcript, params string[] segments)
        {
            ThrowIfDisposed();
            Proxy.SetTranscript(transcript, segments != null && segments.Length > 0 ? segments : null);
            Invoke("alias.aicomms");
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            if (initialized)
            {
                VA_Plugin.VA_Exit1(Proxy);
            }

            disposed = true;
        }

        private void InvokePtt(int channel, bool pressed, bool longPress)
        {
            if (channel < 1 || channel > 6)
            {
                throw new ArgumentOutOfRangeException(nameof(channel), "PTT channel must be from 1 through 6.");
            }

            Proxy.SetLongPressInvoked(longPress);
            try
            {
                Invoke("ptt.hotkey.tx" + channel + (pressed ? ".press" : ".release"));
            }
            finally
            {
                Proxy.SetLongPressInvoked(false);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(VaicomRuntime));
            }
        }
    }
}
