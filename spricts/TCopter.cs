// FlyBomb.cs
using Godot;
using System;
using System.Linq;

public partial class TCopter : Infantry
{
    public override string GetIdleAnimName() => team == "Player2" ? "TCopter2" : "TCopter1";

    public override void _Ready()
    {
        base._Ready();
        // FlyBomb 的特殊初始化已通过 UnitRegistry 完成
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
        if (inputs is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
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
                gm.selectedInfantry.state == UnitState.Moved && gm.selectedInfantry.originalGrid != null)
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
