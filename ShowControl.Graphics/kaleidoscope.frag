#version 450

layout(location=0) out vec4 outColor;

// ───────────────────────────────────────────────────────────
// Push constants (128B total; 6 colors @ 16B stride each)
// ───────────────────────────────────────────────────────────
layout(push_constant) uniform Push {
    vec2  u_resolution;          //  8
    float u_time;                //  4
    float _pad0;                 //  4  -> 16

    float u_distortionStrength;  //  4
    float u_hueTolerance;        //  4  (radians; bandwidth for hue match)
    float u_falloff;             //  4  (kernel sharpness)
    int   u_colorCount;          //  4  -> 32

    vec3  u_colors[6];           // 6 * 16 = 96  (vec3 has 16B stride)
} pc;

// ───────────────────────────────────────────────────────────
// Tunables (no ABI changes)
// ───────────────────────────────────────────────────────────
const float PI  = 3.141592653589793;
const float TAU = 6.283185307179586;

// Distortion & highlight/pulse (from earlier)
const float HIGHLIGHT_GAIN = 2.0;
const float PULSE_GAIN     = 2.5;
const float PULSE_SPEED    = 6.2831; // ~1 Hz

// Regional bias strength
const float REGION_SNAP_STRENGTH  = 0.007; // 0..1 hue snap toward region's assigned color
const float REGION_GRADE_STRENGTH = 0.007; // 0..1 gentle grading toward assigned color

// Region layout
const int   REGION_RINGS   = 6;     // number of radial rings
const float RING_SMOOTH    = 0.015; // soften ring edges (in normalized radius)
const float SECTOR_SMOOTH  = 0.015; // soften sector edges (in radians)
const float RING_SPAN            = 1.5;  // >1.0 makes rings extend past screen radius
const float RING_THICKNESS_MULT  = 1.8;   // >1.0 thickens rings; can cover whole viewport if large

// ───────────────────────────────────────────────────────────
// Helpers
// ───────────────────────────────────────────────────────────
vec3 palette(float t){ return 0.5 + 0.5 * cos(TAU * (vec3(0.0,0.33,0.67)+t)); }

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
    float v = cMax;
    return vec3(h, s, v);
}

vec3 hsv2rgb(vec3 hsv) {
    vec3 K = vec3(1.0, 2.0/3.0, 1.0/3.0);
    vec3 p = abs(fract(vec3(hsv.x) + K.yzx) * 6.0 - 3.0);
    return hsv.z * mix(vec3(1.0), clamp(p - 1.0, 0.0, 1.0), hsv.y);
}

float hueDistance01(float h1, float h2) {
    float d = abs(h1 - h2);
    return min(d, 1.0 - d);
}

float hueMatchWeight(float hue01) {
    if (pc.u_colorCount <= 0) return 0.0;
    float tolHue = clamp(pc.u_hueTolerance / TAU, 0.0, 0.5);
    float w = 0.0;
    for (int i = 0; i < pc.u_colorCount && i < 6; ++i) {
        float selHue01 = rgb2hsv(pc.u_colors[i]).x;
        float d = hueDistance01(hue01, selHue01);
        float k = 1.0 - smoothstep(0.0, tolHue, d);
        k = pow(k, max(pc.u_falloff, 0.0001));
        w = max(w, k);
    }
    return clamp(w, 0.0, 1.0);
}

float nearestSelectedHue01(float h) {
    if (pc.u_colorCount <= 0) return h;
    float bestH = h;
    float bestD = 1.0;
    for (int i = 0; i < pc.u_colorCount && i < 6; ++i) {
        float sh = rgb2hsv(pc.u_colors[i]).x;
        float d = hueDistance01(h, sh);
        if (d < bestD) { bestD = d; bestH = sh; }
    }
    return bestH;
}

float hueLerp01(float h, float th, float s) {
    float d = th - h;
    if (d >  0.5) d -= 1.0;
    if (d < -0.5) d += 1.0;
    return fract(h + d * s);
}

// Smooth region masks for nicer edges
float smoothStepEdge(float x, float edgeCenter, float halfWidth) {
    float t = (x - (edgeCenter - halfWidth)) / (2.0 * halfWidth);
    return clamp(t, 0.0, 1.0);
}

