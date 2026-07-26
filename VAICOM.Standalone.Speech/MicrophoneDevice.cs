using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace VAICOM.Standalone.Speech
{
    public sealed class MicrophoneDevice
    {
        internal MicrophoneDevice(int deviceNumber, string name)
        {
            DeviceNumber = deviceNumber;
            Name = name;
        }

        public int DeviceNumber { get; }
        public string Name { get; }
    }

    public static class MicrophoneDevices
    {
        public static IReadOnlyList<MicrophoneDevice> Enumerate()
        {
            var devices = new List<MicrophoneDevice>(WaveIn.DeviceCount);
            for (var deviceNumber = 0; deviceNumber < WaveIn.DeviceCount; deviceNumber++)
            {
                devices.Add(new MicrophoneDevice(deviceNumber, WaveIn.GetCapabilities(deviceNumber).ProductName));
            }

            return devices;
        }
    }
}
