// ActionResourceRuntime.cs — ✅ 开创人2026-11-15: 统一性基底·第一住户（修正版：每操作独立池）
// 三界（单位/兵器/设施）同构Export组 + 本静态运行时 = 所有通用模块的可编程动作层。
// 铁律：每条操作持有自己的 装填/上限/现值/冷却/次数——A操作的任何消耗在物理上碰不到B操作。
// 上限actionMax[i]=0 → 无上限（无限蓄能：纪念法变身/220连打11发全合法）。
// 经Godot原生Get/Set读写Export字段——对三个类完全同一套代码（真·通用化+可联动修改化）。
using Godot;
using System;
using System.Collections.Generic;

public static class ActionResourceRuntime
{
    private static readonly Dictionary<GodotObject, List<(string field, Variant oldVal, int turnsLeft)>> _tempEvents = new();

    private static int Idx(GodotObject o, string action)
    {
        var table = o.Get("actionTable") as string[];
        if (table == null) return -1;
        for (int i = 0; i < table.Length; i++)
            if (string.Equals(table[i], action, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
    private static int[] IArr(GodotObject o, string name) => o.Get(name) as int[] ?? Array.Empty<int>();
    private static bool[] BArr(GodotObject o, string name) => o.Get(name) as bool[] ?? Array.Empty<bool>();
    private static T At<T>(T[] a, int i, T dflt) => (a != null && i >= 0 && i < a.Length) ? a[i] : dflt;
    private static int[] CloneSet(int[] a, int i, int v)
    {
        var b = (int[])(a ?? Array.Empty<int>()).Clone();
        if (i < b.Length) b[i] = v;
        return b;
    }

    /// <summary>能否执行（总开关关=永远可以）。四道门全按操作独立：纪念法解锁/消耗品存量/独立冷却/次数。</summary>
    public static bool CanUse(GodotObject o, string action)
    {
        if (!(bool)(o.Get("actionResourceEnabled") ?? false)) return true;
        int i = Idx(o, action);
        if (i < 0) return true;
        var costs = IArr(o, "actionCosts");
        var unlockAt = IArr(o, "actionUnlockAt");
        var cur = IArr(o, "actionCurrent");

        // 纪念法：首次攒到阈值即永久解锁
        if (At(unlockAt, i, -1) >= 0)
        {
            var unlocked = BArr(o, "actionUnlocked");
            if (!At(unlocked, i, false) && At(cur, i, 0) >= unlockAt[i])
            {
                var u2 = (bool[])unlocked.Clone(); u2[i] = true; o.Set("actionUnlocked", u2);
                unlocked = u2;
            }
            if (!At(unlocked, i, false)) return false;
        }
        int cost = At(costs, i, -1);
        if (cost > 0 && At(cur, i, 0) < cost) return false;               // 自己的池子不够（绝不碰别人的）
        if (At(IArr(o, "actionCooldownLeft"), i, 0) > 0) return false;     // 自己的冷却
        int maxPerTurn = At(IArr(o, "actionMaxPerTurn"), i, 0);
        if (maxPerTurn > 0 && At(IArr(o, "actionUsedThisTurn"), i, 0) >= maxPerTurn) return false;
        return true;
    }

    /// <summary>执行成功后结算：扣自己的池/记次/置自己的冷却。</summary>
    public static void Consume(GodotObject o, string action)
    {
        if (!(bool)(o.Get("actionResourceEnabled") ?? false)) return;
        int i = Idx(o, action);
        if (i < 0) return;
        int cost = At(IArr(o, "actionCosts"), i, -1);
        if (cost > 0) o.Set("actionCurrent", CloneSet(IArr(o, "actionCurrent"), i, Math.Max(0, At(IArr(o, "actionCurrent"), i, 0) - cost)));
        o.Set("actionUsedThisTurn", CloneSet(IArr(o, "actionUsedThisTurn"), i, At(IArr(o, "actionUsedThisTurn"), i, 0) + 1));
        int cd = At(IArr(o, "actionCooldownTurns"), i, 0);
        if (cd > 0) o.Set("actionCooldownLeft", CloneSet(IArr(o, "actionCooldownLeft"), i, cd));
    }

    /// <summary>己方回合开始：各池独立装填（上限0=无限）/各冷却-1/次数清零/哑火清算/临时事件到期。</summary>
    public static void TurnStart(GodotObject o)
    {
        if (!(bool)(o.Get("actionResourceEnabled") ?? false)) return;
        var table = o.Get("actionTable") as string[];
        int n = table?.Length ?? 0;
        var cur = IArr(o, "actionCurrent");
        var fill = IArr(o, "actionFillPerTurn");
        var max = IArr(o, "actionMax");
        // 哑火：装填全白干
        if ((bool)(o.Get("actionMisfireFlag") ?? false))
        {
            for (int k = 0; k < cur.Length; k++) cur[k] = 0;
            o.Set("actionMisfireFlag", false);
        }
        for (int i = 0; i < n; i++)
        {
            int f = At(fill, i, 0);
            if (f <= 0) continue;
            int cap = At(max, i, 0);
            long sum = (long)At(cur, i, 0) + f;                          // 无上限用long防溢出,钳回int
            int nv = (cap > 0) ? (int)Math.Min(cap, sum) : (int)Math.Min(int.MaxValue, sum);
            cur = CloneSet(cur, i, nv);
        }
        o.Set("actionCurrent", cur);
        var cd = IArr(o, "actionCooldownLeft");
        for (int k = 0; k < cd.Length; k++) if (cd[k] > 0) cd[k]--;
        o.Set("actionCooldownLeft", cd);
        var used = IArr(o, "actionUsedThisTurn");
        for (int k = 0; k < used.Length; k++) used[k] = 0;
        o.Set("actionUsedThisTurn", used);
        if (_tempEvents.TryGetValue(o, out var list))
        {
            for (int k = list.Count - 1; k >= 0; k--)
            {
                var e = list[k]; e.turnsLeft--;
                if (e.turnsLeft <= 0) { o.Set(e.field, e.oldVal); list.RemoveAt(k); } else list[k] = e;
            }
            if (list.Count == 0) _tempEvents.Remove(o);
        }
    }

    /// <summary>哑火判定：受击后按 血量/单次伤害 阈值 → 置哑火旗（TurnStart清算清零全池）。条件全空=任何受击都哑。</summary>
    public static void OnHitMisfire(GodotObject o, int hpAfter, int damage)
    {
        if (!(bool)(o.Get("actionResourceEnabled") ?? false)) return;
        int below = (int)(o.Get("misfireHpBelow") ?? -1);
        int above = (int)(o.Get("misfireHpAbove") ?? -1);
        int dmgBelow = (int)(o.Get("misfireDamageBelow") ?? -1);
        int dmgAbove = (int)(o.Get("misfireDamageAbove") ?? -1);
        bool anySet = below >= 0 || above >= 0 || dmgBelow >= 0 || dmgAbove >= 0;
        bool trigger = !anySet
            || (below >= 0 && hpAfter < below)
            || (above >= 0 && hpAfter > above)
            || (dmgBelow >= 0 && damage < dmgBelow)
            || (dmgAbove >= 0 && damage > dmgAbove);
        if (trigger) o.Set("actionMisfireFlag", true);
    }

    /// <summary>事件引擎v1：操作成功后触发——when缺省=always；module=Godot原生Set/Get改任意Export（add/set，永久或temp回合后还原）。</summary>
    public static void FireEvents(GodotObject o, string action)
    {
        if (!(bool)(o.Get("actionResourceEnabled") ?? false)) return;
        var events = o.Get("actionEvents") as Godot.Collections.Array<Godot.Collections.Dictionary>;
        if (events == null) return;
        foreach (var raw in events)
        {
            if (raw is not Godot.Collections.Dictionary d) continue;
            if (!string.Equals(d.GetValueOrDefault("action", "").ToString(), action, StringComparison.OrdinalIgnoreCase)) continue;
            string when = d.GetValueOrDefault("when", "always").ToString();
            if (when.StartsWith("current>="))
            {
                int i = Idx(o, action);
                if (i < 0 || At(IArr(o, "actionCurrent"), i, 0) < Convert.ToInt32(when.Substring(9))) continue;
            }
            else if (when != "always") continue;
            string field = d.GetValueOrDefault("field", "").ToString();
            if (string.IsNullOrEmpty(field)) continue;
            string mode = d.GetValueOrDefault("mode", "set").ToString();
            float value = Convert.ToSingle(d.GetValueOrDefault("value", 0f));
            int temp = Convert.ToInt32(d.GetValueOrDefault("temp", 0));
            var oldVal = o.Get(field);
            float cur2 = 0f;
            try { cur2 = Convert.ToSingle(oldVal); } catch { }
            o.Set(field, mode == "add" ? cur2 + value : value);
            if (temp > 0)
            {
                if (!_tempEvents.TryGetValue(o, out var list)) { list = new(); _tempEvents[o] = list; }
                list.Add((field, oldVal, temp));
            }
        }
    }
}
