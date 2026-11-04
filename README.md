# ShowControl

A live-show LED / DMX / MIDI control rig: a Vulkan shader renders a fixed image, a
pixel map samples it onto LED strips and DMX fixtures over DDP / Art-Net / USB DMX, an
audio-analysis engine drives the shaders from the room's sound and Pro DJ Link beat
metadata, and a MIDI control surface steers it all. The shipping target is the .NET MAUI
Mac Catalyst app in `ShowControl.ControlPanel.Maui`.

> ## ⚠️ Warning — read before building, running or reusing anything here
>
> **This is one person's working show rig, published as-is. It is not a product, a
> library, or a reference implementation, and it comes with no support.**
>
> - **No warranty, no liability.** The software is provided *as is*, without warranty of
>   any kind, express or implied, including fitness for a particular purpose. The author
>   accepts no liability for any damage, loss or injury arising from its use. Use it
>   entirely at your own risk.
> - **It drives real hardware on a real network.** On start-up it opens UDP sockets,
>   sends DDP and Art-Net packets to the fixture addresses in its pixel map, writes to USB
>   DMX interfaces, opens the audio input device, and takes over any connected MIDI
>   control surface. Running it on a network you do not control, or with a pixel map
>   you have not reviewed, will push light to fixtures you did not intend to.
> - **It can produce flashing and strobing light.** Audio-reactive effects respond to
>   transients and drops with sudden full-frame changes, and the latency-measurement mode
>   of `audiolite.frag` deliberately slams the output to full white on every beat. Do not
>   point the output at people without considering photosensitive-epilepsy risk; follow
>   your venue's guidance.
> - **Venue state is hard-coded on purpose.** Device names, fixture addresses, fader
>   schedules, palette choices and blocks of commented-out code are deliberate per-venue
>   configuration, edited in source between shows (see `CLAUDE.md` → *Venue toggles*).
>   They are not defaults you should expect to make sense for your rig, and they are not
>   cruft to clean up.
> - **It is tuned to one machine.** Builds assume an Apple-silicon Mac with the bundled
>   MoltenVK, `glslc` on the path, a specific set of forked submodules under `external/`,
>   and the audio interfaces named in `AudioService`. Branches are named after the shows
>   they were cut for; most will not build on anything else without work.
> - **Security has not been reviewed.** The app listens for Pro DJ Link traffic and
>   Art-Net/DDP replies without authentication, as those protocols require. Run it on an
>   isolated show network only.
>
> If any of that is a problem for your use, do not use this code.

## Layout

| Project | Role |
|---|---|
| `ShowControl.ControlPanel.Maui` | The app: preview window, pixel-map editor, audio debug view (Mac Catalyst; Windows target unmaintained) |
| `ShowControl.Host` | DI composition root and logging bootstrap |
| `ShowControl.Rendering` | Render loop, pixel sampling, output-device fan-out |
| `ShowControl.Graphics` | Vulkan host, shaders (`.frag`/`.comp` are source; `.spv` are generated) |
| `ShowControl.AudioAnalysis` | Clock-free analysis core: STFT, onsets, tempo PLL, section tracker, AudioLite contract |
| `ShowControl.Devices.*` | Audio capture, DMX/DDP/Art-Net output, MIDI input, Pro DJ Link |
| `ShowControl.PixelMapping` | Rig layout model, geometry, sampling, persistence |
| `ShowControl.Tests` | `dotnet test ShowControl.Tests/ShowControl.Tests.csproj` |
| `docs/architecture/` | Dated design reviews |

`CLAUDE.md` is the engineering notebook: the hard-won operating rules for the render
loop, presentation, audio pipeline and editor live there and are kept current.

## Building

```
git submodule update --init --recursive
scripts/build-native-mac.sh          # MoltenVK and native deps
dotnet test ShowControl.Tests/ShowControl.Tests.csproj
ShowControl.ControlPanel.Maui/build  # publishes the Mac Catalyst app
```

## Licence

No licence is granted. All rights reserved by the author. The submodules under
`external/` carry their own licences.
