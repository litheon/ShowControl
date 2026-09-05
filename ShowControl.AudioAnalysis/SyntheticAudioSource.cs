namespace ShowControl.AudioAnalysis;

public enum SyntheticPreset
{
    Silence,
    ClickTrack,
    SingleBurst,
    Sweep,
    NoiseFloorWithKicks,
}

/// <summary>
/// Deterministic signal generators pushed through <see cref="AudioAnalysisEngine.Submit"/>,
/// so tests and the debug view can exercise every published field before (or without) live
/// capture. Time is synthetic: sample index over sample rate.
/// </summary>
public sealed class SyntheticAudioSource : IDisposable
{
    private readonly AudioAnalysisEngine _engine;
    private readonly int _sampleRate;
    private long _position;
    private CancellationTokenSource? _realtimeCts;
    private Thread? _realtimeThread;

    public SyntheticAudioSource(AudioAnalysisEngine engine)
    {
        _engine = engine;
        _sampleRate = engine.Settings.SampleRate;
    }

    /// <summary>True while a real-time feed is running.</summary>
    public bool IsRunning => _realtimeThread is { IsAlive: true };

    /// <summary>
    /// Feeds the preset at wall-clock pace on a background thread, for the debug view —
    /// the analysis itself stays clock-free; only this source knows about real time.
    /// Starting a new preset stops the previous one. Runs until <see cref="Stop"/>.
    /// </summary>
    public void StartRealtime(SyntheticPreset preset, double bpm = 128)
    {
        Stop();
        var cts = new CancellationTokenSource();
        _realtimeCts = cts;
        _realtimeThread = new Thread(() => RealtimeLoop(preset, bpm, cts.Token))
        {
            IsBackground = true,
            Name = "SyntheticAudio",
        };
        _realtimeThread.Start();
    }

    public void Stop()
    {
        _realtimeCts?.Cancel();
        _realtimeThread?.Join(500);
        _realtimeCts?.Dispose();
        _realtimeCts = null;
        _realtimeThread = null;
    }

    private void RealtimeLoop(SyntheticPreset preset, double bpm, CancellationToken token)
    {
        const int blockSize = 512;
        var block = new float[blockSize];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long sent = 0;
        long startPosition = _position;
        while (!token.IsCancellationRequested)
        {
            // Stay one block ahead of the wall clock; sleep off any surplus.
            double aheadSeconds = (double)(sent - (long)(clock.Elapsed.TotalSeconds * _sampleRate)) / _sampleRate;
            if (aheadSeconds > 0.02)
            {
                Thread.Sleep(10);
                continue;
            }
            for (int i = 0; i < blockSize; i++)
            {
                block[i] = Sample(preset, startPosition + sent + i, bpm);
            }
            _engine.Submit(block, (double)(startPosition + sent) / _sampleRate);
            sent += blockSize;
        }
        _position = startPosition + sent;
    }

    public void Dispose() => Stop();

    /// <summary>Generates <paramref name="seconds"/> of the preset and submits it in
    /// capture-sized blocks. Call <see cref="AudioAnalysisEngine.ProcessPending"/> (tests)
    /// or rely on the engine thread (debug view) to drain.</summary>
    public void Generate(SyntheticPreset preset, double seconds, double bpm = 128, int blockSize = 512)
    {
        int total = (int)(seconds * _sampleRate);
        var block = new float[blockSize];
        int generated = 0;
        while (generated < total)
        {
            int count = Math.Min(blockSize, total - generated);
            for (int i = 0; i < count; i++)
            {
                block[i] = Sample(preset, _position + i, bpm);
            }
            _engine.Submit(block.AsSpan(0, count), (double)_position / _sampleRate);
            _position += count;
            generated += count;
        }
    }

    private float Sample(SyntheticPreset preset, long index, double bpm)
    {
        double t = (double)index / _sampleRate;
        switch (preset)
        {
            case SyntheticPreset.Silence:
                return 0f;

            case SyntheticPreset.ClickTrack:
            {
                // 3ms click on every beat.
                double beatPeriod = 60.0 / bpm;
                double tInBeat = t % beatPeriod;
                return tInBeat < 0.003 ? (float)(0.9 * Math.Sin(2 * Math.PI * 2000 * tInBeat)) : 0f;
            }

            case SyntheticPreset.SingleBurst:
            {
                // One 3ms noise burst at t = 0.5s, silence elsewhere.
                if (t is >= 0.5 and < 0.503)
                {
                    return (float)(0.9 * Math.Sin(2 * Math.PI * 3000 * t) * Math.Sin(2 * Math.PI * 4700 * t + 1.3));
                }
                return 0f;
            }

            case SyntheticPreset.Sweep:
            {
                // 100Hz → 8kHz exponential sweep over 10s, looping.
                double phase = t % 10.0;
                double f = 100 * Math.Pow(80, phase / 10.0);
                return (float)(0.5 * Math.Sin(2 * Math.PI * f * phase));
            }

            case SyntheticPreset.NoiseFloorWithKicks:
            {
                // Quiet deterministic pseudo-noise plus a 60Hz kick thump on each beat.
                double beatPeriod = 60.0 / bpm;
                double tInBeat = t % beatPeriod;
                double noise = 0.02 * Math.Sin(2 * Math.PI * 997 * t) * Math.Sin(2 * Math.PI * 1571 * t + 0.7);
                double kick = tInBeat < 0.08
                    ? 0.8 * Math.Sin(2 * Math.PI * 60 * tInBeat) * Math.Exp(-tInBeat / 0.03)
                    : 0;
                return (float)(noise + kick);
            }

            default:
                return 0f;
        }
    }
}
