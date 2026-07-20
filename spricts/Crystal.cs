// Crystal.cs - 黑水晶（Black Crystal）兵器
// 继承 Weapon 基类，实现环形范围治疗+补给系统
// 机制：
// 1. 作用范围 n~m（环形/donut范围，n=0时无盲区）
// 2. 范围回血：百分比，可超上限（自定义阈值），负值=扣血，独立特效
// 3. 队伍判定：0=不分敌我 1=仅我方 2=仅敌方
// 4. 单位类型判定：准许类型列表过滤
// 5. 能否给兵器回血
// 6. 范围补给：Flare弹/自爆弹/Ammo/燃料，每种分别计算，支持类型+超上限+自定义上限
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Crystal : Weapon
{
    // ========== 黑水晶专用配置 ==========
    [ExportGroup("黑水晶 - 范围治疗")]
    [Export] public int crystalHealMinRange = 0;        // 治疗最小范围（盲区）
    [Export] public int crystalHealMaxRange = 3;        // 治疗最大范围
    [Export] public float crystalHealPercent = 0.2f;    // 治疗百分比（0.2=20%）
    [Export] public int crystalHealTeamMode = 1;          // 0=不分敌我 1=仅我方 2=仅敌方
    [Export] public Godot.Collections.Array<string> crystalHealUnitTypes = new(); // 准许类型（空=所有类型）
    [Export] public bool crystalHealAffectsWeapons = true; // 能否给兵器回血
    [Export] public bool crystalHealCanOverMaxHp = false;  // 能否超过最大血量
    [Export] public int crystalHealMaxHpCapPercent = 100;    // 超过上限时的阈值（100=100%最大血量）

    [ExportGroup("黑水晶 - 范围补给")]
    [Export] public int crystalSupplyMinRange = 0;      // 补给最小范围
    [Export] public int crystalSupplyMaxRange = 3;      // 补给最大范围
    [Export] public int crystalSupplyTeamMode = 1;      // 0=不分敌我 1=仅我方 2=仅敌方
    [Export] public Godot.Collections.Array<string> crystalSupplyUnitTypes = new(); // 准许类型（空=所有类型）
    [Export] public bool crystalSupplyAffectsWeapons = true; // 能否给兵器补给

    // --- 补给消耗弹药设置（防止无限卡 Bug）---
    [Export] public bool crystalSupplyConsumesAmmo = true;      // 补给是否消耗自身弹药
    [Export] public int crystalSupplyAmmoCostPerTarget = 1;      // 每个补给目标消耗弹药数
    [Export] public bool crystalSupplyAffectsSelf = false;       // 补给是否对自身生效（默认 false 防无限循环）

    // --- Flare 弹补给 ---
    [Export] public int crystalSupplyFlareAmmo = 0;
    [Export] public Godot.Collections.Array<string> crystalSupplyFlareSupportedTypes = new();
    [Export] public bool crystalSupplyFlareCanOverMax = false;
    [Export] public int crystalSupplyFlareMaxCap = 999;

    // --- 自爆弹补给 ---
    [Export] public int crystalSupplyExplodeAmmo = 0;
    [Export] public Godot.Collections.Array<string> crystalSupplyExplodeSupportedTypes = new();
    [Export] public bool crystalSupplyExplodeCanOverMax = false;
    [Export] public int crystalSupplyExplodeMaxCap = 999;

    // --- 主武器弹药补给 ---
    [Export] public int crystalSupplyPrimaryAmmo = 0;
    [Export] public Godot.Collections.Array<string> crystalSupplyPrimarySupportedTypes = new();
    [Export] public bool crystalSupplyPrimaryCanOverMax = false;
    [Export] public int crystalSupplyPrimaryMaxCap = 999;

    // --- 燃料补给 ---
    [Export] public int crystalSupplyFuel = 0;
    [Export] public Godot.Collections.Array<string> crystalSupplyFuelSupportedTypes = new();
    [Export] public bool crystalSupplyFuelCanOverMax = false;
    [Export] public int crystalSupplyFuelMaxCap = 999;

    // 循环动画参数
    private Timer pulseTimer;
    private bool isPulsing = false;

    public override void _Ready()
    {
        base._Ready();


        // 按队伍绑定正确贴图动画
        UpdateCrystalVisual();
        // 初始化默认准许类型（所有类型）
        if (crystalHealUnitTypes == null || crystalHealUnitTypes.Count == 0)
        {
            crystalHealUnitTypes = new Godot.Collections.Array<string>
            {
                "Infantry", "Mech", "Bike", "LightTank", "MdTank", "Oozium",
                "Artillery", "APC", "AntiAir", "Recon", "AntiTank", "Flare", "FlyBomb"
            };
        }
        if (crystalSupplyUnitTypes == null || crystalSupplyUnitTypes.Count == 0)
        {
            crystalSupplyUnitTypes = new Godot.Collections.Array<string>
            {
                "Infantry", "Mech", "Bike", "LightTank", "MdTank", "Oozium",
                "Artillery", "APC", "AntiAir", "Recon", "AntiTank", "Flare", "FlyBomb"
            };
        }
        // 默认补给数值（正加负减零不变，这里默认补 1 发弹药 / 5 燃料）
        if (crystalSupplyPrimaryAmmo == 0) crystalSupplyPrimaryAmmo = 1;
        if (crystalSupplyFuel == 0) crystalSupplyFuel = 5;
        // 脉冲动画
        pulseTimer = new Timer { WaitTime = 1.5f, OneShot = false };
        pulseTimer.Timeout += OnPulseTimer;
        AddChild(pulseTimer);
        pulseTimer.Start();
    }

    private void OnPulseTimer()
    {
        if (animSprite == null || hasActed) return;
        var tween = CreateTween();
        tween.TweenProperty(animSprite, "scale", Vector2.One * 1.1f, 0.4f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(animSprite, "scale", Vector2.One, 0.4f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    // ========== 计算环形范围格子 ==========
    private List<Grids> CalculateRingRange(int minR, int maxR)
    {
        var result = new List<Grids>();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null || grid == null) return result;

        var gridMgr = gm.gridManager;
        var map = gridMgr.map;
        int w = map.GetLength(0), h = map.GetLength(1);
        var center = grid.GridIndex;

        for (int dx = -maxR; dx <= maxR; dx++)
        {
            for (int dy = -maxR; dy <= maxR; dy++)
            {
                int dist = Mathf.Abs(dx) + Mathf.Abs(dy);
                if (dist < minR || dist > maxR) continue;
                int nx = center.X + dx, ny = center.Y + dy;
                if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                {
                    var g = map[nx, ny];
                    if (g != null && IsInstanceValid(g)) result.Add(g);
                }
            }
        }
        return result;
    }

    // ========== 攻击范围显示（环形范围） ==========
    public override void ShowAttackRange()
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return;

        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = true;

        var rangeGrids = CalculateRingRange(crystalHealMinRange, crystalHealMaxRange);
        var fog = gm.fogOfWarManager;
        bool isFogEnabled = fog != null && fog.isFogOfWarEnabled;

        foreach (var g in rangeGrids)
        {
            if (isFogEnabled && fog != null && !fog.IsGridVisible(g)) continue;
            g.attackRangeIcon?.Show();
            if (g.attackRangeIcon != null)
            {
                // 绿色环形范围表示治疗/补给区域
                g.attackRangeIcon.Modulate = new Color(0.2f, 0.9f, 0.3f, 0.85f);
            }
            g.OnClickGrid = (to) => OnRangeGridClicked(to, gm);
        }
    }

    private void OnRangeGridClicked(Grids targetGrid, GameManager gm)
    {
        // 防重复触发：已行动则忽略，同时手动清除回调
        if (hasActed || remainingAttacks <= 0) return;
        targetGrid.OnClickGrid = null;
        
        // 点击环形范围内任意格子 -> 执行治疗+补给，然后直接标记已行动
        PerformCrystalEffect(gm);
        hasActed = true;
        remainingAttacks = 0;
        SetVisualDark();
        gm.gridManager.ClearWeaponRange();
        gm.selectedWeapon = null;
    }

    // ========== 执行黑水晶效果（治疗 + 补给） ==========
    public void PerformCrystalEffect(GameManager gm)
    {
        if (gm == null || grid == null) return;

        // 1. 范围治疗（不消耗弹药）
        var healGrids = CalculateRingRange(crystalHealMinRange, crystalHealMaxRange);
        foreach (var g in healGrids)
        {
            foreach (var unit in g.infantries.ToList())
            {
                if (!IsInstanceValid(unit)) continue;
                TryHealUnit(unit);
            }
            if (crystalHealAffectsWeapons && g.weapon != null && IsInstanceValid(g.weapon) && g.weapon != this)
            {
                TryHealWeapon(g.weapon);
            }
        }

        // 2. 范围补给（先检查弹药，再执行，避免无限卡 Bug）
        var supplyGrids = CalculateRingRange(crystalSupplyMinRange, crystalSupplyMaxRange);
        var supplyTargets = new List<object>(); // 收集所有需要补给的目标
        foreach (var g in supplyGrids)
        {
            // 自身过滤
            if (!crystalSupplyAffectsSelf && g == grid) continue;

            foreach (var unit in g.infantries.ToList())
            {
                if (!IsInstanceValid(unit)) continue;
                if (WouldSupplyUnit(unit)) supplyTargets.Add(unit);
            }
            if (crystalSupplyAffectsWeapons && g.weapon != null && IsInstanceValid(g.weapon) && g.weapon != this)
            {
                if (WouldSupplyWeapon(g.weapon)) supplyTargets.Add(g.weapon);
            }
        }

        // 检查弹药是否足够（如果开启消耗）
        int totalCost = supplyTargets.Count * crystalSupplyAmmoCostPerTarget;
        if (crystalSupplyConsumesAmmo && useAmmoSystem && totalCost > 0)
        {
            if (currentAmmo < totalCost)
            {
                ShowCrystalPulseEffect();
                return; // 弹药不够，全部跳过（避免部分执行导致卡死）
            }
            // 先扣弹药，再执行补给
            currentAmmo -= totalCost;
            UpdateHpLabel(); // 刷新弹药显示
        }

        // 执行补给
        foreach (var target in supplyTargets)
        {
            if (target is Infantry unit && IsInstanceValid(unit))
                ExecuteSupplyUnit(unit);
            else if (target is Weapon weapon && IsInstanceValid(weapon))
                ExecuteSupplyWeapon(weapon);
        }

        // 播放特效
        ShowCrystalPulseEffect();
    }

    // 预检：单位是否会被补给（不实际修改）
    private bool WouldSupplyUnit(Infantry unit)
    {
        if (!CheckTeamFilter(unit.team, crystalSupplyTeamMode)) return false;
        if (!CheckTypeFilter(unit.GetType().Name, crystalSupplyUnitTypes)) return false;
        // 检查是否有任何一种补给会实际改变数值
        if (crystalSupplyFlareAmmo != 0 && unit.canIlluminate)
            if (crystalSupplyFlareSupportedTypes.Count == 0 || crystalSupplyFlareSupportedTypes.Contains(unit.GetType().Name))
                if (ApplySupplyPreview(unit.currentFlareAmmo, unit.maxFlareAmmo, crystalSupplyFlareAmmo, crystalSupplyFlareCanOverMax, crystalSupplyFlareMaxCap) != unit.currentFlareAmmo) return true;
        if (crystalSupplyExplodeAmmo != 0 && unit.canExplode && !unit.explosionDestroysSelf)
            if (crystalSupplyExplodeSupportedTypes.Count == 0 || crystalSupplyExplodeSupportedTypes.Contains(unit.GetType().Name))
                if (ApplySupplyPreview(unit.currentExplodeAmmo, unit.maxExplodeAmmo, crystalSupplyExplodeAmmo, crystalSupplyExplodeCanOverMax, crystalSupplyExplodeMaxCap) != unit.currentExplodeAmmo) return true;
        if (crystalSupplyPrimaryAmmo != 0 && unit.hasPrimaryWeapon && unit.primaryHasLimitedAmmo && unit.maxPrimaryAmmo < 99)
            if (crystalSupplyPrimarySupportedTypes.Count == 0 || crystalSupplyPrimarySupportedTypes.Contains(unit.GetType().Name))
                if (ApplySupplyPreview(unit.currentPrimaryAmmo, unit.maxPrimaryAmmo, crystalSupplyPrimaryAmmo, crystalSupplyPrimaryCanOverMax, crystalSupplyPrimaryMaxCap) != unit.currentPrimaryAmmo) return true;
        if (crystalSupplyFuel != 0 && unit.consumeFuel)
            if (crystalSupplyFuelSupportedTypes.Count == 0 || crystalSupplyFuelSupportedTypes.Contains(unit.GetType().Name))
                if (ApplySupplyPreview(unit.fuel, unit.maxFuel, crystalSupplyFuel, crystalSupplyFuelCanOverMax, crystalSupplyFuelMaxCap) != unit.fuel) return true;
        return false;
    }

    private bool WouldSupplyWeapon(Weapon weapon)
    {
        if (!CheckTeamFilter(weapon.team, crystalSupplyTeamMode)) return false;
        if (!weapon.useAmmoSystem) return false;
        if (crystalSupplyPrimaryAmmo != 0)
            if (ApplySupplyPreview(weapon.currentAmmo, weapon.maxAmmo, crystalSupplyPrimaryAmmo, crystalSupplyPrimaryCanOverMax, crystalSupplyPrimaryMaxCap) != weapon.currentAmmo) return true;
        return false;
    }

    private int ApplySupplyPreview(int current, int max, int delta, bool canOverMax, int maxCap)
    {
        if (delta == 0) return current;
        if (delta > 0)
        {
            if (canOverMax) return Mathf.Min(current + delta, maxCap);
            else return Mathf.Min(current + delta, max);
        }
        else return Mathf.Max(0, current + delta);
    }

    // ========== 治疗单位 ==========
    private void TryHealUnit(Infantry unit)
    {
        // 队伍判定
        if (!CheckTeamFilter(unit.team, crystalHealTeamMode)) return;
        // 类型判定
        if (!CheckTypeFilter(unit.GetType().Name, crystalHealUnitTypes)) return;
        // 治疗量
        if (Mathf.Abs(crystalHealPercent) < 0.001f) return; // 0没反应

        int healAmount = Mathf.RoundToInt(unit.maxHealth * crystalHealPercent);
        if (healAmount == 0) return;

        int oldHp = unit.health;
        if (crystalHealPercent > 0)
        {
            // 加血
            if (crystalHealCanOverMaxHp)
            {
                int cap = Mathf.RoundToInt(unit.maxHealth * Mathf.Max(1, crystalHealMaxHpCapPercent) / 100f);
                unit.health = Mathf.Min(unit.health + healAmount, cap);
            }
            else
            {
                unit.health = Mathf.Min(unit.health + healAmount, unit.maxHealth);
            }
        }
        else
        {
            // 扣血（负值）
            unit.health = Mathf.Max(1, unit.health + healAmount); // healAmount 是负数
        }

        int delta = unit.health - oldHp;
        if (delta != 0)
        {
            unit.UpdateHpLabel();
            ShowCrystalHealEffect(unit.GlobalPosition, delta, crystalHealPercent > 0);
        }
    }

    private void TryHealWeapon(Weapon weapon)
    {
        if (!CheckTeamFilter(weapon.team, crystalHealTeamMode)) return;
        if (Mathf.Abs(crystalHealPercent) < 0.001f) return;

        int healAmount = Mathf.RoundToInt(weapon.maxHealth * crystalHealPercent);
        if (healAmount == 0) return;

        int oldHp = weapon.health;
        if (crystalHealPercent > 0)
        {
            if (crystalHealCanOverMaxHp)
            {
                int cap = Mathf.RoundToInt(weapon.maxHealth * Mathf.Max(1, crystalHealMaxHpCapPercent) / 100f);
                weapon.health = Mathf.Min(weapon.health + healAmount, cap);
            }
            else
            {
                weapon.health = Mathf.Min(weapon.health + healAmount, weapon.maxHealth);
            }
        }
        else
        {
            weapon.health = Mathf.Max(1, weapon.health + healAmount);
        }

        int delta = weapon.health - oldHp;
        if (delta != 0)
        {
            weapon.UpdateHpLabel();
            ShowCrystalHealEffect(weapon.GlobalPosition, delta, crystalHealPercent > 0);
        }
    }

    // ========== 执行补给单位（弹药已预扣，直接执行） ==========
    private void ExecuteSupplyUnit(Infantry unit)
    {
        bool anySupplied = false;
        var supplyParts = new List<string>();
        Color supplyColor = new Color(0.2f, 0.9f, 0.9f); // 青色，区别于设施回血的绿色

        // Flare 弹
        if (crystalSupplyFlareAmmo != 0 && unit.canIlluminate)
        {
            if (crystalSupplyFlareSupportedTypes.Count == 0 || crystalSupplyFlareSupportedTypes.Contains(unit.GetType().Name))
            {
                int old = unit.currentFlareAmmo;
                int newVal = ApplySupply(unit.currentFlareAmmo, unit.maxFlareAmmo, crystalSupplyFlareAmmo,
                    crystalSupplyFlareCanOverMax, crystalSupplyFlareMaxCap);
                if (newVal != old) { unit.currentFlareAmmo = newVal; anySupplied = true; supplyParts.Add("Flare+"); }
            }
        }
        // 自爆弹
        if (crystalSupplyExplodeAmmo != 0 && unit.canExplode && !unit.explosionDestroysSelf)
        {
            if (crystalSupplyExplodeSupportedTypes.Count == 0 || crystalSupplyExplodeSupportedTypes.Contains(unit.GetType().Name))
            {
                int old = unit.currentExplodeAmmo;
                int newVal = ApplySupply(unit.currentExplodeAmmo, unit.maxExplodeAmmo, crystalSupplyExplodeAmmo,
                    crystalSupplyExplodeCanOverMax, crystalSupplyExplodeMaxCap);
                if (newVal != old) { unit.currentExplodeAmmo = newVal; anySupplied = true; supplyParts.Add("Bomb+"); }
            }
        }
        // 主武器弹药
        if (crystalSupplyPrimaryAmmo != 0 && unit.hasPrimaryWeapon && unit.primaryHasLimitedAmmo && unit.maxPrimaryAmmo < 99)
        {
            if (crystalSupplyPrimarySupportedTypes.Count == 0 || crystalSupplyPrimarySupportedTypes.Contains(unit.GetType().Name))
            {
                int old = unit.currentPrimaryAmmo;
                int newVal = ApplySupply(unit.currentPrimaryAmmo, unit.maxPrimaryAmmo, crystalSupplyPrimaryAmmo,
                    crystalSupplyPrimaryCanOverMax, crystalSupplyPrimaryMaxCap);
                if (newVal != old) { unit.currentPrimaryAmmo = newVal; anySupplied = true; supplyParts.Add("Ammo+"); }
            }
        }
        // 燃料
        if (crystalSupplyFuel != 0 && unit.consumeFuel)
        {
            if (crystalSupplyFuelSupportedTypes.Count == 0 || crystalSupplyFuelSupportedTypes.Contains(unit.GetType().Name))
            {
                int old = unit.fuel;
                int newVal = ApplySupply(unit.fuel, unit.maxFuel, crystalSupplyFuel,
                    crystalSupplyFuelCanOverMax, crystalSupplyFuelMaxCap);
                if (newVal != old) { unit.fuel = newVal; anySupplied = true; supplyParts.Add("Fuel+"); }
            }
        }

        if (anySupplied)
        {
            unit.UpdateHpLabel();
            ShowCrystalSupplyEffect(unit.GlobalPosition, string.Join(" ", supplyParts), supplyColor);
        }
    }

    private void ExecuteSupplyWeapon(Weapon weapon)
    {
        if (!weapon.useAmmoSystem) return;

        bool anySupplied = false;
        var supplyParts = new List<string>();
        Color supplyColor = new Color(0.2f, 0.9f, 0.9f);

        // 兵器只有主弹药系统
        if (crystalSupplyPrimaryAmmo != 0)
        {
            int old = weapon.currentAmmo;
            int newVal = ApplySupply(weapon.currentAmmo, weapon.maxAmmo, crystalSupplyPrimaryAmmo,
                crystalSupplyPrimaryCanOverMax, crystalSupplyPrimaryMaxCap);
            if (newVal != old) { weapon.currentAmmo = newVal; anySupplied = true; supplyParts.Add("Ammo+"); }
        }

        if (anySupplied)
        {
            weapon.UpdateHpLabel();
            ShowCrystalSupplyEffect(weapon.GlobalPosition, string.Join(" ", supplyParts), supplyColor);
        }
    }

    // ========== 通用补给计算 ==========
    private int ApplySupply(int current, int max, int delta, bool canOverMax, int maxCap)
    {
        if (delta == 0) return current;
        if (delta > 0)
        {
            // 加补给
            if (canOverMax)
                return Mathf.Min(current + delta, maxCap);
            else
                return Mathf.Min(current + delta, max);
        }
        else
        {
            // 扣补给（负值）
            return Mathf.Max(0, current + delta);
        }
    }

    // ========== 过滤判定 ==========
    private bool CheckTeamFilter(string targetTeam, int mode)
    {
        return mode switch
        {
            0 => true,                                    // 不分敌我
            1 => targetTeam == this.team,                  // 仅我方
            2 => targetTeam != this.team && !string.IsNullOrEmpty(targetTeam), // 仅敌方
            _ => true
        };
    }

    private bool CheckTypeFilter(string typeName, Godot.Collections.Array<string> allowedTypes)
    {
        if (allowedTypes == null || allowedTypes.Count == 0) return true; // 空列表=所有类型
        return allowedTypes.Contains(typeName);
    }

    // ========== 特效系统 ==========
    private void ShowCrystalHealEffect(Vector2 pos, int delta, bool isHeal)
    {
        var label = new Label();
        label.Text = $"{(delta > 0 ? "+" : "")}{delta}HP";
        label.Position = pos + new Vector2(-10, -30);
        label.AddThemeColorOverride("font_color", isHeal ? new Color(0.2f, 1f, 0.3f) : new Color(1f, 0.2f, 0.2f));
        label.AddThemeConstantOverride("outline_size", 2);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeFontSizeOverride("font_size", 16);
        GetTree().Root.AddChild(label);

        var tween = label.CreateTween();
        tween.TweenProperty(label, "position", pos + new Vector2(-10, -60), 1.0f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(label, "modulate:a", 0f, 1.0f);
        tween.TweenCallback(Callable.From(() => { if (IsInstanceValid(label)) label.QueueFree(); }));
    }

    private void ShowCrystalSupplyEffect(Vector2 pos, string text, Color color)
    {
        var label = new Label();
        label.Text = text;
        label.Position = pos + new Vector2(-10, -45);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeConstantOverride("outline_size", 2);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeFontSizeOverride("font_size", 14);
        GetTree().Root.AddChild(label);

        var tween = label.CreateTween();
        tween.TweenProperty(label, "position", pos + new Vector2(-10, -75), 1.0f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(label, "modulate:a", 0f, 1.0f);
        tween.TweenCallback(Callable.From(() => { if (IsInstanceValid(label)) label.QueueFree(); }));
    }

    private void ShowCrystalPulseEffect()
    {
        if (animSprite == null) return;
        var tween = CreateTween();
        tween.TweenProperty(animSprite, "modulate", new Color(0.3f, 1f, 0.3f, 1f), 0.2f);
        tween.TweenProperty(animSprite, "modulate", Colors.White, 0.3f);

        // 范围扩散光环
        var pulse = new Sprite2D();
        var frameTex = animSprite.SpriteFrames?.GetFrameTexture(animSprite.Animation, 0);
        if (frameTex == null) return;
        pulse.Texture = frameTex;
        pulse.Position = animSprite.Position;
        pulse.Scale = animSprite.Scale * 0.5f;
        pulse.Modulate = new Color(0.2f, 1f, 0.3f, 0.6f);
        pulse.ZIndex = 5;
        AddChild(pulse);

        var pt = pulse.CreateTween();
        pt.TweenProperty(pulse, "scale", animSprite.Scale * 2.5f, 0.6f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
        pt.Parallel().TweenProperty(pulse, "modulate:a", 0f, 0.6f);
        pt.TweenCallback(Callable.From(() => { if (IsInstanceValid(pulse)) pulse.QueueFree(); }));
    }

    // ========== AI 执行入口 ==========
    public override void ExecuteAI()
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return;
        PerformCrystalEffect(gm);
        hasActed = true;
        remainingAttacks = 0;
        SetVisualDark();
    }

    public override void PerformAttack(Infantry target)
    {
        // 黑水晶不需要选择特定目标，点击范围内任意格子即触发
        // 这里保留接口兼容性
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        PerformCrystalEffect(gm);
    }

    public override void RotateDirection()
    {
        // 黑水晶不需要旋转方向
    }

    public override bool CanAttack()
    {
        return !hasActed && remainingAttacks > 0;
    }

    // ========== 贴图/动画绑定（按队伍） ==========
    private void UpdateCrystalVisual()
    {
        if (animSprite == null) return;
        // 按队伍选择动画：P1=橙, P2=蓝, P0=无色基础, P-1=橙(染浅紫)
        string animName = team switch
        {
            TeamHelper.Player1 => "OrangeCrystal",
            TeamHelper.Player2 => "BlueCrystal",
            TeamHelper.Player0 => "Crystal",
            TeamHelper.Player => "OrangeCrystal", // P-1 用 Orange，但染浅紫
            _ => "Crystal"
        };
        if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation(animName))
            animSprite.Animation = animName;
        else if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation("Crystal"))
            animSprite.Animation = "Crystal";

        SetVisualNormal();
    }

    public override void SetVisualNormal()
    {
        if (animSprite == null) return;
        // P1/P2 动画本身有颜色，保持 White；P0 染灰；P-1 染浅紫
        Color tint = team switch
        {
            TeamHelper.Player1 => Colors.White,
            TeamHelper.Player2 => Colors.White,
            TeamHelper.Player0 => TeamHelper.GetTeamColor(TeamHelper.Player0),
            TeamHelper.Player => TeamHelper.GetTeamColor(TeamHelper.Player),
            _ => Colors.White
        };
        animSprite.Modulate = tint;
    }

    public override void SetVisualDark()
    {
        if (animSprite == null) return;
        Color normal = team switch
        {
            TeamHelper.Player1 => Colors.White,
            TeamHelper.Player2 => Colors.White,
            TeamHelper.Player0 => TeamHelper.GetTeamColor(TeamHelper.Player0),
            TeamHelper.Player => TeamHelper.GetTeamColor(TeamHelper.Player),
            _ => Colors.White
        };
        animSprite.Modulate = new Color(normal.R * 0.5f, normal.G * 0.5f, normal.B * 0.5f, 1f);
    }
}
