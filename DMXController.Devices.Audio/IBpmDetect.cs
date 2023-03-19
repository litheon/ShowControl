using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Audio
{
    interface IBpmDetect
    {
        public float GetBpm();
        public void InputSamples(ReadOnlySpan<float> samples, int numSamples);
    }
}
