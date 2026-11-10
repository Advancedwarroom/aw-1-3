using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · Input（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{
    public virtual void OnWaitSelected()
    {
        StopBreath();
        isMoved = true;
        isAttacked = true;
        hasActed = true;   // 待机=行动完毕：回合系统/AI/再选择都认这个标志（缺了它=假待机）
        state = UnitState.Acted;
        movePoints = defaultMovePoints;
        FreezeToIdleDark();
        originalGrid = null;

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.ClearSelectedInfantry();
    }

    // ✅ 修改 InputClick - 移动后点击任何单位都触发回退
// Infantry.cs - 修改 InputClick 方法
public virtual void InputClick(Node viewport, InputEvent inputs, int shape_index)
{
    // ✅ 回合中回合冻结：遭遇战决策期间全操作禁入
    {
        var gm0 = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm0 != null && gm0.encounterPause) return;
    }
    // ✅ 被迷雾/隐身隐藏的单位不响应点击（Area2D 拾取不受 Visible 影响，否则会暴露位置/信息）
    if (!IsVisibleInTree()) return;


    if (inputs is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
    {
        if (mouseEvent.ButtonIndex == MouseButton.Left)
        {
            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm == null) return;

            // ✅ 关键修复：如果当前有其他单位正在移动范围模式下，且该单位不是我自己
            // 说明这是移动范围点击，不是单位点击，忽略此次输入
            if (gm.selectedInfantry != null && 
                gm.selectedInfantry != this && 
                gm.gridManager != null &&
                gm.gridManager.moveRange.Count > 0)
            {
                // ✅ 范围外的己方可行动单位：直接切换选中（不必先点空地取消）
                bool inRange = grid != null && gm.gridManager.moveRange.Contains(grid);
                bool selMoved = gm.selectedInfantry.state == UnitState.Moved;
                if (inRange || selMoved || isMoved || state == UnitState.Acted || !gm.IsTurnPhaseValid(team))
                    return;
            }


            if (this.state == UnitState.Moved && this.originalGrid != null)
            {
                
                // 检查点击的是否是其他可移动单位
                if (this.overlapType == UnitOverlapType.Overlapping && 
                    grid != null && 
                    grid.infantries.Count > 1)
                {
                    var otherUnit = grid.infantries.FirstOrDefault(u => u != this && u != null && IsInstanceValid(u) && !u.isMoved);
                    if (otherUnit != null)
                    {
                        var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                        actionMenu?.ShowMoveChoiceDialog(this, otherUnit, grid);
                        return;
                    }
                }
                
                // gm?.RollbackMove(); // 不自动回退，让用户选择
                return;
            }


                        if (this.state == UnitState.Moved && this.originalGrid != null)
            {

            }
                    if (currentCaptureProgress > 0 && capturingGrid == grid && !isMoved)
        {
            gm?.OnSelectPiece(this);
            return;
        }

            // 检查是否是点击其他已移动的单位
if (gm.selectedInfantry != null && gm.selectedInfantry != this && 
    gm.selectedInfantry.state == UnitState.Moved && 
    gm.selectedInfantry.originalGrid != null)
{
    if (gm.isSelectingAttackTarget && this.team != gm.selectedInfantry.team)
                    {
                        return;
                    }
    else
    {
        gm.RollbackMove();
        return;
    }
}


if (Input.IsKeyPressed(Key.Ctrl))
{
    gm.gridManager.CloseRange();
    gm.gridManager.HideAttackRange();
    gm.gridManager.ClearWeaponRange();
    var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
    actionMenu?.Hide();
    ShowUnitInfo();
    GetViewport().SetInputAsHandled();
    return;
}


            // ✅ 究极自由：同格多单位选择逻辑
            if (grid != null && grid.infantries.Count > 1)
            {
                string currentTeam = gm.turnPhase == 1 ? "Player1" : "Player2";

                // 优先选择：未移动的、可搭载的单位（如果当前单位需要搭载）
                var transportUnits = grid.infantries
                    .Where(u => u.team == currentTeam 
                        && u.canTransportUnits 
                        && u.maxTransportCapacity > 0
                        && IsInstanceValid(u)
                        && u.CanTransportUnit(this))   // ✅ 统一搭载判定（白名单/容量/套娃）
                    .ToList();

                // 如果有可搭载单位且当前单位可以被搭载，显示搭载菜单
                if (transportUnits.Count > 0 && !this.isMoved && this.state != UnitState.Acted)
                {
                    var transport = transportUnits[0];
                    if (transport.CanTransportUnit(this))
                    {
                        var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                        actionMenu?.ShowTransportMenu(transport, afterMove: false);
                        return;
                    }
                }

                // 否则按原逻辑选择
                var myTeamUnits = grid.infantries
                    .Where(u => u.team == currentTeam && !u.isMoved && IsInstanceValid(u))
                    .OrderByDescending(u => u.health)
                    .ToList();

                if (myTeamUnits.Count > 0)
                {
                    var targetUnit = myTeamUnits[0];
                    if (targetUnit != this)
                    {
                        gm.OnSelectPiece(targetUnit);
                        return;
                    }
                }
            }

            bool isMyTurn = gm.IsTurnPhaseValid(team);

            if (isMyTurn)   // ✅ 开创人2026-11-11：放开!isMoved——已移动单位点击交给OnSelectPiece统一处理
            {
                // 取消之前的选中
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
                        return;  // ✅ 添加 return，避免继续执行选中逻辑
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
 
    protected void ShowUnitInfo()
    {
        var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
        if (actionMenu == null) return;

        var originalUnit = actionMenu.currentUnit;
        actionMenu.currentUnit = this;
        actionMenu.OnInfoPressed();
    }
}
