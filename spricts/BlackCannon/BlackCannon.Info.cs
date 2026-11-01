// BlackCannon.cs - 黑炮：三角形范围，固定百分比伤害，支持自定义目标选择和改良伤害模式
// ✅ 新增：完整弹药系统 + 多回合冷却系统（还原原版高级战争原型炮/完全体差异）
using Godot;
using System.Collections.Generic;
using System.Linq;

// BlackCannon · Info（由 BlackCannon.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class BlackCannon
{

    // ========== ✅ 新增：获取黑炮完整信息（用于信息面板） ==========
    public override string GetBlackCannonFullInfo()
    {
        string info = "";

        info += $"[黑炮] {Name}\n";
        info += $"团队: {team}\n";
        info += $"血量: {health}/{maxHealth} ({Mathf.CeilToInt(health/10f)}格)\n";
        info += $"状态: {(hasActed ? TranslateGroup.T("已行动") : TranslateGroup.T("待机"))}\n";

        // ✅ 弹药信息
        info += $"\n=== 弹药系统 ===\n";
        if (useAmmoSystem)
        {
            info += $"弹药: {currentAmmo}/{maxAmmo}\n";
            info += $"补给状态: {(currentAmmo >= maxAmmo ? TranslateGroup.T("已满") : TranslateGroup.T("可补给"))}\n";
        }
        else
        {
            info += $"弹药: 无限 (∞)\n";
        }

        // ✅ 冷却信息
        info += $"\n=== 冷却系统 ===\n";
        if (useCooldownSystem)
        {
            info += $"冷却周期: {cooldownTurns}回合\n";
            info += $"周期攻击次数: {attacksPerCooldown}次\n";
            info += $"存次数: {(storeAttacks ? TranslateGroup.T("是") : TranslateGroup.T("否"))}\n";
            info += $"当前周期进度: {turnsSinceLastAttack}/{cooldownTurns}回合\n";
            info += $"周期剩余次数: {attacksRemainingInCycle}次\n";
            info += $"冷却就绪: {(cooldownReady ? TranslateGroup.T("✓ 是") : TranslateGroup.T("✗ 否"))}\n";

            if (!cooldownReady)
            {
                info += $"还需等待: {cooldownTurns - turnsSinceLastAttack}回合\n";
            }
        }
        else
        {
            info += $"冷却: 无（每回合重置）\n";
            info += $"每回合次数: {maxAttacksPerTurn}次\n";
        }

        // ✅ 综合攻击状态
        info += $"\n=== 攻击状态 ===\n";
        info += $"可攻击: {(CanAttack() ? TranslateGroup.T("✓ 是") : TranslateGroup.T("✗ 否"))}\n";
        if (!CanAttack())
        {
            if (useAmmoSystem && currentAmmo <= 0)
                info += $"原因: 弹药耗尽\n";
            if (useCooldownSystem && !cooldownReady)
                info += $"原因: 冷却中 ({turnsSinceLastAttack}/{cooldownTurns}回合)\n";
            if (useCooldownSystem && attacksRemainingInCycle <= 0)
                info += $"原因: 周期次数已用完\n";
        }

        info += $"\n=== 配置信息 ===\n";
        info += $"射程深度: {maxAttackDepth}格\n";
        info += $"伤害模式: {(useModifiedDamage ? TranslateGroup.T("改良模式") : TranslateGroup.T("传统模式"))}\n";
        info += $"朝向: {direction}\n";
        info += $"可攻击兵器: {(CanAttackWeapon ? TranslateGroup.T("是") : TranslateGroup.T("否"))}\n";
        info += $"目标选择: {targetSelectionMode switch 
        { 
            TargetSelectionMode.AllSelect => TranslateGroup.T("所有单位"), 
            TargetSelectionMode.OnlyEnemyUnits => TranslateGroup.T("仅敌方单位"), 
            TargetSelectionMode.OnlyUserUnits => TranslateGroup.T("仅己方单位"), 
            _ => TranslateGroup.T("未知")
        }}\n";

        if (!useModifiedDamage)
        {
            info += $"固定伤害: {fixedDamagePercent * 100:F0}%\n";
            info += $"可摧毁: {(CanDestroy ? TranslateGroup.T("是") : TranslateGroup.T("否"))}\n";
        }
        else
        {
            info += $"血量变化: {modifiedHealthPercent * 100:F0}% ({(modifiedHealthPercent > 0 ? TranslateGroup.T("扣血") : modifiedHealthPercent < 0 ? TranslateGroup.T("回血") : TranslateGroup.T("无变化"))})\n";
            info += $"弹药变化: {modifiedAmmoPercent * 100:F0}%\n";
            info += $"燃料变化: {modifiedFuelPercent * 100:F0}%\n";
            info += $"可超血量上限: {(canOverMaxHp ? TranslateGroup.T("是") : TranslateGroup.T("否"))}\n";
        }

        info += $"\n[操作说明]\n";
        info += $"• 左键点击: 选择/显示攻击范围\n";
        info += $"• R键: 旋转方向\n";
        info += $"• 攻击范围: 三角形区域，深度{maxAttackDepth}格\n";

        if (useAmmoSystem)
        {
            info += $"• 需要APC在范围内补给弹药\n";
        }

        return info;
    }
}
