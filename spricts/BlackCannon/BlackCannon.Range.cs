// BlackCannon.cs - 黑炮：三角形范围，固定百分比伤害，支持自定义目标选择和改良伤害模式
// ✅ 新增：完整弹药系统 + 多回合冷却系统（还原原版高级战争原型炮/完全体差异）
using Godot;
using System.Collections.Generic;
using System.Linq;

// BlackCannon · Range（由 BlackCannon.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class BlackCannon
{

public override void ShowAttackRange()
{
    var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
    if (gm?.gridManager == null) return;

    if (!HasAmmoAndCooldown())
    {
        ShowAttackRangeAsDisabled(gm);
        return;
    }

    gm.gridManager.ClearWeaponRange();
    Grids.IsForceActionMode = true;

    var rangeGrids = CalculateAttackRange();
    var fog = gm.fogOfWarManager;
    bool isFogEnabled = fog != null && fog.isFogOfWarEnabled;

    foreach (var g in rangeGrids)
    {
        if (isFogEnabled && fog != null && !fog.IsGridVisible(g)) continue;
        bool hasValidTarget = HasValidTargetInGrid(g);

        if (hasValidTarget)
        {
            g.attackRangeIcon?.Show();
            if (g.attackRangeIcon != null)
                g.attackRangeIcon.Modulate = new Color(1, 0.2f, 0.2f, 0.9f);

            // ✅ 修复：lambda 中检查实例有效性
            g.OnClickGrid = (to) => {
                if (!IsInstanceValid(this)) {
                    gm.gridManager.ClearWeaponRange();
                    Grids.IsForceActionMode = false;
                    return;
                }
                OnRangeGridClicked(g);
            };
            gm.gridManager.OverrideUnitInput(g, true);
            
            g.IsInWeaponRange = true;
        }
        else
        {
            g.pathIcon?.Show();
            if (g.pathIcon != null)
                g.pathIcon.Modulate = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            
            g.OnClickGrid = (to) => {
                if (!IsInstanceValid(this)) {
                    gm.gridManager.ClearWeaponRange();
                    Grids.IsForceActionMode = false;
                    foreach (var grid in rangeGrids)
                        gm.gridManager.OverrideUnitInput(grid, false);
                    return;
                }
                gm.gridManager.ClearWeaponRange();
                Grids.IsForceActionMode = false;
                foreach (var grid in rangeGrids)
                    gm.gridManager.OverrideUnitInput(grid, false);
                if (!hasActed)
                {
                    var menu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                    menu?.ShowWeaponMenu(this);
                }
            };
            gm.gridManager.OverrideUnitInput(g, true);
            
            g.IsInWeaponRange = true;
        }
    }

    // 范围外点击
    foreach (var g in gm.gridManager.grids)
    {
        if (!rangeGrids.Contains(g))
        {
            g.OnClickEmpty = () => {
                if (!IsInstanceValid(this)) {
                    gm.gridManager.ClearWeaponRange();
                    Grids.IsForceActionMode = false;
                    foreach (var grid in rangeGrids)
                        gm.gridManager.OverrideUnitInput(grid, false);
                    return;
                }
                gm.gridManager.ClearWeaponRange();
                Grids.IsForceActionMode = false;
                foreach (var grid in rangeGrids)
                    gm.gridManager.OverrideUnitInput(grid, false);
                
                if (!hasActed)
                {
                    var menu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                    menu?.ShowWeaponMenu(this);
                }
            };
        }
    }
}

protected void ShowAttackRangeAsDisabled(GameManager gm)
{
    gm.gridManager.ClearWeaponRange();
    var rangeGrids = CalculateAttackRange();
    var fog = gm.fogOfWarManager;
    bool isFogEnabled = fog != null && fog.isFogOfWarEnabled;

    foreach (var g in rangeGrids)
    {
        if (isFogEnabled && fog != null && !fog.IsGridVisible(g)) continue;
        g.pathIcon?.Show();
        if (g.pathIcon != null)
            g.pathIcon.Modulate = new Color(0.5f, 0.5f, 0.5f, 0.3f);
        g.IsInWeaponRange = true;
    }

    foreach (var g in gm.gridManager.grids)
    {
        if (!rangeGrids.Contains(g))
        {
            g.OnClickEmpty = () => {
                if (!IsInstanceValid(this)) {
                    gm.gridManager.ClearWeaponRange();
                    return;
                }
                gm.gridManager.ClearWeaponRange();
                var menu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                menu?.ShowWeaponMenu(this);
            };
        }
    }
}

    // 计算三角形攻击范围内的所有格子
    public override List<Grids>  CalculateAttackRange()
    {
        var range = new List<Grids>();
        if (grid == null) return range;

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null) return range;

        Vector2I dir = DirectionVectors[(int)direction];
        Vector2I perp = PerpVectors[(int)direction];
        Vector2I start = grid.GridIndex;

        // 遍历每一层深度（距离）
        for (int depth = 1; depth <= maxAttackDepth; depth++)
        {
            // 当前层的中心点（沿主方向前进depth格）
            Vector2I center = start + dir * depth;

            // 计算当前层宽度：第1层3格，第2层5格...第n层 (1+2*n) 格，最大19格对应depth=9
            int halfWidth = depth;

            // 遍历当前层的横向所有格子
            for (int w = -halfWidth; w <= halfWidth; w++)
            {
                Vector2I checkPos = center + perp * w;

                // 边界检查
                if (checkPos.X < 0 || checkPos.X >= gm.gridManager.searchRange.X ||
                    checkPos.Y < 0 || checkPos.Y >= gm.gridManager.searchRange.Y)
                    continue;

                var targetGrid = gm.gridManager.map[checkPos.X, checkPos.Y];
                if (targetGrid != null && targetGrid.gridType != GridType.METEORITE)
                {
                    // ✅ N屏隔离：跨屏格子不进射程（开canCrossScreens才可跨图）
                    if (!canCrossScreens && targetGrid.screenId != grid.screenId) continue;
                    range.Add(targetGrid);
                }
            }
        }

        return range;
    }




    private bool CanSelectFacility(Grids targetGrid)
    {
        var facility = targetGrid?.city;
        if (facility == null || !IsInstanceValid(facility) || !facility.CanBeAttackedFrom(grid)) return false;
        if (team == TeamHelper.Player0) return true;
        return targetSelectionMode switch
        {
            TargetSelectionMode.AllSelect => true,
            TargetSelectionMode.OnlyEnemyUnits => facility.facilityTeam != team,
            TargetSelectionMode.OnlyUserUnits => facility.facilityTeam == team,
            _ => false
        };
    }

    public bool HasValidTargetInGrid(Grids targetGrid)
    {
        if (CanSelectFacility(targetGrid)) return true;
        // P0 兵器：无差别攻击，除了自己谁都能打，跳过友军/敌军判定
        if (team == TeamHelper.Player0)
        {
            if (targetGrid.infantries.Count > 0) return true;
            if (CanAttackWeapon && targetGrid.weapon != null && targetGrid.weapon != this) return true;
            return false;
        }

        switch (targetSelectionMode)
        {
            case TargetSelectionMode.AllSelect:
                // ✅ 检查单位
                if (targetGrid.infantries.Count > 0) return true;
                // ✅ 检查兵器（根据CanAttackWeapon设置）
                if (CanAttackWeapon && targetGrid.weapon != null && targetGrid.weapon != this) return true;
                return false;

            case TargetSelectionMode.OnlyEnemyUnits:
                // ✅ 检查敌方单位
                if (targetGrid.HasEnemyInfantry(team)) return true;
                // ✅ 检查敌方兵器（根据CanAttackWeapon设置）
                if (CanAttackWeapon && targetGrid.weapon != null && targetGrid.weapon.team != team && targetGrid.weapon != this) return true;
                return false;

            case TargetSelectionMode.OnlyUserUnits:
                // ✅ 检查己方单位
                if (targetGrid.infantries.Any(i => i.team == team)) return true;
                // ✅ 检查己方兵器（根据CanAttackWeapon设置）
                if (CanAttackWeapon && targetGrid.weapon != null && targetGrid.weapon.team == team && targetGrid.weapon != this) return true;
                return false;

            default:
                return false;
        }
    }

    private void OnRangeGridClicked(Grids targetGrid)
    {
        // ✅ 关键修复：检查格子是否还有效
        if (targetGrid == null || !IsInstanceValid(targetGrid) || IsScreenBlocked(targetGrid)) return;

        // ✅ 再次检查弹药和冷却
        if (!HasAmmoAndCooldown())
        {
            return;
        }

        var targets = GetValidTargetsInGrid(targetGrid);
        if (targets.Count == 0) return;

        var target = targets[0];

        // ✅ 关键修复：再次检查目标有效性
        if (target == null || !IsInstanceValid(target)) return;

        // ✅ 消耗弹药（如果开启弹药系统）
        if (!ConsumeAmmo()) return;

        // ✅ 消耗冷却次数
        if (useCooldownSystem)
        {
            attacksRemainingInCycle--;
        }

        if (target is Infantry infantry)
        {
            PerformAttack(infantry);
        }
        else if (target is Weapon weapon)
        {
            // ✅ 关键修复：攻击前检查兵器是否已被销毁
            if (IsInstanceValid(weapon)) PerformAttackOnWeapon(weapon);
        }
        else if (target is Facility facility && IsInstanceValid(facility)) PerformAttackOnFacility(facility);
    }

    private List<Node2D> GetValidTargetsInGrid(Grids targetGrid)
    {
        var targets = new List<Node2D>();
        if (CanSelectFacility(targetGrid)) targets.Add(targetGrid.city);

        // P0 兵器：无差别攻击，除了自己谁都能打，跳过友军/敌军判定
        if (team == TeamHelper.Player0)
        {
            targets.AddRange(targetGrid.infantries.Where(i => IsInstanceValid(i)).Cast<Node2D>());
            if (CanAttackWeapon && targetGrid.weapon != null && IsInstanceValid(targetGrid.weapon) && targetGrid.weapon != this)
                targets.Add(targetGrid.weapon);
            return targets;
        }

        switch (targetSelectionMode)
        {
            case TargetSelectionMode.AllSelect:
                // ✅ 添加所有单位
                targets.AddRange(targetGrid.infantries.Where(i => IsInstanceValid(i)).Cast<Node2D>());
                // ✅ 添加兵器（根据CanAttackWeapon设置）
                if (CanAttackWeapon && targetGrid.weapon != null && IsInstanceValid(targetGrid.weapon) && targetGrid.weapon != this)
                    targets.Add(targetGrid.weapon);
                break;

            case TargetSelectionMode.OnlyEnemyUnits:
                // ✅ 添加敌方单位
                targets.AddRange(targetGrid.infantries
                    .Where(i => IsInstanceValid(i) && i.team != team)
                    .Cast<Node2D>());
                // ✅ 添加敌方兵器（根据CanAttackWeapon设置）
                if (CanAttackWeapon && targetGrid.weapon != null && IsInstanceValid(targetGrid.weapon) && targetGrid.weapon.team != team && targetGrid.weapon != this)
                    targets.Add(targetGrid.weapon);
                break;

            case TargetSelectionMode.OnlyUserUnits:
                // ✅ 添加己方单位
                targets.AddRange(targetGrid.infantries
                    .Where(i => IsInstanceValid(i) && i.team == team)
                    .Cast<Node2D>());
                // ✅ 添加己方兵器（根据CanAttackWeapon设置）
                if (CanAttackWeapon && targetGrid.weapon != null && IsInstanceValid(targetGrid.weapon) && targetGrid.weapon.team == team && targetGrid.weapon != this)
                    targets.Add(targetGrid.weapon);
                break;
        }

        return targets;
    }

    private int GetDistanceToTarget(Godot.Node2D target)
    {
        if (grid == null || target == null) return -1;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return -1;

        var targetPos = target.Position;
        int tx = (int)((targetPos.X - gm.gridManager.startPos.X) / gm.gridManager.gridSize.X);
        int ty = (int)((targetPos.Y - gm.gridManager.startPos.Y) / gm.gridManager.gridSize.Y);
        return Mathf.Abs(grid.GridIndex.X - tx) + Mathf.Abs(grid.GridIndex.Y - ty);
    }
}
