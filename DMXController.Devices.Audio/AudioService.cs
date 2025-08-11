using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using PortAudioSharp;
using ShowControl.Utilities;

namespace ShowControl.Devices.Audio
{
    public class AudioService : IAudioService
    {
        public event EventHandler<AudioFrameProcessedEventArgs> AudioFrameProcessed;

        private readonly AsioOut _asioOut;
        public const int SampleRate = 192000;

        private const int ChannelOffset = 2;
        private const int ChannelCount = 2;

        private const int SampleLevelWindowSeconds = 20;
        private const int SampleQueueLength = SampleLevelWindowSeconds * SampleLevelWindowSeconds;

        private const double BassCutOffHz = 250.0d;
        private const double MidRangeCutOffHz = 5000.0d;
        private readonly SampleAverageLevelCalculator _bassAverageLevel;
        private readonly SampleAverageLevelCalculator _bassCurrentLevel;
        private readonly SampleAverageLevelCalculator _overallAverageLevel;
        private readonly SampleAverageLevelCalculator _overallAverageLevelAverage;

        private static ArrayPool<float> _audioPool = ArrayPool<float>.Shared;
        private static ArrayPool<Complex> _fftPool = ArrayPool<Complex>.Shared;
        private static float[] _activeSamples;

        private int _copyBufferIndex;
        private readonly Point[][] _copyBuffer = new Point[4][];

        public const int FftLength = 4096;
        private int _fftPos;
        private readonly int _m = (int)Math.Log(FftLength, 2.0);
        private readonly Complex[] _activeBuffer = new Complex[FftLength];

        private readonly IBpmDetect _bpmDetect;

        private record AudioProcessSignal(float[] sharedArray, int frameCount);
        private readonly object _disposalLock = new object();
        private bool _stopped;
        private Timer t;
        private Stream _stream;
        private Thread _processingThread;
        ConcurrentQueue<AudioProcessSignal> _signalQueue;
        public AudioService()
        {
            t = new Timer(callback);
            t.Change(4000, Timeout.Infinite);
            PortAudio.Initialize();
            int deviceIndex = 0;
            for (int i = 0; i < PortAudio.DeviceCount; i++)
            {
                var inputDevice = PortAudio.GetDeviceInfo(i);
                if (inputDevice.name.Contains("Focusrite USB ASIO") || inputDevice.name.Contains("Scarlett"))
                {
                    deviceIndex = i;
                    break;
                }
            }
            var inputParameters = new StreamParameters
            {
                device = deviceIndex,
                channelCount = ChannelOffset + ChannelCount,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = PortAudio.GetDeviceInfo(deviceIndex).defaultLowInputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero,
            };
            for (int i = 0; i < _copyBuffer.Length; i++)
            {
                _copyBuffer[i] = new Point[FftLength];
            }
            _bpmDetect = new RealTimeBpmDetect(ChannelCount, SampleRate);
            //_bpmDetect = new FallbackBpmDetect();
            _bassAverageLevel = new SampleAverageLevelCalculator(0);
            _bassCurrentLevel = new SampleAverageLevelCalculator(Convert.ToInt32(Math.Round(BassCutOffHz / (SampleRate / FftLength))) - 1);
            _overallAverageLevel = new SampleAverageLevelCalculator(0);
            _overallAverageLevelAverage = new SampleAverageLevelCalculator(0);
            _stream = new PortAudioSharp.Stream(inputParameters, null, SampleRate, 0, StreamFlags.NoFlag, new Stream.Callback(Callback), null);
            _activeSamples = new float[FftLength * ChannelCount];
            _processingThread = new Thread(ProcessingThreadStart);
            _signalQueue = new ConcurrentQueue<AudioProcessSignal>();
        }

        private void StreamFinishedCallback(IntPtr userData)
        {
            int x = 0;
        }
        private void callback(object? v)
        {
            _stream.SetFinishedCallback(StreamFinishedCallback);
            _processingThread.Start();
            _stream.Start();
            int x = 0;
        }

