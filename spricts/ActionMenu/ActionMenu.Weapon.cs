// ActionMenu.cs - 完整修复版，移除所有unloadButton直接引用
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;

// ActionMenu · Weapon（由 ActionMenu.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class ActionMenu
{

   public void ShowWeaponMenu(Weapon weapon)
    {
        currentUnit = null;
        currentWeapon = weapon;

        if (!IsNodeReady()) return;

        // ✅ 统一隐藏所有不需要的按钮
        moveButton.Visible = false;
        captureButton.Visible = false;
        rotateButton?.Hide();
        explodeButton?.Hide();
        illuminateButton?.Hide();
        stealthButton?.Hide();
        scentButton?.Hide();
        loadButton?.Hide();
        supplyButton.Visible = false;

        // ✅ Crystal 黑水晶：显示"治疗"按钮，隐藏"攻击"按钮
        if (weapon is Crystal crystal)
        {
            bool canHeal = crystal.CanAttack(); // CanAttack = !hasActed && remainingAttacks > 0
            attackButton.Visible = false;
            if (healButton != null)
            {
                healButton.Visible = canHeal;
            }
        }
        else
        {
            if (healButton != null) healButton.Visible = false;

            // ✅ 修改：根据弹药和冷却状态判断是否可以攻击
            bool canAttack = weapon.CanAttack();
            bool hasTargetInRange = false;

            if (canAttack)
            {
                var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
                var range = weapon.CalculateAttackRange();
                var fog = gm?.fogOfWarManager;
                bool isFogEnabled = fog != null && fog.isFogOfWarEnabled;
                hasTargetInRange = range.Any(g => 
                {
                    if (isFogEnabled && fog != null && !fog.IsGridVisible(g)) return false;
                    if (weapon is BlackCannon cannon)
                        return cannon.HasValidTargetInGrid(g);
                    return g.HasEnemyInfantry(weapon.team) || (weapon.CanAttackWeapon && g.weapon != null && g.weapon.team != weapon.team);
                });
            }

            attackButton.Visible = canAttack && hasTargetInRange;
        }

        waitButton.Visible = true;
        infoButton.Visible = true;

        unitInfoLabel.Visible = false;
        closeInfoButton.Visible = false;

        SetupEmptyClickToCloseMenuOnly(weapon);

        // ✅ 超级兵工厂：显示"生产"按钮
        UpdateFactoryButtons();

        // ✅ 黑色弧光兵器：显示"天基打击"按钮（十字准星瞄准）
        UpdateArcStrikeButton();

        // ✅ 大雷弹：显示"蠕动"按钮（新机制）
        UpdateWriggleButton();

        Position = weapon.GlobalPosition + new Vector2(-40, -100);
        Show();
    }
}
