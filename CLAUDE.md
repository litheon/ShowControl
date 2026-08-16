# ShowControl

Live-show LED/DMX/MIDI control rig. Primary shipping target is the MAUI
MacCatalyst app (`ShowControl.ControlPanel.Maui`), published from a Mac via
`ShowControl.ControlPanel.Maui/build`.

## Logging

- Serilog is configured once in `ShowControl.Host/LoggingBootstrap.cs`
  (Debug + Console + rolling file under
  `~/Library/Application Support/ShowControl/logs/`, inside the sandbox
  container for the published app). Every entry point calls
  `LoggingBootstrap.Initialize()`, which also registers global
  unhandled-exception handlers that flush to disk.
- New diagnostics: use constructor-injected `ILogger<T>` in DI-constructed
  types, or static `Serilog.Log` in static/UI contexts.
- `Console.WriteLine` / `Debug.WriteLine` are for temporary local debugging
  only. Nothing above Trace/Verbose level in per-frame paths (render loop,
  `OnRender`, audio frame handlers).
- Recurring failures in per-frame paths go through `OutputDeviceHealth`
  (`ShowControl.Utilities`): log once when it breaks, once per 30s while it
  stays broken, once when it recovers — never per frame. Output-device sends
  also back off to ~1/s while failing, and each device is written inside its
  own try/catch so one dead controller cannot blank the others.

## Shaders

- `.frag`/`.vert` in `ShowControl.Graphics/` are the source of truth; `.spv`
  are generated at build time (glslc) and gitignored. The build copies them to
  `ShowControl.ControlPanel.Maui/Resources/Raw/` and `ShowControl.Host/`
  automatically — never copy or commit `.spv` by hand.

## Networking

- The app must survive starting before the venue network is up. `DdpDevice`
  opens its socket on first send, not in the constructor, and rebuilds it after
  any send failure; when the network is missing it throws rather than blocking,
  and `OutputDeviceHealth` supplies the retry cadence.
- `ArtNetController` instead waits on `_readyTask` until `ArtNet.Instance`
  publishes a NetworkClient. That wait is unbounded, and `SendSync` is called
  from the render thread — if no network client ever appears, the render loop
  parks on the first frame. Worth bounding if ArtNet comes back into use.

## Pixel mapping

- The rig layout is data, not arithmetic. `ShowControl.PixelMapping` holds the
  model, geometry, sampling and persistence; it is deliberately free of project
  references so `ShowControl.Animation` (which cannot see Devices.DMX/Output)
  and the plain-net9.0 test project can both consume it.
- `PixelMapLayout` is the single authority for frame order, surface order,
  device order and channel offsets. `RenderService` and `DmxDeviceEnumerator`
  both derive from it, so channel routing is no longer positional.
- `FixtureDefinition` stores an origin plus flat per-pixel offsets and **no
  angle, spacing or pivot fields**. Laying out a line, bending a section about a
  pivot, mirroring — all editor operations in `PixelLayoutTools` that rewrite
  offsets. The file records only the result, so editor tooling can grow without
  a schema change.
- Live edits are two-tier. Geometry (a drag) publishes an immutable
  `PixelSampleMap` with one volatile write; structural changes are handed to the
  render thread, which rebuilds surfaces, buffers and devices itself at a frame
  boundary so it stays the sole writer of everything it reads. Never reconstruct
  `ShaderAnimation` to apply a change — `Start()` calls `SetShader`, and a
  runtime pipeline rebuild can hang the GPU on MoltenVK. Use `SetSampleMap`.
- `PixelMapValidator` runs before anything is published. A size mismatch reaching
  `RenderSurface.Render` throws out of the render loop and freezes the rig, so
  there are three guards: validate, revert-on-failure, and a pre-render size
  check that skips the render but still paces the frame.
- The working copy under `~/Library/Application Support/ShowControl/` is
  authoritative, not advisory. Mac Catalyst has no cancellable terminate hook
  under the sandbox, so unsaved edits are flushed on the way out and the user is
  asked on the next launch.

## Venue toggles

- Commented-out fader-schedule values, fixture/animation lines, and the disabled
  AudioService stream are intentional per-venue state — do not "clean them up".
- Output devices are the exception: they are no longer a commented-out list in
  `DmxDeviceEnumerator`. A venue toggle is the `Enabled` flag on a fixture, and
  because each fixture carries its own endpoint and explicit start channel,
  toggling one can no longer re-address the others. The old lines are retained
  there as a reference comment.
- Color palettes live in `ShowControl.Rendering/Palettes.cs` and are referenced
  by bare name from `RenderService.Initialize` via `using static`; keep the
  palette field names stable so commented venue lines keep compiling when
  uncommented.

## Tests

- `dotnet test ShowControl.Tests/ShowControl.Tests.csproj` (plain net9.0).
  Tests cover DMX fixture layouts, DDP/ArtNet packetization, fader schedule,
  MIDI mapping, and now pixel mapping (geometry, sample-map compilation, channel
  planning, serialization, atomic writes). Animation logic is still uncovered.
- The gate for the pixel-map work is
  `LegacySamplingReference_AndDefaultMapWithAllStripsEnabled_ProduceIdenticalPixels`,
  which pins the new coordinate-driven sampling against a verbatim copy of the
  magic-literal loop it replaced. Do not weaken it — the cutover has no runtime
  fallback to the old path.
- `ShowControl.Rendering` cannot be referenced from the test project: it pulls in
  Animation → Graphics → MAUI, and `ShowControl.Devices.Input`'s plain net9.0
  target does not compile. `PixelRunRenderSurface` is therefore untested
  directly; keep logic that deserves coverage in `ShowControl.PixelMapping`.
