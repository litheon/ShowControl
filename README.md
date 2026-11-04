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
> If any of that is a problem for your use, do not use this code. If you intend to run
> it in front of people, read **What controlling lights makes you responsible for**
> below first — it is not optional reading.

## What controlling lights makes you responsible for

Running this software makes **you the lighting operator** for whatever is on the other
end of the cable. That is a role with real duties, and none of them transfer to the
code or its author. Before you plug anything in, be sure you understand and accept
the following.

**People in the beam are your responsibility.**
- *Photosensitive epilepsy.* Roughly 1 in 4,000 people is photosensitive, and flashing
  between about 3 and 60 Hz — especially bright, full-field, red-heavy flashes — can
  trigger seizures. Audio-reactive effects here will flash on every transient at
  whatever rate the music supplies, the measurement mode goes full white on every beat,
  and several supported DMX fixtures expose a hardware strobe channel. Keep sustained
  flash rates below 3 Hz where you can, post strobe warnings at entrances where you
  cannot, know how to stop it instantly, and follow your venue's and jurisdiction's
  rules (e.g. the Harding/Ofcom guidance, local licensing conditions).
- *Performers and crew.* Fixtures aimed into eyes blind people who are walking on
  stages, ladders and truss. Agree focus with the stage manager; never re-aim or
  re-level live without warning.
- *Egress and emergency lighting.* Exit signs, house lights and emergency lighting are
  life-safety systems. Do not patch them into a universe this software drives, do not
  blackout a room in a way that defeats them, and know how to bring the house lights up
  independently of anything running on this machine.

**Everything on the DMX / Art-Net / DDP network is in reach — not just your fixtures.**
A universe is shared. A wrong start address or universe number in the pixel map can
drive someone else's moving heads, hazers, or — on badly segregated rigs — relays,
motors and pyro controllers that should never share a data line with effects
lighting. Confirm the patch against the venue's plot, never put this software on a
universe that carries anything but lighting, and treat an unreviewed pixel map as a
loaded instrument.

**Fixtures, power and heat are physical.** LED pixel strips at full white draw their
rated maximum; a saturated audio-reactive frame will hold them there. Make sure
supplies, injection points, fusing and heat dissipation are rated for 100 % duty, not
for the average of a tasteful look. The software has no notion of thermal or
electrical limits and will not protect hardware from itself.

**Know this rig's failure modes and have a manual way out.** This code is written to
keep the lights running through its own faults, which also means its faults can be
*silent*:
- There is **no blackout, kill or master-dim control** in the software. On shutdown
  it stops animating and closes its sockets; it does not send a dark frame first.
  Strips and fixtures that hold their last value will **stay lit at whatever was last
  sent** until something else addresses them or they are powered down.
- A lost GPU device, a stalled render loop or a frozen staging frame leaves the rig
  **holding its last picture indefinitely** rather than going dark (see `CLAUDE.md`,
  *Graphics host*). A frozen frame at full intensity looks identical to a working
  show from the booth.
- Output devices back off and retry while a controller is unreachable; a strip that
  drops off the network and returns will **snap straight to the live frame**, not
  fade in.
- The audio engine drives effects from whatever the input hears. Feedback, a dropped
  microphone, or a DJ's test tone will be rendered faithfully.

So: run it alongside a hardware console, a DMX merger with a master, or at minimum a
power switch you can reach, and rehearse the blackout you will use when the software
is not the thing you can trust. Watch the rig, not the screen.

**Legal and venue obligations are yours too.** Operating lighting at a public event
can bring licensing conditions, fire-marshal sign-off, insurance requirements and
accessibility obligations that vary by jurisdiction. Publishing this code is not
advice on any of them, and using it does not satisfy any of them.

If you are not prepared to take on all of the above, use a commercial console with a
support contract instead. That is a reasonable choice, and it is the one this
repository's warranty disclaimer is pointing you toward.

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
