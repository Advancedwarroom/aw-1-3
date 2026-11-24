using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// GameManager · FireSupport —— 火力支援模块（开创人 2026-11-24）
// 主攻手开火时，呼叫半径内所有"够得着目标"的支援火力齐射（同队、fireSupportProvider、未行动、射程/攻防表过、弹药足）。
// 支援 strike：allowCounter=false（支援方免疫反击——间射本色）、伤害系数可调、防递归（支援不触发支援、支援成员不触发混编齐射）。
// 裁决默认值（开创人2026-11-24）：系数 50%、圆心=主攻手、默认不耗行动——全部 Export，拧就完事。
public partial class Infantry
{
    [ExportGroup("火力支援模块（FireSupport · 开创人2026-11-24）")]
    [Export] public bool fireSupportEnabled = false;               // 主攻手呼叫周围火炮齐射
    [Export] public bool fireSupportProvider = false;            // 支援火力的身份（可被呼叫）
    [Export] public int fireSupportRange = 2;                    // 呼叫半径（主攻手为圆心）
    [Export] public int fireSupportDamagePercent = 50;           // 支援伤害系数（50=半伤，0-200）
    [Export] public bool fireSupportConsumesAction = false;      // 支援方消耗行动
    [Export] public bool fireSupportUsesAmmo = true;             // 支援方须弹药充足才可响应
    [Export] public int fireSupportMaxResponders = 0;            // 最大响应数（0=不限）
    [Export] public string[] fireSupportWhitelist = new string[0]; // 可响应单位白名单（UnitTypeId/TemplateId，空=不限）
    [Export] public string[] fireSupportBlacklist = new string[0]; // 禁入黑名单
}

public partial class GameManager
{
    public static bool resolvingFireSupport = false;   // 防递归闸门

    // ========== 响应者枚举（资格集中门） ==========
    public List<Infantry> GetFireSupportResponders(Infantry attacker, Infantry target)
    {
        var result = new List<Infantry>();
        if (attacker == null || target == null || gridManager == null) return result;
        if (resolvingFireSupport) return result;
        int range = Mathf.Max(0, attacker.fireSupportRange);
        int max = attacker.fireSupportMaxResponders;
        foreach (var g in gridManager.grids)
        {
            if (g?.infantry == null) continue;
            var u = g.infantry;
            if (u == null || !IsInstanceValid(u) || u == attacker) continue;
            if (u.team != attacker.team) continue;
            if (!u.fireSupportProvider) continue;
            if (attacker.fireSupportWhitelist != null && attacker.fireSupportWhitelist.Length > 0
                && !attacker.fireSupportWhitelist.Contains(u.UnitTypeId) && !attacker.fireSupportWhitelist.Contains(u.TemplateId)) continue;
            if (attacker.fireSupportBlacklist != null
                && (attacker.fireSupportBlacklist.Contains(u.UnitTypeId) || attacker.fireSupportBlacklist.Contains(u.TemplateId))) continue;
            if (u.grid == null || attacker.grid == null) continue;
            int man = Mathf.Abs(u.grid.GridIndex.X - attacker.grid.GridIndex.X) + Mathf.Abs(u.grid.GridIndex.Y - attacker.grid.GridIndex.Y);
            if (man > range) continue;
            if (u.state == UnitState.Acted || u.hasActed) continue;
            if (attacker.fireSupportUsesAmmo && u.attackUsesAmmo && u.currentPrimaryAmmo <= 0) continue;
            result.Add(u);
            if (max > 0 && result.Count >= max) break;
        }
        return result;
    }

    // ========== 齐射触发：主攻手开火 → 支援火力跟随轰击 ==========
    public void TriggerFireSupport(Infantry attacker, Infantry target)
    {
        if (attacker == null || target == null || !attacker.fireSupportEnabled) return;
        if (resolvingFireSupport) return;
        var responders = GetFireSupportResponders(attacker, target);
        if (responders.Count == 0) return;
        resolvingFireSupport = true;
        foreach (var r in responders)
        {
            if (r == null || !IsInstanceValid(r) || !IsInstanceValid(target)) continue;
            // 支援 strike：免疫反击 + 伤害系数；Attack 内部自行校验射程/攻防表/隐身门（够不着自动静默）
            r.Attack(target, allowCounter: false, damagePercent: Mathf.Clamp(attacker.fireSupportDamagePercent, 0, 200));
            if (attacker.fireSupportConsumesAction)
            {
                r.isMoved = true;
                r.isAttacked = true;
                r.hasActed = true;
                r.state = UnitState.Acted;
                r.SetWaitVisual(true);
            }
        }
        resolvingFireSupport = false;
    }
}
