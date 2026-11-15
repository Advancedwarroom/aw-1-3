using Godot;
using System;
using System.Collections.Generic;

// PrepModule.Engine.cs - 准备/冷却引擎：所有操作点两行接入（CanPerform + NotifyPerformed），单位/兵器/设施通用。
// 找不到模块/模块关闭=全场放行（零入侵）；填不存在的操作名/模块名/字段名=该规则默认不调用。
public static class PrepEngine
{
    // ========== 模块查找 ==========
    public static PrepModule Get(Node entity)
    {
        var m = Mods.FindModule<PrepModule>(entity);
        return (m != null && m.prepEnabled && !m.moduleOff) ? m : null;
    }

    // ========== 水桶（Infantry 走 AmmoKind 管线；兵器/设施走模块自带桶）==========
    public static int GetBucket(Node entity)
    {
        if (entity is Infantry inf) return inf.currentPrepBucket;
        var m = Get(entity); return m?.moduleBucket ?? 0;
    }
    public static int GetBucketMax(Node entity)
    {
        if (entity is Infantry inf) return inf.maxPrepBucket;
        var m = Get(entity); return m?.moduleBucketMax ?? 0;
    }
    public static void AddBucket(Node entity, int amount, int capOverride = -1)
    {
        int cap = capOverride > 0 ? capOverride : GetBucketMax(entity);
        if (entity is Infantry inf) { inf.AddAmmo(Infantry.AmmoKind.PrepBucket, amount); return; } // AddAmmo 自带 0..上限夹取
        var m = Get(entity);
        if (m != null) m.moduleBucket = Mathf.Clamp(m.moduleBucket + amount, 0, Mathf.Max(cap, 0));
    }
    public static void ClearProgress(Node entity)
    {
        if (entity is Infantry inf) inf.SetAmmoRaw(Infantry.AmmoKind.PrepBucket, 0);
        else { var m = Get(entity); if (m != null) m.moduleBucket = 0; }
    }

    // ========== 行解析：行在=行优先（sentinel 回落全局）；行不在=全局 ==========
    public static PrepOpConfig FindRow(PrepModule m, string op)
    {
        if (m?.prepOps == null || string.IsNullOrEmpty(op)) return null;
        foreach (var r in m.prepOps) if (r != null && r.operation == op) return r;
        return null;
    }
    static int I(int rowVal, int global) => rowVal < 0 ? global : rowVal;
    static bool B(PrepTriState t, bool global) => t == PrepTriState.Global ? global : t == PrepTriState.Yes;

    // ── 每操作独立状态访问（A 的冷却不污染 B）──
    static int GetCd(PrepModule m, PrepOpConfig row) => row == null ? m.prepCooldownLeftGlobal : row._cooldownLeft;
    static void SetCd(PrepModule m, PrepOpConfig row, int v) { if (row == null) m.prepCooldownLeftGlobal = v; else row._cooldownLeft = v; }
    static int GetUses(PrepModule m, PrepOpConfig row) => row == null ? m._globalUsesThisTurn : row._usesThisTurn;
    static void SetUses(PrepModule m, PrepOpConfig row, int v) { if (row == null) m._globalUsesThisTurn = v; else row._usesThisTurn = v; }
    static int GetRefreshed(PrepModule m, PrepOpConfig row) => row == null ? m.prepGlobalUsesLeft : row._refreshedUsesLeft;
    static void SetRefreshed(PrepModule m, PrepOpConfig row, int v) { if (row == null) m.prepGlobalUsesLeft = v; else row._refreshedUsesLeft = v; }

