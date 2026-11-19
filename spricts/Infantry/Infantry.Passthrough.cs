using Godot;
using System;
using System.Linq;

// Infantry.Passthrough.cs —— 移动阻挡三勾选 + 穿过触发模块（开创人2026-11-19 定案）
// 阻挡：本单位的"被敌方单位/设施/兵器阻挡"三开关（敌方不再挡路即"穿过"的前提）
// 触发：路径中间格（不含终点）每有一名合法触发目标 → 自身付代价 + 目标付代价，一次移动逐格结算
// 资金：值直接加减（负=扣，正=加，无钳制）；血/弹/油：正值为消耗量（负=返还），血量下限保1
// 弹药：八选勾多选式（勾选哪个扣哪个，数值共用一份增减量）
public partial class Infantry
{
    [ExportGroup("移动阻挡（本单位）")]
    [Export] public bool blockedByEnemyUnits = true;
    [Export] public bool blockedByEnemyFacilities = false;
    [Export] public bool blockedByEnemyWeapons = true;

    [ExportGroup("穿过触发模块")]
    [Export] public bool passthroughEnabled = false;
    [Export] public bool passthroughTriggerEnemy = true;
    [Export] public bool passthroughTriggerFriendly = false;
    [Export] public bool passthroughTriggerNeutral = false;
    [Export] public string[] passthroughBlacklistUnits = new string[0];
    [Export] public string[] passthroughBlacklistWeapons = new string[0];
    [Export] public string[] passthroughBlacklistFacilities = new string[0];
    [Export] public int passthroughSelfHp = 0;
    [ExportGroup("穿过触发·自身弹药（勾选生效）")]
    [Export] public bool passthroughSelfAmmoPrimary = false;
    [Export] public bool passthroughSelfAmmoExplode = false;
    [Export] public bool passthroughSelfAmmoFlare = false;
    [Export] public bool passthroughSelfAmmoStealth = false;
    [Export] public bool passthroughSelfAmmoReveal = false;
    [Export] public bool passthroughSelfAmmoCamo = false;
    [Export] public bool passthroughSelfAmmoProduction = false;
    [Export] public bool passthroughSelfAmmoConstruction = false;
    [Export] public int passthroughSelfAmmoAmount = 0;
    [Export] public int passthroughSelfFuel = 0;
    [Export] public int passthroughSelfFunds = 0;
    [Export] public int passthroughTargetHp = 0;
    [ExportGroup("穿过触发·目标弹药（勾选生效）")]
    [Export] public bool passthroughTargetAmmoPrimary = false;
    [Export] public bool passthroughTargetAmmoExplode = false;
    [Export] public bool passthroughTargetAmmoFlare = false;
    [Export] public bool passthroughTargetAmmoStealth = false;
    [Export] public bool passthroughTargetAmmoReveal = false;
    [Export] public bool passthroughTargetAmmoCamo = false;
    [Export] public bool passthroughTargetAmmoProduction = false;
    [Export] public bool passthroughTargetAmmoConstruction = false;
    [Export] public int passthroughTargetAmmoAmount = 0;
    [Export] public int passthroughTargetFuel = 0;

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
        AddSelfAmmo();
        if (passthroughSelfFuel != 0) fuel = Mathf.Max(0, fuel - passthroughSelfFuel);
        if (gm != null && passthroughSelfFunds != 0)
            gm.SetTeamFunds(team, gm.GetTeamFunds(team) + passthroughSelfFunds);
    }

    private void ApplyTargetToUnit(Infantry u)
    {
        if (passthroughTargetHp != 0) u.health = Mathf.Max(1, u.health - passthroughTargetHp);
        AddTargetAmmo(u);
        if (passthroughTargetFuel != 0) u.fuel = Mathf.Max(0, u.fuel - passthroughTargetFuel);
        u.UpdateHpLabel();
    }

    private void ApplyTargetToWeapon(Weapon w)
    {
        if (passthroughTargetHp != 0) w.health = Mathf.Max(1, w.health - passthroughTargetHp);
        AddTargetAmmoToWeapon(w);
    }

    private void ApplyTargetToFacility(Facility f)
    {
        if (passthroughTargetHp != 0) f.health = Mathf.Max(1, f.health - passthroughTargetHp);
    }

    private void AddSelfAmmo()
    {
        int d = -passthroughSelfAmmoAmount;
        if (d == 0) return;
        if (passthroughSelfAmmoPrimary) currentPrimaryAmmo = Mathf.Max(0, currentPrimaryAmmo + d);
        if (passthroughSelfAmmoExplode) currentExplodeAmmo = Mathf.Max(0, currentExplodeAmmo + d);
        if (passthroughSelfAmmoFlare) currentFlareAmmo = Mathf.Max(0, currentFlareAmmo + d);
        if (passthroughSelfAmmoStealth) currentStealthBullets = Mathf.Max(0, currentStealthBullets + d);
        if (passthroughSelfAmmoReveal) currentRevealBullets = Mathf.Max(0, currentRevealBullets + d);
        if (passthroughSelfAmmoCamo) currentCamoBullets = Mathf.Max(0, currentCamoBullets + d);
        if (passthroughSelfAmmoProduction) currentProductionAmmo = Mathf.Max(0, currentProductionAmmo + d);
        if (passthroughSelfAmmoConstruction) currentConstructionAmmo = Mathf.Max(0, currentConstructionAmmo + d);
    }

    private void AddTargetAmmo(Infantry u)
    {
        int d = -passthroughTargetAmmoAmount;
        if (d == 0) return;
        if (passthroughTargetAmmoPrimary) u.currentPrimaryAmmo = Mathf.Max(0, u.currentPrimaryAmmo + d);
        if (passthroughTargetAmmoExplode) u.currentExplodeAmmo = Mathf.Max(0, u.currentExplodeAmmo + d);
        if (passthroughTargetAmmoFlare) u.currentFlareAmmo = Mathf.Max(0, u.currentFlareAmmo + d);
        if (passthroughTargetAmmoStealth) u.currentStealthBullets = Mathf.Max(0, u.currentStealthBullets + d);
        if (passthroughTargetAmmoReveal) u.currentRevealBullets = Mathf.Max(0, u.currentRevealBullets + d);
        if (passthroughTargetAmmoCamo) u.currentCamoBullets = Mathf.Max(0, u.currentCamoBullets + d);
        if (passthroughTargetAmmoProduction) u.currentProductionAmmo = Mathf.Max(0, u.currentProductionAmmo + d);
        if (passthroughTargetAmmoConstruction) u.currentConstructionAmmo = Mathf.Max(0, u.currentConstructionAmmo + d);
    }

    private void AddTargetAmmoToWeapon(Weapon w)
    {
        int d = -passthroughTargetAmmoAmount;
        if (d == 0) return;
        if (passthroughTargetAmmoPrimary) w.currentAmmo = Mathf.Max(0, w.currentAmmo + d);
        if (passthroughTargetAmmoExplode) w.wExplodeCurrent = Mathf.Max(0, w.wExplodeCurrent + d);
        if (passthroughTargetAmmoFlare) w.wFlareCurrent = Mathf.Max(0, w.wFlareCurrent + d);
        if (passthroughTargetAmmoStealth) w.wStealthBulletCurrent = Mathf.Max(0, w.wStealthBulletCurrent + d);
        if (passthroughTargetAmmoReveal) w.wRevealBulletCurrent = Mathf.Max(0, w.wRevealBulletCurrent + d);
        if (passthroughTargetAmmoCamo) w.wCamoBulletCurrent = Mathf.Max(0, w.wCamoBulletCurrent + d);
    }
}
