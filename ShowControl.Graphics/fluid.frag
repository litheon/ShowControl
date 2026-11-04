#version 450

// ───────────────────────────────────────────────────────────────────────────
// Fluid visualization — stage 2 of the "fluid" effect.
//
// Renders the compressible-fluid state written by fluid.comp this frame
// (binding 3): the colored dye carried by the flow, tone-mapped from linear
// energy, plus a shimmer from the density deviation so the pressure wave
// fronts themselves are visible as they cross the field, even over dark
// regions. The state holds LINEAR values; only the R8G8B8A8Srgb attachment
// encodes to sRGB — never store sRGB in the state buffer.
//
// The exposure is fixed (the energy lift it had is disabled in shade()).
// Nothing here flashes on a hit: impact is the simulation's to express, as motion.
// ───────────────────────────────────────────────────────────────────────────

layout(location = 0) out vec4 outColor;

layout(push_constant) uniform Push {
    vec2  u_resolution;
    float u_time;
    float u_distortionStrength;
    float u_hueTolerance;
    float u_falloff;
    float u_param1;
    float u_param2;
    float u_param3;
    float u_param4;
    float a_energy;
    float a_impulse;
    float a_tension;
    float a_phase;
    float a_hue;
    float a_grain;
    float a_motion;
    float a_spread;
} pc;

// The held pad colors, as the kaleidoscope reads them: current entries, then
// the snapshot being cross-faded from. Each vec4: .xyz linear RGB, .w fade.
layout(std430, set = 0, binding = 0) readonly buffer ColorState {
    int   currentCount;
    int   previousCount;
    float transitionT;
    float _pad;
    vec4  data[];
} cs;

layout(std430, set = 0, binding = 1) readonly buffer Spectrum {
    int   binCount;
    int   version;
    float hopHz;
    float dropSeconds; // + seconds until the nearest drop, − seconds since one fired
    float mags[1024];
    // The injection table after the bins — the same layout as fluid.comp's.
    float siteX[5];
    float siteY[5];
    float siteHue[5];
    float sitePulse[5];
    float biasField;
    float biasSplit;
    float marble;      // how marbled the dye is shown, 0..1 (InjectionPalette): each
                       // drop switches between plain palette color and marbling
    float padHold;     // 0..1, how firmly a pad colour is in force (fluid.comp
                       // flattens the injected hue by it; unused here)
    float turbulence;
} spec;

struct Cell {
    vec4 a;   // x = density ρ, yz = velocity, w reserved (see fluid.comp)
    vec4 b;   // rgb = dye (linear), a = marble phase (see fluid.comp)
};
layout(std430, set = 0, binding = 3) readonly buffer StateDst { Cell cells[]; } state;

// Must match FluidSimulationPass.GridWidth/GridHeight and fluid.comp — the
// grid size lives in three places that must agree.
const int GRID_W = 288;
const int GRID_H = 256;

const float EXPOSURE = 1.6;
const float SAT_BOOST = 1.5;      // counteracts the whitening of additive dye mixing
// Where differently colored dye has mixed, the part all three channels share is
// gray. This much of it is removed before tone mapping, so a mixture shows as
// the color that dominates it instead of settling into beige; an even mix of
// the whole palette goes dark rather than white.
const float DEGRAY = 0.85;
// Marbling: the phase carried with the dye is read as fine stripes — the hue
// swings up to this far either side of the dye's own (in turns) and the
// brightness dips up to this much in the troughs, MARBLE_FREQ stripes per
// unit of phase. The flow has folded the phase field, so the stripes marble.
// Both scale with spec.marble, which drops switch between 0 and 1. The swing
// is 5% of the wheel: the dye a held pad injects is its exact hue, and this
// is the only thing left that moves it, so it is held to the tolerance asked
// for (it was 0.099; gating it on the pads instead was tried, and recoloured
// the dye already there on every press).
const float MARBLE_HUE = 0.05;
const float MARBLE_DIP = 0.18;
const float MARBLE_FREQ = 1.0;    // at the density ceiling for now (see MARBLE_RINGS)
// The stripes also glide steadily through the dye: the read phase advances
// this many cycles per u_time unit (~7 s), so the marbling is always in
// gentle motion rather than sitting still between pulses.
const float MARBLE_FLOW = 0.5;
// The stripes' shape: the dark seam between rings is this narrow — the dip
// is raised to this power, so the bright rings are thick and the troughs
// between them thin lines rather than an even sine wave.
const float MARBLE_SEAM = 6.0;
const float WAVE_SHIMMER = 0.25;  // how visibly density fronts ripple the image
// A pixel whose brightest channel, as written to the 8-bit sRGB image, would
// fall below this is written as black instead — but only at the
// PERIPHERY of a shape, where its surroundings are dim too. Inside a shape,
// where the surroundings are bright, a dim pixel keeps its color, so the
// shapes read as solid forms with black outside them rather than as bright
// cores riddled with holes. "Surroundings" is the mean brightness on a ring
// FLOOR_RING_CELLS out from the pixel.
const float BLACK_FLOOR = 135.0 / 255.0;
const float FLOOR_RING_CELLS = 5.0;
const int FLOOR_RING_SAMPLES = 8;
// The floor is a ramp this wide (in the same 0..1 written units) centred on
// BLACK_FLOOR rather than a step, so the edge it cuts is anti-aliased: a
// pixel fades out across the band instead of switching.
const float FLOOR_SOFTNESS = 12.0 / 255.0;

