namespace ShowControl.AudioAnalysis;

/// <summary>
/// The 8-parameter shader contract. These are interpretations, not raw features — the
/// analysis layer makes the judgments so shaders don't have to. Every field is bounded
/// and NaN-free by construction (<see cref="AudioMath.Sanitize"/> runs on publish).
/// The wire form is 8 floats appended to the graphics push-constant block; the contract
/// is append-only: new fields go on the end and <see cref="AnalysisSnapshot.ContractVersion"/>
/// increments — never reorder, never repurpose a slot.
/// </summary>
/// <param name="Energy">0..1. Partially-normalized level with asymmetric attack/release
/// already applied. Quiet passages sit near 0.2, not 0.0.</param>
/// <param name="Impulse">0..1. Unified transient channel where magnitude encodes
/// significance (hi-hat ≈ 0.2, snare ≈ 0.5, drop = 1.0). Arrives already decaying and is
/// held ≥ ~65ms (3 render frames) so a 46fps consumer cannot miss it.</param>
/// <param name="Tension">0..1. Build accumulation from the section tracker; holds at 1.0
/// through a pre-drop gap, snaps to 0 at the hit, slow-leaks if a predicted drop never
/// lands.</param>
/// <param name="Phase">0..1. Bar phase, pre-advanced by measured pipeline latency, wraps,
/// keeps advancing in silence.</param>
/// <param name="Hue">0..1 turns. Palette angle from chroma. 0 until chroma lands.</param>
/// <param name="Grain">0..1. Tonal → noisy, from spectral flatness.</param>
/// <param name="Motion">0..~3. Rate multiplier from the section tracker, not a raw
/// feature: ~0 during a gap, spikes at the hit, ~1 in a groove.</param>
/// <param name="Spread">0..1. Spatial extent from the section tracker: collapses during a
/// build, expands at the drop, 0.5 in a groove.</param>
public readonly record struct AudioLiteFrame(
    float Energy,
    float Impulse,
    float Tension,
    float Phase,
    float Hue,
    float Grain,
    float Motion,
    float Spread)
{
    /// <summary>A valid frame for "no audio at all" — phase is the one field the engine
    /// still advances during silence, so prefer the engine's output over this constant
    /// whenever an engine exists.</summary>
    public static readonly AudioLiteFrame Silence = new(0f, 0f, 0f, 0f, 0f, 0f, 1f, 0.5f);
}
