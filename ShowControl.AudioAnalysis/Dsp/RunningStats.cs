namespace ShowControl.AudioAnalysis.Dsp;

/// <summary>
/// Exponentially-weighted running mean and variance with a time constant in seconds,
/// for turning raw features into z-scores against their own recent history.
/// </summary>
public sealed class RunningStats
{
    private readonly double _tauSeconds;
    private double _mean;
    private double _variance;
    private bool _seeded;

    public RunningStats(double tauSeconds)
    {
        _tauSeconds = tauSeconds;
    }

    public double Mean => _mean;
    public double StandardDeviation => Math.Sqrt(Math.Max(_variance, 0));

    public void Add(double value, double dt)
    {
        if (!double.IsFinite(value))
        {
            return;
        }
        if (!_seeded)
        {
            _mean = value;
            _variance = 0;
            _seeded = true;
            return;
        }
        double alpha = _tauSeconds <= 0 ? 1.0 : 1.0 - Math.Exp(-dt / _tauSeconds);
        double delta = value - _mean;
        _mean += alpha * delta;
        _variance = (1 - alpha) * (_variance + alpha * delta * delta);
    }

    /// <summary>Z-score of a value against the current statistics; 0 until seeded or while
    /// the deviation is degenerate (constant input).</summary>
    public double ZScore(double value)
    {
        if (!_seeded || !double.IsFinite(value))
        {
            return 0;
        }
        double sd = StandardDeviation;
        if (sd < 1e-12)
        {
            return 0;
        }
        return (value - _mean) / sd;
    }
}
