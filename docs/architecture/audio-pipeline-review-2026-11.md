# Audio reactivity → shader pipeline: architecture review

**Date:** 2026-11-04
**Branch reviewed:** `main`, then named `djlink_beat_metadata` (working tree, uncommitted changes included)
**Scope:** capture (`ShowControl.Devices.Audio`), analysis (`ShowControl.AudioAnalysis`),
the bridge (`ShowControl.Rendering/AudioLiteBridge.cs`), the graphics host's audio
paths (`ShowControl.Graphics/GraphicsHost.cs`, `FluidSimulationPass.cs`), and the
shaders that consume them (`audiolite.frag`, `fluid.comp`, `fluid.frag`).
**Method:** reading only — no code was run, no software installed.

This is a point-in-time review. When a finding is fixed, link the fixing commit here
rather than editing the finding; the file is a record of what was true on this date.

---

## 1. The pipeline as built

```
PortAudio callback ──copy/downmix──▶ AudioAnalysisEngine.Submit (pooled block + sample-clock time)
        │                                   │ analysis thread, per 512-sample hop (93.75 Hz)
        │                                   ├─ AutoGain → OnsetDetector → TempoPll ◀── IBeatMetadataSource (Pro DJ Link / synthetic)
        │                                   ├─ STFT 2048 → flux/centroid/flatness/bands → SectionTracker
        │                                   ├─ InjectionHistory / InjectionPulses / InjectionPalette / FlowBias  (fluid-specific)
        │                                   └─ AudioLiteComposer → AnalysisSnapshot (immutable, Volatile.Write) + SnapshotPublished
        │                                                                     │ AudioLiteBridge (same thread)
        │                                                                     ├─ SetAudio(8 floats)        → push constants (72 B)
        │                                                                     ├─ SetAudioSpectrum(1024+25) → per-frame SSBO, binding 1
        │                                                                     └─ SetDropSignal(1 float)    → SSBO header
        │                                                               render thread: one Volatile.Read each per frame
        └──legacy queue──▶ 4096-pt FFT + RealTimeBpmDetect ──▶ AudioFrameProcessed ──▶ 6 animations, RenderService, MainPage
```

Threads, in order: the PortAudio real-time callback; the `AudioAnalysis` worker; the
render thread (`GraphicsHost.Run`); and, for the legacy stack, `AudioService`'s processing
and dispatch threads. Every hand-off between them is a pooled copy plus a queue or an
immutable holder plus a volatile write — there are no locks on the audio path.

---

## 2. What is genuinely good

- **Clock-free, deterministic core.** Time arrives with the samples
  (`AudioService.cs:438` passes `_capturedSamples / SampleRate`), the engine anchors once
  and counts from there, and every component takes `dt`. That is why
  `AudioAnalysisEngine.ProcessPending()` lets tests drive the whole stack with no threads,
  and why the analysis side has ~80 tests across nine files. This is the right foundation
  and the thing most worth protecting.
- **Lock-free hand-off everywhere it matters.** Capture → engine is one pooled rent plus
  an enqueue; engine → host is immutable-holder plus `Volatile.Write`; the render thread
  reads each channel exactly once per frame (`GraphicsHost.cs:2921` for the push block,
  `:2563` for the spectrum). The 3-deep spectrum ring and its tear analysis
  (`GraphicsHost.cs:129-136`) are sound: a tear would need two publishes (~21 ms) to land
  inside a microsecond memcpy.
- **Interpretations, not features.** `AudioLiteFrame` encodes judgments — impulse
  significance as a z-score against the material's own recent flux, tension held through
  the pre-drop gap, bar phase pre-advanced to display time — so shaders stay pure functions
  of the current frame. Pinning the impulse hold to three render frames
  (`AnalysisSettings.cs:24`) is exactly the cross-layer reasoning that makes a 46 fps
  consumer reliable against a 94 Hz producer.
- **Dependency direction is respected.** `ShowControl.AudioAnalysis` is a leaf;
  `ShowControl.Graphics` cannot see it, so `AudioLitePush` mirrors `AudioLiteFrame` and the
  bridge (`AudioLiteBridge.cs:42-50`) is the one joint. The engine runs for the life of the
  app and nothing audio hangs off a shader or animation lifecycle.
