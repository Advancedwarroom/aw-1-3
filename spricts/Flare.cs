// Flare.cs - 照明炮单位，带照明模块系统
using Godot;
using System;
using System.Linq;

public partial class Flare : Infantry
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

    protected override string GetIdleAnimName() => team == "Player2" ? "Flare2" : "Flare1";

    public override void ApplyUnitSpecificDefaults()
    {
        if (!useDefaultConfig) return;
        overlapType = UnitOverlapType.NonOverlapping;
        attackType = AttackType.CanAttack;
        moveType = MoveType.Treads;

        defaultMovePoints = 5;
        movePoints = 5;

        attackRange = 1;
        minAttackRange = 1;
        maxAttackRange = 1;

        // 无主武器
        hasPrimaryWeapon = false;

        // 只有副武器（机枪）
        hasSecondaryWeapon = true;
        secondaryAttack = 100;
        secondaryAntiArmor = true;
        secondaryAntiInfantry = true;

        maxFuel = 60;
        consumeFuel = true;
        lowFuelThreshold = 30;

        counterMul = 0.5f;

        captureAbility = CaptureAbility.CannotCapture;
        capturePower = 10;

        baseAttack = 50;

        // 照明模块默认值（字段在 Infantry 基类）
        canIlluminate = true;
        maxFlareAmmo = 3;
        currentFlareAmmo = 3;
        minLaunchRange = 0;
        maxLaunchRange = 5;
        minIlluminationRange = 0;
        maxIlluminationRange = 2;
        flareDurationTurns = 1;
        flareDurationTurns = 1;
        canIlluminateAfterMove = false;

        cost = 5000;  // Flare造价
    }

    public override void _Process(double delta)
    {
        bool shouldBeIdle = isMoved || isAttacked || state == UnitState.Acted;
        if (shouldBeIdle && (idleSprite == null || !idleSprite.Visible))
            ShowIdleState();

        var icon = GetNodeOrNull<AnimatedSprite2D>("NoFuelIcno");
        if (icon != null)
        {
            if (!consumeFuel) icon.Visible = false;
            else if (fuel <= 0) icon.Visible = true;
            else if (fuel <= lowFuelThreshold) icon.Visible = (Time.GetTicksMsec() / 500) % 2 == 0;
            else icon.Visible = false;
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
        EnsureGridBound();
        ShowAnimState("move");
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.gridManager.ShowMoveRange(this);
    }

    public override void OnWaitSelected()
    {
        isMoved = true; isAttacked = true; movePoints = defaultMovePoints; originalGrid = null;
        FreezeToIdleDark();
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

    public override void OnTurnEnd()
    {
        base.OnTurnEnd();
        string animName = GetIdleAnimName();
        ShowAnimState(animName);
    }
}
