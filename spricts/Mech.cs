// Mech.cs
using Godot;
using System;
using System.Linq;

public partial class Mech : Infantry
{
    [Export] public int primaryAttack = 120;
    [Export] public int SecondaryAttack = 80;
    [Export] public AnimatedSprite2D animSprite;
    [Export] public Sprite2D idleSprite;

    private bool _wasActed = false;

    public override void _Ready()
    {
        base._Ready();

        if (noAmmoIcon == null) noAmmoIcon = GetNodeOrNull<AnimatedSprite2D>("NoAmmoIcon");
        if (animSprite == null) animSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (idleSprite == null) idleSprite = GetNodeOrNull<Sprite2D>("Sprite2D");

        actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
        hpLabel = GetNodeOrNull<Label>("HpLabel");

        SetupVisualsByTeam();
        UpdateHpLabel();

        string animName = GetIdleAnimName();
        ShowAnimState(animName);
        StartBreath();
        EnsureGridBound();
    }

    private void EnsureGridBound()
    {
        if (grid != null && IsInstanceValid(grid))
        {
            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm?.unitManager != null)
            {
                var expectedPos = gm.unitManager.GridToWorld(grid.GridIndex);
                if (Position.DistanceTo(expectedPos) > 1) Position = expectedPos;
            }
            return;
        }
        var gameManager = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gameManager?.unitManager != null)
        {
            bool bound = gameManager.unitManager.BindUnitToGrid(this, true);
            if (!bound || grid == null)
            {
                var gridPos = gameManager.unitManager.WorldToGrid(Position);
                if (gameManager.unitManager.IsValidGrid(gridPos))
                {
                    var targetGrid = gameManager.gridManager.map[gridPos.X, gridPos.Y];
                    if (targetGrid != null)
                    {
                        grid = targetGrid;
                        if (!targetGrid.infantries.Contains(this)) targetGrid.infantries.Add(this);
                        if (targetGrid.infantry == null) targetGrid.infantry = this;
                        Position = targetGrid.Position;
                    }
                }
            }
        }
    }

    private void SetupVisualsByTeam()
    {
        if (animSprite == null) return;
        string animName = GetIdleAnimName();
        if (animSprite.SpriteFrames.HasAnimation(animName)) animSprite.Play(animName);
        else if (animSprite.SpriteFrames.HasAnimation("idle")) animSprite.Play("idle");
    }

    protected override string GetIdleAnimName() => team == "Player2" ? "mech2" : "mech1";

    public override void ApplyUnitSpecificDefaults()
    {
        if (!useDefaultConfig) return; // ✅ Inspector自定义模式：不执行硬编码默认值
        overlapType = UnitOverlapType.NonOverlapping;
        attackType = AttackType.CanAttack;
        moveType = MoveType.Mech;

        defaultMovePoints = 2;

        attackRange = 1;
        minAttackRange = 1;
        maxAttackRange = 1;

        hasPrimaryWeapon = true;
        primaryHasLimitedAmmo = true;
        maxPrimaryAmmo = 3;
        currentPrimaryAmmo = maxPrimaryAmmo;
        primaryAntiArmor = true;
        primaryAntiInfantry = false;

        hasSecondaryWeapon = true;
        secondaryAttack = SecondaryAttack > 0 ? SecondaryAttack : 80;
        secondaryAntiArmor = true;
        secondaryAntiInfantry = true;

        maxFuel = 70;
        consumeFuel = true;
        lowFuelThreshold = 30;

        counterMul = 0.4f;

        if (canTransportUnitTypes == null || canTransportUnitTypes.Count == 0)
            canTransportUnitTypes = new Godot.Collections.Array<string> { "Infantry" };

        captureAbility = CaptureAbility.CanCapture;
        capturePower = 10;

        cost = 3000;  // Mech造价
        capturePower = 10;
    }

    public override void _Process(double delta)
    {
        bool nowActed = isAttacked || state == UnitState.Acted;
        if (nowActed && !_wasActed)
            ShowIdleState();
        else if (!nowActed && _wasActed)
            RestoreAnimAndBreath();
        _wasActed = nowActed;

        var icon = GetNodeOrNull<AnimatedSprite2D>("NoFuelIcno");
        if (icon != null)
        {
            if (!consumeFuel) icon.Visible = false;
            else if (fuel <= 0) icon.Visible = true;
            else if (fuel <= lowFuelThreshold) icon.Visible = (Time.GetTicksMsec() / 500) % 2 == 0;
            else icon.Visible = false;
        }

        if (noAmmoIcon != null)
        {
            if (!hasPrimaryWeapon) noAmmoIcon.Visible = false;
            else if (!CanUsePrimaryWeapon()) noAmmoIcon.Visible = true;
            else if (currentPrimaryAmmo == 1)
                noAmmoIcon.Visible = (Time.GetTicksMsec() / 500) % 2 == 0;
            else noAmmoIcon.Visible = false;
        }
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
        if (animSprite != null) { animSprite.Stop(); animSprite.Hide(); }
        if (idleSprite != null)
        {
            idleSprite.Show();
            idleSprite.Modulate = dim;
        }
        else if (animSprite != null)
        {
            animSprite.Show();
            animSprite.Modulate = dim;
            animSprite.Stop();
            animSprite.Frame = 0;
        }
    }

    private void RestoreAnimAndBreath()
    {
        string animName = GetIdleAnimName();
        ShowAnimState(animName);
        StartBreath();
    }

    public override void OnMoveSelected()
    {
        EnsureGridBound();
        ShowAnimState("move");
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.gridManager.ShowMoveRange(this);
    }

    public override void OnWaitSelected()
    {
        isMoved = true; isAttacked = true; movePoints = defaultMovePoints; originalGrid = null;
        breathTween?.Kill();
        ShowIdleState();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.ClearSelectedInfantry();
    }

    public override void OnAttackSelected()
    {
        EnsureGridBound();
        if (attackType == AttackType.NoAttack) return;
        ShowAnimState("attack");
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.gridManager.ShowAttackRange(this);
    }

    public override void SetWaitVisual(bool waiting)
    {
        if (!waiting)
        {
            string animName = GetIdleAnimName();
            ShowAnimState(animName);
            StartBreath();
        }
        else
        {
            ShowIdleState();
        }
    }

    public override bool IsArmoredTarget(Infantry target)
    {
        // 优先使用目标的 isArmoredUnit 属性（零硬编码，新单位自动适配）
        if (target.isArmoredUnit) return true;
        // Mech 攻击 Mech 时使用副武器（不视为装甲单位）
        if (target is Mech) return false;
        // 后备：用 GameManager 分类
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm != null && gm.unitCategories.TryGetValue(target, out var category))
        {
            return category == UnitCategory.Tank || category == UnitCategory.Vehicle;
        }
        return false;
    }

    public void OnTurnEnd()
    {
        originalGrid = null; movePoints = defaultMovePoints;
        isMoved = false; isAttacked = false; state = UnitState.Idle;
        breathTween?.Kill();
        breathTween = null;
        if (animSprite != null) animSprite.Scale = Vector2.One;
        string animName = GetIdleAnimName();
        ShowAnimState(animName);
        StartBreath();
    }
}
