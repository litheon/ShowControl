using ShowControl.AudioAnalysis.Dsp;

namespace ShowControl.AudioAnalysis;

/// <summary>Per-hop features handed to the composer. Raw, unclamped — sanitizing is the
/// composer's job on the way out.</summary>
internal readonly record struct HopFeatures(
    float Rms,
    float Peak,
    float Flux,
    float CentroidHz,
    float Flatness,
    bool SignalPresent);

/// <summary>
/// Turns raw per-hop features into the 8 AudioLite interpretations. Owns every piece of
/// temporal state behind them (envelopes, flux statistics, impulse hold/decay) so shaders
/// stay pure functions of the current frame. Every output field is clamped and NaN-scrubbed;
/// nothing unbounded leaves this class.
/// </summary>
internal sealed class AudioLiteComposer
{
    private readonly AnalysisSettings _settings;
    private readonly EnvelopeFollower _energyEnv;
    private readonly EnvelopeFollower _grainEnv;
    private readonly RunningStats _fluxStats;
    private readonly RunningStats _rmsStats;

    private float _impulse;
    private double _impulseHoldUntil;
    private double _impulseDecayTau = 0.06;

    public AudioLiteComposer(AnalysisSettings settings)
    {
        _settings = settings;
        _energyEnv = new EnvelopeFollower(settings.EnergyAttackSeconds, settings.EnergyReleaseSeconds);
        _grainEnv = new EnvelopeFollower(0.05, 0.2);
        _fluxStats = new RunningStats(settings.FluxStatsSeconds);
        _rmsStats = new RunningStats(settings.FluxStatsSeconds);
    }

    /// <param name="displayBarPhase">Bar phase pre-advanced to display time by the engine.</param>
    /// <param name="tension">From the section tracker; sanitized here like everything else.</param>
    /// <param name="motion">From the section tracker.</param>
    /// <param name="spread">From the section tracker.</param>
    public AudioLiteFrame Compose(in HopFeatures f, double time, double dt, double displayBarPhase,
        float tension, float motion, float spread)
    {
        // Energy: perceptual-ish level with asymmetric attack/release, partially normalized —
        // the raw component keeps a drop visibly louder than its build; the normalized one
        // keeps a quiet master usable. A quiet passage sits near 0.2 (the envelope input is
        // floored while signal is present); true silence releases to 0.
        double rawLevel = Math.Clamp(Math.Sqrt(Math.Max(f.Rms, 0)) * 1.2, 0, 1);
        double normLevel = Math.Clamp(
            f.Rms / (_rmsStats.Mean * 2 + _rmsStats.StandardDeviation * 2 + 1e-9), 0, 1);
        _rmsStats.Add(f.Rms, dt);
        double ratio = Math.Clamp(_settings.NormalizationRatio, 0, 1);
        double level = (1 - ratio) * rawLevel + ratio * normLevel;
        double target = f.SignalPresent ? 0.2 + 0.8 * level : 0;
        double energy = _energyEnv.Process(target, dt);

        // Impulse: spectral-flux z-score against its own recent history, so significance is
        // relative to the material rather than absolute level. Magnitude encodes importance;
        // bigger hits decay slower. The hold keeps a triggering value up for ≥3 render frames.
        // z against history *before* this hop joins it, with a floored deviation so a
        // transient out of near-silence (sd ≈ 0) reads as maximally significant instead
        // of dividing away to nothing.
        double sd = Math.Max(_fluxStats.StandardDeviation, 1e-9 + 0.05 * _fluxStats.Mean);
        double z = (f.Flux - _fluxStats.Mean) / sd;
        _fluxStats.Add(f.Flux, dt);
        double significance = Math.Clamp((z - _settings.ImpulseThresholdZ) / _settings.ImpulseScaleZ, 0, 1);
        if (significance > _impulse)
        {
            _impulse = (float)significance;
            _impulseHoldUntil = time + _settings.ImpulseHoldSeconds;
            _impulseDecayTau = 0.05 * Math.Exp(4.4 * significance);
        }
        else if (time >= _impulseHoldUntil)
        {
            _impulse *= (float)Math.Exp(-dt / _impulseDecayTau);
        }

        double grain = _grainEnv.Process(f.Flatness, dt);

        return new AudioLiteFrame(
            Energy: AudioMath.Sanitize((float)energy, 0f, 1f),
            Impulse: AudioMath.Sanitize(_impulse, 0f, 1f),
            Tension: AudioMath.Sanitize(tension, 0f, 1f),
            Phase: AudioMath.SanitizePhase(displayBarPhase),
            Hue: 0f,                                       // chroma, later phase
            Grain: AudioMath.Sanitize((float)grain, 0f, 1f),
            Motion: AudioMath.Sanitize(motion, 0f, 3f),
            Spread: AudioMath.Sanitize(spread, 0f, 1f));
    }
}
