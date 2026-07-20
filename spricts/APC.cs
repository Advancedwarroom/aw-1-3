// APC.cs - 装甲运兵车（简洁模式）
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class APC : Infantry
{
    [Export] public AnimatedSprite2D animSprite;
    [Export] public Sprite2D idleSprite;

    private Tween loadedIconTween;

    public override void _Ready()
    {
        base._Ready();

        if (animSprite == null) animSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (idleSprite == null) idleSprite = GetNodeOrNull<Sprite2D>("Sprite2D");
        if (noFuelIcon == null) noFuelIcon = GetNodeOrNull<AnimatedSprite2D>("NoFuelIcon");

        actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
        hpLabel = GetNodeOrNull<Label>("HpLabel");

        SetupLoadedIcon();
        SetupVisualsByTeam();
        UpdateHpLabel();
        UpdateLoadedIcon();

        string animName = GetIdleAnimName();
        ShowAnimState(animName);
        StartBreath();

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.RegisterUnit(this, UnitCategory.APC);

        EnsureGridBound();
    }

    public override void ApplyUnitSpecificDefaults()
    {
        if (!useDefaultConfig) return; // ✅ Inspector自定义模式：不执行硬编码默认值
        overlapType = UnitOverlapType.NonOverlapping;
        attackType = AttackType.NoAttack;
        moveType = MoveType.Treads;

        defaultMovePoints = 6;

        attackRange = 0;
        minAttackRange = 0;
        maxAttackRange = 0;

        baseAttack = 0;

        hasPrimaryWeapon = false;
        hasSecondaryWeapon = false;

        maxFuel = 70;
        consumeFuel = true;

        counterMul = 0f;
        captureAbility = CaptureAbility.CannotCapture;
        canAttackAfterMoving = false;

        // 搭载配置（默认在Inspector中覆盖）
        canTransportUnits = true;
        canSupplyUnits = true;  // APC可以补给其他单位
        maxTransportCapacity = 1;
        canTransportUnits = true;
        maxTransportCapacity = 1;
        if (canTransportUnitTypes == null || canTransportUnitTypes.Count == 0)
        {
            canTransportUnitTypes = new Godot.Collections.Array<string> { "Infantry", "Mech" };
        }

        minSupplyRange = 1;
        maxSupplyRange = 1;

        cost = 5000;  // APC造价
        maxSupplyRange = 1;
    }

    protected override string GetIdleAnimName() => team == "Player2" ? "apc2" : "apc1";

    #region 特有逻辑（装载图标、补给、呼吸等）

    private void SetupLoadedIcon()
    {
        if (loadedIcon == null) return;
        var spriteFrames = GD.Load<SpriteFrames>("res://asscets/AnimatedSprite/loaded.tres");
        if (spriteFrames == null) return;
        loadedIcon.SpriteFrames = spriteFrames;
        loadedIcon.Animation = "loaded";
        loadedIcon.Hide();
        loadedIcon.ZIndex = 200;
    }

    private void StartLoadedIconBlink()
    {
        if (loadedIcon == null) return;
        StopLoadedIconBlink();
        loadedIcon.Show();
        loadedIcon.Play();
        var timer = new Timer();
        timer.WaitTime = 1.0f;
        timer.OneShot = false;
        timer.Autostart = true;
        timer.Timeout += () => { if (loadedIcon != null && IsInstanceValid(loadedIcon)) loadedIcon.Visible = !loadedIcon.Visible; };
        AddChild(timer);
        SetMeta("blink_timer", timer);
    }

    private void StopLoadedIconBlink()
    {
        if (loadedIconTween != null && loadedIconTween.IsValid()) { loadedIconTween.Kill(); loadedIconTween = null; }
        if (HasMeta("blink_timer"))
        {
            var timer = GetMeta("blink_timer").As<Timer>();
            if (timer != null && IsInstanceValid(timer)) { timer.Stop(); timer.QueueFree(); }
            RemoveMeta("blink_timer");
        }
        if (loadedIcon != null) { loadedIcon.Hide(); loadedIcon.Modulate = Colors.White; loadedIcon.Visible = false; }
    }

    public override void OnDestroyed()
    {
        StopLoadedIconBlink();
        foreach (var unit in transportedUnits.ToList())
        {
            if (unit != null && IsInstanceValid(unit))
            {
                var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
                gm?.RemovePiece(unit);
                unit.QueueFree();
            }
        }
        transportedUnits.Clear();
        base.OnDestroyed();
    }

    public void OnTurnEnd()
    {
        originalGrid = null;
        movePoints = defaultMovePoints;
        isMoved = false;
        isAttacked = false;
        hasActed = false;
        state = UnitState.Idle;
        string animName = GetIdleAnimName();
        ShowAnimState(animName);
        StartBreath();
        UpdateLoadedIcon();
    }

    public override void _Process(double delta)
    {
        UpdateNoFuelIcon();
    }

    public new void StartBreath()
    {
        if (animSprite == null || !IsInstanceValid(animSprite)) return;
        breathTween?.Kill();
        animSprite.Scale = Vector2.One;
        breathTween = CreateTween();
        if (breathTween == null) return;
        breathTween.SetLoops();
        breathTween.SetProcessMode(Tween.TweenProcessMode.Idle);
        breathTween.TweenProperty(animSprite, "scale", Vector2.One * 1.05f, 1.2f)
                   .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        breathTween.TweenProperty(animSprite, "scale", Vector2.One, 1.2f)
                   .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    public new void StopBreath()
    {
        if (animSprite == null || !IsInstanceValid(animSprite)) return;
        breathTween?.Kill();
        animSprite.Scale = Vector2.One;
    }

    public override void SetWaitVisual(bool waiting)
    {
        if (waiting)
        {
            StopBreath();
            if (animSprite != null) { animSprite.Modulate = dim; animSprite.Stop(); }
            if (idleSprite != null) { idleSprite.Show(); idleSprite.Modulate = dim; }
        }
        else
        {
            if (animSprite != null) { animSprite.Modulate = normal; animSprite.Play(GetIdleAnimName()); }
            StartBreath();
        }
        if (hpLabel != null) hpLabel.Modulate = Colors.White;
    }

    public void ShowAnimState(string animName)
    {
        if (animSprite == null) return;
        if (idleSprite != null) idleSprite.Hide();
        animSprite.Show();
        animSprite.Modulate = normal;
        string actualAnim = animName;
        if (!animSprite.SpriteFrames.HasAnimation(actualAnim))
            actualAnim = GetIdleAnimName();
        if (animSprite.SpriteFrames.HasAnimation(actualAnim)) animSprite.Play(actualAnim);
    }

    public void ShowIdleState()
    {
        if (idleSprite != null) idleSprite.Hide();
        if (animSprite != null)
        {
            animSprite.Show();
            string idleAnimName = GetIdleAnimName();
            if (animSprite.SpriteFrames.HasAnimation(idleAnimName)) { animSprite.Play(idleAnimName); animSprite.Stop(); animSprite.Modulate = dim; }
        }
    }

    private void SetupVisualsByTeam()
    {
        if (animSprite == null) return;
        string animName = GetIdleAnimName();
        if (animSprite.SpriteFrames.HasAnimation(animName)) animSprite.Play(animName);
    }

    private void UpdateNoFuelIcon()
    {
        var icon = GetNodeOrNull<AnimatedSprite2D>("NoFuelIcon");
        if (icon == null) icon = GetNodeOrNull<AnimatedSprite2D>("NoFuelIcno");
        if (icon == null) return;
        if (!consumeFuel) icon.Visible = false;
        else if (fuel <= 0) icon.Visible = true;
        else if (fuel <= 30) icon.Visible = (Time.GetTicksMsec() / 500) % 2 == 0;
        else icon.Visible = false;
    }

    private void EnsureGridBound()
    {
        if (grid != null && IsInstanceValid(grid)) return;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.unitManager != null) gm.unitManager.BindUnitToGrid(this, true);
    }

    #endregion

    #region 覆盖交互方法

    public override void OnMoveSelected()
    {
        if (consumeFuel && fuel <= 0) return;
        ShowAnimState("move");
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.gridManager.ShowMoveRange(this);
    }

    public override void OnWaitSelected()
    {
        isMoved = true; isAttacked = true; hasActed = true; state = UnitState.Acted; originalGrid = null;
        ShowIdleState();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.ClearSelectedInfantry();
    }

    public override void InputClick(Node viewport, InputEvent inputs, int shape_index)
    {
        if (inputs is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
        {
            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm == null) return;

            if (grid != null)
            {
                var mountableUnit = grid.infantries
                    .FirstOrDefault(u => u != this && u.team == this.team && !u.isAttacked && u.state != UnitState.Acted && this.CanTransportUnit(u));
                if (mountableUnit != null)
                {
                    gm.gridManager?.CloseRange();
                    gm.gridManager?.HideAttackRange();
                    gm.gridManager?.ClearWeaponRange();
                    if (gm.selectedInfantry != null && gm.selectedInfantry != mountableUnit) gm.ClearSelectedInfantry();
                    gm.selectedWeapon = null;
                    gm.isSelectingAttackTarget = false;
                    var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                    actionMenu?.Hide();
                    actionMenu?.ShowTransportLoadMenu(this, mountableUnit);
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }

            bool isMyTurn = gm.IsTurnPhaseValid(team);
            if (!isMyTurn) return;

            if (Input.IsKeyPressed(Key.Ctrl))
            {
                bool IsMyTurn = gm.IsTurnPhaseValid(team);
                bool isEnemy = !IsMyTurn;
                if ((IsMyTurn && isMoved) || isEnemy)
                {
                    gm.gridManager.CloseRange();
                    gm.gridManager.HideAttackRange();
                    var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                    actionMenu?.Hide();
                    ShowUnitInfo();
                    return;
                }
            }

            if (this.transportedUnits.Count > 0)
            {
                var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                actionMenu?.ShowTransportUnloadMenu(this);
                return;
            }

            base.InputClick(viewport, inputs, shape_index);
        }
    }

    #endregion

    public string GetAPCInfo()
    {
        string info = $"[APC] 装甲运兵车\n团队: {team}\n生命值: {health}/{maxHealth}\n燃料: {fuel}/{maxFuel}\n移动力: {movePoints}\n装载容量: {maxTransportCapacity}\n当前装载: {transportedUnits.Count}\n补给范围: {minSupplyRange}-{maxSupplyRange}\n";
        if (transportedUnits.Count > 0)
        {
            info += "\n[装载单位]\n";
            foreach (var unit in transportedUnits)
                if (unit != null && IsInstanceValid(unit))
                    info += $"- {unit.Name} (HP:{unit.health})\n";
        }
        return info;
    }
}
