// ComTower.cs - 通信塔（Communication Tower）
// 原作AWDS：占领后提升己方单位攻击力。本作规格：模块化Export，可对多种属性加成/减成。
// 通信塔模块Export：1.是否开启 2.占领后对血量/弹药/燃料/攻击力/防御力/移动力的加成减成 3.是否自动化
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class ComTower : Facility
{
    // Communication Tower base frames use a warm-red source palette.
    protected override float GetBaseArtHue() => 53.0f / 360.0f;

    // ========== 通信塔模块 ==========
    [ExportGroup("通信塔模块")]
    /// <summary>1. 是否开启通信塔模块</summary>
    [Export] public bool comTowerEnabled = true;
    /// <summary>3. 是否自动化（非自动需操作菜单按钮激活）</summary>
    [Export] public bool comTowerAuto = true;

    // ========== 2. 加成/减成（百分比，正=加成 负=减成） ==========
    [ExportGroup("通信塔 - 属性加成/减成（百分比）")]
    /// <summary>血量（实际HP）加成%：每回合恢复/扣减</summary>
    [Export] public int hpBonusPercent = 0;
    /// <summary>血量上限加成%</summary>
    [Export] public int maxHpBonusPercent = 0;
    /// <summary>各种弹药加成%</summary>
    [Export] public int ammoBonusPercent = 0;
    /// <summary>燃料加成%</summary>
    [Export] public int fuelBonusPercent = 0;
    /// <summary>攻击力加成%</summary>
    [Export] public int attackBonusPercent = 10;
    /// <summary>固定防御力加成%</summary>
    [Export] public int defenseBonusPercent = 0;
    /// <summary>移动加成%：正=增加移动力</summary>
    [Export] public int moveBonusPercent = 0;
    /// <summary>移动力固定加值（格）</summary>
    [Export] public int moveBonusFlat = 0;

    // ========== 运行时 ==========
    /// <summary>本回合是否已激活（非自动模式需按钮）</summary>
    public bool comTowerActivatedThisTurn = false;
    /// <summary>全局已激活通信塔效果（供攻击/防御/移动计算读取）</summary>
    public static int ActiveAttackBonus = 0;
    public static int ActiveDefenseBonus = 0;
    public static int ActiveMoveBonusFlat = 0;

    protected override (string neutral, string red, string blue)? GetTeamAnimNames()
        => ("P0ComTower", "comtowerteam1", "comtowerteam2");

    public override void _Ready()
    {
        base._Ready();
    }

    /// <summary>通信塔是否生效（需开启+己方占领+（自动或已激活））</summary>
    public bool IsComTowerActive()
    {
        if (!comTowerEnabled) return false;
        if (string.IsNullOrEmpty(facilityTeam) || facilityTeam == TeamHelper.Player0) return false;
        if (comTowerAuto) return true;
        return comTowerActivatedThisTurn;
    }

    /// <summary>✅ 激活通信塔（非自动模式：菜单按钮调用）</summary>
    public void ActivateComTower()
    {
        comTowerActivatedThisTurn = true;
        ApplyComTowerEffects();
    }

    /// <summary>✅ 应用通信塔效果：给己方单位加成/减成</summary>
    public void ApplyComTowerEffects()
    {
        if (!IsComTowerActive()) return;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.unitManager == null) return;

        // 全局攻击/防御/移动加成（供战斗/移动计算读取）
        ActiveAttackBonus = attackBonusPercent;
        ActiveDefenseBonus = defenseBonusPercent;
        ActiveMoveBonusFlat = moveBonusFlat;

        // 逐单位应用血量/弹药/燃料加成
        foreach (var unit in gm.unitManager.AllUnits.ToList())
        {
            if (unit == null || !IsInstanceValid(unit) || unit.team != facilityTeam) continue;
            ApplyUnitBonuses(unit);
        }
    }

    /// <summary>给单个单位应用加成/减成</summary>
    private void ApplyUnitBonuses(Infantry unit)
    {
        // 血量（实际HP）：按百分比恢复/扣减
        if (hpBonusPercent != 0)
        {
            int delta = Mathf.RoundToInt(unit.health * hpBonusPercent / 100f);
            unit.health = Mathf.Clamp(unit.health + delta, 1, unit.GetEffectiveMaxHealth());
        }
        // 血量上限：按百分比增减
        if (maxHpBonusPercent != 0)
            unit.maxHealth = Mathf.Max(1, Mathf.RoundToInt(unit.maxHealth * (100 + maxHpBonusPercent) / 100f));
        // 燃料：按百分比恢复/扣减
        if (fuelBonusPercent != 0)
        {
            int delta = Mathf.RoundToInt(unit.fuel * fuelBonusPercent / 100f);
            unit.fuel = Mathf.Clamp(unit.fuel + delta, 0, unit.maxFuel);
        }
        // 弹药：按百分比恢复/扣减
        if (ammoBonusPercent != 0)
        {
            // 遍历所有弹药类型
            var ammoFields = new[] { "primaryAmmo", "flareAmmo", "explodeAmmo" };
            foreach (var fname in ammoFields)
            {
                var fi = typeof(Infantry).GetField(fname);
                if (fi == null) continue;
                int cur = (int)fi.GetValue(unit);
                int delta = Mathf.RoundToInt(cur * ammoBonusPercent / 100f);
                fi.SetValue(unit, Mathf.Max(0, cur + delta));
            }
        }
        unit.UpdateHpLabel();
    }

    /// <summary>每回合开始：自动模式则自动应用</summary>
    public void OnTurnStartComTower()
    {
        comTowerActivatedThisTurn = comTowerAuto;
        if (IsComTowerActive()) ApplyComTowerEffects();
    }

    /// <summary>占领后自动应用（占领时）</summary>
    public override void OnCaptureCompleted(Infantry unit)
    {
        base.OnCaptureCompleted(unit);
        if (IsComTowerActive()) ApplyComTowerEffects();
    }
}
