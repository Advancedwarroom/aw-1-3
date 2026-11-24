using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// 广义 Join：编队只负责组织同格成员，不替换、不合并任何成员对象。
public enum SquadDamageMode
{
    Independent = 0, // 每个合法成员分别完整承伤
    WeightedAftermath = 1, // 一次最终伤害，按原权重分配与顺延
    GentlePool = 2 // 合法成员共池，等比例变回
}

public enum SquadRemainderMoveMode
{
    Stay = 0,
    FurthestReachable = 1
}

public partial class Infantry
{
    [ExportGroup("联合部队（广义 Join）")]
    [Export] public bool canFormSquad = false;
    [Export] public Godot.Collections.Array<string> squadBlacklist = new();
    [Export] public bool squadSameTypeAllowed = false;             // 同型对允许混编（默认false=同型归合流专属，开创人2026-11-24）
    [Export] public SquadDamageMode squadDamageMode = SquadDamageMode.WeightedAftermath;
    // 最大联合上限按编队中的单位数量计算，不按血量、占格数或兵种数计算。
    [Export(PropertyHint.Range, "1,32,1")] public int squadMaxMembers = 2;
    [Export] public bool squadLimitAfterJoin = true;
    [Export(PropertyHint.Range, "0,999,1")] public int squadMinTargetHp = 0;
    // -1 表示不限制目标编队总显示血量。
    [Export(PropertyHint.Range, "-1,9999,1")] public int squadMaxTargetHp = -1;
    [Export] public bool canSplitFromSquad = true;
    [Export] public SquadRemainderMoveMode squadRemainderMoveMode = SquadRemainderMoveMode.Stay;
    // 联合后只增减自身攻击百分比；默认 0，不设固定上下限，最终攻击倍率最低为 0。
    [Export] public float squadAttackPercent = 0f;
    // 运行时编组号也随 Export 保存；0 表示未参加广义 Join。
    [Export] public int squadId = 0;
}

public partial class GameManager
{
    private static bool resolvingSquadAttack;

    public bool IsSquadMember(Infantry unit)
        => unit != null && IsInstanceValid(unit) && unit.squadId > 0 && GetSquadMembers(unit).Count > 1;

    public List<Infantry> GetSquadMembers(Infantry unit)
    {
        if (unit == null || !IsInstanceValid(unit)) return new List<Infantry>();
        if (unit.squadId <= 0 || unit.grid == null) return new List<Infantry> { unit };
        return unit.grid.infantries
            .Where(u => u != null && IsInstanceValid(u) && u.team == unit.team && u.squadId == unit.squadId)
            .OrderByDescending(u => u.health).ToList();
    }

    // 仅真实的多成员编队生效；每个成员只读取自己的 Export，不影响同格队友。
    public float GetSquadAttackMultiplier(Infantry unit)
        => IsSquadMember(unit) ? Mathf.Max(0f, 1f + unit.squadAttackPercent / 100f) : 1f;

    public int NextSquadId()
    {
        int maxId = 0;
        if (unitManager != null)
            foreach (var unit in unitManager.AllUnits)
                if (unit != null && IsInstanceValid(unit)) maxId = Math.Max(maxId, unit.squadId);
        return maxId + 1;
    }

