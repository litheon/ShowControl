namespace ShowControl.AudioAnalysis;

/// <summary>
/// A full-track beat grid on the local capture clock: strictly increasing beat times with
/// their beat-within-bar positions. Immutable — a source publishes a new instance when the
/// track changes or its clock anchor moves. Queries beyond either end extrapolate at the
/// edge tempo, so consumers never see a stalled phase.
/// </summary>
public sealed class BeatGrid
{
    private readonly double[] _beatTimes;
    private readonly int[] _beatWithinBar; // 1..beatsPerBar, 1 = downbeat

    public BeatGrid(double[] beatTimes, int[] beatWithinBar, float confidence)
    {
        if (beatTimes.Length < 2)
        {
            throw new ArgumentException("A grid needs at least two beats.", nameof(beatTimes));
        }
        if (beatWithinBar.Length != beatTimes.Length)
        {
            throw new ArgumentException("One bar position per beat.", nameof(beatWithinBar));
        }
        for (int i = 1; i < beatTimes.Length; i++)
        {
            if (beatTimes[i] <= beatTimes[i - 1])
            {
                throw new ArgumentException("Beat times must be strictly increasing.", nameof(beatTimes));
            }
        }
        _beatTimes = beatTimes;
        _beatWithinBar = beatWithinBar;
        Confidence = Math.Clamp(confidence, 0f, 1f);
    }

    public float Confidence { get; }

    public int BeatCount => _beatTimes.Length;

    /// <summary>Index of the last beat at or before <paramref name="time"/>; clamped to the
    /// grid, so callers combine it with the fractional part from <see cref="FractionAt"/>.</summary>
    public int IndexAtOrBefore(double time)
    {
        int idx = Array.BinarySearch(_beatTimes, time);
        if (idx < 0)
        {
            idx = ~idx - 1;
        }
        return Math.Clamp(idx, 0, _beatTimes.Length - 1);
    }

    public double BeatTimeAt(int index) => _beatTimes[Math.Clamp(index, 0, _beatTimes.Length - 1)];

    public int BeatWithinBarAt(int index) => _beatWithinBar[Math.Clamp(index, 0, _beatWithinBar.Length - 1)];

    /// <summary>Beat interval (seconds) around <paramref name="index"/>; edge intervals for
    /// out-of-range queries.</summary>
    public double IntervalAt(int index)
    {
        int i = Math.Clamp(index, 0, _beatTimes.Length - 2);
        return _beatTimes[i + 1] - _beatTimes[i];
    }

    public double BpmAt(double time) => 60.0 / IntervalAt(IndexAtOrBefore(time));

    /// <summary>Continuous position on the grid in beats (index + fraction), extrapolating
    /// at the edges.</summary>
    public double BeatPositionAt(double time)
    {
        int i = IndexAtOrBefore(time);
        double interval = IntervalAt(i);
        return i + (time - _beatTimes[i]) / interval;
    }

    /// <summary>Beat phase 0..1 at a time.</summary>
    public double PhaseAt(double time)
    {
        double position = BeatPositionAt(time);
        return position - Math.Floor(position);
    }

    /// <summary>Bar phase 0..1 at a time, using the grid's own downbeat marks.</summary>
    public double BarPhaseAt(double time, int beatsPerBar)
    {
        int i = IndexAtOrBefore(time);
        double fraction = BeatPositionAt(time) - i;
        double beatsIntoBar = _beatWithinBar[i] - 1 + fraction;
        return AudioMath.SanitizePhase(beatsIntoBar / beatsPerBar);
    }

    /// <summary>Local time of the first downbeat strictly after <paramref name="afterTime"/>,
    /// extrapolating a bar-periodic continuation past the end of the grid.</summary>
    public double NextDownbeat(double afterTime, int beatsPerBar)
    {
        for (int i = Math.Max(IndexAtOrBefore(afterTime), 0); i < _beatTimes.Length; i++)
        {
            if (_beatTimes[i] > afterTime && _beatWithinBar[i] == 1)
            {
                return _beatTimes[i];
            }
        }
        // Past the grid: continue the last bar period.
        double interval = IntervalAt(_beatTimes.Length - 2);
        int last = _beatTimes.Length - 1;
        // Time of the downbeat of the bar containing the last beat, then step bars forward.
        double lastDownbeat = _beatTimes[last] - (_beatWithinBar[last] - 1) * interval;
        double barLength = interval * beatsPerBar;
        double t = lastDownbeat;
        while (t <= afterTime)
        {
            t += barLength;
        }
        return t;
    }

    /// <summary>Convenience: a perfectly regular grid (tests, synthetic sources).</summary>
    public static BeatGrid Regular(double firstBeatTime, double bpm, int beatCount, int beatsPerBar = 4, float confidence = 1f)
    {
        var times = new double[beatCount];
        var bars = new int[beatCount];
        double interval = 60.0 / bpm;
        for (int i = 0; i < beatCount; i++)
        {
            times[i] = firstBeatTime + i * interval;
            bars[i] = (i % beatsPerBar) + 1;
        }
        return new BeatGrid(times, bars, confidence);
    }
}
