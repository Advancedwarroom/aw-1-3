shader_type canvas_item;

// ==================== 模式选择 ====================
// 0 = LUT模式（贴图R通道作为调色板索引）
// 1 = 兼容模式（自动检测红/蓝区域并替换，适配现有红蓝双色贴图）
// 2 = 模板色表模式（参照 Commander Wars 的 color table：以红/蓝原版贴图为模板，
//     模板色相带内的像素按亮度阶梯映射到「暗→势力主色→亮」，肤色等共享色排除）
uniform int mode : hint_range(0, 2) = 2;

// ==================== 模板色表模式参数 ====================
// 0 = 红色模板（OS 红色贴图，P1 及所有无专属贴图的势力）  1 = 蓝色模板（BM 蓝色贴图，P2）
uniform int template_side : hint_range(0, 1) = 0;
// 模板亮度阶梯（Rec.601 亮度）：暗部 / 主色 / 高光，C# 按模板自动设置
uniform float tpl_dark = 0.15;
uniform float tpl_mid = 0.28;
uniform float tpl_light = 0.80;
uniform float tpl_min_sat = 0.35;   // 模板色最低饱和度（排除灰、金属）
uniform float tpl_min_val = 0.25;   // 模板色最低明度（排除描边黑）
uniform float red_hue_min = 330.0;  // 红色模板色相下限（含品红可调低，如水晶 300）
uniform float red_hue_max = 40.0;   // 红色模板色相上限（含橙）
uniform float blue_hue_min = 180.0;
uniform float blue_hue_max = 265.0;
uniform float dark_factor = 0.40;   // 暗部 = 主色 × 该值
uniform float light_mix = 0.85;     // 高光 = 主色向白色混合的比例

// ==================== LUT模式参数 ====================
uniform sampler2D palette : repeat_disable, filter_nearest;

// ==================== 兼容模式参数 ====================
uniform vec4 primary_color : source_color = vec4(1.0, 0.2, 0.2, 1.0);   // 势力主色
uniform vec4 secondary_color : source_color = vec4(0.2, 0.4, 1.0, 1.0);  // 势力副色

// 颜色检测阈值（在Inspector中可调，找到最适合你贴图的值）
uniform float red_threshold : hint_range(0.0, 0.8) = 0.2;      // R比G/B高多少算红区
uniform float blue_threshold : hint_range(0.0, 0.8) = 0.2;     // B比R/G高多少算蓝区
uniform float sat_threshold : hint_range(0.0, 1.0) = 0.35;     // 饱和度门槛（0.35=排除肤色/棕色）
uniform float min_channel : hint_range(0.0, 1.0) = 0.45;       // 主导色通道最低值（R>0.45或B>0.45）
uniform float max_other : hint_range(0.0, 0.8) = 0.4;          // 其他通道最高值（排除偏橙/肤色）

// ==================== 通用参数 ====================
// 全局色调乘数（用于待机变暗、选中高亮等）
uniform vec4 tone : source_color = vec4(1.0, 1.0, 1.0, 1.0);

// 是否启用着色（false=原图直出）
uniform bool enabled = true;

// P-1/P0 特殊色调（可选覆盖）
uniform vec4 override_color : source_color = vec4(0.0, 0.0, 0.0, 0.0);
uniform bool use_override = false;

// ==================== 辅助函数 ====================
// 计算饱和度 (0~1)
float get_saturation(vec3 rgb) {
    float max_c = max(max(rgb.r, rgb.g), rgb.b);
    float min_c = min(min(rgb.r, rgb.g), rgb.b);
    return max_c - min_c;
}

// 检测是否是红色模板区域
// 条件：R够高、G和B够低、饱和度够高（排除肤色/棕色/灰色）
bool is_red_dominant(vec3 rgb) {
    float sat = get_saturation(rgb);
    if (sat < sat_threshold) return false;       // 饱和度太低=肤色/灰/棕，跳过
    if (rgb.r < min_channel) return false;       // R不够高
    if (rgb.g > max_other || rgb.b > max_other) return false; // G或B太高=偏橙/偏紫，跳过
    return (rgb.r - rgb.g > red_threshold) && (rgb.r - rgb.b > red_threshold);
}

// 检测是否是蓝色模板区域
bool is_blue_dominant(vec3 rgb) {
    float sat = get_saturation(rgb);
    if (sat < sat_threshold) return false;
    if (rgb.b < min_channel) return false;
    if (rgb.r > max_other || rgb.g > max_other) return false;
    return (rgb.b - rgb.r > blue_threshold) && (rgb.b - rgb.g > blue_threshold);
}

