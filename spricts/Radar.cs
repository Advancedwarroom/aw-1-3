// Radar.cs - 雷达（Radar）
// 原作AW：占领后清除迷雾。本作规格：模块化Export配置。
// 雷达模块：是否开启 / x-y范围去雾 / 是否自动化 / 是否强视野 / 是否强侦查
// 照明塔模块：是否开启 / x-y范围选目标点去雾（如照明弹，无弹药）/ 是否自动化
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class Radar : Facility
{
    // ========== 雷达模块 ==========
    [ExportGroup("雷达模块")]
    /// <summary>1. 是否开启雷达模块</summary>
    [Export] public bool radarEnabled = true;
    /// <summary>2. 以雷达为核心 x 范围内去雾（x格）</summary>
    [Export] public int radarRangeX = 5;
    /// <summary>2. 以雷达为核心 y 范围内去雾（y格）</summary>
    [Export] public int radarRangeY = 5;
    /// <summary>3. 是否自动化（非自动需操作菜单按钮激活）</summary>
    [Export] public bool radarAuto = true;
    /// <summary>4. 是否强视野（能看到常规雾战需临格才能看到的森林/礁石）</summary>
    [Export] public bool strongVision = false;
    /// <summary>5. 是否强侦查（能看到隐形模块单位如隐形战机和潜艇）</summary>
    [Export] public bool strongDetection = false;

    // ========== 照明塔模块 ==========
    [ExportGroup("照明塔模块")]
    /// <summary>照明塔是否开启</summary>
    [Export] public bool illuminationEnabled = false;
    /// <summary>照明塔 x 范围（可投放目标点范围）</summary>
    [Export] public int illuminationRangeX = 5;
    /// <summary>照明塔 y 范围（可投放目标点范围）</summary>
    [Export] public int illuminationRangeY = 5;
    /// <summary>照明塔是否自动化</summary>
    [Export] public bool illuminationAuto = false;

    // ========== 运行时状态 ==========
    /// <summary>雷达本回合是否已激活（非自动模式需按钮）</summary>
    public bool radarActivatedThisTurn = false;
    /// <summary>强视野豁免格（本回合内绕过 requiresAdjacentVision）</summary>
    public static HashSet<Grids> StrongVisionExemptGrids = new();

    protected override (string neutral, string red, string blue)? GetTeamAnimNames()
        => ("P0Radar", "radarteam1", "radarteam2");

    

    // CoW LUT 单图层染色（MissileSilo 范式）：body 编码体贴图 + 势力色表直查，P-1 等全势力自动随色
    protected override void ApplyPaletteMaterial()
    {
        if (animSprite == null || !IsInstanceValid(animSprite)) return;
        var lut = FactionPaletteManager.GetCowLutMaterial(facilityTeam);
        if (lut == null) return;
        animSprite.Material = (ShaderMaterial)lut.Duplicate();
    }public override void _Ready()
    {
        base._Ready();
    }

    /// <summary>雷达是否生效（需开启+己方占领+（自动或已激活））</summary>
    public bool IsRadarActive()
    {
        if (!radarEnabled) return false;
        if (string.IsNullOrEmpty(facilityTeam) || facilityTeam == TeamHelper.Player0) return false;
        if (radarAuto) return true;
        return radarActivatedThisTurn;
    }

    /// <summary>获取雷达 x-y 范围内的格子</summary>
    public List<Grids> GetRadarRangeGrids()
    {
        var result = new List<Grids>();
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null || ParentGrid == null) return result;
        var map = gm.gridManager.map;
        int cx = ParentGrid.GridIndex.X, cy = ParentGrid.GridIndex.Y;
        for (int dx = -radarRangeX; dx <= radarRangeX; dx++)
            for (int dy = -radarRangeY; dy <= radarRangeY; dy++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= map.GetLength(0) || y >= map.GetLength(1)) continue;
                var g = map[x, y];
                if (g != null) result.Add(g);
            }
        return result;
    }

    /// <summary>✅ 重写设施视野：雷达提供 x-y 范围视野</summary>
    public override HashSet<Grids> CalculateFacilityVision(GameManager gm)
    {
        var visible = base.CalculateFacilityVision(gm);
        if (!IsRadarActive()) return visible;
        foreach (var g in GetRadarRangeGrids())
            visible.Add(g);
        return visible;
    }

    /// <summary>✅ 激活雷达（非自动模式：菜单按钮调用）</summary>
    public void ActivateRadar()
    {
        radarActivatedThisTurn = true;
        ApplyRadarEffects();
        RefreshFog();
    }

    /// <summary>✅ 应用雷达效果：强视野豁免 + 强侦查显形</summary>
    public void ApplyRadarEffects()
    {
        if (!IsRadarActive()) return;
        var grids = GetRadarRangeGrids();

        // 强视野：豁免 requiresAdjacentVision（森林/礁石）
        if (strongVision)
            foreach (var g in grids)
                if (g != null) StrongVisionExemptGrids.Add(g);

        // 强侦查：显形隐身单位
        if (strongDetection)
            foreach (var g in grids)
                if (g != null)
                    foreach (var u in g.infantries.ToList())
                        if (u != null && u.isStealthed && u.team != facilityTeam)
                            u.RevealStealth();
    }

    /// <summary>每回合重置（自动模式则自动应用）</summary>
    public void OnTurnStartRadar()
    {
        radarActivatedThisTurn = radarAuto; // 自动模式=始终激活
        if (IsRadarActive()) ApplyRadarEffects();
    }

    /// <summary>✅ 照明塔：选择目标点，目标点为新的核心 x-y 范围去雾（如照明弹）</summary>
    public void FireIllumination(Grids targetGrid)
    {
        if (!illuminationEnabled || targetGrid == null) return;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var fog = gm?.GetTree()?.GetFirstNodeInGroup("fog_of_war") as FogOfWarManager;
        if (fog == null) return;
        // 目标点为新的核心，x-y 范围去雾（duration=1回合，如照明弹）
        fog.AddIllumination(targetGrid, 0, Mathf.Max(illuminationRangeX, illuminationRangeY), 1);
    }

    /// <summary>照明塔是否可用（需开启+己方占领）</summary>
    public bool CanFireIllumination()
    {
        return illuminationEnabled && !string.IsNullOrEmpty(facilityTeam) && facilityTeam != TeamHelper.Player0;
    }

    private void RefreshFog()
    {
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var fog = GetTree()?.GetFirstNodeInGroup("fog_of_war") as FogOfWarManager;
        fog?.RefreshFog();
    }
}
