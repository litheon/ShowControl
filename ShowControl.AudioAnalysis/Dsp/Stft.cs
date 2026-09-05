using NAudio.Dsp;

namespace ShowControl.AudioAnalysis.Dsp;

/// <summary>
/// Overlapped short-time Fourier transform. Frame f covers absolute samples
/// [f·hop, f·hop + window); it becomes computable once that many samples have been
/// appended. <see cref="HopRms"/>/<see cref="HopPeak"/> summarize the final hop of the
/// frame — the samples new since the previous frame — so consecutive frames tile the
/// input exactly and nothing falls between publishes (the max-over-interval guarantee).
/// Non-finite input samples are scrubbed to 0 on append so one bad capture block cannot
/// poison the spectrum.
/// </summary>
public sealed class Stft
{
    private readonly int _windowSize;
    private readonly int _hopSize;
    private readonly int _fftOrder;
    private readonly float[] _hann;
    private readonly Complex[] _fft;
    private readonly float[] _magnitudes;

    private float[] _fifo;
    private int _fifoStart;          // index in _fifo of absolute sample _fifoStartAbs
    private int _fifoCount;
    private long _fifoStartAbs;      // absolute index of the first buffered sample
    private long _totalAppended;
    private long _nextFrameStart;    // absolute index where the next frame begins

    public Stft(int windowSize, int hopSize)
    {
        if (windowSize <= 0 || (windowSize & (windowSize - 1)) != 0)
        {
            throw new ArgumentException("Window size must be a power of two.", nameof(windowSize));
        }
        if (hopSize <= 0 || hopSize > windowSize)
        {
            throw new ArgumentException("Hop must be in (0, window].", nameof(hopSize));
        }
        _windowSize = windowSize;
        _hopSize = hopSize;
        _fftOrder = (int)Math.Log2(windowSize);
        _hann = new float[windowSize];
        for (int i = 0; i < windowSize; i++)
        {
            _hann[i] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / (windowSize - 1))));
        }
        _fft = new Complex[windowSize];
        _magnitudes = new float[windowSize / 2];
        _fifo = new float[windowSize * 4];
    }

    /// <summary>Bin magnitudes of the last computed frame (window/2 bins, DC first).</summary>
    public ReadOnlySpan<float> Magnitudes => _magnitudes;

    /// <summary>RMS over the last hop of the last computed frame, linear full-scale.</summary>
    public float HopRms { get; private set; }

    /// <summary>Max |sample| over the last hop of the last computed frame.</summary>
    public float HopPeak { get; private set; }

    /// <summary>Absolute index one past the last sample of the last computed frame.
    /// Divide by the sample rate for the frame-end time offset.</summary>
    public long FrameEndSampleIndex { get; private set; }

    public int WindowSize => _windowSize;
    public int HopSize => _hopSize;

    public void Append(ReadOnlySpan<float> samples)
    {
        EnsureCapacity(_fifoCount + samples.Length);
        foreach (float raw in samples)
        {
            float s = float.IsFinite(raw) ? raw : 0f;
            _fifo[(_fifoStart + _fifoCount) % _fifo.Length] = s;
            _fifoCount++;
        }
        _totalAppended += samples.Length;
    }

    /// <summary>Computes the next frame if enough samples have arrived. Call in a loop —
    /// a large append can make several frames ready.</summary>
    public bool TryComputeFrame()
    {
        if (_totalAppended < _nextFrameStart + _windowSize)
        {
            return false;
        }

        long frameStart = _nextFrameStart;
        long hopStart = frameStart + _windowSize - _hopSize;
        double sumSquares = 0;
        float peak = 0;
        for (int i = 0; i < _windowSize; i++)
        {
            float s = SampleAt(frameStart + i);
            _fft[i].X = s * _hann[i];
            _fft[i].Y = 0;
            if (frameStart + i >= hopStart)
            {
                sumSquares += (double)s * s;
                float abs = Math.Abs(s);
                if (abs > peak)
                {
                    peak = abs;
                }
            }
        }
        FastFourierTransform.FFT(true, _fftOrder, _fft);
        for (int i = 0; i < _magnitudes.Length; i++)
        {
            _magnitudes[i] = MathF.Sqrt(_fft[i].X * _fft[i].X + _fft[i].Y * _fft[i].Y);
        }
        HopRms = (float)Math.Sqrt(sumSquares / _hopSize);
        HopPeak = peak;
        FrameEndSampleIndex = frameStart + _windowSize;

        _nextFrameStart += _hopSize;
        DropBefore(_nextFrameStart);
        return true;
    }

    private float SampleAt(long absoluteIndex)
    {
        long offset = absoluteIndex - _fifoStartAbs;
        return _fifo[(int)((_fifoStart + offset) % _fifo.Length)];
    }

    private void DropBefore(long absoluteIndex)
    {
        long drop = absoluteIndex - _fifoStartAbs;
        if (drop <= 0)
        {
            return;
        }
        _fifoStart = (int)((_fifoStart + drop) % _fifo.Length);
        _fifoCount -= (int)drop;
        _fifoStartAbs = absoluteIndex;
    }

    private void EnsureCapacity(int needed)
    {
        if (needed <= _fifo.Length)
        {
            return;
        }
        int newSize = _fifo.Length;
        while (newSize < needed)
        {
            newSize *= 2;
        }
        var grown = new float[newSize];
        for (int i = 0; i < _fifoCount; i++)
        {
            grown[i] = _fifo[(_fifoStart + i) % _fifo.Length];
        }
        _fifo = grown;
        _fifoStart = 0;
    }
}
