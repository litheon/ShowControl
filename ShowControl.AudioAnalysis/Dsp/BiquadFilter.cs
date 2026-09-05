namespace ShowControl.AudioAnalysis.Dsp;

/// <summary>
/// Direct-form-I biquad. Only the shapes the transient path needs; coefficients follow the
/// Audio EQ Cookbook. Per-sample <see cref="Process"/> is the hot path — keep it branch-free.
/// </summary>
public sealed class BiquadFilter
{
    private readonly double _b0, _b1, _b2, _a1, _a2;
    private double _x1, _x2, _y1, _y2;

    private BiquadFilter(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }

    public static BiquadFilter HighPass(int sampleRate, double cutoffHz, double q = 0.707)
    {
        double w0 = 2 * Math.PI * cutoffHz / sampleRate;
        double cos = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2 * q);
        double b0 = (1 + cos) / 2;
        double b1 = -(1 + cos);
        double b2 = (1 + cos) / 2;
        double a0 = 1 + alpha;
        double a1 = -2 * cos;
        double a2 = 1 - alpha;
        return new BiquadFilter(b0, b1, b2, a0, a1, a2);
    }

    public double Process(double x)
    {
        double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = y;
        return y;
    }

    public void Reset()
    {
        _x1 = _x2 = _y1 = _y2 = 0;
    }
}
