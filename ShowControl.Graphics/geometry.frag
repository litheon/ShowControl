#version 450

layout (location = 0) out vec4 outColor;

layout (push_constant) uniform Push {
    vec2  u_resolution;   // pixels
    float u_time;         // seconds
} pc;

// -------------------- Auto-rotating palette (no external colors) --------------------
const float PALETTE_CYCLE_SEC = 18.0; // how long to morph from one palette to the next
const float PALETTE_HOLD_SEC  = 4.0;  // optional hold at each palette before morph
const float TAU = 6.28318530718;

// Basic hash -> [0,1)
float hash11(float x){
    return fract(sin(x * 114.5143) * 8123.123);
}

// Build one cosine palette param set from an index
void genPaletteParams(float idx, out vec3 A, out vec3 B, out vec3 C, out vec3 D) {
    // Random-ish but curated ranges for neon-ish looks
    A = vec3(0.40 + 0.25*hash11(idx+1.0),
             0.35 + 0.25*hash11(idx+2.0),
             0.40 + 0.25*hash11(idx+3.0));

    B = vec3(0.30 + 0.35*hash11(idx+4.0),
             0.25 + 0.35*hash11(idx+5.0),
             0.30 + 0.35*hash11(idx+6.0));

    C = vec3(0.80 + 0.60*hash11(idx+7.0),     // frequency multipliers
             0.80 + 0.60*hash11(idx+8.0),
             0.80 + 0.60*hash11(idx+9.0));

    D = vec3(0.10 + 0.80*hash11(idx+10.0),    // phase offsets
             0.10 + 0.80*hash11(idx+11.0),
             0.10 + 0.80*hash11(idx+12.0));
}

// Cosine palette
vec3 cosPalette(float t, vec3 A, vec3 B, vec3 C, vec3 D){
    return A + B * cos(TAU * (C * t + D));
}

// Smoothstep with a little easing
float ease(float x){ x = clamp(x,0.0,1.0); return x*x*(3.0-2.0*x); }

// Returns palette params smoothly morphed over time t
void autoPalette(float t, out vec3 A, out vec3 B, out vec3 C, out vec3 D) {
    float phase  = t / (PALETTE_CYCLE_SEC + PALETTE_HOLD_SEC);
    float ip     = floor(phase);            // which palette pair
    float local  = fract(phase) * (PALETTE_CYCLE_SEC + PALETTE_HOLD_SEC);
    float f      = clamp((local - PALETTE_HOLD_SEC) / PALETTE_CYCLE_SEC, 0.0, 1.0); // 0..1 morph
    f = ease(f);

    vec3 A0,B0,C0,D0, A1,B1,C1,D1;
    genPaletteParams(ip,   A0,B0,C0,D0);
    genPaletteParams(ip+1.0, A1,B1,C1,D1);

    A = mix(A0, A1, f);
    B = mix(B0, B1, f);
    C = mix(C0, C1, f);
    D = mix(D0, D1, f);
}

// -------------------- Visual field (demo: dynamic plasma) --------------------
float sizzle(vec2 p){ return sin(p.x)*cos(p.y); }

void main(){
    float t = pc.u_time;

    // UV in [-1,1] with aspect fix
    vec2 uv = (gl_FragCoord.xy / pc.u_resolution) * 2.0 - 1.0;
    uv.x *= pc.u_resolution.x / pc.u_resolution.y;

    // Mild domain warp for motion
    vec2 wp = uv * 2.5 + vec2(0.6*t, -0.5*t);
    float w  = 0.35 * sizzle(wp) + 0.25 * sizzle(wp*1.7 + 1.3);

    // Build a dynamic scalar field (plasma-ish)
    float f =
        0.55 * sin(3.0*uv.x + 4.0*uv.y + 1.2*t) +
        0.45 * sin(5.0*uv.x - 2.0*uv.y - 0.9*t) +
        0.35 * sin(6.0*length(uv + 0.35*vec2(sin(t*0.3), cos(t*0.27))) - 1.1*t) +
        0.30 * sin(8.0*atan(uv.y, uv.x) + 0.8*t) +
        0.40 * w;

    // Normalize & mild contrast
    float val = 0.5 + 0.5 * tanh(1.1 * f);

    // Get auto-changing palette params and shade
    vec3 A,B,C,D;
    autoPalette(t, A,B,C,D);
    vec3 col = cosPalette(val + 0.07*w + 0.05*sin(0.2*t), A,B,C,D);

    // Saturation + exposure pop (internal, no external inputs)
    float luma = dot(col, vec3(0.299,0.587,0.114));
    col = mix(vec3(luma), col, 1.22);   // saturation
    col *= 1.28;                        // exposure

    // Soft vignette to frame it
    float r = length(uv);
    col *= 1.0 - 0.55 * smoothstep(0.7, 1.1, r);

    outColor = vec4(clamp(col, 0.0, 1.0), 1.0);
}
