namespace ShowControl.AudioAnalysis;

/// <summary>Which of its three forms the bias is taking. Exactly one at a time.</summary>
internal enum FlowBiasMode
{
    /// <summary>Everything moves right: the fluid is pushed, and the injection points
    /// travel, at the bias speed.</summary>
    Right,

    /// <summary>Everything moves left.</summary>
    Left,

    /// <summary>Nothing is moved; the bias goes instead into the injection points'
    /// emission, which is increased to their left and right by it.</summary>
    Split,
}

/// <summary>
/// The sideways bias of a visualization's field: its form and its speed.
///
/// The form is one of three — everything moving left, everything moving right (the
/// fluid pushed and the injection points physically carried along, around the plane's
/// continuous left-right axis), or nothing moved and the same velocity handed to the
/// injection points' left and right emission — and never a blend: a drop switches it,
/// at once, to the next form.
/// The speed follows the music's shape: it sinks in a slow section (low energy) and
/// RAMPS up to a drop — quadratically, so the climb is gentle at first and steep at the
/// end — smoothed so it swells rather than steps. How far into the lead-up we are comes
/// from two readings, whichever is further along: the section tracker's tension, which
/// is inferred from the audio itself and needs no metadata (rising level, brightness and
/// onset density accumulated over bars), and, when a drop is announced or predicted, a
/// countdown over the last <see cref="RampSeconds"/> before it.
///
/// The speed is owned here, in plane widths, so the push on the fluid and the travel of
/// the injection points are the same number and cannot drift apart. Clock-free like the
/// rest of the analysis core.
/// </summary>
internal sealed class FlowBias
{
    /// <summary>The nominal bias speed, plane widths per second: 800 pixels of the
    /// 1350-pixel image per minute. <see cref="Speed"/> is a multiple of this.</summary>
    internal const double NominalWidthsPerSecond = 800.0 / 1350.0 / 60.0;

    /// <summary>Speed multiple in the slowest section (no energy, no build).</summary>
    internal const double SlowSpeed = 0.25;

    /// <summary>How many times the top of a build multiplies the speed.</summary>
    internal const double BuildGain = 3.0;

    /// <summary>How long before a known drop the countdown ramp begins.</summary>
    internal const double RampSeconds = 16.0;

    /// <summary>The speed's smoothing time constant.</summary>
    internal const double SpeedSeconds = 1.5;

    private double _speed = SlowSpeed;
    private double _offset;

    /// <summary>How far into the lead-up to a drop the music is, 0..1, after the last
    /// <see cref="Advance"/>: the greater of the inferred tension and the countdown.</summary>
    public float Build { get; private set; }

    /// <summary>The lead-up to a drop as one 0..1 reading. Works with no metadata at all
    /// (tension alone); a known drop's countdown takes over when it is further along.</summary>
    public static double BuildOf(double tension, double dropIn)
    {
        double build = double.IsFinite(tension) ? Math.Clamp(tension, 0, 1) : 0;
        if (double.IsFinite(dropIn) && dropIn >= 0)
        {
            build = Math.Max(build, 1 - Math.Min(dropIn, RampSeconds) / RampSeconds);
        }
        return build;
    }

    public FlowBiasMode Mode { get; private set; } = FlowBiasMode.Right;

    /// <summary>The current speed as a multiple of the nominal, ≥ 0, whatever form the
    /// bias is in.</summary>
    public float Speed => AudioMath.Sanitize((float)_speed, 0f, (float)BuildGain);

    /// <summary>Signed drift of the whole field, plane widths per second: + right,
    /// − left, 0 while split.</summary>
    public float Field => (float)(Direction * Speed * NominalWidthsPerSecond);

    /// <summary>How far that drift has carried the injection points so far, in plane
    /// widths, wrapped to 0..1 — to be added to each point's own position. Holds still
    /// while split.</summary>
    public float Offset => AudioMath.SanitizePhase(_offset);

    private int Direction => Mode switch
    {
        FlowBiasMode.Right => 1,
        FlowBiasMode.Left => -1,
        _ => 0,
    };

    /// <summary>Left-and-right emission speed at the injection points, as a multiple of
    /// the nominal: the speed while split, 0 otherwise.</summary>
    public float Split => Mode == FlowBiasMode.Split ? Speed : 0f;

    /// <param name="energy">0..1 level (AudioLite energy): low in a slow section.</param>
    /// <param name="tension">0..1 build accumulation from the section tracker.</param>
    /// <param name="dropIn">Seconds until the next known drop, or negative for none.</param>
    /// <param name="dropFired">True on the hop a drop lands.</param>
    public void Advance(double dt, double energy, double tension, double dropIn, bool dropFired)
    {
        if (dropFired)
        {
            Mode = Mode switch
            {
                FlowBiasMode.Right => FlowBiasMode.Left,
                FlowBiasMode.Left => FlowBiasMode.Split,
                _ => FlowBiasMode.Right,
            };
        }
        if (!(dt > 0) || !double.IsFinite(dt))
        {
            return;
        }
        double level = double.IsFinite(energy) ? Math.Clamp(energy, 0, 1) : 0;
        double build = BuildOf(tension, dropIn);
        Build = (float)build;
        // Squared: a ramp that starts gently and arrives steeply.
        double target = (SlowSpeed + (1 - SlowSpeed) * level) * (1 + (BuildGain - 1) * build * build);
        _speed += (target - _speed) * (1 - Math.Exp(-dt / SpeedSeconds));

        _offset += Direction * Math.Clamp(_speed, 0, BuildGain) * NominalWidthsPerSecond * dt;
        _offset -= Math.Floor(_offset);
    }
}
