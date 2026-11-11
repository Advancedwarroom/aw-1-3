// GridManager.cs - 修改 HandleGridClick 和 SetupMoveRangeCallbacks 方法
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

// GridManager · SpecialRanges（由 GridManager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class GridManager
{

    // ========== ✅ 显示修复范围（粉色）==========
    public void ShowRepairRange(Infantry unit)
    {
        if (unit == null || unit.grid == null) return;
        ForceClearAllRangeVisuals();
        
        repairRange.Clear();
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return;

        repairRange = unit.CalculateRepairRange();   // ✅ 与执行同一范围（含 repairTargetMode=3 仅自身格）

        Grids.IsForceActionMode = true;
        foreach (var grid in repairRange)
        {
            grid.attackRangeIcon?.Show();
            grid.attackRangeIcon.Modulate = new Color(1.0f, 0.4f, 0.7f, 0.8f); // 粉色
            grid.IsInAttackRangeMode = true;
        }
    }
    
    // ========== ✅ 爆炸范围显示 ==========
    public void ShowExplosionRange(Infantry unit, int minR, int maxR)
    {
        HideExplosionRange();
        if (unit?.grid == null) return;

        var allRangeGrids = FindRange(unit.grid, maxR, false, unit.explosionCanCrossScreens);
        List<Grids> innerGrids = minR > 0 ? FindRange(unit.grid, minR - 1, false, unit.explosionCanCrossScreens) : new List<Grids>();

        foreach (var grid in allRangeGrids)
        {
            if (innerGrids.Contains(grid)) continue; // 排除内圈（形成环形）
            explosionRange.Add(grid);
            if (grid.attackRangeIcon != null)
            {
                grid.attackRangeIcon.Modulate = new Color(1f, 0.3f, 0f, 0.9f); // 橙红色
                grid.attackRangeIcon.Show();
            }
        }
    }

    public void HideExplosionRange()
    {
        foreach (var grid in explosionRange)
        {
            if (grid?.attackRangeIcon != null)
            {
                grid.attackRangeIcon.Modulate = Colors.White; // 恢复默认颜色
                grid.attackRangeIcon.Hide();
            }
        }
        explosionRange.Clear();
    }

    // ========== ✅ 照明弹射程范围显示（绿色）==========
    public void ShowFlareRange(Infantry unit, int minR, int maxR)
    {
        HideFlareRange();
        if (unit?.grid == null) return;

        var allRangeGrids = FindRange(unit.grid, maxR, false, unit.flareLaunchCanCrossScreens);
        List<Grids> innerGrids = minR > 0 ? FindRange(unit.grid, minR - 1, false, unit.flareLaunchCanCrossScreens) : new List<Grids>();

        foreach (var grid in allRangeGrids)
        {
            if (innerGrids.Contains(grid)) continue;
            flareRange.Add(grid);
            if (grid.attackRangeIcon != null)
            {
                grid.attackRangeIcon.Modulate = new Color(0f, 1f, 0f, 0.7f); // 绿色
                grid.attackRangeIcon.Show();
            }
        }
    }

    public void HideFlareRange()
    {
        foreach (var grid in flareRange)
        {
            if (grid?.attackRangeIcon != null)
            {
                grid.attackRangeIcon.Modulate = Colors.White;
                grid.attackRangeIcon.Hide();
            }
        }
        flareRange.Clear();
    }

    // ========== ✅ 照明覆盖范围显示（黄色，以目标为中心）==========
    public void ShowIlluminationRange(Grids center, int minR, int maxR)
    {
        HideIlluminationRange();
        if (center == null) return;

        var allRangeGrids = FindRange(center, maxR, false);
        List<Grids> innerGrids = minR > 0 ? FindRange(center, minR - 1, false) : new List<Grids>();

        foreach (var grid in allRangeGrids)
        {
            if (innerGrids.Contains(grid)) continue;
            illuminationRange.Add(grid);
            if (grid.attackRangeIcon != null)
            {
                grid.attackRangeIcon.Modulate = new Color(1f, 1f, 0f, 0.7f); // 黄色
                grid.attackRangeIcon.Show();
            }
        }
    }

    public void HideIlluminationRange()
    {
        foreach (var grid in illuminationRange)
        {
            if (grid?.attackRangeIcon != null)
            {
                grid.attackRangeIcon.Modulate = Colors.White;
                grid.attackRangeIcon.Hide();
            }
        }
        illuminationRange.Clear();
    }
}
