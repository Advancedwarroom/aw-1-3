using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// 广义 Join：编队只负责组织同格成员，不替换、不合并任何成员对象。
public enum JointDamageMode
{
    Independent = 0, // 每个合法成员分别完整承伤
    WeightedAftermath = 1, // 一次最终伤害，按原权重分配与顺延
    GentlePool = 2 // 合法成员共池，等比例变回
}

public enum JointRemainderMoveMode
{
    Stay = 0,
    FurthestReachable = 1
}

public partial class Infantry
{
    [ExportGroup("联合部队（广义 Join）")]
    [Export] public bool canJoinFormation = false;
    [Export] public Godot.Collections.Array<string> joinBlacklist = new();
    [Export] public JointDamageMode jointDamageMode = JointDamageMode.WeightedAftermath;
    [Export(PropertyHint.Range, "1,32,1")] public int joinMaxMembers = 2;
    [Export] public bool joinLimitAfterJoin = true;
    [Export(PropertyHint.Range, "0,999,1")] public int joinMinTargetHp = 0;
    // -1 表示不限制目标编队总显示血量。
    [Export(PropertyHint.Range, "-1,9999,1")] public int joinMaxTargetHp = -1;
    [Export] public bool canSplitFromJoin = true;
    [Export] public JointRemainderMoveMode jointRemainderMoveMode = JointRemainderMoveMode.Stay;
    // 运行时编组号也随 Export 保存；0 表示未参加广义 Join。
    [Export] public int joinGroupId = 0;
}

public partial class GameManager
{
    private static bool resolvingJointAttack;

    public bool IsJoined(Infantry unit)
        => unit != null && IsInstanceValid(unit) && unit.joinGroupId > 0 && GetJoinedMembers(unit).Count > 1;

    public List<Infantry> GetJoinedMembers(Infantry unit)
    {
        if (unit == null || !IsInstanceValid(unit)) return new List<Infantry>();
        if (unit.joinGroupId <= 0 || unit.grid == null) return new List<Infantry> { unit };
        return unit.grid.infantries
            .Where(u => u != null && IsInstanceValid(u) && u.team == unit.team && u.joinGroupId == unit.joinGroupId)
            .OrderByDescending(u => u.health).ToList();
    }

    public int NextJoinGroupId()
    {
        int maxId = 0;
        if (unitManager != null)
            foreach (var unit in unitManager.AllUnits)
                if (unit != null && IsInstanceValid(unit)) maxId = Math.Max(maxId, unit.joinGroupId);
        return maxId + 1;
    }

    private static bool Blacklists(Infantry unit, Infantry other)
    {
        if (unit?.joinBlacklist == null || other == null) return false;
        string typeName = other.GetType().Name;
        return unit.joinBlacklist.Any(x => !string.IsNullOrWhiteSpace(x) &&
            (string.Equals(x, typeName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(x, other.UnitTypeId, StringComparison.OrdinalIgnoreCase)));
    }

    // 支持与黑名单均双向检查，不能反向加入绕过拒绝。
    public bool CanPairJoin(Infantry a, Infantry b)
    {
        if (a == null || b == null || a == b || !IsInstanceValid(a) || !IsInstanceValid(b)) return false;
        if (!a.canJoinFormation || !b.canJoinFormation || a.team != b.team) return false;
        return !Blacklists(a, b) && !Blacklists(b, a);
    }

    public int GetJoinDisplayHp(Infantry unit)
    {
        return GetJoinedMembers(unit).Sum(u =>
        {
            float full = Mathf.Max(1f, u.maxHealth / 10f);
            return Mathf.Max(0, Mathf.CeilToInt(u.health / full));
        });
    }

    public int GetJoinDisplayHp(IEnumerable<Infantry> members)
    {
        return members.Where(u => u != null && IsInstanceValid(u)).Sum(u =>
        {
            float full = Mathf.Max(1f, u.maxHealth / 10f);
            return Mathf.Max(0, Mathf.CeilToInt(u.health / full));
        });
    }

    // 上限、判定时机、血量门全部只读“新加入者”自己的 Export。
    public bool CanJoinFormation(Infantry incoming, Infantry target)
    {
        var members = GetJoinedMembers(target);
        if (members.Count == 0 || members.Any(m => !CanPairJoin(incoming, m))) return false;
        int before = members.Count;
        int countToCheck = incoming.joinLimitAfterJoin ? before + 1 : before;
        if (countToCheck > Mathf.Max(1, incoming.joinMaxMembers)) return false;
        int hp = GetJoinDisplayHp(members);
        if (hp < incoming.joinMinTargetHp) return false;
        if (incoming.joinMaxTargetHp >= 0 && hp > incoming.joinMaxTargetHp) return false;
        return true;
    }

    public Infantry FindJoinTarget(Infantry incoming, Grids grid)
    {
        if (incoming == null || grid == null) return null;
        return grid.infantries.FirstOrDefault(u => u != incoming && CanJoinFormation(incoming, u));
    }

    public bool FormJoin(Infantry incoming, Infantry target)
    {
        if (!CanJoinFormation(incoming, target)) return false;
        int id = target.joinGroupId > 0 ? target.joinGroupId : NextJoinGroupId();
        foreach (var member in GetJoinedMembers(target)) member.joinGroupId = id;
        incoming.joinGroupId = id;
        RefreshJoinVisuals(target.grid);
        return true;
    }

    public void LeaveJoin(Infantry unit)
    {
        if (unit == null) return;
        var oldGrid = unit.grid;
        int oldId = unit.joinGroupId;
        unit.joinGroupId = 0;
        if (oldGrid != null && oldId > 0)
        {
            var rest = oldGrid.infantries.Where(u => u != unit && u.joinGroupId == oldId).ToList();
            if (rest.Count == 1) rest[0].joinGroupId = 0;
            RefreshJoinVisuals(oldGrid);
        }
    }

    public void RefreshJoinVisuals(Grids grid)
    {
        if (grid == null) return;
        foreach (var unit in grid.infantries.Where(u => u != null && IsInstanceValid(u)))
        {
            unit.Position = grid.Position;
            if (unit.joinGroupId > 0)
            {
                var members = GetJoinedMembers(unit);
                if (members.Count > 1 && members[0] == unit)
                {
                    for (int i = 0; i < members.Count; i++)
                    {
                        float x = (i - (members.Count - 1) * 0.5f) * 14f;
                        float y = (i % 2 == 0 ? 1f : -1f) * 5f;
                        members[i].Position = grid.Position + new Vector2(x, y);
                    }
                }
            }
            unit.UpdateHpLabel();
        }
    }

    public void RefreshAllJoinVisuals()
    {
        if (gridManager?.grids == null) return;
        foreach (var grid in gridManager.grids)
            if (grid != null && grid.infantries.Any(u => u != null && u.joinGroupId > 0)) RefreshJoinVisuals(grid);
    }

}
