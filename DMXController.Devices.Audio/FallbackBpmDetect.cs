using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Audio
{
    public class FallbackBpmDetect : IBpmDetect
    {
        public float GetBpm()
        {
            return 60;
        }

        public void InputSamples(ReadOnlySpan<float> samples, int numSamples) { }
    }
}
