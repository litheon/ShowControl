using NAudio.Dsp;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Audio
{
    public interface IAudioService : IDisposable
    {
        public event EventHandler<AudioFrameProcessedEventArgs> AudioFrameProcessed;

        /// <summary>The capture devices available to select from. Snapshot; safe from any
        /// thread. Empty when capture is disabled or the audio stack failed to come up.</summary>
        IReadOnlyList<AudioInputDevice> GetInputDevices();

        /// <summary>PortAudio index of the device currently capturing, or -1 when none is.</summary>
        int ActiveInputDeviceIndex { get; }

        /// <summary>Switches capture to the given device. Returns false (and keeps or
        /// restores the previous device where possible) on failure — selecting a device
        /// must never take the audio stack down, let alone the app.</summary>
        bool SelectInputDevice(int deviceIndex);
    }
}
