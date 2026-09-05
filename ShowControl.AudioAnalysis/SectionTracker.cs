using ShowControl.AudioAnalysis.Dsp;

namespace ShowControl.AudioAnalysis;

public enum Section
{
    Steady,
    Build,
    Gap,
    Drop,
}

/// <summary>Per-hop inputs to the section tracker. Raw where possible — the tracker owns
/// its own trend statistics so tests can feed hand-built sequences.</summary>
public readonly record struct SectionFeatures(
    double Time,
    float Rms,
    float Flux,
    float CentroidHz,
    float OnsetRate,
    float HighBand,
    float TempoConfidence);

/// <summary>Section tracker tunables. Time-like values in seconds.</summary>
public sealed record SectionSettings
{
    /// <summary>Fast/slow trend pair: a build is features rising over bars, read as the
    /// fast average pulling above the slow one.</summary>
    public double FastTrendSeconds { get; init; } = 1.2;
    public double SlowTrendSeconds { get; init; } = 7.0;

    /// <summary>Build confidence accumulates over bars — no threshold triggers.</summary>
    public double ConfidenceTau { get; init; } = 3.0;

    /// <summary>Confidence below which rising trends are not yet called a build.</summary>
    public double BuildEnterConfidence { get; init; } = 0.30;

    /// <summary>Confidence required before a broadband collapse commits as a pre-drop gap.
    /// The gap is the point of no return, so it demands a confident build first.</summary>
    public double CommitConfidence { get; init; } = 0.55;

    /// <summary>RMS below this fraction of its slow average reads as broadband collapse.</summary>
    public double GapCollapseRatio { get; init; } = 0.30;

    /// <summary>RMS above this fraction of its slow average reads as the drop landing.</summary>
    public double DropReturnRatio { get; init; } = 0.65;

    /// <summary>How long past the predicted hit to wait before declaring the drop missed.</summary>
    public double HitGraceSeconds { get; init; } = 1.2;

    /// <summary>Tension leak time constant when a predicted drop never lands — reads as an
    /// intentional swell, never a snap.</summary>
    public double FailedLeakTau { get; init; } = 3.0;

    /// <summary>Settle time for the drop's motion/spread pulse.</summary>
    public double DropSettleSeconds { get; init; } = 1.6;

    /// <summary>Output smoothing for motion/spread (bypassed at the drop, which snaps on
    /// purpose).</summary>
    public double OutputSmoothingSeconds { get; init; } = 0.06;

    /// <summary>Scheduled transitions (predicted or announced hits) fire early by this much
    /// so the light lands with the sound. Live-detected transitions cannot be advanced.</summary>
    public double LeadSeconds { get; init; } = 0;
}

/// <summary>
/// The section state machine behind tension/motion/spread. Two tiers, announced-first:
/// when the beat metadata carries confident section markers, builds and drops are
/// *scheduled* from them (tension ramps to a known hit, fired <see cref="SectionSettings.LeadSeconds"/>
/// early); otherwise they are *inferred* — build confidence accumulated over bars from
/// rising level/centroid/onset-density/high-band trends, gap committed on broadband
/// collapse after a confident build, hit time from the PLL's next downbeat, and a missed
/// drop leaking tension gracefully instead of snapping.
///
/// Clock-free and single-consumer like the rest of the engine: one
/// <see cref="Advance"/> per hop on the analysis thread.
/// </summary>
public sealed class SectionTracker
{
    private readonly SectionSettings _s;
    private readonly Func<double, double> _nextDownbeat;

    private readonly EnvelopeFollower _fastRms;
    private readonly EnvelopeFollower _slowRms;
    private readonly EnvelopeFollower _fastCentroid;
    private readonly EnvelopeFollower _slowCentroid;
    private readonly EnvelopeFollower _fastOnsets;
    private readonly EnvelopeFollower _slowOnsets;
    private readonly EnvelopeFollower _fastHigh;
    private readonly EnvelopeFollower _slowHigh;

    private IReadOnlyList<SectionMarker> _announced = Array.Empty<SectionMarker>();

    private double _confidence;
    private double _tension;
    private double _motion = 1;
    private double _spread = 0.5;
    private double _pulse;           // 1 at the hit, decaying through Drop
    private double _hitTime;         // when the committed/announced drop should land (already lead-advanced)
    private bool _leaking;           // a predicted drop failed; tension is swelling away

