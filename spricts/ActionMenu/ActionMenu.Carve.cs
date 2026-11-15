// ActionMenu.Carve.cs — ✅ 开创人2026-11-15: 山脉开道·游戏内开凿按钮（镜像传送按钮模式）
using Godot;
using System;

public partial class ActionMenu
{
    private Button _carveButton;

    /// <summary>建一次：放在移动按钮后。</summary>
    public void EnsureCarveButton()
    {
        if (_carveButton != null && GodotObject.IsInstanceValid(_carveButton)) return;
        _carveButton = new Button { Text = "开凿" };
        _carveButton.Pressed += OnCarvePressed;
        var parent = moveButton.GetParent();
        parent.AddChild(_carveButton);
        int idx = moveButton.GetIndex();
        parent.MoveChild(_carveButton, idx + 2);
    }

    /// <summary>菜单刷新可见性——可开凿且四邻有未凿高山。</summary>
    public void UpdateCarveButtonVisibility(Infantry unit)
    {
        EnsureCarveButton();
        if (unit == null || !GodotObject.IsInstanceValid(unit)) { _carveButton.Visible = false; return; }
        bool show = unit.canCarveMountains && !unit.isMoved && !unit.isAttacked && FindCarveTarget(unit) != null;
        _carveButton.Visible = show;
    }

    private Grids FindCarveTarget(Infantry unit)
    {
        if (unit?.grid?.availableGrids == null) return null;
        foreach (var g in unit.grid.availableGrids)
        {
            if (g == null || g.carvedPath) continue;
            if (unit.GetMoveCost(g.gridType) >= 999) return g;
        }
        return null;
    }

    private void OnCarvePressed()
    {
        if (currentUnit == null) return;
        var target = FindCarveTarget(currentUnit);
        if (target == null) return;
        if (!ActionResourceRuntime.CanUse(currentUnit, "carve")) return;
        if (currentUnit.TryCarveMountain(target))
        {
            ActionResourceRuntime.Consume(currentUnit, "carve");
            ActionResourceRuntime.FireEvents(currentUnit, "carve");
            HideMenu();
        }
    }
}
