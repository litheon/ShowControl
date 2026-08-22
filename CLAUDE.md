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

## Graphics host

- The render loop logs through `ReportRenderFailure`, which throttles on
  `OutputDeviceHealth` exactly as the output devices do. Nothing in `OnRender`
  may log per attempt: it runs thousands of times a second, so a failure that
  repeats every frame writes tens of thousands of lines a minute and takes the
  process down with the log — a lost GPU device did precisely that, filling 50MB
  in 40 seconds and requiring a force quit.
- `VK_ERROR_DEVICE_LOST` is terminal. `_deviceLost` latches, the render loop
  stops, `ReadFromMemory` returns Empty rather than waiting on a timeline
  semaphore that will never be signalled, and `SetShader` is ignored. Recovering
  the picture means rebuilding the Vulkan device and everything under it; today
  that is a restart.
- **The Metal drawable is pinned and the swapchain is never rebuilt on resize.**
  `VulkanSurfaceView.PinnedDrawableWidth/Height` fixes `CAMetalLayer.DrawableSize`
  at the offscreen image's size, so `LayoutSubviews` stops changing it, the
  swapchain never goes out of date, and `HandleMetalSurfaceResize` is a documented
  no-op. This is only sound because nothing renders at the window's resolution:
  the shader draws a fixed image and the preview is a full stretch of it, so
  matching the swapchain to the window merely moved the final upscale from the
  compositor into the blit — while putting a heavyweight GPU rebuild on a path
  driven by a mouse drag, where overlapping rebuilds lost the device and hung the
  app. The cost is a softer preview when the window is larger than the image.
  Pass the pinned size to `EnableMetalPresentation`, not the view's: the swapchain
  extent has to match the drawable. Unpinning it was tried while chasing a CPU
  spike and put back: the spike was the render loop, not the resolution.
- **The render loop is paced explicitly** to `TargetFrameInterval`, which is the
  same 21739µs (~46fps) `RenderService` paces its own loop to — a frame rendered
  between two samples is a frame nobody reads. Keep the two in step if either
  changes; matching exactly means jitter occasionally costs a sample its fresh
  frame and the sampler holds the previous one, which is invisible on LEDs.
  Presentation used to set the rate by accident: with FIFO, an acquire that found
  no free image returned early and the frame did no work, so the loop advanced at
  the display's refresh. Once a frame that cannot be presented had to render anyway
  — the rig's pixels are in that submission — nothing paced it and it free-ran at
  hundreds of frames a second, each one a full render and a 6.5MB staging copy,
  spiking CPU and GPU for frames nobody read. `Thread.Sleep(0)`, which is what was
  there under a comment claiming to throttle, yields and returns immediately; it is
  not a delay.
- Nothing in `OnRender` writes to `Debug.WriteLine` any more. What is genuinely
  once-per-session (the first blit, the DComp target size, presentation coming up)
  is `_logger.LogDebug` behind a `_loggedX` flag; what recurs (a swapchain not
  ready, a zero extent, a pending resize) goes through `NotePresentationSuspended`,
  which reports the transition and then stays quiet. Both call sites pass constant
  strings, so nothing interpolates per frame either.
- A minimised or hidden window makes Core Animation sit on the drawable request,
  so `AcquireNextImage` blocks for up to a second even with a zero timeout. Both
  it and `QueuePresent` are timed; a call over 100ms backs presentation off for a
  second (`_presentRetryAfter`) and is logged once per outage. The frames still
  render and the rig still gets its pixels — only the showing stops, which is free
  when nobody is looking.
- **Presentation must never gate the LED path.** `OnRender` renders offscreen and
  copies to the staging ring the rig samples; presenting is a separate concern
  layered on top. So a swapchain that is not ready, an acquire that yields no
  drawable (minimised, occluded, on another Space) and a present that reports
  out-of-date all set `present = false` and carry on — they never return early,
  because the bookkeeping that publishes the staging slot lives at the end of the
  frame. Skipping it freezes the rig on its last frame. `SUBOPTIMAL` in particular
  is the *steady state* with a pinned drawable, since the swapchain is
  deliberately not the layer's size: treating it as a reason to bail stopped the
  LEDs after the first frame.
- **The rig's pixels and the preview's are still in one submission**, so the
  guards above are avoidance, not isolation: the staging copy that feeds the LEDs
  and the blit to the swapchain are recorded into the same command buffer, and
  when `present` is true that submit waits on the image-available semaphore *at
  the transfer stage* — which is the stage the staging copy is in. A drawable the
  compositor will not hand back therefore withholds the LED data itself, and both
  `WaitForFences` at the top of the next frame and `WaitSemaphores` in
  `ReadFromMemory` are unbounded, so the stall propagates to the output thread and
  the rig stops sending entirely. Splitting the submit in two — offscreen plus
  staging signalling `_gpuDoneTL`, present waiting on that — is the fix that would
  make the rest of this section merely an optimisation.
