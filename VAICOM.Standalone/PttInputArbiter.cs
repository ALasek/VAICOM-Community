using System;

namespace VAICOM.Standalone
{
    internal enum PttInputKind
    {
        None,
        Keyboard,
        DirectInputButton
    }

    internal sealed class PttInputBinding
    {
        public PttInputKind Kind { get; set; }
        public int VirtualKey { get; set; }
        public string KeyName { get; set; }
        public string DeviceInstanceGuid { get; set; }
        public string DeviceName { get; set; }
        public int ButtonIndex { get; set; }

        public string DisplayName => Kind == PttInputKind.None
            ? "Not assigned"
            : Kind == PttInputKind.Keyboard
                ? "Keyboard " + KeyName
                : DeviceName + " - Button " + (ButtonIndex + 1);

        public static PttInputBinding None()
        {
            return new PttInputBinding { Kind = PttInputKind.None };
        }

        public static PttInputBinding Keyboard(int virtualKey, string keyName)
        {
            return new PttInputBinding
            {
                Kind = PttInputKind.Keyboard,
                VirtualKey = virtualKey,
                KeyName = keyName
            };
        }

        public PttInputBinding Clone()
        {
            return (PttInputBinding)MemberwiseClone();
        }
    }

    internal sealed class PttBinding
    {
        public PttBinding(int key, string keyName, int tx)
            : this(PttInputBinding.Keyboard(key, keyName), tx)
        {
        }

        public PttBinding(PttInputBinding input, int tx)
        {
            Input = input ?? throw new ArgumentNullException(nameof(input));
            Tx = tx;
        }

        public PttInputBinding Input { get; }
        public string KeyName => Input.Kind == PttInputKind.Keyboard ? Input.KeyName : Input.DisplayName;
        public int Tx { get; }
    }

    internal enum PttTransitionKind
    {
        None,
        Pressed,
        Released
    }

    internal struct PttTransition
    {
        public PttTransition(PttTransitionKind kind, PttBinding binding)
        {
            Kind = kind;
            Binding = binding;
        }

        public PttTransitionKind Kind { get; }
        public PttBinding Binding { get; }
    }

    internal sealed class PttInputArbiter
    {
        private readonly PttBinding[] bindings;
        private readonly bool[] previousDown;
        private int activeIndex = -1;

        public PttInputArbiter(PttBinding[] bindings)
        {
            this.bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            if (bindings.Length == 0)
            {
                throw new ArgumentException("At least one PTT binding is required.", nameof(bindings));
            }

            previousDown = new bool[bindings.Length];
        }

        public void Resync(Func<PttInputBinding, bool> isDown)
        {
            for (int index = 0; index < bindings.Length; index++)
            {
                previousDown[index] = isDown(bindings[index].Input);
            }
        }

        public PttTransition Poll(Func<PttInputBinding, bool> isDown)
        {
            var currentDown = new bool[bindings.Length];
            for (int index = 0; index < bindings.Length; index++)
            {
                currentDown[index] = isDown(bindings[index].Input);
            }

            int transitionIndex = -1;
            PttTransitionKind kind = PttTransitionKind.None;
            if (activeIndex >= 0)
            {
                if (!currentDown[activeIndex] && previousDown[activeIndex])
                {
                    transitionIndex = activeIndex;
                    activeIndex = -1;
                    kind = PttTransitionKind.Released;
                }
            }
            else
            {
                for (int index = 0; index < bindings.Length; index++)
                {
                    if (currentDown[index] && !previousDown[index])
                    {
                        transitionIndex = index;
                        activeIndex = index;
                        kind = PttTransitionKind.Pressed;
                        break;
                    }
                }
            }

            Array.Copy(currentDown, previousDown, currentDown.Length);
            return transitionIndex >= 0
                ? new PttTransition(kind, bindings[transitionIndex])
                : new PttTransition(PttTransitionKind.None, null);
        }
    }
}
