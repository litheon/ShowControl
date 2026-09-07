using BeatLink.Data;
using ShowControl.AudioAnalysis;
using LinkBeatGrid = BeatLink.Data.BeatGrid;

namespace ShowControl.Devices.ProDjLink;

/// <summary>
/// Reads a whole-track feature profile out of a rekordbox waveform detail: an RMS-like level
/// series, a low-frequency series where the waveform style carries one (the three-band low
/// layer directly, the red channel scaled by height as a proxy for RGB waveforms), and the
/// track times where sustained bass-heavy regions begin. Everything here is on the track's own
/// timeline; the adapter maps the region starts onto the local clock when it publishes them.
/// </summary>
internal static class WaveformBassProfile
{
    /// <summary>Waveform half-frames averaged per profile hop (150/s → 37.5/s).</summary>
    internal const int DownsampleFactor = 4;

    /// <summary>Seconds per profile hop.</summary>
    internal const double HopSeconds = DownsampleFactor / 150.0;

    /// <summary>Normalized low-band level (relative to the track's own
    /// <see cref="NormalizationPercentile"/> level) at which a bass-heavy region begins, and
    /// the lower level at which it ends (hysteresis).</summary>
    internal const float EnterThreshold = 0.6f;
    internal const float ExitThreshold = 0.45f;

    /// <summary>A region must hold above the exit threshold at least this long to count.</summary>
    internal const double MinimumRegionSeconds = 2.0;

    /// <summary>The smoothing window applied before thresholding, in seconds.</summary>
    internal const double SmoothingSeconds = 0.5;

    /// <summary>Series are normalized by this percentile of their own values (clamped to 1)
    /// rather than the maximum, so a single outlier bass peak cannot compress the whole
    /// track's scale and push every threshold out of reach.</summary>
    internal const double NormalizationPercentile = 0.95;

    /// <summary>Normalized low-band level below which the bass counts as "sucked out"
    /// (the pre-drop gap), relative to the track's own 95th-percentile level.</summary>
    internal const float SuctionThreshold = 0.2f;

    /// <summary>How long the bass must stay sucked out for the gap to be a drop candidate
    /// (roughly a bar at typical dance tempos).</summary>
    internal const double MinimumSuctionSeconds = 1.5;

    /// <summary>How soon after the suction ends the bass must come back for the return to
    /// count as this gap's payoff. Real drops land noticeably after the smoothed gap-end
    /// (risers and fills lift the low band before the hit): observed ~1.2-1.4 s on real
    /// tracks, so the window comfortably covers that.</summary>
    internal const double RestorationToleranceSeconds = 2.5;

    /// <summary>Normalized low-band level the return must reach to count as a restoration
    /// (relative to the track's own 95th-percentile level).</summary>
    internal const float RestorationThreshold = 0.5f;

    /// <summary>How long after the restoration the bass must, on average, stay strong —
    /// rejects a single stab that doesn't actually start a bass-heavy passage.</summary>
    internal const double RestorationSustainSeconds = 2.0;

    /// <summary>The mean normalized low-band level required across the sustain window.</summary>
    internal const float RestorationSustainThreshold = 0.4f;

    internal sealed record Profile(TrackAnalysis Analysis, IReadOnlyList<double> BassStartsTrackSeconds);

    /// <summary>Extracts the profile, or null when the waveform is empty.</summary>
    internal static Profile? Extract(WaveformDetail detail)
    {
        var frames = detail.FrameCount;
        var hops = frames / DownsampleFactor;
        if (hops < 2)
        {
            return null;
        }

        var rms = new float[hops];
        var low = detail.Style == WaveformStyle.Blue ? null : new float[hops];
        for (var hop = 0; hop < hops; hop++)
        {
            var segment = hop * DownsampleFactor;
            switch (detail.Style)
            {
                case WaveformStyle.ThreeBand:
                {
                    var lowHeight = detail.SegmentHeight(segment, DownsampleFactor, ThreeBandLayer.Low);
                    var midHeight = detail.SegmentHeight(segment, DownsampleFactor, ThreeBandLayer.Mid);
                    var highHeight = detail.SegmentHeight(segment, DownsampleFactor, ThreeBandLayer.High);
                    rms[hop] = lowHeight + midHeight + highHeight;
                    low![hop] = lowHeight;
                    break;
                }
                case WaveformStyle.Rgb:
                {
                    var height = detail.SegmentHeight(segment, DownsampleFactor);
                    var color = detail.SegmentColor(segment, DownsampleFactor);
                    rms[hop] = height;
                    low![hop] = color.R / 255f * height;
                    break;
                }
                default:
                    rms[hop] = detail.SegmentHeight(segment, DownsampleFactor);
                    break;
            }
        }

        Normalize(rms);
        IReadOnlyList<double> starts = Array.Empty<double>();
        if (low != null)
        {
            Normalize(low);
            starts = FindBassRegionStarts(low, HopSeconds);
        }
        return new Profile(new TrackAnalysis(HopSeconds, rms, low), starts);
    }

