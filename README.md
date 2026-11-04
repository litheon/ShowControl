# ShowControl

A live-show LED / DMX / MIDI control rig: a Vulkan shader renders a fixed image, a
pixel map samples it onto LED strips and DMX fixtures over DDP / Art-Net / USB DMX, an
audio-analysis engine drives the shaders from the room's sound and Pro DJ Link beat
metadata, and a MIDI control surface steers it all. The shipping target is the .NET MAUI
Mac Catalyst app in `ShowControl.ControlPanel.Maui`.

It's a personal project, shared as-is: no warranty and no support, but you're welcome
to look around, build it, and borrow ideas. A few things are worth knowing before you
run it.

## Before you run it

**It starts sending as soon as it launches.** There's no "connect" button. On start-up
it loads the pixel map (the one saved under `~/Library/Application Support/ShowControl/`,
or the built-in default from `DefaultPixelMap.json` if there isn't one) and begins
streaming DDP to the addresses in it — the default map points at `192.168.3.x`, my
strips at home. On another network those packets go nowhere and the app carries on
happily, so a first run with nothing attached is completely fine. If you *do* have
WLED strips or an Art-Net node on the same subnet, open the pixel map editor (the
Edit switch in the preview window) and check the addresses before you launch, or
tick the fixtures off with their `Enabled` flag.

**There's no blackout button.** Quitting stops the stream but doesn't send a dark frame
first, so strips keep showing whatever they last received until something else
addresses them or they lose power. The same is true if the render loop ever stalls:
the lights hold the last picture rather than going out. For bench testing that just
means "unplug it or send black from WLED when you're done"; it's worth knowing before
you leave a strip lit overnight.

**The effects flash.** Audio-reactive shaders respond to beats and drops with sudden
changes, and the `audiolite` shader has a measurement mode (rotary 2 past halfway) that
goes full white on every beat on purpose. Fine on a bench; keep it in mind if you point
real fixtures at other people, and have a look at the short section below if you ever
take it to a show.

**Venue settings live in the source.** Device names, fixture addresses, fader schedules,
and blocks of commented-out lines are per-show configuration that gets edited between
gigs rather than externalised (see `CLAUDE.md` → *Venue toggles*). They'll look odd out
of context; they're meant to.

**It's built for one machine.** Apple-silicon Mac, the bundled MoltenVK, `glslc` on the
path, the forked submodules under `external/`, and the audio interfaces named in
`AudioService`. It runs without a control surface, without an audio interface, and
without the network, falling back gracefully in each case — but the Windows target
hasn't been built in a long while, and branches are named after the shows they were
cut for, so expect most of them to need work.

## If you take it in front of an audience

Then you're the lighting operator, with the usual responsibilities that come with the
job — not things this software can take on for you. In short: flashing light affects
some people badly, so keep sustained flash rates low and follow your venue's guidance
on strobe warnings; make sure nothing on a universe you drive is life-safety lighting;
double-check the patch so you're only addressing your own fixtures; give LED supplies
headroom for full-white frames; and, because there's no blackout here, keep a way to
kill the lights that doesn't depend on this app — a console master, a DMX merger, or a
power switch within reach. Watch the rig, not the screen.

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

No licence is granted yet — all rights reserved for now. The submodules under
`external/` carry their own licences.
