// BlackCannon.cs - 黑炮：三角形范围，固定百分比伤害，支持自定义目标选择和改良伤害模式
// ✅ 新增：完整弹药系统 + 多回合冷却系统（还原原版高级战争原型炮/完全体差异）
using Godot;
using System.Collections.Generic;
using System.Linq;

// BlackCannon · AI（由 BlackCannon.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class BlackCannon
{

    /// <summary>✅ AI 入口：等同玩家点击射程内格子（走同一套弹药/冷却/目标筛选）。返回是否真的开火</summary>
    public bool AIFireAtGrid(Grids targetGrid)
    {
        if (targetGrid == null || !CanAttack()) return false;
        int before = remainingAttacks;
        OnRangeGridClicked(targetGrid);
        return remainingAttacks < before || hasActed;
    }

    /// <summary>✅ AI 用：该格子在玩家点击时会被选中的目标（与 OnRangeGridClicked 一致取第一个）</summary>
    public Node2D AIPeekTargetInGrid(Grids targetGrid)
    {
        if (targetGrid == null) return null;
        var t = GetValidTargetsInGrid(targetGrid);
        return t.Count > 0 ? t[0] : null;
    }

    // ✅ P0/P-1 兵器 AI：自动攻击所有目标（单位优先，兵器次之）
    public override void ExecuteAI()
    {
        if (!TeamHelper.ShouldWeaponUseAI(team) && !TeamHelper.ShouldPlayerWeaponUseAI(3, team))
            return;
        if (hasActed) return;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return;

        // 构建所有候选目标（除了自己）
        var candidates = new List<Godot.Node2D>();

        // 1. 优先收集所有单位
        if (gm.unitManager?.AllUnits != null)
        {
            foreach (var u in gm.unitManager.AllUnits)
            {
                if (u != null && IsInstanceValid(u) && TeamHelper.IsEnemyForAttacker(team, u.team) && u.health > 0)
                    candidates.Add(u);
            }
        }

        // 2. 收集兵器（CanAttackWeapon 时）
        if (CanAttackWeapon && gm.weaponManager?.AllWeapons != null)
        {
            foreach (var w in gm.weaponManager.AllWeapons)
            {
                if (w != null && IsInstanceValid(w) && w != this && TeamHelper.IsEnemyForAttacker(team, w.team) && w.health > 0)
                    candidates.Add(w);
            }
        }

        if (candidates.Count == 0) return;

        // 找到射程内最近的目标
        Godot.Node2D bestTarget = null;
        float bestDist = float.MaxValue;
        foreach (var target in candidates)
        {
            var targetGrid = target is Infantry infTarget ? infTarget.grid : target is Weapon weaponTarget ? (weaponTarget.GetWeakPointGrid() ?? weaponTarget.grid) : null;
            if (targetGrid == null || IsScreenBlocked(targetGrid)) continue;
            int dist = GetDistanceToTarget(target);
            if (dist >= 0 && dist <= maxAttackDepth && dist < bestDist)
            {
                bestDist = dist;
                bestTarget = target;
            }
        }

        if (bestTarget != null)
        {
            if (bestTarget is Infantry inf)
                PerformAttack(inf);
            else if (bestTarget is Weapon wp)
                AttackWeapon(wp);
        }
    }
}
