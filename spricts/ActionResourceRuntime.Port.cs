// ActionResourceRuntime.Port.cs — ✅ 开创人2026-11-15 移植层：把"弱点格装填手/哑火弹药监视+禁操作块/手动装填/关闭效果/固定炮台锁/事件清理判定"嫁进参谋的 ActionResourceRuntime 基座。
// 约定：只读基座已有 Export 字段 + 本文件新增字段（三界 partial 各一份，逐字段一致）；填不存在的操作名/字段名=默认不调用。
using Godot;
using System;
using System.Collections.Generic;

public static partial class ActionResourceRuntime
{
    // ── 事件调用计数（perTurn/maxStacks 上限口径用；TurnStart 清零）──
    static readonly Dictionary<(GodotObject, int), int> _eventCounts = new();

    /// <summary>固定炮台锁：actionLockAsTurret && 任一池有存货 → 锁移动（ShowMenu/TryQuickMove 门禁）</summary>
    public static bool MovementLocked(GodotObject o)
    {
        if (!Flag(o, "actionLockAsTurret")) return false;
        // ✅ 2026-11-15 补水桶口径：共享弹药桶有存货同样锁（此前只查分池——纯水桶单位永不锁，与"有水桶=锁移动"设计不符）
        if (Flag(o, "useSharedBucket") && Int(o, "maxBucketAmmo") > 0)
            return Int(o, "currentBucketAmmo") > 0;
        foreach (var c in IArr(o, "actionCurrent")) if (c > 0) return true;
        return false;
    }

    /// <summary>手动装填：各池 + 各自 fillPerTurn（夹 actionMax）。耗不耗行动由 actionFillConsumesAction 由按钮方定。</summary>
    public static void ManualFill(GodotObject o)
    {
        if (!Flag(o, "actionResourceEnabled")) return;
        int n = ((string[])o.Get("actionTable").Obj).Length;
        var cur = IArr(o, "actionCurrent");
        var fill = IArr(o, "actionFillPerTurn");
        var max = IArr(o, "actionMax");
        for (int i = 0; i < n; i++)
        {
            int f = At(fill, i, 0);
            if (f <= 0) continue;
            int cap = At(max, i, 0);
            int nv = At(cur, i, 0) + f;
            if (cap > 0 && nv > cap) nv = cap;
            cur = CloneSet(cur, i, nv);
        }
        o.Set("actionCurrent", cur);
    }

    /// <summary>自主关闭/开启效果：关闭时还原全部临时事件效果。</summary>
    public static void ToggleOff(GodotObject o)
    {
        bool off = Flag(o, "actionManualOff");
        o.Set("actionManualOff", !off);
        if (!off && _tempEvents.TryGetValue(o, out var list))
        {
            foreach (var (field, old, _) in list) o.Set(field, old);
            _tempEvents.Remove(o);
        }
    }

    /// <summary>★弱点格装填手：被打格序号在 actionWeakCellReset 标 1 → 全部池清零。</summary>
    public static void OnWeakCellHit(GodotObject o, int hitCellIndex)
    {
        if (!Flag(o, "actionResourceEnabled") || hitCellIndex < 0) return;
        var wr = IArr(o, "actionWeakCellReset");
        if (hitCellIndex >= wr.Length || wr[hitCellIndex] == 0) return;
        int n = ((string[])o.Get("actionTable").Obj).Length;
        var cur = IArr(o, "actionCurrent");
        for (int i = 0; i < n; i++) cur = CloneSet(cur, i, 0);
        o.Set("actionCurrent", cur);
    }

    /// <summary>事件清理·受击清（A9 v2，开创人2026-11-15）：单位挨打时清掉标了 cleanupHit 的事件</summary>
    public static void OnUnitHit(GodotObject o)
    {
        if (!Flag(o, "actionResourceEnabled")) return;
        CleanupEvents(o, d => d.GetValueOrDefault("cleanupHit").AsBool());
    }

