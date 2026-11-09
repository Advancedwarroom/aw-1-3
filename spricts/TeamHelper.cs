using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 单个势力的配置数据
/// </summary>
public class FactionConfig
{
    public string Id;           // 唯一标识，如 "Player3"
    public string DisplayName;  // 显示名称，如 "势力3"
    public Color PrimaryColor;  // 主色
    public Color SecondaryColor;// 副色（可选）
    public bool IsActive = true;// 是否启用
    public int SortOrder;       // 排序
}

/// <summary>
/// 势力调色板管理器：动态生成LUT调色板纹理，管理ShaderMaterial
/// </summary>
public static class FactionPaletteManager
{
    // 每个势力对应一张调色板纹理（256x1）
    private static readonly Dictionary<string, ImageTexture> Palettes = new();
    
    // ShaderMaterial 缓存（每个势力一个材质实例）
    private static readonly Dictionary<string, ShaderMaterial> Materials = new();
    
    // 灰度重着色材质缓存（模式3，中性灰贴图用）
    private static readonly Dictionary<string, ShaderMaterial> GrayMaterials = new();
    
    // 共享的 Shader
    private static Shader _paletteShader;
    
    /// <summary>
    /// 自动为未注册的势力创建默认配置（确保任意 team 都能正确染色）
    /// </summary>
    private static FactionConfig GetOrCreateFaction(string factionId)
    {
        var faction = TeamHelper.GetFaction(factionId);
        if (faction != null)
        {
            // P-1正典紫防御：运行时任何污染（面板旧存档/脏缓存）导致偏色时，当场纠正并清缓存重新生成
            var canonicalPurple = new Color(0.7f, 0.4f, 0.9f);
            if (factionId == TeamHelper.Player && !faction.PrimaryColor.IsEqualApprox(canonicalPurple))
            {
                faction.PrimaryColor = canonicalPurple;
                faction.SecondaryColor = new Color(0.5f, 0.3f, 0.7f);
                Materials.Remove(factionId);
                Palettes.Remove(factionId);
            }
            return faction;
        }
        
        // 预定义 Player3~Player8 的默认颜色
        Color primary = factionId switch
        {
            "Player3" => new Color(0.2f, 0.8f, 0.2f),   // 绿
            "Player4" => new Color(1.0f, 0.9f, 0.1f),   // 黄
            "Player5" => new Color(1.0f, 0.5f, 0.1f),   // 橙
            "Player6" => new Color(1.0f, 0.4f, 0.7f),   // 粉
            "Player7" => new Color(0.2f, 0.9f, 0.9f),   // 青
            "Player8" => new Color(0.6f, 0.4f, 0.2f),   // 棕
            "Player" => new Color(0.7f, 0.4f, 0.9f),   // Player 兜底紫（与注册表一致）
            _ => HashColor(factionId)                        // 其他：按ID确定性着色，杜绝灰
        };
        
        faction = new FactionConfig
        {
            Id = factionId,
            DisplayName = factionId,
            PrimaryColor = primary,
            SecondaryColor = primary.Darkened(0.3f),
            IsActive = true,
            SortOrder = ExtractSortOrder(factionId)
        };
        TeamHelper.RegisterFaction(faction);
        return faction;
    }

    // 按势力ID生成确定性颜色（色相散列），保证任何势力都不会落到灰色
    private static Color HashColor(string id)
    {
        int h = 0;
        foreach (char c in id) h = (h * 31 + c) & 0x7fffffff;
        return Color.FromHsv((h % 360) / 360.0f, 0.65f, 0.9f);
    }
    
    private static int ExtractSortOrder(string factionId)
    {
        if (factionId == TeamHelper.Player) return -1;
        if (factionId == TeamHelper.Player0) return 0;
        if (factionId.StartsWith("Player") && int.TryParse(factionId.Substring(6), out int n))
            return n;
        return 999;
    }
    public static Shader PaletteShader
    {
        get
        {
            if (_paletteShader == null || !GodotObject.IsInstanceValid(_paletteShader))
            {
                _paletteShader = GD.Load<Shader>("res://asscets/shaders/palette_lut.gdshader");
            }
            return _paletteShader;
        }
    }
    // 双图层方案的Base层Shader（简化LUT，只查R通道，无颜色检测）
    private static Shader _baseShader;
    public static Shader BaseShader
    {
        get
        {
            if (_baseShader == null || !GodotObject.IsInstanceValid(_baseShader))
            {
                _baseShader = GD.Load<Shader>("res://asscets/shaders/palette_base.gdshader");
            }
            return _baseShader;
        }
    }
    
