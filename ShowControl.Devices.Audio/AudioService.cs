using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NAudio.Dsp;
using PortAudioSharp;
using ShowControl.Utilities;

namespace ShowControl.Devices.Audio
{
    public class AudioService : IAudioService
    {
        public event EventHandler<AudioFrameProcessedEventArgs> AudioFrameProcessed;

        public const int SampleRate = 44100;
        private const int ChannelOffset = 0;
        private const int ChannelCount = 1;

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
        private readonly ArrayPool<Point> _pointPool = ArrayPool<Point>.Shared;
        private static float[] _activeSamples;

        public const int FftLength = 4096;
        private int _fftPos;
        private readonly int _m = (int)Math.Log(FftLength, 2.0);
        private readonly Complex[] _fftBuffer = new Complex[FftLength];

        private readonly IBpmDetect _bpmDetect;
        private readonly ILogger<AudioService> _logger;

        private record AudioProcessSignal(float[] sharedArray, int frameCount);
        private readonly object _disposalLock = new object();
        private bool _stopped;
        private Stream _stream;
        private Thread _audioProcessingThread;
        private Thread _audioDispatchThread;
        private ManualResetEvent _signalQueueChangeEvent = new ManualResetEvent(false);
        ConcurrentQueue<AudioProcessSignal> _signalQueue;
        private ManualResetEvent _audioQueueChangeEvent = new ManualResetEvent(false);
        ConcurrentQueue<AudioFrameProcessedEventArgs> _audioFrameEventArgsQueue;
        private CancellationTokenSource _cancellationTokenSource;
        public AudioService(ILogger<AudioService> logger)
        {
            _logger = logger;
            _cancellationTokenSource = new CancellationTokenSource();
            _bpmDetect = new RealTimeBpmDetect(ChannelCount, SampleRate);
            _bassAverageLevel = new SampleAverageLevelCalculator(0);
            _bassCurrentLevel = new SampleAverageLevelCalculator(Convert.ToInt32(Math.Round(BassCutOffHz / (SampleRate / FftLength))) - 1);
            _overallAverageLevel = new SampleAverageLevelCalculator(0);
            _overallAverageLevelAverage = new SampleAverageLevelCalculator(0);
            _activeSamples = new float[FftLength * ChannelCount];
            _audioProcessingThread = new Thread(ProcessingThreadStart);
            _audioDispatchThread = new Thread(DispatchThreadStart);
            _signalQueue = new ConcurrentQueue<AudioProcessSignal>();
            _audioFrameEventArgsQueue = new ConcurrentQueue<AudioFrameProcessedEventArgs>();
            PortAudio.Initialize();
            int deviceIndex = -1;
            for (int i = 0; i < PortAudio.DeviceCount; i++)
            {
                var inputDevice = PortAudio.GetDeviceInfo(i);
                if (inputDevice.name.Contains("Focusrite USB ASIO") || inputDevice.name.Contains("Scarlett") || inputDevice.name == "Universal Audio Thunderbolt")
                {
                    deviceIndex = i;
                    break;
                }
            }
            if (deviceIndex == -1)
            {
                for (int i = 0; i < PortAudio.DeviceCount; i++)
                {
                    var inputDevice = PortAudio.GetDeviceInfo(i);
                    if (inputDevice.name.Contains("Microphone") && !inputDevice.name.Contains("iPhone"))
                    {
                        deviceIndex = i;
                        break;
                    }
                }
            }
            // var inputParameters = new StreamParameters
            // {
            //     device = deviceIndex,
            //     channelCount = ChannelOffset + ChannelCount,
            //     sampleFormat = SampleFormat.Float32,
            //     suggestedLatency = PortAudio.GetDeviceInfo(deviceIndex).defaultLowInputLatency,
            //     hostApiSpecificStreamInfo = IntPtr.Zero,
            // };
            // _stream = new PortAudioSharp.Stream(inputParameters, null, SampleRate, 0, StreamFlags.NoFlag, new Stream.Callback(Callback), null);
            // _stream.SetFinishedCallback(StreamFinishedCallback);
            // _audioDispatchThread.Start();
            // _audioProcessingThread.Start();
            // _stream.Start();
        }

        private void StreamFinishedCallback(IntPtr userData)
        {
            _logger.LogInformation("Audio stream finished, restarting.");
            _stream.Stop();
            _stream.Start();
        }

        public AudioService(ILogger<AudioService> logger, EventHandler<AudioFrameProcessedEventArgs> audioFrameProcessedEventHandler) : this(logger)
        {
            AudioFrameProcessed += audioFrameProcessedEventHandler;
        }

