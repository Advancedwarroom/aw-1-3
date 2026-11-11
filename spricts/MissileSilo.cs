// MissileSilo.cs - 导弹发射井（AW2/3 原作机制 + 发射台模块全配置）
// 原作：步兵站上井口发射一次性导弹，5×5 菱形范围 3HP 固定伤害
// 模块：发射台（15项配置）——详见发射台模块 Export
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class MissileSilo : Weapon
{
    // ========== 1. 是否开启 ==========
    [ExportGroup("发射台模块")]
    [Export] public bool siloEnabled = true;                    // 1.Export是否开启

    // ========== 发射方式（开创人2026-11-11 定案四件套）==========
    [ExportGroup("发射方式")]
    [Export(PropertyHint.Enum, "点击兵器直接发射,单位菜单发射,两者皆可")]
    public int siloLaunchMode = 1;                              // ① 0=点井本身弹发射；1=单位菜单内发射；2位置保留；3=①②皆可
    [Export] public string[] siloLaunchUnitWhitelist = new string[] { "Infantry", "Mech" };  // ② 模式1白名单（默认=原作AWDS：仅Infantry/Mech可发射；DoR风格加"Bike"；清空=放宽为全部非空军）
    [Export] public string[] siloLaunchUnitBlacklist = System.Array.Empty<string>();   // 不能发射的单位(UnitTypeId/类名)；空=谁都能发（开创人2026-11-11：替原空军硬禁）
    [Export] public int siloLaunchRangeMin = 0;               // ③ 模式1距离带下限（0=含正上方/同格）
    [Export] public int siloLaunchRangeMax = 0;               // ④ 模式1距离带上限（0-0=仅站上；1-1=临格，均为n-m特例）

    // ========== 发射代价（开创人2026-11-11：自费补给联动·第一步资金，自身/献祭后续接入）==========
    [ExportGroup("发射代价")]
    [Export] public int siloLaunchFundsCost = 0;                    // 发射资金代价（0=免费；负=发射倒贴）
    [Export(PropertyHint.Enum, "操作方步兵支付,目标方导弹井支付")]
    public int siloLaunchPayer = 0;                                 // 付款方：0=操作方步兵所属势力 1=导弹井所属势力

    // ========== 2. 是否按所属势力判定 ==========
    [Export] public bool factionLocked = false;                 // 2.按所属势力判定（颜色随势力；开启时仅所属势力能发射；Player0中立=不限制）

    // ========== 3. 是否可被攻击摧毁 ==========
    [Export] public bool siloAttackable = false;                // 3.Export是否可被攻击摧毁（原作默认不可攻击）

    // ========== 4. 血量（必须开启3才有效，否则作废不可攻击，同原作）==========
    // maxHealth/health 继承基类（千分制）：被摧毁后不发射，变为空导弹井
    // 攻击方式：单位在所属格不可攻击，移动到临格可像兵器一样攻击；井上有单位时点击临格单位可弹选择菜单

    // ========== 5. 是否一次性 ==========
    [Export] public bool oneTimeUse = true;                     // 5.是否一次性（默认原作一次性）

    // ========== 6. 剩余弹药（非一次性时有效）==========
    // useAmmoSystem/maxAmmo/currentAmmo 继承基类弹药系统

    // ========== 7. 是否可被APC/Rig等补给 ==========
    [Export] public bool canBeResupplied = true;                // 7.补给后由空导弹井变回导弹井

    // ========== 8-10. 自动恢复 ==========
    [Export] public bool autoReload = false;                    // 8.是否自动恢复
    [Export] public int autoReloadTurns = 2;                    // 9.自动恢复消耗回合数
    [Export] public int autoReloadAmount = 999;                 // 10.每次自动恢复：999=补满，其他=填数（可为负减少）

    // ========== 11. 导弹打击范围（以该井为核心）==========
    [ExportGroup("打击范围")]
    [Export] public int strikeMinRange = 1;                     // 11.最小范围
    [Export] public int strikeMaxRange = 99;                    // 11.最大范围（原作全图）

    // ========== 12. 打击伤害面（以目标点为新圆心）==========
    [Export] public int blastMinRadius = 0;                     // 12.伤害面最小半径（盲区）
    [Export] public int blastMaxRadius = 2;                    // 12.伤害面最大半径（原作5×5菱形=2）
    [Export] public string effectBlacklist = "";                        // 打击效果黑名单（类名逗号分隔；空=不屏蔽，导弹井沿用原作留空）
    [Export] public int[] effectAmmoDeltaPct = new int[8];              // 按弹药类型0-7：该弹药现有量百分比增减（可负）
    [Export] public int[] effectAmmoMaxPct = new int[8];                // 按弹药类型0-7：该弹药上限百分比增减（可负）

    // ========== 13. 打击效果（范围内所有单位/兵器）==========
    [ExportGroup("打击效果（正=增加 负=减少）")]
    [Export] public int effectHealthDelta = -30;                // 改变现有血量（原作-30=3HP）
    [Export] public int effectMaxHealthDelta = 0;               // 改变血量上限
    [Export] public int effectPrimaryAmmoDelta = 0;            // 改变主弹药现有量
    [Export] public int effectMaxAmmoDelta = 0;                // 改变主弹药上限
    [Export] public int effectFuelDelta = 0;                   // 改变燃料现有量
    [Export] public int effectMaxFuelDelta = 0;                // 改变燃料上限

    // ========== 14. 支持队伍（如开启势力限制）==========
    [ExportGroup("队伍与目标")]
    [Export] public bool siloIncludeFriendly = true;
    [Export] public bool siloIncludeEnemy = true;
    [Export] public bool siloIncludeAllied = true;
    [Export] public bool targetUnitIncludeFriendly = true;
    [Export] public bool targetUnitIncludeEnemy = true;
    [Export] public bool targetUnitIncludeAllied = true;
    [Export] public bool affectsWeapons = true;                // 打击是否影响兵器
    [Export] public bool affectsFacilities = false;            // 打击是否影响设施
    // ========== 15. 不支持单位种类黑名单（导弹井全部沿用原作，不做黑名单）==========

    private int reloadCounter = 0;
    private bool hasFired = false;

    public override void _Ready()
    {
        base._Ready();
        if (useAmmoSystem && currentAmmo <= 0) hasFired = true;
        UpdateSiloVisual();
    }

    // ---------- 队伍判定 ----------
    private static bool AreFriendly(string a, string b)
        => TeamHelper.IsFriendlyTeam(a, b) || TeamHelper.IsAlliedTeam(a, b);

    private bool TeamCanFire(string firingTeam)
    {
        if (!factionLocked) return true;
        if (team == TeamHelper.Player0) return true;   // 中立不限制
        return AreFriendly(team, firingTeam);
    }

    public bool TargetTeamOk(string targetTeam)
    {
        return TeamHelper.MatchesTeamRelation(team, targetTeam,
            siloIncludeFriendly, siloIncludeEnemy, siloIncludeAllied);
    }

    public bool TargetUnitTeamOk(string targetTeam)
    {
        return TeamHelper.MatchesTeamRelation(team, targetTeam,
            targetUnitIncludeFriendly, targetUnitIncludeEnemy, targetUnitIncludeAllied);
    }

    // ---------- 井上发射（原作：步兵站上井口）----------
    public bool CanBeFiredBy(Infantry unit)
    {
        if (!siloEnabled || isDestroyed || hasFired) return false;
        if (!HasAmmo()) return false;
        if (unit == null)
        {
            // 模式0/3：点击兵器本身发射（距离/白名单不适用；发射权按当前操作方由调用方把关）
            return siloLaunchMode == 0 || siloLaunchMode == 3;
        }
        if (siloLaunchMode != 1 && siloLaunchMode != 3) return false;
        // 模式1——n-m 距离带（0-0=仅同格/正上方；1-1=临格；同格与临格均为n-m特例）
        if (grid != null && unit.grid != null)
        {
            int d = Mathf.Abs(unit.grid.GridIndex.X - grid.GridIndex.X) + Mathf.Abs(unit.grid.GridIndex.Y - grid.GridIndex.Y);
            if (d < siloLaunchRangeMin || d > siloLaunchRangeMax) return false;
        }
        // 白名单：空=默认全部非空军（原作：任意步兵系站上即可发射，此处默认放宽为任意地面单位）
        if (siloLaunchUnitWhitelist != null && siloLaunchUnitWhitelist.Length > 0)
        {
            if (!siloLaunchUnitWhitelist.Contains(unit.UnitTypeId) && !siloLaunchUnitWhitelist.Contains(unit.GetType().Name))
                return false;
        }
        // 黑名单：命中即不能发射（开创人2026-11-11：替代原"空军一律不能发射"硬编码）
        if (siloLaunchUnitBlacklist != null && siloLaunchUnitBlacklist.Length > 0
            && (siloLaunchUnitBlacklist.Contains(unit.UnitTypeId) || siloLaunchUnitBlacklist.Contains(unit.GetType().Name)))
            return false;
        if (!TargetUnitTeamOk(unit.team)) return false;
        if (!TeamCanFire(unit.team)) return false;
        return true;
    }

    private bool CanInfantryFire(Infantry u) => u != null;

    private bool HasAmmo()
    {
        if (oneTimeUse) return !hasFired;
        if (!useAmmoSystem) return !hasFired;
        return currentAmmo > 0;
    }

    // ---------- 发射流程：显示范围 → 点击目标 ----------
    public void BeginTargetSelection(Infantry firer)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return;
        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = true;

        var rangeGrids = gm.gridManager.grids.Where(g =>
        {
            int d = Mathf.Abs(g.GridIndex.X - grid.GridIndex.X) + Mathf.Abs(g.GridIndex.Y - grid.GridIndex.Y);
            return d >= strikeMinRange && d <= strikeMaxRange && !IsScreenBlocked(g); // ✅ N屏：默认仅限己图
        }).ToList();

        foreach (var g in rangeGrids)
        {
            g.attackRangeIcon?.Show();
            if (g.attackRangeIcon != null) g.attackRangeIcon.Modulate = new Color(1f, 0.5f, 0.2f, 0.85f);
            g.IsInWeaponRange = true;
            g.OnClickGrid = (to) => { if (IsInstanceValid(this)) LaunchMissile(g, firer); };
            gm.gridManager.OverrideUnitInput(g, true);
        }
        // 范围外点击取消
        foreach (var g in gm.gridManager.grids)
        {
            if (rangeGrids.Contains(g)) continue;
            g.OnClickEmpty = () =>
            {
                if (!IsInstanceValid(this)) return;
                CancelTargetSelection(gm);
            };
        }
    }

    public void CancelTargetSelection(GameManager gm)
    {
        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = false;
        foreach (var g in gm.gridManager.grids)
        {
            gm.gridManager.OverrideUnitInput(g, false);
            g.OnClickGrid = null; g.OnClickEmpty = null;
        }
    }

    // ---------- 发射 ----------
    public void LaunchMissile(Grids targetGrid, Infantry firer)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || targetGrid == null) return;
        CancelTargetSelection(gm);

        // 消耗弹药
        if (oneTimeUse) hasFired = true;
        else if (useAmmoSystem) currentAmmo = Mathf.Max(0, currentAmmo - 1);
        else hasFired = true;

        // 发射单位行动结束（原作：发射后单位可继续移动？不——原作发射导弹不消耗单位行动。此处只标记井已用）
        ShowMissileLaunchEffect(targetGrid);

        // 计算伤害面（以目标为圆心，曼哈顿菱形）
        var gm2 = gm;
        var blastGrids = gm2.gridManager.grids.Where(g =>
        {
            int d = Mathf.Abs(g.GridIndex.X - targetGrid.GridIndex.X) + Mathf.Abs(g.GridIndex.Y - targetGrid.GridIndex.Y);
            return d >= blastMinRadius && d <= blastMaxRadius;
        }).ToList();

        foreach (var g in blastGrids) ApplyStrikeEffects(g, gm2);

        UpdateSiloVisual();
        UpdateHpLabel();
        if (firer != null && IsInstanceValid(firer))
        {
            // 原作：发射导弹不结束该单位回合
        }
    }

    private void ShowMissileLaunchEffect(Grids targetGrid)
    {
        // 目标格爆炸闪光 + 相机震动
        var fx = new CannonFlash();
        targetGrid.AddChild(fx);
        fx.Position = Vector2.Zero;
        var cam = GetTree().GetFirstNodeInGroup("main_camera") as Camera2D;
        if (cam != null)
        {
            var tween = cam.CreateTween();
            tween.TweenProperty(cam, "offset", new Vector2(4, -3), 0.05f);
            tween.TweenProperty(cam, "offset", new Vector2(-3, 2), 0.05f);
            tween.TweenProperty(cam, "offset", Vector2.Zero, 0.05f);
        }
    }

    // ---------- 13. 打击效果 ----------
    // 打击效果黑名单：类名逗号分隔（如 "ForceSatellite,AttackSatellite"），空串=不屏蔽
    private bool IsBlacklisted(Node n)
    {
        if (string.IsNullOrEmpty(effectBlacklist) || n == null) return false;
        var name = n.GetType().Name;
        foreach (var item in effectBlacklist.Split(','))
            if (item.Trim() == name) return true;
        return false;
    }

    private void ApplyStrikeEffects(Grids g, GameManager gm)
    {
        // 单位
        g.infantries?.RemoveAll(i => !IsInstanceValid(i));
        foreach (var unit in g.infantries?.ToList() ?? new List<Infantry>())
        {
            if (unit == null || !IsInstanceValid(unit)) continue;
            if (!TargetUnitTeamOk(unit.team)) continue;
            ApplyEffectToUnit(unit);
        }
        // 兵器
        if (affectsWeapons && g.weapons != null)
        {
            foreach (var w in g.weapons.ToList())
            {
                if (w == null || !IsInstanceValid(w) || w.isDestroyed) continue;
                if (!TargetTeamOk(w.team)) continue;
                ApplyEffectToWeapon(w);
            }
        }
        // 设施
        if (affectsFacilities && g.city != null)
        {
            ApplyEffectToFacility(g.city);
        }
    }

    private void ApplyEffectToUnit(Infantry unit)
    {
        if (effectHealthDelta != 0)
            if (unit != null && IsBlacklisted(unit)) unit = null;
        {
            int old = unit.health;
            unit.health = Mathf.Max(1, unit.health + effectHealthDelta);  // 固定伤害语义：-30=固定扣30HP(=原作3HP，开创人2026-11-03定：井伤固定不缩放)
            ShowDeltaLabel(unit, unit.health - old, effectHealthDelta < 0 ? new Color(1f, 0.35f, 0.25f) : new Color(0.3f, 0.9f, 0.4f));
        }
        if (effectMaxHealthDelta != 0)
        {
            unit.maxHealth = Mathf.Max(1, unit.maxHealth + unit.maxHealth * effectMaxHealthDelta / 100);
            unit.health = Mathf.Clamp(unit.health, 1, unit.maxHealth);
        }
        if (effectPrimaryAmmoDelta != 0 && unit.hasPrimaryWeapon && unit.primaryHasLimitedAmmo)
            unit.currentPrimaryAmmo = Mathf.Clamp(unit.currentPrimaryAmmo + unit.maxPrimaryAmmo * effectPrimaryAmmoDelta / 100, 0, unit.maxPrimaryAmmo);
        if (effectMaxAmmoDelta != 0 && unit.hasPrimaryWeapon)
        {
            unit.maxPrimaryAmmo = Mathf.Max(0, unit.maxPrimaryAmmo + unit.maxPrimaryAmmo * effectMaxAmmoDelta / 100);
            unit.currentPrimaryAmmo = Mathf.Min(unit.currentPrimaryAmmo, unit.maxPrimaryAmmo);
        }
        if (effectFuelDelta != 0 && unit.consumeFuel)
        {
            int old = unit.fuel;
            unit.fuel = Mathf.Clamp(unit.fuel + unit.maxFuel * effectFuelDelta / 100, 0, unit.maxFuel);
            if (unit.fuel != old) ShowDeltaLabel(unit, 0, new Color(0.9f, 0.7f, 0.2f), "Fuel");
        }
        if (effectMaxFuelDelta != 0)
        {
            unit.maxFuel = Mathf.Max(1, unit.maxFuel + unit.maxFuel * effectMaxFuelDelta / 100);
            unit.fuel = Mathf.Min(unit.fuel, unit.maxFuel);
        }
        unit.UpdateHpLabel();
    }

    private void ApplyEffectToWeapon(Weapon w)
    {
            if (w != null && IsBlacklisted(w)) w = null; // 黑名单命中→本目标全部效果跳过
            if (effectAmmoDeltaPct != null)
                for (int t = 0; t < 8 && t < effectAmmoDeltaPct.Length; t++)
                    if (effectAmmoDeltaPct[t] != 0)
                    {
                        int maxA = w.GetAmmoMaxByType(t);
                        w.SetAmmoByType(t, Mathf.Clamp(w.GetAmmoByType(t) + maxA * effectAmmoDeltaPct[t] / 100, 0, maxA));
                    }
        if (effectHealthDelta != 0)
        {
            int old = w.health;
            w.health = Mathf.Max(0, w.health + effectHealthDelta);  // 固定伤害（对兵器同理）
            ShowDeltaLabel(w, w.health - old, effectHealthDelta < 0 ? new Color(1f, 0.35f, 0.25f) : new Color(0.3f, 0.9f, 0.4f));
            w.UpdateHpLabel();
            if (w.health <= 0)
            {
                if (w is MissileSilo ms) ms.BecomeEmptySilo();
                else w.TakeDamage(99999);
            }
        }
        if (effectMaxHealthDelta != 0)
        {
            w.maxHealth = Mathf.Max(1, w.maxHealth + w.maxHealth * effectMaxHealthDelta / 100);
            w.health = Mathf.Clamp(w.health, 0, w.maxHealth);
            w.UpdateHpLabel();
        }
    }

    private void ApplyEffectToFacility(Facility f)
    {
        // 设施打击：走血量概念（设施无血量则跳过，仅状态提示）
    }

    private void ShowDeltaLabel(Node2D target, int delta, Color color, string prefix = "HP")
    {
        if (delta == 0 || target == null || !IsInstanceValid(target)) return;
        var label = new Label();
        label.Text = delta > 0 ? $"+{prefix}{delta / 10}" : $"{prefix}{delta / 10}";
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 3);
        label.ZIndex = 300;
        target.AddChild(label);
        label.Position = new Vector2(4, -18);
        var tween = label.CreateTween();
        tween.TweenProperty(label, "position:y", -34f, 0.8f);
        tween.TweenProperty(label, "modulate:a", 0f, 0.3f);
        tween.TweenCallback(Callable.From(() => label.QueueFree()));
    }

    // ---------- 被摧毁 → 空导弹井 ----------
    public void BecomeEmptySilo()
    {
        isDestroyed = true;
        hasFired = true;
        hasActed = true;
        currentAmmo = 0;
        var area = GetNodeOrNull<Area2D>("Area2D");
        if (area != null) area.SetDeferred("input_pickable", false);
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.selectedWeapon == this) gm.selectedWeapon = null;
        UpdateSiloVisual();
        gm?.CheckVictoryCondition();
        // 空井保留占格（不可再发射）
    }

    public override void TakeDamage(int damage, Infantry attacker = null)
    {
        if (!siloAttackable) return;   // 3.未开启=不可攻击（原作）
        base.TakeDamage(damage, attacker);
        if (health <= 0 && !isDestroyed) BecomeEmptySilo();
    }

    // ---------- 自动恢复 ----------
    public override void OnTurnStart()
    {
        base.OnTurnStart();
        if (autoReload && siloEnabled)
        {
            reloadCounter++;
            if (reloadCounter >= autoReloadTurns)
            {
                reloadCounter = 0;
                if (useAmmoSystem)
                {
                    if (autoReloadAmount >= 999) currentAmmo = maxAmmo;
                    else currentAmmo = Mathf.Clamp(currentAmmo + autoReloadAmount, 0, maxAmmo);
                }
                else if (autoReloadAmount >= 999 || autoReloadAmount > 0) hasFired = false;
                else hasFired = true;
                UpdateSiloVisual();
            }
        }
    }

    // ---------- 补给（APC/Rig等）----------
    public override bool ResupplyAmmo()
    {
        if (!canBeResupplied) return false;
        if (!oneTimeUse && !useAmmoSystem) return false;

        // 一次性井不经过弹药池：发射只翻 hasFired，补给必须能把它重新装填。
        // 带弹药池的一次性井也不能因“发射后弹药仍满”而拒绝恢复发射状态。
        if (useAmmoSystem)
        {
            if (currentAmmo >= maxAmmo && !hasFired && !isDestroyed) return false;
            currentAmmo = maxAmmo;
        }
        else if (!hasFired && !isDestroyed)
        {
            return false; // 未发射且未损毁：没有需要补给的状态
        }

        hasFired = false;
        isDestroyed = false;
        UpdateSiloVisual();
        return true;
    }

    // ---------- 视觉 ----------
    private void UpdateSiloVisual()
    {
        if (animSprite == null) return;
        bool empty = hasFired || (useAmmoSystem && currentAmmo <= 0) || (oneTimeUse && hasFired);
        string anim = empty ? "CowMissileSiloEmpty" : "CowMissileSilo";
        if (animSprite?.SpriteFrames == null || !animSprite.SpriteFrames.HasAnimation(anim))
            anim = empty ? "MissileSiloEmpty" : "MissileSilo";
        if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation(anim))
            animSprite.Play(anim);
        UpdateHpLabel();
    }

    public override string GetBrokenAnimName() => "MissileSiloEmpty";
    public override bool CanAttack() => false;   // 井本身不攻击（发射走单位菜单）

    // ---------- 编辑器/序列化辅助 ----------
    public override void RefreshTeamVisual()
    {
        base.RefreshTeamVisual();
        UpdateSiloVisual();
    }

    // ---------- 势力配色（CoW LUT 解码）----------
    // 导弹井贴图为 CoW (R,G)编码 mask（b4a2764 起纯LUT）；灰度模式(mode3)解不了编码会显示成 mask 原图。
    // 改用剧场同款 battle_recolor LUT 解码：像素(R,G)查 256×256 colortable，颜色全随势力换。
    protected override void ApplyPaletteMaterial()
    {
        if (animSprite == null || !IsInstanceValid(animSprite)) return;
        var lut = FactionPaletteManager.GetCowLutMaterial(team);
        if (lut == null) return;
        // 每实例独立副本：避免待机变暗 tone 污染共享缓存
        animSprite.Material = (ShaderMaterial)lut.Duplicate();
    }

    public string GetSiloInfo()
    {
        string state = isDestroyed || hasFired ? TranslateGroup.T("空导弹井") : TranslateGroup.T("导弹井");
        string ammo = oneTimeUse ? TranslateGroup.T("一次性") : $"{currentAmmo}/{maxAmmo}";
        return $"{TranslateGroup.T("导弹发射井")} [{state}] {TranslateGroup.T("弹药")}:{ammo}";
    }

    // ✅ 导弹井可通行（单位站上发射）：通行判定接口（原各处 is not MissileSilo 特判的统一收口）
    public override bool AllowsPassage(Infantry mover)
    {
        return true;
    }
}