    // Base层材质缓存（双图层方案用）
    private static readonly Dictionary<string, ShaderMaterial> BaseMaterials = new();


    /// <summary>
    /// 为指定势力生成调色板纹理
    /// </summary>
    public static ImageTexture GeneratePalette(FactionConfig faction)
    {
        if (Palettes.TryGetValue(faction.Id, out var existing) && GodotObject.IsInstanceValid(existing))
            return existing;

        // 创建 256x1 的 RGBA8 图像
        var image = Image.CreateEmpty(256, 1, false, Image.Format.Rgba8);
        
        Color main = faction.PrimaryColor;
        Color secondary = faction.SecondaryColor;
        
        for (int i = 0; i < 256; i++)
        {
            float t = i / 255f;
            Color pixel;
            
            // 调色板映射规则：
            // 0~63   (0~0.25):   从黑到主色的暗部
            // 64~127 (0.25~0.5): 从主色暗部到主色亮部
            // 128~191(0.5~0.75): 从主色亮部到副色
            // 192~255(0.75~1.0): 从副色到白色（高亮）
            
            if (i < 64)
            {
                float local = i / 64f;
                pixel = new Color(0, 0, 0, 1).Lerp(main.Darkened(0.5f), local);
            }
            else if (i < 128)
            {
                float local = (i - 64) / 64f;
                pixel = main.Darkened(0.5f).Lerp(main, local);
            }
            else if (i < 192)
            {
                float local = (i - 128) / 64f;
                pixel = main.Lerp(secondary, local);
            }
            else
            {
                float local = (i - 192) / 63f;
                pixel = secondary.Lerp(Colors.White, local);
            }
            
            image.SetPixel(i, 0, pixel);
        }
        
        var texture = ImageTexture.CreateFromImage(image);
        Palettes[faction.Id] = texture;
        return texture;
    }

    // 原版贴图自带的势力色（与之相同则直接显示原图，像素级保真）
    public static readonly Color NativeRedArtColor = new Color(1f, 0.2f, 0.2f);
    public static readonly Color NativeBlueArtColor = new Color(0.2f, 0.4f, 1f);

    /// <summary>该势力使用哪套原版贴图作为染色模板：P2 → 蓝色模板，其余 → 红色模板</summary>
    public static bool UsesBlueTemplate(string factionId) => factionId == TeamHelper.Player2;

    /// <summary>
    /// 该势力是否可直接显示原版贴图（P1/P2 主色未改动时）。
    /// P0 不直出：中立设施贴图本身无红色模板像素（实测 0 像素被染），而借用红色贴图的中立单位/兵器会被染成中立灰。
    /// </summary>
    public static bool IsNativeArt(string factionId, FactionConfig faction)
    {
        if (faction == null) return false;
        if (factionId == TeamHelper.Player1) return ColorClose(faction.PrimaryColor, NativeRedArtColor);
        if (factionId == TeamHelper.Player2) return ColorClose(faction.PrimaryColor, NativeBlueArtColor);
        return false;
    }

    private static bool ColorClose(Color a, Color b) =>
        Mathf.Abs(a.R - b.R) < 0.02f && Mathf.Abs(a.G - b.G) < 0.02f && Mathf.Abs(a.B - b.B) < 0.02f;

