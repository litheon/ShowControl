namespace ShowControl.AudioAnalysis;

/// <summary>
/// Decides WHERE each slice of the spectrum enters a visualization, from what that slice
/// has done over the last minute. The bass (60–300 Hz), which is what times injection, is cut into
/// <see cref="BandCount"/> log-spaced bands, and each band's site is a point anywhere in
/// the visualization's plane. Across, it is the band's home position (plain frequency
/// order) displaced by its accumulated activity over a sliding <see cref="WindowSeconds"/>
/// window; down, it is displaced from the top by its activity over the most recent
/// <see cref="RecentSeconds"/> of that window. Both are folded back and forth between
/// the walls, and the lower and upper halves of the bands travel across in OPPOSITE
/// directions: activity is
/// largely shared between bands, and with every band heading the same way the whole set
/// migrated together and left one side of the plane with no injector at all. Travelling
/// against each other, the part of the motion the bands share moves each band and its
/// mirror-image partner (lowest with highest, and so on inward) to mirrored positions,
/// so both sides stay populated. A band therefore moves across at a speed
/// set by the difference between what it is doing now and what it was doing a minute ago,
/// and down by the difference from half a minute ago — two different readings of the same minute, so the site traces a
/// genuinely two-dimensional path. The layout is a running record of the track's last
/// sixty seconds, never repeats between tracks or sections, and bands cross and overlap —
/// but it evolves slowly and continuously (nothing teleports) and after a minute of
/// silence every band is back home, in a row along the top.
///
/// Activity is each band's level relative to its own usual level (fast attack, slow
/// release), so a band travels when it is busy for itself — bass does not out-run the
/// treble purely by spectral slope.
///
/// Clock-free like the rest of the analysis core: time arrives as dt with each hop. The
/// window is exact rather than an exponential average, which is the point — an EMA only
/// knows the present level, a window also knows what is leaving it.
/// </summary>
internal sealed class InjectionHistory
{
    /// <summary>Number of bands, and of published sites (two floats each). Must match INJ_BANDS in
    /// fluid.comp and GraphicsHost.InjectionSites — three places that must agree.</summary>
    public const int BandCount = 5;

    /// <summary>Length of the history window.</summary>
    public const double WindowSeconds = 60.0;

    /// <summary>The recent end of the window, which places a band vertically.</summary>
    public const double RecentSeconds = 30.0;

    // The bass, which is what times the injection — fluid.comp's MIN_BIN up to its
    // BASS_SPLIT at 48 kHz / 2048. Each band is one injection point's slice of it.
    private const double LowestHz = 60.0;
    private const double HighestHz = 300.0;

    /// <summary>Plane widths a band travels across per second of full activity. A band
    /// that is half-active for the whole window ends more than a width from home, so
    /// where it lands has little to do with its frequency — but this is also the fastest
    /// a site can ever move (widths per second), and it is kept low so the layout
    /// evolves rather than animates.</summary>
    internal const double TravelPerActiveSecond = 0.04;

    /// <summary>Plane heights a band travels down per second of full activity in the
    /// recent window: half-active for all of it lands three quarters of the way down.</summary>
    internal const double DescentPerActiveSecond = 0.05;

    /// <summary>Time constant of each of the two smoothing stages between the window
    /// sums and the published sites. Activity arrives in beats, so the raw sums advance in
    /// lurches — a bar that steps forward on every kick. Two stages rather than one so the
    /// site's velocity is continuous as well as its position; the lag this adds (about
    /// twice the constant) is nothing against a sixty-second window.</summary>
    internal const double GlideSeconds = 2.5;

    /// <summary>The same, for travel across. Longer: sideways the bars should follow the
    /// shape of the minute, not answer each phrase — at the vertical constant they read
    /// as twitchy. This slows how quickly a site responds, not how far it ranges.</summary>
    internal const double GlideAcrossSeconds = 3.0;

    private const double UsualAttackSeconds = 1.2;
    private const double UsualReleaseSeconds = 12.0;
    private const double UsualFloor = 2.0e-4;
    private const double ActivityGain = 0.5;