vec3 rgb2hsv(vec3 c) {
    vec4 K = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    vec4 p = mix(vec4(c.bg, K.wz), vec4(c.gb, K.xy), step(c.b, c.g));
    vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

// 红/蓝原版贴图共用的肤色、皮革色（两套贴图里都出现 → 不属于势力色）
const vec3 SHARED_COLORS[6] = vec3[6](
    vec3(216.0, 128.0, 80.0) / 255.0, vec3(124.0, 80.0, 76.0) / 255.0,
    vec3(186.0, 156.0, 100.0) / 255.0, vec3(188.0, 136.0, 112.0) / 255.0,
    vec3(222.0, 132.0, 82.0) / 255.0, vec3(248.0, 216.0, 128.0) / 255.0
);

bool is_template_color(vec3 rgb) {
    for (int i = 0; i < 6; i++) {
        if (distance(rgb, SHARED_COLORS[i]) < 0.02) return false;
    }
    vec3 hsv = rgb2hsv(rgb);
    if (hsv.y < tpl_min_sat || hsv.z < tpl_min_val) return false;
    float h = hsv.x * 360.0;
    if (template_side == 0) return h >= red_hue_min || h <= red_hue_max;   // 红→橙
    return h >= blue_hue_min && h <= blue_hue_max;                         // 青→蓝→蓝紫
}

vec3 map_to_faction(vec3 rgb) {
    float l = dot(rgb, vec3(0.299, 0.587, 0.114));
    vec3 p = primary_color.rgb;
    vec3 dark = p * dark_factor;
    vec3 light = mix(p, vec3(1.0), light_mix);
    if (l <= tpl_mid) {
        float t = clamp((l - tpl_dark) / max(tpl_mid - tpl_dark, 0.001), 0.0, 1.0);
        vec3 c = mix(dark, p, t);
        if (l < tpl_dark) c *= l / max(tpl_dark, 0.001);
        return c;
    }
    float t = clamp((l - tpl_mid) / max(tpl_light - tpl_mid, 0.001), 0.0, 1.0);
    return mix(p, light, t);
}

void fragment() {
    vec4 tex = texture(TEXTURE, UV);
    // 继承节点（及父节点）Modulate 的透明度：潜行半透明、淡入淡出等效果依赖它
    // RGB 不乘 COLOR：变暗统一走 tone，避免与 Modulate 双重变暗
    float inherited_alpha = COLOR.a;
    
    // 完全透明或禁用时不处理
    if (!enabled || tex.a < 0.01) {
        COLOR = vec4(tex.rgb * tone.rgb, tex.a * tone.a * inherited_alpha);
    }
    else {
        vec3 rgb = tex.rgb;
        vec3 result = rgb;
        
        if (mode == 0) {
            // ===== LUT模式：R通道作为调色板索引 =====
            float index = clamp(tex.r, 0.0, 1.0);
            vec2 palette_uv = vec2(index, 0.0);
            vec4 replaced = texture(palette, palette_uv);
            result = replaced.rgb;
        }
        else if (mode == 2) {
            // ===== 模板色表模式 =====
            if (is_template_color(rgb)) result = map_to_faction(rgb);
        }
        else if (mode == 3) {
            // ===== 灰度重着色模式：中性灰贴图（导弹井/中立兵器）整体按亮度映射到势力色 =====
            // 亮度曲线：暗部压到 0.25×主色（保住描边深度），高光逐渐混白（保住高光）
            // 彩色像素原样保留；纯黑描边(v<tpl_min_val)不处理
            float g_sat = get_saturation(rgb);
            vec3 g_hsv = rgb2hsv(rgb);
            if (g_sat < tpl_min_sat && g_hsv.z >= tpl_min_val) {
                float l = dot(rgb, vec3(0.299, 0.587, 0.114));
                vec3 p = primary_color.rgb;
                if (l <= 0.55) {
                    // 暗部伽马曲线：描边沉下去，中灰渐入主色
                    result = p * pow(l / 0.55, 1.25);
                } else {
                    // 高光：主色渐白，保住受光面
                    float t = clamp((l - 0.55) / 0.45, 0.0, 1.0);
                    result = mix(p, vec3(1.0), t * 0.65);
                }
            }
        }
        else {
            // ===== 兼容模式：红蓝区域自动检测替换 =====
            if (is_red_dominant(rgb)) {
                // 红色模板区 → 替换为势力主色（保留原明暗层次）
                float luminance = dot(rgb, vec3(0.299, 0.587, 0.114));
                result = primary_color.rgb * (0.4 + luminance * 1.2);
            }
            else if (is_blue_dominant(rgb)) {
                // 蓝色模板区 → 替换为势力副色
                float luminance = dot(rgb, vec3(0.299, 0.587, 0.114));
                result = secondary_color.rgb * (0.4 + luminance * 1.2);
            }
            // 其他颜色（肤色、金属、阴影等）保持不变
        }
        
        // P-1/P0 特殊色调覆盖
        if (use_override) {
            float luminance = dot(rgb, vec3(0.299, 0.587, 0.114));
            result = override_color.rgb * luminance * 1.5;
        }
        
        // 应用全局色调（待机变暗等）
        COLOR = vec4(result * tone.rgb, tex.a * tone.a * inherited_alpha);
    }
}
