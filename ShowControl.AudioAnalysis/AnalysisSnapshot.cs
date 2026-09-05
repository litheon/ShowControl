namespace ShowControl.AudioAnalysis;

/// <summary>
/// One published analysis frame. Immutable — published by a single volatile reference
/// write and read wherever needed (the SetSampleMap idiom), so consumers on any thread
/// see a complete frame with no locks. The arrays are owned by the snapshot and must not
/// be mutated by consumers.
///
/// Contract guarantees: silence produces a valid frame (not a stalled one) with
/// <see cref="SignalPresent"/> false and phases still advancing; every field is bounded
/// and NaN-free; flux and peak are max-over-interval since the previous publish, never
/// instantaneous, so a consumer sampling slower than the ~93.75 Hz publish rate cannot
/// miss a transient.
/// </summary>
public sealed class AnalysisSnapshot
{
    /// <summary>Append-only contract version. Bump when fields are added.</summary>
    public const int ContractVersion = 1;

    /// <summary>Number of log-spaced bands in <see cref="BandEnergy"/>/<see cref="BandFlux"/>.</summary>
    public const int BandCount = 16;

    /// <summary>Capture-clock time of the end of the hop this frame summarizes, seconds.</summary>
    public double Time { get; init; }

    /// <summary>Seconds since the previous publish (one hop, except the very first frame).</summary>
    public double Dt { get; init; }

    /// <summary>Monotonic publish counter.</summary>
    public long FrameIndex { get; init; }

    /// <summary>False while the input has been below the silence floor for longer than the
    /// signal hold. Phase keeps advancing regardless.</summary>
    public bool SignalPresent { get; init; }

    /// <summary>RMS of the last hop, linear full-scale, 0..1.</summary>
    public float Rms { get; init; }

    /// <summary>Max |sample| over the interval since the previous publish, 0..1.</summary>
    public float Peak { get; init; }

    /// <summary>Total spectral flux, max-over-interval since the previous publish. ≥ 0,
    /// normalized per bin; practically well under 1 for real material.</summary>
    public float FluxTotal { get; init; }

    /// <summary>Spectral centroid in Hz, 0..Nyquist. 0 during silence.</summary>
    public float CentroidHz { get; init; }

    /// <summary>Spectral flatness 0 (tonal) .. 1 (noise).</summary>
    public float Flatness { get; init; }

    /// <summary>Current tempo estimate. Fallback/default until the PLL lands.</summary>
    public float Bpm { get; init; }

    /// <summary>0..1 confidence in <see cref="Bpm"/>. 0 for the fallback tempo.</summary>
    public float TempoConfidence { get; init; }

    /// <summary>Beat phase 0..1 at <see cref="Time"/> (not latency-advanced — the
    /// pre-advanced value is <see cref="Lite"/>.Phase's job).</summary>
    public float BeatPhase { get; init; }

    /// <summary>Bar phase 0..1 at <see cref="Time"/>.</summary>
    public float BarPhase { get; init; }

    /// <summary>Per-band energy, 16 log-spaced bands, 0..1 each. Zero-filled until the
    /// band analyzer lands (contract shape fixed early on purpose).</summary>
    public float[] BandEnergy { get; init; } = new float[BandCount];

    /// <summary>Per-band flux, max-over-interval, ≥ 0. Zero-filled until the band analyzer lands.</summary>
    public float[] BandFlux { get; init; } = new float[BandCount];

    /// <summary>Current musical section from the tracker (announced-first, inferred fallback).</summary>
    public Section Section { get; init; } = Section.Steady;

    /// <summary>0..1 confidence in the section call (build confidence while building).</summary>
    public float SectionConfidence { get; init; }

    /// <summary>The composed 8-parameter shader contract for this frame.</summary>
    public AudioLiteFrame Lite { get; init; } = AudioLiteFrame.Silence;

    /// <summary>A valid "engine not running yet" frame.</summary>
    public static readonly AnalysisSnapshot Empty = new();
}
