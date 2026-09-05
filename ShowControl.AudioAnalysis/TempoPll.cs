namespace ShowControl.AudioAnalysis;

/// <summary>PLL tunables. Time-like values in seconds; gains dimensionless.</summary>
public sealed record PllSettings
{
    public double MinBpm { get; init; } = 60;
    public double MaxBpm { get; init; } = 200;
    public double DefaultBpm { get; init; } = 120;
    public int BeatsPerBar { get; init; } = 4;

    /// <summary>Fraction of the phase error applied per onset.</summary>
    public double PhaseGain { get; init; } = 0.25;

    /// <summary>Fraction of the phase error folded into the period per onset.</summary>
    public double FrequencyGain { get; init; } = 0.06;

    /// <summary>Confidence decay time constant while no onsets confirm the lock.</summary>
    public double ConfidenceTau { get; init; } = 4.0;

    /// <summary>Fraction of the residual audio-vs-grid offset applied per onset while a
    /// metadata grid is driving phase.</summary>
    public double GridTrimGain { get; init; } = 0.1;

    /// <summary>Bound on the grid trim: garbage metadata (or a bad clock mapping in the
    /// source) can pull phase at most this far from the announced grid.</summary>
    public double MaxGridTrimSeconds { get; init; } = 0.06;
}

/// <summary>
/// Beat tracking as a fusion, per the design rule that network beat metadata is a prior
/// correcting a local PLL — never a direct trigger. Three regimes, degrading gracefully:
///
///  - <see cref="BeatMetadataPresence.FullGrid"/>: the announced grid drives phase, tempo
///    and downbeats with whole-track horizon; local onsets trim a bounded residual offset
///    (the audio-vs-metadata latency the source cannot know). Local state is mirrored every
///    advance so losing the grid mid-track hands over without a phase jump.
///  - <see cref="BeatMetadataPresence.BeatsOnly"/>: beat packets act as strong, bar-aware
///    onsets on the local PLL.
///  - <see cref="BeatMetadataPresence.Absent"/> (or no source): pure onset PLL —
///    inter-onset intervals acquire the tempo, phase/frequency corrections track it, and
///    silence free-runs at the last locked tempo.
///
/// Clock-free: time arrives with <see cref="Advance"/> and the events. Not thread-safe by
/// design — the analysis thread owns it.
/// </summary>
public sealed class TempoPll
{
    private readonly PllSettings _s;
    private IBeatMetadataSource? _metadata;

    private double _period;
    private double _lastBeatTime;   // local time of beat number _beatIndex
    private long _beatIndex;        // (_beatIndex % beatsPerBar) == 0 is a downbeat
    private double _confidence;
    private double _lastAdvanceTime = double.NaN;
    private double _lastOnsetTime = double.NaN;
    private double _gridTrim;
    private BeatGrid? _activeGrid;

    public TempoPll(PllSettings? settings = null)
    {
        _s = settings ?? new PllSettings();
        _period = 60.0 / _s.DefaultBpm;
    }

    public double Bpm => 60.0 / _period;
    public float TempoConfidence => (float)Math.Clamp(_confidence, 0, 1);

    /// <summary>The grid currently driving phase, or null when running locally.</summary>
    public bool UsingGrid => _activeGrid != null;

    public void AttachMetadata(IBeatMetadataSource source)
    {
        _metadata = source;
        source.BeatObserved += OnBeatObserved;
    }

    /// <summary>Called once per analysis hop. Advances the free-running phase, adopts or
    /// releases the metadata grid, and decays confidence.</summary>
    public void Advance(double now)
    {
        double dt = double.IsNaN(_lastAdvanceTime) ? 0 : Math.Max(now - _lastAdvanceTime, 0);
        _lastAdvanceTime = now;

        BeatGrid? grid = _metadata?.Presence == BeatMetadataPresence.FullGrid ? _metadata.CurrentGrid : null;
        if (grid != null)
        {
            _activeGrid = grid;
            // Mirror the grid into the local state every advance: phase, tempo, and bar
            // alignment. If the grid disappears next hop, the local PLL continues from
            // exactly here — the seamless-handover guarantee.
            _period = ClampPeriod(60.0 / grid.BpmAt(now - _gridTrim));
            int i = grid.IndexAtOrBefore(now - _gridTrim);
            _lastBeatTime = grid.BeatTimeAt(i) + _gridTrim;
            _beatIndex = grid.BeatWithinBarAt(i) - 1;
            _confidence = grid.Confidence;
            return;
        }
        if (_activeGrid != null)
        {
            _activeGrid = null; // handover; local state already mirrors the last grid hop
        }

        while (now - _lastBeatTime >= _period)
        {
            _lastBeatTime += _period;
            _beatIndex++;
        }
        if (dt > 0 && _s.ConfidenceTau > 0)
        {
            _confidence *= Math.Exp(-dt / _s.ConfidenceTau);
        }
    }

