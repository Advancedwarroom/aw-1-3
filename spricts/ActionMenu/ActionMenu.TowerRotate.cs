// ActionMenu.cs - 完整修复版，移除所有unloadButton直接引用
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;

// ActionMenu · TowerRotate（由 ActionMenu.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class ActionMenu
{

    // ========== ✅ 瞭望塔旋转菜单 ==========
    public void ShowGridRotateMenu(Grids grid)
    {
        if (!IsNodeReady()) return;

        if (rotateButton == null)
        {
            return;
        }

        moveButton?.Hide();
        attackButton?.Hide();
        waitButton?.Hide();
        captureButton?.Hide();
        infoButton?.Hide();
        loadButton?.Hide();
        if (unloadContainer != null) unloadContainer.Visible = false;
        supplyButton?.Hide();

        rotateButton.Visible = true;

        foreach (var conn in rotateButton.GetSignalConnectionList(BaseButton.SignalName.Pressed))
        {
            rotateButton.Disconnect(BaseButton.SignalName.Pressed, conn["callable"].AsCallable());
        }

        rotateButton.Pressed += () => RotateTower(grid);

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager != null)
        {
            foreach (var g in gm.gridManager.grids)
            {
                g.OnClickEmpty = () => {
                    Hide();
                    gm.gridManager.ClearAllEmptyCallbacks();
                };
            }
        }

        Position = grid.GlobalPosition + new Vector2(-40, -80);
        Show();
    }

    private void RotateTower(Grids grid)
    {
        if (grid == null || !IsInstanceValid(grid)) return;

        grid.towerDirection = grid.towerDirection switch
        {
            TowerDirection.Up => TowerDirection.Right,
            TowerDirection.Right => TowerDirection.Down,
            TowerDirection.Down => TowerDirection.Left,
            TowerDirection.Left => TowerDirection.Up,
            _ => TowerDirection.Up
        };

        var arrow = grid.TowerArrow;
        arrow.Visible = true;
        arrow.RotationDegrees = grid.towerDirection switch
        {
            TowerDirection.Up => 0,
            TowerDirection.Right => 90,
            TowerDirection.Down => 180,
            TowerDirection.Left => 270,
            _ => 0
        };

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.fogOfWarManager?.RefreshFog();

    }
}
