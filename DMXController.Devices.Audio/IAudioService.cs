using NAudio.Dsp;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Audio
{
    public interface IAudioService : IDisposable
    {
        public event EventHandler<AudioFrameProcessedEventArgs> AudioFrameProcessed;
    }
}