    /// <summary>哑火·弹药监视：被监视弹药的变化量超阈值 → 哑火旗 + 禁操作块。misfireAmmoKind=-1 不看。</summary>
    public static void OnAmmoChanged(GodotObject o, int ammoKind, int delta)
    {
        if (!Flag(o, "actionResourceEnabled") || delta == 0) return;
        var kv = o.Get("misfireAmmoKind");
        if (kv.VariantType == Variant.Type.Nil || kv.AsInt32() < 0 || kv.AsInt32() != ammoKind) return;
        bool trip = false;
        var dB = o.Get("misfireAmmoDeltaBelow"); var dA = o.Get("misfireAmmoDeltaAbove");
        var b = o.Get("misfireAmmoBelow"); var a = o.Get("misfireAmmoAbove");
        if (dB.VariantType != Variant.Type.Nil && dB.AsInt32() >= 0 && delta < dB.AsInt32()) trip = true;
        if (dA.VariantType != Variant.Type.Nil && dA.AsInt32() >= 0 && delta > dA.AsInt32()) trip = true;
        if (o is Infantry inf)   // 现值阈值（弹药管线只 Infantry 有）
        {
            int now = inf.GetAmmo((Infantry.AmmoKind)ammoKind);
            if (b.VariantType != Variant.Type.Nil && b.AsInt32() >= 0 && now < b.AsInt32()) trip = true;
            if (a.VariantType != Variant.Type.Nil && a.AsInt32() >= 0 && now > a.AsInt32()) trip = true;
        }
        if (trip)
        {
            o.Set("actionMisfireFlag", true);
            SetMisfireBlocks(o);
        }
    }

    /// <summary>哑火·燃油阈值求值（A9 v2，开创人2026-11-15）：现值+一次性变化量，与弹药哑火同式。
    /// 在燃油变动点调 EvalFuelMisfire(o, 变化量)；回合开始可用 delta=0 轮询现值。</summary>
    public static void EvalFuelMisfire(GodotObject o, int delta)
    {
        var below = Int(o, "misfireFuelBelow");
        var above = Int(o, "misfireFuelAbove");
        var dBelow = Int(o, "misfireFuelDeltaBelow");
        var dAbove = Int(o, "misfireFuelDeltaAbove");
        if (below < 0 && above < 0 && dBelow < 0 && dAbove < 0) return;
        var fuel = Int(o, "fuel");
        bool hit = false;
        if (below >= 0 && fuel < below) hit = true;
        if (above >= 0 && fuel > above) hit = true;
        if (dBelow >= 0 && delta < dBelow) hit = true;
        if (dAbove >= 0 && delta > dAbove) hit = true;
        if (hit)
        {
            o.Set("actionMisfireFlag", true);
            SetMisfireBlocks(o);
        }
    }

    /// <summary>哑火·禁操作块：misfireBlockOps 命中的操作各禁 misfireBlockTurns 回合（0/缺省=仅当回合→1）。</summary>
    public static void SetMisfireBlocks(GodotObject o)
    {
        var ops = o.Get("misfireBlockOps");
        if (ops.VariantType != Variant.Type.Array) return;
        int turns = 1;
        var tv = o.Get("misfireBlockTurns");
        if (tv.VariantType != Variant.Type.Nil && tv.AsInt32() > 0) turns = tv.AsInt32();
        var left = IArr(o, "actionBlockLeft");
        foreach (var raw in ops.AsGodotArray())
        {
            int i = Idx(o, raw.ToString());
            if (i < 0) continue;
            left = CloneSet(left, i, Mathf.Max(At(left, i, 0), turns));
        }
        o.Set("actionBlockLeft", left);
    }

    /// <summary>该操作是否被哑火块禁着。</summary>
    public static bool IsBlocked(GodotObject o, int i) => i >= 0 && At(IArr(o, "actionBlockLeft"), i, 0) > 0;

    /// <summary>事件清理判定：按谓词还原命中事件的临时效果（cleanupMove/cleanupHit/cleanupOps 共用）。</summary>
    public static void CleanupEvents(GodotObject o, Func<Godot.Collections.Dictionary, bool> predicate)
    {
        if (!_tempEvents.TryGetValue(o, out var list)) return;
        var events = o.Get("actionEvents");
        if (events.VariantType != Variant.Type.Array) return;
        for (int k = list.Count - 1; k >= 0; k--)
        {
            foreach (var raw in events.AsGodotArray())
            {
                if (raw.Obj is Godot.Collections.Dictionary d
                    && d.GetValueOrDefault("field", "").ToString() == list[k].field
                    && predicate(d))
                {
                    o.Set(list[k].field, list[k].oldVal);
                    list.RemoveAt(k);
                    break;
                }
            }
        }
    }

    /// <summary>事件字典是否点名本操作（cleanupOps 对表）。</summary>
    public static bool EventNamesAction(Godot.Collections.Dictionary d, string action)
    {
        var ops = d.GetValueOrDefault("cleanupOps", default(Variant));
        if (ops.VariantType != Variant.Type.Array) return false;
        foreach (var x in ops.AsGodotArray())
            if (string.Equals(x.ToString(), action, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
