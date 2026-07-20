// PipeRunner.cs - 管道炮：仅限管道移动，远程间接火力，射程2-5，可移动后攻击，被攻击不能反击
using Godot;
using System;
using System.Linq;

public partial class PipeRunner : Infantry
{
    [Export] public AnimatedSprite2D animSprite;
    [Export] public Sprite2D idleSprite;

    public override void _Ready()
    {
        base._Ready();

        if (animSprite == null) animSprite = GetNodeOrNull<AnimatedSprite2D>("Base2D");
        if (animSprite == null) animSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (idleSprite == null) idleSprite = GetNodeOrNull<Sprite2D>("BaseSprite");
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
        if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation(animName)) animSprite.Play(animName);
        else if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation("idle")) animSprite.Play("idle");
    }

    protected override string GetIdleAnimName() => team == "Player2" ? "PipeRunner2" : "PipeRunner1";

    public override void ApplyUnitSpecificDefaults()
    {
        if (!useDefaultConfig) return;

        overlapType = UnitOverlapType.NonOverlapping;
        attackType = AttackType.CanAttack;
        moveType = MoveType.PipeRunner;

        defaultMovePoints = 9;
        movePoints = 9;

        minAttackRange = 2;
        maxAttackRange = 5;
        attackRange = maxAttackRange;  // ✅ 与 max 一致
        useMinMaxAttackRange = true;

        hasPrimaryWeapon = true;
        primaryHasLimitedAmmo = true;
        maxPrimaryAmmo = 9;
        currentPrimaryAmmo = maxPrimaryAmmo;
        primaryAntiArmor = true;
        primaryAntiInfantry = true;

        hasSecondaryWeapon = false;

        maxFuel = 99;
        fuel = 99;
        consumeFuel = false;
        dailyFuelConsumption = 0;
        destroyOnOutOfFuel = false;

        canAttackAfterMoving = true;

        // ✅ 间接攻击单位：攻击时别人不能反击，自己被攻击时不能反击
        cannotCounterWhenAttacked = true;
        canCounterWhenDefending = false;
        canCounterAtRange = false;

        counterMul = 0.5f;

        captureAbility = CaptureAbility.CannotCapture;
        capturePower = 0;

        visionRange = 4;
        useConfigVision = true;

        unitCategory = UnitCategory.Other;

        defenseBonusType = DefenseBonusType.CanDefenseBonus;

        lowFuelThreshold = 15;

        canExplode = false;
        canIlluminate = false;

        baseAttack = 50;

        cost = 20000;
    }

    public override void _Process(double delta)
    {
        bool shouldBeIdle = isMoved || isAttacked || state == UnitState.Acted;
        if (shouldBeIdle && (idleSprite == null || !idleSprite.Visible))
            ShowIdleState();

        if (noFuelIcon != null)
        {
            noFuelIcon.Visible = false;
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
        // ✅ 防御性 null 检查
        if (animSprite.SpriteFrames == null) return;
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
            if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation(idleAnimName))
                animSprite.Animation = idleAnimName;
            else if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation("idle"))
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
            if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation(idleAnimName))
                animSprite.Animation = idleAnimName;
            else if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation("idle"))
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
        if (!CanUsePrimaryWeapon()) return;
        if (!canAttackAfterMoving && state == UnitState.Moved) return;
        if (animSprite != null) animSprite.Modulate = dim;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.gridManager.ShowAttackRange(this);
    }

    public override bool CanAttackAfterMove()
    {
        if (state == UnitState.Idle) return true;
        return canAttackAfterMoving;
    }

// PipeRunner.cs
public override void OnTurnEnd()
{
    base.OnTurnEnd();
    string animName = GetIdleAnimName();
    ShowAnimState(animName);
}
}
