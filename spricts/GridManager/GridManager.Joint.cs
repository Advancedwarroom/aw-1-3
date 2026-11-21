using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class GridManager
{
    private readonly Dictionary<Infantry, Grids> jointMoveOrigins = new();
    private readonly Dictionary<Infantry, int> jointMovePointsBefore = new();
    private readonly Dictionary<Infantry, int> jointFuelBefore = new();
    private readonly Dictionary<Infantry, int> jointGroupIdsBefore = new();

    private void RememberJointMove(Infantry unit)
    {
        if (unit == null || jointMoveOrigins.ContainsKey(unit)) return;
        jointMoveOrigins[unit] = unit.grid;
        jointMovePointsBefore[unit] = unit.movePoints;
        jointFuelBefore[unit] = unit.fuel;
    }

    public bool RollbackJointMoves(Infantry selected)
    {
        if (selected == null || !jointMoveOrigins.ContainsKey(selected)) return false;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        foreach (var pair in jointMoveOrigins.ToList())
        {
            var unit = pair.Key;
            var origin = pair.Value;
            if (unit == null || !IsInstanceValid(unit) || origin == null) continue;
            var current = unit.grid;
            if (current != null)
            {
                current.infantries.Remove(unit);
                if (current.infantry == unit) current.infantry = current.infantries.Count > 0 ? current.infantries[0] : null;
                gm?.RefreshJoinVisuals(current);
            }
            unit.grid = origin;
            if (!origin.infantries.Contains(unit)) origin.infantries.Add(unit);
            origin.infantry ??= unit;
            unit.Position = origin.Position;
            unit.movePoints = jointMovePointsBefore.GetValueOrDefault(unit, unit.movePoints);
            unit.fuel = jointFuelBefore.GetValueOrDefault(unit, unit.fuel);
            unit.originalGrid = null;
            unit.state = UnitState.Idle;
            unit.isMoved = false;
            unit.isAttacked = false;
            unit.SetWaitVisual(false);
        }
        foreach (var pair in jointGroupIdsBefore)
            if (pair.Key != null && IsInstanceValid(pair.Key)) pair.Key.joinGroupId = pair.Value;
        jointMoveOrigins.Clear();
        jointMovePointsBefore.Clear();
        jointFuelBefore.Clear();
        jointGroupIdsBefore.Clear();
        CloseRange();
        ClearAllEmptyCallbacks();
        var menu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
        menu?.Hide();
        gm?.fogOfWarManager?.OnUnitMoved();
        gm?.RefreshAllJoinVisuals();
        return true;
    }

    private int JointMoveAllowance(Infantry unit)
    {
        int allowance = unit.movePoints + CoModuleSystem.GetMovementBonus(unit);
        if (!unit.canMoveWithoutFuel && unit.fuelBar >= 0) allowance = Mathf.Min(allowance, unit.fuelBar);
        return allowance;
    }

    public bool CanJointMemberReach(Infantry unit, Grids target)
    {
        if (unit == null || target == null || unit.grid == null || unit.isAttacked || unit.state == UnitState.Acted) return false;
        if (unit.grid == target) return true;
        if (!GetMoveRange(unit, JointMoveAllowance(unit)).Contains(target)) return false;
        return CanStayOnGrid(unit, target);
    }

    public void AddJointMoveRanges(Infantry selected)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || selected.joinGroupId <= 0) return;
        foreach (var member in gm.GetJoinedMembers(selected))
        {
            if (member == selected) continue;
            foreach (var grid in GetMoveRange(member, JointMoveAllowance(member)))
                if (!moveRange.Contains(grid)) moveRange.Add(grid);
        }
    }

    private Grids FurthestJointGrid(Infantry unit, Grids destination)
    {
        var path = BuildPath(unit, destination, ignoreMovePoints: true);
        var reachable = GetMoveRange(unit, JointMoveAllowance(unit));
        Grids result = null;
        foreach (var grid in path)
        {
            if (grid == unit.grid || !reachable.Contains(grid) || !CanStayOnGrid(unit, grid)) continue;
            result = grid;
        }
        return result;
    }

    public bool HandleJointMoveClick(Infantry selected, Grids destination)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || selected == null || destination == null || selected.grid == destination) return false;
        var members = gm.GetJoinedMembers(selected);
        if (members.Count <= 1) return false;
        var movers = members.Where(m => CanJointMemberReach(m, destination)).ToList();
        if (movers.Count == 0) return false;
        bool wholeFormation = movers.Count == members.Count;
        if (!wholeFormation && movers.Any(m => !m.canSplitFromJoin)) return false;
        var stayers = members.Where(m => !movers.Contains(m)).ToList();

        jointMoveOrigins.Clear();
        jointMovePointsBefore.Clear();
        jointFuelBefore.Clear();
        jointGroupIdsBefore.Clear();
        foreach (var member in members) jointGroupIdsBefore[member] = member.joinGroupId;
        foreach (var mover in movers) RememberJointMove(mover);
        bool oldWalkAnim = enableWalkAnim;
        enableWalkAnim = false;
        if (!wholeFormation)
        {
            int movingId = movers.Count > 1 ? gm.NextJoinGroupId() : 0;
            foreach (var mover in movers) mover.joinGroupId = movingId;
            if (stayers.Count == 1) stayers[0].joinGroupId = 0;
        }

        // 慢速留守成员先按自己的 Export 原地或走到最远可达格。
        foreach (var stayer in stayers)
        {
            if (stayer.jointRemainderMoveMode == JointRemainderMoveMode.FurthestReachable)
            {
                var fallback = FurthestJointGrid(stayer, destination);
                if (fallback != null)
                {
                    RememberJointMove(stayer);
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
            jointMoveOrigins[mover] = mover.grid;
            bool showMenu = mover == menuUnit;
            MoveTo(mover, destination, showMenu: showMenu, preserveAction: true);
        }
        gm.selectedInfantry = menuUnit;
        gm.RefreshAllJoinVisuals();
        enableWalkAnim = oldWalkAnim;
        return true;
    }

    public void AddJointAttackRanges(Infantry selected)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || selected.joinGroupId <= 0) return;
        foreach (var member in gm.GetJoinedMembers(selected))
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
