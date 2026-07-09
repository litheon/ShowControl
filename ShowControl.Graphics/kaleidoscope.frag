#version 450

layout(location=0) out vec4 outColor;

// ───────────────────────────────────────────────────────────
// Push constants (scalars only — color data is in SSBO)
// ───────────────────────────────────────────────────────────
layout(push_constant) uniform Push {
    vec2  u_resolution;          //  8
    float u_time;                //  4
    float u_distortionStrength;  //  4
    float u_hueTolerance;        //  4
    float u_falloff;             //  4
    float u_param1;              //  4  sectors (N)
    float u_param2;              //  4  animation speed offset
    float u_param3;              //  4  radial color ring density (k base)
    float u_param4;              //  4  flow pattern frequency  = 40 bytes total
} pc;

// ───────────────────────────────────────────────────────────
// SSBO — color state (unbounded, written CPU-side each frame)
//
// Layout:
//   data[0 .. currentCount-1]                  = current colors
//   data[currentCount .. currentCount+previousCount-1] = snapshot colors
//
// Each vec4: .xyz = linear RGB, .w = per-color fade weight [0..1]
// ───────────────────────────────────────────────────────────
layout(std430, set=0, binding=0) readonly buffer ColorState {
    int   currentCount;
    int   previousCount;
    float transitionT;  // 0..1 — how far current→previous cross-fade has progressed
    float _pad;
    vec4  data[];       // currentCount + previousCount entries
} cs;

vec4 currentColor(int i)  { return cs.data[i]; }
vec4 previousColor(int i) { return cs.data[cs.currentCount + i]; }

// ───────────────────────────────────────────────────────────
// Tunables (no ABI changes)
// ───────────────────────────────────────────────────────────
const float PI  = 3.141592653589793;
const float TAU = 6.283185307179586;

// Distortion & highlight/pulse (from earlier)
const float HIGHLIGHT_GAIN = 2.0;
const float PULSE_GAIN     = 2.5;
const float PULSE_SPEED    = 6.2831; // ~1 Hz

// Regional bias strength — driven by u_param4 (default 0.55)
const float REGION_GRADE_STRENGTH = 0.55;  // 0..1 direct color blend toward assigned color

// Region layout
const int   REGION_RINGS   = 6;     // number of radial rings
const int   REGION_SECTORS = 10;    // fixed sector count for regional color bias
const float RING_SMOOTH    = 0.015; // soften ring edges (in normalized radius)
const float SECTOR_SMOOTH  = 0.015; // soften sector edges (in radians)
const float RING_SPAN            = 1.5;  // >1.0 makes rings extend past screen radius
const float RING_THICKNESS_MULT  = 1.8;   // >1.0 thickens rings; can cover whole viewport if large
// SPIRAL_TWIST is now driven by u_param1 (default 2.5)

// ───────────────────────────────────────────────────────────
// Helpers
// ───────────────────────────────────────────────────────────
vec3 palette(float t){
    vec3 c = 0.5 + 0.5 * cos(TAU * (vec3(0.0,0.33,0.67)+t));
    // Warm bias: lift red, slightly lift green, reduce blue
    c.r = min(c.r * 0.95, 1.0);
    c.g = min(c.g * 1.10, 1.0);
    c.b *= 0.60;
    return c;
}

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
    if (cs.currentCount <= 0) return 0.0;
    float tolHue = clamp(pc.u_hueTolerance / TAU, 0.0, 0.5);
    float w = 0.0;
    for (int i = 0; i < cs.currentCount; ++i) {
        vec4  c   = currentColor(i);
        float selHue01 = rgb2hsv(c.xyz).x;
        float d = hueDistance01(hue01, selHue01);
        float k = max(0.0, 1.0 - smoothstep(0.0, tolHue, d)); // clamp before pow to prevent NaN
        k = pow(k, max(pc.u_falloff, 0.0001));
        k *= c.w;  // scale by per-color fade weight
        w = max(w, k);
    }
    return clamp(w, 0.0, 1.0);
}

