using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using SharpGen.Runtime;
using Vortice.DirectInput;

namespace VAICOM.Standalone
{
    internal sealed class PttInputService : IDisposable
    {
        private readonly object gate = new object();
        private readonly bool[] keyboardCurrent = new bool[256];
        private readonly bool[] keyboardPrevious = new bool[256];
        private readonly Dictionary<Guid, ControllerDevice> devices = new Dictionary<Guid, ControllerDevice>();
        private readonly Queue<PttInputBinding> pressedBindings = new Queue<PttInputBinding>();
        private readonly ManualResetEventSlim ready = new ManualResetEventSlim();
        private readonly GlobalHotkeyService configHotkey;
        private readonly Thread worker;
        private bool refreshRequested;
        private bool stopping;
        private bool configShortcutDown;
        private int configShortcutRequested;
        private bool globalShortcutRegistered;
        private string lastError = string.Empty;

        public PttInputService()
        {
            configHotkey = new GlobalHotkeyService(RequestConfigShortcut);
            globalShortcutRegistered = configHotkey.Registered;
            Console.WriteLine(globalShortcutRegistered
                ? "Config shortcut: Ctrl+Alt+C global hotkey registered."
                : "Config shortcut: global registration unavailable; keyboard polling fallback is active.");
            worker = new Thread(WorkerMain)
            {
                IsBackground = true,
                Name = "VAICOM DirectInput"
            };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            ready.Wait();
        }

        public string LastError
        {
            get
            {
                lock (gate)
                {
                    return lastError;
                }
            }
        }

        public void Refresh()
        {
            lock (gate)
            {
                refreshRequested = true;
            }
        }

        public void SynchronizeEdges()
        {
            lock (gate)
            {
                Array.Copy(keyboardCurrent, keyboardPrevious, keyboardCurrent.Length);
                foreach (ControllerDevice device in devices.Values)
                {
                    device.SynchronizeEdges();
                }

                pressedBindings.Clear();
            }
        }

        public bool IsDown(PttInputBinding binding)
        {
            if (binding == null || binding.Kind == PttInputKind.None)
            {
                return false;
            }

            lock (gate)
            {
                if (binding.Kind == PttInputKind.Keyboard)
                {
                    return binding.VirtualKey >= 0 && binding.VirtualKey < keyboardCurrent.Length &&
                        keyboardCurrent[binding.VirtualKey];
                }

                return TryGetDevice(binding, out ControllerDevice device) && device.IsDown(binding.ButtonIndex);
            }
        }

        public bool IsConnected(PttInputBinding binding)
        {
            if (binding == null || binding.Kind == PttInputKind.None)
            {
                return false;
            }

            if (binding.Kind == PttInputKind.Keyboard)
            {
                return true;
            }

            lock (gate)
            {
                return TryGetDevice(binding, out ControllerDevice device) && device.Connected;
            }
        }

        public bool TryGetPressedBinding(out PttInputBinding binding)
        {
            lock (gate)
            {
                if (pressedBindings.Count == 0)
                {
                    binding = null;
                    return false;
                }

                binding = pressedBindings.Dequeue();
                return true;
            }
        }

        public bool ConsumeConfigShortcutRequest()
        {
            return Interlocked.Exchange(ref configShortcutRequested, 0) != 0;
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (stopping)
                {
                    return;
                }
                stopping = true;
            }

            worker.Join();
            configHotkey.Dispose();
            ready.Dispose();
        }

        private void WorkerMain()
        {
            InputWindow window = null;
            IDirectInput8 directInput = null;
            try
            {
                try
                {
                    window = new InputWindow();
                    directInput = DInput.DirectInput8Create();
                    lock (gate)
                    {
                        RefreshCore(directInput, window.Handle);
                    }
                }
                catch (Exception exception)
                {
                    lock (gate)
                    {
                        lastError = "Controller input is unavailable: " + exception.Message;
                    }
                }
                finally
                {
                    ready.Set();
                }

                while (true)
                {
                    lock (gate)
                    {
                        if (stopping)
                        {
                            break;
                        }

                        if (refreshRequested && directInput != null && window != null)
                        {
                            RefreshCore(directInput, window.Handle);
                            refreshRequested = false;
                        }

                        PollCore();
                    }

                    Thread.Sleep(10);
                }
            }
            finally
            {
                lock (gate)
                {
                    DisposeDevices();
                }

                directInput?.Dispose();
                window?.Dispose();
                if (!ready.IsSet)
                {
                    ready.Set();
                }
            }
        }

