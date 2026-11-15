// ActionMenu.Carve.cs — ✅ 开创人2026-11-15: 山脉开道·开凿按钮（实现参谋二预留的 UpdateCarveButton() 调用）
using Godot;
using System;

public partial class ActionMenu
{
    private Button _carveButton;

    public void EnsureCarveButton()
    {
        if (_carveButton != null && GodotObject.IsInstanceValid(_carveButton)) return;
        _carveButton = new Button { Text = "开凿" };
        _carveButton.Pressed += OnCarvePressed;
        var parent = moveButton.GetParent();
        parent.AddChild(_carveButton);
        parent.MoveChild(_carveButton, moveButton.GetIndex() + 2);
    }

    /// <summary>参谋二在ShowMenu预留的调用点（无参，用currentUnit）。</summary>
    public void UpdateCarveButton()
    {
        EnsureCarveButton();
        var unit = currentUnit;
        if (unit == null || !GodotObject.IsInstanceValid(unit)) { _carveButton.Visible = false; return; }
        _carveButton.Visible = unit.canCarveMountains && !unit.isMoved && !unit.isAttacked && FindCarveTarget(unit) != null;
    }

    private Grids FindCarveTarget(Infantry unit)
    {
        if (unit?.grid == null) return null;
        try
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            var gi = unit.grid.GridIndex;
            foreach (var off in new[] { new Vector2I(1, 0), new Vector2I(-1, 0), new Vector2I(0, 1), new Vector2I(0, -1) })
            {
                var g = gm?.gridManager?.GetGridAt(gi + off);
                if (g == null || g.carvedPath) continue;
                if (unit.GetMoveCost(g.gridType) >= 999) return g;
            }
        }
        catch { }
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
            Visible = false;
        }
    }
}
