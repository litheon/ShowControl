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

## Venue toggles

- Commented-out device lists (`DmxDeviceEnumerator`), fader-schedule values,
  fixture/animation lines, and the disabled AudioService stream are
  intentional per-venue state — do not "clean them up".
- Color palettes live in `ShowControl.Rendering/Palettes.cs` and are referenced
  by bare name from `RenderService.Initialize` via `using static`; keep the
  palette field names stable so commented venue lines keep compiling when
  uncommented.

## Tests

- `dotnet test ShowControl.Tests/ShowControl.Tests.csproj` (plain net9.0).
  Tests cover stable code only (DMX fixture layouts, DDP/ArtNet packetization,
  fader schedule, MIDI mapping) — not pixel mapping or animation logic, which
  are slated for refactoring.