    public SectionTracker(SectionSettings settings, Func<double, double> nextDownbeat)
    {
        _s = settings;
        _nextDownbeat = nextDownbeat;
        _fastRms = new EnvelopeFollower(_s.FastTrendSeconds, _s.FastTrendSeconds);
        _slowRms = new EnvelopeFollower(_s.SlowTrendSeconds, _s.SlowTrendSeconds);
        _fastCentroid = new EnvelopeFollower(_s.FastTrendSeconds, _s.FastTrendSeconds);
        _slowCentroid = new EnvelopeFollower(_s.SlowTrendSeconds, _s.SlowTrendSeconds);
        _fastOnsets = new EnvelopeFollower(_s.FastTrendSeconds, _s.FastTrendSeconds);
        _slowOnsets = new EnvelopeFollower(_s.SlowTrendSeconds, _s.SlowTrendSeconds);
        _fastHigh = new EnvelopeFollower(_s.FastTrendSeconds, _s.FastTrendSeconds);
        _slowHigh = new EnvelopeFollower(_s.SlowTrendSeconds, _s.SlowTrendSeconds);
    }

    public Section Section { get; private set; } = Section.Steady;
    public float Tension => (float)Math.Clamp(_tension, 0, 1);
    public float Motion => (float)Math.Clamp(_motion, 0, 3);
    public float Spread => (float)Math.Clamp(_spread, 0, 1);
    public float SectionConfidence => (float)Math.Clamp(_confidence, 0, 1);

    /// <summary>Swap in the announced markers (immutable list from the metadata source);
    /// empty when nothing is announced.</summary>
    public void SetAnnouncedSections(IReadOnlyList<SectionMarker>? markers) =>
        _announced = markers ?? Array.Empty<SectionMarker>();

    private bool _seeded;

    public void Advance(in SectionFeatures f, double dt)
    {
        if (dt <= 0 || !double.IsFinite(dt))
        {
            return;
        }

        if (!_seeded)
        {
            // Seed the trend pairs at the first frame: without this, any material starting
            // from a cold engine reads as "everything rising" for the slow tau's duration
            // and fakes a build at every track start.
            _seeded = true;
            _fastRms.Reset(f.Rms);
            _slowRms.Reset(f.Rms);
            _fastCentroid.Reset(f.CentroidHz);
            _slowCentroid.Reset(f.CentroidHz);
            _fastOnsets.Reset(f.OnsetRate);
            _slowOnsets.Reset(f.OnsetRate);
            _fastHigh.Reset(f.HighBand);
            _slowHigh.Reset(f.HighBand);
        }

        _fastRms.Process(f.Rms, dt);
        _slowRms.Process(f.Rms, dt);
        _fastCentroid.Process(f.CentroidHz, dt);
        _slowCentroid.Process(f.CentroidHz, dt);
        _fastOnsets.Process(f.OnsetRate, dt);
        _slowOnsets.Process(f.OnsetRate, dt);
        _fastHigh.Process(f.HighBand, dt);
        _slowHigh.Process(f.HighBand, dt);

        if (!TryAdvanceAnnounced(f.Time, dt))
        {
            AdvanceInferred(f, dt);
        }

        ComposeOutputs(dt);
    }

    /// <summary>Announced-first tier: with confident markers covering "now", builds and
    /// drops are scheduled, not inferred. Returns false to fall through to inference.</summary>
    private bool TryAdvanceAnnounced(double now, double dt)
    {
        // The marker span we are currently inside, then the hit that resolves it — the
        // first drop/chorus marker after the span started, chosen independently of "now"
        // so the build-up branch still sees its hit at the moment it arrives.
        SectionMarker? current = null;
        foreach (var marker in _announced)
        {
            if (marker.Confidence < 0.5f)
            {
                continue;
            }
            if (marker.LocalTime <= now && (current is null || marker.LocalTime > current.Value.LocalTime))
            {
                current = marker;
            }
        }
        if (current is null)
        {
            return false;
        }
        SectionMarker? nextHit = null;
        foreach (var marker in _announced)
        {
            if (marker.Confidence >= 0.5f
                && marker.Kind is SectionKind.Drop or SectionKind.Chorus
                && marker.LocalTime > current.Value.LocalTime
                && (nextHit is null || marker.LocalTime < nextHit.Value.LocalTime))
            {
                nextHit = marker;
            }
        }

        switch (current.Value.Kind)
        {
            case SectionKind.BuildUp when nextHit is not null:
            {
                // Tension is progress toward the known hit; the last stretch is the gap;
                // the hit itself fires here, lead-advanced — while the announced "current"
                // section is still the build-up.
                double hit = nextHit.Value.LocalTime - _s.LeadSeconds;
                if (now >= hit)
                {
                    if (Section != Section.Drop)
                    {
                        FireDrop();
                    }
                    return true;
                }
                double span = Math.Max(hit - current.Value.LocalTime, 0.5);
                double progress = Math.Clamp(1 - (hit - now) / span, 0, 1);
                _tension = Math.Max(_tension, progress);
                _confidence = current.Value.Confidence;
                Section = hit - now < span * 0.15 ? Section.Gap : Section.Build;
                _hitTime = hit;
                _leaking = false;
                return true;
            }
            case SectionKind.BuildUp:
                // A build with no announced resolution: ramp on the marker's word alone
                // and let the inferred tier's gap/drop logic finish it.
                _tension = Math.Min(1, _tension + dt / 8.0);
                _confidence = current.Value.Confidence;
                Section = Section.Build;
                return false;

            case SectionKind.Drop:
            case SectionKind.Chorus when Section is Section.Build or Section.Gap or Section.Drop:
            {
                // Usually already fired (lead-advanced) from the build-up branch above;
                // this catches a hit marker with no announced build in front of it.
                double sinceHit = now - (current.Value.LocalTime - _s.LeadSeconds);
                if (sinceHit >= 0 && sinceHit < 0.5 && Section != Section.Drop)
                {
                    FireDrop();
                }
                return Section == Section.Drop; // let the pulse settle via ComposeOutputs
            }

            default:
                return false; // Intro/Verse/Outro etc.: the inferred tier decides
        }
    }

