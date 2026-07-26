using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace VAICOM.Standalone
{
    internal static class VirtualKeys
    {
        public const int F13 = 0x7C;
        public const int C = 0x43;
        public const int LControl = 0xA2;
        public const int LAlt = 0xA4;
        public const int RControl = 0xA3;
        public const int LShift = 0xA0;
        public const int RShift = 0xA1;
        public const int RAlt = 0xA5;

        public static int[] BindableKeys { get; } = CreateBindableKeys();

        public static int Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A PTT key is required.");
            }

            string key = value.Trim().ToUpperInvariant();
            if (key.StartsWith("0X", StringComparison.Ordinal) &&
                int.TryParse(key.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex) &&
                IsBindable(hex))
            {
                return hex;
            }

            if (key.Length == 1 && ((key[0] >= 'A' && key[0] <= 'Z') || (key[0] >= '0' && key[0] <= '9')))
            {
                return key[0];
            }

            if (key.StartsWith("F", StringComparison.Ordinal) &&
                int.TryParse(key.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int function) &&
                function >= 1 && function <= 24)
            {
                return 0x70 + function - 1;
            }

            switch (key)
            {
                case "SPACE": return 0x20;
                case "TAB": return 0x09;
                case "CAPSLOCK": return 0x14;
                case "LCONTROL": return 0xA2;
                case "RCONTROL": return 0xA3;
                case "LSHIFT": return 0xA0;
                case "RSHIFT": return 0xA1;
                case "LALT": return 0xA4;
                case "RALT": return 0xA5;
                case "INSERT": return 0x2D;
                case "DELETE": return 0x2E;
                case "HOME": return 0x24;
                case "END": return 0x23;
                case "PAGEUP": return 0x21;
                case "PAGEDOWN": return 0x22;
                default: throw new ArgumentException("Unsupported PTT key: " + value);
            }
        }

        public static bool IsDown(int key)
        {
            return (GetAsyncKeyState(key) & 0x8000) != 0;
        }

        public static bool IsBindable(int key)
        {
            return Array.IndexOf(BindableKeys, key) >= 0;
        }

        public static string GetName(int key)
        {
            if ((key >= 'A' && key <= 'Z') || (key >= '0' && key <= '9'))
            {
                return ((char)key).ToString();
            }

            if (key >= 0x70 && key <= 0x87)
            {
                return "F" + (key - 0x70 + 1).ToString(CultureInfo.InvariantCulture);
            }

            switch (key)
            {
                case 0x20: return "SPACE";
                case 0x09: return "TAB";
                case 0x14: return "CAPSLOCK";
                case 0xA2: return "LCONTROL";
                case 0xA3: return "RCONTROL";
                case 0xA0: return "LSHIFT";
                case 0xA1: return "RSHIFT";
                case 0xA4: return "LALT";
                case 0xA5: return "RALT";
                case 0x2D: return "INSERT";
                case 0x2E: return "DELETE";
                case 0x24: return "HOME";
                case 0x23: return "END";
                case 0x21: return "PAGEUP";
                case 0x22: return "PAGEDOWN";
                default: return "0x" + key.ToString("X2", CultureInfo.InvariantCulture);
            }
        }

        private static int[] CreateBindableKeys()
        {
            var keys = new int[24 + 26 + 10 + 9 + 6];
            int index = 0;
            for (int key = 0x70; key <= 0x87; key++) keys[index++] = key;
            for (int key = 'A'; key <= 'Z'; key++) keys[index++] = key;
            for (int key = '0'; key <= '9'; key++) keys[index++] = key;
            foreach (int key in new[] { 0x20, 0x09, 0x14, 0x2D, 0x2E, 0x24, 0x23, 0x21, 0x22 }) keys[index++] = key;
            foreach (int key in new[] { LControl, RControl, LShift, RShift, LAlt, RAlt }) keys[index++] = key;
            return keys;
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);
    }
}
