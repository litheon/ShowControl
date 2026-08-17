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

## Editor UI

- The editor's logic lives in `ShowControl.ControlPanel.Editor` (plain net9.0 +
  CommunityToolkit.Mvvm), not in the MAUI project, because the test project
  cannot reference MAUI and a view-model there would be untestable. Keep MAUI
  types out of it — `System.Drawing` or tuples — or it stops building for net9.0
  and the tests go with it. `PreviewPage` keeps only what is genuinely platform:
  pointer coordinates, the surface transform, the drawable, dialogs and timers.
- **Command bindings bypass the view's try/catch.** `RunGuarded` in the page is
  what stops a UI-callback exception aborting the process on Catalyst, and a
  `Command=` binding does not go through it — so every command body runs inside
  `EditorViewModelBase.Guard` instead. Anything new on the panel needs one or the
  other.
- Scroll-to-zoom is native: MAUI surfaces no wheel event, so
  `Platforms/MacCatalyst/ScrollZoomGesture` attaches a `UIPanGestureRecognizer`
  with `AllowedScrollTypesMask` set and **no allowed touch types** — the empty
  touch-type list is what stops it also claiming click-drags and fighting the
  fixture drag. It is reattached on `HandlerChanged`, since the platform view is
  recreated with the handler.
- Preview zoom is a compositing transform on the surface view (`Scale` +
  `AnchorX/AnchorY`), never a change to the render path: `MetalBackedView` only
  resizes its drawable from `Bounds`, which a transform does not touch, so no
  swapchain is rebuilt and the LEDs are unaffected. The overlay is *not* scaled —
  it is redrawn from the equivalent `PreviewRegion` so it stays vector-crisp and
  the grab radius stays constant on screen. Anchor and region are two views of one
  state (`PreviewRegion.ForZoom`/`AnchorFor`); the anchor's own [0,1] range is
  exactly the range in which the region stays inside the image, which is why
  clamping the anchor is the only clamp. If they ever disagree the map visibly
  drifts off the picture — that is the check after touching any of it.

## Control surface

- The rig must come up with no MIDI control surface attached — the shader, pixel
  map and DMX output do not depend on one. `RenderService` falls back to
  `AbsentMidiInputDevice` (no pads, no encoders, no capabilities, writes
  swallowed) and adopts a real surface later from `OnInputDeviceReconnected`,
  which CoreMIDI also raises for a first connection. Anything indexing
  `_padStates` needs a bounds check: with no surface the array is empty, and
  some of those writes arrive on an animation's own timer thread.
- Host startup runs synchronously inside the `MainPage` constructor, which UIKit
  calls from `viewDidLoad`. An exception escaping it aborts the process before
  there is a window to read the error in, so `Initialize` catches, logs, and
  comes up without the host rather than taking the app down.

## Pixel mapping

- The rig layout is data, not arithmetic. `ShowControl.PixelMapping` holds the
  model, geometry, sampling and persistence; it is deliberately free of project
  references so `ShowControl.Animation` (which cannot see Devices.DMX/Output)
  and the plain-net9.0 test project can both consume it.
- The shader image is 1350x1200, set in two places that must agree:
  `GraphicsHost.RunHeadless`'s default extent and
  `PixelMap.DefaultImageWidth`/`DefaultImageHeight` (Graphics does not reference
  PixelMapping). `ShaderAnimation` refuses to sample when they disagree and
  `PixelMapValidator` rejects a map authored for another size, so a saved map from
  an older build is not loadable — it falls back to the built-in default with a
  warning. The shaders normalize by `u_resolution`, so a proportional change to
  both axes is a resolution change, not a change to the picture; the shipped map
  was scaled by the same factor so the rig still covers the same fraction of it.
- `PixelMapLayout` is the single authority for frame order, surface order,
  device order and channel offsets. `RenderService` and `DmxDeviceEnumerator`
  both derive from it, so channel routing is no longer positional.
