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

    /// <summary>按 GridType 取材质: 气候专属地形(沙漠/雪/废土系)强制固定气候调色板, 其余用传入的气候/风格</summary>
    public static ShaderMaterial GetMaterialFor(GridType type, Biome biome, Style style)
    {
        if (_cowMap.TryGetValue(type, out var m) && m.fixedBiome.HasValue)
            biome = m.fixedBiome.Value;
        return GetMaterial(biome, style);
    }

    /// <summary>从 GameManager 的 [Export] 配置同步静态状态(gm=null 时保持现状)</summary>
    public static void ApplyFromGameManager(GameManager gm)
    {
        if (gm == null) return;
        bool changed = _biome != gm.terrainBiome || _style != gm.terrainStyle;
        _biome = gm.terrainBiome;
        _style = gm.terrainStyle;
        if (changed) Refresh();
    }

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

    // ============ 基底地形(不空底): 特征地形下面垫基础地形, 对齐 CoW loadBaseTerrain ============
    /// <summary>返回该地形应垫在底下的基础地形; null=贴图自含地面无需基底</summary>
    public static GridType? GetCowBaseTerrain(GridType t) => t switch
    {
        // 海洋系: 海滩/礁石/漩涡/陨石/海雾 → 垫开阔海(CoW: SEA)
        GridType.BEACH or GridType.REEF or GridType.WHIRLPOOL
        or GridType.METEORITE or GridType.SEAFOG => GridType.SEA,
        // 气候特征 → 垫对应气候地面(CoW: SNOW/DESERT/WASTE)
        GridType.DESERT_FOREST or GridType.DESERT_HILL or GridType.DESERT_RUINS
        or GridType.DESERT_PIPE or GridType.DESERT_PIPESEAM or GridType.DESERT_BROKENPIPE => GridType.DESERT_GROUND,
        GridType.SNOW_FOREST or GridType.SNOW_HILL or GridType.SNOW_RUINS
        or GridType.SNOW_PIPE or GridType.SNOW_PIPESEAM or GridType.SNOW_BROKENPIPE => GridType.SNOW_GROUND,
        GridType.WASTE_FOREST or GridType.WASTE_HILL or GridType.WASTE_RUINS
        or GridType.WASTE_PIPE or GridType.WASTE_PIPESEAM or GridType.WASTE_BROKENPIPE => GridType.WASTE_GROUND,
        // 陆地特征: 树林/山地/管道/墙/藤蔓/等离子/废墟/传送台/陆雾 → 垫草地(CoW: PLAINS)
        GridType.FOREST or GridType.HILL or GridType.PIPE or GridType.PASSABLEPIPE
        or GridType.BROKENPIPE or GridType.PIPESEAM or GridType.WALL or GridType.WEAK_WALL
        or GridType.CREEPER or GridType.PLASMA or GridType.RUINS or GridType.RUINS2
        or GridType.RUINS3 or GridType.TP or GridType.LANDFOG => GridType.GROUND,
        _ => null,
    };

    // ============ 变体贴图解析(候选链, 兼容 CoW 各类命名) ============
    // CoW 命名不统一: street+style0+N+E.png / creeper+NE.png / sea+N+mask.png / plains+0.png ...
    // 统一用候选链尝试, 命中即缓存
    private static readonly Dictionary<string, string> _pathCache = new();

    // 方向型 CoW 目录(dirMask 位: N=1 E=2 S=4 W=8)
    private static readonly HashSet<string> _directionalDirs = new()
    {
        "street", "bridge", "pipeline", "plasma", "wall", "creeper", "sea", "river",
        "reaf", "teleporttile", "meteor", "fog",
        "snow_Pipeline", "waste_Pipeline", "desert_pipeline",
        "snow_street", "waste_path", "desert_path",
    };

    /// <summary>解析 GridType+方向掩码 到 CoW 贴图路径; null=无可用变体(回退旧贴图)</summary>
    public static string ResolveCowTexturePath(GridType type, int dirMask)
    {
        if (!TryGetCowMapping(type, out string cowDir, out _)) return null;
        string key = $"{cowDir}:{dirMask}";
        if (_pathCache.TryGetValue(key, out var cached)) return string.IsNullOrEmpty(cached) ? null : cached;
        string resolved = ResolveUncached(cowDir, type, dirMask);
        _pathCache[key] = resolved ?? "";
        return resolved;
    }

    private static string ResolveUncached(string cowDir, GridType type, int dirMask)
    {
        string root = $"{CowRoot}/{cowDir}";
        string baseName = cowDir switch
        {
            "ruins" => type switch  // 气候废墟有专属贴图
            {
                GridType.DESERT_RUINS => "desert_ruin",
                GridType.SNOW_RUINS => "snow_ruin",
                GridType.WASTE_RUINS => "waste_ruin",
                _ => "ruin",
            },
            "wasteland" => "desert_wasteland",
            "forest" or "snow_forest" or "desert_forest" or "waste_forest" => $"{cowDir}_style0",
            "street" or "bridge" or "snow_street" or "waste_path" or "desert_path" => $"{cowDir}+style0",
            _ => cowDir,
        };

        // 桥: aw3 语义——有竖无横=N+S, 有横无竖=E+W, 否则四通
        if (type == GridType.BRIDGE)
        {
            bool hasV = (dirMask & 0b0101) != 0, hasH = (dirMask & 0b1010) != 0;
            dirMask = (hasV && !hasH) ? 0b0101 : (hasH && !hasV) ? 0b1010 : 0b1111;
        }
        // 断桥: CoW destroyedweld 只有两方向
        if (type is GridType.BROKENPIPE or GridType.DESERT_BROKENPIPE or GridType.SNOW_BROKENPIPE or GridType.WASTE_BROKENPIPE)
        {
            string weld = cowDir == "pipeline" ? "destroyedweld" : $"{cowDir.Split('_')[0]}_destroyedweld";
            bool h = (dirMask & 0b1010) != 0 && (dirMask & 0b0101) == 0;
            return FirstExists($"{root}/{weld}+{(h ? "E+W" : "N+S")}.png", $"{root}/{weld}+E+W.png");
        }
        // 海: 方向=邻陆侧(与海邻接掩码相反); 0=开阔海
        if (cowDir == "sea")
        {
            int land = (~dirMask) & 0b1111;
            return land == 0
                ? FirstExists($"{root}/sea+mask.png")
                : FirstExists($"{root}/sea+{PlusJoin(land)}+mask.png", $"{root}/sea+mask.png");
        }
        // 河: 单流向有专用瓦; 其余先用池塘(含全岸)兜底, 边检测后续迭代
        if (cowDir == "river")
        {
            if (dirMask is 1 or 2 or 4 or 8)
                return FirstExists($"{root}/river+{ConcatJoin(dirMask)}+mask.png", $"{root}/river+0+land+N+E+S+W+mask.png");
            return FirstExists($"{root}/river+0+land+N+E+S+W+mask.png", $"{root}/river+N+mask.png");
        }
        // 通用方向型
        if (_directionalDirs.Contains(cowDir) && dirMask != 0)
        {
            return FirstExists(
                $"{root}/{baseName}+{PlusJoin(dirMask)}.png",
                $"{root}/{baseName}+{ConcatJoin(dirMask)}.png",
                $"{root}/{baseName}+{PlusJoin(dirMask)}+mask.png",
                $"{root}/{baseName}+{ConcatJoin(dirMask)}+mask.png",
                $"{root}/{baseName}.png",
                $"{root}/{baseName}+0.png",
                $"{root}/{baseName}+mask.png");
        }
        // 静态地形: 基础候选(编号变体取0号)
        return FirstExists(
            $"{root}/{baseName}.png",
            $"{root}/{baseName}+0.png",
            $"{root}/{baseName}+mask.png",
            $"{root}/{baseName}+0+mask.png");
    }

    private static string PlusJoin(int mask)
    {
        var parts = new List<string>(4);
        if ((mask & 1) != 0) parts.Add("N");
        if ((mask & 2) != 0) parts.Add("E");
        if ((mask & 4) != 0) parts.Add("S");
        if ((mask & 8) != 0) parts.Add("W");
        return string.Join('+', parts);
    }

    private static string ConcatJoin(int mask)
    {
        string s = "";
        if ((mask & 1) != 0) s += "N";
        if ((mask & 2) != 0) s += "E";
        if ((mask & 4) != 0) s += "S";
        if ((mask & 8) != 0) s += "W";
        return s;
    }

    private static string FirstExists(params string[] paths)
    {
        foreach (var p in paths)
            if (ResourceLoader.Exists(p)) return p;
        return null;
    }
}