float nearestSelectedHue01(float h) {
    if (cs.currentCount <= 0) return h;
    float bestH = h;
    float bestD = 1.0;
    for (int i = 0; i < cs.currentCount; ++i) {
        float sh = rgb2hsv(currentColor(i).xyz).x;
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

// ───────────────────────────────────────────────────────────
// Main
// ───────────────────────────────────────────────────────────
void main(){
    vec2 res  = pc.u_resolution;
    // Wrap time to prevent float precision loss after long runs.
    // 6000 is ~10 LCM periods of the various frequency multipliers,
    // large enough that no visual discontinuity is perceptible.
    float time = mod(pc.u_time, 6000.0);

    // Base coords
    vec2 p = (2.0 * gl_FragCoord.xy - res) / res.y;
    float aWorld = atan(p.y, p.x);       // world angle (no sectorization yet)
    float rWorld = length(p);            // world radius (0..~1+)

    // Continuous spiral (no sector folding — seamless wrap)
    float spiralTwist = pc.u_param1;
    float a = aWorld + rWorld * spiralTwist;
    float r = rWorld;
    vec2 dir = vec2(cos(a), sin(a));
    p = dir * r;

    // ── Pass 1: compute base hue from original pattern
    float t  = time + pc.u_param2;
    float k  = pc.u_param3 + 2.0 * sin(t * 0.7);
    float bands = 0.5 + 0.5 * sin(20.0 * r + 6.0 * a + t * 2.0);
    float flow  = 0.5 + 0.5 * sin(pc.u_param4 * r - 3.0 * t + 2.0 * sin(8.0 * a + t));
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
    float flow2  = 0.5 + 0.5 * sin(pc.u_param4 * r2 - 3.0 * t + 2.0 * sin(8.0 * a2 + t));
    float hue2   = fract(r2 * k - 0.1 * t + 0.25 * bands2);

    vec3 col = palette(hue2);
    col *= pow(flow2, 1.5) * (0.3 + 0.7 * pow(1.0 - clamp(r2, 0.0, 1.0), 3.0));
    col *= 1.2 / max(1e-6, (1.0 + dot(p2, p2)));

    // ───────────────────────────────────────────────────────
    // Highlight + Pulse on matched hues
    // ───────────────────────────────────────────────────────
    float w2 = hueMatchWeight(hue2);
    float pulse = 0.5 + 0.5 * sin(time * PULSE_SPEED + hue2 * 12.0);
    col = mix(col, col * HIGHLIGHT_GAIN, w2);
    col *= (1.0 + w2 * pulse * PULSE_GAIN);

    // ───────────────────────────────────────────────────────
    // Regional palette bias
    //   - Partition by sector (angular) and ring (radial)
    //   - Assign each region to a color index
    //   - Snap hue & gently grade toward that color
    //   - Cross-fade using SSBO snapshot when count changes
    // ───────────────────────────────────────────────────────
    // Run regional bias when there are active colors OR when a snapshot transition is
    // still fading out (e.g., all colors removed but cross-fade not yet complete).
    bool hasCurrent   = cs.currentCount > 0;
    bool hasTransition = cs.previousCount > 0 && cs.transitionT < 0.999;
    if (hasCurrent || hasTransition) {
        // Sector index based on spiral-twisted angle (fixed sector count for color regions)
        float sectorAngle = TAU / float(REGION_SECTORS);

        float aNorm = aWorld + rWorld * spiralTwist;
        if (aNorm < 0.0) aNorm += TAU * ceil(-aNorm / TAU);
        aNorm = mod(aNorm, TAU);

        int sectorIndex = int(floor(aNorm / sectorAngle));  // 0..REGION_SECTORS-1

        // Scale radius so rings can extend off-screen
        float rScaled = rWorld / RING_SPAN;

        // Pick ring by scaled radius
        int ringIndex = int(floor(rScaled * float(REGION_RINGS)));
        ringIndex = clamp(ringIndex, 0, REGION_RINGS - 1);

        int regionId = sectorIndex + ringIndex;

        // Sector softness (use twisted angle for spiral-consistent edges)
        float sectorCenter = (float(sectorIndex) + 0.5) * sectorAngle;
        float dSector = abs(atan(sin(aNorm - sectorCenter), cos(aNorm - sectorCenter)));
        float mSector = 1.0 - smoothstep(sectorAngle * 0.5 - SECTOR_SMOOTH,
                                         sectorAngle * 0.5,
                                         dSector);

        // Ring softness
        float ringCenter = (float(ringIndex) + 0.5) / float(REGION_RINGS);
        float ringHalf   = RING_THICKNESS_MULT * (0.5 / float(REGION_RINGS));
        float dRing      = abs(rScaled - ringCenter);
        float mRing      = 1.0 - smoothstep(ringHalf - RING_SMOOTH, ringHalf, dRing);

        float mRegion = clamp(mSector * mRing, 0.0, 1.0);

        // Start from current color assignment (zero when no current colors)
        vec3  targetRGB = vec3(0.0);
        float colorFade = 0.0;
        if (hasCurrent) {
            int colorIdx = regionId % cs.currentCount;
            vec4 curColor = currentColor(colorIdx);
            targetRGB = curColor.xyz;
            colorFade = curColor.w;
        }

        // Cross-fade with snapshot when count changed or colors are finishing fade-out
        if (hasTransition) {
            int  oldIdx  = clamp(regionId % cs.previousCount, 0, cs.previousCount - 1);
            vec4 oldColor = previousColor(oldIdx);
            targetRGB = mix(oldColor.xyz, targetRGB, cs.transitionT);
            colorFade = mix(oldColor.w,   colorFade,  cs.transitionT);
        }

        // Direct color blend — no HSV roundtrip, no threshold, purely continuous
        col = mix(col, mix(col, targetRGB, 0.65), 0.55 * mRegion * colorFade);
    }

    outColor = vec4(col, 1.0);
}