    /// <summary>
    /// 获取势力的 ShaderMaterial（模板色表模式，参照 Commander Wars color table）
    /// 以红/蓝原版贴图为模板，模板色相带内像素按亮度阶梯映射为「暗部→势力主色→高光」，
    /// 肤色等红蓝贴图共享色不染。P1/P2 未改色时直出原图。
    /// </summary>
    public static ShaderMaterial GetMaterial(string factionId)
    {
        var faction = GetOrCreateFaction(factionId);
        
        if (Materials.TryGetValue(factionId, out var mat) && GodotObject.IsInstanceValid(mat))
            return mat;
        
        mat = new ShaderMaterial();
        mat.Shader = PaletteShader;
        
        bool blue = UsesBlueTemplate(factionId);
        mat.SetShaderParameter("mode", 2);
        mat.SetShaderParameter("enabled", !IsNativeArt(factionId, faction));
        mat.SetShaderParameter("tone", new Color(1, 1, 1, 1));
        mat.SetShaderParameter("template_side", blue ? 1 : 0);
        // 亮度阶梯取自原版贴图统计：红 (128,0,16)/(240,0,8)/(248,184,120)，蓝 (0,24,168)/(0,104,232)/(184,240,248)
        mat.SetShaderParameter("tpl_dark", blue ? 0.13f : 0.15f);
        mat.SetShaderParameter("tpl_mid", blue ? 0.40f : 0.28f);
        mat.SetShaderParameter("tpl_light", blue ? 0.90f : 0.80f);
        
        // 设置势力颜色
        mat.SetShaderParameter("primary_color", faction.PrimaryColor);
        mat.SetShaderParameter("secondary_color", faction.SecondaryColor);
        
        Materials[factionId] = mat;
        return mat;
    }

    // 队伍 → CoW colortable(asscets/BattleAnim/colortables/*.png), 与战斗剧场同套映射
    private static readonly Dictionary<string, string> _cowLutTableByTeam = new()
    {
        { TeamHelper.Player0, "neutral" }, { TeamHelper.Player1, "red" },
        { TeamHelper.Player2, "blue_moon" }, { TeamHelper.Player3, "yellow_comet" },
        { TeamHelper.Player4, "green_earth" }, { TeamHelper.Player5, "purple" },
        { TeamHelper.Player, "purple" },
    };

    /// <summary>
    /// CoW (R,G)编码 mask 贴图的 LUT 解码材质（复用剧场 battle_recolor.gdshader + colortable，随势力换色）。
    /// 适用: Gate/导弹井/黑弧/幽灵炮等编码贴图。返回共享缓存材质, 调用方须 Duplicate 再改参数。
    /// </summary>
    public static ShaderMaterial GetCowLutMaterial(string factionId)
    {
        if (_cowLutTableByTeam.TryGetValue(factionId, out var named))
            return BattleAnimCatalog.GetRecolorMaterial(named);
        // ✅ 注册色适配 CoW 路（开创人 2026-11-09）：未登记具名表的势力按注册主/副色现场生成色表
        var f = TeamHelper.GetFaction(factionId);
        if (f != null)
            return BattleAnimCatalog.GetRecolorMaterialForFaction(factionId, f.PrimaryColor, f.SecondaryColor);
        return BattleAnimCatalog.GetRecolorMaterial("neutral");
    }

    /// <summary>
    /// 获取势力的灰度重着色材质（模式3）：中性灰贴图（导弹井/无势力原版贴图）整体按亮度
    /// 映射为「暗部→势力主色→高光」。P0 中立返回 enabled=false 的材质（保持原灰）。
    /// </summary>
    public static ShaderMaterial GetGrayscaleMaterial(string factionId)
    {
        string cacheKey = factionId + "::gray";
        if (GrayMaterials.TryGetValue(cacheKey, out var mat) && GodotObject.IsInstanceValid(mat))
            return mat;

        var faction = GetOrCreateFaction(factionId);
        mat = new ShaderMaterial();
        mat.Shader = PaletteShader;
        // P0 中立 / 内置原版势力：不禁用也行，但 P0 保持中立灰
        bool neutral = factionId == TeamHelper.Player0;
        mat.SetShaderParameter("mode", 3);
        mat.SetShaderParameter("enabled", !neutral);
        mat.SetShaderParameter("tone", new Color(1, 1, 1, 1));
        mat.SetShaderParameter("template_side", 0);
        // 灰度阶梯：暗部0.10 / 主色0.35 / 高光0.85（比模板色表更平缓，适合整体灰贴图）
        mat.SetShaderParameter("tpl_dark", 0.10f);
        mat.SetShaderParameter("tpl_mid", 0.35f);
        mat.SetShaderParameter("tpl_light", 0.85f);
        mat.SetShaderParameter("primary_color", faction.PrimaryColor);
        mat.SetShaderParameter("secondary_color", faction.SecondaryColor);

        GrayMaterials[cacheKey] = mat;
        return mat;
    }

