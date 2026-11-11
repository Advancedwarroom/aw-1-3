// BlackObelisk.cs - 黑尖塔（AW3《双重打击》Crystal Calamity / For the Future!）
// Wiki: https://warswiki.org/wiki/Black_Obelisk
// 原作机制：
// 1. 3×3 多格占据，完全不可通行、无地形星（摧毁后仍不可通行）
// 2. 仅弱点（正门，防御力≈中型坦克）可受击
// 3. 不攻击：每回合开始自动为 2 格范围内的我方单位 +2HP 并免费补油补弹（原作行为）
// 4. 与黑水晶并列是唯一能维修 Oozium 的东西
// 实现借鉴：LargeCannon 的多格/弱点系统 + Crystal 的范围治疗补给
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class BlackObelisk : BlackCannon
{
    // ✅ CoW LUT换色(2026-11全换色): 仅主体走colortable LUT; GlowSprite(黄色光线)显式无材质=钉死黄色不变色
    protected override void ApplyPaletteMaterial()
    {
        var body = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D") ?? animSprite;
        if (body != null && IsInstanceValid(body))
        {
            var lut = FactionPaletteManager.GetCowLutMaterial(team);
            if (lut != null) body.Material = (ShaderMaterial)lut.Duplicate();
        }
        var glow = GetNodeOrNull<AnimatedSprite2D>("GlowSprite");
        if (glow != null && IsInstanceValid(glow)) glow.Material = null; // ✅ 黄色光线不参与换色
    }

    [ExportGroup("黑尖塔 - 多格配置")]
    [Export] public Vector2I multiTileSize = new Vector2I(3, 3);

    [ExportGroup("黑尖塔 - 范围治疗（原作：2格内我方+2HP）")]
    [Export] public int obeliskHealRange = 2;         // 治疗范围（曼哈顿距离，从尖塔中心算）
    [Export] public bool obeliskHealCanCrossScreens = false; // 黑尖塔治疗与补给脉冲是否允许跨屏
    [Export] public int obeliskHealAmount = 20;        // 每回合回血（内部100制，20=显示+2HP）
    [Export] public bool obeliskHealWeapons = true;    // 能否给兵器回血（原作可维修Oozium等）
    [Export] public bool obeliskHealCanOverMaxHp = false; // 能否超过最大血量

    [ExportGroup("黑尖塔 - 范围补给（原作：免费油料弹药）")]
    [Export] public bool obeliskSupplyFuel = true;    // 补满油料
    [Export] public bool obeliskSupplyAmmo = true;    // 补满弹药

    [ExportGroup("黑尖塔 - 脉冲触发")]
    [Export] public bool obeliskAutoPulse = true;      // 每回合开始自动触发（原作行为）
    [Export] public bool obeliskPulseOnAnyTurn = false; // false=仅我方回合开始触发（原作每天一次）；true=任意回合开始

    // 弱点偏移：正门在下中（3×3 的 (1,2)）
    private Vector2I weakPointOffset = new Vector2I(1, 2);

    public override void _Ready()
    {
        // 在base._Ready之前设置多格属性
        isMultiTile = true;
        size = multiTileSize;
        canRotate = false;      // 黑尖塔不旋转
        direction = CannonDirection.Up; // 固定朝向（弱点在下中）

        base._Ready();

        UpdateMultiTileVisual();
    }

    // ========== 弱点：正门（下中） ==========
    public override Grids GetWeakPointGrid()
    {
        if (occupiedGrids.Count == 0) return grid;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null) return grid;

        var anchorIndex = grid?.GridIndex ?? new Vector2I(0, 0);
        var wpIndex = anchorIndex + weakPointOffset;

        if (wpIndex.X >= 0 && wpIndex.X < gm.gridManager.searchRange.X &&
            wpIndex.Y >= 0 && wpIndex.Y < gm.gridManager.searchRange.Y)
        {
            return gm.gridManager.map[wpIndex.X, wpIndex.Y];
        }
        return grid;
    }

    public override bool IsWeakPointGrid(Grids checkGrid)
    {
        if (!isMultiTile || occupiedGrids.Count == 0) return true;
        return checkGrid == GetWeakPointGrid();
    }

    // ========== 大贴图位置：3×3 中心 ==========
    public override void UpdateMultiTileVisual()
    {
        if (animSprite != null)
        {
            // 将贴图中心对准3×3的中心，底部略微压到格子区
            animSprite.Position = new Vector2(
                16 + (multiTileSize.X - 1) * 16f,
                20 + (multiTileSize.Y - 1) * 16f
            );
        }
    }

    // ========== 尖塔中心格（治疗范围圆心：3×3 正中） ==========
    private Grids GetCenterGrid()
    {
        if (occupiedGrids.Count == 0) return grid;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null) return grid;
        var anchorIndex = grid?.GridIndex ?? new Vector2I(0, 0);
        var cIndex = anchorIndex + new Vector2I(multiTileSize.X / 2, multiTileSize.Y / 2);
        if (cIndex.X >= 0 && cIndex.X < gm.gridManager.searchRange.X &&
            cIndex.Y >= 0 && cIndex.Y < gm.gridManager.searchRange.Y)
        {
            return gm.gridManager.map[cIndex.X, cIndex.Y];
        }
        return grid;
    }

    // ========== 治疗范围（环形：0~obeliskHealRange） ==========
    private List<Grids> CalculateHealRange()
    {
        var result = new List<Grids>();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null) return result;

        var gridMgr = gm.gridManager;
        var map = gridMgr.map;
        int w = map.GetLength(0), h = map.GetLength(1);
        var center = GetCenterGrid()?.GridIndex ?? new Vector2I(-999, -999);

        for (int dx = -obeliskHealRange; dx <= obeliskHealRange; dx++)
        {
            for (int dy = -obeliskHealRange; dy <= obeliskHealRange; dy++)
            {
                int dist = Mathf.Abs(dx) + Mathf.Abs(dy);
                if (dist > obeliskHealRange) continue;
                int nx = center.X + dx, ny = center.Y + dy;
                if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                {
                    var g = map[nx, ny];
                    if (g != null && IsInstanceValid(g) && gridMgr.IsRangeAllowed(GetCenterGrid(), g, obeliskHealCanCrossScreens)) result.Add(g);
                }
            }
        }
        return result;
    }

    // ========== 攻击范围显示 = 绿色治疗圈（借鉴 Crystal） ==========
    public override void ShowAttackRange()
    {
        if (isDestroyed) return;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return;

        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = true;

        var rangeGrids = CalculateHealRange();
        var fog = gm.fogOfWarManager;
        bool isFogEnabled = fog != null && fog.isFogOfWarEnabled;

        foreach (var g in rangeGrids)
        {
            if (isFogEnabled && fog != null && !fog.IsGridVisible(g)) continue;
            g.attackRangeIcon?.Show();
            if (g.attackRangeIcon != null)
                g.attackRangeIcon.Modulate = new Color(0.2f, 0.9f, 0.3f, 0.85f); // 绿色=治疗
            g.IsInWeaponRange = true;
        }
    }

    // ========== 回合开始：自动脉冲（原作：每回合开始治疗补给） ==========
    public override void OnTurnStart()
    {
        if (isDestroyed) return;

        // 重置行动状态（尖塔不攻击，仅维持脉冲）
        hasActed = false;
        remainingAttacks = maxAttacksPerTurn;
        SetVisualNormal();

        if (!obeliskAutoPulse) return;

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return;

        bool shouldPulse = obeliskPulseOnAnyTurn;
        if (!shouldPulse)
        {
            // 仅我方回合开始触发（原作每天一次的节奏）
            string currentTurnTeam = TeamHelper.GetPhaseTeamName(gm.turnPhase);
            shouldPulse = currentTurnTeam == team;
        }

        if (shouldPulse)
        {
            CallDeferred(nameof(PerformObeliskPulse));
        }
    }

    // ========== 执行脉冲：范围治疗 + 补给 ==========
    public void PerformObeliskPulse()
    {
        if (isDestroyed) return;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return;

        var healGrids = CalculateHealRange();
        int affected = 0;

        foreach (var g in healGrids)
        {
            foreach (var unit in g.infantries.ToList())
            {
                if (!IsInstanceValid(unit)) continue;
                if (!TeamHelper.IsFriendlyTeam(team, unit.team)) continue; // 仅同队（盟军由独立筛选配置决定）
                if (TryHealUnit(unit)) affected++;
            }
            if (obeliskHealWeapons && g.weapon != null && IsInstanceValid(g.weapon) && g.weapon != this)
            {
                if (g.weapon.team == team && TryHealWeapon(g.weapon)) affected++;
            }
        }

        ShowObeliskPulseEffect();
    }

    // 治疗单位：+HP 并补满油料弹药（原作：+2HP、免费油料弹药）
    private bool TryHealUnit(Infantry unit)
    {
        bool any = false;
        int oldHp = unit.health;

        // 回血
        int cap = obeliskHealCanOverMaxHp ? unit.maxHealth : unit.maxHealth;
        int newHp = Mathf.Min(unit.health + obeliskHealAmount, cap);
        if (newHp != unit.health)
        {
            unit.health = newHp;
            unit.UpdateHpLabel();
            ShowHealLabel(unit.GlobalPosition, newHp - oldHp);
            any = true;
        }

        // 补满油料
        if (obeliskSupplyFuel && unit.consumeFuel && unit.fuel < unit.maxFuel)
        {
            ShowSupplyLabel(unit.GlobalPosition, TranslateGroup.T("油料补满"));
            unit.fuel = unit.maxFuel;
            any = true;
        }

        // 补满主武器弹药（无限弹药的不用补）
        if (obeliskSupplyAmmo && unit.hasPrimaryWeapon && unit.primaryHasLimitedAmmo
            && unit.maxPrimaryAmmo < 99 && unit.currentPrimaryAmmo < unit.maxPrimaryAmmo)
        {
            ShowSupplyLabel(unit.GlobalPosition + new Vector2(0, 16), TranslateGroup.T("弹药补满"));
            unit.currentPrimaryAmmo = unit.maxPrimaryAmmo;
            any = true;
        }

        return any;
    }

    // 治疗兵器（原作：黑尖塔可维修 Oozium 等）
    private bool TryHealWeapon(Weapon weapon)
    {
        int oldHp = weapon.health;
        int newHp = Mathf.Min(weapon.health + obeliskHealAmount, weapon.maxHealth);
        if (newHp == weapon.health) return false;
        weapon.health = newHp;
        weapon.UpdateHpLabel();
        ShowHealLabel(weapon.GlobalPosition, newHp - oldHp);
        return true;
    }

    // ========== 特效（借鉴 Crystal 的浮动文字） ==========
    private void ShowHealLabel(Vector2 pos, int delta)
    {
        var label = new Label();
        label.Text = $"+{delta / 10}HP"; // 显示为10制HP
        label.Position = pos + new Vector2(-10, -30);
        label.AddThemeColorOverride("font_color", new Color(0.2f, 1f, 0.3f));
        label.AddThemeConstantOverride("outline_size", 2);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeFontSizeOverride("font_size", 16);
        GetTree().Root.AddChild(label);
        FloatAwayLabel(label, pos);
    }

    private void ShowSupplyLabel(Vector2 pos, string text)
    {
        var label = new Label();
        label.Text = text;
        label.Position = pos + new Vector2(-14, -45);
        label.AddThemeColorOverride("font_color", new Color(0.2f, 0.9f, 0.9f));
        label.AddThemeConstantOverride("outline_size", 2);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeFontSizeOverride("font_size", 12);
        GetTree().Root.AddChild(label);
        FloatAwayLabel(label, pos);
    }

    private void FloatAwayLabel(Label label, Vector2 pos)
    {
        var tween = label.CreateTween();
        tween.TweenProperty(label, "position", pos + new Vector2(-10, -60), 1.0f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(label, "modulate:a", 0f, 1.0f);
        tween.TweenCallback(Callable.From(() => { if (IsInstanceValid(label)) label.QueueFree(); }));
    }

    private void ShowObeliskPulseEffect()
    {
        if (animSprite == null) return;
        var tween = CreateTween();
        tween.TweenProperty(animSprite, "modulate", new Color(0.5f, 0.4f, 1f, 1f), 0.25f);
        tween.TweenProperty(animSprite, "modulate", Colors.White, 0.35f);

        // 范围扩散光环（紫色，尖塔专属）
        var pulse = new Sprite2D();
        var frameTex = animSprite.SpriteFrames?.GetFrameTexture(animSprite.Animation, 0);
        if (frameTex == null) return;
        pulse.Texture = frameTex;
        pulse.Position = animSprite.Position;
        pulse.Scale = animSprite.Scale * 0.4f;
        pulse.Modulate = new Color(0.5f, 0.3f, 0.95f, 0.5f);
        pulse.ZIndex = 5;
        AddChild(pulse);

        var pt = pulse.CreateTween();
        pt.TweenProperty(pulse, "scale", animSprite.Scale * 3.0f, 0.7f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
        pt.Parallel().TweenProperty(pulse, "modulate:a", 0f, 0.7f);
        pt.TweenCallback(Callable.From(() => { if (IsInstanceValid(pulse)) pulse.QueueFree(); }));
    }

    // ========== 兵器不能攻击（黑尖塔是纯辅助建筑） ==========
    public override bool CanAttack()
    {
        return false;
    }

    public override void PerformAttack(Infantry target)
    {
        // 黑尖塔不攻击
    }

    public override void RotateDirection()
    {
        // 黑尖塔不旋转
    }

    // ========== AI 执行入口（AI控制时：脉冲治疗） ==========
    public override void ExecuteAI()
    {
        PerformObeliskPulse();
        hasActed = true;
    }

    // ========== 摧毁处理（借鉴 LargeCannon：多格Broken形态，仍不可通行） ==========
    public override void TakeDamage(int damage, Infantry attacker = null)
    {
        if (isDestroyed) return;
        base.TakeDamage(damage, attacker);
    }

    public override void OnDestroyed()
    {
        OnMultiTileDestroyed(); // 标记多格摧毁状态 + Broken动画

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.gridManager?.ClearWeaponRange();
        Grids.IsForceActionMode = false;

        if (gm != null && gm.selectedWeapon == this)
            gm.selectedWeapon = null;

        // 禁用交互
        var area = GetNodeOrNull<Area2D>("Area2D");
        if (area != null)
        {
            area.InputPickable = false;
            area.Monitoring = false;
            area.Monitorable = false;
        }

        if (hpLabel != null) hpLabel.Hide();
        var rotateTip = GetNodeOrNull<Label>("RotateTip");
        if (rotateTip != null) rotateTip.Hide();

        // 摧毁特效：屏震 + 闪光
        ShakeCamera(amplitude: 10f, times: 5);
        var flash = new CannonFlash();
        flash.Position = GlobalPosition;
        GetTree().CurrentScene?.AddChild(flash);

        // ✅ 恢复所有占据格子的weapon引用（摧毁后仍不可通行，与原作一致）
        if (occupiedGrids.Count > 0)
        {
            foreach (var g in occupiedGrids)
            {
                if (g != null && IsInstanceValid(g))
                {
                    if (!g.weapons.Contains(this)) g.weapons.Add(this);
                    if (g.weapon == null) g.weapon = this;
                }
            }
            grid = occupiedGrids[0];

            if (animSprite != null)
            {
                animSprite.Modulate = Colors.White;
                string brokenAnim = GetBrokenAnimName();
                if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation(brokenAnim))
                    animSprite.Play(brokenAnim);
            }
            // ✅ 破碎后熄灭符文发光层
            var glow = GetNodeOrNull<AnimatedSprite2D>("GlowSprite");
            if (glow != null) glow.Hide();
        }

        // 胜利判定检查（黑尖塔常作为过关目标）
        gm?.CheckVictoryCondition();
    }

    private void ShakeCamera(float amplitude, int times)
    {
        var cam = GetViewport().GetCamera2D();
        if (cam == null) return;
        var tween = cam.CreateTween().SetParallel(false);
        for (int i = 0; i < times; i++)
        {
            var rnd = new Vector2(GD.Randf() * amplitude - amplitude * 0.5f,
                                  GD.Randf() * amplitude - amplitude * 0.5f);
            tween.TweenProperty(cam, "position", cam.Position + rnd, 0.05f);
        }
        tween.TweenProperty(cam, "position", cam.Position, 0.05f);
    }

    public override string GetBrokenAnimName()
    {
        return "BrokenObelisk";
    }

    // ✅ 覆盖动画名称：匹配 BlackObelisk.tres 中的动画命名
    public override string GetAnimName()
    {
        return "BlackObelisk";
    }

    // ========== 信息面板 ==========
    public override string GetBlackCannonFullInfo()
    {
        string info = base.GetBlackCannonFullInfo();
        info = info.Replace(TranslateGroup.T("[黑炮]"), TranslateGroup.T("[黑尖塔]"));
        info += $"\n=== {TranslateGroup.T("黑尖塔机制")} ===\n";
        info += $"{TranslateGroup.T("治疗范围")}: {obeliskHealRange}{TranslateGroup.T("格（我方单位+2HP）")}\n";
        info += $"{TranslateGroup.T("补给")}: {(obeliskSupplyFuel ? TranslateGroup.T("油料") : "")}{(obeliskSupplyAmmo ? TranslateGroup.T("弹药") : "")}{TranslateGroup.T("补满")}\n";
        info += $"{TranslateGroup.T("自动脉冲")}: {(obeliskAutoPulse ? TranslateGroup.T("每回合开始") : TranslateGroup.T("关闭"))}\n";
        info += $"=== {TranslateGroup.T("多格占据")} ===\n";
        info += $"{TranslateGroup.T("尺寸")}: {size.X}×{size.Y}\n";
        info += $"{TranslateGroup.T("弱点")}: {TranslateGroup.T("正门（下中）")}\n";
        info += $"{TranslateGroup.T("状态")}: {(isDestroyed ? TranslateGroup.T("已摧毁") : TranslateGroup.T("运作中"))}\n";
        if (contributesToVictory)
        {
            info += $"\n[{TranslateGroup.T("胜利条件")}] {TranslateGroup.T("摧毁此兵器计入胜利判定")}\n";
        }
        return info;
    }

    public override void OnTurnEnd()
    {
        if (isDestroyed) return;
        base.OnTurnEnd();
    }
}