- **A held image outlives the frame that acquired it.** The acquire signals
  `_imageAvailableSemaphores[_currentFrame]`, and the submit that consumes the
  image must wait on that same slot — but the two are different frames whenever an
  image is acquired and presentation then pauses, because frames keep rendering for
  the rig and `_currentFrame` keeps advancing. `_acquiredFrameIndex` records the
  acquiring slot for exactly this reason; waiting on the current slot instead waits
  on a semaphore nothing will signal, and the unbounded fence wait next frame takes
  the render loop and the rig with it.
- The one remaining rebuild is `RebuildSwapchainAtPinnedSize`, from an
  out-of-date acquire — a display reconfiguration, say — because otherwise the
  preview would skip frames for the rest of the session. It holds `_swapchainLock`
  (two threads recreating at once is how the device gets lost), logs through the
  render throttle, and drains with `DeviceWaitIdle` rather than `QueueWaitIdle`
  because the staging ring the LED sampling reads is filled by transfers on the
  same device. `CleanupMetalPresentation` takes the same lock.
- Windows still recreates on resize (`RecreateWin32Swapchain`,
  `RecreateDirectCompositionSharedImage`). Pinning would suit it equally — the
  DComp path blits the same fixed image to a shared texture — but that target does
  not build on macOS, so it was left alone.

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
- **Presentation is gated on the window, from `PreviewPage`.** A window that is
  not being composited withholds its drawable, and the frame that waits on it
  carries the rig's pixels — so `UpdatePresentationVisibility` calling
  `SetPresentationEnabled` is a safety mechanism, not a power saving. It is driven
  by `PreviewVisibilityMonitor`, which observes the scene/application/window
  lifecycle notifications; the 250ms poll is only a backstop, because a MAUI
  dispatcher timer is an `NSTimer` the system coalesces exactly when the app stops
  being frontmost. Leaving notifications (`WillDeactivate`, `WillResignActive`,
  `DidEnterBackground`, `DidBecomeHidden`) are *acted on* rather than verified —
  they arrive before the state they announce is readable, so polling at that moment
  returns the answer that is about to be wrong — while returning ones only prompt a
  re-evaluation. Window notifications are posted for every window in the process,
  so the handler checks the source is this preview's.
- **Only `ForegroundActive` counts as visible.** Foreground-*inactive* is not
  "visible but unfocused": it is equally the state of a window that is minimised,
  on another Space, or fully covered by another app, and Core Animation stops
  recycling drawables in all three. The trade is that the preview holds its last
  frame whenever the app is not frontmost — deliberately, since the picture returns
  on the next click and the lights never stopped. Relaxing it to include
  `ForegroundInactive` is a one-line change in `UpdatePresentationVisibility` and a
  real risk; do not make it to "fix" a frozen preview.
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
- **TODO — zoom on Windows.** The zoom itself should already work there and is
  simply unverified: MAUI's Windows transform sets `RenderTransformOrigin` to the
  anchor and a `ScaleTransform`, the same "the anchor maps to itself" semantics
  the `PreviewRegion` arithmetic assumes, and `RenderTransform` does not affect
  layout, so `SwapChainPanel.SizeChanged` never fires and no swapchain is rebuilt.
  So the buttons, drag-to-pan and overlay registration need testing, not code.
  What is genuinely missing is the scroll input, and pinch:
  - Wheel and precision-touchpad scroll both arrive as
    `UIElement.PointerWheelChanged` on the overlay's platform view.
    `e.GetCurrentPoint(element)` gives `.Position` (the focus) and
    `.Properties.MouseWheelDelta` (±120 per notch). Raise the existing
    `ScrollZoomEventArgs` from it and `PreviewPage.OnScrollZoom` needs no change;
    set `e.Handled` so it does not bubble.
  - MAUI's `PinchGestureRecognizer` on Windows rides WinUI manipulation events,
    which are touch and pen only — a touchpad pinch never reaches it. Windows
    delivers touchpad pinch as **Ctrl + wheel**, so treating
    `e.KeyModifiers == Control` as zoom in the same handler *is* pinch support
    there.
  - Watch `SwapChainPanel.CompositionScaleChanged`, which a scale transform
    raises. Nothing subscribes to it today, so the composited swapchain is simply
    magnified — the same softness trade as Catalyst. Reacting to it by resizing
    the swapchain is the Windows-native route to a sharper zoom, and the
    equivalent of the blit-source-rect approach that was backed out.
  - Keep it in its own `#if WINDOWS` file: the Windows target framework does not
    build on macOS, so a mistake there is invisible until someone builds on
    Windows, and an isolated file makes that failure obvious.

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