    /// <summary>
    /// 势力配置修改后即时刷新场景中所有单位/设施/兵器的颜色（无需重启场景）
    /// </summary>
    public static void RefreshAllVisuals(Node root)
    {
        if (root == null || !GodotObject.IsInstanceValid(root)) return;
        ClearCache();
        RefreshRecursive(root);
    }

    private static void RefreshRecursive(Node node)
    {
        switch (node)
        {
            case Infantry inf: inf.UpdateTeamVisual(); break;
            case Facility fac: fac.UpdateCityVisual(); break;
            case Weapon w: w.RefreshTeamVisual(); break;
        }
        foreach (var child in node.GetChildren()) RefreshRecursive(child);
    }
    
    /// <summary>
    /// 获取势力的变暗材质（用于待机/已行动状态）
    /// </summary>
    public static ShaderMaterial GetDimMaterial(string factionId)
    {
        var baseMat = GetMaterial(factionId);
        if (baseMat == null) return null;
        
        var mat = (ShaderMaterial)baseMat.Duplicate();
        mat.SetShaderParameter("tone", new Color(0.6f, 0.6f, 0.6f, 1.0f));
        return mat;
    }
    
    /// <summary>
    /// 获取双图层方案的Base层材质（简化LUT，用于BaseSprite染色）
    /// </summary>
    public static ShaderMaterial GetBaseLayerMaterial(string factionId)
    {
        var faction = GetOrCreateFaction(factionId);
        
        if (BaseMaterials.TryGetValue(factionId, out var mat) && GodotObject.IsInstanceValid(mat))
            return mat;
        
        // 确保调色板已生成
        var palette = GeneratePalette(faction);
        
        mat = new ShaderMaterial();
        mat.Shader = BaseShader;
        mat.SetShaderParameter("palette", palette);
        mat.SetShaderParameter("tone", new Color(1, 1, 1, 1));
        
        BaseMaterials[factionId] = mat;
        return mat;
    }


    /// <summary>
    /// 清除缓存（势力配置改变时调用）
    /// </summary>
    public static void ClearCache()
    {
        foreach (var tex in Palettes.Values)
            if (GodotObject.IsInstanceValid(tex)) tex.Dispose();
        Palettes.Clear();
        
        foreach (var mat in Materials.Values)
            if (GodotObject.IsInstanceValid(mat)) mat.Dispose();
        Materials.Clear();
        
        foreach (var mat in BaseMaterials.Values)
            if (GodotObject.IsInstanceValid(mat)) mat.Dispose();
        BaseMaterials.Clear();
        
        if (_paletteShader != null && GodotObject.IsInstanceValid(_paletteShader))
            _paletteShader.Dispose();
        _paletteShader = null;
    }
}

/// <summary>
/// 势力帮助类：支持最多100个动态配置的势力
/// </summary>
public static class TeamHelper
{
    // ========== 内置默认势力（向后兼容）==========
    public const string Player = "Player";      // P-1
    public const string Player0 = "Player0";    // P0 中立
    public const string Player1 = "Player1";    // P1
    public const string Player2 = "Player2";    // P2
    // ===== P3~P8（四方向/多势力扩展：SuperFactory 分势力生产等引用）=====
    public const string Player3 = "Player3";    // P3
    public const string Player4 = "Player4";    // P4
    public const string Player5 = "Player5";    // P5
    public const string Player6 = "Player6";    // P6
    public const string Player7 = "Player7";    // P7
    public const string Player8 = "Player8";    // P8
    
    // 最大势力数
    public const int MaxFactions = 100;
    
    // ========== 运行时势力配置（可由TerrainEditor修改）==========
    private static readonly Dictionary<string, FactionConfig> Factions = new();
    private static readonly List<string> ActiveFactionIds = new();
    private static bool _initialized = false;
    
