// ActionMenu.MultiTileRotate.cs - 多格单位"旋转"按钮（issue#1 二期 2026-11-15：对齐兵器 canRotate 体系）
using Godot;

public partial class ActionMenu
{
    private Button multiTileRotateButton;
    private bool multiTileRotateWired = false;

    private void EnsureMultiTileRotateButton()
    {
        if (multiTileRotateButton != null) return;
        var parent = (attackButton?.GetParent() as Container) ?? (moveButton?.GetParent() as Container);
        if (parent == null) return;
        multiTileRotateButton = new Button();
        TranslateGroup.SetText(multiTileRotateButton, "旋转");
        multiTileRotateButton.Name = "MultiTileRotateButton";
        parent.AddChild(multiTileRotateButton);
        if (!multiTileRotateWired)
        {
            multiTileRotateButton.Pressed += OnMultiTileRotatePressed;
            multiTileRotateWired = true;
        }
    }

    // 由 ShowMenu 调用（对齐 UpdateWriggleButton 位）
    public void UpdateMultiTileRotateButton(Infantry unit)
    {
        EnsureMultiTileRotateButton();
        if (multiTileRotateButton == null) return;
        bool show = unit != null && IsInstanceValid(unit) && unit.isMultiTileUnit && unit.canRotate
            && unit.health > 0 && unit.state != UnitState.Acted && !unit.isAttacked;
        multiTileRotateButton.Visible = show;
        if (show)
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            multiTileRotateButton.Disabled = !unit.CanAnyRotationFit(gm);
        }
    }

    private void OnMultiTileRotatePressed()
    {
        var unit = currentUnit;
        if (unit == null || !IsInstanceValid(unit)) return;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        unit.TryRotate(1, gm);
    }
}
