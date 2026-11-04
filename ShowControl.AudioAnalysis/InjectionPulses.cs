using System;

namespace ShowControl.AudioAnalysis;

/// <summary>
/// Turns each injection band's moment-to-moment bass level into one-shot pulses: a
/// band whose level rises past usual while it is idle FIRES, and the pulse then runs
/// its whole course — an attack, a hold, a release, all raised-cosine so nothing
/// steps — before the band can fire again. The level during a pulse cannot cut the
/// pulse short, stretch it, or start another. So the fluid answers a hit with one
/// complete movement and the next hit with the next, rather than tracking every
/// flicker of the level as it did when the shader injected by the live spectrum.
/// <para>One gate on firing: the level must stand <see cref="AnalysisSettings.PulseTriggerRatio"/>
/// above the band's usual. There is no outright level it must reach — an absolute floor
/// was tried and taken out, because it made the quietest material unreactive however
/// the ratio went; how far a quiet room is lifted is the auto gain's business
/// (<see cref="AnalysisSettings.AutoGainFloor"/>), not this class's.</para>
/// <para>A pulse STIRS: its envelope goes out as <see cref="Turbulence"/>, which the
/// shader spends on a smooth, gentle churning of the flow. It injects no dye.
/// Injecting dye on every hit was tried, scaled by level and then at one fixed size,
/// and either way the beat read as a steady pumping of dye — the hits are better
/// spent moving what is there, with new dye kept for the moments that earn it.</para>
/// <para>Those moments are the ACCENT: one envelope of dye at full from every band at
/// once, in <see cref="Pulses"/>, for a hit at the loudest any band has been over the
/// last <see cref="AnalysisSettings.PulseReferenceSeconds"/> — the big drop, measured
/// against each band's reference, which rises at once, eases down when its peak
/// leaves the window, and never falls below the floor. Once spent it is not available
/// again for <see cref="AnalysisSettings.PulseAccentSeconds"/>, so however the track
/// is limited there is at most one accent in that time. An accent stirs as well.</para>
/// <para>A pad PRESS (<see cref="Advance"/>'s <c>pressedDiscs</c>) is the operator's flood:
/// the same full envelope of dye, every press, with no rest — it is its own envelope,
/// not the accent's, so the audio's one-in-three-minutes is neither spent by a press
/// nor withheld from one — and it rises at once (<see cref="PressAttackSeconds"/>)
/// where the accent eases in. It comes from the PAD'S OWN DISC (every pad has one;
/// see InjectionDiscs in Graphics), so pressing another pad floods another point and
/// two pads held flood two. A press while its disc's flood is running retriggers it:
/// at the top, it stays there for a whole hold again; falling, it snaps back to the
/// top. A pad HELD is a different ask: after <see cref="HoldDelaySeconds"/> its disc injects at
/// <see cref="HoldLevel"/> of full for as long as it is held, eased in and out, under
/// whatever the envelopes are doing — the operator's way of keeping the fluid fed
/// through a passage the audio would not.</para>
/// <para>Under the accents there is always a BASELINE of injection, so the fluid keeps
/// moving through material too quiet to fire, and an accent rises from it rather
/// than from nothing. It is normalized per band: <see cref="AnalysisSettings.PulseBaseline"/>
/// when the band is at its average level of the last <see cref="AnalysisSettings.PulseReferenceSeconds"/>,
/// up to twice that when it is well above the average and down to a quarter when it
/// is silent, following the band's level against that average through a smoothing
/// (fast up, slow down) so it breathes with the music rather than beating.</para>
/// </summary>
internal sealed class InjectionPulses
{
    public const int BandCount = InjectionHistory.BandCount;

    internal const double AttackSeconds = 0.64;
    internal const double HoldSeconds = 1.76;
    internal const double ReleaseSeconds = 2.0;
    internal const double PulseSeconds = AttackSeconds + HoldSeconds + ReleaseSeconds;
    /// <summary>The press's flood rises in one hop, not the accent's attack: it is a
    /// burst, and a ramp into the colour was exactly what it should not be.</summary>
    internal const double PressAttackSeconds = 0.01;
    internal const double PressSeconds = PressAttackSeconds + HoldSeconds + ReleaseSeconds;
    /// <summary>How quickly the reference eases down after its peak expires.</summary>
    internal const double ReferenceFallSeconds = 3.0;
    /// <summary>A held pad injects after this long ...</summary>
    internal const double HoldDelaySeconds = 0.5;
    /// <summary>... at this much of full ...</summary>
    internal const double HoldLevel = 1.0;
    /// <summary>... eased in and out with this time constant.</summary>
    internal const double HoldRampSeconds = 0.125;
    /// <summary>Smoothing of each band's relative level for the baseline.</summary>
    internal const double RelativeAttackSeconds = 0.3;
    internal const double RelativeReleaseSeconds = 2.0;
    /// <summary>The baseline's range as a multiple of its value at the average level:
    /// this at silence ...</summary>
    internal const double BaselineQuietFraction = 0.25;
    /// <summary>... and this at or above twice the average.</summary>
    internal const double BaselineLoudFraction = 2.0;
    /// <summary>The least the average and the reference can be — a guard on the
    /// divisions, far below any level the auto gain lets through, not a sensitivity.</summary>
    private const double Epsilon = 1e-6;

