// ActionMenu.Empowerment.cs - 单位/兵器/设施共用手动赋能与解能按钮。
using Godot;

public partial class ActionMenu
{
    private Button empowermentActionButton;
    private Node empowermentSource;
    private Grids empowermentOrigin;

    public void UpdateEmpowermentButton(Node source)
    {
        if (empowermentActionButton == null)
        {
            empowermentActionButton = new Button();
            empowermentActionButton.Name = "EmpowermentActionButton";
            empowermentActionButton.Pressed += () => BeginEmpowermentSelection(empowermentSource);
            AddChild(empowermentActionButton);
        }
        empowermentSource = source;
        bool visible = source != null && EmpowermentRuntime.CanManualUse(source);
        empowermentActionButton.Visible = visible;
        if (!visible) return;
        string actionName = GetEmpowermentActionName(source);
        empowermentActionButton.Text = actionName;
    }

    private static string GetEmpowermentActionName(Node source)
    {
        var field = source.GetType().GetField("empowermentActionName");
        string configured = field?.GetValue(source) as string;
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        int operation = source.GetType().GetField("empowermentOperationType")?.GetValue(source) is int value ? value : 0;
        return operation == 1 ? "解能" : "赋能";
    }

    public void BeginEmpowermentSelection(Node source)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null || !EmpowermentRuntime.CanManualUse(source)) return;
        Hide();
        empowermentSource = source;
        empowermentOrigin = source switch
        {
            Infantry unit => unit.grid,
            Weapon weapon => weapon.grid,
            _ => null
        };
        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = true;
        bool any = false;
        foreach (var grid in EmpowermentRuntime.GetManualCastCells(source))
        {
            any = true;
            grid.Modulate = new Color(0.45f, 0.85f, 1f, 0.9f);
            grid.IsInWeaponRange = true;
            gm.gridManager.OverrideUnitInput(grid, true);
            grid.OnClickGrid = ConfirmEmpowermentSelection;
        }
        foreach (var node in GetTree().GetNodesInGroup("grids"))
            if (node is Grids grid && !grid.IsInWeaponRange)
            {
                gm.gridManager.OverrideUnitInput(grid, true);
                grid.OnClickEmpty = CancelEmpowermentSelection;
            }
        if (!any) CancelEmpowermentSelection();
    }

    private void ConfirmEmpowermentSelection(Grids target)
    {
        var source = empowermentSource;
        CancelEmpowermentSelection();
        if (source != null) EmpowermentRuntime.Activate(source, target);
    }

    private void CancelEmpowermentSelection()
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager != null)
        {
            gm.gridManager.ClearWeaponRange();
            foreach (var node in GetTree().GetNodesInGroup("grids"))
                if (node is Grids grid)
                {
                    gm.gridManager.OverrideUnitInput(grid, false);
                    grid.OnClickGrid = null;
                    grid.OnClickEmpty = null;
                }
        }
        Grids.IsForceActionMode = false;
        empowermentSource = null;
        empowermentOrigin = null;
    }
}