        private void RefreshCore(IDirectInput8 directInput, IntPtr windowHandle)
        {
            DisposeDevices();
            pressedBindings.Clear();
            lastError = string.Empty;
            try
            {
                foreach (DeviceInstance info in directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
                {
                    IDirectInputDevice8 device = null;
                    try
                    {
                        device = directInput.CreateDevice(info.InstanceGuid);
                        device.SetCooperativeLevel(
                            windowHandle,
                            CooperativeLevel.Background | CooperativeLevel.NonExclusive).CheckError();
                        device.SetDataFormat<RawJoystickState>().CheckError();
                        Result acquire = device.Acquire();
                        if (acquire.Failure)
                        {
                            lastError = "Could not acquire " + DeviceName(info) + ".";
                            device.Dispose();
                            device = null;
                            continue;
                        }

                        devices[info.InstanceGuid] = new ControllerDevice(info, device);
                        device = null;
                    }
                    catch (Exception exception)
                    {
                        device?.Dispose();
                        lastError = "Could not open " + DeviceName(info) + ": " + exception.Message;
                    }
                }
            }
            catch (Exception exception)
            {
                lastError = "Could not enumerate controller devices: " + exception.Message;
            }

            PollCore();
            SynchronizeEdges();
        }

        private void PollCore()
        {
            foreach (int virtualKey in VirtualKeys.BindableKeys)
            {
                keyboardPrevious[virtualKey] = keyboardCurrent[virtualKey];
                keyboardCurrent[virtualKey] = VirtualKeys.IsDown(virtualKey);
            }

            bool shortcutDown = (VirtualKeys.IsDown(VirtualKeys.LControl) || VirtualKeys.IsDown(VirtualKeys.RControl)) &&
                (VirtualKeys.IsDown(VirtualKeys.LAlt) || VirtualKeys.IsDown(VirtualKeys.RAlt)) &&
                keyboardCurrent[VirtualKeys.C];
            if (!globalShortcutRegistered && shortcutDown && !configShortcutDown)
            {
                Interlocked.Exchange(ref configShortcutRequested, 1);
            }
            configShortcutDown = shortcutDown;

            foreach (ControllerDevice device in devices.Values)
            {
                device.Poll();
            }

            EnqueuePressedBindings();
        }

        private void EnqueuePressedBindings()
        {
            foreach (ControllerDevice device in devices.Values
                .OrderBy(item => IsVirtualDevice(item.Name))
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                foreach (int buttonIndex in device.PressedButtons())
                {
                    Enqueue(new PttInputBinding
                    {
                        Kind = PttInputKind.DirectInputButton,
                        DeviceInstanceGuid = device.InstanceGuid.ToString("D"),
                        DeviceName = device.Name,
                        ButtonIndex = buttonIndex
                    });
                }
            }

            foreach (int virtualKey in VirtualKeys.BindableKeys)
            {
                if (keyboardCurrent[virtualKey] && !keyboardPrevious[virtualKey] &&
                    !(virtualKey == VirtualKeys.C && configShortcutDown))
                {
                    Enqueue(PttInputBinding.Keyboard(virtualKey, VirtualKeys.GetName(virtualKey)));
                }
            }
        }

        private void Enqueue(PttInputBinding binding)
        {
            while (pressedBindings.Count >= 32)
            {
                pressedBindings.Dequeue();
            }

            pressedBindings.Enqueue(binding);
        }

        private void RequestConfigShortcut()
        {
            Interlocked.Exchange(ref configShortcutRequested, 1);
        }

        private bool TryGetDevice(PttInputBinding binding, out ControllerDevice device)
        {
            device = null;
            return Guid.TryParse(binding.DeviceInstanceGuid, out Guid instanceGuid) &&
                devices.TryGetValue(instanceGuid, out device);
        }

        private void DisposeDevices()
        {
            foreach (ControllerDevice device in devices.Values)
            {
                device.Dispose();
            }

            devices.Clear();
        }

        private static string DeviceName(DeviceInstance info)
        {
            return !string.IsNullOrWhiteSpace(info.InstanceName) ? info.InstanceName : info.ProductName;
        }

        private static bool IsVirtualDevice(string name)
        {
            return name != null &&
                (name.IndexOf("vJoy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 name.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private sealed class ControllerDevice : IDisposable
        {
            private readonly IDirectInputDevice8 device;
            private bool[] current = Array.Empty<bool>();
            private bool[] previous = Array.Empty<bool>();

            public ControllerDevice(DeviceInstance info, IDirectInputDevice8 device)
            {
                this.device = device;
                InstanceGuid = info.InstanceGuid;
                Name = DeviceName(info);
            }

            public Guid InstanceGuid { get; }
            public string Name { get; }
            public bool Connected { get; private set; } = true;

            public void Poll()
            {
                if (current.Length > 0)
                {
                    previous = (bool[])current.Clone();
                }

                try
                {
                    Result result = device.Poll();
                    if (result == ResultCode.InputLost)
                    {
                        result = device.Acquire();
                    }

                    if (result == ResultCode.NotAcquired || result.Failure)
                    {
                        Connected = false;
                        current = Array.Empty<bool>();
                        return;
                    }

                    current = device.GetCurrentJoystickState().Buttons ?? Array.Empty<bool>();
                    Connected = true;
                    if (previous.Length != current.Length)
                    {
                        previous = (bool[])current.Clone();
                    }
                }
                catch (SharpGenException)
                {
                    Connected = false;
                    current = Array.Empty<bool>();
                }
                catch (Exception)
                {
                    Connected = false;
                    current = Array.Empty<bool>();
                }
            }

            public bool IsDown(int buttonIndex)
            {
                return Connected && buttonIndex >= 0 && buttonIndex < current.Length && current[buttonIndex];
            }

            public IEnumerable<int> PressedButtons()
            {
                int length = Math.Min(current.Length, previous.Length);
                for (int index = 0; index < length; index++)
                {
                    if (current[index] && !previous[index])
                    {
                        yield return index;
                    }
                }
            }

            public void SynchronizeEdges()
            {
                previous = (bool[])current.Clone();
            }

            public void Dispose()
            {
                try
                {
                    device.Unacquire();
                }
                catch
                {
                }

                device.Dispose();
            }
        }

        private sealed class InputWindow : NativeWindow, IDisposable
        {
            public InputWindow()
            {
                CreateHandle(new CreateParams
                {
                    Caption = "VAICOM Standalone Input",
                    ExStyle = 0x80
                });
            }

            public void Dispose()
            {
                DestroyHandle();
            }
        }

        private sealed class GlobalHotkeyService : IDisposable
        {
            private const int HotkeyId = 0x5641;
            private const uint WmHotkey = 0x0312;
            private const uint WmQuit = 0x0012;
            private const uint ModAlt = 0x0001;
            private const uint ModControl = 0x0002;
            private const uint ModNoRepeat = 0x4000;
            private readonly Action pressed;
            private readonly ManualResetEventSlim ready = new ManualResetEventSlim();
            private readonly Thread thread;
            private uint threadId;

            public GlobalHotkeyService(Action pressed)
            {
                this.pressed = pressed;
                thread = new Thread(Run) { IsBackground = true, Name = "VAICOM Config Hotkey" };
                thread.Start();
                ready.Wait();
            }

            public bool Registered { get; private set; }

            public void Dispose()
            {
                if (threadId != 0) PostThreadMessage(threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
                thread.Join();
                ready.Dispose();
            }

            private void Run()
            {
                threadId = GetCurrentThreadId();
                Registered = RegisterHotKey(IntPtr.Zero, HotkeyId, ModControl | ModAlt | ModNoRepeat, VirtualKeys.C);
                ready.Set();
                try
                {
                    while (GetMessage(out NativeMessage message, IntPtr.Zero, 0, 0) > 0)
                    {
                        if (message.Message == WmHotkey && message.WParam.ToInt32() == HotkeyId) pressed();
                    }
                }
                finally
                {
                    if (Registered) UnregisterHotKey(IntPtr.Zero, HotkeyId);
                }
            }

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            private struct NativeMessage
            {
                public IntPtr Window;
                public uint Message;
                public IntPtr WParam;
                public IntPtr LParam;
                public uint Time;
                public System.Drawing.Point Point;
            }

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, int virtualKey);

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            private static extern bool UnregisterHotKey(IntPtr window, int id);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern int GetMessage(out NativeMessage message, IntPtr window, uint minimum, uint maximum);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

            [System.Runtime.InteropServices.DllImport("kernel32.dll")]
            private static extern uint GetCurrentThreadId();
        }
    }
}
