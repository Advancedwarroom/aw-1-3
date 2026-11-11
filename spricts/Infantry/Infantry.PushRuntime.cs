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
            || target.grid == null) return;

        Vector2I direction = GetPushDirection(target.grid);

        if (direction == Vector2I.Zero) return;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null) return;
        var initialStack = GetLivingStack(target.grid);
        if (!CanPushTargetTeam(initialStack)
            || !CanPushStack(new List<Infantry> { this }, initialStack, GetPushForceMultiplier())
            || !CanPayPushCost(gm)) return;
        PayPushCost(gm);
        ResolvePushChain(target.grid, direction, pushDistance, gm.gridManager, gm);
    }

    public void TryPushWeaponOnAttack(Weapon target)
    {
        if (pushTriggerMode == PushTriggerMode.Attack && pushTargetClass == PushTargetClass.AnyPushableEntity
            && target != null && CanPushTeamName(target.team, target.GetType().Name)
            && CanPushNonInfantryTarget(target.grid, target.pushableByPhysics, target.GetEffectiveWeight(),
                target.equalWeightPushMode, target.isDestroyed))
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm?.gridManager == null || target.isMultiTile) return;
            Vector2I direction = GetPushDirection(target.grid);
            if (direction == Vector2I.Zero || !CanPayPushCost(gm)) return;
            var firstDestination = gm.gridManager.GetGridAt(target.grid.GridIndex + direction);
            if (!CanMoveStaticTargetTo(firstDestination)) return;

            PayPushCost(gm);
            for (int step = 0; step < pushDistance; step++)
            {
                var next = gm.gridManager.GetGridAt(target.grid.GridIndex + direction);
                if (!CanMoveStaticTargetTo(next) || !target.MoveByPhysicsPush(next, gm.weaponManager)) break;
            }
            if (pushLockTargetToOneHp) target.health = 1;
            else if (pushTargetDamage > 0) target.TakeDamage(pushTargetDamage, this);
        }
    }

    public void TryPushFacilityOnAttack(Facility target)
    {
        if (pushTriggerMode != PushTriggerMode.Attack || pushTargetClass != PushTargetClass.AnyPushableEntity
            || target == null || !CanPushTeamName(target.facilityTeam, target.GetType().Name)
            || !CanPushNonInfantryTarget(target.ParentGrid, target.pushableByPhysics, target.GetEffectiveWeight(),
                target.equalWeightPushMode, target.health <= 0))
            return;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return;
        Vector2I direction = GetPushDirection(target.ParentGrid);
        if (direction == Vector2I.Zero || !CanPayPushCost(gm)) return;
        var firstDestination = gm.gridManager.GetGridAt(target.ParentGrid.GridIndex + direction);
        if (!CanMoveStaticTargetTo(firstDestination)) return;

        PayPushCost(gm);
        for (int step = 0; step < pushDistance; step++)
        {
            var next = gm.gridManager.GetGridAt(target.ParentGrid.GridIndex + direction);
            if (!CanMoveStaticTargetTo(next) || !target.MoveByPhysicsPush(next)) break;
        }
        if (pushLockTargetToOneHp) target.health = 1;
        else if (pushTargetDamage > 0) target.TakeDamage(pushTargetDamage, this);
    }

    private bool CanMoveStaticTargetTo(Grids destination)
    {
        return destination != null && destination.city == null
            && (destination.weapons == null || destination.weapons.Count == 0)
            && GetLivingStack(destination).Count == 0;
    }

    private bool CanPushNonInfantryTarget(Grids targetGrid, bool? pushable, float? targetWeight,
        EqualWeightPushMode targetMode, bool invalid)
    {
        if (!pushEnabled || (pushTriggerMode != PushTriggerMode.Attack
            && pushTriggerMode != PushTriggerMode.SeparateAction) || pushDistance <= 0
            || targetGrid == null || invalid || pushable != true || targetWeight == null)
            return false;
        float ownWeight = GetEffectiveWeight() * GetPushForceMultiplier();
        if (ownWeight > targetWeight.Value && !Mathf.IsEqualApprox(ownWeight, targetWeight.Value))
            return true;
        if (!Mathf.IsEqualApprox(ownWeight, targetWeight.Value)) return false;
        if (equalWeightPushMode == EqualWeightPushMode.AllowSimultaneous
            && targetMode == EqualWeightPushMode.AllowSimultaneous) return true;
        if (equalWeightPushMode == EqualWeightPushMode.Stop && targetMode == EqualWeightPushMode.Stop)
            return false;
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
        int maxRange = Mathf.Max(minRange, pushMaxTargetRange);
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
            if (unit.team == team) return pushCanTargetFriendlies;
            if (TeamHelper.AreAllied(team, unit.team)) return pushCanTargetAllies;
            return pushCanTargetEnemies;
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
        if (targetTeam == team) return pushCanTargetFriendlies;
        if (TeamHelper.AreAllied(team, targetTeam)) return pushCanTargetAllies;
        return pushCanTargetEnemies;
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
        }
    }

    private int GetMitigatedPushDistance(List<Infantry> stack, int requestedDistance)
    {
        int tiles = stack.Where(u => u.pushMitigationEnabled)
            .Select(u => Mathf.Max(0, u.pushMitigationTiles)).DefaultIfEmpty(0).Max();
        float percent = stack.Where(u => u.pushMitigationEnabled)
            .Select(u => Mathf.Clamp(u.pushMitigationPercent, 0f, 100f)).DefaultIfEmpty(0f).Max();
        int remaining = Mathf.Max(0, requestedDistance - tiles);
        return Mathf.Clamp(Mathf.FloorToInt(remaining * (1f - percent / 100f)), 0, requestedDistance);
    }

    private bool TryReboundStack(List<Infantry> stack, Grids origin, Vector2I direction, GameManager gm)
    {
        var rebounder = stack.FirstOrDefault(u => IsLivingUnit(u) && u.pushReboundEnabled);
        if (rebounder == null || origin == null || GetLivingStack(origin).Count > 0)
            return false;
        if (rebounder.GetAmmo(AmmoKind.Primary) < rebounder.pushReboundAmmoCost
            || rebounder.fuel < rebounder.pushReboundFuelCost
            || (rebounder.pushReboundFundsCost > 0 && (gm == null
                || gm.GetTeamFunds(rebounder.team) < rebounder.pushReboundFundsCost)))
            return false;
        if (rebounder.pushReboundAmmoCost > 0)
            rebounder.TryConsumeAmmo(AmmoKind.Primary, rebounder.pushReboundAmmoCost);
        rebounder.fuel = Mathf.Max(0, rebounder.fuel - rebounder.pushReboundFuelCost);
        if (rebounder.pushReboundFundsCost > 0 && gm != null)
            gm.SetTeamFunds(rebounder.team, gm.GetTeamFunds(rebounder.team) - rebounder.pushReboundFundsCost);
        var reboundDestination = rebounder.pushReboundToOrigin
            ? origin
            : gm?.gridManager?.GetGridAt(origin.GridIndex - direction);
        if (reboundDestination == null || GetLivingStack(reboundDestination).Count > 0
            || reboundDestination.city != null || reboundDestination.weapon != null)
            return false;
        MoveStackByPush(stack, reboundDestination);
        return true;
    }

    private bool TryTransferStack(List<Infantry> stack, Vector2I pushDirection,
        GridManager gridManager, GameManager gm)
    {
        var transferUnit = stack.FirstOrDefault(u => IsLivingUnit(u) && u.pushTransferEnabled);
        if (transferUnit == null || transferUnit.pushTransferDirection == Vector2I.Zero)
            return false;
        int ammoCost = Mathf.Max(0, transferUnit.pushTransferAmmoCost);
        int fuelCost = Mathf.Max(0, transferUnit.pushTransferFuelCost);
        int healthCost = Mathf.Max(0, transferUnit.pushTransferHealthCost);
        int fundsCost = Mathf.Max(0, transferUnit.pushTransferFundsCost);
        if (transferUnit.GetAmmo(AmmoKind.Primary) < ammoCost || transferUnit.fuel < fuelCost
            || transferUnit.health <= healthCost || (gm != null && gm.GetTeamFunds(transferUnit.team) < fundsCost))
            return false;

        Vector2I direction = transferUnit.pushTransferDirection;
        direction = Mathf.Abs(direction.X) >= Mathf.Abs(direction.Y)
            ? new Vector2I(Math.Sign(direction.X), 0)
            : new Vector2I(0, Math.Sign(direction.Y));
        var origin = stack[0].grid;
        var destination = origin == null ? null : gridManager.GetGridAt(origin.GridIndex + direction);
        if (destination == null || GetLivingStack(destination).Count > 0
            || destination.city != null || destination.weapon != null)
            return false;

        if (ammoCost > 0) transferUnit.TryConsumeAmmo(AmmoKind.Primary, ammoCost);
        transferUnit.fuel -= fuelCost;
        transferUnit.health -= healthCost;
        if (fundsCost > 0 && gm != null)
            gm.SetTeamFunds(transferUnit.team, gm.GetTeamFunds(transferUnit.team) - fundsCost);
        MoveStackByPush(stack, destination);
        return true;
    }

    private void ApplyPushTargetEffects(List<Infantry> stack, GameManager gm)
    {
        foreach (var unit in stack.ToList())
        {
            if (!IsLivingUnit(unit)) continue;
            if (unit.pushTargetMaxHealthDelta != 0)
                unit.maxHealth = Mathf.Max(1, unit.maxHealth + unit.pushTargetMaxHealthDelta);
            if (pushLockTargetToOneHp) unit.health = 1;
            else if (pushTargetHealAmount > 0)
                unit.health = Mathf.Min(unit.pushTargetCanOverMaxHealth ? int.MaxValue : unit.GetEffectiveMaxHealth(),
                    unit.health + pushTargetHealAmount);
            else if (pushTargetDamage > 0)
            {
                unit.health -= pushTargetDamage;
                if (unit.health <= 0 && pushDamageCanDestroy)
                {
                    DestroyPushedUnit(unit, gm);
                    continue;
                }
                unit.health = Mathf.Max(1, unit.health);
            }
            if (pushTargetFuelDelta != 0)
                unit.fuel = Mathf.Max(0, unit.fuel + pushTargetFuelDelta);
            if (pushTargetAmmoDelta != 0)
                unit.AddAmmo(AmmoKind.Primary, pushTargetAmmoDelta);
            unit.UpdateHpLabel();
        }
    }

    private void ExecuteSeparatePushOnGrid(Grids targetGrid)
    {
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var gridManager = gm?.gridManager;
        if (targetGrid == null || grid == null || gridManager == null) return;
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
            if (weapon != null) TryPushWeaponOnAttack(weapon);
            else TryPushFacilityOnAttack(facility);
            return;
        }

        Vector2I direction = GetPushDirection(targetGrid);
        if (direction == Vector2I.Zero || !CanPushStack(new List<Infantry> { this }, stack,
            GetPushForceMultiplier())) return;
        if (!CanPayPushCost(gm)) return;

        if (!TryMarkPushActionUsed()) return;
        PayPushCost(gm);
        gridManager.HideAttackRange();
        gridManager.ClearAllEmptyCallbacks();
        gm.isSelectingAttackTarget = false;
        actionMenu?.Hide();
        ResolvePushChain(targetGrid, direction, pushDistance, gridManager, gm);
    }

    private void ResolvePushChain(Grids firstGrid, Vector2I direction, int distance,
        GridManager gridManager, GameManager gameManager)
    {
        // A stack is all living infantry occupying one tile. Its effective mass is the
        // sum of each member's health-adjusted weight.
        var chain = new List<List<Infantry>>();
        var cursor = firstGrid;
        while (cursor != null)
        {
            var stack = GetLivingStack(cursor);
            if (stack.Count == 0 || !CanPushTargetTeam(stack)) break;
            chain.Add(stack);
            if (pushMaxChainStacks > 0 && chain.Count >= pushMaxChainStacks) break;
            cursor = gridManager.GetGridAt(cursor.GridIndex + direction);
        }
        if (chain.Count == 0) return;

        // Resolve force transmission from the attacker through each stack once.
        // When a link fails, stacks farther forward can still move; stacks behind
        // the failed link cannot pass through it.
        int firstBlockedLink = -1;
        List<Infantry> pusherStack = new List<Infantry> { this };
        for (int i = 0; i < chain.Count; i++)
        {
            if (!CanPushStack(pusherStack, chain[i], GetPushForceMultiplier()))
            {
                firstBlockedLink = i;
                break;
            }
            pusherStack = chain[i];
        }

        // Keep each stack's original tile so rebound is resolved once, after the
        // complete displacement. Target effects must also run once per push action,
        // not once for every tile traversed.
        var originalTiles = chain.Select(stack => stack.FirstOrDefault(IsLivingUnit)?.grid).ToList();
        var movedStacks = new HashSet<int>();

        for (int step = 0; step < distance; step++)
        {
            for (int i = chain.Count - 1; i > firstBlockedLink; i--)
            {
                var stack = chain[i].Where(IsLivingUnit).ToList();
                if (stack.Count == 0 || step >= GetMitigatedPushDistance(stack, distance)) continue;
                var currentGrid = stack[0].grid;
                if (currentGrid == null) continue;
                var destination = gridManager.GetGridAt(currentGrid.GridIndex + direction);
                if (destination == null)
                {
                    foreach (var unit in stack) DestroyPushedUnit(unit, gameManager);
                    continue;
                }

                foreach (var unit in stack.ToList())
                {
                    bool invalidTerrain = unit.GetMoveCostOnGrid(destination) >= 999;
                    if (invalidTerrain && (unit.pushSelfDestructOnInvalidIntermediateTile || step == distance - 1))
                        DestroyPushedUnit(unit, gameManager);
                }
                stack = stack.Where(IsLivingUnit).ToList();
                if (stack.Count == 0) continue;

                // A final-tile-only unit may pass over invalid intermediate terrain,
                // but it must never be left standing on an invalid final tile.
                if (step < distance - 1 && stack.Any(u => !u.pushSelfDestructOnInvalidIntermediateTile
                    && u.GetMoveCostOnGrid(destination) >= 999))
                    continue;

                var blockers = GetLivingStack(destination).Where(u => !stack.Contains(u)).ToList();
                if (blockers.Count > 0) continue;
                if (!TryDisplaceWeaponAndFacility(stack, destination, direction, gridManager)) continue;

                MoveStackByPush(stack, destination);
                movedStacks.Add(i);
            }
        }

        // Resolve per-stack post-push modules once, after all chain movement is complete.
        foreach (int i in movedStacks.OrderByDescending(index => index))
        {
            var stack = chain[i].Where(IsLivingUnit).ToList();
            if (stack.Count == 0) continue;
            var origin = originalTiles[i];
            if (TryReboundStack(stack, origin, direction, gameManager)) continue;
            if (TryTransferStack(stack, direction, gridManager, gameManager)) continue;
            ApplyPushTargetEffects(stack, gameManager);
        }

        if (pushPusherFollowsChain && firstGrid != null && grid != null)
        {
            var followDestination = firstGrid;
            bool canOverlapStatic = (followDestination.city == null || !followDestination.city.pushBlocksInfantryWhenOverlapped)
                && (followDestination.weapons == null || followDestination.weapons.All(w =>
                    w == null || !GodotObject.IsInstanceValid(w) || !w.pushBlocksInfantryWhenOverlapped));
            if (canOverlapStatic && GetLivingStack(followDestination).Count == 0)
                MoveStackByPush(new List<Infantry> { this }, followDestination);
        }

        gameManager?.fogOfWarManager?.OnUnitMoved();
        gameManager?.CallDeferred(nameof(GameManager.UpdateUnitLists));
        gameManager?.CallDeferred(nameof(GameManager.CheckVictoryCondition));
    }

    private bool TryDisplaceWeaponAndFacility(List<Infantry> movingStack,
        Grids destination, Vector2I direction, GridManager gridManager)
    {
        float movingWeight = GetStackWeight(movingStack);
        var nextGrid = gridManager.GetGridAt(destination.GridIndex + direction);
        var blockingWeapons = (destination.weapons ?? new List<Weapon>())
            .Where(w => w != null && GodotObject.IsInstanceValid(w) && !w.IsQueuedForDeletion()
                && w.pushBlocksInfantryWhenOverlapped).ToList();
        var blockingFacility = destination.city != null && destination.city.pushBlocksInfantryWhenOverlapped
            ? destination.city : null;

        // Avoid partial relocation: preflight every blocking obstacle before moving any.
        if (blockingWeapons.Count + (blockingFacility == null ? 0 : 1) > 1)
            return false;
        if (blockingWeapons.Count == 0 && blockingFacility == null) return true;
        if (nextGrid == null || !CanMoveStaticTargetTo(nextGrid)) return false;

        if (blockingWeapons.Count == 1)
        {
            var weapon = blockingWeapons[0];
            float obstacleWeight = weapon.GetEffectiveWeight();
            if (!weapon.pushableByPhysics || (movingWeight < obstacleWeight
                && !Mathf.IsEqualApprox(movingWeight, obstacleWeight))) return false;
            if (Mathf.IsEqualApprox(movingWeight, obstacleWeight)
                && !CanResolveEqualWeight(movingStack, weapon.equalWeightPushMode)) return false;
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            return weapon.MoveByPhysicsPush(nextGrid, gm?.weaponManager);
        }

        if (blockingFacility != null)
        {
            float obstacleWeight = blockingFacility.GetEffectiveWeight();
            if (!blockingFacility.pushableByPhysics || (movingWeight < obstacleWeight
                && !Mathf.IsEqualApprox(movingWeight, obstacleWeight))) return false;
            if (Mathf.IsEqualApprox(movingWeight, obstacleWeight)
                && !CanResolveEqualWeight(movingStack, blockingFacility.equalWeightPushMode)) return false;
            return blockingFacility.MoveByPhysicsPush(nextGrid);
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

    private static List<Infantry> GetLivingStack(Grids tile)
    {
        if (tile?.infantries == null) return new List<Infantry>();
        tile.infantries.RemoveAll(u => u == null || !GodotObject.IsInstanceValid(u)
            || u.IsQueuedForDeletion() || u.health <= 0);
        return tile.infantries.Distinct().Where(IsLivingUnit).ToList();
    }

    private static float GetStackWeight(List<Infantry> stack) =>
        stack.Where(IsLivingUnit).Sum(u => u.GetEffectiveWeight());

    private static bool StackAllowsSimultaneous(List<Infantry> stack) =>
        stack.Count > 0 && stack.All(u => u.equalWeightPushMode == EqualWeightPushMode.AllowSimultaneous);

    private static bool StackStopsOnEqual(List<Infantry> stack) =>
        stack.Count > 0 && stack.All(u => u.equalWeightPushMode == EqualWeightPushMode.Stop);

    private static bool CanPushStack(List<Infantry> pusher, List<Infantry> target, float forceMultiplier = 1.0f)
    {
        float pusherWeight = GetStackWeight(pusher) * forceMultiplier;
        float targetWeight = GetStackWeight(target);
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
            if (destination.infantry == null) destination.infantry = unit;
            unit.Position = destination.Position;
        }
    }

    private static void DestroyPushedUnit(Infantry unit, GameManager gameManager)
    {
        if (!IsLivingUnit(unit)) return;
        unit.health = 0;
        unit.UpdateHpLabel();
        unit.OnDestroyed();
        var origin = unit.grid;
        if (origin != null)
        {
            origin.infantries.Remove(unit);
            if (origin.infantry == unit)
                origin.infantry = origin.infantries.FirstOrDefault(IsLivingUnit);
            unit.grid = null;
        }
        unit.ClearFootprint();
        unit.CallDeferred(Node.MethodName.QueueFree);
        gameManager?.RemovePiece(unit);
    }
}
