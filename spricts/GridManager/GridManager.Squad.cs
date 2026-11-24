using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class GridManager
{
    private readonly Dictionary<Infantry, Grids> squadMoveOrigins = new();
    private readonly Dictionary<Infantry, int> squadMovePointsBefore = new();
    private readonly Dictionary<Infantry, int> squadFuelBefore = new();
    private readonly Dictionary<Infantry, int> squadGroupIdsBefore = new();

    private void RememberSquadMove(Infantry unit)
    {
        if (unit == null || squadMoveOrigins.ContainsKey(unit)) return;
        squadMoveOrigins[unit] = unit.grid;
        squadMovePointsBefore[unit] = unit.movePoints;
        squadFuelBefore[unit] = unit.fuel;
    }

    public bool RollbackSquadMoves(Infantry selected)
    {
        if (selected == null || !squadMoveOrigins.ContainsKey(selected)) return false;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        foreach (var pair in squadMoveOrigins.ToList())
        {
            var unit = pair.Key;
            var origin = pair.Value;
            if (unit == null || !IsInstanceValid(unit) || origin == null) continue;
            var current = unit.grid;
            if (current != null)
            {
                current.infantries.Remove(unit);
                if (current.infantry == unit) current.infantry = current.infantries.Count > 0 ? current.infantries[0] : null;
                gm?.RefreshSquadVisuals(current);
            }
            unit.grid = origin;
            if (!origin.infantries.Contains(unit)) origin.infantries.Add(unit);
            origin.infantry ??= unit;
            unit.Position = origin.Position;
            unit.movePoints = squadMovePointsBefore.GetValueOrDefault(unit, unit.movePoints);
            unit.fuel = squadFuelBefore.GetValueOrDefault(unit, unit.fuel);
            unit.originalGrid = null;
            unit.state = UnitState.Idle;
            unit.isMoved = false;
            unit.isAttacked = false;
            unit.SetWaitVisual(false);
        }
        foreach (var pair in squadGroupIdsBefore)
            if (pair.Key != null && IsInstanceValid(pair.Key)) pair.Key.squadId = pair.Value;
        squadMoveOrigins.Clear();
        squadMovePointsBefore.Clear();
        squadFuelBefore.Clear();
        squadGroupIdsBefore.Clear();
        CloseRange();
        ClearAllEmptyCallbacks();
        var menu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
        menu?.Hide();
        gm?.fogOfWarManager?.OnUnitMoved();
        gm?.RefreshAllSquadVisuals();
        return true;
    }

    private int SquadMoveAllowance(Infantry unit)
    {
        int allowance = unit.movePoints + CoModuleSystem.GetMovementBonus(unit);
        if (unit.consumeFuel && unit.fuel >= 0) allowance = Mathf.Min(allowance, unit.fuel);
        return allowance;
    }

    public bool CanSquadMemberReach(Infantry unit, Grids target)
    {
        if (unit == null || target == null || unit.grid == null || unit.isAttacked || unit.state == UnitState.Acted) return false;
        if (unit.grid == target) return true;
        if (!GetMoveRange(unit, SquadMoveAllowance(unit)).Contains(target)) return false;
        return CanStayOnGrid(target, unit);
    }

    public void AddSquadMoveRanges(Infantry selected)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || selected.squadId <= 0) return;
        foreach (var member in gm.GetSquadMembers(selected))
        {
            if (member == selected) continue;
            foreach (var grid in GetMoveRange(member, SquadMoveAllowance(member)))
                if (!moveRange.Contains(grid)) moveRange.Add(grid);
        }
    }

    private Grids FurthestSquadGrid(Infantry unit, Grids destination)
    {
        var path = BuildPath(unit.grid, destination, unit, ignoreMovePoints: true);
        var reachable = GetMoveRange(unit, SquadMoveAllowance(unit));
        Grids result = null;
        foreach (var grid in path)
        {
            if (grid == unit.grid || !reachable.Contains(grid) || !CanStayOnGrid(grid, unit)) continue;
            result = grid;
        }
        return result;
    }

    public bool HandleSquadMoveClick(Infantry selected, Grids destination)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || selected == null || destination == null || selected.grid == destination) return false;
        var members = gm.GetSquadMembers(selected);
        if (members.Count <= 1) return false;
        var movers = members.Where(m => CanSquadMemberReach(m, destination)).ToList();
        if (movers.Count == 0) return false;
        bool wholeFormation = movers.Count == members.Count;
        if (!wholeFormation && movers.Any(m => !m.canSplitFromSquad)) return false;
        var stayers = members.Where(m => !movers.Contains(m)).ToList();

        squadMoveOrigins.Clear();
        squadMovePointsBefore.Clear();
        squadFuelBefore.Clear();
        squadGroupIdsBefore.Clear();
        foreach (var member in members) squadGroupIdsBefore[member] = member.squadId;
        foreach (var mover in movers) RememberSquadMove(mover);
        bool oldWalkAnim = enableWalkAnim;
        enableWalkAnim = false;
        if (!wholeFormation)
        {
            int movingId = movers.Count > 1 ? gm.NextSquadId() : 0;
            foreach (var mover in movers) mover.squadId = movingId;
            if (stayers.Count == 1) stayers[0].squadId = 0;
        }

        // 慢速留守成员先按自己的 Export 原地或走到最远可达格。
        foreach (var stayer in stayers)
        {
            if (stayer.squadRemainderMoveMode == SquadRemainderMoveMode.FurthestReachable)
            {
                var fallback = FurthestSquadGrid(stayer, destination);
                if (fallback != null)
                {
                    RememberSquadMove(stayer);
                    MoveTo(stayer, fallback, showMenu: false, preserveAction: true);
                    continue;
                }
            }
            stayer.state = UnitState.Idle;
            stayer.originalGrid = null;
            stayer.actionMenu?.Hide();
        }

        var menuUnit = movers.Contains(selected) ? selected : movers.Last();
        foreach (var mover in movers)
        {
            if (!IsInstanceValid(mover) || mover.grid == destination) continue;
            squadMoveOrigins[mover] = mover.grid;
            bool showMenu = mover == menuUnit;
            MoveTo(mover, destination, showMenu: showMenu, preserveAction: true);
        }
        gm.selectedInfantry = menuUnit;
        gm.RefreshAllSquadVisuals();
        enableWalkAnim = oldWalkAnim;
        return true;
    }

    public void AddSquadAttackRanges(Infantry selected)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || selected.squadId <= 0) return;
        foreach (var member in gm.GetSquadMembers(selected))
        {
            if (member == selected || member.isAttacked || member.attackType == AttackType.NoAttack) continue;
            foreach (var grid in grids)
            {
                if (grid == null || attackRange.Contains(grid)) continue;
                bool legal = grid.infantries.Any(target => target != null && IsInstanceValid(target)
                    && Infantry.CanTargetStealth(member, target) && member.IsTargetInAttackRange(target)
                    && member.SelectWeaponByMatrix(target) != WeaponType.None
                    && TeamHelper.IsEnemyForAttacker(member.team, target.team, member.canAttackFriendlies, member.canAttackAllies));
                if (!legal) continue;
                attackRange.Add(grid);
                grid.attackRangeIcon?.Show();
                grid.IsInAttackRangeMode = true;
                var area2D = grid.GetNodeOrNull<Area2D>("Area2D");
                if (area2D != null) { area2D.ZIndex = 10; area2D.InputPickable = true; }
                grid.OnClickGrid = to => OnAttackGridClicked(selected, to);
                grid.OnMouseEnteredAttackGrid = () => ShowDamagePreview(selected, grid);
                grid.OnMouseExitedAttackGrid = HideDamagePreview;
            }
        }
    }
}
