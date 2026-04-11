#version 450

layout(location=0) out vec4 outColor;

// ── Push constants: identical contract to kaleidoscope.frag ──────────────────
layout(push_constant) uniform Push {
    vec2  u_resolution;
    float u_time;
    float u_distortionStrength;
    float u_hueTolerance;
    float u_falloff;
    float u_param1;   // blade count
    float u_param2;   // spin angle offset
    float u_param3;   // blade hue offset
    float u_param4;   // dot radius
} pc;

// ── SSBO color state: identical contract to kaleidoscope.frag ────────────────
layout(std430, set=0, binding=0) readonly buffer ColorState {
    int   currentCount;
    int   previousCount;
    float transitionT;
    float _pad;
    vec4  data[];
} cs;

vec4 currentColor(int i)  { return cs.data[i]; }
vec4 previousColor(int i) { return cs.data[cs.currentCount + i]; }

const float PI  = 3.141592653589793;
const float TAU = 6.283185307179586;

// ── Color space helpers ──────────────────────────────────────────────────────
vec3 rgb2hsv(vec3 c) {
    float cMax = max(c.r, max(c.g, c.b));
    float cMin = min(c.r, min(c.g, c.b));
    float delta = cMax - cMin;
    float h = 0.0;
    if (delta > 1e-6) {
        if      (cMax == c.r) h = mod((c.g - c.b) / delta, 6.0);
        else if (cMax == c.g) h = (c.b - c.r) / delta + 2.0;
        else                  h = (c.r - c.g) / delta + 4.0;
        h /= 6.0;
        if (h < 0.0) h += 1.0;
    }
    float s = (cMax <= 0.0) ? 0.0 : delta / cMax;
    return vec3(h, s, cMax);
}

vec3 hsv2rgb(vec3 hsv) {
    vec3 K = vec3(1.0, 2.0 / 3.0, 1.0 / 3.0);
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
        vec4  c = currentColor(i);
        float selHue01 = rgb2hsv(c.xyz).x;
        float d = hueDistance01(hue01, selHue01);
        float k = max(0.0, 1.0 - smoothstep(0.0, tolHue, d));
        k = pow(k, max(pc.u_falloff, 0.0001));
        k *= c.w;
        w = max(w, k);
    }
    return clamp(w, 0.0, 1.0);
}

// ── Circus palette ───────────────────────────────────────────────────────────
// s=0.92, v=0.82 → max component is 0.82 → output is NEVER white.
vec3 circusColor(float hue) {
    return hsv2rgb(vec3(fract(hue), 0.92, 0.82));
}