// The pad overlay, after the kaleidoscope's regional bias: while pads are held
// the picture is partitioned into sectors and rings about the centre, each
// region takes one of the held colors in turn, and the fluid under it is
// graded toward that color and AMPLIFIED where its own hue is already within
// the hue tolerance of it — so a region lights up the matching dye that passes
// through it. Both act on the dye before tone mapping, so the gain brightens
// without clipping and the empty field stays black (the floor sees no tint
// there to let through). The partition turns slowly so the regions drift
// across the flow rather than sitting on it.
const int   OVERLAY_SECTORS = 10;
const int   OVERLAY_RINGS   = 6;
const float OVERLAY_RING_SPAN = 1.5;   // >1 so the outer ring reaches past the corners
const float OVERLAY_RING_THICKNESS = 1.8;
const float OVERLAY_SMOOTH  = 0.015;   // soft region edges (radians / normalized radius)
const float OVERLAY_SPIN    = 0.3;     // radians per u_time unit (~2.5 min per turn)
const float OVERLAY_TINT    = 0.35;    // how far the dye is graded toward the region color
const float OVERLAY_GAIN    = 2.0;     // extra dye energy for a full hue match

int cellIndex(ivec2 g) { return g.y * GRID_W + g.x; }

// A tiny hash for dither, hiding banding in the dark field.
float hash12(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

// Manual bilinear sample of the state grid at a continuous grid position (no
// samplers in this pipeline). The field is continuous left to right
// (fluid.comp wraps X), so the sample wraps too; top and bottom are walls and
// clamp.
void sampleState(vec2 g, out vec3 dye, out float rho, out float marble) {
    ivec2 gi = ivec2(floor(g));
    ivec2 i0 = ivec2((gi.x % GRID_W + GRID_W) % GRID_W, clamp(gi.y, 0, GRID_H - 1));
    ivec2 i1 = ivec2(((gi.x + 1) % GRID_W + GRID_W) % GRID_W, min(i0.y + 1, GRID_H - 1));
    vec2 f = vec2(g.x - float(gi.x), clamp(g.y - float(i0.y), 0.0, 1.0));
    Cell c00 = state.cells[cellIndex(i0)];
    Cell c10 = state.cells[cellIndex(ivec2(i1.x, i0.y))];
    Cell c01 = state.cells[cellIndex(ivec2(i0.x, i1.y))];
    Cell c11 = state.cells[cellIndex(i1)];
    vec4 b = mix(mix(c00.b, c10.b, f.x), mix(c01.b, c11.b, f.x), f.y);
    dye = b.rgb;
    marble = b.a;
    rho = mix(mix(c00.a.x, c10.a.x, f.x), mix(c01.a.x, c11.a.x, f.x), f.y);
}

// The displayed color of a point of the field, before dither and the floor.
vec3 rgb2hsv(vec3 c) {
    float cMax = max(c.r, max(c.g, c.b));
    float cMin = min(c.r, min(c.g, c.b));
    float delta = cMax - cMin;
    float h = 0.0;
    if (delta > 1e-6) {
        if (cMax == c.r)      h = mod((c.g - c.b) / delta, 6.0);
        else if (cMax == c.g) h = (c.b - c.r) / delta + 2.0;
        else                  h = (c.r - c.g) / delta + 4.0;
        h /= 6.0; if (h < 0.0) h += 1.0;
    }
    float s = (cMax <= 0.0) ? 0.0 : (delta / cMax);
    return vec3(h, s, cMax);
}

// Standard conversion — K.xyz, the same as fluid.comp's. kaleidoscope.frag's
// copy uses K.yzx, which rotates every hue by a third of the wheel; copied
// here, it turned the marbled picture's reds blue and blues green.
vec3 hsv2rgb(vec3 hsv) {
    vec3 K = vec3(1.0, 2.0/3.0, 1.0/3.0);
    vec3 p = abs(fract(vec3(hsv.x) + K.xyz) * 6.0 - 3.0);
    return hsv.z * mix(vec3(1.0), clamp(p - 1.0, 0.0, 1.0), hsv.y);
}

vec3 shade(vec3 dye, float rho, float marble) {
    // Luminance-preserving tone map; exposure breathes with the music's level.
    // Per-channel 1−exp() clipped every dense region to white independently in
    // r, g and b — mixed-hue fluid washed out pink-white. Reinhard on the
    // LUMINANCE compresses brightness while keeping the dye's hue, and a mild
    // saturation boost counteracts the whitening additive RGB mixing causes.
    dye -= DEGRAY * min(dye.r, min(dye.g, dye.b));
    // The exposure used to breathe with the level (0.85 + 0.35 · a_energy): with
    // a 15 ms attack that lifted the whole field on every hit, and dye sitting
    // just under the black floor popped into view and back out with it. Off
    // for now; the hits are spent on motion instead.
    vec3 scaled = dye * EXPOSURE;
    float luma = dot(scaled, vec3(0.2126, 0.7152, 0.0722));
    vec3 col = luma > 1e-5 ? scaled / (1.0 + luma) : vec3(0.0);
    float lumaOut = dot(col, vec3(0.2126, 0.7152, 0.0722));
    col = clamp(mix(vec3(lumaOut), col, SAT_BOOST), 0.0, 1.0);

    // Wave fronts: compression (ρ > 1) brightens, rarefaction (ρ < 1) darkens —
    // and the brightening carries the LOCAL DYE'S HUE, not gray: the additive
    // glow uses the dye color normalized to full strength, so even faint dye
    // lends saturated color to a passing front. Undyed regions get no glow,
    // which also keeps the standing hydrostatic dome under center gravity from
    // rendering as a gray object.
    float wave = (rho - 1.0) * WAVE_SHIMMER;
    float maxDye = max(dye.r, max(dye.g, dye.b));
    vec3 tint = dye / max(maxDye, 1e-4);
    col = col * (1.0 + wave)
        + max(wave, 0.0) * 0.25 * tint * smoothstep(0.0, 0.04, maxDye);

    // Marbling: swing the hue and dip the brightness with the carried phase.
    // Only where there is dye — the phase is meaningless in the empty field.
    // The swing runs whatever the pads are doing. Holding it out while a pad
    // colour was in force was tried: it recoloured every dyed pixel on the
    // press, and a press is to colour what is injected from now on, not what is
    // already there.
    float stripe = 6.283185307 * (marble * MARBLE_FREQ + pc.u_time * MARBLE_FLOW);
    vec3 hsv = rgb2hsv(clamp(col, 0.0, 1.0));
    float amount = clamp(spec.marble, 0.0, 1.0);
    hsv.x = fract(hsv.x + MARBLE_HUE * amount * sin(stripe));
    hsv.z *= 1.0 - MARBLE_DIP * amount * pow(0.5 + 0.5 * cos(stripe + 1.0), MARBLE_SEAM);
    return mix(col, hsv2rgb(hsv), smoothstep(0.0, 0.04, maxDye));
}

float hueDistance01(float h1, float h2) {
    float d = abs(h1 - h2);
    return min(d, 1.0 - d);
}

// The overlay region this pixel is in: its color and how strongly it applies
// (the color's fade, the cross-fade from the previous set, and the region's
// soft edge). Returns false when no pads are held and nothing is fading.
bool overlayRegion(vec2 fragCoord, out vec3 color, out float strength) {
    color = vec3(0.0);
    strength = 0.0;
    bool hasCurrent = cs.currentCount > 0;
    bool hasTransition = cs.previousCount > 0 && cs.transitionT < 0.999;
    if (!hasCurrent && !hasTransition) {
        return false;
    }
    vec2 p = (2.0 * fragCoord - pc.u_resolution) / pc.u_resolution.y;
    float sectorAngle = 6.283185307 / float(OVERLAY_SECTORS);
    float a = atan(p.y, p.x) + pc.u_time * OVERLAY_SPIN;
    a = mod(a, 6.283185307);
    if (a < 0.0) a += 6.283185307;
    int sectorIndex = int(floor(a / sectorAngle));
    float rScaled = length(p) / OVERLAY_RING_SPAN;
    int ringIndex = clamp(int(floor(rScaled * float(OVERLAY_RINGS))), 0, OVERLAY_RINGS - 1);
    int regionId = sectorIndex + ringIndex;

    float sectorCenter = (float(sectorIndex) + 0.5) * sectorAngle;
    float dSector = abs(atan(sin(a - sectorCenter), cos(a - sectorCenter)));
    float mSector = 1.0 - smoothstep(sectorAngle * 0.5 - OVERLAY_SMOOTH, sectorAngle * 0.5, dSector);
    float ringCenter = (float(ringIndex) + 0.5) / float(OVERLAY_RINGS);
    float ringHalf = OVERLAY_RING_THICKNESS * (0.5 / float(OVERLAY_RINGS));
    float mRing = 1.0 - smoothstep(ringHalf - OVERLAY_SMOOTH, ringHalf, abs(rScaled - ringCenter));

    float fade = 0.0;
    if (hasCurrent) {
        vec4 c = cs.data[regionId % cs.currentCount];
        color = c.xyz;
        fade = c.w;
    }
    if (hasTransition) {
        vec4 old = cs.data[cs.currentCount + clamp(regionId % cs.previousCount, 0, cs.previousCount - 1)];
        color = mix(old.xyz, color, cs.transitionT);
        fade = mix(old.w, fade, cs.transitionT);
    }
    strength = clamp(mSector * mRing, 0.0, 1.0) * clamp(fade, 0.0, 1.0);
    return strength > 0.0;
}

// The dye as the overlay region shows it: dye whose hue is within the hue
// tolerance of the region's color is amplified, and all of it is graded part
// of the way toward that color. The match is judged on the dye's own hue,
// before the grade pulls it closer.
vec3 overlayDye(vec3 dye, vec3 color, float strength) {
    if (strength <= 0.0) {
        return dye;
    }
    float maxDye = max(dye.r, max(dye.g, dye.b));
    vec3 hsvDye = rgb2hsv(dye);
    vec3 hsvColor = rgb2hsv(color);
    float tol = clamp(pc.u_hueTolerance / 6.283185307, 0.0, 0.5);
    float match = 1.0 - smoothstep(0.0, tol, hueDistance01(hsvDye.x, hsvColor.x));
    match = pow(max(match, 0.0), max(pc.u_falloff, 0.0001)) * hsvDye.y * hsvColor.y;
    dye *= 1.0 + OVERLAY_GAIN * match * strength;
    vec3 graded = color / max(max(color.r, max(color.g, color.b)), 1e-4) * maxDye * (1.0 + OVERLAY_GAIN * match * strength);
    return mix(dye, graded, OVERLAY_TINT * strength);
}

// Brightest channel of a color as the image will hold it — the attachment
// encodes to sRGB on write, so encode the same way here. The brightest
// channel rather than the mean: the mean of a saturated primary is a third
// of its strength, so with it a pure red at full blast fell under the floor
// and was cut to black while a pink of the same strength passed — the pad
// colors seemed not to work.
float writtenBrightness(vec3 col) {
    col = clamp(col, 0.0, 1.0);
    vec3 encoded = mix(col * 12.92, 1.055 * pow(col, vec3(1.0 / 2.4)) - 0.055,
                       step(0.0031308, col));
    return max(encoded.r, max(encoded.g, encoded.b));
}

void main() {
    vec2 uv = gl_FragCoord.xy / pc.u_resolution;
    vec2 g = uv * vec2(GRID_W, GRID_H) - 0.5;
    vec3 dye; float rho; float marble;
    sampleState(g, dye, rho, marble);
    vec3 overlayColor; float overlay;
    overlayRegion(gl_FragCoord.xy, overlayColor, overlay);
    vec3 col = shade(overlayDye(dye, overlayColor, overlay), rho, marble);

    // Constant low dither hides banding on the sRGB attachment. It is fixed
    // in place, not re-rolled per frame: at the floor's edge a moving dither
    // read as a crawling fringe.
    col += (hash12(gl_FragCoord.xy) - 0.5) * 0.006;
    col = clamp(col, 0.0, 1.0);

    // The black floor, at the periphery only, and soft: a dim pixel fades
    // toward black across the softness band when the ring around it is dim as
    // well, and stays when the ring is bright. Both tests are ramps, so the
    // edge is anti-aliased and nothing switches from frame to frame.
    float own = smoothstep(BLACK_FLOOR - FLOOR_SOFTNESS, BLACK_FLOOR + FLOOR_SOFTNESS,
                           writtenBrightness(col));
    if (own < 1.0) {
        float around = 0.0;
        for (int i = 0; i < FLOOR_RING_SAMPLES; i++) {
            float angle = 6.283185307 * float(i) / float(FLOOR_RING_SAMPLES);
            vec3 d; float r; float m;
            sampleState(g + FLOOR_RING_CELLS * vec2(cos(angle), sin(angle)), d, r, m);
            around += writtenBrightness(shade(overlayDye(d, overlayColor, overlay), r, m));
        }
        float inside = smoothstep(BLACK_FLOOR - FLOOR_SOFTNESS, BLACK_FLOOR + FLOOR_SOFTNESS,
                                  around / float(FLOOR_RING_SAMPLES));
        col *= max(own, inside);
    }

    outColor = vec4(col, 1.0);
}