    private readonly int[] _bandFirstBin;
    private readonly int[] _bandLastBin;
    private readonly double[] _usual = new double[BandCount];
    private readonly float[] _ratios = new float[BandCount];
    private readonly float[] _levels = new float[BandCount];
    private readonly double[] _windowSum = new double[BandCount];   // activity·seconds in the window
    private readonly double[] _recentSum = new double[BandCount];   // the same, recent end only
    private readonly float[] _sites = new float[BandCount * 2];
    // Travel across and down, before folding: [stage, band]. Smoothed
    // unfolded, so a site glides through a wall bounce instead of cutting the corner.
    private readonly double[,] _glideAcross = new double[2, BandCount];
    private readonly double[,] _glideDown = new double[2, BandCount];

    // Ring of per-hop contributions (activity·dt per band, plus the hop's dt), so the hop
    // leaving the window can be subtracted exactly.
    private readonly float[] _ring;
    private readonly double[] _ringDt;
    private int _head;
    private int _count;
    private double _windowDuration;
    private int _recentCount;            // newest entries of the ring inside the recent window
    private double _recentDuration;

    public InjectionHistory(AnalysisSettings settings)
    {
        int binCount = settings.FftWindowSize / 2;
        double binHz = (double)settings.SampleRate / settings.FftWindowSize;
        double lowBin = LowestHz / binHz;
        double ratio = HighestHz / LowestHz;
        _bandFirstBin = new int[BandCount];
        _bandLastBin = new int[BandCount];
        for (int band = 0; band < BandCount; band++)
        {
            int first = (int)Math.Round(lowBin * Math.Pow(ratio, (double)band / BandCount));
            int last = (int)Math.Round(lowBin * Math.Pow(ratio, (double)(band + 1) / BandCount)) - 1;
            first = Math.Clamp(first, 1, binCount - 1);
            _bandFirstBin[band] = first;
            _bandLastBin[band] = Math.Clamp(last, first, binCount - 1);
        }

        // A minute of hops, with headroom for jitter in dt; a full ring evicts early
        // rather than growing.
        double hopSeconds = (double)settings.HopSize / settings.SampleRate;
        int capacity = (int)Math.Ceiling(WindowSeconds / hopSeconds) + 16;
        _ring = new float[capacity * BandCount];
        _ringDt = new double[capacity];

        UpdateSites(0);
    }

    /// <summary>Each band's site, lowest band first: <see cref="BandCount"/> positions
    /// across (the centre of the band's segment, 0..1 left to right) followed by
    /// <see cref="BandCount"/> depths (0 = top, 1 = as far down as injection reaches).
    /// Overwritten by the next <see cref="Advance"/>.</summary>
    public ReadOnlySpan<float> Sites => _sites;

    /// <summary>Each band's level this hop relative to its usual level (1 = usual, 2 =
    /// twice usual), lowest band first — what <see cref="InjectionPulses"/> fires on.
    /// Overwritten by the next <see cref="Advance"/>.</summary>
    public ReadOnlySpan<float> Ratios => _ratios;

    /// <summary>Each band's level this hop as it is — the loudest bin magnitude in the
    /// band, after the auto gain — lowest band first. Overwritten by the next
    /// <see cref="Advance"/>.</summary>
    public ReadOnlySpan<float> Levels => _levels;

    /// <summary>Where band <paramref name="band"/> sits across the plane with no history:
    /// plain frequency order, each band centred in its own share of the width. Its
    /// home depth is 0, the top.</summary>
    public static float HomeSite(int band) => (band + 0.5f) / BandCount;