    /// <summary>Normalizes in place by the series' own <see cref="NormalizationPercentile"/>
    /// value, clamping to 1 — outlier peaks saturate instead of compressing the scale.</summary>
    private static void Normalize(float[] values)
    {
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);
        var reference = sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * NormalizationPercentile))];
        if (reference <= 0f)
        {
            return;
        }
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = Math.Min(values[i] / reference, 1f);
        }
    }

    /// <summary>Trailing moving average over <see cref="SmoothingSeconds"/>.</summary>
    private static float[] Smooth(IReadOnlyList<float> low01, double hopSeconds)
    {
        var count = low01.Count;
        var window = Math.Max(1, (int)Math.Round(SmoothingSeconds / hopSeconds));
        var smoothed = new float[count];
        float runningSum = 0;
        for (var i = 0; i < count; i++)
        {
            runningSum += low01[i];
            if (i >= window)
            {
                runningSum -= low01[i - window];
            }
            smoothed[i] = runningSum / Math.Min(i + 1, window);
        }
        return smoothed;
    }

    /// <summary>
    /// Finds the start times (seconds from track start) of sustained bass-heavy regions in a
    /// normalized low-band series: smooth, then enter a region when the level reaches
    /// <see cref="EnterThreshold"/>, leave it when the level falls below
    /// <see cref="ExitThreshold"/>, and keep only regions that lasted at least
    /// <see cref="MinimumRegionSeconds"/>.
    /// </summary>
    internal static IReadOnlyList<double> FindBassRegionStarts(IReadOnlyList<float> low01, double hopSeconds)
    {
        var count = low01.Count;
        if (count == 0)
        {
            return Array.Empty<double>();
        }
        var smoothed = Smooth(low01, hopSeconds);

        var starts = new List<double>();
        var inRegion = false;
        var regionStartHop = 0;
        var minimumHops = MinimumRegionSeconds / hopSeconds;
        for (var i = 0; i < count; i++)
        {
            if (!inRegion && smoothed[i] >= EnterThreshold)
            {
                inRegion = true;
                regionStartHop = i;
            }
            else if (inRegion && smoothed[i] < ExitThreshold)
            {
                if (i - regionStartHop >= minimumHops)
                {
                    starts.Add(CompensatedStart(regionStartHop));
                }
                inRegion = false;
            }
        }
        if (inRegion && count - regionStartHop >= minimumHops)
        {
            starts.Add(CompensatedStart(regionStartHop));
        }

        // The trailing smoother delays the detected onset by about half its window, just as it
        // does for drops; without this the bass marker visibly lags the drop it belongs to.
        double CompensatedStart(int hop) => Math.Max(0, hop * hopSeconds - SmoothingSeconds / 2);
        return starts;
    }

    /// <summary>One suction candidate the drop detector considered, with its fate — the raw
    /// material for diagnosing why a track's predictions are missing or wrong.</summary>
    internal sealed record DropCandidate(double SuctionStartSeconds, double SuctionEndSeconds, string Outcome);

    /// <summary>
    /// Everything the drop detector saw while scanning one track, for logging: the smoothed
    /// low-band statistics that determine whether suction windows can form at all, and every
    /// candidate with its accept/reject reason.
    /// </summary>
    internal sealed record DropDetectionReport(
        double SmoothedMin, double SmoothedMedian, double SmoothedFifthPercentile,
        double FractionBelowSuction, double FractionAboveRestoration,
        IReadOnlyList<DropCandidate> Candidates)
    {
        public override string ToString()
        {
            var candidates = Candidates.Count == 0
                ? "no suction candidates"
                : string.Join("; ", Candidates.Select(c =>
                    $"[{c.SuctionStartSeconds:F1}-{c.SuctionEndSeconds:F1}s: {c.Outcome}]"));
            return $"low-band smoothed min={SmoothedMin:F3} p5={SmoothedFifthPercentile:F3} " +
                $"median={SmoothedMedian:F3}, {FractionBelowSuction:P1} of track below suction " +
                $"threshold {SuctionThreshold}, {FractionAboveRestoration:P1} above restoration " +
                $"threshold {RestorationThreshold} — {candidates}";
        }
    }

    /// <summary>
    /// Predicts drops from the waveform's low band: a sustained suction of low frequency (the
    /// pre-drop gap, ≥ <see cref="MinimumSuctionSeconds"/> below <see cref="SuctionThreshold"/>)
    /// followed promptly by a restoration (≥ <see cref="RestorationThreshold"/> within
    /// <see cref="RestorationToleranceSeconds"/>) that the following beats sustain (mean ≥
    /// <see cref="RestorationSustainThreshold"/> over <see cref="RestorationSustainSeconds"/>).
    /// Each hit is snapped to the beat grid — to the nearest downbeat when one is within a beat,
    /// since drops land on downbeats — and returned as seconds on the track's own timeline.
    /// </summary>
    internal static IReadOnlyList<double> DetectDrops(
        IReadOnlyList<float> low01, double hopSeconds, LinkBeatGrid grid) =>
        DetectDrops(low01, hopSeconds, grid, out _);

    /// <summary>The diagnostic overload: also yields the full detection report.</summary>
    internal static IReadOnlyList<double> DetectDrops(
        IReadOnlyList<float> low01, double hopSeconds, LinkBeatGrid grid, out DropDetectionReport report)
    {
        var count = low01.Count;
        if (count == 0 || grid.BeatCount < 2)
        {
            report = new DropDetectionReport(0, 0, 0, 0, 0, Array.Empty<DropCandidate>());
            return Array.Empty<double>();
        }
        var smoothed = Smooth(low01, hopSeconds);
        var candidates = new List<DropCandidate>();

        var minimumSuctionHops = (int)Math.Round(MinimumSuctionSeconds / hopSeconds);
        var toleranceHops = (int)Math.Round(RestorationToleranceSeconds / hopSeconds);
        var sustainHops = (int)Math.Round(RestorationSustainSeconds / hopSeconds);

        var drops = new List<double>();
        var i = 0;
        while (i < count)
        {
            // Find a suction window: contiguous hops below the suction threshold.
            if (smoothed[i] >= SuctionThreshold)
            {
                i++;
                continue;
            }
            var suctionStart = i;
            while (i < count && smoothed[i] < SuctionThreshold)
            {
                i++;
            }
            var startSeconds = suctionStart * hopSeconds;
            var endSeconds = i * hopSeconds;
            if (i - suctionStart < minimumSuctionHops)
            {
                candidates.Add(new DropCandidate(startSeconds, endSeconds,
                    $"gap too short ({endSeconds - startSeconds:F1}s < {MinimumSuctionSeconds}s)"));
                continue;
            }
            if (i >= count)
            {
                candidates.Add(new DropCandidate(startSeconds, endSeconds, "ran to track end"));
                continue;
            }

            // The bass must come back hard within the tolerance...
            var restorationHop = -1;
            for (var j = i; j < Math.Min(i + toleranceHops, count); j++)
            {
                if (smoothed[j] >= RestorationThreshold)
                {
                    restorationHop = j;
                    break;
                }
            }
            if (restorationHop < 0)
            {
                var peak = 0f;
                for (var j = i; j < Math.Min(i + toleranceHops, count); j++)
                {
                    peak = Math.Max(peak, smoothed[j]);
                }
                candidates.Add(new DropCandidate(startSeconds, endSeconds,
                    $"no restoration (peaked at {peak:F2} < {RestorationThreshold} within {RestorationToleranceSeconds}s) — breakdown"));
                continue;
            }

            // ...and the following beats must keep it there (rejects a single stab). Require at
            // least half the sustain window of track left to judge.
            var sustainEnd = Math.Min(restorationHop + sustainHops, count);
            if (sustainEnd - restorationHop < sustainHops / 2)
            {
                candidates.Add(new DropCandidate(startSeconds, endSeconds,
                    "restored too close to track end to judge sustain"));
                continue;
            }
            float sum = 0;
            for (var j = restorationHop; j < sustainEnd; j++)
            {
                sum += smoothed[j];
            }
            var sustainMean = sum / (sustainEnd - restorationHop);
            if (sustainMean < RestorationSustainThreshold)
            {
                candidates.Add(new DropCandidate(startSeconds, endSeconds,
                    $"stab, not sustained (mean {sustainMean:F2} < {RestorationSustainThreshold} over {RestorationSustainSeconds}s)"));
                continue;
            }

            // The trailing moving average delays the detected edge by about half the smoothing
            // window; compensate before snapping so the true onset drives the beat choice.
            var restorationSeconds = restorationHop * hopSeconds - SmoothingSeconds / 2;
            var snapped = SnapToBeat(Math.Max(0, restorationSeconds), grid);
            if (drops.Count == 0 || Math.Abs(drops[^1] - snapped) > hopSeconds)
            {
                drops.Add(snapped);
                candidates.Add(new DropCandidate(startSeconds, endSeconds,
                    $"DROP predicted at {snapped:F1}s (sustain mean {sustainMean:F2})"));
            }
            else
            {
                candidates.Add(new DropCandidate(startSeconds, endSeconds,
                    $"duplicate of drop at {drops[^1]:F1}s"));
            }
        }

        report = BuildReport(smoothed, candidates);
        return drops;
    }

    private static DropDetectionReport BuildReport(float[] smoothed, List<DropCandidate> candidates)
    {
        var sorted = (float[])smoothed.Clone();
        Array.Sort(sorted);
        var belowSuction = 0;
        var aboveRestoration = 0;
        foreach (var value in smoothed)
        {
            if (value < SuctionThreshold) belowSuction++;
            if (value >= RestorationThreshold) aboveRestoration++;
        }
        return new DropDetectionReport(
            sorted[0],
            sorted[sorted.Length / 2],
            sorted[(int)(sorted.Length * 0.05)],
            (double)belowSuction / smoothed.Length,
            (double)aboveRestoration / smoothed.Length,
            candidates);
    }

    /// <summary>Snaps a track time to the nearest grid beat, preferring a downbeat when one
    /// lies within a beat interval of the raw time.</summary>
    private static double SnapToBeat(double trackSeconds, LinkBeatGrid grid)
    {
        var milliseconds = (long)Math.Round(trackSeconds * 1000);
        var atOrBefore = Math.Clamp(grid.FindBeatAtTime(milliseconds), 1, grid.BeatCount);

        var bestBeat = atOrBefore;
        var bestDistance = double.MaxValue;
        var bestDownbeat = -1;
        var bestDownbeatDistance = double.MaxValue;
        for (var beat = Math.Max(1, atOrBefore - 1); beat <= Math.Min(grid.BeatCount, atOrBefore + 2); beat++)
        {
            var distance = Math.Abs(grid.GetTimeWithinTrack(beat) / 1000.0 - trackSeconds);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestBeat = beat;
            }
            if (grid.GetBeatWithinBar(beat) == 1 && distance < bestDownbeatDistance)
            {
                bestDownbeatDistance = distance;
                bestDownbeat = beat;
            }
        }

        var beatInterval = Math.Abs(
            grid.GetTimeWithinTrack(Math.Min(grid.BeatCount, atOrBefore + 1)) -
            grid.GetTimeWithinTrack(atOrBefore)) / 1000.0;
        var chosen = bestDownbeat > 0 && bestDownbeatDistance <= Math.Max(beatInterval, 0.001)
            ? bestDownbeat
            : bestBeat;
        return grid.GetTimeWithinTrack(chosen) / 1000.0;
    }
}
