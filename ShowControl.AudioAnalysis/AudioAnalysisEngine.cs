using System.Buffers;
using System.Collections.Concurrent;
using ShowControl.AudioAnalysis.Dsp;

namespace ShowControl.AudioAnalysis;

/// <summary>
/// The analysis core. Fed raw capture blocks from any thread via <see cref="Submit"/>;
/// publishes one immutable <see cref="AnalysisSnapshot"/> per STFT hop (~93.75 Hz at the
/// default settings) through a single volatile reference write — the SetSampleMap idiom —
/// plus <see cref="SnapshotPublished"/> for push-style consumers.
///
/// The engine runs for the life of the app, independent of which visualization is on
/// screen: nothing here starts or stops with an animation or shader. It has no clock of
/// its own — time arrives with the samples — so tests drive it deterministically with
/// <see cref="ProcessPending"/> and never need the worker thread.
/// </summary>
public sealed class AudioAnalysisEngine : IDisposable
{
    private readonly AnalysisSettings _settings;
    private readonly Stft _stft;
    private readonly AudioLiteComposer _composer;
    private readonly BandAnalyzer _bands;
    private readonly OnsetDetector _onsets;
    private readonly TempoPll _pll;
    private readonly SectionTracker _sections;
    private readonly IBeatMetadataSource? _beatMetadata;
    private readonly float[] _previousMagnitudes;
    private int _onsetsSinceLastHop;
    private double _onsetRate;
    private bool _havePreviousMagnitudes;

    private readonly ConcurrentQueue<(float[] Buffer, int Count, double Time)> _blocks = new();
    private readonly ManualResetEventSlim _blockAvailable = new(false);
    private readonly CancellationTokenSource _cts = new();
    private readonly object _processLock = new();
    private Thread? _worker;

    private AnalysisSnapshot _latest = AnalysisSnapshot.Empty;

    private bool _anchored;
    private double _timeAnchor;          // capture-clock time of absolute sample 0
    private double _lastPublishTime;
    private long _frameIndex;
    private long _onsetSampleCursor;     // samples already fed to the transient path
    private double _lastLoudTime = double.NegativeInfinity;

    public AudioAnalysisEngine(AnalysisSettings? settings = null, IBeatMetadataSource? beatMetadata = null)
    {
        _settings = settings ?? new AnalysisSettings();
        _stft = new Stft(_settings.FftWindowSize, _settings.HopSize);
        _composer = new AudioLiteComposer(_settings);
        _bands = new BandAnalyzer(_settings);
        _onsets = new OnsetDetector(_settings.SampleRate);
        _pll = new TempoPll(new PllSettings
        {
            DefaultBpm = _settings.DefaultBpm,
            BeatsPerBar = _settings.BeatsPerBar,
        });
        if (beatMetadata != null)
        {
            _pll.AttachMetadata(beatMetadata);
        }
        _beatMetadata = beatMetadata;
        // Scheduled hits (predicted downbeats, announced drops) fire early by the measured
        // pipeline latency so the light lands with the sound; live-detected transitions
        // cannot be advanced and are not.
        _sections = new SectionTracker(
            new SectionSettings { LeadSeconds = LatencyModel.PipelineSeconds },
            t => _pll.NextDownbeat(t));
        _previousMagnitudes = new float[_settings.FftWindowSize / 2];
    }

    public AnalysisSettings Settings => _settings;

    /// <summary>The most recent published frame; never null, valid during silence and
    /// before the first publish.</summary>
    public AnalysisSnapshot Latest => Volatile.Read(ref _latest);

    /// <summary>Raised on the analysis thread (or the ProcessPending caller) once per
    /// published frame. Handlers must be cheap and must not retain the snapshot's arrays
    /// beyond reading them.</summary>
    public event Action<AnalysisSnapshot>? SnapshotPublished;

    /// <summary>Copies a capture block onto the analysis queue. Safe from a real-time
    /// callback: one pooled rent, one copy, one enqueue — no locks, no logging.</summary>
    /// <param name="captureTimeSeconds">Capture-clock time of the first sample in the
    /// block. Only the first submit anchors the clock; sample count carries time from
    /// there, because the sample counter *is* the device clock.</param>
    public void Submit(ReadOnlySpan<float> samples, double captureTimeSeconds)
    {
        if (samples.IsEmpty || _cts.IsCancellationRequested)
        {
            return;
        }
        float[] buffer = ArrayPool<float>.Shared.Rent(samples.Length);
        samples.CopyTo(buffer);
        _blocks.Enqueue((buffer, samples.Length, captureTimeSeconds));
        _blockAvailable.Set();
    }

