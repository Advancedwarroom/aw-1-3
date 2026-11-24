using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// GameManager · FireSupport —— 共鸣火力支援（开创人 2026-11-25 原规格八条钦定版）
// 流程：单位开炮（Attack 入口）→ 以其【目标】为圆心 n~m 扫描 → 同队＋公共队伍、未被黑名单、
//       （默认）未行动、常规就能打到目标的单位共鸣齐射 → 系数增/减 → （可选）发起者自身保留/放弃伤害。
// ①是否能够开炮 ②共鸣范围n~m（目标圆心）③共鸣黑名单 ④增强削弱n% ⑤是否允许已行动
// ⑥共鸣数上限（伤害最高优先）⑦发起者自身是否保留伤害 ⑧必须主动操作（拒当共鸣，自己可发起）
public partial class Infantry
{
    [ExportGroup("共鸣火力支援（FireSupport · 开创人2026-11-25）")]
    [Export] public bool fireSupportEnabled = false;                 // ①是否能够开炮（发起共鸣）
    [Export] public int fireSupportRangeMin = 0;                    // ②共鸣范围下限（以目标为圆心，曼哈顿）
    [Export] public int fireSupportRangeMax = 2;                    // ②共鸣范围上限
    [Export] public string[] fireSupportBlacklist = new string[0]; // ③共鸣单位黑名单（UnitTypeId/TemplateId，如坦克等近战）
    [Export] public int fireSupportDamagePercent = 0;              // ④攻击时增强削弱n%（正=增强，负=削弱，结算×(100+n)/100）
    [Export] public bool fireSupportAllowActed = false;              // ⑤共鸣单位是否允许已行动
    [Export] public int fireSupportMaxResponders = 0;              // ⑥共鸣单位数上限（超出时仅伤害最高的前N个触发）
    [Export] public bool fireSupportSelfDamage = true;             // ⑦发起单位自身是否保留伤害（false=指挥官只指挥不开炮）
    [Export] public bool fireSupportManualOnly = false;            // ⑧必须主动操作：不能被他人共鸣召唤，但自己可发起
}

public partial class GameManager
{
    public static bool resolvingFireSupport = false;   // 防递归：共鸣 strike 不触发新共鸣、不触发混编齐射

    private static bool FireSupportBlacklisted(Infantry attacker, Infantry u)
    {
        var bl = attacker.fireSupportBlacklist;
        return bl != null && (bl.Contains(u.UnitTypeId) || bl.Contains(u.TemplateId) || bl.Contains(u.GetType().Name));
    }

    // 单个共鸣者对目标的预期伤害（取主/副武器较大值；≤0 = 常规就打不了，无共鸣资格）
    private static int ExpectedFireSupportDamage(Infantry r, Infantry target)
    {
        try
        {
            int d1 = r.CalculateFinalDamage(target, WeaponType.Primary);
            int d2 = r.CalculateFinalDamage(target, WeaponType.Secondary);
            return Mathf.Max(d1, d2);
        }
        catch { return 0; }
    }

    // ========== 共鸣者枚举：资格门 → 预期伤害 → 上限取最强 ==========
    public List<Infantry> GetFireSupportResponders(Infantry attacker, Infantry target)
    {
        var result = new List<Infantry>();
        if (attacker == null || target == null || target.grid == null || gridManager == null) return result;
        if (resolvingFireSupport) return result;
        int rMin = Mathf.Max(0, attacker.fireSupportRangeMin);
        int rMax = Mathf.Max(rMin, attacker.fireSupportRangeMax);
        foreach (var g in gridManager.grids)
        {
            if (g?.infantry == null) continue;
            var u = g.infantry;
            if (u == null || !IsInstanceValid(u) || u == attacker) continue;
            if (u.team != attacker.team && u.team != TeamHelper.Player) continue;   // 仅限你能控制的：同队＋公共
            if (u.fireSupportManualOnly) continue;                                   // ⑧主动派：拒当共鸣
            if (FireSupportBlacklisted(attacker, u)) continue;                         // ③黑名单
            if (u.grid == null) continue;
            int man = Mathf.Abs(u.grid.GridIndex.X - target.grid.GridIndex.X) + Mathf.Abs(u.grid.GridIndex.Y - target.grid.GridIndex.Y);
            if (man < rMin || man > rMax) continue;                                    // ②目标圆心 n~m
            if (!attacker.fireSupportAllowActed && (u.hasActed || u.state == UnitState.Acted)) continue;  // ⑤已行动门
            if (ExpectedFireSupportDamage(u, target) <= 0) continue;                   // 常规就能攻击的（表内无值/全零 → 无资格）
            result.Add(u);
        }
        // ⑥上限：仅伤害最高的前N个触发
        int max = attacker.fireSupportMaxResponders;
        if (max > 0 && result.Count > max)
        {
            var ranked = result.OrderByDescending(u => ExpectedFireSupportDamage(u, target)).Take(max).ToList();
            result.Clear();
            result.AddRange(ranked);
        }
        return result;
    }

    // ========== 齐射触发 ==========
    public void TriggerFireSupport(Infantry attacker, Infantry target)
    {
        if (attacker == null || target == null || !attacker.fireSupportEnabled) return;
        if (resolvingFireSupport) return;
        var responders = GetFireSupportResponders(attacker, target);
        if (responders.Count == 0) return;
        resolvingFireSupport = true;
        int percent = 100 + Mathf.Clamp(attacker.fireSupportDamagePercent, -100, 200);   // ④增强削弱n%
        foreach (var r in responders)
        {
            if (r == null || !IsInstanceValid(r) || !IsInstanceValid(target)) continue;
            r.Attack(target, allowCounter: false, damagePercent: percent);
        }
        resolvingFireSupport = false;
    }
}
