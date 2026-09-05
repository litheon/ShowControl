namespace ShowControl.AudioAnalysis;

/// <summary>
/// Tunables for the analysis core. Everything time-like is in seconds so behavior is
/// independent of sample rate; the rate itself is a parameter, never assumed.
/// </summary>
public sealed record AnalysisSettings
{
    /// <summary>Capture rate in Hz. 48000 in v1; components must not bake this in.</summary>
    public int SampleRate { get; init; } = 48_000;

    /// <summary>STFT window length in samples (power of two). 2048 @ 48kHz ≈ 42.7ms —
    /// the time-frequency trade the spectral features are tuned for.</summary>
    public int FftWindowSize { get; init; } = 2048;

    /// <summary>STFT hop in samples. 512 @ 48kHz publishes at 93.75 Hz — comfortably above
    /// the 46fps render/output rate so max-over-interval semantics hold.</summary>
    public int HopSize { get; init; } = 512;

    /// <summary>Minimum time <see cref="AudioLiteFrame.Impulse"/> is held at or above its
    /// triggering value before decay. 3 render frames of 21739µs (the pacing interval in
    /// GraphicsHost.TargetFrameInterval and RenderService's loop — keep in step with those
    /// two literals) so a 46fps consumer that straddles a publish cannot miss a transient.</summary>
    public double ImpulseHoldSeconds { get; init; } = 3 * 0.021739;

    /// <summary>Energy envelope attack, seconds.</summary>
    public double EnergyAttackSeconds { get; init; } = 0.015;

    /// <summary>Energy envelope release, seconds (slower than attack on purpose).</summary>
    public double EnergyReleaseSeconds { get; init; } = 0.35;

    /// <summary>Time constant for the running flux statistics that turn raw spectral flux
    /// into a significance z-score.</summary>
    public double FluxStatsSeconds { get; init; } = 4.0;

    /// <summary>Z-score at which flux starts registering as impulse.</summary>
    public double ImpulseThresholdZ { get; init; } = 1.5;

    /// <summary>Z-score span mapped onto impulse 0..1 above the threshold.</summary>
    public double ImpulseScaleZ { get; init; } = 6.0;

    /// <summary>Blend between raw (0) and self-normalized (1) level for band energies and
    /// the energy field. Deliberately partial: full normalization makes quiet passages as
    /// loud as drops and flattens the dynamic arc. Per-preset knob, tuned on hardware.</summary>
    public double NormalizationRatio { get; init; } = 0.5;

    /// <summary>Fallback tempo used for phase advance until the PLL (or beat metadata)
    /// supplies a real one. Phase must keep advancing in silence.</summary>
    public double DefaultBpm { get; init; } = 120.0;

    /// <summary>Beats per bar for bar-phase; 4/4 assumed until metadata says otherwise.</summary>
    public int BeatsPerBar { get; init; } = 4;

    /// <summary>RMS below this (linear full-scale) counts as silence for the signal-present flag.</summary>
    public double SilenceRmsFloor { get; init; } = 1e-4;

    /// <summary>How long after the last non-silent hop the signal-present flag stays up.</summary>
    public double SignalHoldSeconds { get; init; } = 0.5;
}
