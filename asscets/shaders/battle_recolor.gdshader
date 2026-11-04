shader_type canvas_item;

// CoW 剧场式战斗动画·染色 LUT：mask 贴图像素的 (R,G) 作为 256x256 查色表坐标
// 与 oxygine setColorTable(matrixMode) 完全等价
uniform sampler2D color_table : repeat_disable, filter_nearest;
uniform vec4 tone : source_color = vec4(1.0, 1.0, 1.0, 1.0);

void fragment() {
	vec4 tex = texture(TEXTURE, UV);
	if (tex.a < 0.01) {
		COLOR = vec4(0.0, 0.0, 0.0, 0.0);
	} else {
		ivec2 lut_uv = ivec2(clamp(tex.rg * 255.0, vec2(0.0), vec2(255.0)));
		vec4 rc = texelFetch(color_table, lut_uv, 0);
		COLOR = vec4(rc.rgb * tone.rgb, tex.a * COLOR.a);
	}
}
