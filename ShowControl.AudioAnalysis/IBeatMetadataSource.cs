namespace ShowControl.AudioAnalysis;

public enum BeatMetadataPresence
{
    /// <summary>No metadata source on the network; local analysis stands alone.</summary>
    Absent,

    /// <summary>Real-time beat/tempo packets only (beat time, bpm, beat-within-bar).</summary>
    BeatsOnly,

    /// <summary>Full-track beat grid at load, optionally with section markers and
    /// pre-computed track analysis.</summary>
    FullGrid,
}

/// <summary>A real-time beat announcement (a Pro DJ Link beat packet, once the port exists).</summary>
/// <param name="LocalTime">Capture-clock time of the beat, jitter-smoothed.</param>
/// <param name="Bpm">Effective tempo (pitch applied).</param>
/// <param name="BeatWithinBar">1..4; 1 is the downbeat.</param>
/// <param name="Confidence">0..1.</param>
public readonly record struct BeatObservation(double LocalTime, float Bpm, int BeatWithinBar, float Confidence);

public enum SectionKind
{
    Unknown,
    Intro,
    Verse,
    BuildUp,
    Chorus,
    Drop,
    Breakdown,
    Outro,

    /// <summary>A derived landmark (not phrase analysis): the onset of a sustained
    /// bass-heavy region, computed from a pre-known track waveform. Appears only in
    /// <see cref="IBeatMetadataSource.DerivedMarkers"/>, never in announced sections.</summary>
    BassHeavy,
}

/// <summary>A phrase/song-structure marker (rekordbox phrase analysis, via the future
/// Pro DJ Link port): the section starting at <paramref name="LocalTime"/>.</summary>
public readonly record struct SectionMarker(double LocalTime, SectionKind Kind, float Confidence);

/// <summary>
/// Shape-only placeholder for pre-known audio: when a track is cued on gear that shares its
/// analysis, whole-track feature frames can arrive before playback, and the section tracker
/// (and even the composer) can read the future instead of inferring it. Only the shape is
/// defined now; nothing consumes it yet.
/// </summary>
/// <param name="HopSeconds">Spacing of the frames on the track's own timeline.</param>
/// <param name="Rms">One value per hop from track start, normalized 0..1.</param>
/// <param name="LowBand">Optional low-frequency energy per hop, normalized 0..1 — from a
/// three-band waveform's low layer, or a spectral proxy for other waveform styles; null when
/// the source has no frequency-resolved data.</param>
public sealed record TrackAnalysis(double HopSeconds, IReadOnlyList<float> Rms, IReadOnlyList<float>? LowBand = null);

/// <summary>
/// The ahead-of-time knowledge contract: everything a networked beat source (Pro DJ Link
/// via the planned native port, or a synthetic source in tests and the debug view) can tell
/// the analysis ahead of the audio. Everything is expressed on the LOCAL CAPTURE CLOCK —
/// translating the player's media timeline to local time, including re-anchoring the grid
/// as playback position updates arrive, is the implementation's job, never the consumer's.
///
/// Consumers must degrade gracefully through every <see cref="Presence"/> state, and per
/// the design rule metadata is a prior correcting the local PLL — never a direct visual
/// trigger, because network jitter makes direct triggering feel loose.
/// </summary>
public interface IBeatMetadataSource
{
    BeatMetadataPresence Presence { get; }

    /// <summary>The loaded track's full beat grid on the local clock, or null. Immutable;
    /// republished (reference swap) when the track changes or the anchor moves.</summary>
    BeatGrid? CurrentGrid { get; }

    /// <summary>Section markers for the loaded track, empty when unknown. Immutable list,
    /// swapped with the grid.</summary>
    IReadOnlyList<SectionMarker> Sections { get; }

    /// <summary>Pre-computed analysis of the loaded track, when the source has it. Shape
    /// defined now, consumed later.</summary>
    TrackAnalysis? TrackAnalysis { get; }

    /// <summary>Landmarks the source has DERIVED from pre-known audio (for example bass-heavy
    /// region onsets and waveform-predicted drops read from the track waveform), on the local
    /// capture clock, republished (reference swap) with the grid. Kept separate from
    /// <see cref="Sections"/> so announced phrase analysis stays authoritative for the section
    /// tracker; consumers treating these as predictions, not truth, is the point.
    /// Deliberately NOT default-implemented: a wrapper that forgets to forward this must fail
    /// to compile rather than silently serve an empty list (which is exactly the bug a default
    /// here once caused).</summary>
    IReadOnlyList<SectionMarker> DerivedMarkers { get; }

    /// <summary>Real-time beat packets. May fire in any presence state.</summary>
    event Action<BeatObservation>? BeatObserved;
}
