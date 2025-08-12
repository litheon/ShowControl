using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace ShowControl.Devices.Audio
{
    public class AudioFrameProcessedEventArgs : EventArgs
    {
        // Potentially use ArrayPool and instead of point return an object that's IDisposable with a property of the points
        public Point[] AudioSpectrum { get; private set; }
        public double WidthPerBand { get; private set; }
        public float Bpm { get; private set; }
        public double AverageLevel { get; private set; }
        public double CurrentLevel { get; private set; }
        public double AmplitudeRatio { get; private set; }

        public double Seconds { get; private set; }
        public AudioFrameProcessedEventArgs(Point[] audioSpectrum, double widthPerBand, float bpm, double averageLevel, double currentLevel, double amplitudeRatio, double seconds)
        {
            AudioSpectrum = audioSpectrum;
            WidthPerBand = widthPerBand;
            Bpm = bpm;
            AverageLevel = averageLevel;
            CurrentLevel = currentLevel;
            AmplitudeRatio = amplitudeRatio;
            Seconds = seconds;
        }
    }
}
