using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Audio
{
    public interface IAudioService
    {
        public event EventHandler OnFftCalculated;
    }
}