- `FixtureDefinition` stores an origin plus flat per-pixel offsets and **no
  angle, spacing or pivot fields**. Laying out a line, bending a section about a
  pivot, mirroring, respacing, flipping a run end for end — all editor operations
  in `PixelLayoutTools` that rewrite offsets. The file records only the result, so editor tooling can grow
  without a schema change. The corollary is that the editor cannot recall a
  fixture's spacing, it measures it: `MeasureSpacing` is the mean gap between
  neighbours, and `SetSpacing` scales the run about pixel 0, which keeps the
  fixture's position and any bend in it. `ReverseDirection` is the fix for a strip
  wired from the other end: the positions stay put as a set and the pixel order
  reverses, offsets are rebased so the origin still marks pixel 0, and
  `BlankedPixels` is left alone on purpose — those are indices into the wire, so a
  dead tail stays the tail and simply appears at the other end of the run. Neither
  operation changes the pixel count, so both publish as geometry changes and the
  lights follow immediately.
- Live edits are two-tier. Geometry (a drag) publishes an immutable
  `PixelSampleMap` with one volatile write; structural changes are handed to the
  render thread, which rebuilds surfaces, buffers and devices itself at a frame
  boundary so it stays the sole writer of everything it reads. Never reconstruct
  `ShaderAnimation` to apply a change — `Start()` calls `SetShader`, and a
  runtime pipeline rebuild can hang the GPU on MoltenVK. Use `SetSampleMap`.
- The editor's fixture list is updated in place, never cleared and repopulated.
  Clear-and-refill reaches UIKit as `insertItemsAtIndexPaths:`, and a self-sizing
  cell prepared during that insert animation makes `UICollectionViewFlowLayout`
  invalidate its layout, which it answers by throwing — marshalled back through
  the managed handler on Mac Catalyst, that aborts the process. `FixtureList` is
  therefore `MeasureFirstItem` with a fixed row height (uniform rows, no
  self-sizing), and the selection is only assigned when it differs. For the same
  reason every `PreviewPage` event handler and `PixelMapOverlayDrawable.Draw` run
  inside a catch that logs: an exception leaving a UI callback there kills the
  show, not the panel.
- A managed exception that escapes a UI callback leaves nothing in the Serilog
  file — Xamarin turns it into an NSException. The crash report has it:
  `lastExceptionBacktrace` in the `.ips` under `~/Library/Logs/DiagnosticReports`
  carries the throwing UIKit frames, and the app's own log lives in the sandbox
  container at
  `~/Library/Containers/com.companyname.showcontrol.controlpanel.maui/Data/Documents/.config/ShowControl/logs`.
- Dragging a fixture in `PreviewPage` publishes on every pointer move — that is
  what makes the lights follow the cursor — so the move handler does the map
  edit and an overlay redraw and nothing else. Rebuilding the fixture list there
  churns the CollectionView at pointer rate and hands back a selection reset that
  stalls the drag; the panel catches up in `EndDrag`. The drag is anchored on the
  press point rather than accumulated per move, and it ends on `PointerExited`
  too: Mac Catalyst reports a drag leaving the view as an exit and then finishes
  the gesture with no `PointerReleased`, while hover moves keep arriving.
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
- The gate for the pixel-map work is now a pair, because the render target has
  since grown while the rig has not:
  `LegacySamplingReference_AndLegacyRigWithAllStripsEnabled_ProduceIdenticalPixels`
  pins the coordinate-driven sampling against a verbatim copy of the magic-literal
  loop it replaced, run at the 1080x960 geometry that loop encodes; and
  `ShippedDefaultMap_IsTheLegacyRigScaledToTheCurrentImage` pins the shipped map
  to that same rig scaled. Together they say the sampler is faithful and the rig
  has not moved. Do not weaken either — the cutover has no runtime fallback to the
  old path.
- `ShowControl.Rendering` cannot be referenced from the test project: it pulls in
  Animation → Graphics → MAUI, and `ShowControl.Devices.Input`'s plain net9.0
  target does not compile. `PixelRunRenderSurface` is therefore untested
  directly; keep logic that deserves coverage in `ShowControl.PixelMapping`.
