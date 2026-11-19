using Godot;
using System;
using System.Linq;

// Infantry.Passthrough.cs —— 移动阻挡三勾选 + 穿过触发模块（开创人 2026-11-19 定案）
// 阻挡：本单位的"被敌方单位/设施/兵器阻挡"三开关（敌方不再挡路即"穿过"的前提）
// 触发：路径中间格（不含终点）每有一名合法触发目标 → 自身付代价 + 目标付代价，一次移动逐格结算
// 资金：值直接加减（负=扣，正=加，无钳制）；血/弹/油：正值为消耗量（负=返还），血量下限保1
public partial class Infantry
{
    [ExportGroup("移动阻挡（本单位）")]
    [Export] public bool blockedByEnemyUnits = true;         // 被敌方单位阻挡（false=可穿过敌占格）
    [Export] public bool blockedByEnemyFacilities = false;     // 被敌方设施阻挡（默认关=设施不挡路）
    [Export] public bool blockedByEnemyWeapons = true;         // 被敌方兵器阻挡（false=可穿过敌兵器格）

    [ExportGroup("穿过触发模块")]
    [Export] public bool passthroughEnabled = false;
    [Export] public bool passthroughTriggerEnemy = true;       // 触发：敌方
    [Export] public bool passthroughTriggerFriendly = false;   // 触发：友方（同队/同盟）
    [Export] public bool passthroughTriggerNeutral = false;      // 触发：中立（Player0）
    [Export] public string[] passthroughBlacklistUnits = new string[0];       // 黑名单（以下单位不触发）
    [Export] public string[] passthroughBlacklistWeapons = new string[0];       // 黑名单（以下兵器不触发）
    [Export] public string[] passthroughBlacklistFacilities = new string[0];    // 黑名单（以下设施不触发）
    [Export] public int passthroughSelfHp = 0;                 // 自身扣血（正=扣，负=回；下限保1）
    [Export(PropertyHint.Enum, "无,主弹,自爆弹,照明弹,隐身弹,显形弹,迷彩弹,生产弹,建造弹")]
    public int passthroughSelfAmmoKind = 0;
    [Export] public int passthroughSelfAmmo = 0;               // 自身弹药（正=扣，负=补）
    [Export] public int passthroughSelfFuel = 0;               // 自身燃油（正=扣，负=补）
    [Export] public int passthroughSelfFunds = 0;              // 自身势力资金（直接加减：负=扣，正=加）
    [Export] public int passthroughTargetHp = 0;               // 目标扣血（正=扣，负=回；下限保1）
    [Export(PropertyHint.Enum, "无,主弹,自爆弹,照明弹,隐身弹,显形弹,迷彩弹,生产弹,建造弹")]
    public int passthroughTargetAmmoKind = 0;
    [Export] public int passthroughTargetAmmo = 0;             // 目标弹药
    [Export] public int passthroughTargetFuel = 0;             // 目标燃油（兵器/设施无燃油字段则自动跳过）

    // ==================== 入口：移动层对路径中间格调用 ====================
    public void ApplyPassthroughOn(Grids cell, GameManager gm)
    {
        if (!passthroughEnabled || cell == null) return;

        foreach (var u in cell.infantries)
        {
            if (u == null || !IsInstanceValid(u) || u == this) continue;
            if (IsBlacklisted(passthroughBlacklistUnits, u.UnitTypeId, u.GetType().Name)) continue;
            if (!FactionHit(u.team)) continue;
            ApplyTargetToUnit(u);
        }

        var w = cell.weapon;
        if (w != null && IsInstanceValid(w)
            && !IsBlacklisted(passthroughBlacklistWeapons, w.GetType().Name)
            && FactionHit(w.team))
            ApplyTargetToWeapon(w);

        var c = cell.city;
        if (c != null && IsInstanceValid(c)
            && !IsBlacklisted(passthroughBlacklistFacilities, c.GetType().Name)
            && FactionHit(c.facilityTeam))
            ApplyTargetToFacility(c);

        ApplySelfCost(gm);
        UpdateHpLabel();
    }