// ───────────────────────────────────────────────────────────
// Main
// ───────────────────────────────────────────────────────────
void main(){
    vec2 res  = pc.u_resolution;
    float time = pc.u_time;

    // Base coords
    vec2 p = (2.0 * gl_FragCoord.xy - res) / res.y;
    float aWorld = atan(p.y, p.x);       // world angle (no sectorization yet)
    float rWorld = length(p);            // world radius (0..~1+)

    // Kaleidoscope sectorization (visual structure)
    const float N = 10.0;
    float a = mod(aWorld, TAU / N) - PI / N;
    vec2 dir = vec2(cos(a), sin(a));
    float r = rWorld;
    p = dir * r;

    // ── Pass 1: compute base hue from original pattern
    float t  = time * 0.6;
    float k  = 3.0 + 2.0 * sin(t * 0.7);
    float bands = 0.5 + 0.5 * sin(20.0 * r + 6.0 * a + t * 2.0);
    float flow  = 0.5 + 0.5 * sin(10.0 * r - 3.0 * t + 2.0 * sin(8.0 * a + t));
    float hueBase = fract(r * k - 0.1 * t + 0.25 * bands);

    // Distortion weight from hue match
    float w = hueMatchWeight(hueBase);

    // ── Color-driven distortion
    float strength = pc.u_distortionStrength;
    float wobble   = sin(12.0 * r - 5.0 * time) * 0.5 + 0.5;
    float twist    = sin(7.0 * a + 3.0 * time);

    float dr = strength * w * (0.6 * wobble + 0.4 * twist);
    float da = strength * w * (0.8 * sin(5.0 * r + 2.0 * time));

    float r2 = max(0.0, r + dr);
    float a2 = a + da;
    vec2  p2 = vec2(cos(a2), sin(a2)) * r2;

    // ── Pass 2: recompute hue/color with distorted coords
    float bands2 = 0.5 + 0.5 * sin(20.0 * r2 + 6.0 * a2 + t * 2.0);
    float flow2  = 0.5 + 0.5 * sin(10.0 * r2 - 3.0 * t + 2.0 * sin(8.0 * a2 + t));
    float hue2   = fract(r2 * k - 0.1 * t + 0.25 * bands2);

    vec3 col = palette(hue2);
    col *= pow(flow2, 1.5) * (0.3 + 0.7 * pow(1.0 - clamp(r2, 0.0, 1.0), 3.0));
    col *= 1.2 / max(1e-6, (1.0 + dot(p2, p2)));

    // ───────────────────────────────────────────────────────
    // Highlight + Pulse on matched hues (kept from prior)
    // ───────────────────────────────────────────────────────
    float w2 = hueMatchWeight(hue2);
    float pulse = 0.5 + 0.5 * sin(time * PULSE_SPEED + hue2 * 12.0);
    col = mix(col, col * HIGHLIGHT_GAIN, w2);
    col *= (1.0 + w2 * pulse * PULSE_GAIN);

    // ───────────────────────────────────────────────────────
    // NEW: Regional palette bias
    //   - Partition by sector (angular) and ring (radial)
    //   - Assign each region to a selected color index
    //   - Snap hue & gently grade toward that color
    // ───────────────────────────────────────────────────────
    if (pc.u_colorCount > 0) {
        // Sector index based on world angle (so regions align with kaleidoscope)
        float sectorAngle = TAU / N;

        // Map aWorld into [0, TAU)
        float aNorm = aWorld;
        if (aNorm < 0.0) aNorm += TAU * ceil(-aNorm / TAU);
        aNorm = mod(aNorm, TAU);

        int sectorIndex = int(floor(aNorm / sectorAngle));  // 0..N-1

        // --- KEY CHANGE: no clamp; scale radius so rings can extend off-screen
        // rScaled = 1.0 corresponds to radius = RING_SPAN * (screen "unit" radius)
        float rScaled = rWorld / RING_SPAN;

        // Pick ring by scaled radius (still clamp index into valid range)
        int ringIndex = int(floor(rScaled * float(REGION_RINGS)));
        ringIndex = clamp(ringIndex, 0, REGION_RINGS - 1);

        // Region id → color
        int regionId = sectorIndex + ringIndex;
        int colorIdx = (pc.u_colorCount > 0) ? (regionId % pc.u_colorCount) : 0;

        // --- Soft masks with adjustable thickness so a ring can cover the whole view
        // Sector softness: distance to nearest sector boundary (unchanged)
        float sectorCenter = (float(sectorIndex) + 0.5) * sectorAngle;
        float dSector = abs(atan(sin(aWorld - sectorCenter), cos(aWorld - sectorCenter)));
        float mSector = 1.0 - smoothstep(sectorAngle * 0.5 - SECTOR_SMOOTH,
                                         sectorAngle * 0.5,
                                         dSector);

        // Ring softness: distance to ring center in the *scaled* radius domain
        float ringCenter = (float(ringIndex) + 0.5) / float(REGION_RINGS);           // in [0,1]
        float ringHalf   = RING_THICKNESS_MULT * (0.5 / float(REGION_RINGS));        // widened
        float dRing      = abs(rScaled - ringCenter);
        float mRing      = 1.0 - smoothstep(ringHalf - RING_SMOOTH, ringHalf, dRing);

        // Final region mask (can reach ~1.0 everywhere if RING_THICKNESS_MULT is big)
        float mRegion = clamp(mSector * mRing, 0.0, 1.0);

        // --- Use mRegion exactly as before for hue snap + grading toward target
        vec3  targetRGB = pc.u_colors[colorIdx];
        float targetHue = rgb2hsv(targetRGB).x;

        vec3 hsv = rgb2hsv(col);
        float snapS = REGION_SNAP_STRENGTH * mRegion;
        hsv.x = hueLerp01(hsv.x, targetHue, snapS);
        vec3 snapped = hsv2rgb(hsv);

        vec3 graded = mix(snapped, mix(snapped, targetRGB, 0.5), REGION_GRADE_STRENGTH * mRegion);
        col = graded;
    }

    outColor = vec4(col, 1.0);
}
