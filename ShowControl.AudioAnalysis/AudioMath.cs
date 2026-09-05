namespace ShowControl.AudioAnalysis;

internal static class AudioMath
{
    /// <summary>Clamp to [lo, hi]; NaN and infinities collapse to <paramref name="lo"/>.
    /// Nothing unbounded escapes the analysis library — this runs on every published field.</summary>
    public static float Sanitize(float value, float lo, float hi)
    {
        if (!float.IsFinite(value))
        {
            return lo;
        }
        return Math.Clamp(value, lo, hi);
    }

    /// <summary>Fractional part mapped to [0, 1); NaN/infinity collapse to 0.</summary>
    public static float SanitizePhase(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0f;
        }
        double wrapped = value - Math.Floor(value);
        return (float)wrapped;
    }
}