    // ========== 门禁：某操作现在能不能干 ==========
    public static bool CanPerform(Node entity, string op)
    {
        var m = Get(entity);
        if (m == null || string.IsNullOrEmpty(op)) return true;
        if (op == Ops.Move && MovementLocked(entity)) return false;                 // 固定炮台锁移动

        var row = FindRow(m, op);
        int trigger = I(row?.prepTrigger ?? -1, m.prepTrigger);
        int cooldown = I(row?.cooldownTurns ?? -1, m.cooldownTurns);
        bool oncePer = B(row?.oncePerTurn ?? PrepTriState.Global, m.oncePerTurn);
        int maxUses = I(row?.maxUsesPerTurn ?? -1, m.maxUsesPerTurn);

        if (GetCd(m, row) > 0 && cooldown > 0) return false;      // 该操作自己的冷却中（歇N回合）
        if (oncePer && GetUses(m, row) >= 1) return false;      // 一回合只扣一次→后续不允许
        if (maxUses > 0 && GetUses(m, row) >= maxUses) return false; // 扣款次数封顶
        if (GetRefreshed(m, row) == 0) return false;             // 冷却额度模式：次数用完
        if (trigger > 0 && GetBucket(entity) < trigger) return false; // 装填没到 n
        if (IsMisfireBlocked(entity, op)) return false;              // 哑火表禁操作
        return true;
    }

    // 有存货=固定炮台判定
    public static bool MovementLocked(Node entity)
    {
        var m = Get(entity);
        if (m == null) return false;
        var row = FindRow(m, Ops.Move);
        bool locks = B(row?.locksMovementWhenStocked ?? PrepTriState.Global, m.locksMovementWhenStocked);
        return locks && GetBucket(entity) > 0;
    }

    static bool IsMisfireBlocked(Node entity, string op)
    {
        var m = Get(entity);
        if (m?.prepMisfires == null) return false;
        foreach (var r in m.prepMisfires)
            if (r != null && r._blockedUntilTurn >= 0 && _turn <= r._blockedUntilTurn
                && r.blockOperations != null && Array.IndexOf(r.blockOperations, op) >= 0)
                return true;
        return false;
    }

    // ========== 记账：操作成功执行后调用（扣款/历史/冷却/清进度/事件/冷却表）==========
    static int _turn;
    public static int CurrentTurn => _turn;
    public static void SetTurn(int turn) => _turn = turn;

    public static void NotifyPerformed(Node entity, string op)
    {
        var m = Get(entity);
        if (m == null || string.IsNullOrEmpty(op)) return;
        var row = FindRow(m, op);

        int trigger = I(row?.prepTrigger ?? -1, m.prepTrigger);
        bool consume = (row?.consumeMode ?? m.consumeMode) == PrepConsumeMode.Consume;
        int cooldown = I(row?.cooldownTurns ?? -1, m.cooldownTurns);
        bool refill = B(row?.refillAfterUse ?? PrepTriState.Global, m.fireResetsPrep);
        int refillN = refill ? m.refillAfterUse : 0;

        SetUses(m, row, GetUses(m, row) + 1);
        m.history.Add((op, _turn));
        if (m.history.Count > 300) m.history.RemoveAt(0);

        if (consume && trigger > 0) AddBucket(entity, -trigger, 0);  // 消耗品法：直接去掉20
        if (refillN > 0) AddBucket(entity, refillN);                 // 开火后自动重新装填
        if (cooldown > 0) SetCd(m, row, cooldown);                   // 该操作自己歇N回合（A 不影响 B）

        // 清进度判定
        var resetOps = row?.resetOnOperations ?? m.resetOnOperations;
        if (m.resetOnAnyAction) ClearProgress(entity);
        else if (resetOps != null && resetOps.Length > 0 && Array.IndexOf(resetOps, op) >= 0) ClearProgress(entity);

        EvaluateEvents(entity, op);          // ? 事件
        EvaluateCooldownRules(entity, op);   // 冷却表三条
    }

