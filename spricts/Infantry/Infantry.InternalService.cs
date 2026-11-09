// Infantry.cs - 内部服务（舱内单位）：内部补给 + 内部回血
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// Infantry · InternalService（内部服务：给舱内搭载单位补给/回血）
// 由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注
// ✅ 两个独立白名单：internalSupplyWhitelist / internalHealWhitelist（互相独立，也与搭载白名单独立）
// ✅ 每个大回合开始时结算一次（挂在 GameManager.Turn 的每日结算里）
public partial class Infantry
{
    [ExportGroup("内部服务（舱内单位）")]
    [Export] public bool internalSupplyEnabled = false;   // 内部补给：每回合给舱内单位补满燃料+全弹药
    [Export] public Godot.Collections.Array<string> internalSupplyWhitelist = new();  // 内部补给白名单（空=所有舱内单位）
    [Export] public bool internalHealEnabled = false;     // 内部回血开关
    [Export] public int internalHealAmount = 0;          // 每回合给舱内单位回 x HP（可为负=扣血，扣到 0 以下销毁）
    [Export] public Godot.Collections.Array<string> internalHealWhitelist = new();    // 内部回血白名单（空=所有舱内单位）

    /// <summary>白名单判定：空表=不限制</summary>
    private bool PassesInternalWhitelist(Godot.Collections.Array<string> whitelist, Infantry u)
    {
        if (whitelist == null || whitelist.Count == 0) return true;
        return whitelist.Contains(u.UnitTypeId);
    }

    /// <summary>
    /// 每日结算：内部补给 + 内部回血（由 GameManager.Turn 在大回合开始时调用）
    /// </summary>
    public void ProcessInternalServices()
    {
        if (transportedUnits == null || transportedUnits.Count == 0) return;

        foreach (var u in transportedUnits.ToList())
        {
            if (u == null || !IsInstanceValid(u)) continue;
            ApplyInternalExportService(u);

            // ① 内部补给：燃料+全弹药补满（统一弹药系统）
            if (internalSupplyEnabled && PassesInternalWhitelist(internalSupplyWhitelist, u))
            {
                if (u.consumeFuel) u.fuel = u.GetEffectiveMaxFuel();
                u.RefillAllAmmo();
                u.UpdateHpLabel();
            }

            // ② 内部回血：x 可为负（扣血；扣到 0 以下=舱内单位损毁）
            if (internalHealEnabled && internalHealAmount != 0
                && PassesInternalWhitelist(internalHealWhitelist, u))
            {
                ApplyInternalHeal(u);
            }
        }
    }

    private void ApplyInternalHeal(Infantry u)
    {
        u.health += internalHealAmount;
        if (u.health <= 0)
        {
            // 扣血扣死了：移出货舱并销毁
            transportedUnits.Remove(u);
            u.QueueFree();
            return;
        }
        u.health = Mathf.Clamp(u.health, 1, u.GetEffectiveMaxHealth());
        u.UpdateHpLabel();
    }
}
