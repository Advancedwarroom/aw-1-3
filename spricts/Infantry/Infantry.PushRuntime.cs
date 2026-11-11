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
        if (!CanPushTargetTeam(initialStack)) return;
        ResolvePushChain(target.grid, direction, pushDistance, gm.gridManager, gm);
    }

    public void TryPushWeaponOnAttack(Weapon target)
    {
        if (!CanPushNonInfantryTarget(target?.grid, target?.pushableByPhysics, target?.GetEffectiveWeight(),
            target?.equalWeightPushMode ?? EqualWeightPushMode.Stop, target == null || target.isDestroyed)
            || target == null || !CanPushTeamName(target.team))
            return;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null || target == null || target.isMultiTile) return;
        Vector2I direction = GetPushDirection(target.grid);
        if (direction == Vector2I.Zero) return;
        for (int step = 0; step < pushDistance; step++)
        {
            var next = gm.gridManager.GetGridAt(target.grid.GridIndex + direction);
            if (next == null || next.weapon != null || next.city != null
                || GetLivingStack(next).Count > 0) break;
            if (!target.MoveByPhysicsPush(next, gm.weaponManager)) break;
        }
    }

    public void TryPushFacilityOnAttack(Facility target)
    {
        if (!CanPushNonInfantryTarget(target?.ParentGrid, target?.pushableByPhysics,
            target?.GetEffectiveWeight(), target?.equalWeightPushMode ?? EqualWeightPushMode.Stop,
            target == null || target.health <= 0)
            || target == null || !CanPushTeamName(target.facilityTeam))
            return;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null || target == null) return;
        Vector2I direction = GetPushDirection(target.ParentGrid);
        if (direction == Vector2I.Zero) return;
        for (int step = 0; step < pushDistance; step++)
        {
            var next = gm.gridManager.GetGridAt(target.ParentGrid.GridIndex + direction);
            if (next == null || next.city != null || next.weapon != null
                || GetLivingStack(next).Count > 0) break;
            if (!target.MoveByPhysicsPush(next)) break;
        }
    }

    private bool CanPushNonInfantryTarget(Grids targetGrid, bool? pushable, float? targetWeight,
        EqualWeightPushMode targetMode, bool invalid)
    {
        if (!pushEnabled || (pushTriggerMode != PushTriggerMode.Attack
            && pushTriggerMode != PushTriggerMode.SeparateAction) || pushDistance <= 0
            || targetGrid == null || invalid || pushable != true || targetWeight == null)
            return false;
        float ownWeight = GetEffectiveWeight();
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
                    && w.pushableByPhysics && w.team != team) ?? false)
                    || (tile.city != null && tile.city.pushableByPhysics
                        && tile.city.facilityTeam != team));
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
            if (unit.team == team) return pushCanTargetFriendlies;
            if (TeamHelper.AreAllied(team, unit.team)) return pushCanTargetAllies;
            return pushCanTargetEnemies;
        });
    }

    private bool CanPushTeamName(string targetTeam)
    {
        if (string.IsNullOrEmpty(targetTeam)) return false;
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

    private void ApplyPushTargetEffects(List<Infantry> stack, GameManager gm)
    {
        foreach (var unit in stack.ToList())
        {
            if (!IsLivingUnit(unit)) continue;
            if (pushLockTargetToOneHp) unit.health = 1;
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
                && GodotObject.IsInstanceValid(w) && w.pushableByPhysics && w.team != team);
            var facility = targetGrid.city != null && targetGrid.city.pushableByPhysics
                && targetGrid.city.facilityTeam != team ? targetGrid.city : null;
            if (weapon == null && facility == null) return;
            if (!CanPayPushCost(gm) || !TryMarkPushActionUsed()) return;
            PayPushCost(gm);
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
            if (stack.Count == 0) break;
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

        for (int step = 0; step < distance; step++)
        {
            for (int i = chain.Count - 1; i > firstBlockedLink; i--)
            {
                var stack = chain[i].Where(IsLivingUnit).ToList();
                if (stack.Count == 0) continue;
                var destination = gridManager.GetGridAt(stack[0].grid.GridIndex + direction);
                if (destination == null)
                {
                    foreach (var unit in stack) DestroyPushedUnit(unit, gameManager);
                    continue;
                }

                // Each member's own terrain rules apply. Units unable to enter this tile
                // are destroyed immediately; the rest of the stack continues together.
                foreach (var unit in stack.ToList())
                    if (unit.GetMoveCostOnGrid(destination) >= 999
                        && (unit.pushSelfDestructOnInvalidIntermediateTile || step == distance - 1))
                        DestroyPushedUnit(unit, gameManager);
                stack = stack.Where(IsLivingUnit).ToList();
                if (stack.Count == 0) continue;

                var blockers = GetLivingStack(destination).Where(u => !stack.Contains(u)).ToList();
                if (blockers.Count > 0) continue;

                if (!TryDisplaceWeaponAndFacility(stack, destination, direction, gridManager))
                    continue;

                MoveStackByPush(stack, destination);
                ApplyPushTargetEffects(stack, gameManager);
            }
        }

        if (pushPusherFollowsChain && firstGrid != null && grid != null)
        {
            var followDestination = firstGrid;
            if (GetLivingStack(followDestination).Count == 0)
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

        // A weapon can be stood on only when its own overlap policy allows it.
        foreach (var weapon in destination.weapons.Where(w =>
            w != null && GodotObject.IsInstanceValid(w) && !w.IsQueuedForDeletion()).ToList())
        {
            if (!weapon.pushBlocksInfantryWhenOverlapped) continue;
            if (!weapon.pushableByPhysics || nextGrid == null || !Mathf.IsEqualApprox(movingWeight, weapon.GetEffectiveWeight())
                && movingWeight < weapon.GetEffectiveWeight())
                return false;
            if (Mathf.IsEqualApprox(movingWeight, weapon.GetEffectiveWeight())
                && !CanResolveEqualWeight(movingStack, weapon.equalWeightPushMode))
                return false;
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            if (!weapon.MoveByPhysicsPush(nextGrid, gm?.weaponManager))
                return false;
        }

        var facility = destination.city;
        if (facility != null && facility.pushBlocksInfantryWhenOverlapped)
        {
            if (!facility.pushableByPhysics || nextGrid == null || movingWeight < facility.GetEffectiveWeight())
                return false;
            if (Mathf.IsEqualApprox(movingWeight, facility.GetEffectiveWeight())
                && !CanResolveEqualWeight(movingStack, facility.equalWeightPushMode))
                return false;
            if (!facility.MoveByPhysicsPush(nextGrid))
                return false;
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
