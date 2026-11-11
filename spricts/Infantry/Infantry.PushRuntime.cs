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

        Vector2I delta = target.grid.GridIndex - grid.GridIndex;
        Vector2I direction = Math.Abs(delta.X) >= Math.Abs(delta.Y)
            ? new Vector2I(Math.Sign(delta.X), 0)
            : new Vector2I(0, Math.Sign(delta.Y));
        if (direction == Vector2I.Zero) return;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null) return;
        var initialStack = GetLivingStack(target.grid);
        if (!CanPushTargetTeam(initialStack)) return;
        ResolvePushChain(target.grid, direction, pushDistance, gm.gridManager, gm);
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
            if (stack.Count == 0 || !CanPushTargetTeam(stack)) continue;
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
        foreach (var unit in stack)
        {
            if (unit.team == team && !pushCanTargetFriendlies) continue;
            if (TeamHelper.AreAllied(team, unit.team) && unit.team != team && !pushCanTargetAllies) continue;
            if (unit.team != team && !TeamHelper.AreAllied(team, unit.team)
                && pushCanTargetEnemies) return true;
            if (unit.team == team && pushCanTargetFriendlies) return true;
            if (TeamHelper.AreAllied(team, unit.team) && unit.team != team && pushCanTargetAllies) return true;
        }
        return false;
    }

    private void ExecuteSeparatePushOnGrid(Grids targetGrid)
    {
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var gridManager = gm?.gridManager;
        if (targetGrid == null || grid == null || gridManager == null) return;
        var stack = GetLivingStack(targetGrid);
        if (stack.Count == 0 || !CanPushTargetTeam(stack)) return;

        Vector2I delta = targetGrid.GridIndex - grid.GridIndex;
        Vector2I direction = Math.Abs(delta.X) >= Math.Abs(delta.Y)
            ? new Vector2I(Math.Sign(delta.X), 0)
            : new Vector2I(0, Math.Sign(delta.Y));
        if (direction == Vector2I.Zero || !CanPushStack(new List<Infantry> { this }, stack))
            return;

        if (!TryMarkPushActionUsed()) return;
        gridManager.HideAttackRange();
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
            if (!CanPushStack(pusherStack, chain[i]))
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
                    if (unit.GetMoveCostOnGrid(destination) >= 999)
                        DestroyPushedUnit(unit, gameManager);
                stack = stack.Where(IsLivingUnit).ToList();
                if (stack.Count == 0) continue;

                var blockers = GetLivingStack(destination).Where(u => !stack.Contains(u)).ToList();
                if (blockers.Count > 0)
                {
                    // A contiguous chain stack should already have moved this step.
                    // Any remaining occupant is an unrelated obstruction: stop this stack.
                    continue;
                }
                MoveStackByPush(stack, destination);
            }
        }

        gameManager?.fogOfWarManager?.OnUnitMoved();
        gameManager?.CallDeferred(nameof(GameManager.UpdateUnitLists));
        gameManager?.CallDeferred(nameof(GameManager.CheckVictoryCondition));
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

    private static bool CanPushStack(List<Infantry> pusher, List<Infantry> target)
    {
        float pusherWeight = GetStackWeight(pusher);
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
