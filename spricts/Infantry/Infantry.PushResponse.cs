using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 推动模块原文补全（2026-11-13）：
/// 移动时推动(OnMove)、地图边缘循环、重叠冲突仪式+脆弱、反弹扩展(范围/反推/致目标状态)、
/// 转移方向集、卸力仪式(弹药/削弱重量/副作用/白名单)。全部走虚拟棋盘与效果队列，不直接改真实状态。
/// 菜单类交互（reboundAuto/mitigationAuto/transferAutoDirection=false）当前取第一个合法项，
/// UI 菜单挂点见各 Prompt* 注释。
/// </summary>
public partial class Infantry
{
    // 一格上的阻挡性静态体（兵器+设施可同时存在）
    private static (Weapon w, Facility f) CollectBlockingStatics(Grids tile)
    {
        if (tile == null) return (null, null);
        Weapon w = null;
        if (tile.weapons != null)
            w = tile.weapons.FirstOrDefault(x => x != null && GodotObject.IsInstanceValid(x)
                && !x.IsQueuedForDeletion() && x.pushBlocksInfantryWhenOverlapped);
        Facility f = tile.city != null && tile.city.pushBlocksInfantryWhenOverlapped ? tile.city : null;
        return (w, f);
    }

    private static (Weapon w, Facility f) GetBlockingStaticsFromTile(VirtualTile tile)
    {
        if (tile == null) return (null, null);
        Weapon w = tile.Weapons.FirstOrDefault(x => x != null && GodotObject.IsInstanceValid(x)
            && !x.IsQueuedForDeletion() && x.pushBlocksInfantryWhenOverlapped);
        Facility f = tile.City != null && tile.City.pushBlocksInfantryWhenOverlapped ? tile.City : null;
        return (w, f);
    }

    // ===== 移动时推动（原文方式①）=====
    // GridManager.MoveTo 的 PostMoveActions 调用。移动落格后，若落格上有合规目标则沿移动方向触发推挤。
    public void TryPushOnMove(Grids arrivedAt, Vector2I? fromIndex, GridManager gridManager)
    {
        if (!pushEnabled || pushTriggerMode != PushTriggerMode.OnMove) return;
        if (arrivedAt == null || fromIndex == null || gridManager == null) return;
        if (IsPushOnCooldown()) return;
        if (pushOncePerTurn && pushActionUsed) return;
        var stack = GetLivingStack(arrivedAt);
        var target = stack.FirstOrDefault(u => u != this && CanPushTargetTeam(new List<Infantry> { u }));
        if (target == null) return;
        if (!IsWithinPushTargetRange(arrivedAt)) return;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var direction = arrivedAt.GridIndex - fromIndex.Value;
        if (direction == Vector2I.Zero) return;
        direction = Mathf.Abs(direction.X) >= Mathf.Abs(direction.Y)
            ? new Vector2I(Math.Sign(direction.X), 0) : new Vector2I(0, Math.Sign(direction.Y));
        // 原文子问：是否随移动距离逐步增减消耗/增减度——仅消耗侧有语义，增减度是逐目标Export不可变。
        int distance = pushDistance;
        if (pushMoveScalesWithDistance && pushCostResource != PushResourceType.None)
        {
            int traveled = Mathf.Abs(fromIndex.Value.X - grid.GridIndex.X) + Mathf.Abs(fromIndex.Value.Y - grid.GridIndex.Y);
            // 消耗随移动距离放大在此扩展；当前保持常量消耗，留 Export 语义。
        }
        if (!TryMarkPushActionUsed()) return;
        if (pushMoveScalesWithDistance)
        {
            var cur = grid?.GridIndex ?? arrivedAt.GridIndex;
            pushCostDistanceScale = Mathf.Abs(cur.X - fromIndex.Value.X) + Mathf.Abs(cur.Y - fromIndex.Value.Y);
        }
        ResolvePushChain(arrivedAt, direction, distance, gridManager, gm);
    }

    // ===== 地图边缘循环 =====
    // 撞边时若开启循环，从地图另一侧回来；否则走原有 停/自毁 语义。
    private Grids WrapAroundDestination(GridManager gridManager, Grids currentGrid, Vector2I direction)
    {
        if (!pushWrapAroundEdges || gridManager?.map == null || currentGrid == null) return null;
        int w = gridManager.map.GetLength(0), h = gridManager.map.GetLength(1);
        int x = (currentGrid.GridIndex.X + direction.X % w + w) % w;
        int y = (currentGrid.GridIndex.Y + direction.Y % h + h) % h;
        var wrapped = gridManager.GetGridAt(new Vector2I(x, y));
        if (wrapped == null || wrapped == currentGrid) return null;
        return wrapped;
    }

