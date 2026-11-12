using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Grid-accurate forced displacement. Stack occupants move as one mass.</summary>
public partial class Infantry
{
    public void TryPushTargetOnAttack(Infantry target)
    {
        if (!pushEnabled || pushTriggerMode != PushTriggerMode.Attack || pushDistance <= 0
            || grid == null || target == null || !GodotObject.IsInstanceValid(target)
            || target.grid == null || !IsWithinPushTargetRange(target.grid)) return;

        Vector2I direction = GetPushDirection(target.grid);

        if (direction == Vector2I.Zero) return;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null) return;
        var initialStack = GetLivingStack(target.grid);
        var pusherStack = GetLivingStack(grid);
        if (pusherStack.Count == 0) pusherStack.Add(this);
        if (!CanPushTargetTeam(initialStack)
            || !CanPushStack(pusherStack, initialStack, GetPushForceMultiplier())
            || !CanPayPushCost(gm)) return;
        ResolvePushChain(target.grid, direction, pushDistance, gm.gridManager, gm);
    }

    public void TryPushWeaponOnAttack(Weapon target, bool fromSeparateAction = false)
    {
        if ((fromSeparateAction ? pushTriggerMode == PushTriggerMode.SeparateAction : pushTriggerMode == PushTriggerMode.Attack)
            && pushTargetClass == PushTargetClass.AnyPushableEntity
            && target != null && target.grid != null && IsWithinPushTargetRange(target.grid)
            && CanPushTeamName(target.team, target.GetType().Name)
            && CanPushNonInfantryTarget(target.grid, target.pushableByPhysics, target.GetEffectiveWeight(),
                target.equalWeightPushMode, target.isDestroyed))
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm?.gridManager == null || target == null) return;
            Vector2I direction = GetPushDirection(target.grid);
            if (direction == Vector2I.Zero || !CanPayPushCost(gm)) return;
            var firstDestination = gm.gridManager.GetGridAt(target.grid.GridIndex + direction);
            if (!CanMoveStaticTargetTo(firstDestination, target, null)) return;

            bool moved = false;
            for (int step = 0; step < pushDistance; step++)
            {
                var next = gm.gridManager.GetGridAt(target.grid.GridIndex + direction);
                if (!CanMoveStaticTargetTo(next, target, null)
                    || !target.MoveByPhysicsPush(next, gm.weaponManager)) break;
                moved = true;
            }
            if (!moved) return;
            PayPushCost(gm);
            ApplyStaticPushEffects(target);
        }
    }

    public void TryPushFacilityOnAttack(Facility target, bool fromSeparateAction = false)
    {
        if ((fromSeparateAction ? pushTriggerMode != PushTriggerMode.SeparateAction : pushTriggerMode != PushTriggerMode.Attack)
            || pushTargetClass != PushTargetClass.AnyPushableEntity
            || target == null || target.ParentGrid == null || !IsWithinPushTargetRange(target.ParentGrid)
            || !CanPushTeamName(target.facilityTeam, target.GetType().Name)
            || !CanPushNonInfantryTarget(target.ParentGrid, target.pushableByPhysics, target.GetEffectiveWeight(),
                target.equalWeightPushMode, target.health <= 0))
            return;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return;
        Vector2I direction = GetPushDirection(target.ParentGrid);
        if (direction == Vector2I.Zero || !CanPayPushCost(gm)) return;
        var firstDestination = gm.gridManager.GetGridAt(target.ParentGrid.GridIndex + direction);
        if (!CanMoveStaticTargetTo(firstDestination, null, target)) return;

        bool moved = false;
        for (int step = 0; step < pushDistance; step++)
        {
            var next = gm.gridManager.GetGridAt(target.ParentGrid.GridIndex + direction);
            if (!CanMoveStaticTargetTo(next, null, target) || !target.MoveByPhysicsPush(next)) break;
            moved = true;
        }
        if (!moved) return;
        PayPushCost(gm);
        ApplyStaticPushEffects(target);
    }

    private bool CanMoveStaticTargetTo(Grids destination, Weapon movingWeapon = null,
        Facility movingFacility = null)
    {
        if (destination == null || GetLivingStack(destination).Count > 0
            || HasForeignMultiTileOccupant(destination, new List<Infantry>()))
            return false;
        if (destination.city != null && destination.city != movingFacility)
            return false;
        return destination.weapons == null
            || destination.weapons.All(w => w == null || !GodotObject.IsInstanceValid(w)
                || w == movingWeapon);
    }

    private void ApplyStaticPushEffects(Weapon target)
    {
        if (target == null || !GodotObject.IsInstanceValid(target) || target.isDestroyed) return;
        if (pushLockTargetToOneHp) target.health = 1;
        else
        {
            int damage = (int)Math.Clamp((long)pushTargetDamage + pushRamDamage, 0L, int.MaxValue);
            if (damage > 0) target.TakeDamage(damage, this);
        }
    }

    private void ApplyStaticPushEffects(Facility target)
    {
        if (target == null || !GodotObject.IsInstanceValid(target) || target.health <= 0) return;
        if (pushLockTargetToOneHp) target.health = 1;
        else
        {
            int damage = (int)Math.Clamp((long)pushTargetDamage + pushRamDamage, 0L, int.MaxValue);
            if (damage > 0) target.TakeDamage(damage, this);
        }
    }

    private bool CanPushNonInfantryTarget(Grids targetGrid, bool? pushable, float? targetWeight,
        EqualWeightPushMode targetMode, bool invalid)
    {
        if (!pushEnabled || (pushTriggerMode != PushTriggerMode.Attack
            && pushTriggerMode != PushTriggerMode.SeparateAction) || pushDistance <= 0
            || targetGrid == null || invalid || pushable != true || targetWeight == null)
            return false;
        var pusherStack = GetLivingStack(grid);
        if (pusherStack.Count == 0) pusherStack.Add(this);
        float ownWeight = GetStackWeight(pusherStack) * GetPushForceMultiplier();
        if (!pushUsesWeight) return true;
        if (ownWeight > targetWeight.Value && !Mathf.IsEqualApprox(ownWeight, targetWeight.Value))
            return true;
        if (!Mathf.IsEqualApprox(ownWeight, targetWeight.Value)) return false;
        bool pusherAllows = StackAllowsSimultaneous(pusherStack);
        bool pusherStops = StackStopsOnEqual(pusherStack);
        if (pusherAllows && targetMode == EqualWeightPushMode.AllowSimultaneous) return true;
        if (pusherStops && targetMode == EqualWeightPushMode.Stop) return false;
        return GD.Randf() < 0.5f;
    }

    public void BeginSeparatePushTargetSelection()
    {
        if (!CanUseSeparatePushAction() || grid == null || pushDistance <= 0) return;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var gridManager = gm?.gridManager;
        if (gridManager == null) return;

        gridManager.CloseRange();
        gridManager.HideAttackRange();
        gridManager.ClearWeaponRange();
        int minRange = Mathf.Max(1, pushMinTargetRange);
        int maxRange = Mathf.Max(minRange, pushRange);
        foreach (var tile in gridManager.grids)
        {
            if (tile == null || tile == grid) continue;
            int range = Mathf.Abs(tile.GridIndex.X - grid.GridIndex.X)
                + Mathf.Abs(tile.GridIndex.Y - grid.GridIndex.Y);
            if (range < minRange || range > maxRange
                || !gridManager.IsRangeAllowed(grid, tile, attackCanCrossScreens))
                continue;
            var stack = GetLivingStack(tile);
            bool hasPushableStatic = pushTargetClass == PushTargetClass.AnyPushableEntity
                && ((tile.weapons?.Any(w => w != null && GodotObject.IsInstanceValid(w)
                    && w.pushableByPhysics && CanPushTeamName(w.team, w.GetType().Name)) ?? false)
                    || (tile.city != null && tile.city.pushableByPhysics
                        && CanPushTeamName(tile.city.facilityTeam, tile.city.GetType().Name)));
            if ((stack.Count == 0 || !CanPushTargetTeam(stack))
                && !hasPushableStatic) continue;
            tile.attackRangeIcon?.Show();
            tile.IsInAttackRangeMode = true;
            gridManager.attackRange.Add(tile);
            tile.OnClickGrid = selected => ExecuteSeparatePushOnGrid(selected);
        }
        foreach (var tile in gridManager.grids)
            if (tile != null && !gridManager.attackRange.Contains(tile))
                tile.OnClickEmpty = CancelSeparatePushTargetSelection;
        gm.isSelectingAttackTarget = true;
    }

    private void CancelSeparatePushTargetSelection()
    {
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.gridManager?.HideAttackRange();
        gm?.gridManager?.ClearAllEmptyCallbacks();
        if (gm != null) gm.isSelectingAttackTarget = false;
    }

    private bool IsWithinPushTargetRange(Grids targetGrid)
    {
        if (grid == null || targetGrid == null) return false;
        int distance = Mathf.Abs(targetGrid.GridIndex.X - grid.GridIndex.X)
            + Mathf.Abs(targetGrid.GridIndex.Y - grid.GridIndex.Y);
        int minRange = Mathf.Max(1, pushMinTargetRange);
        int maxRange = Mathf.Max(minRange, pushRange);
        return distance >= minRange && distance <= maxRange;
    }

    private bool CanPushTargetTeam(List<Infantry> stack)
    {
        // A stack moves as one object, so every occupant must satisfy the selected team policy.
        return stack != null && stack.Count > 0 && stack.All(unit =>
        {
            string typeId = unit.UnitTypeId ?? unit.GetType().Name;
            if (pushTargetWhitelist != null && pushTargetWhitelist.Length > 0
                && !pushTargetWhitelist.Contains(typeId) && !pushTargetWhitelist.Contains(unit.GetType().Name))
                return false;
            if (pushTargetBlacklist != null && (pushTargetBlacklist.Contains(typeId)
                || pushTargetBlacklist.Contains(unit.GetType().Name)))
                return false;
            return CanPushTeamRelation(unit.team);
        });
    }

    private bool CanPushTeamName(string targetTeam, string targetType = null)
    {
        if (string.IsNullOrEmpty(targetTeam)) return false;
        if (!string.IsNullOrEmpty(targetType))
        {
            if (pushTargetWhitelist != null && pushTargetWhitelist.Length > 0
                && !pushTargetWhitelist.Contains(targetType)) return false;
            if (pushTargetBlacklist != null && pushTargetBlacklist.Contains(targetType)) return false;
        }
        return CanPushTeamRelation(targetTeam);
    }

    private bool CanPushTeamRelation(string targetTeam)
    {
        bool sameTeam = targetTeam == team;
        bool allied = !sameTeam && TeamHelper.AreAllied(team, targetTeam);
        switch (pushTargetPolicy)
        {
            case PushTargetTeamMode.EnemiesOnly: return !sameTeam && !allied;
            case PushTargetTeamMode.FriendliesOnly: return sameTeam;
            case PushTargetTeamMode.AlliesOnly: return allied;
            case PushTargetTeamMode.Everyone: return true;
            default:
                if (sameTeam) return pushCanTargetFriendlies;
                if (allied) return pushCanTargetAllies;
                return pushCanTargetEnemies;
        }
    }

    private Vector2I GetPushDirection(Grids targetGrid)
    {
        if (targetGrid == null || grid == null) return Vector2I.Zero;
        if (pushDirectionMode == PushDirectionMode.Fixed)
        {
            if (fixedPushDirection == Vector2I.Zero) return Vector2I.Zero;
            return Mathf.Abs(fixedPushDirection.X) >= Mathf.Abs(fixedPushDirection.Y)
                ? new Vector2I(Math.Sign(fixedPushDirection.X), 0)
                : new Vector2I(0, Math.Sign(fixedPushDirection.Y));
        }

        Vector2I delta = targetGrid.GridIndex - grid.GridIndex;
        return Math.Abs(delta.X) >= Math.Abs(delta.Y)
            ? new Vector2I(Math.Sign(delta.X), 0)
            : new Vector2I(0, Math.Sign(delta.Y));
    }

    private float GetPushForceMultiplier() =>
        pushForceScalesWithDistance ? Mathf.Max(0.01f, pushDistance * pushForcePerTile) : 1.0f;

    private bool CanPayPushCost(GameManager gm)
    {
        int cost = Mathf.Max(0, pushCostAmount);
        if (cost == 0 || pushCostResource == PushResourceType.None) return true;
        return pushCostResource switch
        {
            PushResourceType.Funds => gm != null && gm.GetTeamFunds(team) >= cost,
            PushResourceType.Health => health > cost,
            PushResourceType.Fuel => fuel >= cost,
            PushResourceType.PrimaryAmmo => HasAmmo(AmmoKind.Primary, cost),
            PushResourceType.PushAmmo => HasAmmo(AmmoKind.Push, cost),
            _ => true
        };
    }

    private void PayPushCost(GameManager gm)
    {
        int cost = Mathf.Max(0, pushCostAmount);
        if (cost == 0) return;
        switch (pushCostResource)
        {
            case PushResourceType.Funds:
                gm?.SetTeamFunds(team, gm.GetTeamFunds(team) - cost);
                break;
            case PushResourceType.Health:
                health = Mathf.Max(1, health - cost);
                UpdateHpLabel();
                break;
            case PushResourceType.Fuel:
                fuel = Mathf.Max(0, fuel - cost);
                break;
            case PushResourceType.PrimaryAmmo:
                TryConsumeAmmo(AmmoKind.Primary, cost);
                break;
            case PushResourceType.PushAmmo:
                TryConsumeAmmo(AmmoKind.Push, cost);
                break;
        }
    }

    private int GetMitigatedPushDistance(List<Infantry> stack, int requestedDistance)
    {
        // A co-located stack shares one displacement result. Evaluate each member's
        // complete mitigation profile independently and use the strongest valid
        // profile; do not accidentally combine the best flat and percentage values
        // from two different units into a mitigation configuration neither owns.
        return stack.Where(u => IsLivingUnit(u) && u.pushMitigationEnabled)
            .Select(u =>
            {
                int remaining = Mathf.Max(0, requestedDistance - Mathf.Max(0, u.pushMitigationTiles));
                float percent = Mathf.Clamp(u.pushMitigationPercent, 0f, 100f);
                return Mathf.Clamp(Mathf.FloorToInt(remaining * (1f - percent / 100f)),
                    0, requestedDistance);
            })
            .DefaultIfEmpty(requestedDistance)
            .Min();
    }

    // Rebound and transfer compete for the same stack displacement. Resolve the
    // conflict by effective weight, then HP ratio, then team-funds ratio. Only an
    // exact tie is randomized; if the preferred module cannot legally execute, try
    // the other one without charging resources for the failed attempt. Plan-only:
    // costs become effects, movement becomes a move plan.
    private bool TryPlanStackDisplacement(List<Infantry> stack, int linkIndex, Grids origin,
        Vector2I pushDirection, PushVirtualBoard board, GridManager gridManager, GameManager gm,
        List<Grids> virtualPositions, List<PushMovePlan> movePlans, List<PushEffect> effects,
        Infantry pusher = null)
    {
        var rebounder = stack.FirstOrDefault(u => IsLivingUnit(u) && u.pushReboundEnabled);
        var transferer = stack.FirstOrDefault(u => IsLivingUnit(u) && u.pushTransferEnabled
            && u.pushTransferDirection != Vector2I.Zero);

        if (rebounder == null)
            return TryPlanTransfer(stack, linkIndex, gridManager, gm, board, virtualPositions, movePlans, effects, null, pusher);
        if (transferer == null)
            return TryPlanRebound(stack, linkIndex, origin, pushDirection, gridManager, gm, board,
                virtualPositions, movePlans, effects, null, pusher);

        int priority = CompareDisplacementPriority(rebounder, transferer, gm);
        if (priority == 0)
            priority = GD.Randf() < 0.5f ? 1 : -1;

        if (priority > 0)
            return TryPlanRebound(stack, linkIndex, origin, pushDirection, gridManager, gm, board,
                    virtualPositions, movePlans, effects, rebounder, pusher)
                || TryPlanTransfer(stack, linkIndex, gridManager, gm, board, virtualPositions, movePlans, effects, transferer, pusher);
        return TryPlanTransfer(stack, linkIndex, gridManager, gm, board, virtualPositions, movePlans, effects, transferer, pusher)
            || TryPlanRebound(stack, linkIndex, origin, pushDirection, gridManager, gm, board,
                virtualPositions, movePlans, effects, rebounder, pusher);
    }

    // Positive means first unit wins; negative means second unit wins.
    private static int CompareDisplacementPriority(Infantry first, Infantry second, GameManager gm)
    {
        float firstWeight = first.GetEffectiveWeight();
        float secondWeight = second.GetEffectiveWeight();
        if (!Mathf.IsEqualApprox(firstWeight, secondWeight))
            return firstWeight > secondWeight ? 1 : -1;

        float firstHpRatio = (float)first.health / Mathf.Max(1, first.GetEffectiveMaxHealth());
        float secondHpRatio = (float)second.health / Mathf.Max(1, second.GetEffectiveMaxHealth());
        if (!Mathf.IsEqualApprox(firstHpRatio, secondHpRatio))
            return firstHpRatio > secondHpRatio ? 1 : -1;

        float firstFundsRatio = gm == null ? 0f :
            (float)gm.GetTeamFunds(first.team) / Mathf.Max(1, gm.GetTeamFundsMax(first.team));
        float secondFundsRatio = gm == null ? 0f :
            (float)gm.GetTeamFunds(second.team) / Mathf.Max(1, gm.GetTeamFundsMax(second.team));
        if (!Mathf.IsEqualApprox(firstFundsRatio, secondFundsRatio))
            return firstFundsRatio > secondFundsRatio ? 1 : -1;
        return 0;
    }

    private bool TryPlanRebound(List<Infantry> stack, int linkIndex, Grids origin, Vector2I direction,
        GridManager gridManager, GameManager gm, PushVirtualBoard board, List<Grids> virtualPositions,
        List<PushMovePlan> movePlans, List<PushEffect> effects, Infantry preferredRebounder = null,
        Infantry pusher = null)
    {
        var rebounder = preferredRebounder != null && IsLivingUnit(preferredRebounder)
            ? preferredRebounder
            : stack.FirstOrDefault(u => IsLivingUnit(u) && u.pushReboundEnabled);
        if (rebounder == null || origin == null || board.Of(origin).Stack.Count > 0)
            return false;
        int ammoCost = Mathf.Max(0, rebounder.pushReboundAmmoCost);
        int fuelCost = Mathf.Max(0, rebounder.pushReboundFuelCost);
        int fundsCost = Mathf.Max(0, rebounder.pushReboundFundsCost);
        if (rebounder.GetAmmo(AmmoKind.Primary) < ammoCost || rebounder.fuel < fuelCost
            || (fundsCost > 0 && (gm == null || gm.GetTeamFunds(rebounder.team) < fundsCost)))
            return false;
        if (ammoCost > 0 && !rebounder.HasAmmo(AmmoKind.Primary, ammoCost)) return false;
        // 反弹推进弹（独立弹药）
        if (rebounder.pushReboundEnabled && rebounder.maxReboundAmmo > 0
            && !rebounder.HasAmmo(AmmoKind.Rebound, 1)) return false;

        // 蹬鼻子上脸：要反推回去，先按重量判定资格（推不动则反弹整个不成立）
        if (rebounder.reboundCounterPush && pusher != null && IsLivingUnit(pusher))
        {
            float myW = GetStackWeight(stack), atW = GetStackWeight(new List<Infantry> { pusher });
            if (atW >= myW && !Mathf.IsEqualApprox(atW, myW)) return false;
        }

        // 反弹检查范围 n-m（特殊值0=自身原位，即"原地不动弹力只恢复原样"）
        var currentGrid = virtualPositions[linkIndex];
        var candidates = new List<Grids>();
        if (rebounder.reboundRangeMax <= 0 && !rebounder.pushReboundToOrigin)
        {
            candidates.Add(currentGrid); // 自身
        }
        else if (rebounder.pushReboundToOrigin)
        {
            candidates.Add(origin);
        }
        else
        {
            int lo = Mathf.Max(1, rebounder.reboundRangeMin), hi = Mathf.Max(lo, rebounder.reboundRangeMax);
            var range = new List<int>();
            if (rebounder.pushChosenReboundRange.HasValue)
            {
                int c = Mathf.Clamp(rebounder.pushChosenReboundRange.Value, lo, hi);
                range.Add(c);
            }
            else
                for (int n = hi; n >= lo; n--) range.Add(n);
            foreach (var n in range)
            {
                var g = gridManager?.GetGridAt(origin.GridIndex - direction * n);
                if (g != null) candidates.Add(g);
            }
        }

        Grids reboundDestination = null;
        foreach (var c in candidates)
        {
            if (c == null) continue;
            var t = board.Of(c);
            bool isSelf = c == currentGrid;
            if (!isSelf && (t.Stack.Count > 0 || t.City != null || t.Weapons.Count > 0)) continue;
            if (!isSelf && t.Stack.Any(u => !stack.Contains(u))) continue;
            reboundDestination = c;
            break;
        }
        if (reboundDestination == null) return false;

        // 消耗入队：主弹药/燃油/资金 + 反弹推进弹
        if (ammoCost > 0) effects.Add(PushEffect.Ammo(rebounder, ammoCost));
        if (fuelCost > 0) effects.Add(PushEffect.FuelCost(rebounder, fuelCost));
        if (fundsCost > 0) effects.Add(PushEffect.Funds(rebounder.team, fundsCost));
        if (rebounder.maxReboundAmmo > 0)
            effects.Add(PushEffect.AmmoKindCost(rebounder, AmmoKind.Rebound, 1));

        // 反弹致发动者扣状态（负=增）
        if (pusher != null && IsLivingUnit(pusher))
        {
            if (rebounder.reboundAttackerDamage < 0)
                effects.Add(PushEffect.HealUnit(pusher, -rebounder.reboundAttackerDamage, false));
            else if (rebounder.reboundAttackerDamage > 0)
                effects.Add(PushEffect.DamageUnit(pusher, rebounder.reboundAttackerDamage, true));
            if (rebounder.reboundAttackerFuelDelta != 0)
                effects.Add(PushEffect.TargetFuel(pusher, rebounder.reboundAttackerFuelDelta));
            if (rebounder.reboundAttackerAmmoDelta != 0)
                effects.Add(PushEffect.TargetAmmo(pusher, rebounder.reboundAttackerAmmoDelta));
        }

        // 蹬鼻子上脸：把发动者反推一格（虚拟判定+产plan）
        if (rebounder.reboundCounterPush && pusher != null && pusher.grid != null)
        {
            var backGrid = gridManager?.GetGridAt(pusher.grid.GridIndex - direction);
            if (backGrid != null)
            {
                var bt = board.Of(backGrid);
                if (bt.Stack.Count == 0 && bt.City == null && bt.Weapons.Count == 0
                    && bt.MultiTileOccupant == null)
                {
                    var pusherStack = GetLivingStack(pusher.grid);
                    if (pusherStack.Count == 0) pusherStack.Add(pusher);
                    VMoveStack(board, pusher.grid, backGrid, pusherStack);
                    movePlans.Add(PushMovePlan.ForStack(pusherStack, backGrid));
                }
            }
        }

        if (reboundDestination != currentGrid)
        {
            VMoveStack(board, currentGrid, reboundDestination, stack);
            virtualPositions[linkIndex] = reboundDestination;
            movePlans.Add(PushMovePlan.ForStack(stack, reboundDestination));
        }
        return true;
    }

    private bool TryPlanTransfer(List<Infantry> stack, int linkIndex, GridManager gridManager,
        GameManager gm, PushVirtualBoard board, List<Grids> virtualPositions,
        List<PushMovePlan> movePlans, List<PushEffect> effects, Infantry preferredTransferer = null,
        Infantry pusher = null)
    {
        var transferUnit = preferredTransferer != null && IsLivingUnit(preferredTransferer)
            ? preferredTransferer
            : stack.FirstOrDefault(u => IsLivingUnit(u) && u.pushTransferEnabled);
        if (transferUnit == null || transferUnit.pushTransferDirection == Vector2I.Zero
            && (transferUnit.transferDirections == null || transferUnit.transferDirections.Count == 0))
            return false;
        int ammoCost = Mathf.Max(0, transferUnit.pushTransferAmmoCost);
        int fuelCost = Mathf.Max(0, transferUnit.pushTransferFuelCost);
        int healthCost = Mathf.Max(0, transferUnit.pushTransferHealthCost);
        int fundsCost = Mathf.Max(0, transferUnit.pushTransferFundsCost);

        // 反重力场弹（独立弹药）
        if (transferUnit.maxAntiGravityAmmo > 0 && !transferUnit.HasAmmo(AmmoKind.AntiGravity, 1))
            return false;

        // 方向集：菜单选定 > Laser式列表 m 选 1 > 原单方向；八向含对角（0-360°归入格步）。
        var dirs = new List<Vector2I>();
        if (transferUnit.pushChosenTransferDirection.HasValue)
            dirs.Add(transferUnit.pushChosenTransferDirection.Value);
        else if (transferUnit.transferDirections != null && transferUnit.transferDirections.Count > 0)
            dirs.AddRange(transferUnit.transferDirections.Where(d => d != Vector2I.Zero));
        else
            dirs.Add(transferUnit.pushTransferDirection);

        var currentGrid = virtualPositions[linkIndex];
        if (currentGrid == null) return false;
        Grids destination = null;
        Vector2I chosenDir = Vector2I.Zero;
        foreach (var raw in dirs)
        {
            if (raw == Vector2I.Zero) continue;
            Vector2I d = Mathf.Abs(raw.X) >= Mathf.Abs(raw.Y)
                ? new Vector2I(Math.Sign(raw.X), 0) : new Vector2I(0, Math.Sign(raw.Y));
            var g = gridManager.GetGridAt(currentGrid.GridIndex + d);
            if (g == null) continue;
            var t = board.Of(g);
            if (t.Stack.Count > 0 || t.City != null || t.Weapons.Count > 0) continue;
            if (transferUnit.GetAmmo(AmmoKind.Primary) < ammoCost || transferUnit.fuel < fuelCost
                || transferUnit.health <= healthCost
                || (fundsCost > 0 && (gm == null || gm.GetTeamFunds(transferUnit.team) < fundsCost)))
                continue;
            if (ammoCost > 0 && !transferUnit.HasAmmo(AmmoKind.Primary, ammoCost)) continue;
            destination = g;
            chosenDir = d;
            break;
        }
        if (destination == null) return false;

        if (ammoCost > 0) effects.Add(PushEffect.Ammo(transferUnit, ammoCost));
        if (fuelCost > 0) effects.Add(PushEffect.FuelCost(transferUnit, fuelCost));
        if (healthCost > 0) effects.Add(PushEffect.HealthCost(transferUnit, healthCost));
        if (fundsCost > 0) effects.Add(PushEffect.Funds(transferUnit.team, fundsCost));
        if (transferUnit.maxAntiGravityAmmo > 0)
            effects.Add(PushEffect.AmmoKindCost(transferUnit, AmmoKind.AntiGravity, 1));

        VMoveStack(board, currentGrid, destination, stack);
        virtualPositions[linkIndex] = destination;
        movePlans.Add(PushMovePlan.ForStack(stack, destination));
        return true;
    }

    // Record post-push target effects (maxHealth/lock/heal/damage/destroy/fuel/ammo)
    // into the delayed queue. The commit phase moves every stack first and flushes
    // effects afterwards, so damage can never land on a tile state from mid-chain.
    private void QueuePushTargetEffects(List<Infantry> stack, List<PushEffect> effects, int distanceScale = 1)
    {
        foreach (var unit in stack.ToList())
        {
            if (!IsLivingUnit(unit)) continue;
            // 卸力"保住"语义：目标开卸力仪式并勾选的保护项，对应增减度被卸掉（原文：保住血量/资金/燃料/弹药）
            bool ceremony = unit.pushMitigationEnabled && unit.pushMitigationCeremony;
            bool protHealth = ceremony && unit.mitigationProtectsHealth;
            bool protAmmo = ceremony && unit.mitigationProtectsAmmo;
            bool protFuel = ceremony && unit.mitigationProtectsFuel;
            if (!protHealth && unit.pushTargetMaxHealthDelta != 0)
                effects.Add(PushEffect.TargetMaxHealth(unit, unit.pushTargetMaxHealthDelta * distanceScale));
            if (!protHealth && pushLockTargetToOneHp) effects.Add(PushEffect.LockOneHp(unit));
            else if (!protHealth && pushTargetHealAmount > 0)
                effects.Add(PushEffect.HealUnit(unit, pushTargetHealAmount * distanceScale, pushTargetCanOverMaxHealth));
            else if (!protHealth && (pushTargetDamage > 0 || pushRamDamage > 0))
                effects.Add(PushEffect.DamageUnit(unit,
                    Math.Max(0L, ((long)pushTargetDamage + pushRamDamage) * distanceScale), pushDamageCanDestroy));
            if (!protFuel && pushTargetFuelDelta != 0)
                effects.Add(PushEffect.TargetFuel(unit, pushTargetFuelDelta * distanceScale));
            if (!protAmmo && pushTargetAmmoDelta != 0)
                effects.Add(PushEffect.TargetAmmo(unit, pushTargetAmmoDelta * distanceScale));
            // 资金保护暂无 per-target 资金效果通道（资金增减只在反弹/转移/推动成本上），Export 先留
        }
    }

    private void ExecuteSeparatePushOnGrid(Grids targetGrid)
    {
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var gridManager = gm?.gridManager;
        if (grid == null || gridManager == null) return;
        // 原文：pushRange=1 时到最大范围自动推动，无需精确点选目标
        if (targetGrid == null || pushAutoPickTargetAtMaxRange)
        {
            var picked = AutoPickPushTarget(targetGrid, gridManager);
            if (picked == null) return;
            targetGrid = picked;
        }
        var stack = GetLivingStack(targetGrid);
        bool staticTarget = stack.Count == 0 && pushTargetClass == PushTargetClass.AnyPushableEntity;
        if (stack.Count > 0 && !CanPushTargetTeam(stack)) return;
        if (staticTarget)
        {
            var weapon = targetGrid.weapons?.FirstOrDefault(w => w != null
                && GodotObject.IsInstanceValid(w) && w.pushableByPhysics && CanPushTeamName(w.team, w.GetType().Name));
            var facility = targetGrid.city != null && targetGrid.city.pushableByPhysics
                && CanPushTeamName(targetGrid.city.facilityTeam, targetGrid.city.GetType().Name) ? targetGrid.city : null;
            if (weapon == null && facility == null) return;
            bool canPushStatic = weapon != null
                ? CanPushNonInfantryTarget(weapon.grid, weapon.pushableByPhysics, weapon.GetEffectiveWeight(),
                    weapon.equalWeightPushMode, weapon.isDestroyed)
                : CanPushNonInfantryTarget(facility.ParentGrid, facility.pushableByPhysics, facility.GetEffectiveWeight(),
                    facility.equalWeightPushMode, facility.health <= 0);
            if (!canPushStatic || !CanPayPushCost(gm) || !TryMarkPushActionUsed()) return;
            gridManager.HideAttackRange();
            gridManager.ClearAllEmptyCallbacks();
            gm.isSelectingAttackTarget = false;
            actionMenu?.Hide();
            if (weapon != null) TryPushWeaponOnAttack(weapon, fromSeparateAction: true);
            else TryPushFacilityOnAttack(facility, fromSeparateAction: true);
            return;
        }

        Vector2I direction = GetPushDirection(targetGrid);
        var pusherStack = GetLivingStack(grid);
        if (pusherStack.Count == 0) pusherStack.Add(this);
        if (direction == Vector2I.Zero || !CanPushStack(pusherStack, stack,
            GetPushForceMultiplier())) return;
        if (!CanPayPushCost(gm)) return;

        if (!TryMarkPushActionUsed()) return;
        gridManager.HideAttackRange();
        gridManager.ClearAllEmptyCallbacks();
        gm.isSelectingAttackTarget = false;
        actionMenu?.Hide();
        ResolvePushChain(targetGrid, direction, pushDistance, gridManager, gm);
    }

    private void ResolvePushChain(Grids firstGrid, Vector2I direction, int distance,
        GridManager gridManager, GameManager gameManager)
    {
        // ===== 统一混合链（原文#1/#2）：一格上的单位+兵器+设施为整体一环 =====
        var chain = new List<PushNode>();
        var cursor = firstGrid;
        while (cursor != null)
        {
            var node = new PushNode { Tile = cursor };
            node.Units = GetLivingStack(cursor);
            var statics = CollectBlockingStatics(cursor);
            node.Weapon = statics.w;
            node.Facility = statics.f;
            if (node.Units.Count == 0 && !node.HasStatics) break;
            // 原文：第n环可改势力/黑名单/种类（1-based，数组越界=用全局配置）
            int depth = chain.Count + 1;
            if (node.Units.Count > 0 && !CanPushTargetTeamAtDepth(node.Units, depth)) break;
            if (node.HasStatics && depth - 1 < pushChainDepthTargetClasses.Count
                && pushChainDepthTargetClasses[depth - 1] == PushTargetClass.InfantryOnly) break;
            if (node.Units.Any(u => IsBlacklistedAtDepth(u, depth))) break;
            chain.Add(node);
            if (!pushChainEnabled || (pushMaxChainStacks > 0 && chain.Count >= pushMaxChainStacks)) break;
            cursor = gridManager.GetGridAt(cursor.GridIndex + direction);
        }
        if (chain.Count == 0) return;

        // Transaction containers exist before the force check so the mitigation
        // ceremony can queue its ammo cost as an effect.
        var board = SnapshotPushBoard(gridManager);
        var destroyed = new HashSet<Infantry>();
        var movePlans = new List<PushMovePlan>();
        var effects = new List<PushEffect>();
        var mitigationAmmoQueued = new HashSet<Infantry>();

        // 力传导：混合重量累计（单位重量经卸力仪式 + 兵器/设施有效重量），原文"推着重物走"。
        List<Infantry> pusherStack = GetLivingStack(grid);
        if (pusherStack.Count == 0) pusherStack.Add(this);
        float accumulated = GetStackWeight(pusherStack);
        float initialForce = accumulated;
        foreach (var node in chain)
        {
            float unitWeight = node.Units.Count > 0
                ? ApplyMitigationCeremony(node.Units, this, gameManager, effects, mitigationAmmoQueued, board, node.Tile)
                : 0f;
            float nodeWeight = unitWeight + node.StaticWeight;
            if (pushUsesWeight && accumulated < nodeWeight && !Mathf.IsEqualApprox(accumulated, nodeWeight))
                return;
            // 原文：开=累计加重推下一环（封顶 pushChainAccumulatedWeightCap）；关=始终用初始推力
            if (pushChainAccumulatesWeight)
            {
                accumulated += nodeWeight;
                if (pushChainAccumulatedWeightCap > 0)
                    accumulated = Mathf.Min(accumulated, pushChainAccumulatedWeightCap);
            }
            else accumulated = initialForce;
        }
        effects.Add(PushEffect.Cost(this, pushCostResource, PushCostAmountScaled(distance)));

        // ===== Virtual-board pre-roll. The whole chain resolves against a data-only
        // snapshot first; real tiles, HP, ammo, fuel and funds stay untouched until
        // every link proves resolvable. Any failure discards plans and effects
        // wholesale — the real board never sees a partial push.

        var originalTiles = chain.Select(n => n.Tile).ToList();
        var virtualPositions = new List<Grids>(originalTiles);
        var movedNodes = new HashSet<int>();

        bool aborted = false;
        for (int step = 0; step < distance && !aborted; step++)
        {
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                var node = chain[i];
                var survivors = node.Units
                    .Where(u => IsLivingUnit(u) && !destroyed.Contains(u)).ToList();
                // 纯静态节点全程可推；带单位的节点用卸力后的距离
                int nodeDistance = survivors.Count > 0
                    ? GetMitigatedPushDistance(survivors, distance)
                    : distance;
                if (step >= nodeDistance) continue;
                bool finalStepForNode = step == nodeDistance - 1;
                var currentGrid = virtualPositions[i];
                if (currentGrid == null) continue;
                var destination = gridManager.GetGridAt(currentGrid.GridIndex + direction);
                if (destination == null)
                    destination = WrapAroundDestination(gridManager, currentGrid, direction);
                if (destination == null)
                {
                    // 非致命撞边：链在此停住；自毁只结算单位（静态体的边界自毁未定义，保持存活）
                    if (pushSelfDestructOnInvalidIntermediateTile)
                        foreach (var unit in survivors.ToList())
                            QueueSimDestroy(unit, currentGrid, destroyed, board, effects);
                    break;
                }

                foreach (var unit in survivors.ToList())
                {
                    bool invalidTerrain = unit.GetMoveCostOnGrid(destination) >= 999;
                    if (invalidTerrain && (pushSelfDestructOnInvalidIntermediateTile || finalStepForNode))
                        QueueSimDestroy(unit, currentGrid, destroyed, board, effects);
                }
                survivors = survivors.Where(u => IsLivingUnit(u) && !destroyed.Contains(u)).ToList();

                var destinationTile = board.Of(destination);
                // 链内下一节点即将离开目的地，则其占用不算阻挡
                bool destIsNextNode = i + 1 < chain.Count && virtualPositions[i + 1] == destination;
                var blockers = destinationTile.Stack
                    .Where(u => !survivors.Contains(u) && !destroyed.Contains(u)
                        && !(destIsNextNode && chain[i + 1].Units.Contains(u))).ToList();
                var destStatics = GetBlockingStaticsFromTile(destinationTile);
                bool destStaticsAreNextNode = destIsNextNode
                    && ((chain[i + 1].Weapon != null && destStatics.w == chain[i + 1].Weapon)
                        || (chain[i + 1].Facility != null && destStatics.f == chain[i + 1].Facility));
                // 目的地静态体：是下一环节→放行；否则按重量挪开（含重叠冲突仪式）
                if (blockers.Count > 0 || HasForeignVirtualMultiTileOccupant(destinationTile, survivors)
                    || (!destStaticsAreNextNode && (destStatics.w != null || destStatics.f != null)
                        && !PlanDisplaceWeaponAndFacility(survivors, destination, direction,
                            gridManager, board, movePlans, gameManager, effects, destroyed,
                            GetStackWeight(survivors) + node.StaticWeight)))
                {
                    aborted = true;
                    break;
                }

                // 节点整体迁移：单位+静态体同去下一格（多格兵器的足印由提交阶段 MoveByPhysicsPush 处理）
                if (survivors.Count > 0)
                {
                    VMoveStack(board, currentGrid, destination, survivors);
                    movePlans.Add(PushMovePlan.ForStack(survivors, destination));
                }
                if (node.Weapon != null)
                {
                    foreach (var t in board.Tiles) t.Value.Weapons.Remove(node.Weapon);
                    board.Of(destination).Weapons.Add(node.Weapon);
                    movePlans.Add(PushMovePlan.ForWeapon(node.Weapon, destination));
                }
                if (node.Facility != null)
                {
                    board.Of(currentGrid).City = null;
                    board.Of(destination).City = node.Facility;
                    movePlans.Add(PushMovePlan.ForFacility(node.Facility, destination));
                }
                virtualPositions[i] = destination;
                movedNodes.Add(i);
            }
        }

        if (aborted)
        {
            GD.Print("[Push] 链环失败——整链放弃，未提交任何移动与效果。");
            return;
        }

        // 反弹/转移/目标效果只作用于单位成员（原文：反弹/转移是被推动单位的模块）
        foreach (int i in movedNodes.OrderByDescending(index => index))
        {
            var survivors = chain[i].Units
                .Where(u => IsLivingUnit(u) && !destroyed.Contains(u)).ToList();
            if (survivors.Count == 0) continue;
            if (TryPlanStackDisplacement(survivors, i, originalTiles[i], direction, board, gridManager,
                    gameManager, virtualPositions, movePlans, effects, this)) continue;
            // 原文#13：目标增减度按被推格数放大
            int movedTiles = virtualPositions[i] == null || originalTiles[i] == null ? 1
                : Mathf.Abs(virtualPositions[i].GridIndex.X - originalTiles[i].GridIndex.X)
                + Mathf.Abs(virtualPositions[i].GridIndex.Y - originalTiles[i].GridIndex.Y);
            QueuePushTargetEffects(survivors, effects,
                pushTargetEffectsScaleWithDistance ? Mathf.Max(1, movedTiles) : 1);
        }

        if (pushPusherFollowsChain && firstGrid != null && grid != null)
        {
            var followTile = board.Of(firstGrid);
            bool canOverlapStatic = (followTile.City == null || !followTile.City.pushBlocksInfantryWhenOverlapped)
                && followTile.Weapons.All(w => w == null || !w.pushBlocksInfantryWhenOverlapped);
            if (canOverlapStatic && followTile.Stack.Count == 0)
            {
                var followingStack = GetLivingStack(grid);
                if (followingStack.Count == 0) followingStack.Add(this);
                VMoveStack(board, grid, firstGrid, followingStack);
                movePlans.Add(PushMovePlan.ForStack(followingStack, firstGrid));
            }
        }

        CommitPushTransaction(movePlans, effects, gameManager);
        gameManager?.fogOfWarManager?.OnUnitMoved();
        gameManager?.CallDeferred(nameof(GameManager.UpdateUnitLists));
        gameManager?.CallDeferred(nameof(GameManager.CheckVictoryCondition));
    }

    // 原文#13：OnMove 触发时消耗按移动格数放大（TryPushOnMove 先设置 pushCostDistanceScale）
    private int? pushCostDistanceScale = null;
    private int PushCostAmountScaled(int distance)
    {
        int baseCost = Mathf.Max(0, pushCostAmount);
        if (pushMoveScalesWithDistance && pushCostDistanceScale.HasValue)
        {
            int scaled = baseCost * Mathf.Max(1, pushCostDistanceScale.Value);
            pushCostDistanceScale = null;
            return scaled;
        }
        return baseCost;
    }

    // Plan-only displacement of blocking statics. Validates exactly like the old
    // direct-commit version but mutates ONLY the virtual board and appends move
    // plans; the real weapon/facility moves happen in the commit phase.
    private bool PlanDisplaceWeaponAndFacility(List<Infantry> movingStack,
        Grids destination, Vector2I direction, GridManager gridManager,
        PushVirtualBoard board, List<PushMovePlan> movePlans,
        GameManager gameManager, List<PushEffect> effects, HashSet<Infantry> destroyed,
        float? movingWeightOverride = null)
    {
        float movingWeight = movingWeightOverride ?? GetStackWeight(movingStack);
        var destinationTile = board.Of(destination);
        var nextGrid = gridManager.GetGridAt(destination.GridIndex + direction);
        var blockingWeapons = destinationTile.Weapons
            .Where(w => w != null && GodotObject.IsInstanceValid(w) && !w.IsQueuedForDeletion()
                && w.pushBlocksInfantryWhenOverlapped).ToList();
        var blockingFacility = destinationTile.City != null && destinationTile.City.pushBlocksInfantryWhenOverlapped
            ? destinationTile.City : null;

        // A tile can hold one facility and one weapon. They share the destination
        // tile but occupy independent engine slots, so resolve both as one obstacle set.
        // Multiple weapons cannot be rebound into one slot by WeaponManager; reject that
        // configuration before moving anything rather than corrupting occupancy.
        if (blockingWeapons.Count > 1) return false;
        if (blockingWeapons.Count == 0 && blockingFacility == null) return true;
        if (nextGrid == null) return false;
        var nextTile = board.Of(nextGrid);
        bool onlyUnitsOccupying = nextTile.Stack.Count > 0 && nextTile.MultiTileOccupant == null
            && nextTile.City == null && nextTile.Weapons.Count == 0;
        if (!onlyUnitsOccupying && !CanMoveStaticTargetVirtually(board, nextGrid)) return false;

        Weapon blockingWeapon = blockingWeapons.FirstOrDefault();
        if (blockingWeapon != null)
        {
            float obstacleWeight = blockingWeapon.GetEffectiveWeight();
            if (!blockingWeapon.pushableByPhysics) return false;
            if (pushUsesWeight && movingWeight < obstacleWeight
                && !Mathf.IsEqualApprox(movingWeight, obstacleWeight)) return false;
            if (pushUsesWeight && Mathf.IsEqualApprox(movingWeight, obstacleWeight)
                && !CanResolveEqualWeight(movingStack, blockingWeapon.equalWeightPushMode)) return false;
        }

        if (blockingFacility != null)
        {
            float obstacleWeight = blockingFacility.GetEffectiveWeight();
            if (!blockingFacility.pushableByPhysics) return false;
            if (pushUsesWeight && movingWeight < obstacleWeight
                && !Mathf.IsEqualApprox(movingWeight, obstacleWeight)) return false;
            if (pushUsesWeight && Mathf.IsEqualApprox(movingWeight, obstacleWeight)
                && !CanResolveEqualWeight(movingStack, blockingFacility.equalWeightPushMode)) return false;
        }

        // 落到有单位的格子：执行重叠冲突仪式（平移覆盖/冲突由单位Export决定）。
        // 仪式产销毁/脆弱效果，允许共存时单位与静态体同格。
        if (nextTile.Stack.Count > 0)
        {
            float staticWeight = blockingWeapon != null ? blockingWeapon.GetEffectiveWeight()
                : blockingFacility != null ? blockingFacility.GetEffectiveWeight() : 0f;
            if (!PlanOverlapConflict(nextTile, staticWeight, true, null, blockingWeapon, blockingFacility,
                    gameManager, effects, destroyed))
                return false;
            nextTile.Stack.RemoveAll(u => destroyed.Contains(u));
        }

        // 原文#4：兵器/设施作为被推动方也可反弹（重量够才弹得动）
        if (blockingWeapon != null && blockingWeapon.pushReboundEnabled
            && blockingWeapon.GetEffectiveWeight() >= movingWeight)
            return PlanStaticRebound(null, blockingWeapon, movingStack, destination,
                direction, gridManager, board, movePlans);
        if (blockingFacility != null && blockingFacility.pushReboundEnabled
            && blockingFacility.GetEffectiveWeight() >= movingWeight)
            return PlanStaticRebound(blockingFacility, null, movingStack, destination,
                direction, gridManager, board, movePlans);

        // All weight and destination checks passed. Record plans and update virtual
        // occupancy only; the commit phase replays them as real moves.
        if (blockingWeapon != null)
        {
            foreach (var tile in board.Tiles) tile.Value.Weapons.Remove(blockingWeapon);
            board.Of(nextGrid).Weapons.Add(blockingWeapon);
            movePlans.Add(PushMovePlan.ForWeapon(blockingWeapon, nextGrid));
        }
        if (blockingFacility != null)
        {
            destinationTile.City = null;
            board.Of(nextGrid).City = blockingFacility;
            movePlans.Add(PushMovePlan.ForFacility(blockingFacility, nextGrid));
        }
        return true;
    }

    private static bool CanResolveEqualWeight(List<Infantry> movingStack,
        EqualWeightPushMode obstacleMode)
    {
        bool stackAllows = StackAllowsSimultaneous(movingStack);
        bool stackStops = StackStopsOnEqual(movingStack);
        if (stackAllows && obstacleMode == EqualWeightPushMode.AllowSimultaneous) return true;
        if (stackStops && obstacleMode == EqualWeightPushMode.Stop) return false;
        return GD.Randf() < 0.5f;
    }

    private static bool IsLivingUnit(Infantry unit) =>
        unit != null && GodotObject.IsInstanceValid(unit) && !unit.IsQueuedForDeletion()
        && unit.health > 0 && unit.grid != null;

    // Multi-tile infantry occupy footprint tiles through multiTileOccupant rather than
    // appearing in every tile's infantries list. Do not push a stack into such a tile.
    private static bool HasForeignMultiTileOccupant(Grids tile, List<Infantry> movingStack)
    {
        var occupant = tile?.multiTileOccupant;
        return IsLivingUnit(occupant) && (movingStack == null || !movingStack.Contains(occupant));
    }

    private static List<Infantry> GetLivingStack(Grids tile)
    {
        if (tile?.infantries == null) return new List<Infantry>();
        tile.infantries.RemoveAll(u => u == null || !GodotObject.IsInstanceValid(u)
            || u.IsQueuedForDeletion() || u.health <= 0 || u.grid != tile);
        var living = tile.infantries.Distinct().Where(IsLivingUnit).ToList();
        // Keep the legacy single-unit pointer consistent with the authoritative stack list.
        if (tile.infantry == null || !GodotObject.IsInstanceValid(tile.infantry)
            || tile.infantry.IsQueuedForDeletion() || tile.infantry.health <= 0
            || tile.infantry.grid != tile || !living.Contains(tile.infantry))
            tile.infantry = living.FirstOrDefault();
        return living;
    }

    private static float GetStackWeight(List<Infantry> stack) =>
        stack.Where(IsLivingUnit).Sum(u => u.GetEffectiveWeight());

    private static bool StackAllowsSimultaneous(List<Infantry> stack) =>
        stack.Count > 0 && stack.All(u => u.equalWeightPushMode == EqualWeightPushMode.AllowSimultaneous);

    private static bool StackStopsOnEqual(List<Infantry> stack) =>
        stack.Count > 0 && stack.All(u => u.equalWeightPushMode == EqualWeightPushMode.Stop);

    private bool CanPushStack(List<Infantry> pusher, List<Infantry> target, float forceMultiplier = 1.0f,
        float? targetWeightOverride = null)
    {
        if (!pushUsesWeight) return true;
        float pusherWeight = GetStackWeight(pusher) * forceMultiplier;
        float targetWeight = targetWeightOverride ?? GetStackWeight(target);
        if (pusherWeight > targetWeight && !Mathf.IsEqualApprox(pusherWeight, targetWeight))
            return true;
        if (!Mathf.IsEqualApprox(pusherWeight, targetWeight))
            return false;

        bool pusherAllows = StackAllowsSimultaneous(pusher);
        bool targetAllows = StackAllowsSimultaneous(target);
        bool pusherStops = StackStopsOnEqual(pusher);
        bool targetStops = StackStopsOnEqual(target);
        if (pusherAllows && targetAllows) return true;
        if (pusherStops && targetStops) return false;
        return GD.Randf() < 0.5f;
    }

    private static void MoveStackByPush(List<Infantry> stack, Grids destination)
    {
        var moving = stack.Where(IsLivingUnit).ToList();
        if (moving.Count == 0 || destination == null) return;
        foreach (var unit in moving)
        {
            var origin = unit.grid;
            if (origin != null)
            {
                origin.infantries.Remove(unit);
                if (origin.infantry == unit)
                    origin.infantry = origin.infantries.FirstOrDefault(IsLivingUnit);
            }
        }
        foreach (var unit in moving)
        {
            unit.grid = destination;
            if (!destination.infantries.Contains(unit)) destination.infantries.Add(unit);
            if (destination.infantry == null || !GodotObject.IsInstanceValid(destination.infantry)
                || destination.infantry.IsQueuedForDeletion() || destination.infantry.health <= 0
                || destination.infantry.grid != destination)
                destination.infantry = unit;
            unit.Position = destination.Position;
        }
    }

    private static void DestroyPushedUnit(Infantry unit, GameManager gameManager)
    {
        if (!IsLivingUnit(unit)) return;
        var origin = unit.grid;
        // Detach from occupancy before destruction callbacks, which may clear grid themselves.
        if (origin != null)
        {
            origin.infantries.Remove(unit);
            if (origin.infantry == unit)
                origin.infantry = origin.infantries.FirstOrDefault(IsLivingUnit);
            unit.grid = null;
        }
        unit.health = 0;
        unit.UpdateHpLabel();
        unit.OnDestroyed();
        unit.ClearFootprint();
        unit.CallDeferred(Node.MethodName.QueueFree);
        gameManager?.RemovePiece(unit);
    }

    // 原文第n环势力规则（按 main 实际枚举成员：EnemiesOnly/FriendliesOnly/AlliesOnly/Everyone/Custom）
    private bool CanPushTargetTeamAtDepth(List<Infantry> targetStack, int depth)
    {
        if (depth - 1 < pushChainDepthTeamModes.Count)
        {
            var mode = pushChainDepthTeamModes[depth - 1];
            string targetTeam = targetStack.FirstOrDefault(u => IsLivingUnit(u))?.team;
            return mode switch
            {
                PushTargetTeamMode.Everyone => true,
                PushTargetTeamMode.EnemiesOnly => !string.IsNullOrEmpty(targetTeam) && targetTeam != team,
                PushTargetTeamMode.FriendliesOnly => !string.IsNullOrEmpty(targetTeam) && targetTeam == team,
                PushTargetTeamMode.AlliesOnly => CanPushTargetTeam(targetStack),
                PushTargetTeamMode.Custom => CanPushTargetTeam(targetStack),
                _ => CanPushTargetTeam(targetStack),
            };
        }
        return CanPushTargetTeam(targetStack);
    }

    private bool IsBlacklistedAtDepth(Infantry unit, int depth)
    {
        if (depth - 1 >= pushChainDepthBlacklists.Count) return false;
        var list = pushChainDepthBlacklists[depth - 1];
        if (string.IsNullOrEmpty(list)) return false;
        var id = unit.unitTypeId;
        return !string.IsNullOrEmpty(id) && list.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Any(x => x.Trim() == id);
    }

    private Grids AutoPickPushTarget(Grids preferred, GridManager gridManager)
    {
        Vector2I? dir = null;
        if (preferred != null && grid != null)
        {
            var d = preferred.GridIndex - grid.GridIndex;
            if (d != Vector2I.Zero) dir = d;
        }
        var dirs = dir.HasValue ? new List<Vector2I> { dir.Value }
            : new List<Vector2I> { new(1,0), new(-1,0), new(0,1), new(0,-1), new(1,1), new(1,-1), new(-1,1), new(-1,-1) };
        for (int n = Mathf.Max(1, pushRange); n >= 1; n--)
        {
            foreach (var d in dirs)
            {
                var t = gridManager.GetGridAt(grid.GridIndex + d * n);
                if (t == null || t == grid) continue;
                if (IsCompliantPushTarget(t)) return t;
            }
        }
        return null;
    }

    private bool IsCompliantPushTarget(Grids tile)
    {
        if (tile == null) return false;
        var stack = GetLivingStack(tile);
        if (stack.Count > 0) return CanPushTargetTeam(stack);
        if (pushTargetClass != PushTargetClass.AnyPushableEntity) return false;
        return (tile.weapons?.Any(w => w != null && GodotObject.IsInstanceValid(w)
                && w.pushableByPhysics) ?? false)
            || (tile.city != null && tile.city.pushableByPhysics);
    }

    private static float GetStaticMitigatedWeight(float baseWeight, bool mitigationEnabled,
        bool ceremony, bool weakensWeight, MitigationValueMode mode, float value)
    {
        if (!mitigationEnabled || !ceremony || !weakensWeight || value <= 0) return baseWeight;
        float cut = mode == MitigationValueMode.Percent
            ? baseWeight * value / 100f : value;
        return Mathf.Max(0f, baseWeight - cut);
    }

    private static void QueueStaticFragile(Weapon weapon, Facility facility, List<PushEffect> effects)
    {
        if (weapon != null)
        {
            int dmg = weapon.fragileDamageMode == FragileDamageMode.LockOneHp
                ? weapon.health - 1 : Mathf.Abs(weapon.fragileDamageAmount);
            if (dmg > 0) effects.Add(PushEffect.DamageWeapon(weapon, dmg));
            return;
        }
        if (facility != null && facility.fragile)
        {
            int dmg = facility.fragileDamageMode == FragileDamageMode.LockOneHp
                ? facility.health - 1 : Mathf.Abs(facility.fragileDamageAmount);
            if (dmg > 0) effects.Add(PushEffect.DamageFacility(facility, dmg));
        }
    }

    private bool PlanStaticRebound(Facility facility, Weapon weapon, List<Infantry> movingStack,
        Grids destination, Vector2I direction, GridManager gridManager,
        PushVirtualBoard board, List<PushMovePlan> movePlans)
    {
        var mode = weapon != null ? weapon.pushReboundMode : facility.pushReboundMode;
        if (mode == Weapon.StaticReboundMode.Stay) return false;
        var backGrid = gridManager.GetGridAt(destination.GridIndex - direction * 2);
        if (backGrid == null) return false;
        var backTile = board.Of(backGrid);
        if (backTile.Stack.Count > 0 || backTile.City != null
            || backTile.Weapons.Count > 0 || backTile.MultiTileOccupant != null)
            return false;
        var fromTile = gridManager.GetGridAt(destination.GridIndex - direction);
        VMoveStack(board, fromTile, backGrid, movingStack);
        movePlans.Add(PushMovePlan.ForStack(movingStack, backGrid));
        return false;
    }

    // 冷却读真实状态 pushCooldownTurnsRemaining（Push.cs 原版系统字段）
    public bool IsPushOnCooldown() => pushCooldownTurnsRemaining > 0;
    public void CountdownPushCooldowns()
    {
        if (pushCooldownTurnsRemaining > 0) pushCooldownTurnsRemaining--;
    }

}
