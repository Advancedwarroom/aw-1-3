// CoW 地形调色板解码：源贴图像素 (R,G) 是 64x64 调色板坐标（值/4 取整）
// 与 CoW biome_palettes 机制一致：整张图所有不透明像素经查表上色
shader_type canvas_item;

uniform sampler2D palette_texture : filter_nearest, repeat_disable;

void fragment() {
	vec4 src = texture(TEXTURE, UV);
	if (src.a < 0.01) {
		COLOR = src;
	} else {
		vec2 coord = floor(src.rg * 255.0 / 4.0);
		vec3 col = texture(palette_texture, (coord + 0.5) / 64.0).rgb;
		COLOR = vec4(col, src.a);
	}
}
