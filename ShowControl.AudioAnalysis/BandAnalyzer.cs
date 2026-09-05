using ShowControl.AudioAnalysis.Dsp;

namespace ShowControl.AudioAnalysis;

/// <summary>
/// Folds the magnitude spectrum into 16 log-spaced bands with per-band flux and
/// partially-normalized energy. Partial on purpose: full normalization makes a quiet
/// passage as visually loud as a drop and destroys the dynamic arc, so the published
/// value blends a raw (globally-scaled) level with a per-band self-normalized one at an
/// explicit ratio — the drop still visibly exceeds its build.
/// </summary>
internal sealed class BandAnalyzer
{
    private const double LowestBandHz = 35.0;

    private readonly int[] _binToBand;
    private readonly double _normalizationRatio;
    private readonly float[] _rawEnergy;
    private readonly float[] _previousRawEnergy;
    private readonly float[] _bandEnergy;
    private readonly float[] _bandFlux;
    private readonly RunningStats[] _bandStats;
    private readonly RunningStats _totalStats;

    public BandAnalyzer(AnalysisSettings settings)
    {
        int bandCount = AnalysisSnapshot.BandCount;
        _normalizationRatio = Math.Clamp(settings.NormalizationRatio, 0, 1);
        _rawEnergy = new float[bandCount];
        _previousRawEnergy = new float[bandCount];
        _bandEnergy = new float[bandCount];
        _bandFlux = new float[bandCount];
        _bandStats = new RunningStats[bandCount];
        for (int i = 0; i < bandCount; i++)
        {
            _bandStats[i] = new RunningStats(settings.FluxStatsSeconds);
        }
        _totalStats = new RunningStats(settings.FluxStatsSeconds);

        // Bin → band by log frequency, DC parked in band 0.
        int binCount = settings.FftWindowSize / 2;
        double binHz = (double)settings.SampleRate / settings.FftWindowSize;
        double nyquist = settings.SampleRate / 2.0;
        double logLow = Math.Log(LowestBandHz);
        double logSpan = Math.Log(nyquist) - logLow;
        _binToBand = new int[binCount];
        for (int bin = 0; bin < binCount; bin++)
        {
            double hz = Math.Max(bin * binHz, LowestBandHz);
            int band = (int)((Math.Log(hz) - logLow) / logSpan * bandCount);
            _binToBand[bin] = Math.Clamp(band, 0, bandCount - 1);
        }
    }

    /// <summary>Blended 0..1 per-band level for the last analyzed frame.</summary>
    public ReadOnlySpan<float> BandEnergy => _bandEnergy;

    /// <summary>Per-band positive flux (raw scale, ≥ 0) for the last analyzed frame.</summary>
    public ReadOnlySpan<float> BandFlux => _bandFlux;

    public void Analyze(ReadOnlySpan<float> magnitudes, double dt)
    {
        Array.Clear(_rawEnergy);
        for (int bin = 0; bin < magnitudes.Length && bin < _binToBand.Length; bin++)
        {
            float m = magnitudes[bin];
            _rawEnergy[_binToBand[bin]] += m * m;
        }

        double total = 0;
        for (int i = 0; i < _rawEnergy.Length; i++)
        {
            _rawEnergy[i] = MathF.Sqrt(_rawEnergy[i]);
            total += _rawEnergy[i];
        }
        _totalStats.Add(total, dt);
        // One global scale for the raw component keeps the bands' relative levels — and the
        // build-vs-drop contrast — intact; the per-band statistics feed only the normalized
        // component.
        double globalScale = 1.0 / (_totalStats.Mean * 0.25 + 1e-9);

        for (int i = 0; i < _rawEnergy.Length; i++)
        {
            float raw = _rawEnergy[i];
            _bandFlux[i] = Math.Max(0f, raw - _previousRawEnergy[i]);
            _previousRawEnergy[i] = raw;

            var stats = _bandStats[i];
            stats.Add(raw, dt);
            double normalized = raw / (stats.Mean * 2 + stats.StandardDeviation * 2 + 1e-9);
            double scaledRaw = raw * globalScale;
            double blended = (1 - _normalizationRatio) * Math.Clamp(scaledRaw, 0, 1)
                           + _normalizationRatio * Math.Clamp(normalized, 0, 1);
            _bandEnergy[i] = AudioMath.Sanitize((float)blended, 0f, 1f);
        }
    }
}
