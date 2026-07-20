// FlyBomb.cs - 飞弹单位（继承 Infantry）
using Godot;
using System;
using System.Linq;

public partial class FlyBomb : Infantry
{
    [Export] public AnimatedSprite2D animSprite;

    private bool isAnimating = false;
    private string currentAnimState = "idle";

    public override void _Ready()
    {
        base._Ready();

        if (animSprite == null) animSprite = GetNodeOrNull<AnimatedSprite2D>("Base2D");
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

    protected override string GetIdleAnimName() => team == "Player2" ? "FlyBomb2" : "FlyBomb1";

    public override void ApplyUnitSpecificDefaults()
    {
        if (!useDefaultConfig) return; // ✅ Inspector自定义模式：不执行硬编码默认值
        overlapType = UnitOverlapType.NonOverlapping;
        attackType = AttackType.NoAttack;
        moveType = MoveType.Missile;

        defaultMovePoints = 9;

        attackRange = 1;
        minAttackRange = 1;
        maxAttackRange = 1;

        hasPrimaryWeapon = false;
        hasSecondaryWeapon = false;

        maxFuel = 45;
        consumeFuel = true;
        lowFuelThreshold = 20;
        dailyFuelConsumption = 5;

        counterMul = 0f;

        captureAbility = CaptureAbility.CannotCapture;

        IsAirUnit = true;
        defenseBonusType = DefenseBonusType.NoDefenseBonus;
        destroyOnOutOfFuel = true;
        canExplode = true;
        explosionMaxRange = 3;
        explosionDamageMode = 1;
        explosionSelfDamageEnabled = false;
        explosionDestroysSelf = false;  // 无限自爆模式
        maxExplodeAmmo = 5;
        currentExplodeAmmo = 5;

        cost = 25000;  // FlyBomb造价
    }

    public override void _Process(double delta)
    {
        bool nowActed = isAttacked || state == UnitState.Acted;
        if (nowActed && currentAnimState != "idle")
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
        animSprite.Show();
        animSprite.Modulate = normal;
        string actualAnim = animName;
        if (!animSprite.SpriteFrames.HasAnimation(actualAnim))
            actualAnim = GetIdleAnimName();
        if (animSprite.SpriteFrames.HasAnimation(actualAnim)) animSprite.Play(actualAnim);
        isAnimating = true;
        currentAnimState = animName;
    }

    public void ShowIdleState()
    {
        if (animSprite != null)
        {
            animSprite.Stop();
            animSprite.Modulate = dim;
        }
        isAnimating = false;
        currentAnimState = "idle";
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
        isMoved = true;
        isAttacked = true;
        state = UnitState.Acted;
        originalGrid = null;
        ShowIdleState();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.ClearSelectedInfantry();
    }

    public override void InputClick(Node viewport, InputEvent inputs, int shape_index)
    {
        if (inputs is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
        {
            if (mouseEvent.ButtonIndex == MouseButton.Left)
            {
                EnsureGridBound();
                var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
                if (gm == null) return;

                if (this.state == UnitState.Moved && this.originalGrid != null)
                {
                    gm.RollbackMove();
                    return;
                }

                if (gm.selectedInfantry != null && gm.selectedInfantry != this &&
                    gm.selectedInfantry.state == UnitState.Moved &&
                    gm.selectedInfantry.originalGrid != null)
                {
                    if (gm.isSelectingAttackTarget && this.team != gm.selectedInfantry.team)
                        return;
                    else
                    {
                        gm.RollbackMove();
                        return;
                    }
                }

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

                if (grid != null && grid.infantries.Count > 1)
                {
                    string currentTeam = gm.turnPhase == 1 ? "Player1" : "Player2";
                    var myTeamUnits = grid.infantries
                        .Where(u => u.team == currentTeam && !u.isMoved && IsInstanceValid(u))
                        .OrderByDescending(u => u.health)
                        .ToList();
                    if (myTeamUnits.Count > 0 && myTeamUnits[0] != this)
                    {
                        gm.OnSelectPiece(myTeamUnits[0]);
                        return;
                    }
                }

                bool isMyTurn = gm.IsTurnPhaseValid(team);
                if (isMyTurn && !isMoved)
                {
                    if (consumeFuel && fuel <= 0 && !isAttacked)
                    {
                        gm.OnSelectPiece(this);
                        return;
                    }
                    if (consumeFuel && fuel <= 0 && isAttacked)
                    {
                        ShowUnitInfo();
                        return;
                    }

                    if (gm.selectedInfantry != null && gm.selectedInfantry != this)
                    {
                        var prevUnit = gm.selectedInfantry;
                        gm.gridManager.CloseRange();
                        gm.gridManager.HideAttackRange();
                        var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                        actionMenu?.Hide();

                        if (prevUnit.state == UnitState.Moved &&
                            prevUnit.overlapType != UnitOverlapType.Oozium &&
                            prevUnit.originalGrid != null)
                        {
                            gm.RollbackMove();
                            return;
                        }
                        else
                        {
                            gm.ClearSelectedInfantry();
                        }
                    }
                    gm.OnSelectPiece(this);
                }
            }
        }
    }

    public void OnTurnEnd()
    {
        originalGrid = null;
        movePoints = defaultMovePoints;
        isMoved = false;
        isAttacked = false;
        state = UnitState.Idle;
        string animName = GetIdleAnimName();
        ShowAnimState(animName);
        StartBreath();
    }
}
