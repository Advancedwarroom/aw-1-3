shader_type canvas_item;
render_mode blend_mix;

// ✅ 色相旋转染色（2026-11-10 用户钦定：保饱和只换色）
// hue_shift is the delta from each source pixel hue to the faction's canonical hue.
uniform float hue_shift = 0.0;    // degrees
uniform float saturation = 1.0;   // P0 neutral = 0.12
// ✅ 2026-11-11 源色带守卫(默认-1.0=关闭, 双图层行为不变): 仅旋转 hue∈[center±range] 的像素, 其余原样输出。
// 单图层成品画(City/Base/Port/AirPort)由 Facility 传 center=GetBaseArtHue(), 只染队伍红, 不染米色/灰/白。
uniform float guard_center = -1.0;
uniform float guard_range = 0.08;  // 色相环比例 0~0.5

vec3 rgb2hsv(vec3 c) {
    vec4 K = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    vec4 p = mix(vec4(c.bg, K.wz), vec4(c.gb, K.xy), step(c.b, c.g));
    vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}
vec3 hsv2rgb(vec3 c) {
    vec4 K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    vec3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

void fragment() {
    vec4 tex = texture(TEXTURE, UV);
    vec3 hsv = rgb2hsv(tex.rgb);
    // ✅ 2026-11-11: GDShader禁止fragment()内return——守卫改if/else结构
    COLOR = tex;                         // 默认原样输出(守卫拦截或低饱和)
    bool in_band = guard_center < 0.0;   // 守卫关闭 = 全部像素在带内(双图层行为不变)
    if (guard_center >= 0.0) {
        float d = abs(fract(hsv.x - guard_center + 0.5) - 0.5);
        in_band = d <= guard_range;
    }
    if (in_band) {
        hsv.x = fract(hsv.x + hue_shift / 360.0);
        hsv.y *= saturation;
        COLOR = vec4(hsv2rgb(hsv), tex.a);
    }
}