    private bool FactionHit(string targetTeam)
    {
        if (passthroughTriggerEnemy && TeamHelper.IsEnemyTeam(team, targetTeam)) return true;
        if (passthroughTriggerFriendly && (TeamHelper.IsFriendlyTeam(team, targetTeam) || TeamHelper.IsAlliedTeam(team, targetTeam))) return true;
        if (passthroughTriggerNeutral && targetTeam == TeamHelper.Player0) return true;
        return false;
    }

    private static bool IsBlacklisted(string[] list, params string[] names)
    {
        if (list == null || list.Length == 0) return false;
        return names.Any(n => !string.IsNullOrEmpty(n) && list.Contains(n));
    }

    private void ApplySelfCost(GameManager gm)
    {
        if (passthroughSelfHp != 0) health = Mathf.Max(1, health - passthroughSelfHp);
        AddAmmoByKind(this, passthroughSelfAmmoKind, -passthroughSelfAmmo);      // 正=消耗
        if (passthroughSelfFuel != 0) fuel = Mathf.Max(0, fuel - passthroughSelfFuel);
        if (gm != null && passthroughSelfFunds != 0)
            gm.SetTeamFunds(team, gm.GetTeamFunds(team) + passthroughSelfFunds);   // 直接加减（负=扣）
    }

    private void ApplyTargetToUnit(Infantry u)
    {
        if (passthroughTargetHp != 0) u.health = Mathf.Max(1, u.health - passthroughTargetHp);
        AddAmmoByKind(u, passthroughTargetAmmoKind, -passthroughTargetAmmo);
        if (passthroughTargetFuel != 0) u.fuel = Mathf.Max(0, u.fuel - passthroughTargetFuel);
        u.UpdateHpLabel();
    }

    private void ApplyTargetToWeapon(Weapon w)
    {
        if (passthroughTargetHp != 0) w.health = Mathf.Max(1, w.health - passthroughTargetHp);
        AddAmmoToWeapon(w, passthroughTargetAmmoKind, -passthroughTargetAmmo);
    }

    private void ApplyTargetToFacility(Facility f)
    {
        if (passthroughTargetHp != 0) f.health = Mathf.Max(1, f.health - passthroughTargetHp);
    }

    // 弹药统一通道：单位八弹种
    private static void AddAmmoByKind(Infantry u, int kind, int delta)
    {
        if (delta == 0) return;
        switch (kind)
        {
            case 1: u.currentPrimaryAmmo = Mathf.Max(0, u.currentPrimaryAmmo + delta); break;
            case 2: u.currentExplodeAmmo = Mathf.Max(0, u.currentExplodeAmmo + delta); break;
            case 3: u.currentFlareAmmo = Mathf.Max(0, u.currentFlareAmmo + delta); break;
            case 4: u.currentStealthBullets = Mathf.Max(0, u.currentStealthBullets + delta); break;
            case 5: u.currentRevealBullets = Mathf.Max(0, u.currentRevealBullets + delta); break;
            case 6: u.currentCamoBullets = Mathf.Max(0, u.currentCamoBullets + delta); break;
            case 7: u.currentProductionAmmo = Mathf.Max(0, u.currentProductionAmmo + delta); break;
            case 8: u.currentConstructionAmmo = Mathf.Max(0, u.currentConstructionAmmo + delta); break;
        }
    }

    // 弹药统一通道：兵器六弹种（生产/建造弹兵器无字段，自动跳过）
    private static void AddAmmoToWeapon(Weapon w, int kind, int delta)
    {
        if (delta == 0) return;
        switch (kind)
        {
            case 1: w.currentAmmo = Mathf.Max(0, w.currentAmmo + delta); break;
            case 2: w.wExplodeCurrent = Mathf.Max(0, w.wExplodeCurrent + delta); break;
            case 3: w.wFlareCurrent = Mathf.Max(0, w.wFlareCurrent + delta); break;
            case 4: w.wStealthBulletCurrent = Mathf.Max(0, w.wStealthBulletCurrent + delta); break;
            case 5: w.wRevealBulletCurrent = Mathf.Max(0, w.wRevealBulletCurrent + delta); break;
            case 6: w.wCamoBulletCurrent = Mathf.Max(0, w.wCamoBulletCurrent + delta); break;
        }
    }
}
