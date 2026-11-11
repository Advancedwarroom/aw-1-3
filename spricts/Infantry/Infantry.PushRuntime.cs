using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Grid-accurate forced displacement. Stack occupants move as one mass.</summary>
public partial class Infantry
{
    private PopupMenu pushDistancePopup;

    public bool TryPushWeaponOnMovement(Weapon target, Vector2I movementDirection)
    {
        if (target == null || target.grid == null || movementDirection == Vector2I.Zero)
            return false;
        var origin = target.grid;
        TryPushWeaponOnAttack(target, fromMovement: true, directionOverride: movementDirection);
        return target.grid != null && target.grid != origin;
    }

    public bool TryPushFacilityOnMovement(Facility target, Vector2I movementDirection)
    {
        if (target == null || target.ParentGrid == null || movementDirection == Vector2I.Zero)
            return false;
        var origin = target.ParentGrid;
        TryPushFacilityOnAttack(target, fromMovement: true, directionOverride: movementDirection);
        return target.ParentGrid != null && target.ParentGrid != origin;
    }

    public bool TryPushTargetOnMovement(Grids targetGrid, Vector2I movementDirection)
    {
        if (!CanUseMovementPush() || targetGrid == null || grid == null
            || movementDirection == Vector2I.Zero || GetRequestedPushDistance() <= 0)
            return false;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return false;
        var targetStack = GetLivingStack(targetGrid);
        if (!CanPushTargetTeam(targetStack) || !CanPayPushCost(gm))
            return false;

        if (!ResolvePushChain(targetGrid, movementDirection, GetRequestedPushDistance(),
            gm.gridManager, gm, fromMovement: true))
            return false;
        if (!TryMarkMovementPushUsed()) return false;
        PayPushCost(gm);
        return true;
    }