    /// <summary>Starts the background analysis thread. Idempotent. Tests skip this and
    /// call <see cref="ProcessPending"/> instead.</summary>
    public void Start()
    {
        if (_worker is not null)
        {
            return;
        }
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "AudioAnalysis" };
        _worker.Start();
    }

    /// <summary>Drains the queue synchronously, publishing every frame that becomes ready.
    /// The worker thread and tests share this path.</summary>
    public void ProcessPending()
    {
        lock (_processLock)
        {
            while (_blocks.TryDequeue(out var block))
            {
                try
                {
                    ProcessBlock(block.Buffer.AsSpan(0, block.Count), block.Time);
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(block.Buffer);
                }
            }
        }
    }

    private void ProcessBlock(ReadOnlySpan<float> samples, double captureTime)
    {
        if (!_anchored)
        {
            _timeAnchor = double.IsFinite(captureTime) ? captureTime : 0;
            _anchored = true;
        }

        // Transient path first: sub-hop-precision onset timestamps into the PLL, at full
        // capture rate, no FFT.
        double blockStart = _timeAnchor + (double)_onsetSampleCursor / _settings.SampleRate;
        _onsetSampleCursor += samples.Length;
        _onsets.Process(samples, blockStart, onset =>
        {
            _pll.OnOnset(onset);
            _onsetsSinceLastHop++;
        });

        _stft.Append(samples);
        while (_stft.TryComputeFrame())
        {
            PublishFrame();
        }
    }

    private void PublishFrame()
    {
        double sampleRate = _settings.SampleRate;
        double time = _timeAnchor + _stft.FrameEndSampleIndex / sampleRate;
        double dt = _frameIndex == 0
            ? (double)_settings.FftWindowSize / sampleRate
            : time - _lastPublishTime;
        _lastPublishTime = time;

        ReadOnlySpan<float> magnitudes = _stft.Magnitudes;
        float flux = _havePreviousMagnitudes ? SpectralFeatures.Flux(_previousMagnitudes, magnitudes) : 0f;
        magnitudes.CopyTo(_previousMagnitudes);
        _havePreviousMagnitudes = true;

        float rms = _stft.HopRms;
        float peak = _stft.HopPeak;
        float centroid = SpectralFeatures.CentroidHz(magnitudes, _settings.SampleRate, _settings.FftWindowSize);
        float flatness = SpectralFeatures.Flatness(magnitudes);

        if (rms > _settings.SilenceRmsFloor)
        {
            _lastLoudTime = time;
        }
        bool signalPresent = time - _lastLoudTime <= _settings.SignalHoldSeconds;

        // Phase advances whatever else happens — the PLL free-runs in silence, tracks
        // onsets when they come, and servos onto a metadata grid when one is present.
        // The pre-advance to display time applies here and only here.
        _pll.Advance(time);
        double bpm = _pll.Bpm;
        double displayBarPhase = _pll.BarPhase(time + LatencyModel.PipelineSeconds);

        _bands.Analyze(magnitudes, dt);
        var bandEnergy = new float[AnalysisSnapshot.BandCount];
        var bandFlux = new float[AnalysisSnapshot.BandCount];
        _bands.BandEnergy.CopyTo(bandEnergy);
        _bands.BandFlux.CopyTo(bandFlux);

        // Onset density as an exponential rate (events/sec), and top-quarter band energy —
        // two of the section tracker's build tells.
        double rateAlpha = 1 - Math.Exp(-dt / 1.5);
        _onsetRate += (_onsetsSinceLastHop / dt - _onsetRate) * rateAlpha;
        _onsetsSinceLastHop = 0;
        float highBand = 0;
        for (int i = AnalysisSnapshot.BandCount * 3 / 4; i < AnalysisSnapshot.BandCount; i++)
        {
            highBand += bandEnergy[i];
        }
        highBand /= AnalysisSnapshot.BandCount / 4;

        if (_beatMetadata is { Presence: BeatMetadataPresence.FullGrid })
        {
            _sections.SetAnnouncedSections(_beatMetadata.Sections);
        }
        else
        {
            _sections.SetAnnouncedSections(null);
        }
        _sections.Advance(new SectionFeatures(
            time, rms, flux, centroid, (float)_onsetRate, highBand, _pll.TempoConfidence), dt);

        var features = new HopFeatures(rms, peak, flux, centroid, flatness, signalPresent);
        AudioLiteFrame lite = _composer.Compose(features, time, dt, displayBarPhase,
            _sections.Tension, _sections.Motion, _sections.Spread);

        var snapshot = new AnalysisSnapshot
        {
            Time = time,
            Dt = dt,
            FrameIndex = _frameIndex++,
            SignalPresent = signalPresent,
            Rms = AudioMath.Sanitize(rms, 0f, 1f),
            Peak = AudioMath.Sanitize(peak, 0f, 1f),
            FluxTotal = AudioMath.Sanitize(flux, 0f, float.MaxValue),
            CentroidHz = AudioMath.Sanitize(centroid, 0f, _settings.SampleRate / 2f),
            Flatness = AudioMath.Sanitize(flatness, 0f, 1f),
            Bpm = AudioMath.Sanitize((float)bpm, 0f, 400f),
            TempoConfidence = AudioMath.Sanitize(_pll.TempoConfidence, 0f, 1f),
            BeatPhase = AudioMath.SanitizePhase(_pll.BeatPhase(time)),
            BarPhase = AudioMath.SanitizePhase(_pll.BarPhase(time)),
            BandEnergy = bandEnergy,
            BandFlux = bandFlux,
            Section = _sections.Section,
            SectionConfidence = AudioMath.Sanitize(_sections.SectionConfidence, 0f, 1f),
            Lite = lite,
        };

        Volatile.Write(ref _latest, snapshot);
        SnapshotPublished?.Invoke(snapshot);
    }

    private void WorkerLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                _blockAvailable.Wait(_cts.Token);
                _blockAvailable.Reset();
                ProcessPending();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _worker?.Join(millisecondsTimeout: 1000);
        _cts.Dispose();
        _blockAvailable.Dispose();
        while (_blocks.TryDequeue(out var block))
        {
            ArrayPool<float>.Shared.Return(block.Buffer);
        }
    }
}