    // ========== 回合推进（GameManager 回合钩子调用；防同回合双跑）==========
    public static void OnTurnStart(Node entity)
    {
        var m = Get(entity);
        if (m == null || m._lastTickedTurn == _turn) return;   // 双跑保护
        m._lastTickedTurn = _turn;

        // 冷却递减：到 0 时应用「刷新到n次机会」语义（0/大=全刷新；小=留差额）
        void TickCd(PrepOpConfig row)
        {
            int cd = GetCd(m, row);
            if (cd > 0)
            {
                cd--;
                SetCd(m, row, cd);
                if (cd == 0)
                {
                    int refreshTo = I(row?.cooldownRefreshesTo ?? -1, m.cooldownRefreshesTo);
                    if (refreshTo > 0 && GetUses(m, row) > refreshTo) SetRefreshed(m, row, refreshTo); // 留差额：次数成为冷却实现品
                    else SetRefreshed(m, row, -1);                                                    // 一致=正确全刷新
                }
            }
        }
        TickCd(null);
        if (m.prepOps != null) foreach (var row in m.prepOps) if (row != null) TickCd(row);

        // 各操作独立：本回合计数复位 + 事件调用计数复位
        m._globalUsesThisTurn = 0;
        if (m.prepOps != null) foreach (var row in m.prepOps) if (row != null) { row._usesThisTurn = 0; }
        if (m.prepEvents != null) foreach (var ev in m.prepEvents) if (ev != null) ev._invokesThisTurn = 0;

        // 不跨回合存：清零（各操作独立判定，任一操作不存即清——保守语义）
        bool store = B(PrepTriState.Global, m.storeAcrossTurns);
        if (m.prepOps != null) foreach (var row in m.prepOps)
            if (row != null && !B(row.storeAcrossTurns, m.storeAcrossTurns)) { store = false; break; }
        if (!store) ClearProgress(entity);

        // 自动装填（手动模式由装填按钮走 ManualFill）
        if (m.prepAutoStart || m.prepPerTurn > 0)
        {
            int fill = m.prepPerTurn;
            if (m.prepOps != null) foreach (var row in m.prepOps)
            {
                int p = I(row?.prepPerTurn ?? -1, m.prepPerTurn);
                if (p > fill) fill = p;                                  // 装填率取各行最大
            }
            if (fill > 0)
            {
                int cap = m.prepMax;
                if (m.prepOps != null) foreach (var row in m.prepOps)
                {
                    int c = I(row?.prepMax ?? -1, m.prepMax);
                    if (c > cap) cap = c;
                }
                AddBucket(entity, fill, cap);
            }
        }

        // 临时效果到期还原（FieldMods 驱动）
        FieldMods.TickAndRestore(entity, m.activeTempEffects);
    }

    // ========== ? 事件：trigger 满足 → 附加模块 / 改任意 Export 字段（FieldMods 反射）/ 临时永久 / 三种上限口径 ==========
    static void EvaluateEvents(Node entity, string op)
    {
        var m = Get(entity);
        if (m?.prepEvents == null) return;
        foreach (var ev in m.prepEvents)
        {
            if (ev == null || ev.triggerAt <= 0) continue;
            if (!string.IsNullOrEmpty(ev.operation) && ev.operation != op) continue;
            if (GetBucket(entity) < ev.triggerAt) continue;

            // 附加模块：不存在的模块名=默认不调用
            if (ev.attachToModule)
            {
                if (string.IsNullOrEmpty(ev.targetModule)) continue;
                if (FindRow(m, ev.targetModule) == null && Array.IndexOf(Ops.All, ev.targetModule) < 0) continue;
            }

            // 层数上限（三种口径）+ 每回合调用上限
            if (ev.perTurnInvokeCap > 0 && ev._invokesThisTurn >= ev.perTurnInvokeCap) continue;
            if (ev.maxStacks > 0 && m.activeTempEffects != null)
            {
                int stacks = 0, self = 0, total = m.activeTempEffects.Count;
                foreach (var a in m.activeTempEffects)
                    if (a.src == ev) self++;
                    else if (a.fieldName == ev.fieldName) stacks++;
                int counted = ev.capScope == PrepCapScope.AllStacks ? total
                            : ev.capScope == PrepCapScope.ExceptSelf ? stacks
                            : (stacks > 0 || self > 0 ? 1 : 0);      // DistinctKinds：同field只算一层
                if (counted >= ev.maxStacks) continue;
            }

            ev._invokesThisTurn++;
            if (string.IsNullOrEmpty(ev.fieldName)) continue;          // 纯通知事件
            bool ok = FieldMods.Apply(entity, ev.fieldName, ev.modifyMode, ev.value, ev.stringValue,
                                       ev.permanent, ev.durationTurns, ev, m.activeTempEffects);
            if (ok && ev.keepHealthOnTransform && !string.IsNullOrEmpty(ev.stringValue))
                ResetRuntimeExceptHealth(entity);                      // 改变兵种类：血量留着其余重置
        }
    }

