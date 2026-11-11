// ActionMenu.cs - 完整修复版，移除所有unloadButton直接引用
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;

// ActionMenu · Attack（由 ActionMenu.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class ActionMenu
{

    // ✅ 攻击按钮统一判定：可攻击类型 + 未攻击 + 移动后允许 + 有弹药 + 射程内有可伤害目标
    private bool CanShowAttackButton(Infantry unit)
    {
        if (unit == null) return false;
        if (unit.attackType == AttackType.NoAttack) return false;
        if (unit.isAttacked) return false;
        if (!unit.CanAttackAfterMove()) return false;
        if (!unit.CanUsePrimaryWeapon() && !unit.CanUseSecondaryWeapon()) return false;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        return gm != null && HasAttackableEnemyInRange(unit, gm);
    }

    // ✅ 射程内是否存在“实际能打”的目标——与 GridManager.ShowAttackRange / ShowMinMaxAttackRange 的高亮判定逐条一致：
    //    射程形状一致（环形=min~max；否则=攻击邻格）/ 迷雾不可见格不算 / 多格兵器只认弱点格 /
    //    隐身看不见、运输中、阵亡、同队都不算 / 伤害矩阵打不动不算
    private bool HasAttackableEnemyInGrid(Infantry unit, Grids g, FogOfWarManager fog, List<Grids> all, bool ringMode)
    {
        if (g == null) return false;
        if (fog != null && fog.isFogOfWarEnabled && !fog.IsGridVisible(g)) return false;
        bool weaponOk = g.weapon != null && IsInstanceValid(g.weapon) && (g.weapon.team != unit.team || unit.canAttackFriendlies || (unit.canAttackAllies && TeamHelper.AreAllied(unit.team, g.weapon.team)))
                        && (!g.weapon.isMultiTile || g.weapon.IsWeakPointGrid(g))
                        && (ringMode || !g.weapon.hasActed);
        if (weaponOk) return true;
        if (ringMode && g.weapon != null && g.weapon.isMultiTile && !g.weapon.IsWeakPointGrid(g)) return false;
        if (g.HasAttackableFacility(unit)) return true;
        return g.HasAttackableEnemyInfantry(unit, all);
    }

    private bool HasAttackableEnemyInRange(Infantry unit, GameManager gm)
    {
        if (gm?.gridManager == null || unit.grid == null) return false;
        var fog = gm.fogOfWarManager;
        var all = gm.gridManager.grids;
        bool ring = unit.useMinMaxAttackRange;
        IEnumerable<Grids> candidates = ring
            ? all.Where(g => g != null && g != unit.grid && gm.gridManager.IsRangeAllowed(unit.grid, g, unit.attackCanCrossScreens) && Dist(g, unit.grid) >= unit.minAttackRange && Dist(g, unit.grid) <= unit.maxAttackRange)
            : gm.gridManager.GetAttackNeighbours(unit.grid, unit.attackCanCrossScreens);
        if (candidates.Any(g => HasAttackableEnemyInGrid(unit, g, fog, all, ring))) return true;
        // 重叠型单位可打同格（与 ShowAttackRange 同格分支一致）
        return !ring && unit.overlapType == UnitOverlapType.Overlapping
               && (fog == null || !fog.isFogOfWarEnabled || fog.IsGridVisible(unit.grid))
               && (unit.grid.HasAttackableEnemyInfantry(unit, all) || unit.grid.HasAttackableFacility(unit));
    }

    private static int Dist(Grids a, Grids b) => Mathf.Abs(a.GridIndex.X - b.GridIndex.X) + Mathf.Abs(a.GridIndex.Y - b.GridIndex.Y);
}
