using Godot;
using System;
using System.Collections.Generic;

// CoW 地形风格管理器（静态）:
// - 气候(Biome) x 风格(Style) 的全局选择, 对应 art/Grids/地形/palettes/ 下 64x64 调色板
// - 调色板经 terrain_palette.gdshader 查表解码: 源贴图像素(R,G)=调色板坐标(值/4)
// - 气候专属 GridType(沙漠/雪/废土系)固定用本气候调色板, 普通地形跟随全局气候
public static class TerrainStyleManager
{
    public enum Biome { Clear, Snow, Rain, Waste, Desert }
    public enum Style { AW1, SFW, AW2, AWDS, DoR }

    public const string CowRoot = "res://art/Grids/地形/cow";
    public const string PaletteRoot = "res://art/Grids/地形/palettes";
    public const string PaletteShaderPath = "res://asscets/shaders/terrain_palette.gdshader";

    public static event Action StyleChanged;

    private static Biome _biome = Biome.Clear;
    private static Style _style = Style.AW2;
    private static Shader _shader;
    private static readonly Dictionary<string, Texture2D> _paletteCache = new();
    private static readonly Dictionary<string, ShaderMaterial> _materialCache = new();

    public static Biome CurrentBiome
    {
        get => _biome;
        set { if (_biome != value) { _biome = value; Refresh(); } }
    }

    public static Style CurrentStyle
    {
        get => _style;
        set { if (_style != value) { _style = value; Refresh(); } }
    }

    /// <summary>当前全局调色板文件名(含缺失组合回退: rain 只有 aw2; waste 缺 aw1/aw2 回退 awds)</summary>
    public static string GetPaletteFileName(Biome biome, Style style)
    {
        string b = biome.ToString().ToLowerInvariant();
        string s = style.ToString().ToLowerInvariant();
        if (biome == Biome.Rain) return "palette_rain+aw2.png";
        // waste/desert 缺 aw1/aw2, 回退 awds
        if ((biome == Biome.Waste || biome == Biome.Desert) && (style == Style.AW1 || style == Style.AW2)) s = "awds";
        if (biome == Biome.Clear && style == Style.AW2) return "palette_clear.png"; // 原色即 AW2
        return $"palette_{b}+{s}.png";
    }

    public static Texture2D GetPalette(Biome biome, Style style)
    {
        string file = GetPaletteFileName(biome, style);
        if (_paletteCache.TryGetValue(file, out var tex) && tex != null) return tex;
        string path = $"{PaletteRoot}/{file}";
        tex = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        if (tex == null) GD.PushWarning($"[TerrainStyle] {TranslateGroup.T("调色板缺失")}: {path}");
        _paletteCache[file] = tex;
        return tex;
    }

    /// <summary>按调色板取共享材质(同调色板的地形共用一份, 风格切换时整体换色)</summary>
    public static ShaderMaterial GetMaterial(Biome biome, Style style)
    {
        string file = GetPaletteFileName(biome, style);
        if (_materialCache.TryGetValue(file, out var mat) && mat != null) return mat;
        _shader ??= GD.Load<Shader>(PaletteShaderPath);
        var palette = GetPalette(biome, style);
        if (_shader == null || palette == null) return null;
        mat = new ShaderMaterial { Shader = _shader };
        mat.SetShaderParameter("palette_texture", palette);
        _materialCache[file] = mat;
        return mat;
    }

    /// <summary>普通地形用的当前全局材质</summary>
    public static ShaderMaterial CurrentMaterial => GetMaterial(_biome, _style);

    public static void Refresh()
    {
        // 材质按调色板文件名缓存, 无需逐个更新; 通知监听者(地形网格/编辑器)重取材质与变体
        StyleChanged?.Invoke();
    }