        public AudioService(EventHandler<AudioFrameProcessedEventArgs> audioFrameProcessedEventHandler) : this()
        {
            AudioFrameProcessed += audioFrameProcessedEventHandler;
        }

        private StreamCallbackResult Callback(IntPtr input, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo e, StreamCallbackFlags f, IntPtr d)
        {
            var bufferArray = _audioPool.Rent(Convert.ToInt32(frameCount) * (ChannelCount + ChannelOffset));
            Marshal.Copy(input, bufferArray, 0, (int)frameCount * (ChannelCount + ChannelOffset));
            _signalQueue.Enqueue(new AudioProcessSignal(bufferArray, Convert.ToInt32(frameCount)));
            return StreamCallbackResult.Continue;
        }

        private void ProcessingThreadStart()
        {
            while (true)
            {
                if (_signalQueue.TryDequeue(out var processSigntal))
                {
                    DoProcessing(processSigntal.frameCount, processSigntal.sharedArray);
                    _audioPool.Return(processSigntal.sharedArray);
                }
            }
        }
        private void DoProcessing(int frameCount, float[] array)
        {
            for (int i = 0; i < frameCount; i++)
            {
                var sample = (array[i * (ChannelCount + ChannelOffset) + ChannelOffset] + array[i * (ChannelCount + ChannelOffset) + ChannelOffset + 1]) / 2;
                _activeBuffer[_fftPos].X = (float)(sample * FastFourierTransform.HammingWindow(_fftPos, _activeBuffer.Length));
                _activeBuffer[_fftPos].Y = 0;
                _fftPos++;
                if (_fftPos >= _activeBuffer.Length)
                {
                    FftProcessed();
                }
            }
            Span<float> channelArray = stackalloc float[Convert.ToInt32(frameCount * ChannelCount)];
            for (int i = 0; i < frameCount; i++)
            {
                channelArray[i * ChannelOffset] = array[i * (ChannelCount + ChannelOffset) + ChannelOffset];
                channelArray[i * ChannelOffset + 1] = array[i * (ChannelCount + ChannelOffset) + ChannelOffset + 1];
            }
            _bpmDetect.InputSamples(channelArray, Convert.ToInt32(frameCount));
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
            var currentLevelDb = 20 * Math.Log10(_bassCurrentLevel.SampleAverage);
            var averageLevelDb = 20 * Math.Log10(_bassAverageLevel.SampleAverage);
            var amplitudeRatio = _overallAverageLevel.SampleAverage / _overallAverageLevelAverage.SampleAverage;

            var audioFrameProcessedEventArgs = new AudioFrameProcessedEventArgs(_copyBuffer[copyBufferIndex], width, bpm, averageLevelDb, currentLevelDb, amplitudeRatio, seconds);
            Task.Run(() => AudioFrameProcessed?.Invoke(this, audioFrameProcessedEventArgs));
            _copyBufferIndex.InterlockedIncrement(0, _copyBuffer.Length - 1);
        }

        private void CalculateAverageLevel(double Hz, double level, double seconds)
        {
            var windowSampleCount = SampleLevelWindowSeconds / seconds;
            if (Hz > 0 && Hz <= BassCutOffHz)
            {
                _bassAverageLevel.SampleWindowSize = Convert.ToInt32(Math.Round(windowSampleCount));
                _bassAverageLevel.Calculate(level);
                _bassCurrentLevel.Calculate(level);
            }
            if (Hz == 0)
            {
                _overallAverageLevel.SampleWindowSize = Convert.ToInt32(Math.Round(windowSampleCount));
                _overallAverageLevelAverage.SampleWindowSize = Convert.ToInt32(Math.Round(windowSampleCount));
                _overallAverageLevel.Calculate(level);
                _overallAverageLevelAverage.Calculate(_overallAverageLevel.SampleAverage);
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