    private readonly double _triggerRatio;
    private readonly double _baseline;
    private readonly double _accentRatio;
    private readonly double _accentSeconds;

    private readonly double[] _age = new double[BandCount];        // seconds into the pulse; < 0 idle
    private readonly double[] _relative = new double[BandCount];   // smoothed level against the average, 0..BaselineLoudFraction
    private double _accentAge = -1;                                // seconds into the accent; < 0 idle
    private double _accentRest;                                    // seconds until the next accent may fire
    private readonly double[] _pressAge = new double[BandCount]; // per disc: seconds into its press's flood; < 0 idle
    private readonly double[] _heldFor = new double[BandCount];  // per disc: seconds its pad has been held
    private readonly double[] _hold = new double[BandCount];     // per disc: the held pad's injection, eased
    private readonly float[] _pulses = new float[BandCount];
    private float _turbulence;

    // Each band's loudest level per second of the window, and the eased reference;
    // and each band's level summed per second, with the hops in each, for the average.
    private readonly float[,] _slotPeaks;
    private readonly double[,] _slotSums;
    private readonly int[] _slotHops;
    private readonly double[] _reference = new double[BandCount];
    private int _slot;
    private double _slotElapsed;

    public InjectionPulses(AnalysisSettings settings)
    {
        _triggerRatio = settings.PulseTriggerRatio;
        _baseline = Math.Clamp(settings.PulseBaseline, 0, 1);
        _accentRatio = Math.Clamp(settings.PulseAccentRatio, 0, 1);
        _accentSeconds = Math.Max(settings.PulseAccentSeconds, 0);
        int slots = Math.Max((int)Math.Ceiling(settings.PulseReferenceSeconds), 1);
        _slotPeaks = new float[BandCount, slots];
        _slotSums = new double[BandCount, slots];
        _slotHops = new int[slots];
        Array.Fill(_age, -1.0);
        Array.Fill(_pressAge, -1.0);
        Array.Fill(_reference, Epsilon);
    }

    /// <summary>Each band's dye injection right now, 0..1: its baseline, plus the
    /// accent's envelope on top while one is running — and its pad's flood, pressed or
    /// held — lowest band first. Overwritten by the next <see cref="Advance"/>.</summary>
    public ReadOnlySpan<float> Pulses => _pulses;

    /// <summary>How loud <paramref name="band"/> is against its average over the window,
    /// smoothed (1 = at the average, capped at <see cref="BaselineLoudFraction"/>) — what
    /// its baseline follows.</summary>
    public float RelativeOf(int band) => (float)_relative[band];

    /// <summary>How hard the hits are stirring the flow right now, 0..1: the envelope
    /// of whichever pulse is strongest, the accent's included.</summary>
    public float Turbulence => _turbulence;

    /// <summary>What counts as a full-strength hit for <paramref name="band"/> right now.</summary>
    public float ReferenceOf(int band) => (float)_reference[band];

    /// <summary>Whether enough time has passed since the last accent for the next hit
    /// at the peak to fire one.</summary>
    public bool AccentReady => _accentRest <= 0;

