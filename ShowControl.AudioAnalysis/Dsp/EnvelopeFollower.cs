namespace ShowControl.AudioAnalysis.Dsp;

/// <summary>
/// Asymmetric attack/release envelope. Time constants are in seconds and the step size is
/// a parameter, so behavior does not depend on the rate it is driven at.
/// </summary>
public sealed class EnvelopeFollower
{
    private readonly double _attackSeconds;
    private readonly double _releaseSeconds;
    private double _value;

    public EnvelopeFollower(double attackSeconds, double releaseSeconds)
    {
        _attackSeconds = attackSeconds;
        _releaseSeconds = releaseSeconds;
    }

    public double Value => _value;

    public double Process(double input, double dt)
    {
        if (!double.IsFinite(input))
        {
            input = 0;
        }
        double tau = input > _value ? _attackSeconds : _releaseSeconds;
        // One-pole toward the input; exact for any dt, so a 1ms-stepped and a 10ms-stepped
        // caller converge to the same trajectory.
        double alpha = tau <= 0 ? 1.0 : 1.0 - Math.Exp(-dt / tau);
        _value += (input - _value) * alpha;
        return _value;
    }

    public void Reset(double value = 0) => _value = value;
}