    /// <summary>
    /// 初始化默认势力（GameManager启动时调用）
    /// </summary>
    public static void InitializeDefaults()
    {
        if (_initialized) return;
        _initialized = true;
        
        Factions.Clear();
        ActiveFactionIds.Clear();
        
        // 注册默认势力
        RegisterFaction(new FactionConfig
        {
            Id = Player,
            DisplayName = "Player(-1)",
            PrimaryColor = new Color(0.7f, 0.4f, 0.9f),
            SecondaryColor = new Color(0.5f, 0.3f, 0.7f),
            IsActive = true,
            SortOrder = -1
        });
        
        RegisterFaction(new FactionConfig
        {
            Id = Player0,
            DisplayName = "Player0(中立)",
            PrimaryColor = new Color(0.7f, 0.7f, 0.7f),
            SecondaryColor = new Color(0.5f, 0.5f, 0.5f),
            IsActive = true,
            SortOrder = 0
        });
        
        RegisterFaction(new FactionConfig
        {
            Id = Player1,
            DisplayName = "Player1",
            PrimaryColor = new Color(1f, 0.2f, 0.2f),
            SecondaryColor = new Color(0.8f, 0.1f, 0.1f),
            IsActive = true,
            SortOrder = 1
        });
        
        RegisterFaction(new FactionConfig
        {
            Id = Player2,
            DisplayName = "Player2",
            PrimaryColor = new Color(0.2f, 0.4f, 1f),
            SecondaryColor = new Color(0.1f, 0.2f, 0.8f),
            IsActive = true,
            SortOrder = 2
        });
        
        RebuildActiveList();
    }
    
    /// <summary>
    /// 注册新势力
    /// </summary>
    public static bool RegisterFaction(FactionConfig config)
    {
        if (string.IsNullOrEmpty(config.Id)) return false;
        if (Factions.Count >= MaxFactions) return false;
        
        Factions[config.Id] = config;
        RebuildActiveList();
        
        // 势力变更，清除调色板缓存
        FactionPaletteManager.ClearCache();
        return true;
    }
    
    /// <summary>
    /// 移除势力
    /// </summary>
    public static bool RemoveFaction(string id)
    {
        if (id == Player || id == Player0) return false; // 内置势力不可删
        
        bool removed = Factions.Remove(id);
        if (removed)
        {
            RebuildActiveList();
            FactionPaletteManager.ClearCache();
        }
        return removed;
    }
    
    /// <summary>
    /// 获取势力配置
    /// </summary>
    public static FactionConfig GetFaction(string id)
    {
        if (Factions.TryGetValue(id, out var config)) return config;
        return null;
    }
    
    /// <summary>
    /// 获取所有势力（按SortOrder排序）
    /// </summary>
    public static List<FactionConfig> GetAllFactions()
    {
        return Factions.Values.OrderBy(f => f.SortOrder).ToList();
    }
    
    /// <summary>
    /// 获取所有启用的势力ID（按SortOrder排序）
    /// </summary>
    public static List<string> GetActiveFactionIds()
    {
        return ActiveFactionIds.ToList();
    }
    
    /// <summary>
    /// 获取所有启用的势力配置
    /// </summary>
    public static List<FactionConfig> GetActiveFactions()
    {
        return ActiveFactionIds
            .Select(id => Factions.TryGetValue(id, out var f) ? f : null)
            .Where(f => f != null)
            .ToList();
    }
    
    /// <summary>
    /// 设置势力启用状态
    /// </summary>
    public static void SetFactionActive(string id, bool active)
    {
        if (Factions.TryGetValue(id, out var config))
        {
            config.IsActive = active;
            RebuildActiveList();
        }
    }
    
    private static void RebuildActiveList()
    {
        ActiveFactionIds.Clear();
        var sorted = Factions.Values
            .Where(f => f.IsActive)
            .OrderBy(f => f.SortOrder)
            .Select(f => f.Id);
        ActiveFactionIds.AddRange(sorted);
    }
    
    /// <summary>
    /// 势力数量（含未启用的）
    /// </summary>
    public static int FactionCount => Factions.Count;
    
    /// <summary>
    /// 启用的势力数量
    /// </summary>
    public static int ActiveFactionCount => ActiveFactionIds.Count;
    
    // ========== 向后兼容的静态数组（基于当前启用的势力）==========
    public static string[] AllTeams => GetActiveFactionIds().ToArray();
    public static string[] ActiveTeams => GetActiveFactions()
        .Where(f => f.Id != Player && f.Id != Player0)
        .Select(f => f.Id).ToArray();
    public static string[] AllCountableTeams => AllTeams;
    
    // ========== 显示名称 ==========
    public static string GetTeamDisplayName(string team)
    {
        var f = GetFaction(team);
        if (f != null) return f.DisplayName;
        return team;
    }
    
    public static string GetTeamShortName(string team)
    {
        return team switch
        {
            Player => "P-1",
            Player0 => "P0",
            Player1 => "P1",
            Player2 => "P2",
            _ => team
        };
    }
    
