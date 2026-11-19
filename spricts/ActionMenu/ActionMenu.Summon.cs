using Godot;
using System;

public partial class ActionMenu
{
    private Button summonButton;

    public void EnsureSummonButton()
    {
        if (summonButton != null && IsInstanceValid(summonButton)) return;
        if (moveButton == null) return;
        summonButton = new Button { Name = "SummonButton" };
        TranslateGroup.SetText(summonButton, "召唤");
        summonButton.Pressed += OnSummonPressed;
        var parent = moveButton.GetParent();
        parent.AddChild(summonButton);
        parent.MoveChild(summonButton, moveButton.GetIndex() + 1);
    }

    public void UpdateSummonButtonVisibility(Infantry unit)
    {
        EnsureSummonButton();
        if (summonButton == null || !IsInstanceValid(summonButton)) return;
        bool can = unit != null && unit.summonEnabled
            && (!unit.summonRequiresCoUnit || unit.isCoUnit)
            && unit.summonCooldownRemaining <= 0
            && !unit.hasActed && unit.state != UnitState.Acted && !unit.isAttacked
            && !string.IsNullOrWhiteSpace(unit.summonUnitName);
        summonButton.Visible = can;
        if (can) TranslateGroup.SetText(summonButton,
            string.IsNullOrWhiteSpace(unit.summonActionName) ? "召唤" : unit.summonActionName);
    }

    private void OnSummonPressed()
    {
        var unit = currentUnit;
        if (unit == null || !IsInstanceValid(unit)) return;
        var dlg = new ConfirmationDialog
        {
            DialogText = $"召唤 {unit.summonUnitName} ×{Mathf.Max(1, unit.summonCount)}？（存在 {unit.summonLifespan} 回合）",
            OkButtonText = "召唤", CancelButtonText = "取消"
        };
        AddChild(dlg);
        dlg.Confirmed += () => { unit.PerformSummon(); ShowMenu(unit, false); dlg.QueueFree(); };
        dlg.Canceled += () => dlg.QueueFree();
        dlg.PopupCentered();
    }
}
