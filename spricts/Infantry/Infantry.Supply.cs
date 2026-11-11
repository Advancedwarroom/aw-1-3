using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · Supply（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{

    [ExportGroup("补给")]
    [Export] public string[] supplyBlacklist = System.Array.Empty<string>();  // 雾战独立攻防表（固定数值，禁小数）
    [Export] public bool supplyCanCrossScreens = false; // 补给范围是否允许跨屏
    [Export] public bool useDefaultConfig = true;           // ✅ 是否使用代码默认值（false=完全由Inspector控制，不执行ApplyUnitSpecificDefaults）
    [Export] public bool canDevour = false;
    [Export] public bool canDevourAllies = true;   // 吞噬友军（AW原作史莱姆不分敌我；关掉则只吞噬敌军）                // 是否能吞噬敌人（Oozium特性）
    [Export] public bool canCapture = true;                // 是否能占领城市
    [Export] public UnitCategory unitCategory = UnitCategory.Other;  // 手动指定分类

    [Export] public int cost = 1000;  // 单位造价（可在Inspector中修改，非硬编码）

// ========== ✅ 究极自由：补给范围计算（从 APC 迁移） ==========
public virtual List<Grids> CalculateSupplyRange()
{
    var result = new List<Grids>();
    var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
    if (gm?.gridManager == null || grid == null) return result;

    var allRange = gm.gridManager.FindRange(grid, maxSupplyRange, false, supplyCanCrossScreens);

    if (minSupplyRange > 1)
    {
        var blindZone = gm.gridManager.FindRange(grid, minSupplyRange - 1, false, supplyCanCrossScreens);
        allRange = allRange.Where(g => !blindZone.Contains(g)).ToList();
    }

    allRange = allRange.Where(g => g != grid).ToList();

    return allRange;
}

// ========== ✅ 究极自由：获取补给范围内的友方单位（从 APC 迁移） ==========
public virtual List<Infantry> GetSupplyRangeUnits()
{
    var result = new List<Infantry>();
    var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
    if (gm?.gridManager == null) return result;
    
    var supplyRange = CalculateSupplyRange();
    
    foreach (var g in supplyRange)
    {
        if (g == null) continue;
        
        foreach (var unit in g.infantries)
        {
            if (unit != null && IsInstanceValid(unit) && unit.team == this.team && unit != this)
            {
                result.Add(unit);
            }
        }
    }
    
    return result;
}

// ========== ✅ 究极自由：补给黑炮弹药（从 APC 迁移） ==========
public virtual bool ResupplyBlackCannon(Weapon weapon)
{
    return ResupplyWeapon(weapon);
}

// ========== ✅ 究极自由：获取范围内需要弹药补给的黑炮（从 APC 迁移） ==========
public virtual List<Weapon> GetBlackCannonsNeedingAmmo()
{
    return GetWeaponsNeedingAmmo();
}

// ========== ✅ 究极自由：执行补给（从 APC 迁移） ==========
public virtual void PerformSupply()
{
    if (!canSupplyUnits) return;  // 没有补给能力的单位不能执行补给
{
    bool anySupplied = false;

    // 1. 补给单位
    var targetUnits = GetSupplyRangeUnits();
    foreach (var unit in targetUnits)
    {
        if (supplyBlacklist != null && supplyBlacklist.Length > 0
            && System.Array.IndexOf(supplyBlacklist, unit.UnitTypeId) >= 0) continue;
        bool supplied = false;
        string supplyText = "";
        Color supplyColor = new Color(0.2f, 0.9f, 0.3f);

        // ✅ 统一弹药系统：全弹药种类补满（主武器/自爆/照明/隐身弹/显形弹/迷彩弹/生产弹/建造弹）
        //    只有确实补到了的弹药才计入补给提示
        foreach (var kind in Infantry.AllAmmoKinds)
        {
            int max = unit.GetMaxAmmo(kind);
            if (max <= 0) continue;
            if (unit.GetAmmo(kind) >= max) continue;
            // 主弹药：无主武器/无限弹不补
            if (kind == Infantry.AmmoKind.Primary && (!unit.hasPrimaryWeapon || !unit.primaryHasLimitedAmmo)) continue;
            if (kind == Infantry.AmmoKind.Explode && (!unit.canExplode || unit.explosionDestroysSelf)) continue;
            if (kind == Infantry.AmmoKind.Flare && !unit.canIlluminate) continue;
            unit.RefillAmmo(kind);
            supplied = true;
            supplyText = string.IsNullOrEmpty(supplyText) ? "Ammo+" : supplyText + " Ammo+";
        }

        // 补给燃料
        if (unit.consumeFuel && unit.fuel < unit.maxFuel)
        {
            unit.fuel = unit.maxFuel;
            if (!supplied)
            {
                supplyText = "Fuel+";
                supplyColor = new Color(0.9f, 0.7f, 0.2f);
            }
            else
            {
                supplyText += " Fuel+";
            }
            supplied = true;
        }

        if (supplied)
        {
            anySupplied = true;
            unit.UpdateHpLabel();
            ShowSupplyEffect(unit, supplyText, supplyColor);
        }
    }

    var weaponsNeedingAmmo = GetWeaponsNeedingAmmo();
    foreach (var weapon in weaponsNeedingAmmo)
    {
        if (ResupplyWeapon(weapon))
        {
            anySupplied = true;
        }
    }

    if (anySupplied)
    {
        ShowTransportSupplyEffect();
    }
}
}
private void ShowSupplyEffect(Infantry unit, string text, Color color)
{
    var effect = new SupplyEffect();
    if (grid != null)
    {
        var relativePos = unit.GlobalPosition - this.GlobalPosition;
        effect.Setup(relativePos, text, color);
        AddChild(effect);
    }
}

private void ShowTransportSupplyEffect()
{
    var effect = new GridSupplyEffect();
    AddChild(effect);
}

// ========== ✅ 究极自由：补给按钮回调（从 APC 迁移） ==========
public virtual void OnSupplySelected()
{
    PerformSupply();
    
    isMoved = true;
    isAttacked = true;
    hasActed = true;
    state = UnitState.Acted;
    originalGrid = null;
    
    SetWaitVisual(true);
    
    var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
    gm?.ClearSelectedInfantry();
}

// ========== ✅ 究极自由：回合开始自动补给（从 APC 迁移） ==========
public virtual void OnTurnStartAutoSupply()
{
    PerformSupply();
}
}