- **Metadata as a prior, not a trigger.** The PLL/grid fusion with a bounded residual
  (`MaxGridTrimSeconds`) is the correct design for untrusted CDJ data: garbage metadata
  cannot drag phase off the audible beat, and grid loss hands over without a phase jump.
- **The compute prelude is isolated.** `FluidSimulationPass` owns its buffers, pipeline,
  descriptor writes and barriers; `GraphicsHost` has no fluid-specific code beyond calling
  `Record`. The ping-pong/barrier scheme is correct, and the project-wide "pipelines only
  at startup" MoltenVK constraint is honoured.

---

## 3. Findings, ranked

### 3.1 The contract is spread across C# and GLSL with no single authority and no test that can pin it

The same facts live in several places, maintained by hand:

| Fact | Where it lives |
|---|---|
| Injection band count = 5 | `InjectionHistory.cs:37` (`BandCount`), `GraphicsHost.cs:123` (`InjectionSites`), `IGraphicsHost.cs:109` (`InjectionDiscs.Count`), `fluid.comp:246` (`INJ_BANDS`) |
| Injection table layout (25 floats, positional) | engine writes by arithmetic (`AudioAnalysisEngine.cs:315-333`), host writes `padHold` at `InjectionSites*4+3` (`GraphicsHost.cs:2593`), `fluid.comp:97-121` declares it as a struct |
| Push block = 72 bytes, field order | `GraphicsHost.PushData` (`:77-100`), `audiolite.frag:18-36` (declared canonical), `fluid.frag:19-`, `fluid.comp:70-88` |
| Hop rate 93.75 Hz | derivable from `SampleRate / HopSize`, but hard-coded as a literal twice (`GraphicsHost.cs:2476`, `:2567`) |
| 1024 bins at 48 kHz / 2048 window | `GraphicsHost.SpectrumBins`; `fluid.comp:221-226` (`MIN_BIN`, `MAX_BIN`, `BASS_SPLIT` all assume this rate and window); `InjectionHistory.HighestHz` (300 Hz) must equal `BASS_SPLIT` |
| 21739 µs render frame | `GraphicsHost.TargetFrameInterval` (`:442`), `RenderService.cs:1200`, `AnalysisSettings.ImpulseHoldSeconds` (`:24`), `fluid.comp:144` (`DT_SEC = 1/138` = 46 fps × 3 substeps) |
| Fluid grid 288×256 | `FluidSimulationPass.cs:25-26`, `fluid.comp:137-138`, `fluid.frag` |

The CLAUDE.md notes reading "three places that must agree" are the symptom; several of
these are four or five places, and the CLAUDE.md list is incomplete.

The more serious half: the `PushData` / `AudioLitePush` side **cannot be tested**.
`ShowControl.Graphics` pulls MAUI in, so `ShowControl.Tests` cannot reference it, cannot
assert `Unsafe.SizeOf<PushData>() == 72`, cannot check field offsets, and cannot check that
`AudioLiteBridge` copies every field. A contract bump (appending a ninth float) relies
entirely on someone remembering four GLSL files and one C# mirror, and the failure is
silent until the lights are wrong.

**Recommendation.** Extract a `ShowControl.AudioContract` leaf project (plain net9.0, no
project references — the `PixelMapping` precedent) holding:

- `AudioLitePush` and the push-block layout struct (move `PushData` there);
- the injection-table layout as named offsets or a blittable struct, so the engine, the
  host and any future reader index by name;
- `BandCount`, `SpectrumBins`, the fluid grid size, and the pacing literals.

Reference it from `AudioAnalysis`, `Graphics`, `Rendering` and `Tests`. Then:

1. one test pins the push block's size and every field offset with `Marshal.OffsetOf`;
2. one test reads `fluid.comp` / `fluid.frag` / `audiolite.frag` as text and asserts
   `INJ_BANDS`, `GRID_W`/`GRID_H`, `DT_SEC`, `BASS_SPLIT` and the push-block field order
   match the C# constants — cheap, and it catches exactly the drift that bites;
