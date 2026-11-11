shader_type canvas_item;
// ✅ 开创人2026-11-12: CoW正统建筑染色——贴图(R,G)二维索引势力色板, 明暗阶由色板自带
// 源码背书: CoW building.cpp setColorTable(palette, matrixMode) + colortables/*.png(256×256)
uniform sampler2D palette : filter_nearest;

void fragment() {
    vec4 tex = texture(TEXTURE, UV);
    if (tex.a < 0.01) {
        COLOR = vec4(0.0);
    } else {
        vec3 c = texture(palette, vec2(tex.r, tex.g)).rgb;
        COLOR = vec4(c, tex.a);
    }
}
