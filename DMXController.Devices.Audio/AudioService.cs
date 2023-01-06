using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
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
        private const int ChannelCount = 2;

        private const int SampleLevelWindowSeconds = 30;
        private const int SampleQueueLength = SampleLevelWindowSeconds * SampleLevelWindowSeconds;

        private const double BassCutOffHz = 250.0d;
        private const double MidRangeCutOffHz = 5000.0d;
        private readonly SampleAverageLevelCalculator _bassAverageLevel;
        private readonly SampleAverageLevelCalculator _bassCurrentLevel;

        private static float[] _samples1;
        private static float[] _samples2;
        private static float[] _samples3;
        private static float[] _samples4;
        private static float[] _activeSamples;

        private int _copyBufferIndex;
        private readonly Point[][] _copyBuffer = new Point[4][];

        public const int FftLength = 4096;
        private int _fftPos;
        private readonly int _m = (int)Math.Log(FftLength, 2.0);
        private readonly Complex[] _fftBuffer = new Complex[FftLength];
        private readonly Complex[] _activeBuffer;

        private readonly BpmDetect _bpmDetect;

        private readonly object _disposalLock = new object();
        private bool _stopped;

        public AudioService()
        {
            _bpmDetect = new BpmDetect(ChannelCount, SampleRate);
            _bassAverageLevel = new SampleAverageLevelCalculator(0);
            _bassCurrentLevel = new SampleAverageLevelCalculator(Convert.ToInt32(Math.Round(BassCutOffHz / (SampleRate / FftLength))) - 1);
            _activeBuffer = _fftBuffer;

            for (int i = 0; i < _copyBuffer.Length; i++)
            {
                _copyBuffer[i] = new Point[FftLength];
            }

            var drivers = AsioOut.GetDriverNames();
            var driver = drivers.FirstOrDefault(d => d.Contains("Focusrite USB"));
            if (driver != null)
            {
                _asioOut = new AsioOut(driver)
                {
                    InputChannelOffset = 2
                    //InputChannelOffset = 8
                };
                _asioOut.InitRecordAndPlayback(null, ChannelCount, SampleRate);
                _asioOut.AudioAvailable += AsioOut_AudioAvailable;
                _asioOut.Play();
            }
        }

        public AudioService(EventHandler<AudioFrameProcessedEventArgs> audioFrameProcessedEventHandler) : this()
        {
            AudioFrameProcessed += audioFrameProcessedEventHandler;
        }

        private void AsioOut_AudioAvailable(object sender, AsioAudioAvailableEventArgs e)
        {
            if (_activeSamples == null || _activeSamples.Length != e.SamplesPerBuffer * ChannelCount)
            {
                _samples1 = new float[e.SamplesPerBuffer * 2];
                _samples2 = new float[e.SamplesPerBuffer * 2];
                _samples3 = new float[e.SamplesPerBuffer * 2];
                _samples4 = new float[e.SamplesPerBuffer * 2];
                _activeSamples = _samples1;
            }
            e.GetAsInterleavedSamples(_activeSamples);
            for (int i = 0; i < _activeSamples.Length / ChannelCount; i++)
            {
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
            double seconds = 1 / width;
            double Hz = 0;
            int copyBufferIndex = _copyBufferIndex.InterlockedGet();
            for (int j = 0; j < _activeBuffer.Length; j++)
            {
                _copyBuffer[copyBufferIndex][j].X = Hz == 0 ? Math.Log10(5) / Math.Log10(20000) : Math.Log10(Hz) / Math.Log10(20000);

                var level = Math.Abs(Math.Sqrt(Math.Pow(_activeBuffer[j].Y, 2) + Math.Pow(_activeBuffer[j].X, 2)));
                CalculateAverageLevel(Hz, level, seconds);

                _copyBuffer[copyBufferIndex][j].Y = 20 * Math.Log10(level);
                Hz += width;
            }
            var bpm = _bpmDetect.GetBpm();
            var audioFrameProcessedEventArgs = new AudioFrameProcessedEventArgs(_copyBuffer[copyBufferIndex], width, bpm, _bassAverageLevel.SampleAverage, _bassCurrentLevel.SampleAverage);
            Task.Run(() => AudioFrameProcessed?.Invoke(this, audioFrameProcessedEventArgs));
            _copyBufferIndex.InterlockedIncrement(0, _copyBuffer.Length - 1);
        }

        private void CalculateAverageLevel(double Hz, double level, double seconds)
        {
            if (Hz > 0 && Hz <= BassCutOffHz)
            {
                var windowSampleCount = SampleLevelWindowSeconds / (seconds * BassCutOffHz / (1 / seconds));
                _bassAverageLevel.SampleWindowSize = Convert.ToInt32(Math.Round(windowSampleCount));
                _bassAverageLevel.Calculate(level);
                _bassCurrentLevel.Calculate(level);
            }
        }

        public void Stop()
        {
            lock(_disposalLock)
            {
                if (!_stopped)
                {
                    _asioOut.Stop();
                    _asioOut.Dispose();
                    _stopped = true;
                }
            }
        }
        public void Dispose()
        {
            Stop();
        }
    }
}
