// Rocket.cs - 火箭炮：远程间接火力，射程3-5格
using Godot;
using System;
using System.Linq;

public partial class Rocket : Infantry
{
    [Export] public AnimatedSprite2D animSprite;
    [Export] public Sprite2D idleSprite;

    public override void _Ready()
    {
        base._Ready();

        if (animSprite == null) animSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (idleSprite == null) idleSprite = GetNodeOrNull<Sprite2D>("Sprite2D");
        if (noFuelIcon == null) noFuelIcon = GetNodeOrNull<AnimatedSprite2D>("NoFuelIcno");
        if (noAmmoIcon == null) noAmmoIcon = GetNodeOrNull<AnimatedSprite2D>("NoAmmoIcon");

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

    protected override string GetIdleAnimName() => team == "Player2" ? "rocket2" : "rocket1";

    public override void ApplyUnitSpecificDefaults()
    {
        if (!useDefaultConfig) return; // ✅ Inspector自定义模式：不执行硬编码默认值
        overlapType = UnitOverlapType.NonOverlapping;
        attackType = AttackType.CanAttack;
        moveType = MoveType.Tire;

        defaultMovePoints = 4;

        minAttackRange = 3;
        maxAttackRange = 5;
        attackRange = maxAttackRange;

        hasPrimaryWeapon = true;
        primaryHasLimitedAmmo = true;
        maxPrimaryAmmo = 6;
        currentPrimaryAmmo = maxPrimaryAmmo;
        primaryAntiArmor = true;
        primaryAntiInfantry = true;

        hasSecondaryWeapon = false;

        maxFuel = 50;
        consumeFuel = true;

        counterMul = 0.3f;
        cannotCounterWhenAttacked = true;
        canCounterWhenDefending = false;  // 被攻击时不能反击（间接单位）

        captureAbility = CaptureAbility.CannotCapture;

        cost = 15000;  // Rocket造价
    }

    public override void _Process(double delta)
    {
        bool shouldBeIdle = isMoved || isAttacked || state == UnitState.Acted;
        if (shouldBeIdle && (idleSprite == null || !idleSprite.Visible))
            ShowIdleState();

        if (noFuelIcon != null)
        {
            if (!consumeFuel) noFuelIcon.Visible = false;
            else if (fuel <= 0) noFuelIcon.Visible = true;
            else if (fuel <= lowFuelThreshold) noFuelIcon.Visible = (Time.GetTicksMsec() / 500) % 2 == 0;
            else noFuelIcon.Visible = false;
        }

        if (noAmmoIcon != null)
        {
            if (!CanUsePrimaryWeapon()) noAmmoIcon.Visible = true;
            else if (currentPrimaryAmmo <= 3) noAmmoIcon.Visible = (Time.GetTicksMsec() / 500) % 2 == 0;
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
        if (animSprite != null)
        {
            string idleAnimName = GetIdleAnimName();
            if (animSprite.SpriteFrames.HasAnimation(idleAnimName))
                animSprite.Animation = idleAnimName;
            else if (animSprite.SpriteFrames.HasAnimation("idle"))
                animSprite.Animation = "idle";
            animSprite.Frame = 0;
            animSprite.Stop();
            animSprite.Show();
            animSprite.Modulate = dim;
        }
        if (idleSprite != null) idleSprite.Hide();
    }

    private void FreezeToIdleDark()
    {
        breathTween?.Kill();
        if (animSprite != null)
        {
            string idleAnimName = GetIdleAnimName();
            if (animSprite.SpriteFrames.HasAnimation(idleAnimName))
                animSprite.Animation = idleAnimName;
            else if (animSprite.SpriteFrames.HasAnimation("idle"))
                animSprite.Animation = "idle";
            animSprite.Frame = 0;
            animSprite.Stop();
            animSprite.Show();
            animSprite.Modulate = dim;
        }
        if (idleSprite != null) idleSprite.Hide();
    }

    public override void OnMoveSelected()
    {
        if (consumeFuel && fuel <= 0) return;
        EnsureGridBound();
        ShowAnimState("move");
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.gridManager.ShowMoveRange(this);
    }

    public override void OnWaitSelected()
    {
        isMoved = true; isAttacked = true; state = UnitState.Acted; originalGrid = null;
        FreezeToIdleDark();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.ClearSelectedInfantry();
    }

    public override void OnAttackSelected()
    {
        EnsureGridBound();
        if (attackType == AttackType.NoAttack) return;
        if (!CanUsePrimaryWeapon())
        {
            return;
        }
        if (!canAttackAfterMoving && state == UnitState.Moved)
        {
            return;
        }
        if (animSprite != null) animSprite.Modulate = dim;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.gridManager.ShowAttackRange(this);
    }

    public override bool CanAttackAfterMove()
    {
        if (state == UnitState.Idle) return true;
        return canAttackAfterMoving;
    }

    public void OnTurnEnd()
    {
        originalGrid = null; movePoints = defaultMovePoints;
        isMoved = false; isAttacked = false; state = UnitState.Idle;
        string animName = GetIdleAnimName();
        ShowAnimState(animName);
        StartBreath();
    }
}
