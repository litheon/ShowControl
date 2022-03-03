using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace ShowControl.Devices.Audio
{
    public class AudioFrameProcessedEventArgs : EventArgs
    {
        public Point[] AudioSpectrum { get; private set; }
        public double WidthPerBand { get; private set; }
        public float Bpm { get; private set; }
        public AudioFrameProcessedEventArgs(Point[] audioSpectrum, double widthPerBand, float bpm)
        {
            AudioSpectrum = audioSpectrum;
            WidthPerBand = widthPerBand;
            Bpm = bpm;
        }
    }
}