// ── Hash helpers ─────────────────────────────────────────────────────────────
float hash11(float n) { return fract(sin(n * 127.1) * 43758.5453); }
float hash21(vec2 p)  { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

// ── No-white guard ───────────────────────────────────────────────────────────
// Prevents SSBO bias from blending in near-white colors.
vec3 noWhite(vec3 col) {
    float maxC = max(col.r, max(col.g, col.b));
    // Hard cap: scale down proportionally to preserve hue
    col *= min(1.0, 0.88 / max(maxC, 0.001));
    // If still bright and desaturated (near gray), crush brightness
    maxC = max(col.r, max(col.g, col.b));
    float minC = min(col.r, min(col.g, col.b));
    float sat = (maxC > 0.001) ? (maxC - minC) / maxC : 1.0;
    float crush = (1.0 - smoothstep(0.0, 0.25, sat)) * smoothstep(0.35, 0.65, maxC);
    col *= 1.0 - crush * 0.72;
    return col;
}

// ── Main ─────────────────────────────────────────────────────────────────────
void main() {
    vec2  res  = pc.u_resolution;
    float time = pc.u_time;

    // Normalized coords: p.y in [-1, 1], p.x aspect-corrected
    vec2  p = (2.0 * gl_FragCoord.xy - res) / res.y;
    float r = length(p);
    float a = atan(p.y, p.x);  // [-PI, PI]

    // ── SSBO-driven distortion ────────────────────────────────────────────────
    float baseHue = fract(a / TAU + r * 1.8 + time * 0.07);
    float w = hueMatchWeight(baseHue);
    float strength = pc.u_distortionStrength;
    float dA = strength * w * 0.5  * sin(5.0 * r - 2.5 * time);
    float dR = strength * w * 0.22 * cos(3.0 * a + 1.8 * time);
    float r2 = max(0.0, r + dR);
    float a2 = a + dA;
    vec2  p2 = vec2(cos(a2), sin(a2)) * r2;

    // ── Layer 1: Spinning fan blades ─────────────────────────────────────────
    // Rotating wedges like a circus top/pinwheel, each a different vivid color.
    float N_BLADES    = pc.u_param1;
    float BLADE_WIDTH = TAU / N_BLADES;
    const float GAP_FRAC = 0.07;      // fraction at each edge that is dark

    float spinAngle  = a2 + time * 0.32 + pc.u_param2;
    float normAngle  = mod(spinAngle + PI, TAU);
    float bladeID    = floor(normAngle / BLADE_WIDTH);
    float bladePhase = fract(normAngle / BLADE_WIDTH);  // [0, 1) within blade

    // Dark seam between blades (bold clown-outfit outline feel)
    float gapMask = smoothstep(0.0, GAP_FRAC, bladePhase) *
                    smoothstep(1.0, 1.0 - GAP_FRAC, bladePhase);

    // Each blade holds a stable hue from the circus palette, drifting slowly
    float bladeHue = hash11(bladeID) + time * 0.016 + pc.u_param3;
    vec3  fanColor = circusColor(bladeHue) * gapMask;

    // Radial fade: black stub at center, fade out past screen edges
    float radFade = smoothstep(0.07, 0.18, r2) * (1.0 - smoothstep(1.1, 1.45, r2));
    fanColor *= radFade;

    // Zigzag radial stripes within each blade (circus-tent texture)
    float zigzag = step(0.5, fract(r2 * 5.5 - time * 0.22));
    fanColor = mix(fanColor, fanColor * 0.55, 0.28 * zigzag * gapMask * radFade);

    // ── Center bullseye: concentric rings expanding outward ───────────────────
    // Evokes the classic clown target / spinning bow tie.
    float bullPulse = sin(r2 * 24.0 - time * 5.2);           // rings ripple outward
    float bullFill  = max(0.0, bullPulse);                    // only lit half-cycles (dark gaps)
    float bullArea  = smoothstep(0.26, 0.0, r2);              // fade out beyond r2=0.26
    // Golden-ratio hue spacing per ring → maximally distinct adjacent colors
    float bullHue   = fract(floor(r2 * 12.0) * 0.618 + time * 0.045);
    vec3  bullColor = circusColor(bullHue) * bullFill;
    fanColor = mix(fanColor, bullColor, bullArea);             // bullseye overrides fan at center

    // ── Layer 2: Pulsing polka dots ───────────────────────────────────────────
    // Two interleaved square grids give hex-like dot packing.
    float dotScale = 3.0;
    vec2  dotUV = p2 * dotScale;

    vec2 la = fract(dotUV)            - 0.5;   // grid A local pos
    vec2 lb = fract(dotUV - vec2(0.5)) - 0.5;  // grid B local pos (offset)
    vec2 ca = floor(dotUV);
    vec2 cb = floor(dotUV - vec2(0.5));

    float da = length(la);
    float db = length(lb);

    vec2  dotCell = (da < db) ? ca : cb;
    float dotDist = min(da, db);

    float dotPhase = hash21(dotCell + vec2(3.7, 9.1)) * TAU;
    // Each dot drifts hue at its own rate (more chaotic, more clown)
    float dotDriftRate = 0.025 + 0.035 * hash21(dotCell + vec2(5.5, 2.3));
    float dotHue  = hash21(dotCell) + time * dotDriftRate;
    float dotR    = pc.u_param4 + 0.07 * sin(time * 1.9 + dotPhase);  // breathing radius

    // Solid dot center with dark outline ring (face-paint outline look)
    float outerMask = 1.0 - smoothstep(dotR - 0.015, dotR + 0.015, dotDist);
    float innerMask = 1.0 - smoothstep(dotR * 0.70 - 0.015, dotR * 0.70 + 0.015, dotDist);
    vec3  dotSurface = mix(vec3(0.0), circusColor(dotHue), innerMask);

    // ── Composite: dots overlay fan ───────────────────────────────────────────
    vec3 col = mix(fanColor, dotSurface, outerMask);

    // ── SSBO regional bias ────────────────────────────────────────────────────
    // Same sector/ring system as kaleidoscope so color grading works identically.
    const float REGION_GRADE   = 0.55;
    const int   N_RINGS        = 5;
    const float RING_SPAN      = 1.4;
    const float RING_SMOOTH    = 0.015;
    const float SEC_SMOOTH     = 0.015;
    const float RING_THICK     = 1.6;
    const float N_SEC          = 10.0;

    bool hasCurrent    = cs.currentCount > 0;
    bool hasTransition = cs.previousCount > 0 && cs.transitionT < 0.999;

    if (hasCurrent || hasTransition) {
        float secAngle = TAU / N_SEC;
        float aNorm    = mod(a + TAU, TAU);
        int   secIdx   = int(floor(aNorm / secAngle));

        float rScaled  = r / RING_SPAN;
        int   ringIdx  = clamp(int(floor(rScaled * float(N_RINGS))), 0, N_RINGS - 1);
        int   regionId = secIdx + ringIdx;

        float secCenter = (float(secIdx) + 0.5) * secAngle;
        float dSec      = abs(atan(sin(a - secCenter), cos(a - secCenter)));
        float mSec      = 1.0 - smoothstep(secAngle * 0.5 - SEC_SMOOTH, secAngle * 0.5, dSec);

        float ringCenter = (float(ringIdx) + 0.5) / float(N_RINGS);
        float ringHalf   = RING_THICK * 0.5 / float(N_RINGS);
        float dRing      = abs(rScaled - ringCenter);
        float mRing      = 1.0 - smoothstep(ringHalf - RING_SMOOTH, ringHalf, dRing);

        float mRegion = clamp(mSec * mRing, 0.0, 1.0);

        vec3  targetRGB = vec3(0.0);
        float colorFade = 0.0;

        if (hasCurrent) {
            int  ci = regionId % cs.currentCount;
            vec4 cc = currentColor(ci);
            targetRGB = cc.xyz;
            colorFade = cc.w;
        }

        if (hasTransition) {
            int  oi = clamp(regionId % cs.previousCount, 0, cs.previousCount - 1);
            vec4 oc = previousColor(oi);
            targetRGB = mix(oc.xyz, targetRGB, cs.transitionT);
            colorFade = mix(oc.w,   colorFade,  cs.transitionT);
        }

        col = mix(col, mix(col, targetRGB, 0.65), REGION_GRADE * mRegion * colorFade);
    }

    // ── Vignette (spotlight from center) ─────────────────────────────────────
    vec2  uv01      = gl_FragCoord.xy / res;
    float cornerDist = length(uv01 - 0.5) * 2.0;
    col *= 1.0 - smoothstep(0.80, 1.28, cornerDist) * 0.7;

    // ── No-white guard ────────────────────────────────────────────────────────
    col = noWhite(col);

    outColor = vec4(col, 1.0);
}