    static void ResetRuntimeExceptHealth(Node entity)
    {
        if (entity is Infantry inf)
        {
            int hp = inf.health;
            inf.OnTurnEnd();
            inf.health = hp;
        }
    }

    // ========== 冷却表三条规则 ==========
    static void EvaluateCooldownRules(Node entity, string op)
    {
        var m = Get(entity);
        if (m?.prepCooldowns == null) return;
        foreach (var r in m.prepCooldowns)
        {
            if (r == null || r.threshold <= 0) continue;
            if (r.scope == PrepCooldownScope.PerOperation && string.IsNullOrEmpty(r.operation)) continue; // 空=不调用
            int count = 0;
            foreach (var h in m.history)
            {
                bool match = r.scope == PrepCooldownScope.TotalCount || h.op == r.operation;
                if (!match) continue;
                if (r.window == PrepCooldownWindow.ThisTurn && h.turn == _turn) count++;
                else if (r.window == PrepCooldownWindow.LastNTurns && _turn - h.turn < Mathf.Max(1, r.windowTurns)) count++;
                else if (r.window == PrepCooldownWindow.GlobalTotal) count++;
            }
            if (count > r.threshold)
            {
                // 命中哪一行就压哪一行的冷却（A 不影响 B）
                var row = r.scope == PrepCooldownScope.PerOperation ? FindRow(m, r.operation) : null;
                SetCd(m, row, Mathf.Max(GetCd(m, row), r.cooldownTurns));
                if (r.refreshTo > 0 && count > r.refreshTo) SetRefreshed(m, row, r.refreshTo); // 留差额
                else SetRefreshed(m, row, -1);                                                  // 全刷新
                if (!r.keepWhenIdle) { /* 不保留：额度已按 refreshTo 刷新，idle 自然到期 */ }
            }
        }
    }

    // ========== 受击钩子（伤害管线调用：打弱点格→装填手哑火 + 通用受击判定 + 事件清理）==========
    public static void OnHit(Node entity, int hitCellIndex, float damageDealt)
    {
        var m = Get(entity);
        if (m == null) return;
        // 弱点格联动：打中标记格（装填手！）→ 清进度判定
        if (hitCellIndex >= 0 && m.weakCellResetPrep != null && hitCellIndex < m.weakCellResetPrep.Length && m.weakCellResetPrep[hitCellIndex] != 0)   // 1=装填手格
            ClearProgress(entity);
        if (m.prepResetOnHit) EvaluateMisfire(entity, damageDealt, -1, 0);
        CleanupEffects(entity, ev => ev.cleanupOnHit || ev.cleanupOnHpChange);
    }