    private static bool Blacklists(Infantry unit, Infantry other)
    {
        if (unit?.squadBlacklist == null || other == null) return false;
        string typeName = other.GetType().Name;
        return unit.squadBlacklist.Any(x => !string.IsNullOrWhiteSpace(x) &&
            (string.Equals(x, typeName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(x, other.UnitTypeId, StringComparison.OrdinalIgnoreCase)));
    }

    // 支持与黑名单均双向检查，不能反向加入绕过拒绝。
    public bool CanPairSquad(Infantry a, Infantry b)
    {
        if (a == null || b == null || a == b || !IsInstanceValid(a) || !IsInstanceValid(b)) return false;
        if (!a.canFormSquad || !b.canFormSquad || a.team != b.team) return false;
        return !Blacklists(a, b) && !Blacklists(b, a);
    }

    public int GetSquadDisplayHp(Infantry unit)
    {
        return GetSquadMembers(unit).Sum(u =>
        {
            float full = Mathf.Max(1f, u.maxHealth / 10f);
            return Mathf.Max(0, Mathf.CeilToInt(u.health / full));
        });
    }

    public int GetSquadDisplayHp(IEnumerable<Infantry> members)
    {
        return members.Where(u => u != null && IsInstanceValid(u)).Sum(u =>
        {
            float full = Mathf.Max(1f, u.maxHealth / 10f);
            return Mathf.Max(0, Mathf.CeilToInt(u.health / full));
        });
    }

    // 上限、判定时机、血量门全部只读“新加入者”自己的 Export。
    public bool CanFormSquad(Infantry incoming, Infantry target)
    {
        var members = GetSquadMembers(target);
        if (members.Count == 0 || members.Any(m => !CanPairSquad(incoming, m))) return false;
        int before = members.Count; // members.Count 就是当前联合单位数。
        int countToCheck = incoming.squadLimitAfterJoin ? before + 1 : before;
        if (countToCheck > Mathf.Max(1, incoming.squadMaxMembers)) return false;
        int hp = GetSquadDisplayHp(members);
        if (hp < incoming.squadMinTargetHp) return false;
        // 开创人裁决（2026-11-24）：同型对默认归合流专属，混编不抢合流的对；incoming 开 squadSameTypeAllowed 可放行
        if (!incoming.squadSameTypeAllowed && members.Any(m => m != null && m.UnitTypeId == incoming.UnitTypeId)) return false;
        if (incoming.squadMaxTargetHp >= 0 && hp > incoming.squadMaxTargetHp) return false;
        return true;
    }

    public Infantry FindSquadTarget(Infantry incoming, Grids grid)
    {
        if (incoming == null || grid == null) return null;
        return grid.infantries.FirstOrDefault(u => u != incoming && CanFormSquad(incoming, u));
    }

    public bool FormSquad(Infantry incoming, Infantry target)
    {
        if (!CanFormSquad(incoming, target)) return false;
        int id = target.squadId > 0 ? target.squadId : NextSquadId();
        foreach (var member in GetSquadMembers(target)) member.squadId = id;
        incoming.squadId = id;
        RefreshSquadVisuals(target.grid);
        return true;
    }

    public void LeaveSquad(Infantry unit)
    {
        if (unit == null) return;
        var oldGrid = unit.grid;
        int oldId = unit.squadId;
        unit.squadId = 0;
        if (oldGrid != null && oldId > 0)
        {
            var rest = oldGrid.infantries.Where(u => u != unit && u.squadId == oldId).ToList();
            if (rest.Count == 1) rest[0].squadId = 0;
            RefreshSquadVisuals(oldGrid);
        }
    }

    public void RefreshSquadVisuals(Grids grid)
    {
        if (grid == null) return;
        foreach (var unit in grid.infantries.Where(u => u != null && IsInstanceValid(u)))
        {
            unit.Position = grid.Position;
            if (unit.squadId > 0)
            {
                var members = GetSquadMembers(unit);
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

    public void RefreshAllSquadVisuals()
    {
        if (gridManager?.grids == null) return;
        foreach (var grid in gridManager.grids)
            if (grid != null && grid.infantries.Any(u => u != null && u.squadId > 0)) RefreshSquadVisuals(grid);
    }


    // ✅ 混编残席清理（开创人2026-11-24）：成员被合流吃掉后，编组活人不足 2 即解散并清视觉
    public void CleanupSquadIfUnderstaffed(int id)
    {
        if (id <= 0 || gridManager == null) return;
        var alive = new List<Infantry>();
        foreach (var grid in gridManager.grids)
        {
            if (grid?.infantries == null) continue;
            foreach (var u in grid.infantries)
                if (u != null && IsInstanceValid(u) && u.squadId == id) alive.Add(u);
        }
        if (alive.Count >= 2) return;
        foreach (var u in alive) { u.squadId = 0; RefreshSquadVisuals(u.grid); }
    }
}