    public void Advance(ReadOnlySpan<float> magnitudes, double dt)
    {
        if (!(dt > 0) || !double.IsFinite(dt))
        {
            return;
        }

        int capacity = _ringDt.Length;
        while (_count > 0 && (_count == capacity || _windowDuration + dt > WindowSeconds))
        {
            Evict();
        }
        while (_recentCount > 0 && _recentDuration + dt > RecentSeconds)
        {
            EvictRecent();
        }

        int slot = (_head + _count) % capacity;
        var entry = _ring.AsSpan(slot * BandCount, BandCount);
        for (int band = 0; band < BandCount; band++)
        {
            double level = 0;
            int last = Math.Min(_bandLastBin[band], magnitudes.Length - 1);
            for (int bin = _bandFirstBin[band]; bin <= last; bin++)
            {
                level = Math.Max(level, magnitudes[bin]);
            }
            if (!double.IsFinite(level))
            {
                level = 0;
            }

            double tau = level > _usual[band] ? UsualAttackSeconds : UsualReleaseSeconds;
            _usual[band] += (level - _usual[band]) * (1 - Math.Exp(-dt / tau));
            _levels[band] = (float)level;
            double ratio = level / Math.Max(_usual[band], UsualFloor);
            _ratios[band] = AudioMath.Sanitize((float)ratio, 0f, float.MaxValue);
            double activity = 1 - Math.Exp(-ratio * ActivityGain);

            float contribution = (float)(activity * dt);
            entry[band] = contribution;
            _windowSum[band] += contribution;
            _recentSum[band] += contribution;
        }
        _ringDt[slot] = dt;
        _windowDuration += dt;
        _count++;
        _recentDuration += dt;
        _recentCount++;

        UpdateSites(dt);
    }

    private void EvictRecent()
    {
        int index = (_head + _count - _recentCount) % _ringDt.Length;
        var oldest = _ring.AsSpan(index * BandCount, BandCount);
        for (int band = 0; band < BandCount; band++)
        {
            _recentSum[band] -= oldest[band];
        }
        _recentDuration -= _ringDt[index];
        _recentCount--;
        if (_recentCount == 0)
        {
            Array.Clear(_recentSum);
            _recentDuration = 0;
        }
    }

    private void Evict()
    {
        if (_recentCount == _count)
        {
            EvictRecent();
        }
        var oldest = _ring.AsSpan(_head * BandCount, BandCount);
        for (int band = 0; band < BandCount; band++)
        {
            _windowSum[band] -= oldest[band];
        }
        _windowDuration -= _ringDt[_head];
        _head = (_head + 1) % _ringDt.Length;
        _count--;
        if (_count == 0)
        {
            // Empty window: drop whatever rounding the running sums have collected.
            Array.Clear(_windowSum);
            _windowDuration = 0;
        }
    }

    private void UpdateSites(double dt)
    {
        double glide = dt > 0 ? 1 - Math.Exp(-dt / GlideSeconds) : 0;
        double glideAcross = dt > 0 ? 1 - Math.Exp(-dt / GlideAcrossSeconds) : 0;
        for (int band = 0; band < BandCount; band++)
        {
            _glideAcross[0, band] += (Math.Max(_windowSum[band], 0) * TravelPerActiveSecond - _glideAcross[0, band]) * glideAcross;
            _glideAcross[1, band] += (_glideAcross[0, band] - _glideAcross[1, band]) * glideAcross;
            _glideDown[0, band] += (Math.Max(_recentSum[band], 0) * DescentPerActiveSecond - _glideDown[0, band]) * glide;
            _glideDown[1, band] += (_glideDown[0, band] - _glideDown[1, band]) * glide;

            // Travel in units of the range the band's centre can cover, folded like a
            // ball between two walls so the site stays continuous at both ends.
            // The lower bands travel right, the upper ones left, so a band and its mirror
            // partner always head opposite ways (see the class comment).
            double u = (double)band / (BandCount - 1)
                     + (band < (BandCount + 1) / 2 ? 1 : -1) * _glideAcross[1, band];
            _sites[band] = AudioMath.Sanitize(
                (float)((0.5 + Fold(u) * (BandCount - 1)) / BandCount), 0f, 1f);


            double v = _glideDown[1, band];
            _sites[BandCount + band] = AudioMath.Sanitize((float)Fold(v), 0f, 1f);
        }
    }

    private static double Fold(double u) => 1 - Math.Abs(1 - ((u % 2.0) + 2.0) % 2.0);
}
