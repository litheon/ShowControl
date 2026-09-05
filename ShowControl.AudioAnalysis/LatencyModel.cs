namespace ShowControl.AudioAnalysis;

/// <summary>
/// The measured end-to-end pipeline latency: acoustic transient at the audio interface
/// input → LED photons. Prediction-capable fields (bar phase, and later the section
/// tracker's hit scheduling) are pre-advanced by this much so what the fixture shows
/// lines up with what the room hears. Shaders never reason about latency.
/// </summary>
public static class LatencyModel
{
    /// <summary>
    /// PLACEHOLDER — not yet measured. Replace via the procedure below and record
    /// min/median/max here.
    ///
    /// Measurement procedure (repeat after any change to render pacing, the staging
    /// ring, capture buffer size, or the output devices):
    ///  1. Point a phone at one LED strip recording 240fps slow-mo, with the speaker
    ///     playing a click track audible in the video's audio (or a speaker in frame).
    ///  2. Feed the same click track into the capture interface.
    ///  3. Run the audiolite shader with its measurement mode on (u_p1 high): it slams
    ///     full white when impulse > 0.8.
    ///  4. Count video frames from each click transient to the LED flash, ~10 clicks;
    ///     take the median. Jitter of ±1–2 render frames (±22–43ms) is expected — the
    ///     staging ring drops frames when full rather than stalling.
    ///
    /// Rough budget for sanity-checking the measurement: capture buffer (~5–10ms)
    /// + analysis hop (≤10.7ms) + snapshot staleness (≤21.7ms) + render/staging ring
    /// (up to 2 frames, 43ms) + DDP send + WLED latch + WS2812 shift-out ≈ 65–110ms.
    /// </summary>
    public const double PipelineSeconds = 0.088;
}