    // ========== 颜色（动态查询）==========
    /// <summary>
    /// 按势力选择原版贴图套：P0 → 中立，P2 → 蓝色，其余（P1、P-1、P3…P100）→ 红色模板（由 Shader 染成势力主色）
    /// </summary>
    public static string PickTeamArt(string team, string neutral, string red, string blue)
    {
        if (team == Player0) return neutral;
        if (team == Player2) return blue;
        return red;
    }

    /// <summary>按阵营颜色取动画名（8 色槽位，色相就近匹配；槽位 null 则回退 红→中立）。P-1 恒紫。</summary>
    public static string PickArtByFactionColor(string team, string neutral, string red, string blue,
        string purple = null, string green = null, string yellow = null, string black = null, string pink = null)
    {
        if (team == Player0) return neutral;
        if (team == Player) return purple ?? red;   // P-1 正典紫，不走色匹配
        if (team == Player2) return blue ?? red;
        var f = GetFaction(team);
        if (f == null) return red ?? neutral;
        Color c = f.PrimaryColor;
        if (c.V < 0.25f) return black ?? red;       // 近黑（黑洞军一类）
        float h = c.H;
        (string slot, float hue)[] anchors = {
            (red, 0.03f), (yellow, 0.14f), (green, 0.33f), (blue, 0.62f), (purple, 0.75f), (pink, 0.92f)
        };
        string best = null; float bestD = float.MaxValue;
        foreach (var (slot, hue) in anchors)
        {
            if (slot == null) continue;
            float d = Mathf.Abs(h - hue); if (d > 0.5f) d = 1f - d;
            if (d < bestD) { bestD = d; best = slot; }
        }
        return best ?? red ?? neutral;
    }

    /// <summary>原版贴图套编号：P2 → "2"，其余 → "1"</summary>
    public static string ArtSuffix(string team) => team == Player2 ? "2" : "1";

    public static Color GetTeamColor(string team)
    {
        var f = GetFaction(team);
        if (f != null) return f.PrimaryColor;
        return Colors.White;
    }
    
    public static Color GetTeamSecondaryColor(string team)
    {
        var f = GetFaction(team);
        if (f != null) return f.SecondaryColor;
        return Colors.Gray;
    }
    
    // ========== 操作权限（基于势力索引）==========
    public static bool CanOperateTeam(int turnPhase, string team)
    {
        var active = GetActiveFactionIds();
        int idx = active.IndexOf(team);
        
        // P-1: 所有phase可操作
        if (team == Player) return true;
        // P0: 不可操作
        if (team == Player0) return false;
        // 其他: 对应phase可操作 (phase 1=第1个非内置势力, phase 2=第2个...)
        if (idx >= 0)
        {
            // 跳过P-1和P0，计算在战斗势力中的索引
            var combatFactions = GetActiveFactions()
                .Where(f => f.Id != Player && f.Id != Player0)
                .Select(f => f.Id).ToList();
            int combatIdx = combatFactions.IndexOf(team);
            return combatIdx >= 0 && turnPhase == combatIdx + 1;
        }
        return false;
    }
    
    public static bool IsCurrentPhasePlayer(int turnPhase, string team)
    {
        var combatFactions = GetActiveFactions()
            .Where(f => f.Id != Player && f.Id != Player0)
            .Select(f => f.Id).ToList();
        int idx = combatFactions.IndexOf(team);
        return idx >= 0 && turnPhase == idx + 1;
    }
    
    public static string GetPhaseTeamName(int turnPhase)
    {
        var combatFactions = GetActiveFactions()
            .Where(f => f.Id != Player && f.Id != Player0)
            .Select(f => f.Id).ToList();
        if (turnPhase >= 1 && turnPhase <= combatFactions.Count)
            return combatFactions[turnPhase - 1];
        return "";
    }
    
    // ========== 攻击/反击逻辑 ==========
    public static bool CanAttackTarget(string attackerTeam, string defenderTeam)
    {
        if (attackerTeam == Player || attackerTeam == Player0)
            return true;
        if (attackerTeam == defenderTeam) return false;
        return true;
    }
    