    // ============ GridType -> CoW 地形目录映射 ============
    // 值: (CoW目录, 固定气候或null=跟随全局)
    private static readonly Dictionary<GridType, (string dir, Biome? fixedBiome)> _cowMap = new()
    {
        [GridType.GROUND] = ("plains", null),
        [GridType.FOREST] = ("forest", null),
        [GridType.HILL] = ("mountain", null),
        [GridType.SEA] = ("sea", null),
        [GridType.ROAD] = ("street", null),
        [GridType.RIVER] = ("river", null),
        [GridType.BEACH] = ("beach", null),
        [GridType.WASTELAND] = ("wasteland", null),
        [GridType.RUINS] = ("ruins", null),
        [GridType.RUINS2] = ("ruins", null),
        [GridType.RUINS3] = ("ruins", null),
        [GridType.REEF] = ("reaf", null),
        [GridType.TP] = ("teleporttile", null),
        [GridType.PIPE] = ("pipeline", null),
        [GridType.PASSABLEPIPE] = ("pipeline", null),
        [GridType.BRIDGE] = ("bridge", null),
        [GridType.PLASMA] = ("plasma", null),
        [GridType.METEORITE] = ("meteor", null),
        [GridType.CREEPER] = ("creeper", null),
        [GridType.WALL] = ("wall", null),
        [GridType.WEAK_WALL] = ("wall", null),
        [GridType.BROKENPIPE] = ("pipeline", null),
        [GridType.LANDFOG] = ("fog", null),
        [GridType.SEAFOG] = ("fog", null),
        // 沙漠系(CoW 有专属 desert 调色板)
        [GridType.DESERT_GROUND] = ("desert", Biome.Desert),
        [GridType.DESERT_FOREST] = ("desert_forest", Biome.Desert),
        [GridType.DESERT_HILL] = ("mountain", Biome.Desert),
        [GridType.DESERT_ROAD] = ("desert_path", Biome.Desert),
        [GridType.DESERT_RUINS] = ("ruins", Biome.Desert),
        [GridType.DESERT_WASTELAND] = ("desert", Biome.Desert),
        [GridType.DESERT_PIPE] = ("desert_pipeline", Biome.Desert),
        [GridType.DESERT_PIPESEAM] = ("desert_pipeline", Biome.Desert),
        [GridType.DESERT_BROKENPIPE] = ("desert_pipeline", Biome.Desert),
        // 雪系(固定雪调色板)
        [GridType.SNOW_GROUND] = ("snow", Biome.Snow),
        [GridType.SNOW_FOREST] = ("snow_forest", Biome.Snow),
        [GridType.SNOW_HILL] = ("mountain", Biome.Snow),
        [GridType.SNOW_ROAD] = ("snow_street", Biome.Snow),
        [GridType.SNOW_RUINS] = ("ruins", Biome.Snow),
        [GridType.SNOW_WASTELAND] = ("snow", Biome.Snow),
        [GridType.SNOW_PIPE] = ("snow_Pipeline", Biome.Snow),
        [GridType.SNOW_PIPESEAM] = ("snow_Pipeline", Biome.Snow),
        [GridType.SNOW_BROKENPIPE] = ("snow_Pipeline", Biome.Snow),
        // 废土系(固定废土调色板)
        [GridType.WASTE_GROUND] = ("waste", Biome.Waste),
        [GridType.WASTE_FOREST] = ("waste_forest", Biome.Waste),
        [GridType.WASTE_HILL] = ("mountain", Biome.Waste),
        [GridType.WASTE_ROAD] = ("waste_path", Biome.Waste),
        [GridType.WASTE_RUINS] = ("ruins", Biome.Waste),
        [GridType.WASTE_WASTELAND] = ("wasteland", Biome.Waste),
        [GridType.WASTE_PIPE] = ("waste_Pipeline", Biome.Waste),
        [GridType.WASTE_PIPESEAM] = ("waste_Pipeline", Biome.Waste),
        [GridType.WASTE_BROKENPIPE] = ("waste_Pipeline", Biome.Waste),
    };

    /// <summary>查询 GridType 的 CoW 映射; 返回 false 表示该地形未接入 CoW 渲染(走原有贴图)</summary>
    public static bool TryGetCowMapping(GridType type, out string cowDir, out Biome effectiveBiome)
    {
        if (_cowMap.TryGetValue(type, out var m))
        {
            cowDir = m.dir;
            effectiveBiome = m.fixedBiome ?? _biome;
            return true;
        }
        cowDir = null;
        effectiveBiome = _biome;
        return false;
    }
}