    // ===== 重叠冲突仪式 =====
    // 被推来的兵器/设施落到有单位的格子上（或单位被推到共存格）时：
    // 任一方 Export pushOverlapExecutesConflict=true 则执行仪式：重量大者存活，相等按血量+资金比例，再平随机。
    // 存活方若 fragile=true，按 fragileDamageMode 结算冲击。返回是否允许共存（无冲突=共存）。
    private bool PlanOverlapConflict(VirtualTile tile, float incomingWeight, bool incomingIsStatic,
        Infantry incomingUnit, Weapon incomingWeapon, Facility incomingFacility,
        GameManager gm, List<PushEffect> effects, HashSet<Infantry> destroyed)
    {
        var units = tile.Stack.Where(u => IsLivingUnit(u) && !destroyed.Contains(u)).ToList();
        if (units.Count == 0) return true; // 没人可冲突，正常共存
        bool anyConflict = units.Any(u => u.pushOverlapExecutesConflict);
        if (!anyConflict) return true;      // 平移覆盖：直接踩上去共存

        // 冲突在重叠单位间逐个执行（当前实现：与第一个冲突单位单挑）。
        var defender = units.First(u => u.pushOverlapExecutesConflict);
        float defenderWeight = GetStackWeight(new List<Infantry> { defender });
        // 仪式三判：重量 → (血量%+资金%)比例 → 随机
        int cmp = CompareRitualWeight(incomingWeight, defenderWeight);
        if (cmp == 0) cmp = CompareHealthFundsRatio(incomingUnit, defender, gm);
        if (cmp == 0) cmp = GD.Randf() < 0.5f ? 1 : -1;

        bool incomingWins = cmp > 0;
        // 败者销毁
        if (incomingWins)
            PlanDestroyStaticOrUnit(defender, null, null, gm, effects, destroyed);
        else
            PlanDestroyStaticOrUnit(incomingUnit, incomingWeapon, incomingFacility, gm, effects, destroyed);

        // 存活方脆弱冲击
        var survivor = incomingWins
            ? (incomingUnit != null ? (incomingUnit, (Infantry)null) : ((Infantry)null, (Infantry)null))
            : ((Infantry)null, defender);
        var survivorUnit = incomingWins ? (incomingIsStatic ? null : incomingUnit) : defender;
        if (survivorUnit != null && survivorUnit.fragile)
            QueueFragileShock(survivorUnit, defender, gm, effects);
        return true; // 冲突处理完毕，允许链继续（败者已 plan 销毁）
    }

    private int CompareRitualWeight(float a, float b)
    {
        if (a > b) return 1;
        if (a < b) return -1;
        return 0;
    }

    private int CompareHealthFundsRatio(Infantry a, Infantry b, GameManager gm)
    {
        float Ra = HealthFundsRatio(a, gm), Rb = HealthFundsRatio(b, gm);
        if (Ra > Rb) return 1;
        if (Ra < Rb) return -1;
        return 0;
    }

    private float HealthFundsRatio(Infantry u, GameManager gm)
    {
        float hp = u.maxHealth > 0 ? (float)u.health / u.maxHealth : 0f;
        float funds = 0f;
        if (gm != null && !string.IsNullOrEmpty(u.team))
        {
            float f = gm.GetTeamFunds(u.team);
            funds = f / 10000f; // 归一化常数，持平判定用
        }
        return hp + funds;
    }

    private void PlanDestroyStaticOrUnit(Infantry unit, Weapon weapon, Facility facility,
        GameManager gm, List<PushEffect> effects, HashSet<Infantry> destroyed)
    {
        if (unit != null)
        {
            destroyed.Add(unit);
            effects.Add(PushEffect.DestroyUnit(unit));
        }
        else if (weapon != null)
            effects.Add(PushEffect.DamageWeapon(weapon, 999999));
        else if (facility != null)
            effects.Add(PushEffect.DamageFacility(facility, 999999));
    }