    public static bool IsEnemyForAttacker(string attackerTeam, string defenderTeam)
    {
        if (attackerTeam == Player || attackerTeam == Player0)
            return true;
        // ✅ 盟友机制：同盟势力不算敌人（不能互攻、AI不视为目标）
        if (AreAllied(attackerTeam, defenderTeam)) return false;
        return attackerTeam != defenderTeam;
    }

    // ========== ✅ 盟友机制（俩势力一伙）：运行时API，全局生效 ==========
    private static readonly List<(string A, string B)> alliances = new();

    /// <summary>设置/解除同盟（双向）</summary>
    public static void SetAlliance(string teamA, string teamB, bool allied)
    {
        if (string.IsNullOrEmpty(teamA) || string.IsNullOrEmpty(teamB) || teamA == teamB) return;
        var key = teamA.CompareTo(teamB) <= 0 ? (teamA, teamB) : (teamB, teamA);
        if (allied) { if (!alliances.Contains(key)) alliances.Add(key); }
        else alliances.Remove(key);
    }

    /// <summary>是否同盟（含同势力）</summary>
    public static bool AreAllied(string teamA, string teamB)
    {
        if (teamA == teamB) return true;
        if (string.IsNullOrEmpty(teamA) || string.IsNullOrEmpty(teamB)) return false;
        var key = teamA.CompareTo(teamB) <= 0 ? (teamA, teamB) : (teamB, teamA);
        return alliances.Contains(key);
    }

