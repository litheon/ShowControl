#version 450

layout(location=0) out vec4 outColor;

// Keep 16-byte alignment; many drivers expect push constants to follow std430-ish rules
layout(push_constant) uniform Push {
    vec2  u_resolution;  // 8
    float u_time;        // 4
    float _pad0;         // 4 (pad to 16)
} pc;

vec3 palette(float t){ return 0.5 + 0.5 * cos(6.28318 * (vec3(0.0,0.33,0.67)+t)); }

void main(){
    vec2 res = pc.u_resolution;
    float time = pc.u_time;

    vec2 p = (2.0 * gl_FragCoord.xy - res) / res.y;
    float a = atan(p.y, p.x);
    float r = length(p);

    const float N = 10.0;
    a = mod(a, 6.2831853 / N) - 3.1415926 / N;
    p = vec2(cos(a), sin(a)) * r;

    float t = time * 0.6;
    float k = 3.0 + 2.0 * sin(t * 0.7);
    float bands = 0.5 + 0.5 * sin(20.0 * r + 6.0 * a + t * 2.0);
    float flow  = 0.5 + 0.5 * sin(10.0 * r - 3.0 * t + 2.0 * sin(8.0 * a + t));
    float hue   = fract(r * k - 0.1 * t + 0.25 * bands);

    vec3 col = palette(hue);
    col *= pow(flow, 1.5) * (0.3 + 0.7 * pow(1.0 - clamp(r, 0.0, 1.0), 3.0));
    col *= 1.2 / (1.0 + dot(p, p));

    outColor = vec4(col, 1.0);
}