    /// <param name="ratios">Each band's level relative to usual this hop
    /// (<see cref="InjectionHistory.Ratios"/>).</param>
    /// <param name="levels">Each band's level outright (<see cref="InjectionHistory.Levels"/>).</param>
    /// <param name="pressedDiscs">One bit per disc whose pad was pressed this hop: that
    /// disc floods, whatever the audio is doing and whether or not the accent is resting.</param>
    /// <param name="heldDiscs">One bit per disc with a pad down right now; held past
    /// <see cref="HoldDelaySeconds"/> the disc injects at <see cref="HoldLevel"/>.</param>
    public void Advance(ReadOnlySpan<float> ratios, ReadOnlySpan<float> levels, double dt,
        int pressedDiscs = 0, int heldDiscs = 0)
    {
        if (!(dt > 0) || !double.IsFinite(dt))
        {
            return;
        }

        // Advance the reference window: a slot that comes round again starts empty,
        // which is how an old peak expires.
        int slots = _slotPeaks.GetLength(1);
        _slotElapsed += dt;
        while (_slotElapsed >= 1.0)
        {
            _slotElapsed -= 1.0;
            _slot = (_slot + 1) % slots;
            _slotHops[_slot] = 0;
            for (int band = 0; band < BandCount; band++)
            {
                _slotPeaks[band, _slot] = 0f;
                _slotSums[band, _slot] = 0;
            }
        }
        _slotHops[_slot]++;
        int windowHops = 0;
        for (int s = 0; s < slots; s++)
        {
            windowHops += _slotHops[s];
        }

        _accentRest = Math.Max(_accentRest - dt, 0);
        if (_accentAge >= 0)
        {
            _accentAge += dt;
            if (_accentAge >= PulseSeconds)
            {
                _accentAge = -1;
            }
        }

        double padFloodMax = 0;
        Span<double> padFlood = stackalloc double[BandCount];
        for (int disc = 0; disc < BandCount; disc++)
        {
            if (_pressAge[disc] >= 0)
            {
                _pressAge[disc] += dt;
                if (_pressAge[disc] >= PressSeconds)
                {
                    _pressAge[disc] = -1;
                }
            }
            if ((pressedDiscs & (1 << disc)) != 0)
            {
                _pressAge[disc] = 0;
            }
            bool held = (heldDiscs & (1 << disc)) != 0;
            _heldFor[disc] = held ? _heldFor[disc] + dt : 0;
            double holdTarget = _heldFor[disc] >= HoldDelaySeconds ? HoldLevel : 0;
            _hold[disc] += (holdTarget - _hold[disc]) * (1 - Math.Exp(-dt / HoldRampSeconds));
            padFlood[disc] = Math.Max(Envelope(_pressAge[disc], PressAttackSeconds), _hold[disc]);
            padFloodMax = Math.Max(padFloodMax, padFlood[disc]);
        }

        double turbulence = 0;
        for (int band = 0; band < BandCount; band++)
        {
            double ratio = band < ratios.Length && float.IsFinite(ratios[band]) ? Math.Max(ratios[band], 0) : 0;
            double level = band < levels.Length && float.IsFinite(levels[band]) ? Math.Max(levels[band], 0) : 0;

            if (level > _slotPeaks[band, _slot])
            {
                _slotPeaks[band, _slot] = (float)level;
            }
            _slotSums[band, _slot] += level;
            double windowPeak = 0;
            double windowSum = 0;
            for (int s = 0; s < slots; s++)
            {
                windowPeak = Math.Max(windowPeak, _slotPeaks[band, s]);
                windowSum += _slotSums[band, s];
            }
            double average = Math.Max(windowSum / windowHops, Epsilon);
            double target = Math.Max(windowPeak, Epsilon);
            if (target > _reference[band])
            {
                _reference[band] = target;                        // louder than anything in the window: at once
            }
            else
            {
                _reference[band] += (target - _reference[band]) * (1 - Math.Exp(-dt / ReferenceFallSeconds));
            }
            double hit = Math.Min(level / _reference[band], 1.0);
            double relative = Math.Min(level / average, BaselineLoudFraction);
            double tau = relative > _relative[band] ? RelativeAttackSeconds : RelativeReleaseSeconds;
            _relative[band] += (relative - _relative[band]) * (1 - Math.Exp(-dt / tau));
            bool firing = ratio >= _triggerRatio && level > 0;

            if (_age[band] >= 0)
            {
                _age[band] += dt;
                if (_age[band] >= PulseSeconds)
                {
                    _age[band] = -1;
                }
            }
            if (_age[band] < 0 && firing)
            {
                _age[band] = 0;
            }
            // A hit at the peak fires the accent, if it is not resting.
            if (firing && hit >= _accentRatio && _accentRest <= 0)
            {
                _accentAge = 0;
                _accentRest = _accentSeconds;
            }
            turbulence = Math.Max(turbulence, Envelope(_age[band]));
        }

        double accent = Envelope(_accentAge, AttackSeconds);
        turbulence = Math.Max(turbulence, Math.Max(accent, padFloodMax));
        for (int band = 0; band < BandCount; band++)
        {
            double baseline = _baseline * Math.Max(_relative[band], BaselineQuietFraction);
            double flood = Math.Max(accent, padFlood[band]);
            double pulse = baseline + (1 - baseline) * flood;
            _pulses[band] = AudioMath.Sanitize((float)pulse, 0f, 1f);
        }
        _turbulence = AudioMath.Sanitize((float)turbulence, 0f, 1f);
    }

    private static double Envelope(double age, double attack = AttackSeconds)
    {
        if (age < 0 || age >= attack + HoldSeconds + ReleaseSeconds)
        {
            return 0;
        }
        if (age < attack)
        {
            return 0.5 - 0.5 * Math.Cos(Math.PI * age / attack);
        }
        if (age < attack + HoldSeconds)
        {
            return 1;
        }
        return 0.5 + 0.5 * Math.Cos(Math.PI * (age - attack - HoldSeconds) / ReleaseSeconds);
    }
}
