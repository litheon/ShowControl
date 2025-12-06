using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Text;

namespace ShowControl.Devices.Audio
{
    public class AudioFrameProcessedEventArgs : EventArgs, IDisposable
    {
        public AudioSpectrum AudioSpectrum { get; private set; }
        public double WidthPerBand { get; private set; }
        public float Bpm { get; private set; }
        public double AverageLevel { get; private set; }
        public double CurrentLevel { get; private set; }
        public double AmplitudeRatio { get; private set; }
        public double Seconds { get; private set; }
        private bool _disposed;
        public AudioFrameProcessedEventArgs(AudioSpectrum audioSpectrum, double widthPerBand, float bpm, double averageLevel, double currentLevel, double amplitudeRatio, double seconds)
        {
            AudioSpectrum = audioSpectrum;
            WidthPerBand = widthPerBand;
            Bpm = bpm;
            AverageLevel = averageLevel;
            CurrentLevel = currentLevel;
            AmplitudeRatio = amplitudeRatio;
            Seconds = seconds;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                AudioSpectrum.Dispose();
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