        private StreamCallbackResult Callback(IntPtr input, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo e, StreamCallbackFlags f, IntPtr d)
        {
            try
            {
                var bufferArray = _audioPool.Rent(Convert.ToInt32(frameCount) * (ChannelCount + ChannelOffset));
                Marshal.Copy(input, bufferArray, 0, (int)frameCount * (ChannelCount + ChannelOffset));
                _signalQueue.Enqueue(new AudioProcessSignal(bufferArray, Convert.ToInt32(frameCount)));
                _signalQueueChangeEvent.Set();
                return StreamCallbackResult.Continue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in audio callback, aborting stream.");
                return StreamCallbackResult.Abort;
            }
        }

        private void ProcessingThreadStart()
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                _signalQueueChangeEvent.WaitOne();
                if (_signalQueue.TryDequeue(out var processSignal))
                {
                    DoProcessing(processSignal.frameCount, processSignal.sharedArray);
                    _audioPool.Return(processSignal.sharedArray);
                }
                if (_signalQueue.Count == 0)
                {
                    _signalQueueChangeEvent.Reset();
                }
            }
        }

        private void DispatchThreadStart()
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                _audioQueueChangeEvent.WaitOne();
                if (_audioFrameEventArgsQueue.TryDequeue(out var eventArgs))
                {
                    AudioFrameProcessed?.Invoke(this, eventArgs);
                }
                if (_audioFrameEventArgsQueue.Count == 0)
                {
                    _audioQueueChangeEvent.Reset();
                }
            }
        }

        private void DoProcessing(int frameCount, float[] samples)
        {
            for (int i = 0; i < frameCount; i++)
            {
                var sample = (samples[i * (ChannelCount + ChannelOffset) + ChannelOffset] + samples[i * (ChannelCount + ChannelOffset) + ChannelOffset + 1]) / 2;
                _fftBuffer[_fftPos].X = (float)(sample * FastFourierTransform.HammingWindow(_fftPos, _fftBuffer.Length));
                _fftBuffer[_fftPos].Y = 0;
                _fftPos++;
                if (_fftPos >= _fftBuffer.Length)
                {
                    FftProcessed();
                }
            }
            Span<float> channelArray = stackalloc float[Convert.ToInt32(frameCount * ChannelCount)];
            for (int i = 0; i < frameCount; i++)
            {
                channelArray[i * ChannelOffset] = samples[i * (ChannelCount + ChannelOffset) + ChannelOffset];
                channelArray[i * ChannelOffset + 1] = samples[i * (ChannelCount + ChannelOffset) + ChannelOffset + 1];
            }
            _bpmDetect.InputSamples(channelArray, Convert.ToInt32(frameCount));
        }

        private void FftProcessed()
        {
            _fftPos = 0;
            FastFourierTransform.FFT(true, _m, _fftBuffer);
            double width = SampleRate / _fftBuffer.Length;
            double seconds = 1 / width;
            double Hz = 0;
            var copyBuffer = _pointPool.Rent(_fftBuffer.Length);
            for (int j = 0; j < _fftBuffer.Length; j++)
            {
                copyBuffer[j].X = Hz == 0 ? Math.Log10(5) / Math.Log10(20000) : Math.Log10(Hz) / Math.Log10(20000);

                var level = Math.Abs(Math.Sqrt(Math.Pow(_fftBuffer[j].Y, 2) + Math.Pow(_fftBuffer[j].X, 2)));
                CalculateAverageLevel(Hz, level, seconds);

                copyBuffer[j].Y = 20 * Math.Log10(level);
                Hz += width;
            }
            var bpm = _bpmDetect.GetBpm();
            var currentLevelDb = 20 * Math.Log10(_bassCurrentLevel.SampleAverage);
            var averageLevelDb = 20 * Math.Log10(_bassAverageLevel.SampleAverage);
            var amplitudeRatio = _overallAverageLevel.SampleAverage / _overallAverageLevelAverage.SampleAverage;

            var audioSpectrum = new AudioSpectrum(_pointPool, copyBuffer);
            var audioFrameProcessedEventArgs = new AudioFrameProcessedEventArgs(audioSpectrum, width, bpm, averageLevelDb, currentLevelDb, amplitudeRatio, seconds);
            _audioFrameEventArgsQueue.Enqueue(audioFrameProcessedEventArgs);
            _audioQueueChangeEvent.Set();
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
                    _cancellationTokenSource.Cancel();
                    // _stream.Stop();
                    // _stream.Dispose();
                    // PortAudio.Terminate();
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
