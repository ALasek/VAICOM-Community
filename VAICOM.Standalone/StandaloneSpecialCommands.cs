using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VAICOM.Standalone
{
    internal static class StandaloneSpecialCommands
    {
        internal const string GotItPhrase = "got it";

        public static bool IsMatch(string transcript)
        {
            return string.Equals(SpeechGrammar.Normalize(transcript), GotItPhrase, StringComparison.Ordinal);
        }

        public static bool TryExecute(string transcript, out string message)
        {
            if (!IsMatch(transcript))
            {
                message = null;
                return false;
            }

            DcsKeyboard.SendSpace();
            message = "Special command: got it -> Space";
            return true;
        }
    }

    internal static class DcsKeyboard
    {
        private const uint InputKeyboard = 1;
        private const uint KeyEventScanCode = 0x0008;
        private const uint KeyEventKeyUp = 0x0002;
        private const ushort SpaceScanCode = 0x39;

        public static void SendSpace()
        {
            IntPtr foregroundWindow = GetForegroundWindow();
            GetWindowThreadProcessId(foregroundWindow, out uint processId);
            if (processId == 0 || !IsDcsProcess(processId))
            {
                throw new InvalidOperationException("'got it' was recognized, but DCS is not the foreground application; Space was not sent.");
            }

            var inputs = new[]
            {
                KeyboardInput(SpaceScanCode, KeyEventScanCode),
                KeyboardInput(SpaceScanCode, KeyEventScanCode | KeyEventKeyUp)
            };
            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input))) != (uint)inputs.Length)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not send Space to DCS.");
            }
        }

        private static bool IsDcsProcess(uint processId)
        {
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    return string.Equals(process.ProcessName, "DCS", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static Input KeyboardInput(ushort scanCode, uint flags)
        {
            return new Input
            {
                Type = InputKeyboard,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInputData
                    {
                        ScanCode = scanCode,
                        Flags = flags
                    }
                }
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public uint Type;
            public InputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MouseInputData Mouse;
            [FieldOffset(0)] public KeyboardInputData Keyboard;
            [FieldOffset(0)] public HardwareInputData Hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInputData
        {
            public int X;
            public int Y;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInputData
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HardwareInputData
        {
            public uint Message;
            public ushort ParameterLow;
            public ushort ParameterHigh;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);
    }
}
