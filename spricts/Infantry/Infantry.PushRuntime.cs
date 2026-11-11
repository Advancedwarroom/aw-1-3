using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Grid-based push resolution for infantry entities.
/// This deliberately bypasses normal movement costs and action-state transitions.
/// </summary>
public partial class Infantry
{
    /// <summary>Attack-triggered push. Direction is derived from attacker to the attacked grid.</summary>
    public void TryPushTargetOnAttack(Infantry target)
    {
        if (!pushEnabled || pushTriggerMode != PushTriggerMode.Attack
            || pushDistance <= 0 || grid == null || target == null
            || !GodotObject.IsInstanceValid(target) || target.grid == null)
            return;

        Vector2I delta = target.grid.GridIndex - grid.GridIndex;
        Vector2I direction;
        if (Math.Abs(delta.X) >= Math.Abs(delta.Y))
            direction = new Vector2I(Math.Sign(delta.X), 0);
        else
            direction = new Vector2I(0, Math.Sign(delta.Y));
        if (direction == Vector2I.Zero) return;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var gridManager = gm?.gridManager;
        if (gridManager?.map == null) return;

        ResolvePushChain(target, direction, pushDistance, gridManager, gm);
    }

    private void ResolvePushChain(Infantry first, Vector2I direction, int distance,
        GridManager gridManager, GameManager gameManager)
    {
        // Capture a contiguous one-unit-per-cell chain. Overlapping stacks are left untouched
        // until the stack-collision policy is implemented as its own configurable module.
        var chain = new List<Infantry>();
        var cursor = first?.grid;
        while (cursor != null)
        {
            cursor.infantries.RemoveAll(u => u == null || !GodotObject.IsInstanceValid(u)
                || u.IsQueuedForDeletion());
            var occupants = cursor.infantries.Where(u => u != null && u != this
                && GodotObject.IsInstanceValid(u) && !u.IsQueuedForDeletion()).ToList();
            if (occupants.Count == 0) break;
            if (occupants.Count != 1) return;
            var occupant = occupants[0];
            if (chain.Contains(occupant)) return;
            chain.Add(occupant);
            cursor = gridManager.GetGridAt(cursor.GridIndex + direction);
        }
        if (chain.Count == 0) return;

        // Decide weight links once per attack. A mixed equal-weight pair gets one 50/50
        // result for the entire push, rather than rerolling independently on each tile.
        int firstBlockedLink = -1;
        for (int i = 0; i < chain.Count; i++)
        {
            var pusher = i == 0 ? this : chain[i - 1];
            if (!CanPushByWeight(pusher, chain[i]))
            {
                firstBlockedLink = i;
                break;
            }
        }

        // Resolve each tile-step from the front backwards so a vacated cell is available
        // to the unit behind it during the same step. A blocked link also blocks the units
        // behind it, while units farther forward can still complete their own displacement.
        for (int step = 0; step < distance; step++)
        {
            for (int i = chain.Count - 1; i > firstBlockedLink; i--)
            {
                var unit = chain[i];
                if (unit == null || !GodotObject.IsInstanceValid(unit)
                    || unit.IsQueuedForDeletion() || unit.grid == null)
                    continue;

                var destination = gridManager.GetGridAt(unit.grid.GridIndex + direction);
                if (destination == null || unit.GetMoveCostOnGrid(destination) >= 999)
                {
                    DestroyPushedUnit(unit, gameManager);
                    continue;
                }

                // A non-chain occupant blocks this push step. Chain members are already
                // being processed front-to-back and will have vacated their cells.
                var blockers = destination.infantries.Where(other => other != unit
                    && GodotObject.IsInstanceValid(other) && !other.IsQueuedForDeletion()).ToList();
                if (blockers.Count > 0)
                {
                    var blocker = blockers[0];
                    if (!CanPushByWeight(unit, blocker)) continue;
                    // Do not recursively move an unrelated stack; its own push module
                    // must be explicitly part of the chain to participate.
                    if (!chain.Contains(blocker)) continue;
                }

                MoveUnitByPush(unit, destination);
            }
        }

        gameManager?.fogOfWarManager?.OnUnitMoved();
        gameManager?.CallDeferred(nameof(GameManager.UpdateUnitLists));
        gameManager?.CallDeferred(nameof(GameManager.CheckVictoryCondition));
    }

    private static bool CanPushByWeight(Infantry pusher, Infantry target)
    {
        float pusherWeight = pusher.GetEffectiveWeight();
        float targetWeight = target.GetEffectiveWeight();
        if (pusherWeight > targetWeight && !Mathf.IsEqualApprox(pusherWeight, targetWeight))
            return true;
        if (!Mathf.IsEqualApprox(pusherWeight, targetWeight))
            return false;

        if (pusher.equalWeightPushMode == EqualWeightPushMode.AllowSimultaneous
            && target.equalWeightPushMode == EqualWeightPushMode.AllowSimultaneous)
            return true;
        if (pusher.equalWeightPushMode != target.equalWeightPushMode)
            return GD.Randf() < 0.5f;
        return false;
    }

    private static void MoveUnitByPush(Infantry unit, Grids destination)
    {
        if (unit == null || destination == null || !GodotObject.IsInstanceValid(unit)) return;
        var origin = unit.grid;
        if (origin != null)
        {
            origin.infantries.Remove(unit);
            if (origin.infantry == unit)
                origin.infantry = origin.infantries.FirstOrDefault(u =>
                    u != null && GodotObject.IsInstanceValid(u) && u != unit);
        }

        unit.grid = destination;
        if (!destination.infantries.Contains(unit))
            destination.infantries.Add(unit);
        if (destination.infantry == null || unit.overlapType == UnitOverlapType.NonOverlapping)
            destination.infantry = unit;

        unit.Position = destination.Position;
        // Forced displacement is not a voluntary move: do not consume move points/fuel,
        // mark the target as acted, or open its action menu.
    }

    private static void DestroyPushedUnit(Infantry unit, GameManager gameManager)
    {
        if (unit == null || !GodotObject.IsInstanceValid(unit) || unit.IsQueuedForDeletion()) return;
        unit.health = 0;
        unit.UpdateHpLabel();
        unit.OnDestroyed();
        if (unit.grid != null)
        {
            unit.grid.infantries.Remove(unit);
            if (unit.grid.infantry == unit)
                unit.grid.infantry = unit.grid.infantries.FirstOrDefault(u =>
                    u != null && GodotObject.IsInstanceValid(u) && u != unit);
            unit.grid = null;
        }
        unit.ClearFootprint();
        unit.CallDeferred(Node.MethodName.QueueFree);
        gameManager?.RemovePiece(unit);
    }
}
