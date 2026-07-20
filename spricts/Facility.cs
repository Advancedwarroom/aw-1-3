using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Facility : Node2D
{
    public Grids ParentGrid { get; private set; }

    [ExportGroup("生产模块")]
    [Export] public bool canProduce = false;
    [Export] public Godot.Collections.Array<string> producibleUnitNames = new();
    [Export] public bool instantMoveAfterProduction = false; // 生产后是否可立即移动（默认false=生产后待机）
    [Export] public int maxProductionsPerTurn = 1;           // 每回合最多生产次数（0=禁止生产）
    [Export] public int productionsThisTurn = 0;             // 本回合已生产次数（运行时计数，切换回合刷新）

    // 本回合是否还能生产
    public bool CanProduceNow() => canProduce && productionsThisTurn < maxProductionsPerTurn;

    // 回合刷新：重置生产计数
    public void ResetProductionCount() => productionsThisTurn = 0;

    [ExportGroup("设施基础")]
    [Export] public string facilityTeam = "";
    [Export] public int capturePointsRequired = 20;
    [Export] public int healMode = 0;               // 0=固定数值(用healAmount), 1=固定百分比(用healPercent)
    [Export] public int healAmount = 0;             // 数值模式：每回合回血值（负扣血，零不动，正回血）
    [Export] public float healPercent = 0.0f;       // 百分比模式：每回合回血百分比（如0.2=20%最大HP，负值扣血）

    [ExportGroup("补给配置：数值驱动")]
    [Export] public int flareAmmoSupply = 0;     // 照明弹补给量（999=补满）
    [Export] public int explodeAmmoSupply = 0;   // 自爆弹补给量（999=补满）
    [Export] public int primaryAmmoSupply = 0;   // 主武器弹药补给量（999=补满）
    [Export] public int fuelSupply = 0;          // 燃料补给量（999=补满）

    [ExportGroup("补给支持兵种过滤（为空=不限制）")]
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForHeal = new();
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForFlare = new();
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForExplode = new();
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForPrimaryAmmo = new();
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForFuel = new();

    public List<Infantry> capturingUnits = new();
    private CaptureContestEffect contestEffect;

    public override void _Ready()
    {
        ZIndex = 5;
        ParentGrid = GetParent() as Grids;
        if (ParentGrid == null)
        {
            GD.PushError("[Facility] Facility 节点必须是 Grids 的子节点！");
            return;
        }

        UpdateCityVisual();
    }

    public bool CanBeProducedBy(string team)
    {
        if (!canProduce || string.IsNullOrEmpty(facilityTeam)) return false;
        if (facilityTeam == TeamHelper.Player0) return false; // P0中立设施不能生产
        if (facilityTeam == TeamHelper.Player) return true;   // P-1设施所有势力可用
        return facilityTeam == team;                          // P1/P2只能用自己的
    }

    public bool CanProduceUnit(string unitName)
    {
        return canProduce && producibleUnitNames.Contains(unitName);
    }

    // ========== 占领系统 ==========

    public bool CanBeCapturedBy(Infantry unit)
    {
        if (unit == null || facilityTeam == unit.team) return false;
        return true;
    }

    public void OnCaptureStarted(Infantry unit)
    {
        capturingUnits.RemoveAll(u => u == null || !IsInstanceValid(u));
        if (HasContest()) ShowCaptureContestVisual();
    }

    public void OnCaptureInterrupted(Infantry unit)
    {
        capturingUnits.RemoveAll(u => u == null || !IsInstanceValid(u));
        capturingUnits.Remove(unit);
        if (capturingUnits.Count > 0)
        {
            if (capturingUnits.Select(u => u.team).Distinct().Count() > 1)
            {
                foreach (var u in capturingUnits) u.currentCaptureProgress = 0;
                ShowCaptureContestVisual();
            }
        }
        else ClearCaptureVisual();
        UpdateCaptureVisual();
    }

    public void OnCaptureCompleted(Infantry unit)
    {
        foreach (var u in capturingUnits.ToList()) { if (u != unit) { u.currentCaptureProgress = 0; u.capturingGrid = null; } }
        capturingUnits.Clear();
        facilityTeam = unit.team;
        ClearCaptureVisual();
        ShowCaptureCompletedVisual(unit.team);
    }

    public bool HasContest()
    {
        if (ParentGrid == null) return false;
        var capturing = ParentGrid.infantries
            .Where(u => u.currentCaptureProgress > 0 && u.capturingGrid == ParentGrid)
            .ToList();
        capturing.RemoveAll(u => u == null || !IsInstanceValid(u));
        if (capturing.Count < 2) return false;
        return capturing.Select(u => u.team).Distinct().Count() > 1;
    }

    // ========== 视觉系统 ==========

    public virtual void UpdateCityVisual()
    {
        if (ParentGrid == null) return;
        var sprite = ParentGrid.GetNodeOrNull<Sprite2D>("Sprite2D");
        if (sprite == null) return;

        Color teamColor = facilityTeam switch
        {
            "Player1" => new Color(1f, 0.3f, 0.3f),
            "Player2" => new Color(0.3f, 0.5f, 1f),
            "Player0" => new Color(0.8f, 0.8f, 0.8f),
            "Player" => new Color(0.7f, 0.4f, 0.9f),
            _ => Colors.White
        };

        var tween = CreateTween();
        tween.TweenProperty(sprite, "modulate", teamColor, 0.5f);

    }

    public void ShowCaptureContestVisual()
    {
        if (contestEffect != null && IsInstanceValid(contestEffect)) contestEffect.QueueFree();
        contestEffect = new CaptureContestEffect { Position = new Godot.Vector2(0, -20), ZIndex = 200 };
        AddChild(contestEffect);
    }

    public void ShowCaptureCompletedVisual(string newTeam)
    {
        ClearCaptureVisual();
        var effect = new CaptureEffect
        {
            CaptureProgress = 100f,
            TeamColor = newTeam == "Player1" ? new Color(1f, 0.2f, 0.2f) : new Color(0.2f, 0.4f, 1f),
            IsCapturing = false, Position = new Godot.Vector2(0, -10), ZIndex = 200
        };
        AddChild(effect);
    }

    private void UpdateCaptureVisual() { }

    public void ClearContestVisual()
    {
        if (contestEffect != null && IsInstanceValid(contestEffect)) { contestEffect.QueueFree(); contestEffect = null; }
    }

    private void ClearCaptureVisual()
    {
        if (contestEffect != null && IsInstanceValid(contestEffect)) { contestEffect.QueueFree(); contestEffect = null; }
    }

    // ========== 补给系统 ==========

    public void PerformSupply()
    {
        if (ParentGrid == null) return;
        ParentGrid.infantries?.RemoveAll(i => !IsInstanceValid(i));
        bool anySupplied = false;

        foreach (var unit in ParentGrid.infantries)
        {
            if (unit == null || !IsInstanceValid(unit)) continue;
            bool canSupply = TeamHelper.CanFacilitySupply(facilityTeam, unit.team);
            if (!canSupply) continue;

            string unitType = unit.GetType().Name;
            bool supplied = false;
            string supplyText = "";
            Color supplyColor = Colors.Green;
            var supplyParts = new List<string>();

            // 1. 回血（数值模式 或 百分比模式）
            int healValue = 0;
            if (healMode == 0)
                healValue = healAmount;
            else if (healMode == 1)
                healValue = Mathf.RoundToInt(unit.maxHealth * healPercent);

            if (healValue != 0)
            {
                if (supportedUnitTypesForHeal.Count == 0 || supportedUnitTypesForHeal.Contains(unitType))
                {
                    int oldHealth = unit.health;
                    unit.health += healValue;
                    if (!unit.explosionCanExceedMaxHealth)
                        unit.health = Mathf.Clamp(unit.health, 1, unit.maxHealth);
                    else
                        unit.health = Mathf.Max(1, unit.health);
                    int delta = unit.health - oldHealth;
                    if (delta != 0)
                    {
                        supplied = true;
                        supplyParts.Add(delta > 0 ? $"+{delta}HP" : $"{delta}HP");
                        supplyColor = new Color(0.2f, 0.9f, 0.3f);
                    }
                }
            }

            // 2. 主武器弹药补给
            if (primaryAmmoSupply != 0)
            {
                if (unit.hasPrimaryWeapon && unit.primaryHasLimitedAmmo && unit.maxPrimaryAmmo < 99)
                {
                    if (supportedUnitTypesForPrimaryAmmo.Count == 0 || supportedUnitTypesForPrimaryAmmo.Contains(unitType))
                    {
                        int oldAmmo = unit.currentPrimaryAmmo;
                        if (primaryAmmoSupply >= 999)
                            unit.currentPrimaryAmmo = unit.maxPrimaryAmmo;
                        else
                            unit.currentPrimaryAmmo = Mathf.Clamp(unit.currentPrimaryAmmo + primaryAmmoSupply, 0, unit.maxPrimaryAmmo);
                        if (unit.currentPrimaryAmmo != oldAmmo)
                        {
                            supplied = true;
                            supplyParts.Add("Ammo+");
                            if (supplyColor == Colors.Green) supplyColor = new Color(0.9f, 0.3f, 0.2f);
                        }
                    }
                }
            }

            // 3. 燃料补给
            if (fuelSupply != 0)
            {
                if (unit.consumeFuel)
                {
                    if (supportedUnitTypesForFuel.Count == 0 || supportedUnitTypesForFuel.Contains(unitType))
                    {
                        int oldFuel = unit.fuel;
                        if (fuelSupply >= 999)
                            unit.fuel = unit.maxFuel;
                        else
                            unit.fuel = Mathf.Clamp(unit.fuel + fuelSupply, 0, unit.maxFuel);
                        if (unit.fuel != oldFuel)
                        {
                            supplied = true;
                            supplyParts.Add("Fuel+");
                            if (supplyColor == Colors.Green) supplyColor = new Color(0.9f, 0.7f, 0.2f);
                        }
                    }
                }
            }

            // 4. 照明弹补给
            if (flareAmmoSupply != 0)
            {
                if (unit.canIlluminate)
                {
                    if (supportedUnitTypesForFlare.Count == 0 || supportedUnitTypesForFlare.Contains(unitType))
                    {
                        int oldFlare = unit.currentFlareAmmo;
                        if (flareAmmoSupply >= 999)
                            unit.currentFlareAmmo = unit.maxFlareAmmo;
                        else
                            unit.currentFlareAmmo = Mathf.Clamp(unit.currentFlareAmmo + flareAmmoSupply, 0, unit.maxFlareAmmo);
                        if (unit.currentFlareAmmo != oldFlare)
                        {
                            supplied = true;
                            supplyParts.Add("Flare+");
                            if (supplyColor == Colors.Green) supplyColor = new Color(0.9f, 0.9f, 0.2f);
                        }
                    }
                }
            }

            // 5. 自爆弹补给
            if (explodeAmmoSupply != 0)
            {
                if (unit.canExplode && !unit.explosionDestroysSelf) // 只有无限自爆模式的单位才需要自爆弹
                {
                    if (supportedUnitTypesForExplode.Count == 0 || supportedUnitTypesForExplode.Contains(unitType))
                    {
                        int oldExplode = unit.currentExplodeAmmo;
                        if (explodeAmmoSupply >= 999)
                            unit.currentExplodeAmmo = unit.maxExplodeAmmo;
                        else
                            unit.currentExplodeAmmo = Mathf.Clamp(unit.currentExplodeAmmo + explodeAmmoSupply, 0, unit.maxExplodeAmmo);
                        if (unit.currentExplodeAmmo != oldExplode)
                        {
                            supplied = true;
                            supplyParts.Add("Bomb+");
                            if (supplyColor == Colors.Green) supplyColor = new Color(0.9f, 0.5f, 0.1f);
                        }
                    }
                }
            }

            if (supplied)
            {
                anySupplied = true;
                supplyText = string.Join(" ", supplyParts);
                unit.UpdateHpLabel();
                ShowSupplyEffectNew(unit, supplyText, supplyColor);
            }
        }
        if (anySupplied) ShowGridSupplyEffectNew();
    }

    private void ShowSupplyEffectNew(Infantry unit, string text, Color color)
    {
        var effect = new SupplyEffect(); AddChild(effect);
        effect.Setup(unit.Position - GlobalPosition, text, color);
    }

    private void ShowGridSupplyEffectNew() { var effect = new GridSupplyEffect(); AddChild(effect); }

    // ========== 设施视野（所属势力提供自身格子的视野）==========

    public HashSet<Grids> CalculateFacilityVision(GameManager gm)
    {
        var visible = new HashSet<Grids>();
        if (gm == null || gm.gridManager?.map == null) return visible;
        if (TeamHelper.DoesFacilityProvideVision(facilityTeam) && ParentGrid != null)
            visible.Add(ParentGrid);
        return visible;
    }
}
