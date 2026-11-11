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
            && target != null && target.grid != null
            && (fromMovement || IsWithinPushTargetRange(target.grid))
            && CanPushTeamName(target.team, target)
            && CanPushNonInfantryTarget(target.grid, target.pushableByPhysics, target.GetEffectiveWeight(),
                target.equalWeightPushMode, target.isDestroyed, distance))
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm?.gridManager == null || target == null) return;
            Vector2I direction = directionOverride ?? GetPushDirection(target.grid);
            if (direction == Vector2I.Zero || !CanPayPushCost(gm)) return;
            var firstDestination = gm.gridManager.GetGridAt(target.grid.GridIndex + direction);
            if (!TryResolveWeaponFootprintCollisions(target, firstDestination, direction,
                gm, out bool firstDestroyed))
            {
                if (!firstDestroyed) return;
            }
            if (!firstDestroyed && !CanMoveStaticTargetTo(firstDestination, target, null,
                allowInfantryOverlap: !target.pushBlocksInfantryWhenOverlapped)) return;

            bool moved = firstDestroyed;
            for (int step = 0; step < distance && !firstDestroyed; step++)
            {
                var next = gm.gridManager.GetGridAt(target.grid.GridIndex + direction);
                if (!TryResolveWeaponFootprintCollisions(target, next, direction, gm,
                    out bool destroyed))
                {
                    if (destroyed) moved = true;
                    break;
                }
                if (destroyed)
                {
                    moved = true;
                    break;
                }
                if (!CanMoveStaticTargetTo(next, target, null,
                    allowInfantryOverlap: !target.pushBlocksInfantryWhenOverlapped)
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
            if (GodotObject.IsInstanceValid(target) && !target.isDestroyed && target.health > 0)
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
            || target == null || target.ParentGrid == null
            || (!fromMovement && !IsWithinPushTargetRange(target.ParentGrid))
            || !CanPushTeamName(target.facilityTeam, target)
            || !CanPushNonInfantryTarget(target.ParentGrid, target.pushableByPhysics, target.GetEffectiveWeight(),
                target.equalWeightPushMode, target.health <= 0, distance))
            return;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return;
        Vector2I direction = directionOverride ?? GetPushDirection(target.ParentGrid);
        if (direction == Vector2I.Zero || !CanPayPushCost(gm)) return;
        var firstDestination = gm.gridManager.GetGridAt(target.ParentGrid.GridIndex + direction);
        if (!TryResolveStaticTargetCollision(firstDestination, direction, null, target,
            gm, out bool firstDestroyed))
        {
            if (!firstDestroyed) return;
        }
        if (!firstDestroyed && !CanMoveStaticTargetTo(firstDestination, null, target,
            allowInfantryOverlap: !target.pushBlocksInfantryWhenOverlapped)) return;

        bool moved = firstDestroyed;
        for (int step = 0; step < distance && !firstDestroyed; step++)
        {
            var next = gm.gridManager.GetGridAt(target.ParentGrid.GridIndex + direction);
            if (!TryResolveStaticTargetCollision(next, direction, null, target, gm,
                out bool destroyed))
            {
                if (destroyed) moved = true;
                break;
            }
            if (destroyed)
            {
                moved = true;
                break;
            }
            if (!CanMoveStaticTargetTo(next, null, target,
                allowInfantryOverlap: !target.pushBlocksInfantryWhenOverlapped)
                || !target.MoveByPhysicsPush(next)) break;
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
        if (GodotObject.IsInstanceValid(target) && target.ParentGrid != null && target.health > 0)
            ApplyStaticPushEffects(target);
    }

    private bool CanMoveStaticTargetTo(Grids destination, Weapon movingWeapon = null,
        Facility movingFacility = null, bool allowInfantryOverlap = false)
    {
        if (movingWeapon != null && movingWeapon.isMultiTile)
            return CanMoveWeaponFootprintTo(movingWeapon, destination, allowInfantryOverlap);
        if (destination == null || (!allowInfantryOverlap && GetLivingStack(destination).Count > 0)
            || HasForeignMultiTileOccupant(destination, new List<Infantry>()))
            return false;
        if (destination.city != null && destination.city != movingFacility)
            return false;
        return destination.weapons == null
            || destination.weapons.All(w => w == null || !GodotObject.IsInstanceValid(w)
                || w == movingWeapon);
    }

    private bool TryResolveWeaponFootprintCollisions(Weapon weapon, Grids nextAnchor,
        Vector2I direction, GameManager gm, out bool movingWeaponDestroyed)
    {
        movingWeaponDestroyed = false;
        if (weapon == null || nextAnchor == null || gm?.gridManager == null)
            return false;
        if (!weapon.pushBlocksInfantryWhenOverlapped) return true;

        var oldFootprint = weapon.grid == null
            ? new HashSet<Vector2I>()
            : weapon.GetOccupiedIndices(weapon.grid.GridIndex).ToHashSet();
        foreach (var index in weapon.GetOccupiedIndices(nextAnchor.GridIndex)
            .Where(index => !oldFootprint.Contains(index))
            .OrderByDescending(index => index.X * direction.X + index.Y * direction.Y))
        {
            var tile = gm.gridManager.GetGridAt(index);
            if (tile == null) return false;
            if (!TryResolveStaticTargetCollision(tile, direction, weapon, null, gm,
                out bool cellDestroyed))
            {
                movingWeaponDestroyed = cellDestroyed;
                return false;
            }
            if (cellDestroyed)
            {
                movingWeaponDestroyed = true;
                return false;
            }
        }
        return true;
    }

    private bool CanMoveWeaponFootprintTo(Weapon weapon, Grids nextAnchor,
        bool allowInfantryOverlap)
    {
        if (weapon == null || nextAnchor == null) return false;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return false;
        var oldFootprint = weapon.grid == null
            ? new HashSet<Vector2I>()
            : weapon.GetOccupiedIndices(weapon.grid.GridIndex).ToHashSet();
        foreach (var index in weapon.GetOccupiedIndices(nextAnchor.GridIndex))
        {
            var tile = gm.gridManager.GetGridAt(index);
            if (tile == null) return false;
            if (!oldFootprint.Contains(index)
                && (tile.city != null
                    || (tile.weapons?.Any(w => w != null && GodotObject.IsInstanceValid(w)
                        && w != weapon) ?? false)))
                return false;
            if (!allowInfantryOverlap && !oldFootprint.Contains(index)
                && GetLivingStack(tile).Count > 0)
                return false;
            if (!allowInfantryOverlap && !oldFootprint.Contains(index)
                && HasForeignMultiTileOccupant(tile, new List<Infantry>()))
                return false;
        }
        return true;
    }

    private bool TryResolveStaticTargetCollision(Grids destination, Vector2I direction,
        Weapon movingWeapon, Facility movingFacility, GameManager gm, out bool movingStaticDestroyed)
    {
        movingStaticDestroyed = false;
        if (destination == null) return false;
        bool blocksInfantry = movingWeapon != null
            ? movingWeapon.pushBlocksInfantryWhenOverlapped
            : movingFacility?.pushBlocksInfantryWhenOverlapped ?? false;
        if (!blocksInfantry) return true;

        var stack = GetLivingStack(destination);
        if (stack.Count == 0) return true;
        if (!pushUsesWeight)
        {
            ApplyFragileCollisionEffects(stack);
            return false;
        }
        float staticWeight = movingWeapon != null
            ? movingWeapon.GetEffectiveWeight()
            : movingFacility.GetEffectiveWeight();
        float unitWeight = GetStackWeight(stack);
        EqualWeightPushMode staticMode = movingWeapon != null
            ? movingWeapon.equalWeightPushMode : movingFacility.equalWeightPushMode;

        if (staticWeight > unitWeight && !Mathf.IsEqualApprox(staticWeight, unitWeight))
        {
            DestroyCollisionLoser(stack);
            return true;
        }
        if (unitWeight > staticWeight && !Mathf.IsEqualApprox(unitWeight, staticWeight))
        {
            movingStaticDestroyed = DestroyStaticPushTarget(movingWeapon, movingFacility, gm);
            ApplyFragileCollisionEffects(stack);
            return false;
        }

        bool staticAllows = staticMode == EqualWeightPushMode.AllowSimultaneous;
        bool staticStops = staticMode == EqualWeightPushMode.Stop;
        bool unitAllows = StackAllowsSimultaneous(stack);
        bool unitStops = StackStopsOnEqual(stack);
        bool moveBoth = staticAllows && unitAllows
            || !(staticStops && unitStops) && GD.Randf() < 0.5f;
        if (!moveBoth)
        {
            ApplyFragileCollisionEffects(stack);
            return false;
        }

        var next = gm?.gridManager?.GetGridAt(destination.GridIndex + direction);
        if (next == null || GetLivingStack(next).Count > 0
            || HasForeignMultiTileOccupant(next, stack)
            || (next.city != null && next.city.pushBlocksInfantryWhenOverlapped)
            || (next.weapons?.Any(w => w != null && GodotObject.IsInstanceValid(w)
                && w.pushBlocksInfantryWhenOverlapped) ?? false))
        {
            ApplyFragileCollisionEffects(stack);
            return false;
        }

        foreach (var unit in stack.ToList())
        {
            if (unit.GetMoveCostOnGrid(next) >= 999
                || (pushDestroyOnProperty && next.city != null))
                DestroyPushedUnit(unit, gm);
        }
        stack = stack.Where(IsLivingUnit).ToList();
        if (stack.Count > 0) MoveStackByPush(stack, next);
        ApplyFragileCollisionEffects(stack);
        return true;
    }

    private bool DestroyStaticPushTarget(Weapon weapon, Facility facility, GameManager gm)
    {
        if (weapon != null && GodotObject.IsInstanceValid(weapon) && !weapon.isDestroyed)
        {
            weapon.TakeDamage(int.MaxValue, this);
            return weapon.health <= 0 || weapon.isDestroyed || weapon.IsQueuedForDeletion();
        }
        if (facility != null && GodotObject.IsInstanceValid(facility))
        {
            var origin = facility.ParentGrid;
            if (origin != null && origin.city == facility) origin.city = null;
            facility.health = 0;
            facility.QueueFree();
            gm?.CallDeferred(nameof(GameManager.UpdateUnitLists));
            return true;
        }
        return false;
    }

    private void ApplyStaticPushEffects(Weapon target)
    {
        if (target == null || !GodotObject.IsInstanceValid(target) || target.isDestroyed) return;
        if (pushTargetMaxHealthDelta != 0)
            target.maxHealth = (int)Math.Clamp((long)target.maxHealth + pushTargetMaxHealthDelta, 1L, int.MaxValue);

        if (!pushTargetCanOverMaxHealth)
            target.health = Math.Min(target.health, target.maxHealth);
        if (pushLockTargetToOneHp) target.health = 1;
        else if (pushTargetHealAmount > 0)
            target.health = (int)Math.Min(pushTargetCanOverMaxHealth ? int.MaxValue : target.maxHealth,
                Math.Min(int.MaxValue, (long)target.health + pushTargetHealAmount));
        else
        {
            int damage = (int)Math.Clamp((long)pushTargetDamage + pushRamDamage, 0L, int.MaxValue);
            if (damage > 0)
            {
                if (!pushDamageCanDestroy && (long)target.health - damage <= 0)
                    target.health = 1;
                else
                    target.TakeDamage(damage, this);
            }
        }

        int weaponAmmoType = pushTargetAmmoKind switch
        {
            AmmoKind.Primary => 1,
            AmmoKind.Explode => 2,
            AmmoKind.Flare => 3,
            AmmoKind.StealthBullet => 4,
            AmmoKind.RevealBullet => 5,
            AmmoKind.CamoBullet => 6,
            _ => 0
        };
        if (pushTargetAmmoDelta != 0 && weaponAmmoType > 0)
        {
            int current = target.GetAmmoByType(weaponAmmoType);
            long maximum = pushTargetAmmoCanOverMax
                ? int.MaxValue : Math.Max(0, target.GetAmmoMaxByType(weaponAmmoType));
            target.SetAmmoByType(weaponAmmoType,
                (int)Math.Clamp((long)current + pushTargetAmmoDelta, 0L, maximum));
        }
        target.UpdateHpLabel();
    }

    private void ApplyStaticPushEffects(Facility target)
    {
        if (target == null || !GodotObject.IsInstanceValid(target) || target.health <= 0) return;
        if (pushTargetMaxHealthDelta != 0)
            target.maxHealth = (int)Math.Clamp((long)target.maxHealth + pushTargetMaxHealthDelta, 1L, int.MaxValue);

        if (!pushTargetCanOverMaxHealth)
            target.health = Math.Min(target.health, target.maxHealth);
        if (pushLockTargetToOneHp) target.health = 1;
        else if (pushTargetHealAmount > 0)
            target.health = (int)Math.Min(pushTargetCanOverMaxHealth ? int.MaxValue : target.maxHealth,
                Math.Min(int.MaxValue, (long)target.health + pushTargetHealAmount));
        else
        {
            int damage = (int)Math.Clamp((long)pushTargetDamage + pushRamDamage, 0L, int.MaxValue);
            if (damage > 0)
            {
                long remainingHealth = (long)target.health - damage;
                if (remainingHealth <= 0 && !pushDamageCanDestroy)
                    target.health = 1;
                else if (remainingHealth <= 0)
                {
                    target.health = 0;
                    var origin = target.ParentGrid;
                    if (origin != null && origin.city == target) origin.city = null;
                    target.QueueFree();
                    return;
                }
                else target.health = (int)remainingHealth;
            }
        }
        target.GetType().GetMethod("UpdateStatusBarUI",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic)?.Invoke(target, null);
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
        var eligibleTiles = new List<Grids>();
        var firstRule = GetPushChainRule(0);
        bool allowWeapons = firstRule?.targetWeapons ?? pushCanTargetWeapons;
        bool allowFacilities = firstRule?.targetFacilities ?? pushCanTargetFacilities;
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
                (allowWeapons && (tile.weapons?.Any(w => w != null && GodotObject.IsInstanceValid(w)
                    && w.pushableByPhysics && CanPushTeamName(w.team, w)) ?? false))
                || (allowFacilities && tile.city != null && tile.city.pushableByPhysics
                    && CanPushTeamName(tile.city.facilityTeam, tile.city));
            if ((stack.Count == 0 || !CanPushTargetTeam(stack)) && !hasPushableStatic)
                continue;
            eligibleTiles.Add(tile);
        }

        // At exactly one-tile target range, a single eligible target is unambiguous
        // and can be pushed immediately without opening the target-selection overlay.
        if (minRange == 1 && maxRange == 1 && eligibleTiles.Count == 1)
        {
            ExecuteSeparatePushOnGrid(eligibleTiles[0]);
            return;
        }

        foreach (var tile in eligibleTiles)
        {
            tile.attackRangeIcon?.Show();
            tile.IsInAttackRangeMode = true;
            gridManager.attackRange.Add(tile);
            tile.OnClickGrid = selected => ExecuteSeparatePushOnGrid(selected);
        }
        foreach (var tile in gridManager.grids)
            if (tile != null && !gridManager.attackRange.Contains(tile))
                tile.OnClickEmpty = CancelSeparatePushTargetSelection;
        if (eligibleTiles.Count == 0)
        {
            gridManager.HideAttackRange();
            gridManager.ClearAllEmptyCallbacks();
            gm.isSelectingAttackTarget = false;
            return;
        }
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

    private PushChainRule GetPushChainRule(int chainIndex)
    {
        if (chainIndex < 0 || pushChainRules == null || chainIndex >= pushChainRules.Count)
            return null;
        return pushChainRules[chainIndex];
    }

    private bool CanPushTargetTeam(List<Infantry> stack, int chainIndex = 0)
    {
        // A stack moves as one object, so every occupant must satisfy the rules for
        // this chain link. Missing rule entries inherit the initiating unit's exports.
        var rule = GetPushChainRule(chainIndex);
        bool allowInfantry = rule?.targetInfantry ?? pushCanTargetInfantry;
        if (!allowInfantry || stack == null || stack.Count == 0) return false;

        var whitelist = rule?.whitelist ?? pushTargetWhitelist;
        var blacklist = rule?.blacklist ?? pushTargetBlacklist;
        return stack.All(unit =>
        {
            string typeId = unit.UnitTypeId ?? unit.GetType().Name;
            if (whitelist != null && whitelist.Length > 0
                && !whitelist.Contains(typeId) && !whitelist.Contains(unit.GetType().Name))
                return false;
            if (blacklist != null && (blacklist.Contains(typeId)
                || blacklist.Contains(unit.GetType().Name)))
                return false;
            return rule == null || rule.teamPolicy == 0
                ? CanPushTeamRelation(unit.team)
                : CanPushTeamRelation(unit.team, rule);
        });
    }

    private bool CanPushTeamName(string targetTeam, Node target = null, int chainIndex = 0)
    {
        if (string.IsNullOrEmpty(targetTeam)) return false;
        var rule = GetPushChainRule(chainIndex);
        if (target is Weapon)
        {
            if (rule != null ? !rule.targetWeapons : !pushCanTargetWeapons) return false;
        }
        else if (target is Facility)
        {
            if (rule != null ? !rule.targetFacilities : !pushCanTargetFacilities) return false;
        }

        string targetType = target?.GetType().Name;
        var whitelist = rule?.whitelist ?? pushTargetWhitelist;
        var blacklist = rule?.blacklist ?? pushTargetBlacklist;
        if (!string.IsNullOrEmpty(targetType))
        {
            if (whitelist != null && whitelist.Length > 0 && !whitelist.Contains(targetType)) return false;
            if (blacklist != null && blacklist.Contains(targetType)) return false;
        }
        return rule == null || rule.teamPolicy == 0
            ? CanPushTeamRelation(targetTeam)
            : CanPushTeamRelation(targetTeam, rule);
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

    private bool CanPushTeamRelation(string targetTeam, PushChainRule rule)
    {
        bool sameTeam = targetTeam == team;
        bool allied = !sameTeam && TeamHelper.AreAllied(team, targetTeam);
        switch (rule.teamPolicy)
        {
            case 1: return !sameTeam && !allied;
            case 2: return sameTeam;
            case 3: return allied;
            case 4: return true;
            default:
                if (sameTeam) return rule.targetFriendlies;
                if (allied) return rule.targetAllies;
                return rule.targetEnemies;
        }
    }

    private Vector2I GetPushDirection(Grids targetGrid)
    {
        if (targetGrid == null || grid == null) return Vector2I.Zero;
        if (pushDirectionMode == PushDirectionMode.Fixed)
        {
            if (fixedPushDirection == Vector2I.Zero) return Vector2I.Zero;
            return new Vector2I(Math.Sign(fixedPushDirection.X), Math.Sign(fixedPushDirection.Y));
        }

        Vector2I delta = targetGrid.GridIndex - grid.GridIndex;
        return new Vector2I(Math.Sign(delta.X), Math.Sign(delta.Y));
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

    private static int GetMitigationDistanceForUnit(Infantry unit, int requestedDistance)
    {
        if (!IsLivingUnit(unit) || !unit.pushMitigationEnabled
            || (unit.pushMitigationAmmoCost > 0
                && !unit.HasAmmo(unit.pushMitigationAmmoKind, unit.pushMitigationAmmoCost)))
            return requestedDistance;
        int remaining = Mathf.Max(0, requestedDistance - Mathf.Max(0, unit.pushMitigationTiles));
        float percent = Mathf.Clamp(unit.pushMitigationPercent, 0f, 100f);
        return Mathf.Clamp(Mathf.FloorToInt(remaining * (1f - percent / 100f)),
            0, requestedDistance);
    }

    private int GetMitigatedPushDistance(List<Infantry> stack, int requestedDistance)
    {
        return stack.Where(IsLivingUnit)
            .Select(unit => GetMitigationDistanceForUnit(unit, requestedDistance))
            .DefaultIfEmpty(requestedDistance)
            .Min();
    }

    private static void PayMitigationAmmoCost(List<Infantry> stack,
        int requestedDistance, int resolvedDistance)
    {
        // The stack uses the strongest single mitigation result, so charge only
        // one module that actually produced that result, not every enabled module.
        var provider = stack.Where(IsLivingUnit)
            .FirstOrDefault(unit => GetMitigationDistanceForUnit(unit, requestedDistance) == resolvedDistance
                && resolvedDistance < requestedDistance);
        if (provider == null) return;
        int cost = Mathf.Max(0, provider.pushMitigationAmmoCost);
        if (cost > 0 && provider.HasAmmo(provider.pushMitigationAmmoKind, cost))
            provider.TryConsumeAmmo(provider.pushMitigationAmmoKind, cost);
    }

    // Rebound and transfer compete for the same stack displacement. Resolve the
    // conflict by effective weight, then HP ratio, then team-funds ratio. Only an
    // exact tie is randomized; if the preferred module cannot legally execute, try
    // the other one without charging resources for the failed attempt.
    private bool TryResolveStackDisplacement(List<Infantry> stack, Grids origin,
        Vector2I pushDirection, GridManager gridManager, GameManager gm)
    {
        var candidates = new List<(Infantry unit, bool rebound)>();
        foreach (var unit in stack.Where(IsLivingUnit))
        {
            if (unit.pushReboundEnabled) candidates.Add((unit, true));
            if (unit.pushTransferEnabled && unit.pushTransferDirection != Vector2I.Zero)
                candidates.Add((unit, false));
        }

        // Resolve every eligible module, not just the first one in the stack. Weight,
        // HP ratio, and funds ratio decide priority; exact ties are randomized.
        while (candidates.Count > 0)
        {
            int bestIndex = 0;
            for (int i = 1; i < candidates.Count; i++)
            {
                int priority = CompareDisplacementPriority(
                    candidates[i].unit, candidates[bestIndex].unit, gm);
                if (priority > 0 || (priority == 0 && GD.Randf() < 0.5f))
                    bestIndex = i;
            }

            var candidate = candidates[bestIndex];
            candidates.RemoveAt(bestIndex);
            bool resolved = candidate.rebound
                ? TryReboundStack(stack, origin, pushDirection, gm, candidate.unit)
                : TryTransferStack(stack, pushDirection, gridManager, gm, candidate.unit);
            if (resolved) return true;
        }
        return false;
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
        if (rebounder == null || origin == null || gm?.gridManager == null)
            return false;

        bool sameTeam = string.Equals(rebounder.team, team, StringComparison.OrdinalIgnoreCase);
        bool alliedTeam = !sameTeam && TeamHelper.AreAllied(rebounder.team, team);
        if (sameTeam && !rebounder.pushReboundCanTargetFriendlies) return false;
        if (alliedTeam && !rebounder.pushReboundCanTargetAllies) return false;
        if (!sameTeam && !alliedTeam && !rebounder.pushReboundCanTargetEnemies) return false;

        var initiatingStack = GetLivingStack(grid);
        if (initiatingStack.Count == 0) initiatingStack.Add(this);
        if (pushUsesWeight)
        {
            float reboundWeight = GetStackWeight(stack);
            float initiatingWeight = GetStackWeight(initiatingStack);
            if (reboundWeight < initiatingWeight
                && !Mathf.IsEqualApprox(reboundWeight, initiatingWeight))
                return false;
            if (Mathf.IsEqualApprox(reboundWeight, initiatingWeight))
            {
                bool reboundAllows = StackAllowsSimultaneous(stack);
                bool initiatingAllows = StackAllowsSimultaneous(initiatingStack);
                bool reboundStops = StackStopsOnEqual(stack);
                bool initiatingStops = StackStopsOnEqual(initiatingStack);
                if (!(reboundAllows && initiatingAllows))
                {
                    if (reboundStops && initiatingStops) return false;
                    if (GD.Randf() >= 0.5f) return false;
                }
            }
        }

        int ammoCost = Mathf.Max(0, rebounder.pushReboundAmmoCost);
        int healthCost = Mathf.Max(0, rebounder.pushReboundHealthCost);
        int fuelCost = Mathf.Max(0, rebounder.pushReboundFuelCost);
        int fundsCost = Mathf.Max(0, rebounder.pushReboundFundsCost);
        if (rebounder.GetAmmo(rebounder.pushReboundAmmoKind) < ammoCost
            || rebounder.health <= healthCost || rebounder.fuel < fuelCost
            || (fundsCost > 0 && gm.GetTeamFunds(rebounder.team) < fundsCost))
            return false;

        var reboundDestination = origin;
        List<Infantry> pusherStack = null;
        Grids pusherOrigin = null;
        Grids pusherDestination = null;
        bool destroyPusher = false;

        if (!rebounder.pushReboundToOrigin)
        {
            // A direct rebound enters the pusher's old tile, so the pusher must
            // retreat one tile first. A blocked retreat makes the rebound fail.
            pusherOrigin = grid;
            if (pusherOrigin == null) return false;
            pusherStack = initiatingStack;
            pusherDestination = gm.gridManager.GetGridAt(pusherOrigin.GridIndex - direction);
            if (pusherDestination == null
                || GetLivingStack(pusherDestination).Count > 0
                || HasForeignMultiTileOccupant(pusherDestination, pusherStack)
                || (pusherDestination.city != null && pusherDestination.city.pushBlocksInfantryWhenOverlapped)
                || (pusherDestination.weapons?.Any(w => w != null && GodotObject.IsInstanceValid(w)
                    && w.pushBlocksInfantryWhenOverlapped) ?? false))
                return false;

            destroyPusher = pusherStack.Any(unit => unit.GetMoveCostOnGrid(pusherDestination) >= 999
                || (pushDestroyOnProperty && pusherDestination.city != null));
            reboundDestination = pusherOrigin;
        }

        var destinationOccupants = GetLivingStack(reboundDestination);
        if (destinationOccupants.Any(unit => pusherStack == null || !pusherStack.Contains(unit))
            || (reboundDestination.city != null && reboundDestination.city.pushBlocksInfantryWhenOverlapped)
            || (reboundDestination.weapons?.Any(w => w != null && GodotObject.IsInstanceValid(w)
                && w.pushBlocksInfantryWhenOverlapped) ?? false))
            return false;

        // All destination and cost checks have passed. Only now consume resources.
        if (ammoCost > 0) rebounder.TryConsumeAmmo(rebounder.pushReboundAmmoKind, ammoCost);
        rebounder.health -= healthCost;
        rebounder.fuel -= fuelCost;
        if (fundsCost > 0)
            gm.SetTeamFunds(rebounder.team, gm.GetTeamFunds(rebounder.team) - fundsCost);
        if (healthCost > 0) rebounder.UpdateHpLabel();

        if (pusherStack != null)
        {
            if (destroyPusher)
                foreach (var unit in pusherStack.ToList()) DestroyPushedUnit(unit, gm);
            else
                MoveStackByPush(pusherStack, pusherDestination);
        }
        foreach (var unit in stack.ToList())
        {
            if (IsLivingUnit(unit) && (unit.GetMoveCostOnGrid(reboundDestination) >= 999
                || (pushDestroyOnProperty && reboundDestination.city != null)))
                DestroyPushedUnit(unit, gm);
        }
        stack = stack.Where(IsLivingUnit).ToList();
        if (stack.Count > 0) MoveStackByPush(stack, reboundDestination);

        // Rebound impact is applied to the initiating pusher after the reverse
        // displacement, using the rebounder's own Export settings.
        if (pusherStack != null && !destroyPusher)
        {
            foreach (var unit in pusherStack.Where(IsLivingUnit).ToList())
            {
                if (rebounder.pushReboundTargetLockOneHp)
                    unit.health = 1;
                else if (rebounder.pushReboundTargetDamage > 0)
                {
                    long remainingHealth = (long)unit.health - rebounder.pushReboundTargetDamage;
                    if (remainingHealth <= 0 && rebounder.pushReboundTargetCanDestroy)
                    {
                        DestroyPushedUnit(unit, gm);
                        continue;
                    }
                    unit.health = (int)Math.Max(1L, remainingHealth);
                }
                if (rebounder.pushReboundTargetFuelDelta != 0)
                    unit.fuel = (int)Math.Clamp((long)unit.fuel + rebounder.pushReboundTargetFuelDelta, 0L, int.MaxValue);
                if (rebounder.pushReboundTargetAmmoDelta != 0)
                    unit.AddAmmo(rebounder.pushReboundTargetAmmoKind, rebounder.pushReboundTargetAmmoDelta);
                unit.UpdateHpLabel();
            }
        }
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

        Vector2I direction = new Vector2I(
            Math.Sign(transferUnit.pushTransferDirection.X),
            Math.Sign(transferUnit.pushTransferDirection.Y));
        if (direction == Vector2I.Zero) return false;
        var origin = stack.FirstOrDefault(IsLivingUnit)?.grid;
        var destination = origin == null ? null : gridManager.GetGridAt(origin.GridIndex + direction);
        if (destination == null || GetLivingStack(destination).Count > 0
            || HasForeignMultiTileOccupant(destination, stack)
            || (destination.city != null && destination.city.pushBlocksInfantryWhenOverlapped)
            || (destination.weapons?.Any(w => w != null && GodotObject.IsInstanceValid(w)
                && w.pushBlocksInfantryWhenOverlapped) ?? false)
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
        foreach (var unit in stack.ToList())
        {
            if (IsLivingUnit(unit) && (unit.GetMoveCostOnGrid(destination) >= 999
                || (pushDestroyOnProperty && destination.city != null)))
                DestroyPushedUnit(unit, gm);
        }
        stack = stack.Where(IsLivingUnit).ToList();
        if (stack.Count > 0) MoveStackByPush(stack, destination);
        return true;
    }

    private void ApplyPushTargetEffects(List<Infantry> stack, GameManager gm)
    {
        foreach (var unit in stack.ToList())
        {
            if (!IsLivingUnit(unit)) continue;
            if (pushTargetMaxHealthDelta != 0)
                unit.maxHealth = (int)Math.Clamp((long)unit.maxHealth + pushTargetMaxHealthDelta, 1L, int.MaxValue);
            if (!pushTargetCanOverMaxHealth)
                unit.health = Math.Min(unit.health, unit.GetEffectiveMaxHealth());
            if (pushLockTargetToOneHp) unit.health = 1;
            else if (pushTargetHealAmount > 0)
                unit.health = (int)Math.Min(pushTargetCanOverMaxHealth ? int.MaxValue : unit.GetEffectiveMaxHealth(),
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
            {
                long fuelValue = (long)unit.fuel + pushTargetFuelDelta;
                long fuelMax = pushTargetFuelCanOverMax ? int.MaxValue : Math.Max(0, unit.maxFuel);
                unit.fuel = (int)Math.Clamp(fuelValue, 0L, fuelMax);
            }
            if (pushTargetAmmoDelta != 0)
            {
                long ammoValue = (long)unit.GetAmmo(pushTargetAmmoKind) + pushTargetAmmoDelta;
                long ammoMax = pushTargetAmmoCanOverMax ? int.MaxValue : Math.Max(0, unit.GetMaxAmmo(pushTargetAmmoKind));
                unit.SetAmmoRaw(pushTargetAmmoKind, (int)Math.Clamp(ammoValue, 0L, ammoMax));
            }
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
        if (!pushAutoDistance && selectedDistance == null && pushMinDistance < pushDistance)
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
        var firstRule = GetPushChainRule(0);
        bool staticTarget = !infantryTarget
            && ((firstRule?.targetWeapons ?? pushCanTargetWeapons)
                || (firstRule?.targetFacilities ?? pushCanTargetFacilities));
        if (stack.Count > 0 && !infantryTarget && !staticTarget) return;
        if (staticTarget)
        {
            bool allowWeapons = firstRule?.targetWeapons ?? pushCanTargetWeapons;
            bool allowFacilities = firstRule?.targetFacilities ?? pushCanTargetFacilities;
            var weapon = allowWeapons
                ? targetGrid.weapons?.FirstOrDefault(w => w != null
                    && GodotObject.IsInstanceValid(w) && w.pushableByPhysics
                    && CanPushTeamName(w.team, w))
                : null;
            var facility = allowFacilities && targetGrid.city != null && targetGrid.city.pushableByPhysics
                && CanPushTeamName(targetGrid.city.facilityTeam, targetGrid.city)
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
            if (stack.Count == 0 || !CanPushTargetTeam(stack, chain.Count)) break;
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
        if (pushChainWeightMode == PushChainWeightMode.Accumulate && pushChainMaxAccumulatedWeight > 0.0f)
            transmittedForce = Mathf.Min(transmittedForce, pushChainMaxAccumulatedWeight);
        bool pusherMovesSimultaneously = false;
        List<Infantry> linkPusherRules = pusherStack;
        for (int i = 0; i < chain.Count; i++)
        {
            if (!CanPushStackWithForce(linkPusherRules, transmittedForce, chain[i],
                out bool simultaneousMove))
                return false;
            if (i == 0) pusherMovesSimultaneously = simultaneousMove;

            // Only the initiating unit's Export controls force accumulation. Equal-
            // weight arbitration still compares the two adjacent stacks at each link.
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
            linkPusherRules = chain[i];
        }

        // Keep each stack's original tile so rebound is resolved once, after the
        // complete displacement. Target effects must also run once per push action,
        // not once for every tile traversed.
        var originalTiles = chain.Select(stack => stack.FirstOrDefault(IsLivingUnit)?.grid).ToList();
        var movedStacks = new HashSet<int>();
        bool pushResolved = false;
        var mitigatedDistances = chain.Select(stack => GetMitigatedPushDistance(stack, distance)).ToList();

        for (int step = 0; step < distance; step++)
        {
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                var stack = chain[i].Where(IsLivingUnit).ToList();
                if (stack.Count == 0) continue;
                int stackDistance = mitigatedDistances[i];
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
                    foreach (var unit in stack.ToList())
                    {
                        if (!unit.pushSelfDestructOnInvalidIntermediateTile) continue;
                        DestroyPushedUnit(unit, gameManager);
                        pushResolved = true;
                    }
                    break;
                }

                foreach (var unit in stack.ToList())
                {
                    bool invalidTerrain = unit.GetMoveCostOnGrid(destination) >= 999;
                    bool invalidProperty = pushDestroyOnProperty && destination.city != null;
                    if (invalidProperty || (invalidTerrain
                        && (unit.pushSelfDestructOnInvalidIntermediateTile || finalStepForStack)))
                    {
                        DestroyPushedUnit(unit, gameManager);
                        pushResolved = true;
                    }
                }
                stack = stack.Where(IsLivingUnit).ToList();
                if (stack.Count == 0) continue;

                // A non-chain infantry stack can still collide with the moving stack.
                // Weight decides the winner; equal weight follows both stacks' tie rules.
                var blockers = GetLivingStack(destination).Where(u => !stack.Contains(u)).ToList();
                if (blockers.Count > 0)
                {
                    // With domino chaining disabled, a blocked link stops both sides.
                    if (!pushChainEnabled || !pushUsesWeight)
                    {
                        ApplyFragileCollisionEffects(stack);
                        ApplyFragileCollisionEffects(blockers);
                        if (!stack.Any(IsLivingUnit)) pushResolved = true;
                        continue;
                    }
                    float movingWeight = GetStackWeight(stack);
                    float blockerWeight = GetStackWeight(blockers);
                    if (movingWeight > blockerWeight && !Mathf.IsEqualApprox(movingWeight, blockerWeight))
                    {
                        DestroyCollisionLoser(blockers);
                        ApplyFragileCollisionEffects(stack);
                        if (!stack.Any(IsLivingUnit)) pushResolved = true;
                    }
                    else if (blockerWeight > movingWeight && !Mathf.IsEqualApprox(movingWeight, blockerWeight))
                    {
                        DestroyCollisionLoser(stack);
                        ApplyFragileCollisionEffects(blockers);
                        pushResolved = true;
                        continue;
                    }
                    else
                    {
                        bool movingAllows = StackAllowsSimultaneous(stack);
                        bool blockerAllows = StackAllowsSimultaneous(blockers);
                        bool movingStops = StackStopsOnEqual(stack);
                        bool blockerStops = StackStopsOnEqual(blockers);
                        bool bothMove = movingAllows && blockerAllows
                            || !(movingStops && blockerStops) && GD.Randf() < 0.5f;
                        if (!bothMove)
                        {
                            ApplyFragileCollisionEffects(stack);
                            ApplyFragileCollisionEffects(blockers);
                            if (!stack.Any(IsLivingUnit)) pushResolved = true;
                            continue;
                        }

                        var collisionNext = gridManager.GetGridAt(destination.GridIndex + direction);
                        if (collisionNext == null || GetLivingStack(collisionNext).Count > 0
                            || HasForeignMultiTileOccupant(collisionNext, blockers)
                            || !TryDisplaceWeaponAndFacility(blockers, collisionNext, direction, gridManager, i))
                        {
                            ApplyFragileCollisionEffects(stack);
                            ApplyFragileCollisionEffects(blockers);
                            if (!stack.Any(IsLivingUnit)) pushResolved = true;
                            continue;
                        }

                        foreach (var unit in blockers.ToList())
                        {
                            if (unit.GetMoveCostOnGrid(collisionNext) >= 999)
                                DestroyPushedUnit(unit, gameManager);
                        }
                        blockers = blockers.Where(IsLivingUnit).ToList();
                        if (blockers.Count > 0)
                            MoveStackByPush(blockers, collisionNext);
                        ApplyFragileCollisionEffects(blockers);
                        ApplyFragileCollisionEffects(stack);
                    }
                }

                if (!stack.Any(IsLivingUnit))
                {
                    pushResolved = true;
                    continue;
                }
                if (HasForeignMultiTileOccupant(destination, stack)) continue;
                if (!TryDisplaceWeaponAndFacility(stack, destination, direction, gridManager, i)) continue;
                if (!stack.Any(IsLivingUnit))
                {
                    pushResolved = true;
                    continue;
                }

                MoveStackByPush(stack, destination);
                if (stack.Any(IsLivingUnit))
                {
                    movedStacks.Add(i);
                    pushResolved = true;
                }
            }
        }

        // Charge each active mitigation module once, and only for stacks that
        // actually moved at least one tile.
        foreach (int i in movedStacks)
        {
            if (mitigatedDistances[i] < distance)
                PayMitigationAmmoCost(chain[i], distance, mitigatedDistances[i]);
        }

        // Resolve per-stack post-push modules once, after all chain movement is complete.
        foreach (int i in movedStacks.OrderByDescending(index => index))
        {
            var stack = chain[i].Where(IsLivingUnit).ToList();
            if (stack.Count == 0) continue;
            var origin = originalTiles[i];
            TryResolveStackDisplacement(stack, origin, direction, gridManager, gameManager);
            ApplyPushTargetEffects(stack, gameManager);
        }

        if (!fromMovement && pushDirectionMode == PushDirectionMode.OppositeSides && pushResolved)
        {
            MovePusherOppositeDirection(direction, distance, gridManager, gameManager);
        }
        else if (!fromMovement && pushResolved && (pushPusherFollowsChain || pusherMovesSimultaneously)
            && firstGrid != null && grid != null)
        {
            var followDestination = firstGrid;
            var followStack = GetLivingStack(grid);
            if (followStack.Count == 0) followStack.Add(this);
            bool hasBlocker = GetLivingStack(followDestination).Count > 0
                || HasForeignMultiTileOccupant(followDestination, followStack);
            if (!hasBlocker && TryDisplaceWeaponAndFacility(followStack, followDestination,
                direction, gridManager))
            {
                foreach (var unit in followStack.ToList())
                {
                    if (unit.GetMoveCostOnGrid(followDestination) >= 999
                        || (pushDestroyOnProperty && followDestination.city != null))
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
        return pushResolved || movedStacks.Count > 0;
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
                foreach (var unit in pusherStack.ToList())
                    if (unit.pushSelfDestructOnInvalidIntermediateTile)
                        DestroyPushedUnit(unit, gameManager);
                break;
            }

            bool finalStep = step == distance - 1;
            foreach (var unit in pusherStack.ToList())
            {
                bool invalidTerrain = unit.GetMoveCostOnGrid(destination) >= 999;
                bool invalidProperty = pushDestroyOnProperty && destination.city != null;
                if (invalidProperty || (invalidTerrain
                    && (unit.pushSelfDestructOnInvalidIntermediateTile || finalStep)))
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
        Grids destination, Vector2I direction, GridManager gridManager, int chainIndex = 0)
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
        // Multi-tile weapons can be pushed as direct targets with full-footprint
        // validation, but a mixed infantry/static chain needs a footprint-aware
        // domino solver before it can safely move one as an intermediate obstacle.
        if (blockingWeapons.Any(w => w.isMultiTile)) return false;
        if (blockingWeapons.Count == 0 && blockingFacility == null) return true;
        if (nextGrid == null) return false;

        Weapon blockingWeapon = blockingWeapons.FirstOrDefault();
        if (blockingWeapon != null)
        {
            if (!CanPushTeamName(blockingWeapon.team, blockingWeapon, chainIndex))
                return false;
            float obstacleWeight = blockingWeapon.GetEffectiveWeight();
            if (pushUsesWeight && movingWeight < obstacleWeight
                && !Mathf.IsEqualApprox(movingWeight, obstacleWeight))
            {
                DestroyCollisionLoser(movingStack);
                return true;
            }
            if (!blockingWeapon.pushableByPhysics) return false;
            if (pushUsesWeight && Mathf.IsEqualApprox(movingWeight, obstacleWeight)
                && !CanResolveEqualWeight(movingStack, blockingWeapon.equalWeightPushMode)) return false;

            if (!TryResolveWeaponFootprintCollisions(blockingWeapon, nextGrid, direction,
                GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager,
                out bool weaponDestroyed))
            {
                if (!weaponDestroyed) return false;
            }
            if (weaponDestroyed) blockingWeapon = null;
        }

        if (blockingFacility != null)
        {
            if (!CanPushTeamName(blockingFacility.facilityTeam, blockingFacility, chainIndex))
                return false;
            float obstacleWeight = blockingFacility.GetEffectiveWeight();
            if (pushUsesWeight && movingWeight < obstacleWeight
                && !Mathf.IsEqualApprox(movingWeight, obstacleWeight))
            {
                DestroyCollisionLoser(movingStack);
                return true;
            }
            if (!blockingFacility.pushableByPhysics) return false;
            if (pushUsesWeight && Mathf.IsEqualApprox(movingWeight, obstacleWeight)
                && !CanResolveEqualWeight(movingStack, blockingFacility.equalWeightPushMode)) return false;

            if (!TryResolveStaticTargetCollision(nextGrid, direction, null, blockingFacility,
                GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager,
                out bool facilityDestroyed))
            {
                if (!facilityDestroyed) return false;
            }
            if (facilityDestroyed) blockingFacility = null;
        }

        if (blockingWeapon == null && blockingFacility == null) return true;
        if (!CanMoveStaticTargetTo(nextGrid, blockingWeapon, blockingFacility)) return false;

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

        if (blockingWeapon != null || blockingFacility != null)
            ApplyFragileCollisionEffects(movingStack);
        return true;
    }

    private void DestroyCollisionLoser(List<Infantry> losingStack)
    {
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        foreach (var unit in losingStack.ToList())
            DestroyPushedUnit(unit, gm);
    }

    private void ApplyFragileCollisionEffects(List<Infantry> survivingStack)
    {
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        foreach (var unit in survivingStack.Where(IsLivingUnit).ToList())
        {
            if (!unit.pushFragileEnabled) continue;
            int fragileDamage = unit.pushFragileDamageMode switch
            {
                PushFragileDamageMode.AttackCalculation =>
                    Math.Max(0, CalculateFinalDamage(unit, WeaponType.Primary)),
                PushFragileDamageMode.HealthAndFunds =>
                    CalculateFragileHealthFundsDamage(unit, gm),
                _ => Math.Max(0, unit.pushFragileDamage)
            };
            if (unit.pushFragileLockOneHp) unit.health = 1;
            else if (fragileDamage > 0)
            {
                long remainingHealth = (long)unit.health - fragileDamage;
                if (remainingHealth <= 0 && unit.pushFragileCanDestroy)
                {
                    DestroyPushedUnit(unit, gm);
                    continue;
                }
                unit.health = (int)Math.Max(1L, remainingHealth);
            }
            if (unit.pushFragileFuelDelta != 0)
                unit.fuel = (int)Math.Clamp((long)unit.fuel + unit.pushFragileFuelDelta, 0L, int.MaxValue);
            if (unit.pushFragileAmmoDelta != 0)
                unit.AddAmmo(unit.pushFragileAmmoKind, unit.pushFragileAmmoDelta);
            unit.UpdateHpLabel();
        }
    }

    private int CalculateFragileHealthFundsDamage(Infantry survivor, GameManager gm)
    {
        float healthPart = Math.Max(0.0f, (health + survivor.health) * survivor.pushFragileHealthScale);
        double fundsPart = 0.0;
        if (gm != null)
        {
            long ownFunds = Math.Max(0, gm.GetTeamFunds(team));
            long targetFunds = Math.Max(0, gm.GetTeamFunds(survivor.team));
            int divisor = Math.Max(1, survivor.pushFragileFundsPerDamage);
            fundsPart = ((double)ownFunds + targetFunds) / 2.0 / divisor;
        }
        return (int)Math.Clamp(Math.Round(healthPart + fundsPart), 0.0, int.MaxValue);
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
        return CanPushStackWithForce(pusher, GetStackWeight(pusher) * forceMultiplier, target, out _);
    }

    private bool CanPushStackWithForce(List<Infantry> pusherRules, float pusherWeight,
        List<Infantry> target, out bool simultaneousMove)
    {
        simultaneousMove = false;
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
        if (pusherAllows && targetAllows)
        {
            simultaneousMove = true;
            return true;
        }
        if (pusherStops && targetStops) return false;
        simultaneousMove = GD.Randf() < 0.5f;
        return simultaneousMove;
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
