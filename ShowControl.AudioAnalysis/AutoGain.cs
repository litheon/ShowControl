namespace ShowControl.AudioAnalysis;

/// <summary>
/// Automatic input gain: scales the capture so that "100%" is the loudest level heard
/// over the last <see cref="AnalysisSettings.AutoGainWindowSeconds"/>. Everything
/// downstream — levels, flux, the spectrum the visualizations read — then sees a signal
/// whose peak over the window is 1.0, whatever the operator's master is set to.
///
/// The window is an exact sliding maximum (one slot per second), not a decaying
/// average: a loud moment sets the reference for a full five minutes and then drops
/// out of it all at once. A new peak pulls the gain DOWN immediately, in the same block
/// it arrives in, so nothing is ever scaled past full; when the reference expires the
/// gain rises gradually, so a quiet passage swells rather than snaps. The reference
/// never goes below <see cref="AnalysisSettings.AutoGainFloor"/>, which caps how far a
/// quiet room is amplified — silence must stay silence.
///
/// Clock-free like the rest of the analysis core: time arrives as dt with each block.
/// </summary>
internal sealed class AutoGain
{
    private const double RiseSeconds = 3.0;

    private readonly double _floor;
    private readonly float[] _slotPeaks;      // one slot per second of the window
    private int _slot;
    private double _slotElapsed;
    private double _gain = 1.0;

    public AutoGain(AnalysisSettings settings)
    {
        _floor = Math.Max(settings.AutoGainFloor, 1e-6);
        _slotPeaks = new float[Math.Max((int)Math.Ceiling(settings.AutoGainWindowSeconds), 1)];
        Enabled = settings.AutoGainEnabled;
    }

    public bool Enabled { get; }

    /// <summary>The multiplier currently applied to the capture.</summary>
    public float Gain => (float)_gain;

    /// <summary>The loudest sample magnitude in the window — what now counts as 100%.</summary>
    public float WindowPeak { get; private set; }

    /// <summary>Scales <paramref name="samples"/> in place by the gain, having first let
    /// their own peak into the window (so a new loudest moment is itself scaled to 1).</summary>
    public void Process(Span<float> samples, double dt)
    {
        if (!Enabled)
        {
            return;
        }

        if (dt > 0 && double.IsFinite(dt))
        {
            // Advance the window: each slot holds a second's peak; a slot that comes
            // round again starts empty, which is how the old peak expires.
            _slotElapsed += dt;
            while (_slotElapsed >= 1.0)
            {
                _slotElapsed -= 1.0;
                _slot = (_slot + 1) % _slotPeaks.Length;
                _slotPeaks[_slot] = 0f;
            }
        }

        float blockPeak = 0f;
        foreach (float s in samples)
        {
            float a = Math.Abs(s);
            if (a > blockPeak && float.IsFinite(a))
            {
                blockPeak = a;
            }
        }
        if (blockPeak > _slotPeaks[_slot])
        {
            _slotPeaks[_slot] = blockPeak;
        }
        float windowPeak = 0f;
        foreach (float p in _slotPeaks)
        {
            windowPeak = Math.Max(windowPeak, p);
        }
        WindowPeak = windowPeak;

        double target = 1.0 / Math.Max(windowPeak, _floor);
        if (target < _gain)
        {
            _gain = target;                                   // louder than anything in the window: at once
        }
        else if (dt > 0 && double.IsFinite(dt))
        {
            _gain += (target - _gain) * (1 - Math.Exp(-dt / RiseSeconds));
        }

        float g = (float)_gain;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] *= g;
        }
    }
}
