// ActionResourceRuntime.cs — ✅ 开创人2026-11-15: 统一性基底·第一住户（修正版：每操作独立池 + Godot4 Variant惯用法）
using Godot;
using System;
using System.Collections.Generic;

public static class ActionResourceRuntime
{
    private static readonly Dictionary<GodotObject, List<(string field, Variant oldVal, int turnsLeft)>> _tempEvents = new();

    private static int Idx(GodotObject o, string action)
    {
        var v = o.Get("actionTable");
        if (v.VariantType == Variant.Type.Nil) return -1;
        var table = v.AsStringArray();
        for (int i = 0; i < table.Length; i++)
            if (string.Equals(table[i], action, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
    private static int[] IArr(GodotObject o, string name)
    {
        var v = o.Get(name);
        return v.VariantType == Variant.Type.Nil ? Array.Empty<int>() : v.AsInt32Array();
    }
    private static int At(int[] a, int i, int dflt) => (a != null && i >= 0 && i < a.Length) ? a[i] : dflt;
    private static bool Flag(GodotObject o, string name)
    {
        var v = o.Get(name);
        return v.VariantType != Variant.Type.Nil && v.AsBool();
    }
    private static int Int(GodotObject o, string name)
    {
        var v = o.Get(name);
        return v.VariantType == Variant.Type.Nil ? 0 : v.AsInt32();
    }
    private static int[] CloneSet(int[] a, int i, int v)
    {
        var b = (int[])(a ?? Array.Empty<int>()).Clone();
        if (i < b.Length) b[i] = v;
        return b;
    }

    /// <summary>能否执行（总开关关=永远可以）。四道门按操作独立：纪念法解锁/消耗品存量/独立冷却/次数。</summary>
    public static bool CanUse(GodotObject o, string action)
    {
        if (!Flag(o, "actionResourceEnabled")) return true;
        int i = Idx(o, action);
        if (i < 0) return true;
        var costs = IArr(o, "actionCosts");
        var unlockAt = IArr(o, "actionUnlockAt");
        var unlocked = IArr(o, "actionUnlocked");
        bool shared = Flag(o, "useSharedBucket") && Int(o, "maxBucketAmmo") > 0;
        int cur = shared ? Int(o, "currentBucketAmmo") : At(IArr(o, "actionCurrent"), i, 0);

        if (At(unlockAt, i, -1) >= 0)
        {
            if (At(unlocked, i, 0) == 0 && cur >= unlockAt[i])
            {
                var u2 = (int[])unlocked.Clone(); u2[i] = 1; o.Set("actionUnlocked", u2);
                unlocked = u2;
            }
            if (At(unlocked, i, 0) == 0) return false;
        }
        int cost = At(costs, i, -1);
        if (cost > 0 && cur < cost) return false;
        if (At(IArr(o, "actionCooldownLeft"), i, 0) > 0) return false;
        int maxPerTurn = At(IArr(o, "actionMaxPerTurn"), i, 0);
        if (maxPerTurn > 0 && At(IArr(o, "actionUsedThisTurn"), i, 0) >= maxPerTurn) return false;
        return true;
    }

    /// <summary>执行成功后结算：扣自己的池/记次/置自己的冷却。</summary>
    public static void Consume(GodotObject o, string action)
    {
        if (!Flag(o, "actionResourceEnabled")) return;
        int i = Idx(o, action);
        if (i < 0) return;
        bool shared = Flag(o, "useSharedBucket") && Int(o, "maxBucketAmmo") > 0;
        int cost = At(IArr(o, "actionCosts"), i, -1);
        if (cost > 0)
        {
            if (shared) o.Set("currentBucketAmmo", Math.Max(0, Int(o, "currentBucketAmmo") - cost));
            else o.Set("actionCurrent", CloneSet(IArr(o, "actionCurrent"), i, Math.Max(0, At(IArr(o, "actionCurrent"), i, 0) - cost)));
        }
        o.Set("actionUsedThisTurn", CloneSet(IArr(o, "actionUsedThisTurn"), i, At(IArr(o, "actionUsedThisTurn"), i, 0) + 1));
        int cd = At(IArr(o, "actionCooldownTurns"), i, 0);
        if (cd > 0) o.Set("actionCooldownLeft", CloneSet(IArr(o, "actionCooldownLeft"), i, cd));

        // ── 冷却对表三规则（v3）──
        int ownTurn = Int(o, "actionTotalTurns");
        var rulesV = o.Get("actionCooldownRules");
        if (rulesV.VariantType != Variant.Type.Nil)
        {
            var windowUses = IArr(o, "actionWindowUses");
            var windowTurn = IArr(o, "actionWindowTurn");
            var totalUses = IArr(o, "actionTotalUses");
            foreach (var raw in rulesV.AsGodotArray())
            {
                if (raw.Obj is not Godot.Collections.Dictionary d) continue;
                int kind = d.GetValueOrDefault("kind", 0).AsInt32();
                int limit = d.GetValueOrDefault("limit", 0).AsInt32();
                int cooldownR = d.GetValueOrDefault("cooldown", 0).AsInt32();
                if (kind == 0 || limit <= 0 || cooldownR <= 0) continue;
                if (kind == 1)
                {
                    int window = Math.Max(1, d.GetValueOrDefault("window", 3).AsInt32());
                    if (ownTurn - At(windowTurn, i, -9999) >= window) { windowUses = CloneSet(windowUses, i, 0); windowTurn = CloneSet(windowTurn, i, ownTurn); }
                    int wu = At(windowUses, i, 0) + 1;
                    windowUses = CloneSet(windowUses, i, wu);
                    if (wu > limit) o.Set("actionCooldownLeft", CloneSet(IArr(o, "actionCooldownLeft"), i, cooldownR));
                }
                else if (kind == 2)
                {
                    int tu = At(totalUses, i, 0) + 1;
                    totalUses = CloneSet(totalUses, i, tu);
                    if (tu >= limit)
                    {
                        o.Set("actionCooldownLeft", CloneSet(IArr(o, "actionCooldownLeft"), i, cooldownR));
                        totalUses = CloneSet(totalUses, i, d.GetValueOrDefault("refresh", 0).AsInt32());
                    }
                }
            }
            o.Set("actionWindowUses", windowUses);
            o.Set("actionWindowTurn", windowTurn);
            o.Set("actionTotalUses", totalUses);
        }
    }

    /// <summary>己方回合开始：各池独立装填（上限0=无限）/各冷却-1/次数清零/哑火清算/临时事件到期。</summary>
    public static void TurnStart(GodotObject o)
    {
        if (!Flag(o, "actionResourceEnabled")) return;
        var v = o.Get("actionTable");
        int n = v.VariantType == Variant.Type.Nil ? 0 : v.AsStringArray().Length;
        var cur = IArr(o, "actionCurrent");
        var fill = IArr(o, "actionFillPerTurn");
        var max = IArr(o, "actionMax");
        if (Flag(o, "actionMisfireFlag"))
        {
            for (int k = 0; k < cur.Length; k++) cur[k] = 0;
            o.Set("actionMisfireFlag", false);
        }
        for (int i = 0; i < n; i++)
        {
            int f = At(fill, i, 0);
            if (f <= 0) continue;
            int cap = At(max, i, 0);
            long sum = (long)At(cur, i, 0) + f;
            int nv = (cap > 0) ? (int)Math.Min(cap, sum) : (int)Math.Min(int.MaxValue, sum);
            cur = CloneSet(cur, i, nv);
        }
        o.Set("actionCurrent", cur);
        // v3：共享桶装填 + 己方回合计数
        int sf = Int(o, "sharedBucketFillPerTurn");
        int smax = Int(o, "maxBucketAmmo");
        if (sf > 0 && smax > 0) o.Set("currentBucketAmmo", Math.Min(smax, Int(o, "currentBucketAmmo") + sf));
        o.Set("actionTotalTurns", Int(o, "actionTotalTurns") + 1);
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

    /// <summary>哑火判定：受击后按 血量/单次伤害 阈值→哑火旗（回合开始清全池）。条件全空=任何受击都哑。</summary>
    public static void OnHitMisfire(GodotObject o, int hpAfter, int damage)
    {
        if (!Flag(o, "actionResourceEnabled")) return;
        int below = Int(o, "misfireHpBelow");
        int above = Int(o, "misfireHpAbove");
        int dmgBelow = Int(o, "misfireDamageBelow");
        int dmgAbove = Int(o, "misfireDamageAbove");
        bool anySet = below >= 0 || above >= 0 || dmgBelow >= 0 || dmgAbove >= 0;
        bool trigger = !anySet
            || (below >= 0 && hpAfter < below)
            || (above >= 0 && hpAfter > above)
            || (dmgBelow >= 0 && damage < dmgBelow)
            || (dmgAbove >= 0 && damage > dmgAbove);
        if (trigger) o.Set("actionMisfireFlag", true);
    }

    /// <summary>事件引擎v1：操作成功后触发——when缺省=always/current>=n；Godot原生Set/Get改任意Export（add/set；永久或temp回合后还原）。</summary>
    public static void FireEvents(GodotObject o, string action)
    {
        if (!Flag(o, "actionResourceEnabled")) return;
        var ev = o.Get("actionEvents");
        if (ev.VariantType == Variant.Type.Nil) return;
        var events = ev.AsGodotArray();
        foreach (var raw in events)
        {
            if (raw.Obj is not Godot.Collections.Dictionary d) continue;
            string act = d.GetValueOrDefault("action", "").ToString();
            if (!string.Equals(act, action, StringComparison.OrdinalIgnoreCase)) continue;
            string when = d.GetValueOrDefault("when", "always").ToString();
            if (when.StartsWith("current>="))
            {
                int i = Idx(o, action);
                if (i < 0 || !int.TryParse(when.Substring(9), out var need) || At(IArr(o, "actionCurrent"), i, 0) < need) continue;
            }
            else if (when != "always") continue;
            string field = d.GetValueOrDefault("field", "").ToString();
            if (string.IsNullOrEmpty(field)) continue;
            string mode = d.GetValueOrDefault("mode", "set").ToString();
            float value = d.GetValueOrDefault("value", 0f).AsSingle();
            int temp = d.GetValueOrDefault("temp", 0).AsInt32();
            var oldVal = o.Get(field);
            float cur2 = oldVal.VariantType == Variant.Type.Nil ? 0f : oldVal.AsSingle();
            o.Set(field, mode == "add" ? cur2 + value : value);
            if (temp > 0)
            {
                if (!_tempEvents.TryGetValue(o, out var list)) { list = new(); _tempEvents[o] = list; }
                list.Add((field, oldVal, temp));
            }
        }
    }

    /// <summary>移动清池：actionClearOnMove[i]=1 → 清该操作自己的池+解锁（不动别人）。</summary>
    public static void OnMoved(GodotObject o)
    {
        if (!Flag(o, "actionResourceEnabled")) return;
        var tv = o.Get("actionTable");
        int n = tv.VariantType == Variant.Type.Nil ? 0 : tv.AsStringArray().Length;
        var clear = IArr(o, "actionClearOnMove");
        if (clear.Length == 0) return;
        var cur = IArr(o, "actionCurrent");
        var unlocked = IArr(o, "actionUnlocked");
        bool touched = false;
        for (int i = 0; i < n && i < clear.Length; i++)
        {
            if (clear[i] == 0) continue;
            cur = CloneSet(cur, i, 0);
            unlocked = CloneSet(unlocked, i, 0);
            touched = true;
        }
        if (touched) { o.Set("actionCurrent", cur); o.Set("actionUnlocked", unlocked); }
    }
}
