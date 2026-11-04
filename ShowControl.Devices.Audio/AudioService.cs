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

        // VENUE TOGGLE: capture on/off. The stream is the one piece of this service that
        // touches hardware; with the toggle off the service still constructs, enumerates
        // devices and satisfies DI, and every consumer stays dormant — the pre-audio-reactivity
        // state of the rig. Flip to false to ship a build with audio off.
        private const bool CaptureEnabled = true;

        public const int SampleRate = 48_000;

        /// <summary>
        /// How the interleaved capture buffer maps onto the one mono channel everything
        /// downstream consumes. <see cref="Stride"/> is the number of channels the stream was
        /// opened with — the fewest that reach the chosen pair, i.e. the higher index plus
        /// one, so a mono feed on input 1 opens one channel and inputs 1/2 open two.
        /// <see cref="Left"/>/<see cref="Right"/> are the pair averaged into mono; equal
        /// indices select a single channel. <see cref="DeviceChannels"/> is how many the
        /// device has, the range a later selection is validated against. Immutable so the
        /// real-time callback reads one consistent layout per block.
        /// </summary>
        private sealed record CaptureLayout(int Stride, int Left, int Right, int DeviceChannels);
        private static readonly CaptureLayout NoLayout = new(0, 0, 0, 0);
        private volatile CaptureLayout _layout = NoLayout;
        // The pair the operator asked for, kept across device switches and clamped to
        // whatever the next device actually has. 0/1 is the ordinary stereo feed.
        private int _requestedLeft = 0;
        private int _requestedRight = 1;

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

        public const int FftLength = 4096;
        private int _fftPos;
        private readonly int _m = (int)Math.Log(FftLength, 2.0);
        private readonly Complex[] _fftBuffer = new Complex[FftLength];

        private readonly IBpmDetect _bpmDetect;
        private readonly ILogger<AudioService> _logger;

        // The analysis engine is a second, independent consumer of the capture blocks. It is
        // owned by the host and runs whatever visualization is on screen; this service only
        // feeds it. Null when the host runs without it (tests, legacy entry points).
        private readonly ShowControl.AudioAnalysis.AudioAnalysisEngine _analysisEngine;
        private long _capturedSamples;

        private record AudioProcessSignal(float[] sharedArray, int frameCount);
        private readonly object _disposalLock = new object();
        // Volatile: the stream-finished callback reads it from PortAudio's thread while Stop
        // holds the disposal lock — taking the lock there instead would deadlock against
        // _stream.Stop() waiting for callbacks to drain.
        private volatile bool _stopped;
        // Same reasoning: set around a deliberate stream stop during a device switch so the
        // finished callback does not restart the stream being replaced.
        private volatile bool _suppressRestart;
        private volatile int _activeDeviceIndex = -1;
        private bool _threadsStarted;
        private Stream _stream;
        private Thread _audioProcessingThread;
        private Thread _audioDispatchThread;
        private ManualResetEvent _signalQueueChangeEvent = new ManualResetEvent(false);
        ConcurrentQueue<AudioProcessSignal> _signalQueue;
        private ManualResetEvent _audioQueueChangeEvent = new ManualResetEvent(false);
        ConcurrentQueue<AudioFrameProcessedEventArgs> _audioFrameEventArgsQueue;
        private CancellationTokenSource _cancellationTokenSource;
        public AudioService(ILogger<AudioService> logger, ShowControl.AudioAnalysis.AudioAnalysisEngine analysisEngine = null)
        {
            _logger = logger;
            _analysisEngine = analysisEngine;
            _cancellationTokenSource = new CancellationTokenSource();
            _bpmDetect = new RealTimeBpmDetect(channelCount: 1, SampleRate);
            _bassAverageLevel = new SampleAverageLevelCalculator(0);
            // (double) matters: SampleRate / FftLength in integers rounds the bin width and
            // put the bass window one bin past 250Hz.
            _bassCurrentLevel = new SampleAverageLevelCalculator(Convert.ToInt32(Math.Round(BassCutOffHz / ((double)SampleRate / FftLength))) - 1);
            _overallAverageLevel = new SampleAverageLevelCalculator(0);
            _overallAverageLevelAverage = new SampleAverageLevelCalculator(0);
            _audioProcessingThread = new Thread(ProcessingThreadStart);
            _audioDispatchThread = new Thread(DispatchThreadStart);
            _signalQueue = new ConcurrentQueue<AudioProcessSignal>();
            _audioFrameEventArgsQueue = new ConcurrentQueue<AudioFrameProcessedEventArgs>();
            PortAudio.Initialize();

            if (!CaptureEnabled)
            {
                _logger.LogInformation("[AudioService] Capture disabled by venue toggle; running without audio.");
                return;
            }

            int deviceIndex = FindPreferredDeviceIndex();
            if (deviceIndex == PortAudio.NoDevice)
            {
                // This constructor runs inside DI at startup; throwing here takes the whole
                // host down before there is a window to read the error in. The rig must come
                // up with no audio interface attached — the picker can select one later.
                _logger.LogWarning("[AudioService] No audio input device found; running without audio.");
                return;
            }
            if (!OpenStream(deviceIndex))
            {
                // Same startup-resilience rule: a broken audio stack degrades to no audio,
                // never to no app.
                _logger.LogError("[AudioService] Failed to open the capture stream; running without audio.");
            }
        }

        private static int FindPreferredDeviceIndex()
        {
            for (int i = 0; i < PortAudio.DeviceCount; i++)
            {
                var inputDevice = PortAudio.GetDeviceInfo(i);
                if (inputDevice.name.Contains("Focusrite USB ASIO") || inputDevice.name.Contains("Scarlett") || inputDevice.name == "Universal Audio Thunderbolt")
                {
                    return i;
                }
            }
            for (int i = 0; i < PortAudio.DeviceCount; i++)
            {
                var inputDevice = PortAudio.GetDeviceInfo(i);
                if (inputDevice.name.Contains("Microphone") && !inputDevice.name.Contains("iPhone"))
                {
                    return i;
                }
            }
            return PortAudio.DefaultInputDevice;
        }

        /// <summary>Opens and starts the capture stream on a device; the worker threads are
        /// started on the first success and survive device switches. Returns false and
        /// leaves the service streamless on failure — never throws to the caller.</summary>
        private bool OpenStream(int deviceIndex)
        {
            try
            {
                var deviceInfo = PortAudio.GetDeviceInfo(deviceIndex);
                int channels = deviceInfo.maxInputChannels;
                if (channels < 1)
                {
                    _logger.LogWarning("[AudioService] '{Device}' has no usable input channels.", deviceInfo.name);
                    return false;
                }
                // Open only as many channels as the chosen pair reaches. PortAudio has no
                // per-channel selection, just "the first N", so the pair sets N and the
                // callback averages the two it wants out of the interleaved block.
                int left = Math.Clamp(_requestedLeft, 0, channels - 1);
                int right = Math.Clamp(_requestedRight, 0, channels - 1);
                var layout = new CaptureLayout(Math.Max(left, right) + 1, left, right, channels);
                _logger.LogInformation("[AudioService] Capturing from '{Device}' at {Rate}Hz: {Opened} of {Channels} channels, inputs {Left}/{Right}.",
                    deviceInfo.name, SampleRate, layout.Stride, channels, left + 1, right + 1);
                var inputParameters = new StreamParameters
                {
                    device = deviceIndex,
                    channelCount = layout.Stride,
                    sampleFormat = SampleFormat.Float32,
                    suggestedLatency = deviceInfo.defaultLowInputLatency,
                    hostApiSpecificStreamInfo = IntPtr.Zero,
                };
                // Static trampolines, not instance delegates: the published app is aot-only
                // and only a static [MonoPInvokeCallback] target has a precompiled wrapper.
                // The service rides along as the stream's user data (a GCHandle PortAudioSharp
                // hands back to every callback).
                _stream = new PortAudioSharp.Stream(inputParameters, null, SampleRate, 0, StreamFlags.NoFlag, new Stream.Callback(CaptureCallback), this);
                _stream.SetFinishedCallback(StreamFinished);
                if (!_threadsStarted)
                {
                    _threadsStarted = true;
                    _audioDispatchThread.Start();
                    _audioProcessingThread.Start();
                }
                // Publish the layout before the first callback can read it; the previous
                // stream (if any) was stopped and drained before this one was opened.
                _layout = layout;
                _stream.Start();
                _activeDeviceIndex = deviceIndex;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AudioService] Failed to open capture on device {Index}.", deviceIndex);
                _stream = null;
                _layout = NoLayout;
                _activeDeviceIndex = PortAudio.NoDevice;
                return false;
            }
        }

        private void StreamFinishedCallback(IntPtr userData)
        {
            if (_stopped || _suppressRestart)
            {
                // The finished callback also fires for the deliberate stops — teardown and
                // device switches; restarting there would fight both.
                return;
            }
            _logger.LogInformation("Audio stream finished, restarting.");
            _stream.Stop();
            _stream.Start();
        }

        public int ActiveInputDeviceIndex => _activeDeviceIndex;

        public (int Left, int Right) ActiveInputChannels
        {
            get
            {
                var layout = _layout;
                return (layout.Left, layout.Right);
            }
        }

        public bool SelectInputChannels(int left, int right)
        {
            lock (_disposalLock)
            {
                if (_stopped || !CaptureEnabled)
                {
                    return false;
                }
                var current = _layout;
                if (current.Stride == 0)
                {
                    // No stream to validate against: remember the wish, the next OpenStream
                    // clamps it to the device it gets.
                    _requestedLeft = left;
                    _requestedRight = right;
                    return false;
                }
                if (left < 0 || right < 0 || left >= current.DeviceChannels || right >= current.DeviceChannels)
                {
                    _logger.LogWarning("[AudioService] Inputs {Left}/{Right} are outside the {Channels} channels of the active device.",
                        left + 1, right + 1, current.DeviceChannels);
                    return false;
                }
                int previousLeft = _requestedLeft;
                int previousRight = _requestedRight;
                _requestedLeft = left;
                _requestedRight = right;
                if (Math.Max(left, right) + 1 == current.Stride)
                {
                    // The open stream already carries both: one reference swap, the
                    // callback picks it up on its next block.
                    _layout = current with { Left = left, Right = right };
                    return true;
                }
                // A different channel count means a different stream — the same stop/open
                // as a device switch, with the same revert if the new one will not open.
                int device = _activeDeviceIndex;
                if (ReopenStream(device))
                {
                    return true;
                }
                _requestedLeft = previousLeft;
                _requestedRight = previousRight;
                if (ReopenStream(device))
                {
                    _logger.LogWarning("[AudioService] Reverted to the previous input pair.");
                }
                return false;
            }
        }

        /// <summary>Stops and disposes whatever stream is open (its finished callback
        /// suppressed so it does not restart the stream being replaced) and opens
        /// <paramref name="deviceIndex"/> with the current requested pair. Caller holds the
        /// disposal lock. Leaves the service streamless on failure, never throws.</summary>
        private bool ReopenStream(int deviceIndex)
        {
            _suppressRestart = true;
            try
            {
                var previousStream = _stream;
                if (previousStream != null)
                {
                    try
                    {
                        previousStream.Stop();
                        previousStream.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[AudioService] Stopping the previous capture stream failed; continuing with the switch.");
                    }
                    _stream = null;
                    _layout = NoLayout;
                    _activeDeviceIndex = PortAudio.NoDevice;
                }
                return OpenStream(deviceIndex);
            }
            finally
            {
                _suppressRestart = false;
            }
        }

        public IReadOnlyList<AudioInputDevice> GetInputDevices()
        {
            var devices = new List<AudioInputDevice>();
            if (!CaptureEnabled)
            {
                return devices;
            }
            int active = _activeDeviceIndex;
            for (int i = 0; i < PortAudio.DeviceCount; i++)
            {
                var info = PortAudio.GetDeviceInfo(i);
                if (info.maxInputChannels >= 1)
                {
                    devices.Add(new AudioInputDevice(i, info.name, info.maxInputChannels, i == active));
                }
            }
            return devices;
        }

        public bool SelectInputDevice(int deviceIndex)
        {
            lock (_disposalLock)
            {
                if (_stopped)
                {
                    return false;
                }
                if (!CaptureEnabled)
                {
                    _logger.LogInformation("[AudioService] Capture is disabled by the venue toggle; device selection ignored.");
                    return false;
                }
                if (deviceIndex == _activeDeviceIndex && _stream != null)
                {
                    return true;
                }

                int previousDevice = _activeDeviceIndex;
                if (ReopenStream(deviceIndex))
                {
                    return true;
                }
                // The chosen device would not open; put the old one back rather than
                // leaving the rig deaf. If that also fails we run without audio, which
                // every consumer already survives.
                if (previousDevice != PortAudio.NoDevice && ReopenStream(previousDevice))
                {
                    _logger.LogWarning("[AudioService] Reverted to the previous capture device.");
                }
                return false;
            }
        }

        public AudioService(
            ILogger<AudioService> logger,
            EventHandler<AudioFrameProcessedEventArgs> audioFrameProcessedEventHandler,
            ShowControl.AudioAnalysis.AudioAnalysisEngine analysisEngine = null) : this(logger, analysisEngine)
        {
            AudioFrameProcessed += audioFrameProcessedEventHandler;
        }

        [MonoPInvokeCallback(typeof(Stream.Callback))]
        private static StreamCallbackResult CaptureCallback(IntPtr input, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags flags, IntPtr userData)
        {
            var service = userData != IntPtr.Zero ? GCHandle.FromIntPtr(userData).Target as AudioService : null;
            return service == null
                ? StreamCallbackResult.Abort
                : service.Callback(input, output, frameCount, ref timeInfo, flags, userData);
        }

        [MonoPInvokeCallback(typeof(Stream.FinishedCallback))]
        private static void StreamFinished(IntPtr userData)
        {
            var service = userData != IntPtr.Zero ? GCHandle.FromIntPtr(userData).Target as AudioService : null;
            service?.StreamFinishedCallback(userData);
        }

        private StreamCallbackResult Callback(IntPtr input, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo e, StreamCallbackFlags f, IntPtr d)
        {
            try
            {
                var layout = _layout;
                int frames = (int)frameCount;
                int stride = layout.Stride;
                float[] mono;
                if (stride <= 1)
                {
                    // A mono device: the block already is the channel.
                    mono = _audioPool.Rent(frames);
                    Marshal.Copy(input, mono, 0, frames);
                }
                else
                {
                    // Average the chosen pair out of the interleaved block. Both pooled —
                    // frameCount is the driver's choice and has no bound worth betting the
                    // stack on — and the interleaved copy goes straight back.
                    int sampleCount = frames * stride;
                    var interleaved = _audioPool.Rent(sampleCount);
                    try
                    {
                        Marshal.Copy(input, interleaved, 0, sampleCount);
                        mono = _audioPool.Rent(frames);
                        Downmix(interleaved, mono, frames, stride, layout.Left, layout.Right);
                    }
                    finally
                    {
                        _audioPool.Return(interleaved);
                    }
                }

                // Fan out to the analysis engine first (it copies synchronously, so handing
                // the same buffer to the legacy queue afterwards is safe). The sample counter
                // is the capture clock: the engine anchors on the first submit and counts
                // from there. No logging here — this is the real-time thread.
                _analysisEngine?.Submit(mono.AsSpan(0, frames), (double)_capturedSamples / SampleRate);
                _capturedSamples += frameCount;

                _signalQueue.Enqueue(new AudioProcessSignal(mono, frames));
                _signalQueueChangeEvent.Set();
                return StreamCallbackResult.Continue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in audio callback, aborting stream.");
                return StreamCallbackResult.Abort;
            }
        }

        /// <summary>Mono from an interleaved block: the mean of the two chosen channels, or
        /// the one channel when both indices agree (no 6dB loss on a single input).</summary>
        internal static void Downmix(ReadOnlySpan<float> interleaved, Span<float> mono, int frames, int stride, int left, int right)
        {
            if (left == right)
            {
                for (int i = 0; i < frames; i++)
                {
                    mono[i] = interleaved[i * stride + left];
                }
                return;
            }
            for (int i = 0; i < frames; i++)
            {
                int row = i * stride;
                mono[i] = 0.5f * (interleaved[row + left] + interleaved[row + right]);
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
                    // This thread owns the args' pooled spectrum: every subscriber on the
                    // multicast list runs first, then it is disposed exactly once. A
                    // subscriber that disposed it itself would hand later subscribers a
                    // returned array — subscribers copy what they keep and never dispose.
                    try
                    {
                        AudioFrameProcessed?.Invoke(this, eventArgs);
                    }
                    finally
                    {
                        eventArgs.Dispose();
                    }
                }
                if (_audioFrameEventArgsQueue.Count == 0)
                {
                    _audioQueueChangeEvent.Reset();
                }
            }
        }

        private void DoProcessing(int frameCount, float[] samples)
        {
            // The block is already mono (the callback downmixed the chosen pair), so the
            // FFT and the detector both read it straight.
            for (int i = 0; i < frameCount; i++)
            {
                var sample = samples[i];
                _fftBuffer[_fftPos].X = (float)(sample * FastFourierTransform.HammingWindow(_fftPos, _fftBuffer.Length));
                _fftBuffer[_fftPos].Y = 0;
                _fftPos++;
                if (_fftPos >= _fftBuffer.Length)
                {
                    FftProcessed();
                }
            }
            _bpmDetect.InputSamples(samples.AsSpan(0, frameCount), frameCount);
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
                    _stopped = true;
                    _cancellationTokenSource.Cancel();
                    // Wake both workers so the cancellation is seen; they park on the events.
                    _signalQueueChangeEvent.Set();
                    _audioQueueChangeEvent.Set();
                    if (_stream != null)
                    {
                        _stream.Stop();
                        _stream.Dispose();
                        PortAudio.Terminate();
                    }
                }
            }
        }
        public void Dispose()
        {
            Stop();
        }
    }
}