    // ========== 哑火表：血量/一次性伤害 与 弹药/燃油变化 统一入口 ==========
    public static void EvaluateMisfire(Node entity, float damageDealt, int ammoKindChanged, int ammoDelta)
    {
        var m = Get(entity);
        if (m?.prepMisfires == null) return;
        foreach (var r in m.prepMisfires)
        {
            if (r == null) continue;
            bool hit = false;
            if (entity is Infantry inf && inf.GetEffectiveMaxHealth() > 0)
            {
                float hpPct = 100f * inf.health / inf.GetEffectiveMaxHealth();
                if (r.hpBelow >= 0 && hpPct < r.hpBelow) hit = true;
                if (r.hpAbove >= 0 && hpPct > r.hpAbove) hit = true;
            }
            if (r.damageBelow >= 0 && damageDealt < r.damageBelow) hit = true;
            if (r.damageAbove >= 0 && damageDealt > r.damageAbove) hit = true;
            if (ammoKindChanged >= 0 && r.watchAmmoKind == ammoKindChanged)
            {
                int now = entity is Infantry i2 ? i2.GetAmmo((Infantry.AmmoKind)ammoKindChanged) : 0;
                if (r.ammoBelow >= 0 && now < r.ammoBelow) hit = true;
                if (r.ammoAbove >= 0 && now > r.ammoAbove) hit = true;
                if (r.ammoDeltaBelow >= 0 && ammoDelta < r.ammoDeltaBelow) hit = true;
                if (r.ammoDeltaAbove >= 0 && ammoDelta > r.ammoDeltaAbove) hit = true;
            }
            if (r.watchFuel && entity != null)
            {
                var ff = entity.GetType().GetField("currentFuel", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (ff != null)
                {
                    double now = 0;
                    try { now = Convert.ToDouble(ff.GetValue(entity)); } catch { }
                    if (r.fuelBelow >= 0 && now < r.fuelBelow) hit = true;
                    if (r.fuelAbove >= 0 && now > r.fuelAbove) hit = true;
                    if (r.fuelDeltaBelow >= 0 && ammoDelta < r.fuelDeltaBelow) hit = true;   // 燃油变化借 ammoDelta 通道传入（-0 分隔见 OnAmmoChanged）
                    if (r.fuelDeltaAbove >= 0 && -ammoDelta < r.fuelDeltaAbove) hit = true;
                }
            }
            if (hit)
            {
                if (r.clearProgress) ClearProgress(entity);
                if (r.blockTurns > 0) r._blockedUntilTurn = _turn + r.blockTurns;
                else r._blockedUntilTurn = _turn;   // 0=仅当回合
            }
        }
    }

    // ========== 弹药变化钩子（Infantry.SetAmmoRaw 管线调用）==========
    public static void OnAmmoChanged(Node entity, int ammoKind, int delta)
    {
        var m = Get(entity);
        if (m == null || delta == 0) return;
        EvaluateMisfire(entity, -1, ammoKind, delta);
        CleanupEffects(entity, ev => ev.cleanupOnHpChange);
    }

    // ========== 移动钩子（prepLocksMovement 白装 + 事件清理）==========
    public static void OnMove(Node entity)
    {
        var m = Get(entity);
        if (m == null) return;
        if (m.prepLocksMovement) ClearProgress(entity);   // 架着枪移动=白装
        if (m.resetOnMove) ClearProgress(entity);
        CleanupEffects(entity, ev => ev.cleanupOnMove);
    }

    static void CleanupEffects(Node entity, Func<PrepEventConfig, bool> predicate)
    {
        var m = Get(entity);
        if (m?.activeTempEffects == null) return;
        for (int i = m.activeTempEffects.Count - 1; i >= 0; i--)
        {
            var e = m.activeTempEffects[i];
            if (e.src != null && predicate(e.src))
            {
                FieldMods.RestoreOne(entity, e);
                m.activeTempEffects.RemoveAt(i);
            }
        }
    }

    // ========== 手动装填（菜单「装填」按钮调用）==========
    public static void ManualFill(Infantry unit)
    {
        var m = Get(unit);
        if (m == null) return;
        int fill = Mathf.Max(m.prepPerTurn, 1);
        int cap = m.prepMax;
        AddBucket(unit, fill, cap);
        if (m.prepConsumesAction) { unit.state = UnitState.Acted; unit.hasActed = true; }
    }

    // 手动关闭效果（「关闭效果」按钮）
    public static void ToggleOff(Node entity)
    {
        var m = Mods.FindModule<PrepModule>(entity);
        if (m == null) return;
        m.moduleOff = !m.moduleOff;
        if (m.moduleOff)
        {
            for (int i = m.activeTempEffects.Count - 1; i >= 0; i--) FieldMods.RestoreOne(entity, m.activeTempEffects[i]);
            m.activeTempEffects.Clear();
        }
    }
}
