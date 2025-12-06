using System;
using System.Collections.Generic;
using System.Text;

namespace ShowControl.Devices.Audio
{
    public class SampleAverageLevelCalculator
    {
        private Queue<double> _sampleQueue;
        private double _sampleSum;

        private int _sampleWindowSize;
        public int SampleWindowSize
        {
            get
            {
                return _sampleWindowSize;
            }
            set
            {
                if (_sampleWindowSize != value)
                {
                    _sampleWindowSize = value;
                    _sampleQueue.Clear();
                    _sampleSum = 0;
                }
            }
        }

        public double SampleAverage
        {
            get
            {
                if (_sampleQueue.Count > 0)
                {
                    return _sampleSum / _sampleQueue.Count;
                }
                else
                {
                    return -0d;
                }
            }
        }

        public SampleAverageLevelCalculator(int sampleWindowSize)
        {
            _sampleQueue = new Queue<double>(sampleWindowSize);
            SampleWindowSize = sampleWindowSize;
        }

        public void Calculate(double sample)
        {
            while (_sampleQueue.Count >= _sampleWindowSize && _sampleQueue.Count > 0)
            {
                _sampleSum -= _sampleQueue.Dequeue();
            }
            _sampleQueue.Enqueue(sample);
            _sampleSum += sample;
        }
    }
}
