// ActionMenu.Shelter.cs - 兵器庇护卸载按钮与落点选择。
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class ActionMenu
{
    private VBoxContainer shelterUnloadContainer;
    private Weapon shelterSelectionWeapon;
    private Infantry shelterSelectionUnit;

    public void UpdateShelterButtons(Weapon weapon)
    {
        if (shelterUnloadContainer == null)
        {
            shelterUnloadContainer = new VBoxContainer();
            AttachDrag(shelterUnloadContainer);
            shelterUnloadContainer.Name = "ShelterUnloadButtons";
            AddChild(shelterUnloadContainer);
        }
        foreach (Node child in shelterUnloadContainer.GetChildren()) child.QueueFree();
        shelterUnloadContainer.Visible = weapon != null && weapon.shelteredUnits.Count > 0;
        if (!shelterUnloadContainer.Visible) return;

        foreach (var unit in weapon.shelteredUnits.ToList())
        {
            if (unit == null || !IsInstanceValid(unit)) continue;
            var button = new Button();
            button.Text = $"卸载 {unit.unitDisplayName}";
            button.Pressed += () => BeginShelterUnloadSelection(weapon, unit);
            shelterUnloadContainer.AddChild(button);
        }
    }

    public void BeginShelterUnloadSelection(Weapon weapon, Infantry unit)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null || weapon?.grid == null || unit == null) return;
        Hide();
        shelterSelectionWeapon = weapon;
        shelterSelectionUnit = unit;
        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = true;
        bool any = false;
        foreach (var grid in gm.gridManager.GetNeighbours(weapon.grid))
        {
            if (!weapon.CanUnloadShelterUnitToGrid(unit, grid)) continue;
            any = true;
            grid.Modulate = new Color(0.4f, 1f, 0.65f, 0.9f);
            grid.IsInWeaponRange = true;
            gm.gridManager.OverrideUnitInput(grid, true);
            grid.OnClickGrid = target => ConfirmShelterUnload(target);
        }
        foreach (var node in GetTree().GetNodesInGroup("grids"))
            if (node is Grids grid && !grid.IsInWeaponRange)
            {
                gm.gridManager.OverrideUnitInput(grid, true);
                grid.OnClickEmpty = CancelShelterUnloadSelection;
            }
        if (!any) CancelShelterUnloadSelection();
    }

    private void ConfirmShelterUnload(Grids target)
    {
        var weapon = shelterSelectionWeapon;
        var unit = shelterSelectionUnit;
        CancelShelterUnloadSelection();
        if (weapon != null && unit != null) weapon.UnloadShelterUnit(unit, target);
    }

    private void CancelShelterUnloadSelection()
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
        shelterSelectionWeapon = null;
        shelterSelectionUnit = null;
    }
}
