#version 450

// ───────────────────────────────────────────────────────────────────────────
// AudioLite reference visualizer.
//
// This shader is the proof that the AudioLite contract is sufficient: it may
// consume ONLY the push-constant block below. If an effect needs something
// that is not in the struct, the field gets added to the contract (append-only,
// version bump) — never reached around for out of internal analysis state.
//
// This block is the CANONICAL GLSL mirror of GraphicsHost.PushData. Shaders
// that don't care about audio declare only a prefix (see geometry.frag); any
// shader that declares the audio fields must match these offsets exactly.
// ───────────────────────────────────────────────────────────────────────────

layout(location=0) out vec4 outColor;

layout(push_constant) uniform Push {
    vec2  u_resolution;          //  8
    float u_time;                //  4
    float u_distortionStrength;  //  4
    float u_hueTolerance;        //  4
    float u_falloff;             //  4
    float u_param1;              //  4  > 0.5 enables the latency-measurement flash mode
    float u_param2;              //  4
    float u_param3;              //  4
    float u_param4;              //  4  = 40 bytes (legacy prefix)
    float a_energy;              //  4  0..1  level, attack/release applied
    float a_impulse;             //  4  0..1  transient, magnitude = significance, decaying
    float a_tension;             //  4  0..1  build accumulation, holds through the gap
    float a_phase;               //  4  0..1  bar phase, pre-advanced to display time
    float a_hue;                 //  4  0..1  palette angle from chroma
    float a_grain;               //  4  0..1  tonal → noisy
    float a_motion;              //  4  0..~3 rate multiplier
    float a_spread;              //  4  0..1  spatial extent  = 72 bytes total
} pc;

const float TAU = 6.283185307179586;

// The one allowed per-effect knob: a response curve exponent on impulse.
// "Punchier" is a bigger exponent; the firing instant never moves.
const float IMPULSE_RESPONSE_EXP = 1.0;

vec3 hsv2rgb(vec3 hsv) {
    vec3 K = vec3(1.0, 2.0/3.0, 1.0/3.0);
    vec3 p = abs(fract(vec3(hsv.x) + K.yzx) * 6.0 - 3.0);
    return hsv.z * mix(vec3(1.0), clamp(p - 1.0, 0.0, 1.0), hsv.y);
}

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

void main() {
    // ── Latency-measurement flash mode (u_param1 high): slam full white on the
    // beat-phase zero crossing and on a strong impulse. This is what the camera
    // rig times against; see LatencyModel.PipelineSeconds.
    if (pc.u_param1 > 0.5) {
        float beatPhase = fract(pc.a_phase * 4.0);
        bool onBeat = beatPhase < 0.10;
        bool onHit = pc.a_impulse > 0.8;
        outColor = (onBeat || onHit) ? vec4(1.0) : vec4(0.0, 0.0, 0.0, 1.0);
        return;
    }

    vec2 res = pc.u_resolution;
    vec2 p = (2.0 * gl_FragCoord.xy - res) / res.y;

    // spread drives the sampling scale: the picture collapses toward the centre
    // during a build (spread → 0) and fills the frame at the drop (spread → 1).
    p /= max(0.25 + 1.25 * pc.a_spread, 0.05);

    float r = length(p);
    float ang = atan(p.y, p.x);

    // motion scales animation rate. (A raw multiplier on u_time jumps when motion
    // changes; the contract's future AudioWarpedTime field is the continuous fix.
    // Acceptable in the reference visualizer.)
    float t = mod(pc.u_time * pc.a_motion, 6000.0);

    // Radial rings breathing with energy, rotating bar-phase hand.
    float rings = 0.5 + 0.5 * sin(10.0 * r - pc.a_phase * TAU + 2.0 * t);
    float hand = smoothstep(0.25, 0.0, abs(fract((ang / TAU) - pc.a_phase + 0.5) - 0.5));

    float impulse = pow(clamp(pc.a_impulse, 0.0, 1.0), IMPULSE_RESPONSE_EXP);

    // Impulse fires an expanding ring: born at the centre, pushed outward as it decays.
    float ringR = 1.2 * (1.0 - impulse);
    float impulseRing = impulse * exp(-abs(r - ringR) * 14.0);

    // Hue from chroma, slightly spread along the radius; tension desaturates and
    // adds a rising core glow so a build visibly gathers itself.
    float hue = fract(pc.a_hue + 0.08 * r);
    float sat = clamp(0.85 - 0.5 * pc.a_tension, 0.0, 1.0);
    float coreGlow = pc.a_tension * exp(-r * 3.0);

    float brightness = pc.a_energy * (0.25 + 0.75 * rings) * (0.6 + 0.4 * hand);
    brightness += impulseRing + coreGlow;

    vec3 col = hsv2rgb(vec3(hue, sat, clamp(brightness, 0.0, 1.0)));

    // grain: tonal material stays smooth, noisy material shimmers.
    float noise = hash(gl_FragCoord.xy + vec2(t * 60.0, 0.0));
    col *= mix(1.0, 0.55 + 0.9 * noise, clamp(pc.a_grain, 0.0, 1.0) * 0.6);

    outColor = vec4(col, 1.0);
}