3. the 93.75 literal is replaced by `SampleRate / HopSize` carried through the contract.

This is the one to do first because it makes 3.3 and 3.4 mechanical, and it is the only
finding where a future mistake is currently invisible.

### 3.2 Two analysis stacks run on the same capture

`AudioService` still does its own 4096-point Hamming-window FFT, `RealTimeBpmDetect`, and
dB level calculators on a processing thread, and dispatches `AudioFrameProcessedEventArgs`
on a third thread (`AudioService.cs:515-560`). Consumers:

- `RenderService._lastAverageLevel` (`RenderService.cs:84, :177, :242`);
- `MainPage.xaml.cs:265-268` (BPM, average level, amplitude ratio);
- `RainbowAnimation` (live for the pars), `VibrantAnimation`, `ColorPaleteAnimation`,
  `ColorShimmerAnimation`, `BpmSynchronizedAnimation` (several commented as venue toggles).

So there are two BPM estimates (legacy detector vs `TempoPll`), two level scales (dB vs
0..1 normalized), two FFT windows (4096 Hamming vs 2048 Hann), and two pooled-buffer
lifetime contracts (the `AudioFrameProcessedEventArgs` "never dispose" rule vs the
engine's "copy inside the handler" rule). The live par animation is on the old truth while
the shader is on the new one; they can visibly disagree about the beat.

**Recommendation.** Make the legacy event a *projection* of the engine. A small adapter
subscribes to `SnapshotPublished`, builds `AudioFrameProcessedEventArgs` — `Bpm` from the
PLL, `CurrentLevel`/`AverageLevel` from `Rms`/`BandEnergy` converted to the dB scale the
animations expect, `AmplitudeRatio` from the normalized level — and raises
`AudioFrameProcessed`. The FFT, BPM detector, level calculators and processing thread in
`AudioService` then delete; the capture callback does one copy and one submit; the rig has
one tempo. Keep the event signature so the animations (including the commented venue
lines) do not change.

### 3.3 Drop detection exists twice, and one copy is untestable

The engine computes `dropFired` from the section tracker's `Section` edge
(`AudioAnalysisEngine.cs:312`) and feeds it to `InjectionPalette` and `FlowBias`. The
bridge separately infers "a drop just fired" by watching the announced/predicted countdown
vanish or jump forward (`AudioLiteBridge.cs:59-75`, `ComputeDropSignal`), and that drives
the shader's `blast` (`fluid.comp:599-600`). These are different events — one from
inferred-or-announced sections, one from marker timing — so the fluid's throw and its
palette flip can fire at different moments.

`ShowControl.Rendering` cannot be referenced from the test project, so `ComputeDropSignal`
has no coverage, while the engine-side edge does.

**Recommendation.** Move the signed drop signal into `AnalysisSnapshot` (the engine already
computes `dropIn` at `:306-310`), derive the "fired" edge from the same place the palette
and flow bias take it, and reduce the bridge to a pure field copy. Apply the same rule to
anything else that later wants arithmetic in the bridge: if it computes, it belongs in the
engine where it is testable.

### 3.4 Fluid-specific analysis lives in the generic engine

`InjectionHistory`, `InjectionPulses`, `InjectionPalette`, `FlowBias`, the pad
press/hold masks and the 25-float injection table are all specific to `fluid.comp`, but
they are fields on `AudioAnalysisEngine` and run every hop whatever shader is active. The
cost is negligible; the problem is that the story the reference visualizer exists to prove
— "the engine publishes a contract and shaders consume only the contract"
(`audiolite.frag:4-9`) — no longer holds. The fluid consumes a private side channel the
reference visualizer cannot see.

That side channel also brings its own normalizers. "Relative to usual" currently means
three different things:

| Normalizer | Time constant | Decides |
|---|---|---|
| `fluid.comp` per-column EMA (`:289-291`) | 1.2 s attack / 12 s release | hue deviation of each ring |
| `InjectionHistory` sliding window | 60 s (30 s recent) | where each disc sits |
| `InjectionPulses` references | 300 s | when a disc fires, accent gating |
| `AutoGain` peak window | 300 s | input scale under all of the above |

**Recommendation.** Either promote the injection table to a proper second contract (an
`AudioScene` block, versioned like the push block — cleanest once 3.1 exists), or move the
fluid-specific composers behind an `IEffectDriver` the engine runs a list of, so the core
stays generic and each effect's CPU-side state is its own class. Document which normalizer
owns which decision. Consider feeding the GPU's colour deviation from
`InjectionHistory.Ratios` so there is one definition of "usual" on the CPU and the GPU
keeps no history of its own.

### 3.5 Smaller findings

- **`LatencyModel.PipelineSeconds` is still the 88 ms placeholder** (`LatencyModel.cs:30`).
  Everything predictive — `a_phase`, the section tracker's `LeadSeconds` — is pre-advanced
  by an unmeasured number. The measurement procedure is documented there and the
  `audiolite.frag` flash mode exists; it has not been run. Highest value per hour on this
  list.
- **`a_hue` is dead.** `AudioLiteComposer.cs:90` hard-codes 0 and `audiolite.frag:93`
  draws from it. Either ship chroma or mark the slot reserved in the contract docs so no
  effect is designed on it.
- **Fluid physics is tied to render frames, not time.** `fluid.comp:144` fixes
  `DT_SEC = 1/138`, assuming 46 fps × 3 substeps holds. A presentation stall or any change
  to `TargetFrameInterval` silently changes the physics. The push block has 56 bytes free
  under the 128-byte guarantee — pass the real frame `dt`.
- **The Pro DJ Link local clock is the engine's `Latest.Time`** (`Host.cs:59`). Running
  with no capture device is allowed by design, and in that case the clock stays at 0
  forever, so beat packets are timestamped against a frozen clock. Fall back to a
  monotonic clock until the first block arrives.
- **Unbounded capture queue** (`AudioAnalysisEngine.cs:48`). The engine is far faster than
  real time, so it catches up after a stall, but during a long GC pause or a debugger break
  the backlog grows without bound and latency ramps until it is drained. A cap with
  drop-oldest turns the failure mode from "lag" into "skip".
- **Three publishes per hop can tear across each other.** `SetAudio`, `SetAudioSpectrum`
  and `SetDropSignal` are three separate volatile writes (`AudioLiteBridge.cs:45-49`), so
  a frame can read the push block from hop N and the spectrum from hop N−1. Harmless at
  one hop of 10 ms; if the drop signal moves into the snapshot per 3.3, publish it in the
  same holder as the push block.
- **Per-hop allocation.** Each publish allocates an `AnalysisSnapshot`, two `float[16]`
  and an `AudioLiteHolder` — roughly 400 objects/s. Fine for gen-0, noted only so it is
  not grown casually.
- **Preferred-device matching by name** (`AudioService.cs:141`: "Focusrite USB ASIO",
  "Scarlett", "Universal Audio Thunderbolt") is venue configuration in code. Consistent
  with the venue-toggle convention; deserves a comment there saying so.

---

## 4. Verdict

The architecture is in good shape where it counts: real-time safety, determinism, the
thread hand-offs and the push-constant contract itself are sound, and the analysis side's
test coverage is well above the rest of the repository's. The debt is in the seams — a
contract that lives in six files with no authority (3.1), a second analysis stack that was
never retired (3.2), and analysis logic that leaked into the bridge (3.3). Do 3.1 first: it
makes 3.3 and 3.4 mechanical, and it is the only finding where a mistake is invisible until
the lights are wrong.

## 5. Status

| Finding | Status |
|---|---|
| 3.1 Contract leaf project | open |
| 3.2 Retire legacy analysis stack | open |
| 3.3 Unify drop detection in the engine | open |
| 3.4 Separate fluid-specific analysis | open |
| 3.5 Measure `PipelineSeconds` | open |
| 3.5 Remaining small findings | open |

Update this table from the fixing commit; do not rewrite the findings above.
