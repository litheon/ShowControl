using ShowControl.AudioAnalysis.Dsp;

namespace ShowControl.AudioAnalysis;

/// <summary>A detected transient: a timestamp on the capture clock (not a frame index) and
/// a 0..1 strength.</summary>
public readonly record struct OnsetEvent(double TimeSeconds, float Strength);

/// <summary>
/// The transient path: runs at full capture rate with no FFT, so onset timestamps carry
/// sub-hop precision. Highpass → rectify → short energy window → adaptive threshold with a
/// refractory interval. Time constants are in seconds; the sample rate is a parameter.
/// </summary>
public sealed class OnsetDetector
{
    private const double HighpassHz = 4000;
    private const double EnergyWindowSeconds = 0.002;
    private const double FastTauSeconds = 0.002;
    private const double SlowTauSeconds = 0.7;
    private const double RefractorySeconds = 0.05;
    private const double ThresholdRatio = 2.5;
    private const double EnergyFloor = 1e-7;

    private readonly int _sampleRate;
    private readonly BiquadFilter _highpass;
    private readonly double _fastAlpha;
    private readonly double _slowAlpha;
    private double _fastEnergy;
    private double _slowEnergy;
    private double _lastOnsetTime = double.NegativeInfinity;
    private bool _armed = true;

    public OnsetDetector(int sampleRate)
    {
        _sampleRate = sampleRate;
        _highpass = BiquadFilter.HighPass(sampleRate, HighpassHz);
        _fastAlpha = 1.0 - Math.Exp(-1.0 / (FastTauSeconds * sampleRate));
        _slowAlpha = 1.0 - Math.Exp(-1.0 / (SlowTauSeconds * sampleRate));
        _ = EnergyWindowSeconds; // window folded into the fast time constant
    }

    /// <param name="blockStartTime">Capture-clock time of the first sample of the block.</param>
    public void Process(ReadOnlySpan<float> samples, double blockStartTime, Action<OnsetEvent> onOnset)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float raw = samples[i];
            double x = float.IsFinite(raw) ? _highpass.Process(raw) : 0;
            double energy = x * x;
            _fastEnergy += (energy - _fastEnergy) * _fastAlpha;
            _slowEnergy += (energy - _slowEnergy) * _slowAlpha;

            double t = blockStartTime + (double)i / _sampleRate;
            double threshold = _slowEnergy * ThresholdRatio + EnergyFloor;
            if (_armed && _fastEnergy > threshold && t - _lastOnsetTime >= RefractorySeconds)
            {
                _lastOnsetTime = t;
                _armed = false;
                // Strength from how far the transient rises above its background, log-ish.
                double ratio = _fastEnergy / (_slowEnergy + EnergyFloor);
                float strength = (float)Math.Clamp(Math.Log10(Math.Max(ratio, 1)) / 3.0, 0.05, 1.0);
                onOnset(new OnsetEvent(t, strength));
            }
            else if (!_armed && _fastEnergy < threshold * 0.5)
            {
                // Re-arm only after the energy falls back off, so one hit is one onset.
                _armed = true;
            }
        }
    }
}