    /// <summary>Feed a local (audio) onset.</summary>
    public void OnOnset(in OnsetEvent onset)
    {
        if (_activeGrid != null)
        {
            // Grid mode: onsets only trim the bounded audio-vs-metadata offset.
            double gridBeat = _activeGrid.BeatTimeAt(_activeGrid.IndexAtOrBefore(onset.TimeSeconds - _gridTrim));
            double err = FoldToHalfPeriod(onset.TimeSeconds - (gridBeat + _gridTrim));
            _gridTrim += _s.GridTrimGain * err * onset.Strength;
            _gridTrim = Math.Clamp(_gridTrim, -_s.MaxGridTrimSeconds, _s.MaxGridTrimSeconds);
            return;
        }

        // Acquisition: fold the inter-onset interval into the tempo octave and pull the
        // period toward it while the lock is weak.
        if (!double.IsNaN(_lastOnsetTime))
        {
            double interval = onset.TimeSeconds - _lastOnsetTime;
            double folded = FoldInterval(interval);
            if (folded > 0 && _confidence < 0.5)
            {
                double pull = 0.35 * (1 - _confidence);
                _period = ClampPeriod(_period + (folded - _period) * pull);
            }
        }
        _lastOnsetTime = onset.TimeSeconds;

        if (_confidence < 0.1)
        {
            // No lock worth defending: snap phase to the onset.
            _lastBeatTime = onset.TimeSeconds;
            _confidence = Math.Max(_confidence, 0.15);
            return;
        }

        // Tracking: phase and frequency corrections from the error to the nearest
        // predicted beat, weighted by onset strength.
        double predicted = NearestBeatTime(onset.TimeSeconds);
        double error = FoldToHalfPeriod(onset.TimeSeconds - predicted);
        double weight = onset.Strength;
        _lastBeatTime += _s.PhaseGain * error * weight;
        _period = ClampPeriod(_period + _s.FrequencyGain * error * weight);

        double closeness = 1.0 - Math.Min(Math.Abs(error) / (_period * 0.25), 1.0);
        _confidence += (closeness - 0.35) * 0.12;
        _confidence = Math.Clamp(_confidence, 0, 1);
    }

    private void OnBeatObserved(BeatObservation beat)
    {
        if (_activeGrid != null)
        {
            return; // the grid outranks per-beat packets from the same source
        }
        // A beat packet is a strong, bar-aware onset: adopt its tempo, correct phase hard,
        // and align the bar.
        if (beat.Bpm > 0)
        {
            _period = ClampPeriod(60.0 / beat.Bpm);
        }
        double error = FoldToHalfPeriod(beat.LocalTime - NearestBeatTime(beat.LocalTime));
        _lastBeatTime += 0.6 * error * beat.Confidence;
        if (beat.BeatWithinBar is >= 1 and <= 64)
        {
            long withinBar = ((_beatIndex % _s.BeatsPerBar) + _s.BeatsPerBar) % _s.BeatsPerBar;
            long announced = beat.BeatWithinBar - 1;
            _beatIndex += announced - withinBar;
        }
        _confidence = Math.Max(_confidence, Math.Min(beat.Confidence, 0.9f));
    }

    public double BeatPhase(double atTime)
    {
        if (_activeGrid != null)
        {
            return AudioMath.SanitizePhase(_activeGrid.PhaseAt(atTime - _gridTrim));
        }
        return AudioMath.SanitizePhase((atTime - _lastBeatTime) / _period);
    }

    public double BarPhase(double atTime)
    {
        if (_activeGrid != null)
        {
            return AudioMath.SanitizePhase(_activeGrid.BarPhaseAt(atTime - _gridTrim, _s.BeatsPerBar));
        }
        double beats = _beatIndex + (atTime - _lastBeatTime) / _period;
        return AudioMath.SanitizePhase(beats / _s.BeatsPerBar);
    }

    /// <summary>Local time of the next downbeat after <paramref name="afterTime"/>. With a
    /// grid this has whole-track horizon; locally it is a one-bar extrapolation.</summary>
    public double NextDownbeat(double afterTime)
    {
        if (_activeGrid != null)
        {
            return _activeGrid.NextDownbeat(afterTime - _gridTrim, _s.BeatsPerBar) + _gridTrim;
        }
        long withinBar = ((_beatIndex % _s.BeatsPerBar) + _s.BeatsPerBar) % _s.BeatsPerBar;
        double t = _lastBeatTime + (_s.BeatsPerBar - withinBar) % _s.BeatsPerBar * _period;
        while (t <= afterTime)
        {
            t += _s.BeatsPerBar * _period;
        }
        return t;
    }

    private double NearestBeatTime(double time)
    {
        double beatsAway = Math.Round((time - _lastBeatTime) / _period);
        return _lastBeatTime + beatsAway * _period;
    }

    private double FoldToHalfPeriod(double error)
    {
        double half = _period / 2;
        double folded = error % _period;
        if (folded > half)
        {
            folded -= _period;
        }
        if (folded < -half)
        {
            folded += _period;
        }
        return folded;
    }

    private double FoldInterval(double interval)
    {
        if (interval <= 0 || !double.IsFinite(interval))
        {
            return 0;
        }
        // Fold toward the current period's octave, not just into the BPM range: a dropped
        // beat produces a doubled interval that is often still inside the range (2× at
        // 128 BPM is 64 BPM) and would otherwise drag the estimate toward half time.
        double folded = interval;
        while (folded > _period * 1.5)
        {
            folded /= 2;
        }
        while (folded < _period * 0.75)
        {
            folded *= 2;
        }
        folded = ClampPeriod(folded);
        return folded;
    }

    private double ClampPeriod(double period)
    {
        if (!double.IsFinite(period))
        {
            return _period;
        }
        return Math.Clamp(period, 60.0 / _s.MaxBpm, 60.0 / _s.MinBpm);
    }
}