    private void AdvanceInferred(in SectionFeatures f, double dt)
    {
        double rising =
            (Trend(_fastRms.Value, _slowRms.Value)
           + Trend(_fastCentroid.Value, _slowCentroid.Value)
           + Trend(_fastOnsets.Value, _slowOnsets.Value)
           + Trend(_fastHigh.Value, _slowHigh.Value)) / 4;

        switch (Section)
        {
            case Section.Steady:
            case Section.Build:
            {
                // Confidence accumulates over bars; sustained rising trends push it up,
                // flat or falling material bleeds it away.
                _confidence += (rising - 0.12) * (dt / _s.ConfidenceTau);
                _confidence = Math.Clamp(_confidence, 0, 1);

                if (_leaking)
                {
                    _tension *= Math.Exp(-dt / _s.FailedLeakTau);
                    if (_tension < 0.05)
                    {
                        _leaking = false;
                    }
                }
                else
                {
                    // Tension follows confidence up quickly and down slowly.
                    double target = Math.Pow(_confidence, 1.3);
                    double tau = target > _tension ? 0.6 : 4.0;
                    _tension += (target - _tension) * (1 - Math.Exp(-dt / tau));
                }

                Section = _confidence >= _s.BuildEnterConfidence ? Section.Build : Section.Steady;

                // Gap commit: broadband collapse right after a confident build. Point of
                // no return — freeze everything and book the hit on the next downbeat.
                // Instantaneous RMS, not the fast trend: the cut must register within a
                // hop, before the falling trends bleed the build confidence away.
                if (_confidence >= _s.CommitConfidence
                    && _slowRms.Value > 1e-4
                    && f.Rms < _s.GapCollapseRatio * _slowRms.Value)
                {
                    Section = Section.Gap;
                    _tension = 1;
                    _hitTime = _nextDownbeat(f.Time) - _s.LeadSeconds;
                    _leaking = false;
                }
                break;
            }

            case Section.Gap:
            {
                // Tension held at 1, all decay suppressed. The drop is the energy coming
                // back; the schedule only gates the failure path.
                _tension = 1;
                if (f.Rms > _s.DropReturnRatio * _slowRms.Value)
                {
                    FireDrop();
                }
                else if (f.Time > _hitTime + _s.HitGraceSeconds)
                {
                    // The predicted drop never landed: swell away instead of snapping.
                    Section = Section.Steady;
                    _confidence = 0;
                    _leaking = true;
                }
                break;
            }

            case Section.Drop:
            {
                if (_pulse < 0.08)
                {
                    Section = Section.Steady;
                }
                break;
            }
        }
    }

    private void FireDrop()
    {
        Section = Section.Drop;
        _tension = 0;
        _confidence = 0;
        _pulse = 1;
        _leaking = false;
        // The snap is deliberate: bypass the output smoothing for the instant itself.
        _motion = 3;
        _spread = 1;
    }

    private void ComposeOutputs(double dt)
    {
        _pulse *= Math.Exp(-dt / _s.DropSettleSeconds);

        (double motionTarget, double spreadTarget) = Section switch
        {
            // ~1 in a groove, easing off as a build gathers.
            Section.Steady => (1.0 - 0.2 * _tension, 0.5 - 0.35 * _tension),
            Section.Build => (1.0 - 0.6 * _tension, 0.5 - 0.35 * _tension),
            // ~0 during the gap: the freeze before the explosion.
            Section.Gap => (0.08, 0.12),
            // Spike at the hit, settling back to the groove as the pulse decays.
            Section.Drop => (1.0 + 2.0 * _pulse, 0.5 + 0.5 * _pulse),
            _ => (1.0, 0.5),
        };

        double alpha = 1 - Math.Exp(-dt / _s.OutputSmoothingSeconds);
        _motion += (motionTarget - _motion) * alpha;
        _spread += (spreadTarget - _spread) * alpha;
    }

    private static double Trend(double fast, double slow)
    {
        double scale = Math.Abs(slow) + 1e-6;
        return Math.Clamp((fast - slow) / scale * 2.5, -1, 1);
    }
}
