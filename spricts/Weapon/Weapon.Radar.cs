// Weapon.Radar.cs - 兵器雷达模块（开创人令 2026-11-15：给兵器加上完整雷达 Export 模块）
// 字段名/语义与 Radar.cs（设施版）一一对应；默认全关 = 存量兵器零行为变化。
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class Weapon
{
    // ========== 雷达模块 ==========
    [ExportGroup("雷达模块")]
    /// <summary>1. 是否开启雷达模块</summary>
    [Export] public bool radarEnabled = false;
    /// <summary>2. 以兵器为核心 x 范围内去雾（x格）</summary>
    [Export] public int radarRangeX = 5;
    /// <summary>2. 以兵器为核心 y 范围内去雾（y格）</summary>
    [Export] public int radarRangeY = 5;
    /// <summary>3. 是否自动化（非自动需 ActivateWeaponRadar 激活，按钮接线同设施版现状：无）</summary>
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
    [Export] public bool radarIlluminationCanCrossScreens = false; // 雷达照明效果范围是否允许跨屏
    /// <summary>照明塔是否自动化</summary>
    [Export] public bool illuminationAuto = false;

    // ========== 运行时状态 ==========
    /// <summary>雷达本回合是否已激活（非自动模式需按钮）</summary>
    public bool radarActivatedThisTurn = false;

    /// <summary>雷达是否生效（需开启+有归属+（自动或已激活））；归属=兵器自身 team</summary>
    public bool IsWeaponRadarActive()
    {
        if (!radarEnabled) return false;
        if (string.IsNullOrEmpty(team)) return false;
        if (radarAuto) return true;
        return radarActivatedThisTurn;
    }

    /// <summary>获取雷达 x-y 范围内的格子（与设施版 GetRadarRangeGrids 同构）</summary>
    public List<Grids> GetWeaponRadarRangeGrids()
    {
        var result = new List<Grids>();
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null || grid == null) return result;
        var map = gm.gridManager.map;
        int cx = grid.GridIndex.X, cy = grid.GridIndex.Y;
        for (int dx = -radarRangeX; dx <= radarRangeX; dx++)
            for (int dy = -radarRangeY; dy <= radarRangeY; dy++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= map.GetLength(0) || y >= map.GetLength(1)) continue;
                var g = map[x, y];
                if (g != null && gm.gridManager.IsRangeAllowed(grid, g, visionCanCrossScreens)) result.Add(g);
            }
        return result;
    }

    /// <summary>✅ 激活雷达（非自动模式：菜单按钮调用）</summary>
    public void ActivateWeaponRadar()
    {
        radarActivatedThisTurn = true;
        ApplyWeaponRadarEffects();
        var fog = GetTree()?.GetFirstNodeInGroup("fog_of_war") as FogOfWarManager;
        fog?.RefreshFog();
    }

    /// <summary>✅ 应用雷达效果：强视野豁免 + 强侦查显形（复用 Radar.StrongVisionExemptGrids 全局豁免集）</summary>
    public void ApplyWeaponRadarEffects()
    {
        if (!IsWeaponRadarActive()) return;
        var grids = GetWeaponRadarRangeGrids();

        // 强视野：豁免 requiresAdjacentVision（森林/礁石）
        if (strongVision)
            foreach (var g in grids)
                if (g != null) Radar.StrongVisionExemptGrids.Add(g);

        // 强侦查：显形隐身单位（他队）
        if (strongDetection)
            foreach (var g in grids)
                if (g != null)
                    foreach (var u in g.infantries.ToList())
                        if (u != null && u.isStealthed && u.team != team)
                            u.RevealStealth();
    }

    /// <summary>每回合重置（自动模式则自动应用）；由 GameManager 回合开始统一调用</summary>
    public void OnTurnStartWeaponRadar()
    {
        radarActivatedThisTurn = radarAuto; // 自动模式=始终激活
        if (IsWeaponRadarActive()) ApplyWeaponRadarEffects();
    }

    /// <summary>✅ 照明塔：选择目标点，目标点为新的核心 x-y 范围去雾（如照明弹，持续1回合）</summary>
    public void FireWeaponIllumination(Grids targetGrid)
    {
        if (!illuminationEnabled || targetGrid == null) return;
        var fog = GetTree()?.GetFirstNodeInGroup("fog_of_war") as FogOfWarManager;
        if (fog == null) return;
        // 目标点为新的核心，x-y 范围去雾（duration=1回合，如照明弹）
        fog.AddIllumination(targetGrid, 0, Mathf.Max(illuminationRangeX, illuminationRangeY), 1, radarIlluminationCanCrossScreens);
    }

    /// <summary>照明塔是否可用（需开启+有归属）</summary>
    public bool CanFireWeaponIllumination()
    {
        return illuminationEnabled && !string.IsNullOrEmpty(team);
    }
}
