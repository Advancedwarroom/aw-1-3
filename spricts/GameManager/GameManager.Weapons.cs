// GameManager.cs - 添加完整的单位统计系统
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// GameManager · Weapons（由 GameManager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class GameManager
{


public void OnSelectWeapon(Weapon weapon)
{
    if (weapon == null || weapon.hasActed || weapon.isDestroyed) return; // ✅ 已摧毁的兵器不可选择
    if (!IsTurnPhaseValid(weapon.team)) return;

    // ✅ 关键：清除单位选择
    selectedInfantry = null;
    gridManager.HideAttackRange(); // 清除单位攻击范围
    gridManager.CloseRange(); // 清除移动范围

    selectedWeapon = weapon;
    HideProductionMenu();

    var menu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
    menu?.ShowWeaponMenu(weapon); // 这会清除 currentUnit
}

    // ✅ 修改：移除兵器现在通过WeaponManager处理
public void RemoveWeapon(Weapon weapon)
{
    if (weapon == null || !GodotObject.IsInstanceValid(weapon)) return;

    // Keep tile.weapons and the legacy tile.weapon pointer synchronized for
    // both single-tile and multi-tile removals.
    if (weaponManager != null)
    {
        weaponManager.UnbindWeaponFromGrid(weapon);
        weaponManager.AllWeapons.Remove(weapon);
    }
    else
    {
        var occupied = new List<Grids>(weapon.occupiedGrids);
        if (weapon.grid != null && !occupied.Contains(weapon.grid))
            occupied.Add(weapon.grid);
        foreach (var tile in occupied)
        {
            if (tile == null || !GodotObject.IsInstanceValid(tile)) continue;
            tile.weapons?.Remove(weapon);
            if (tile.weapon == weapon)
            {
                tile.weapon = null;
                if (tile.weapons != null)
                    foreach (var candidate in tile.weapons)
                        if (candidate != null && GodotObject.IsInstanceValid(candidate)
                            && !candidate.IsQueuedForDeletion() && !candidate.isDestroyed)
                        { tile.weapon = candidate; break; }
            }
        }
        weapon.occupiedGrids.Clear();
    }

    weapons.Remove(weapon);
    if (selectedWeapon == weapon) selectedWeapon = null;
    weapon.QueueFree();
    UpdateUnitLists();
}
}
