using System;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace ShowControl.Devices.Audio
{
    public class AudioService : IAudioService
    {
        public event EventHandler OnFftCalculated;

        private AsioOut _asioOut;
        private static float[] _samples = new float[512];
        private const int _fftLength = 1024;
        private Complex[] _fftBuffer = new Complex[_fftLength];
        private int _fftPos;
        private int _m = (int)Math.Log(1024, 2.0);
        public AudioService()
        {
            var drivers = AsioOut.GetDriverNames();
            var driver = drivers.FirstOrDefault(d => d.Contains("Focusrite USB"));
            if (driver != null)
            {
                _asioOut = new AsioOut(driver);
                _asioOut.InputChannelOffset = 8;
                var inputChannelCount = 2;
                var sampleRate = 48000;
                _asioOut.InitRecordAndPlayback(null, inputChannelCount, sampleRate);
                _asioOut.AudioAvailable += AsioOut_AudioAvailable;
                _asioOut.Play();
            }
        }



        private void AsioOut_AudioAvailable(object sender, AsioAudioAvailableEventArgs e)
        {
            e.GetAsInterleavedSamples(_samples);
            foreach (var sample in _samples)
            {
                _fftBuffer[_fftPos].X = (float)(sample * FastFourierTransform.HammingWindow(_fftPos, _fftBuffer.Length));
                _fftBuffer[_fftPos].Y = 0;
                _fftPos++;
                if (_fftPos >= _fftBuffer.Length)
                {
                    _fftPos = 0;
                    // 1024 = 2^10
                    FastFourierTransform.FFT(true, _m, _fftBuffer);
                    int x = 0;
                }
            }
        }
    }
}
