namespace ShowControl.AudioAnalysis;

/// <summary>
/// Decides WHAT COLOR each band of the spectrum injects, as a small palette with a story
/// instead of every hue at once. The bands fall into three families — lows, mids, highs —
/// and at any moment the families hold three hues spaced around a key hue, each band a
/// slight shade off its family's so a family shimmers rather than reading as one flat
/// color. The music moves the palette:
///
/// - The key hue turns slowly with the music's energy, and stands still in silence.
/// - Tension closes the palette: as a build accumulates the three families converge on
///   the key hue, until the whole picture is shades of one color.
/// - The drop opens it again and turns the key by the golden angle, so the hit lands as
///   three fresh, widely separated colors — and no two drops in a night share a palette.
///
/// Clock-free like the rest of the analysis core: time arrives as dt with each hop.
/// </summary>
internal sealed class InjectionPalette
{
    /// <summary>Lows, mids, highs.</summary>
    public const int FamilyCount = 3;

    /// <summary>How far apart neighbouring families sit with no tension: a triad.</summary>
    internal const double OpenSeparation = 1.0 / 3.0;

    /// <summary>The spread of shades inside one family, in turns of the wheel, end to
    /// end. Narrow enough that a family is plainly one color.</summary>
    internal const double FamilyShadeWidth = 0.08;

    /// <summary>Turns the key takes at each drop — the golden angle, which never lands
    /// on a hue it has used recently.</summary>
    internal const double DropTurn = 0.381966;

    /// <summary>Turns per second the key drifts at full energy (one revolution in about
    /// three minutes).</summary>
    internal const double DriftPerSecond = 1.0 / 180.0;

    // The drop's turn is eased, not cut: fast enough to belong to the hit, slow enough
    // that the fluid shows the color changing rather than a splice.
    private const double TurnSeconds = 0.25;

    /// <summary>How the marbling eases in or out when a drop switches it.</summary>
    internal const double MarbleFadeSeconds = 2.0;

    private readonly float[] _hues = new float[InjectionHistory.BandCount];
    private double _key;
    private double _pendingTurn;
    private bool _marbleOn = true;
    private double _marble = 1.0;

    public InjectionPalette()
    {
        Advance(0, 0, 0, false);
    }

    /// <summary>The hue each band injects, 0..1 turns, lowest band first. Overwritten by
    /// the next <see cref="Advance"/>.</summary>
    public ReadOnlySpan<float> Hues => _hues;

    /// <summary>How much the dye is marbled (fine stripes of neighbouring hues folded
    /// through each shape) rather than shown in its plain palette colour, 0..1. Each
    /// drop switches between the two treatments, eased over
    /// <see cref="MarbleFadeSeconds"/>, so one section is plain and the next is marbled.</summary>
    public float Marble => AudioMath.Sanitize((float)_marble, 0f, 1f);

    /// <summary>When set, drops no longer switch the marbling off: it stays fully on.
    /// For tuning the marble constants, where a plain section hides what is being
    /// adjusted.</summary>
    public bool MarbleAlways { get; set; }

    /// <summary>The hue the palette is currently built around, 0..1 turns.</summary>
    public float KeyHue => AudioMath.SanitizePhase(_key);

    public static int FamilyOf(int band) => band * FamilyCount / InjectionHistory.BandCount;

    /// <param name="energy">0..1 level (AudioLite energy).</param>
    /// <param name="tension">0..1 build accumulation from the section tracker.</param>
    /// <param name="dropFired">True on the hop a drop lands.</param>
    public void Advance(double dt, double energy, double tension, bool dropFired)
    {
        if (dropFired)
        {
            _pendingTurn += DropTurn;
            _marbleOn = !_marbleOn;
        }
        if (MarbleAlways) _marbleOn = true;
        if (dt > 0 && double.IsFinite(dt))
        {
            double turned = _pendingTurn * (1 - Math.Exp(-dt / TurnSeconds));
            _pendingTurn -= turned;
            double drift = DriftPerSecond * (double.IsFinite(energy) ? Math.Clamp(energy, 0, 1) : 0) * dt;
            _key += turned + drift;
            _key -= Math.Floor(_key);
            _marble += ((_marbleOn ? 1.0 : 0.0) - _marble) * (1 - Math.Exp(-dt / MarbleFadeSeconds));
        }

        double open = 1 - (double.IsFinite(tension) ? Math.Clamp(tension, 0, 1) : 0);
        double separation = OpenSeparation * open;
        for (int band = 0; band < _hues.Length; band++)
        {
            // The families need not be the same size: shade by the band's place among
            // its own family's bands.
            int family = FamilyOf(band);
            int first = band;
            while (first > 0 && FamilyOf(first - 1) == family)
            {
                first--;
            }
            int size = band - first + 1;
            while (first + size < _hues.Length && FamilyOf(first + size) == family)
            {
                size++;
            }
            double shade = ((band - first + 0.5) / size - 0.5) * FamilyShadeWidth;
            _hues[band] = AudioMath.SanitizePhase(_key + (family - 1) * separation + shade);
        }
    }
}
