namespace ShowControl.AudioAnalysis;

internal static class SpectralFeatures
{
    /// <summary>Positive spectral flux between consecutive magnitude frames, normalized per
    /// bin. First frame (empty prev) reports 0 rather than the whole spectrum "appearing".</summary>
    public static float Flux(ReadOnlySpan<float> previous, ReadOnlySpan<float> current)
    {
        if (previous.Length != current.Length)
        {
            return 0f;
        }
        double sum = 0;
        for (int i = 0; i < current.Length; i++)
        {
            double d = current[i] - previous[i];
            if (d > 0)
            {
                sum += d;
            }
        }
        return (float)(sum / current.Length);
    }

    /// <summary>Spectral centroid in Hz; 0 when the spectrum is empty (silence).</summary>
    public static float CentroidHz(ReadOnlySpan<float> magnitudes, int sampleRate, int windowSize)
    {
        double weighted = 0, total = 0;
        double binHz = (double)sampleRate / windowSize;
        for (int i = 0; i < magnitudes.Length; i++)
        {
            weighted += magnitudes[i] * i * binHz;
            total += magnitudes[i];
        }
        return total < 1e-12 ? 0f : (float)(weighted / total);
    }

    /// <summary>Spectral flatness (Wiener entropy) on the power spectrum: 0 tonal → 1 noise.
    /// Returns 0 for silence.</summary>
    public static float Flatness(ReadOnlySpan<float> magnitudes)
    {
        const double epsilon = 1e-12;
        double logSum = 0, sum = 0;
        int n = magnitudes.Length;
        for (int i = 0; i < n; i++)
        {
            double power = (double)magnitudes[i] * magnitudes[i] + epsilon;
            logSum += Math.Log(power);
            sum += power;
        }
        double arithmetic = sum / n;
        if (arithmetic <= epsilon * 2)
        {
            return 0f;
        }
        double geometric = Math.Exp(logSum / n);
        return (float)Math.Clamp(geometric / arithmetic, 0, 1);
    }
}