    /// <summary>获取某势力的所有盟友</summary>
    public static List<string> GetAlliedTeams(string team)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(team)) return result;
        foreach (var (a, b) in alliances)
        {
            if (a == team && !result.Contains(b)) result.Add(b);
            if (b == team && !result.Contains(a)) result.Add(a);
        }
        return result;
    }

    /// <summary>清空全部同盟（重置游戏时调用）</summary>
    public static void ClearAllAlliances() => alliances.Clear();

    /// <summary>获取全部同盟对（面板列表/存档序列化用）</summary>
    public static System.Collections.Generic.List<(string A, string B)> GetAlliancePairs()
        => new System.Collections.Generic.List<(string A, string B)>(alliances);
    
    public static bool CanCounterAttack(string defenderTeam)
    {
        return defenderTeam != Player0;
    }
    
    // ========== 设施补给 ==========
    public static bool CanFacilitySupply(string facilityTeam, string unitTeam)
    {
        if (facilityTeam == Player0) return false;
        if (facilityTeam == Player) return true;
        return facilityTeam == unitTeam;
    }
    
    // ========== 设施视野 ==========
    public static bool DoesFacilityProvideVision(string facilityTeam)
    {
        if (facilityTeam == Player0) return false;
        return true;
    }
    
    // ========== 兵器AI ==========
    public static bool ShouldWeaponUseAI(string weaponTeam)
    {
        return weaponTeam == Player0;
    }
    
    public static bool ShouldPlayerWeaponUseAI(int turnPhase, string weaponTeam)
    {
        return weaponTeam == Player && turnPhase == GetActiveFactions().Count + 1;
    }
    
    public static bool CanPlayerWeaponBeOperated(int turnPhase, string weaponTeam)
    {
        if (weaponTeam != Player) return false;
        var combatCount = GetActiveFactions().Count(f => f.Id != Player && f.Id != Player0);
        return turnPhase >= 1 && turnPhase <= combatCount;
    }
    
    public static bool HasNumberLabel(string team) => false;
    
    private const string FACTION_SAVE_PATH = "user://FactionConfigs.json";
    
    // ========== 配置持久化 ==========
    /// <summary>
    /// 保存势力配置到JSON文件
    /// </summary>
    public static void SaveFactionsToExtraSave()
    {
        try
        {
            var dict = new Godot.Collections.Dictionary();
            var arr = new Godot.Collections.Array();
            
            foreach (var kvp in Factions)
            {
                var f = kvp.Value;
                var fDict = new Godot.Collections.Dictionary
                {
                    ["id"] = f.Id,
                    ["name"] = f.DisplayName,
                    ["primary_r"] = f.PrimaryColor.R,
                    ["primary_g"] = f.PrimaryColor.G,
                    ["primary_b"] = f.PrimaryColor.B,
                    ["secondary_r"] = f.SecondaryColor.R,
                    ["secondary_g"] = f.SecondaryColor.G,
                    ["secondary_b"] = f.SecondaryColor.B,
                    ["active"] = f.IsActive,
                    ["sort"] = f.SortOrder
                };
                arr.Add(fDict);
            }
            dict["factions"] = arr;
            dict["version"] = 1;
            
            string json = Json.Stringify(dict);
            using var file = Godot.FileAccess.Open(FACTION_SAVE_PATH, Godot.FileAccess.ModeFlags.Write);
            file.StoreString(json);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[TeamHelper] 保存势力配置失败: {e.Message}");
        }
    }
    
    /// <summary>
    /// 从JSON文件加载势力配置
    /// </summary>
    public static void LoadFactionsFromExtraSave()
    {
        if (!Godot.FileAccess.FileExists(FACTION_SAVE_PATH)) return;
        
        try
        {
            using var file = Godot.FileAccess.Open(FACTION_SAVE_PATH, Godot.FileAccess.ModeFlags.Read);
            string json = file.GetAsText();
            var parsed = Json.ParseString(json).AsGodotDictionary();
            if (parsed == null) return;
            
            var arr = parsed.GetValueOrDefault("factions", new Variant()).AsGodotArray();
            if (arr == null || arr.Count == 0) return;
            
            Factions.Clear();
            ActiveFactionIds.Clear();
            
            foreach (var item in arr)
            {
                var fDict = item.AsGodotDictionary();
                if (fDict == null) continue;
                
                var config = new FactionConfig
                {
                    Id = fDict.GetValueOrDefault("id").AsString(),
                    DisplayName = fDict.GetValueOrDefault("name").AsString(),
                    PrimaryColor = new Color(
                        fDict.GetValueOrDefault("primary_r").AsSingle(),
                        fDict.GetValueOrDefault("primary_g").AsSingle(),
                        fDict.GetValueOrDefault("primary_b").AsSingle()
                    ),
                    SecondaryColor = new Color(
                        fDict.GetValueOrDefault("secondary_r").AsSingle(),
                        fDict.GetValueOrDefault("secondary_g").AsSingle(),
                        fDict.GetValueOrDefault("secondary_b").AsSingle()
                    ),
                    IsActive = fDict.GetValueOrDefault("active", true).AsBool(),
                    SortOrder = fDict.GetValueOrDefault("sort", 0).AsInt32()
                };
                
                if (!string.IsNullOrEmpty(config.Id))
                    Factions[config.Id] = config;
            }
            
            RebuildActiveList();
            FactionPaletteManager.ClearCache();
            
            // ✅ 确保默认势力始终存在（防止存档覆盖导致缺失）
            EnsureDefaultFactions();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[TeamHelper] 加载势力配置失败: {e.Message}");
        }
    }
    
    /// <summary>
    /// 确保默认势力（Player/P0/P1/P2）始终存在于Factions字典中
    /// </summary>
    private static void EnsureDefaultFactions()
    {
        if (!Factions.ContainsKey(Player))
        {
            RegisterFaction(new FactionConfig
            {
                Id = Player,
                DisplayName = "Player(-1)",
                PrimaryColor = new Color(0.7f, 0.4f, 0.9f),
                SecondaryColor = new Color(0.5f, 0.3f, 0.7f),
                IsActive = true,
                SortOrder = -1
            });
        }
        if (!Factions.ContainsKey(Player0))
        {
            RegisterFaction(new FactionConfig
            {
                Id = Player0,
                DisplayName = "Player0(中立)",
                PrimaryColor = new Color(0.7f, 0.7f, 0.7f),
                SecondaryColor = new Color(0.5f, 0.5f, 0.5f),
                IsActive = true,
                SortOrder = 0
            });
        }
        if (!Factions.ContainsKey(Player1))
        {
            RegisterFaction(new FactionConfig
            {
                Id = Player1,
                DisplayName = "Player1",
                PrimaryColor = new Color(1f, 0.2f, 0.2f),
                SecondaryColor = new Color(0.8f, 0.1f, 0.1f),
                IsActive = true,
                SortOrder = 1
            });
        }
        if (!Factions.ContainsKey(Player2))
        {
            RegisterFaction(new FactionConfig
            {
                Id = Player2,
                DisplayName = "Player2",
                PrimaryColor = new Color(0.2f, 0.4f, 1f),
                SecondaryColor = new Color(0.1f, 0.2f, 0.8f),
                IsActive = true,
                SortOrder = 2
            });
        }
    }
}
