using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Audio
{
    public class RealTimeBpmDetect : IBpmDetect
    {
        private SoundTouch.BpmDetect _realBpmDetect;
        public int _channelCount;
        public int _sampleRate;

        public RealTimeBpmDetect(int channelCount, int sampleRate)
        {
            _realBpmDetect = new SoundTouch.BpmDetect(channelCount, sampleRate);
        }

        public void InputSamples(ReadOnlySpan<float> samples, int numSamples)
        {
            _realBpmDetect.InputSamples(samples, numSamples);
        }

        public float GetBpm()
        {
            return _realBpmDetect.GetBpm();
        }
    }
}