    public void TryPushTargetOnAttack(Infantry target)
    {
        int distance = GetRequestedPushDistance();
        if (!CanUseAttackPush() || distance <= 0
            || grid == null || target == null || !GodotObject.IsInstanceValid(target)
            || target.grid == null || !IsWithinPushTargetRange(target.grid)) return;

        Vector2I direction = GetPushDirection(target.grid);

        if (direction == Vector2I.Zero) return;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null) return;
        if (!CanPayPushCost(gm)) return;
        if (!ResolvePushChain(target.grid, direction, distance, gm.gridManager, gm))
            return;
        if (!TryMarkAttackPushUsed())
            return;
        PayPushCost(gm);
    }

    public void TryPushWeaponOnAttack(Weapon target, bool fromSeparateAction = false,
        int? distanceOverride = null, bool fromMovement = false, Vector2I? directionOverride = null)
    {
        int distance = GetRequestedPushDistance(distanceOverride);
        bool triggerReady = fromSeparateAction ? pushTriggerMode == PushTriggerMode.SeparateAction
            : fromMovement ? CanUseMovementPush() : CanUseAttackPush();
        if (distance > 0 && triggerReady
            && pushCanTargetWeapons
            && target != null && target.grid != null
            && (fromMovement || IsWithinPushTargetRange(target.grid))
            && CanPushTeamName(target.team, target.GetType().Name)
            && CanPushNonInfantryTarget(target.grid, target.pushableByPhysics, target.GetEffectiveWeight(),
                target.equalWeightPushMode, target.isDestroyed, distance))
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm?.gridManager == null || target == null) return;
            Vector2I direction = directionOverride ?? GetPushDirection(target.grid);
            if (direction == Vector2I.Zero || !CanPayPushCost(gm)) return;
            var firstDestination = gm.gridManager.GetGridAt(target.grid.GridIndex + direction);
            if (!CanMoveStaticTargetTo(firstDestination, target, null)) return;

            bool moved = false;
            for (int step = 0; step < distance; step++)
            {
                var next = gm.gridManager.GetGridAt(target.grid.GridIndex + direction);
                if (!CanMoveStaticTargetTo(next, target, null)
                    || !target.MoveByPhysicsPush(next, gm.weaponManager)) break;
                moved = true;
            }
            if (!moved) return;
            if (!fromMovement && pushDirectionMode == PushDirectionMode.OppositeSides)
                MovePusherOppositeDirection(direction, distance, gm.gridManager, gm);
            if (fromSeparateAction)
            {
                if (!TryMarkPushActionUsed()) return;
            }
            else if (fromMovement)
            {
                if (!TryMarkMovementPushUsed()) return;
            }
            else if (!TryMarkAttackPushUsed()) return;
            PayPushCost(gm);
            ApplyStaticPushEffects(target);
        }
    }

    public void TryPushFacilityOnAttack(Facility target, bool fromSeparateAction = false,
        int? distanceOverride = null, bool fromMovement = false, Vector2I? directionOverride = null)
    {
        int distance = GetRequestedPushDistance(distanceOverride);
        bool triggerReady = fromSeparateAction ? pushTriggerMode == PushTriggerMode.SeparateAction
            : fromMovement ? CanUseMovementPush() : CanUseAttackPush();
        if (distance <= 0 || !triggerReady
            || !pushCanTargetFacilities
            || target == null || target.ParentGrid == null
            || (!fromMovement && !IsWithinPushTargetRange(target.ParentGrid))
            || !CanPushTeamName(target.facilityTeam, target.GetType().Name)
            || !CanPushNonInfantryTarget(target.ParentGrid, target.pushableByPhysics, target.GetEffectiveWeight(),
                target.equalWeightPushMode, target.health <= 0, distance))
            return;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return;
        Vector2I direction = directionOverride ?? GetPushDirection(target.ParentGrid);
        if (direction == Vector2I.Zero || !CanPayPushCost(gm)) return;
        var firstDestination = gm.gridManager.GetGridAt(target.ParentGrid.GridIndex + direction);
        if (!CanMoveStaticTargetTo(firstDestination, null, target)) return;

        bool moved = false;
        for (int step = 0; step < distance; step++)
        {
            var next = gm.gridManager.GetGridAt(target.ParentGrid.GridIndex + direction);
            if (!CanMoveStaticTargetTo(next, null, target) || !target.MoveByPhysicsPush(next)) break;
            moved = true;
        }
        if (!moved) return;
        if (!fromMovement && pushDirectionMode == PushDirectionMode.OppositeSides)
            MovePusherOppositeDirection(direction, distance, gm.gridManager, gm);
        if (fromSeparateAction)
        {
            if (!TryMarkPushActionUsed()) return;
        }
        else if (fromMovement)
        {
            if (!TryMarkMovementPushUsed()) return;
        }
        else if (!TryMarkAttackPushUsed()) return;
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
        EqualWeightPushMode targetMode, bool invalid, int distance)
    {
        if (!pushEnabled || (pushTriggerMode != PushTriggerMode.Attack
            && pushTriggerMode != PushTriggerMode.SeparateAction
            && pushTriggerMode != PushTriggerMode.Movement) || distance <= 0
            || targetGrid == null || invalid || pushable != true || targetWeight == null)
            return false;
        var pusherStack = GetLivingStack(grid);
        if (pusherStack.Count == 0) pusherStack.Add(this);
        float ownWeight = GetStackWeight(pusherStack) * GetPushForceMultiplier(distance);
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
        if (!CanUseSeparatePushAction() || grid == null || GetRequestedPushDistance() <= 0) return;
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
            bool hasPushableStatic =
                (pushCanTargetWeapons && (tile.weapons?.Any(w => w != null && GodotObject.IsInstanceValid(w)
                    && w.pushableByPhysics && CanPushTeamName(w.team, w.GetType().Name)) ?? false))
                || (pushCanTargetFacilities && tile.city != null && tile.city.pushableByPhysics
                    && CanPushTeamName(tile.city.facilityTeam, tile.city.GetType().Name));
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
        ClosePushDistanceMenu();
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
        return pushCanTargetInfantry && stack != null && stack.Count > 0 && stack.All(unit =>
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

    private float GetPushForceMultiplier(int? requestedDistance = null) =>
        pushForceScalesWithDistance
            ? Mathf.Max(0.01f, (requestedDistance ?? pushDistance) * pushForcePerTile)
            : 1.0f;

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
            PushResourceType.Ammo => HasAmmo(pushCostAmmoKind, cost),
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
                // A pusher may be destroyed by its own displacement (for example,
                // retreating into water). Never resurrect it while charging the cost.
                if (health > 0)
                {
                    health = Mathf.Max(1, health - cost);
                    UpdateHpLabel();
                }
                break;
            case PushResourceType.Fuel:
                fuel = Mathf.Max(0, fuel - cost);
                break;
            case PushResourceType.PrimaryAmmo:
                TryConsumeAmmo(AmmoKind.Primary, cost);
                break;
            case PushResourceType.Ammo:
                TryConsumeAmmo(pushCostAmmoKind, cost);
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
    // the other one without charging resources for the failed attempt.
    private bool TryResolveStackDisplacement(List<Infantry> stack, Grids origin,
        Vector2I pushDirection, GridManager gridManager, GameManager gm)
    {
        var rebounder = stack.FirstOrDefault(u => IsLivingUnit(u) && u.pushReboundEnabled);
        var transferer = stack.FirstOrDefault(u => IsLivingUnit(u) && u.pushTransferEnabled
            && u.pushTransferDirection != Vector2I.Zero);

        if (rebounder == null)
            return TryTransferStack(stack, pushDirection, gridManager, gm);
        if (transferer == null)
            return TryReboundStack(stack, origin, pushDirection, gm);

        int priority = CompareDisplacementPriority(rebounder, transferer, gm);
        if (priority == 0)
            priority = GD.Randf() < 0.5f ? 1 : -1;

        if (priority > 0)
            return TryReboundStack(stack, origin, pushDirection, gm, rebounder)
                || TryTransferStack(stack, pushDirection, gridManager, gm, transferer);
        return TryTransferStack(stack, pushDirection, gridManager, gm, transferer)
            || TryReboundStack(stack, origin, pushDirection, gm, rebounder);
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

    private bool TryReboundStack(List<Infantry> stack, Grids origin, Vector2I direction, GameManager gm,
        Infantry preferredRebounder = null)
    {
        var rebounder = preferredRebounder != null && IsLivingUnit(preferredRebounder)
            ? preferredRebounder
            : stack.FirstOrDefault(u => IsLivingUnit(u) && u.pushReboundEnabled);
        if (rebounder == null || origin == null || GetLivingStack(origin).Count > 0)
            return false;
        int ammoCost = Mathf.Max(0, rebounder.pushReboundAmmoCost);
        int fuelCost = Mathf.Max(0, rebounder.pushReboundFuelCost);
        int fundsCost = Mathf.Max(0, rebounder.pushReboundFundsCost);
        var reboundDestination = rebounder.pushReboundToOrigin
            ? origin
            : gm?.gridManager?.GetGridAt(origin.GridIndex - direction);
        if (reboundDestination == null || GetLivingStack(reboundDestination).Count > 0
            || reboundDestination.city != null || (reboundDestination.weapons?.Count ?? 0) > 0
            || rebounder.GetAmmo(rebounder.pushReboundAmmoKind) < ammoCost || rebounder.fuel < fuelCost
            || (fundsCost > 0 && (gm == null || gm.GetTeamFunds(rebounder.team) < fundsCost)))
            return false;

        // Validate the destination and every cost before charging anything.
        if (ammoCost > 0 && !rebounder.HasAmmo(rebounder.pushReboundAmmoKind, ammoCost)) return false;
        if (ammoCost > 0) rebounder.TryConsumeAmmo(rebounder.pushReboundAmmoKind, ammoCost);
        rebounder.fuel -= fuelCost;
        if (fundsCost > 0)
            gm.SetTeamFunds(rebounder.team, gm.GetTeamFunds(rebounder.team) - fundsCost);
        MoveStackByPush(stack, reboundDestination);
        return true;
    }

    private bool TryTransferStack(List<Infantry> stack, Vector2I pushDirection,
        GridManager gridManager, GameManager gm, Infantry preferredTransferer = null)
    {
        var transferUnit = preferredTransferer != null && IsLivingUnit(preferredTransferer)
            ? preferredTransferer
            : stack.FirstOrDefault(u => IsLivingUnit(u) && u.pushTransferEnabled);
        if (transferUnit == null || transferUnit.pushTransferDirection == Vector2I.Zero)
            return false;
        int ammoCost = Mathf.Max(0, transferUnit.pushTransferAmmoCost);
        int fuelCost = Mathf.Max(0, transferUnit.pushTransferFuelCost);
        int healthCost = Mathf.Max(0, transferUnit.pushTransferHealthCost);
        int fundsCost = Mathf.Max(0, transferUnit.pushTransferFundsCost);

        Vector2I direction = transferUnit.pushTransferDirection;
        direction = Mathf.Abs(direction.X) >= Mathf.Abs(direction.Y)
            ? new Vector2I(Math.Sign(direction.X), 0)
            : new Vector2I(0, Math.Sign(direction.Y));
        if (direction == Vector2I.Zero) return false;
        var origin = stack.FirstOrDefault(IsLivingUnit)?.grid;
        var destination = origin == null ? null : gridManager.GetGridAt(origin.GridIndex + direction);
        if (destination == null || GetLivingStack(destination).Count > 0
            || destination.city != null || (destination.weapons?.Count ?? 0) > 0
            || transferUnit.GetAmmo(transferUnit.pushTransferAmmoKind) < ammoCost || transferUnit.fuel < fuelCost
            || transferUnit.health <= healthCost
            || (fundsCost > 0 && (gm == null || gm.GetTeamFunds(transferUnit.team) < fundsCost)))
            return false;
        if (ammoCost > 0 && !transferUnit.HasAmmo(transferUnit.pushTransferAmmoKind, ammoCost)) return false;

        // No resources are consumed until the destination is known to be valid.
        if (ammoCost > 0) transferUnit.TryConsumeAmmo(transferUnit.pushTransferAmmoKind, ammoCost);
        transferUnit.fuel -= fuelCost;
        transferUnit.health -= healthCost;
        if (fundsCost > 0)
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
                unit.maxHealth = (int)Math.Clamp((long)unit.maxHealth + unit.pushTargetMaxHealthDelta, 1L, int.MaxValue);
            if (pushLockTargetToOneHp) unit.health = 1;
            else if (pushTargetHealAmount > 0)
                unit.health = (int)Math.Min(unit.pushTargetCanOverMaxHealth ? int.MaxValue : unit.GetEffectiveMaxHealth(),
                    Math.Min(int.MaxValue, (long)unit.health + pushTargetHealAmount));
            else if (pushTargetDamage > 0 || pushRamDamage > 0)
            {
                long totalDamage = Math.Max(0L, (long)pushTargetDamage + pushRamDamage);
                long damagedHealth = (long)unit.health - totalDamage;
                if (damagedHealth <= 0 && pushDamageCanDestroy)
                {
                    DestroyPushedUnit(unit, gm);
                    continue;
                }
                unit.health = (int)Math.Max(1L, damagedHealth);
            }
            if (pushTargetFuelDelta != 0)
                unit.fuel = (int)Math.Clamp((long)unit.fuel + pushTargetFuelDelta, 0L, int.MaxValue);
            if (pushTargetAmmoDelta != 0)
                unit.AddAmmo(pushTargetAmmoKind, pushTargetAmmoDelta);
            unit.UpdateHpLabel();
        }
    }

    private int GetRequestedPushDistance(int? selectedDistance = null)
    {
        int maximum = Mathf.Max(0, pushDistance);
        int minimum = Mathf.Clamp(pushMinDistance, 1, Math.Max(1, maximum));
        int requested = selectedDistance ?? maximum;
        return requested >= minimum && requested <= maximum ? requested : 0;
    }

    private void OpenPushDistanceMenu(Grids targetGrid)
    {
        if (targetGrid == null || !CanUseSeparatePushAction()) return;
        if (pushDistancePopup != null && GodotObject.IsInstanceValid(pushDistancePopup))
            pushDistancePopup.QueueFree();

        int maximum = Mathf.Max(0, pushDistance);
        int minimum = Mathf.Clamp(pushMinDistance, 1, Math.Max(1, maximum));
        if (maximum < minimum) return;

        var popup = new PopupMenu();
        pushDistancePopup = popup;
        GetTree().Root.AddChild(popup);
        for (int distance = minimum; distance <= maximum; distance++)
            popup.AddItem($"{distance} 格", distance);

        popup.IdPressed += id =>
        {
            int chosenDistance = (int)id;
            if (GodotObject.IsInstanceValid(popup)) popup.QueueFree();
            if (pushDistancePopup == popup) pushDistancePopup = null;
            ExecuteSeparatePushOnGrid(targetGrid, chosenDistance);
        };
        var mousePosition = (Vector2I)GetViewport().GetMousePosition();
        popup.Popup(new Rect2I(mousePosition, new Vector2I(1, 1)));
    }

    private void ClosePushDistanceMenu()
    {
        if (pushDistancePopup != null && GodotObject.IsInstanceValid(pushDistancePopup))
            pushDistancePopup.QueueFree();
        pushDistancePopup = null;
    }

    private void ExecuteSeparatePushOnGrid(Grids targetGrid, int? selectedDistance = null)
    {
        if (!pushAutoDistance && selectedDistance == null)
        {
            OpenPushDistanceMenu(targetGrid);
            return;
        }
        int distance = GetRequestedPushDistance(selectedDistance);
        if (distance <= 0) return;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var gridManager = gm?.gridManager;
        if (targetGrid == null || grid == null || gridManager == null) return;
        var stack = GetLivingStack(targetGrid);
        bool infantryTarget = stack.Count > 0 && CanPushTargetTeam(stack);
        bool staticTarget = !infantryTarget && (pushCanTargetWeapons || pushCanTargetFacilities);
        if (stack.Count > 0 && !infantryTarget && !staticTarget) return;
        if (staticTarget)
        {
            var weapon = pushCanTargetWeapons
                ? targetGrid.weapons?.FirstOrDefault(w => w != null
                    && GodotObject.IsInstanceValid(w) && w.pushableByPhysics
                    && CanPushTeamName(w.team, w.GetType().Name))
                : null;
            var facility = pushCanTargetFacilities && targetGrid.city != null && targetGrid.city.pushableByPhysics
                && CanPushTeamName(targetGrid.city.facilityTeam, targetGrid.city.GetType().Name)
                ? targetGrid.city : null;
            if (weapon == null && facility == null) return;
            // The selected target is fully validated once by the corresponding
            // static-target resolver. Avoid a second equal-weight random arbitration.
            if (!CanPayPushCost(gm) || !CanUseSeparatePushAction()) return;
            if (weapon != null) TryPushWeaponOnAttack(weapon, fromSeparateAction: true, distanceOverride: distance);
            else TryPushFacilityOnAttack(facility, fromSeparateAction: true, distanceOverride: distance);
            gridManager.HideAttackRange();
            gridManager.ClearAllEmptyCallbacks();
            gm.isSelectingAttackTarget = false;
            actionMenu?.Hide();
            return;
        }

        Vector2I direction = GetPushDirection(targetGrid);
        if (direction == Vector2I.Zero) return;
        if (!CanPayPushCost(gm) || !CanUseSeparatePushAction()) return;
        if (!ResolvePushChain(targetGrid, direction, distance, gridManager, gm))
            return;
        if (!TryMarkPushActionUsed())
            return;
        PayPushCost(gm);
        gridManager.HideAttackRange();
        gridManager.ClearAllEmptyCallbacks();
        gm.isSelectingAttackTarget = false;
        actionMenu?.Hide();
    }

    private bool ResolvePushChain(Grids firstGrid, Vector2I direction, int distance,
        GridManager gridManager, GameManager gameManager, bool fromMovement = false)
    {
        // A stack is all living infantry occupying one tile. Its effective mass is the
        // sum of each member's health-adjusted weight.
        var chain = new List<List<Infantry>>();
        var cursor = firstGrid;
        int chainUnitCount = 0;
        while (cursor != null)
        {
            var stack = GetLivingStack(cursor);
            if (stack.Count == 0 || !CanPushTargetTeam(stack)) break;
            if (pushMaxChainUnits > 0 && chainUnitCount + stack.Count > pushMaxChainUnits)
                break;
            chain.Add(stack);
            chainUnitCount += stack.Count;
            if (!pushChainEnabled
                || (pushMaxChainStacks > 0 && chain.Count >= pushMaxChainStacks)
                || (pushMaxChainUnits > 0 && chainUnitCount >= pushMaxChainUnits))
                break;
            cursor = gridManager.GetGridAt(cursor.GridIndex + direction);
        }
        if (chain.Count == 0) return false;

        // Force must be transmitted through every occupied stack in order.
        // If any link cannot move, the chain is mechanically blocked: stacks farther
        // ahead are not independently pushed through the failed link.
        List<Infantry> pusherStack = GetLivingStack(grid);
        if (pusherStack.Count == 0) pusherStack.Add(this);
        float transmittedForce = GetStackWeight(pusherStack) * GetPushForceMultiplier(distance);
        for (int i = 0; i < chain.Count; i++)
        {
            if (!CanPushStackWithForce(pusherStack, transmittedForce, chain[i]))
                return false;

            // The initiating unit's Export controls force transmission through the
            // entire chain. Constant preserves the original force; Accumulate adds
            // each displaced stack's mass; Decrease consumes force by that mass.
            float targetMass = GetStackWeight(chain[i]);
            switch (pushChainWeightMode)
            {
                case PushChainWeightMode.Accumulate:
                    transmittedForce += targetMass;
                    if (pushChainMaxAccumulatedWeight > 0.0f)
                        transmittedForce = Mathf.Min(transmittedForce, pushChainMaxAccumulatedWeight);
                    break;
                case PushChainWeightMode.Decrease:
                    transmittedForce = Mathf.Max(0.0f, transmittedForce - targetMass);
                    break;
            }
        }

        // Keep each stack's original tile so rebound is resolved once, after the
        // complete displacement. Target effects must also run once per push action,
        // not once for every tile traversed.
        var originalTiles = chain.Select(stack => stack.FirstOrDefault(IsLivingUnit)?.grid).ToList();
        var movedStacks = new HashSet<int>();

        for (int step = 0; step < distance; step++)
        {
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                var stack = chain[i].Where(IsLivingUnit).ToList();
                if (stack.Count == 0) continue;
                int stackDistance = GetMitigatedPushDistance(stack, distance);
                if (step >= stackDistance) continue;
                bool finalStepForStack = step == stackDistance - 1;
                var currentGrid = stack[0].grid;
                if (currentGrid == null) continue;
                var destination = gridManager.GetGridAt(currentGrid.GridIndex + direction);
                if (destination == null)
                {
                    // The pusher decides whether reaching the map edge is lethal.
                    // A non-destructive push stops at the boundary without moving
                    // any stack through the missing tile.
                    if (pushSelfDestructOnInvalidIntermediateTile)
                        foreach (var unit in stack) DestroyPushedUnit(unit, gameManager);
                    break;
                }

                foreach (var unit in stack.ToList())
                {
                    bool invalidTerrain = unit.GetMoveCostOnGrid(destination) >= 999;
                    if (invalidTerrain && (pushSelfDestructOnInvalidIntermediateTile || finalStepForStack))
                        DestroyPushedUnit(unit, gameManager);
                }
                stack = stack.Where(IsLivingUnit).ToList();
                if (stack.Count == 0) continue;

                // Units configured for final-tile-only checks pass through invalid intermediate tiles.
                var blockers = GetLivingStack(destination).Where(u => !stack.Contains(u)).ToList();
                if (blockers.Count > 0 || HasForeignMultiTileOccupant(destination, stack)) continue;
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
            if (TryResolveStackDisplacement(stack, origin, direction, gridManager, gameManager))
                continue;
            ApplyPushTargetEffects(stack, gameManager);
        }

        if (!fromMovement && pushDirectionMode == PushDirectionMode.OppositeSides && movedStacks.Count > 0)
        {
            MovePusherOppositeDirection(direction, distance, gridManager, gameManager);
        }
        else if (!fromMovement && pushPusherFollowsChain && firstGrid != null && grid != null)
        {
            var followDestination = firstGrid;
            bool canOverlapStatic = (followDestination.city == null || !followDestination.city.pushBlocksInfantryWhenOverlapped)
                && (followDestination.weapons == null || followDestination.weapons.All(w =>
                    w == null || !GodotObject.IsInstanceValid(w) || !w.pushBlocksInfantryWhenOverlapped));
            if (canOverlapStatic && GetLivingStack(followDestination).Count == 0)
            {
                var followStack = GetLivingStack(grid);
                if (followStack.Count == 0) followStack.Add(this);
                foreach (var unit in followStack.ToList())
                {
                    if (unit.GetMoveCostOnGrid(followDestination) >= 999)
                        DestroyPushedUnit(unit, gameManager);
                }
                followStack = followStack.Where(IsLivingUnit).ToList();
                if (followStack.Count > 0)
                    MoveStackByPush(followStack, followDestination);
            }
        }

        gameManager?.fogOfWarManager?.OnUnitMoved();
        gameManager?.CallDeferred(nameof(GameManager.UpdateUnitLists));
        gameManager?.CallDeferred(nameof(GameManager.CheckVictoryCondition));
        return movedStacks.Count > 0;
    }

    private void MovePusherOppositeDirection(Vector2I pushDirection, int distance,
        GridManager gridManager, GameManager gameManager)
    {
        var pusherStack = GetLivingStack(grid);
        if (pusherStack.Count == 0) pusherStack.Add(this);

        Vector2I retreatDirection = -pushDirection;
        for (int step = 0; step < distance; step++)
        {
            pusherStack = pusherStack.Where(IsLivingUnit).ToList();
            if (pusherStack.Count == 0) break;
            var currentGrid = pusherStack[0].grid;
            var destination = currentGrid == null
                ? null : gridManager.GetGridAt(currentGrid.GridIndex + retreatDirection);
            if (destination == null)
            {
                if (pushSelfDestructOnInvalidIntermediateTile)
                    foreach (var unit in pusherStack) DestroyPushedUnit(unit, gameManager);
                break;
            }

            bool finalStep = step == distance - 1;
            foreach (var unit in pusherStack.ToList())
            {
                bool invalidTerrain = unit.GetMoveCostOnGrid(destination) >= 999;
                if (invalidTerrain && (pushSelfDestructOnInvalidIntermediateTile || finalStep))
                    DestroyPushedUnit(unit, gameManager);
            }
            pusherStack = pusherStack.Where(IsLivingUnit).ToList();
            if (pusherStack.Count == 0) break;

            var blockers = GetLivingStack(destination).Where(u => !pusherStack.Contains(u)).ToList();
            if (blockers.Count > 0 || HasForeignMultiTileOccupant(destination, pusherStack))
                break;
            if (!TryDisplaceWeaponAndFacility(pusherStack, destination, retreatDirection, gridManager))
                break;
            MoveStackByPush(pusherStack, destination);
        }
    }

    private bool TryDisplaceWeaponAndFacility(List<Infantry> movingStack,
        Grids destination, Vector2I direction, GridManager gridManager)
    {
        float movingWeight = GetStackWeight(movingStack);
        var nextGrid = gridManager.GetGridAt(destination.GridIndex + direction);
        var blockingWeapons = (destination.weapons?.AsEnumerable() ?? Enumerable.Empty<Weapon>())
            .Where(w => w != null && GodotObject.IsInstanceValid(w) && !w.IsQueuedForDeletion()
                && w.pushBlocksInfantryWhenOverlapped).ToList();
        var blockingFacility = destination.city != null && destination.city.pushBlocksInfantryWhenOverlapped
            ? destination.city : null;

        // A tile can hold one facility and one weapon. They share the destination
        // tile but occupy independent engine slots, so resolve both as one obstacle set.
        // Multiple weapons cannot be rebound into one slot by WeaponManager; reject that
        // configuration before moving anything rather than corrupting occupancy.
        if (blockingWeapons.Count > 1) return false;
        if (blockingWeapons.Count == 0 && blockingFacility == null) return true;
        if (nextGrid == null || !CanMoveStaticTargetTo(nextGrid)) return false;

        Weapon blockingWeapon = blockingWeapons.FirstOrDefault();
        if (blockingWeapon != null)
        {
            if (!pushCanTargetWeapons
                || !CanPushTeamName(blockingWeapon.team, blockingWeapon.GetType().Name))
                return false;
            float obstacleWeight = blockingWeapon.GetEffectiveWeight();
            if (!blockingWeapon.pushableByPhysics) return false;
            if (pushUsesWeight && movingWeight < obstacleWeight
                && !Mathf.IsEqualApprox(movingWeight, obstacleWeight)) return false;
            if (pushUsesWeight && Mathf.IsEqualApprox(movingWeight, obstacleWeight)
                && !CanResolveEqualWeight(movingStack, blockingWeapon.equalWeightPushMode)) return false;
        }

        if (blockingFacility != null)
        {
            if (!pushCanTargetFacilities
                || !CanPushTeamName(blockingFacility.facilityTeam, blockingFacility.GetType().Name))
                return false;
            float obstacleWeight = blockingFacility.GetEffectiveWeight();
            if (!blockingFacility.pushableByPhysics) return false;
            if (pushUsesWeight && movingWeight < obstacleWeight
                && !Mathf.IsEqualApprox(movingWeight, obstacleWeight)) return false;
            if (pushUsesWeight && Mathf.IsEqualApprox(movingWeight, obstacleWeight)
                && !CanResolveEqualWeight(movingStack, blockingFacility.equalWeightPushMode)) return false;
        }

        // All weight and destination checks have passed. Move the weapon first because
        // its manager validates the complete footprint; then move the facility slot.
        if (blockingWeapon != null)
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            if (!blockingWeapon.MoveByPhysicsPush(nextGrid, gm?.weaponManager))
                return false;
        }
        if (blockingFacility != null && !blockingFacility.MoveByPhysicsPush(nextGrid))
        {
            // Facility movement has no failure path after the preflight above; if a
            // custom override rejects it, do not attempt to overwrite the old slot.
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

    private bool CanPushStack(List<Infantry> pusher, List<Infantry> target, float forceMultiplier = 1.0f)
    {
        return CanPushStackWithForce(pusher, GetStackWeight(pusher) * forceMultiplier, target);
    }

    private bool CanPushStackWithForce(List<Infantry> pusherRules, float pusherWeight,
        List<Infantry> target)
    {
        if (!pushUsesWeight) return true;
        float targetWeight = GetStackWeight(target);
        if (pusherWeight > targetWeight && !Mathf.IsEqualApprox(pusherWeight, targetWeight))
            return true;
        if (!Mathf.IsEqualApprox(pusherWeight, targetWeight))
            return false;

        bool pusherAllows = StackAllowsSimultaneous(pusherRules);
        bool targetAllows = StackAllowsSimultaneous(target);
        bool pusherStops = StackStopsOnEqual(pusherRules);
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
}
