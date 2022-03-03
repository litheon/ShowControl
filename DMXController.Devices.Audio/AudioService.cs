using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using ShowControl.Utilities;
using SoundTouch;

namespace ShowControl.Devices.Audio
{
    public class AudioService : IAudioService
    {
        public event EventHandler<AudioFrameProcessedEventArgs> AudioFrameProcessed;

        private readonly AsioOut _asioOut;
        public const int SampleRate = 192000;
        private const int channelCount = 2;
        private static float[] _samples1;
        private static float[] _samples2;
        private static float[] _samples3;
        private static float[] _samples4;
        private static float[] _activeSamples;
        public const int FftLength = 4096;
        private readonly Complex[] _fftBuffer1 = new Complex[FftLength];
        private readonly Complex[] _activeBuffer;
        int _copyBufferIndex;
        private readonly Point[][] _copyBuffer = new Point[4][];
        private int _fftPos;
        private readonly int _m = (int)Math.Log(FftLength, 2.0);
        private readonly BpmDetect _bpmDetect;


        public AudioService()
        {
            _bpmDetect = new BpmDetect(channelCount, SampleRate);
            _activeBuffer = _fftBuffer1;
            var drivers = AsioOut.GetDriverNames();
            var driver = drivers.FirstOrDefault(d => d.Contains("Focusrite USB"));
            if (driver != null)
            {
                _asioOut = new AsioOut(driver)
                {
                    InputChannelOffset = 2
                };
                var sampleRate = AudioService.SampleRate;
                _asioOut.InitRecordAndPlayback(null, channelCount, sampleRate);
                _asioOut.AudioAvailable += AsioOut_AudioAvailable;
                _asioOut.Play();
            }

            for (int i = 0; i < _copyBuffer.Length; i++)
            {
                _copyBuffer[i] = new Point[FftLength];
            }
        }

        public AudioService(EventHandler<AudioFrameProcessedEventArgs> audioFrameProcessedEventHandler) : this()
        {
            AudioFrameProcessed += audioFrameProcessedEventHandler;
        }

        private void AsioOut_AudioAvailable(object sender, AsioAudioAvailableEventArgs e)
        {
            if (_activeSamples == null || _activeSamples.Length != e.SamplesPerBuffer * channelCount)
            {
                _samples1 = new float[e.SamplesPerBuffer * 2];
                _samples2 = new float[e.SamplesPerBuffer * 2];
                _samples3 = new float[e.SamplesPerBuffer * 2];
                _samples4 = new float[e.SamplesPerBuffer * 2];
                _activeSamples = _samples1;
            }
            e.GetAsInterleavedSamples(_activeSamples);
            for (int i = 0; i < _activeSamples.Length / channelCount; i++)
            {
                // This is all really hacky
                var sample = (_activeSamples[i] + _activeSamples[e.SamplesPerBuffer + i]) / 2;
                _activeBuffer[_fftPos].X = (float)(sample * FastFourierTransform.HammingWindow(_fftPos, _activeBuffer.Length));
                _activeBuffer[_fftPos].Y = 0;
                _fftPos++;
                if (_fftPos >= _activeBuffer.Length)
                {
                    FftProcessed();
                }
            }
            if (_activeSamples == _samples1)
            {
                _activeSamples = _samples2;
            }
            else if (_activeSamples == _samples2)
            {
                _activeSamples = _samples3;
            }
            else if (_activeSamples == _samples3)
            {
                _activeSamples = _samples4;
            }
            else
            {
                _activeSamples = _samples1;
            }
            _bpmDetect.InputSamples(_activeSamples.AsSpan(), e.SamplesPerBuffer);
        }

        private void FftProcessed()
        {
            _fftPos = 0;
            FastFourierTransform.FFT(true, _m, _activeBuffer);
            double width = SampleRate / _activeBuffer.Length;
            double Hz = 0;
            int copyBufferIndex = _copyBufferIndex.InterlockedGet();
            for (int j = 0; j < _activeBuffer.Length; j++)
            {
                _copyBuffer[copyBufferIndex][j].X = Hz == 0 ? Math.Log10(5) / Math.Log10(20000) : Math.Log10(Hz) / Math.Log10(20000);
                _copyBuffer[copyBufferIndex][j].Y = 20 * Math.Log10(Math.Abs(Math.Sqrt(Math.Pow(_activeBuffer[j].Y, 2) + Math.Pow(_activeBuffer[j].X, 2))));
                Hz += width;
            }
            var bpm = _bpmDetect.GetBpm();
            var audioFrameProcessedEventArgs = new AudioFrameProcessedEventArgs(_copyBuffer[copyBufferIndex], width, bpm);
            Task.Run(() => AudioFrameProcessed?.Invoke(this, audioFrameProcessedEventArgs));
            _copyBufferIndex.InterlockedIncrement(0, _copyBuffer.Length - 1);
        }
    }
}