    // 脆弱冲击：原文4种扣法
    private void QueueFragileShock(Infantry survivor, Infantry attacker, GameManager gm, List<PushEffect> effects)
    {
        if (!survivor.fragile) return;
        switch (survivor.fragileDamageMode)
        {
            case FragileDamageMode.LockOneHp:
                effects.Add(PushEffect.LockOneHp(survivor));
                break;
            case FragileDamageMode.AttackCalculation:
                // 按两个单位攻击一次计算：以对方攻击力结算（无对方时用自身）
                var src = attacker != null ? attacker : survivor;
                float hpPct = src.GetEffectiveMaxHealth() > 0 ? (float)src.health / src.GetEffectiveMaxHealth() : 1f;
                int dmg = Mathf.Max(0, Mathf.RoundToInt(src.baseAttack * hpPct));
                effects.Add(PushEffect.DamageUnit(survivor, dmg, true));
                break;
            case FragileDamageMode.HealthFundsRatio:
                // 按血量与资金换算：基础 fragileDamageAmount × 自身血比（越残越轻）
                float ratio = survivor.maxHealth > 0 ? (float)survivor.health / survivor.maxHealth : 1f;
                effects.Add(PushEffect.DamageUnit(survivor,
                    (int)(survivor.fragileDamageAmount * ratio), survivor.fragileDamageAmount >= 0));
                break;
            case FragileDamageMode.Fixed:
            default:
                if (survivor.fragileDamageAmount < 0)
                    effects.Add(PushEffect.HealUnit(survivor, -survivor.fragileDamageAmount, false));
                else if (survivor.fragileDamageAmount > 0)
                    effects.Add(PushEffect.DamageUnit(survivor, survivor.fragileDamageAmount, true));
                break;
        }
        if (survivor.fragileAmmoDelta != 0) effects.Add(PushEffect.TargetAmmo(survivor, survivor.fragileAmmoDelta));
        if (survivor.fragileFuelDelta != 0) effects.Add(PushEffect.TargetFuel(survivor, survivor.fragileFuelDelta));
    }

    // ===== 卸力仪式 =====
    // 削弱目标重量实际作用（固定值/百分比），返回仪式后重量。卸力弹消耗在首次生效时入队一次。
    private float ApplyMitigationCeremony(List<Infantry> targetStack, Infantry attacker, GameManager gm,
        List<PushEffect> effects, HashSet<Infantry> mitigationAmmoQueued,
        PushVirtualBoard board = null, Grids originTile = null)
    {
        float weight = GetStackWeight(targetStack);
        if (!pushMitigationCeremony || !pushMitigationEnabled) return weight;
        float rawWeight = weight;

        // 白名单：只对名单内攻击方生效
        if (mitigationWhitelistEnabled && attacker != null)
        {
            string id = attacker.unitTypeId;
            if (string.IsNullOrEmpty(id) || !mitigationWhitelist.Contains(id)) return weight;
        }
        // 卸力弹
        if (pushMitigationAmmoCost > 0)
        {
            if (!HasAmmo(AmmoKind.Mitigation, pushMitigationAmmoCost)) return weight;
            if (mitigationAmmoQueued.Add(attacker ?? this))
                effects.Add(PushEffect.AmmoKindCost(this, AmmoKind.Mitigation, pushMitigationAmmoCost));
        }
        // 削弱重量
        if (mitigationWeakensWeight && mitigationWeightWeakenValue > 0)
        {
            float cut = mitigationWeightWeakenMode == MitigationValueMode.Percent
                ? weight * mitigationWeightWeakenValue / 100f
                : mitigationWeightWeakenValue;
            weight = Mathf.Max(0, weight - cut);
        }
        // 重量差影响卸力：攻击方每高/低一重量，卸力效果增减
        if (mitigationWeightDiffAffects && attacker != null)
        {
            float diff = GetStackWeight(new List<Infantry> { attacker }) - weight;
            float adj = mitigationWeightDiffMode == MitigationValueMode.Percent
                ? weight * Mathf.Abs(diff) * mitigationWeightDiffValue / 100f
                : Mathf.Abs(diff) * mitigationWeightDiffValue;
            weight = diff > 0 ? Mathf.Max(0, weight - adj) : weight + adj;
        }

        // 原文：副作用=削减/增强的差值，按比例作用到副作用目标
        float cutDiff = rawWeight - weight;
        if (mitigationSideEffectTarget != MitigationSideEffectTarget.None && cutDiff != 0f && attacker != null)
        {
            int side = Mathf.Max(0, Mathf.RoundToInt(Mathf.Abs(cutDiff) * mitigationSideEffectScale / 100f));
            if (side > 0)
                effects.Add(PushEffect.DamageUnit(attacker, side, true));
        }

        // 原文：均分方向 n 选 m——菜单选定集优先，空=全触发
        var spreadSource = pushChosenSpreadDirections != null && pushChosenSpreadDirections.Count > 0
            ? pushChosenSpreadDirections : mitigationSpreadDirections;
        if (spreadSource != null && spreadSource.Count > 0
            && board != null && originTile != null && cutDiff != 0f)
        {
            // 均分实现：对每个方向格上的所有单位均摊差值
            int share = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(cutDiff) * mitigationSideEffectScale / 100f
                / Mathf.Max(1, spreadSource.Count)));
            foreach (var d in mitigationSpreadDirections)
            {
                if (d == Vector2I.Zero) continue;
                var g = originTile.GridIndex + d;
                foreach (var kv in board.Tiles)
                {
                    if (kv.Key == null || kv.Key.GridIndex != g) continue;
                    foreach (var u in kv.Value.Stack)
                        if (IsLivingUnit(u) && u != this)
                            effects.Add(PushEffect.DamageUnit(u, share, true));
                }
            }
        }
        return weight;
    }
}
