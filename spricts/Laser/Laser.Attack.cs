// Laser.cs - 高级激光炮系统（修复版）
// 修复内容：
// 1. 多次攻击同步 remainingAttacks 和 attacksRemainingInCycle
// 2. 添加局内角度配置API（EnableAngle/DisableAngle/SetFireAngles等）
// 3. ✅ 移除 attackedThisRound 限制：允许同一回合多次攻击同一目标
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// Laser · Attack（由 Laser.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Laser
{

    // ========== ✅ 核心修复：多次攻击逻辑 ==========

    private void ExecuteGlobalAttack()
    {
        if (!IsInstanceValid(this) || remainingAttacks <= 0) return;
        if (!HasAmmoAndCooldown()) return;

        var allTargets = GetAllValidTargets();
        // 不再限制同一回合重复攻击同一目标
        var newTargets = allTargets;

        if (newTargets.Count == 0) return;

        if (!ConsumeAmmo()) return;

        // ✅ 修复：统一递减逻辑
        if (useCooldownSystem)
        {
            attacksRemainingInCycle--;
            // 同步 remainingAttacks，保持两者一致
            remainingAttacks = attacksRemainingInCycle;
        }
        else
        {
            remainingAttacks--;
        }

        foreach (var targetInfo in newTargets)
        {
            if (targetInfo.Target is Infantry infantry)
                ApplyDamageToTarget(infantry, targetInfo.BaseDamagePercent);
            else if (targetInfo.Target is Weapon weapon)
                ApplyDamageToWeapon(weapon, targetInfo.BaseDamagePercent);
        }

        PlayGlobalLaserEffect(newTargets);

        // ✅ 修复：根据正确的剩余次数判断
        int actualRemaining = useCooldownSystem ? attacksRemainingInCycle : remainingAttacks;

        if (actualRemaining > 0)
        {
            // 攻击后刷新范围显示，允许继续攻击（包括已攻击过的目标）
            var timer = GetTree().CreateTimer(0.3f);
            timer.Timeout += () => {
                if (IsInstanceValid(this) && !hasActed)
                    ShowAttackRange();
            };
        }
        else
        {
            HandlePostAttack();
        }
    }

    // ========== 伤害应用 ==========

    private void ApplyDamageToTarget(Infantry target, float baseDamagePercent)
    {
        if (!IsInstanceValid(target)) return;
        // ✅ N屏隔离：不同屏的单位不受跨图攻击
        if (target.grid != null && IsScreenBlocked(target.grid)) return;
        if (useModifiedDamage)
            ApplyModifiedDamage(target, baseDamagePercent);
        else
            ApplyTraditionalDamage(target, baseDamagePercent);
    }

    private void ApplyTraditionalDamage(Infantry target, float baseDamagePercent)
    {
        int damage = Mathf.RoundToInt(target.maxHealth * baseDamagePercent);
        damage = Mathf.Max(1, damage);
        damage = Mathf.Max(0, damage - target.fixedDefense);
        if (damage <= 0) return;
        if (!CanDestroy)
            target.health = Mathf.Max(1, target.health - damage);
        else
            target.health -= damage;
        target.UpdateHpLabel();
        if (target.health <= 0 && !target.TryFlee())
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.RemovePiece(target);
            target.CallDeferred(Node.MethodName.QueueFree);
        }
    }

    private void ApplyModifiedDamage(Infantry target, float baseDamagePercent)
    {
        int healthChange = Mathf.RoundToInt(target.maxHealth * Mathf.Abs(baseDamagePercent));
        if (modifiedHealthPercent > 0)
        {
            if (!CanDestroy)
                target.health = Mathf.Max(1, target.health - healthChange);
            else
                target.health -= healthChange;
        }
        else if (modifiedHealthPercent < 0)
        {
            if (canOverMaxHp)
                target.health += healthChange;
            else
                target.health = Mathf.Min(target.maxHealth, target.health + healthChange);
        }

        if (modifiedAmmoPercent != 0 && target.hasPrimaryWeapon && target.primaryHasLimitedAmmo)
        {
            float ammoEffectRatio = baseDamagePercent / fixedDamagePercent;
            int ammoChange = Mathf.RoundToInt(target.maxPrimaryAmmo * Mathf.Abs(modifiedAmmoPercent) * ammoEffectRatio);
            if (modifiedAmmoPercent > 0)
                target.currentPrimaryAmmo = Mathf.Max(0, target.currentPrimaryAmmo - ammoChange);
            else
                target.currentPrimaryAmmo = Mathf.Min(target.maxPrimaryAmmo, target.currentPrimaryAmmo + ammoChange);
        }

        if (modifiedFuelPercent != 0 && target.consumeFuel)
        {
            float fuelEffectRatio = baseDamagePercent / fixedDamagePercent;
            int fuelChange = Mathf.RoundToInt(target.maxFuel * Mathf.Abs(modifiedFuelPercent) * fuelEffectRatio);
            if (modifiedFuelPercent > 0)
                target.fuel = Mathf.Max(0, target.fuel - fuelChange);
            else
                target.fuel = Mathf.Min(target.maxFuel, target.fuel + fuelChange);
        }

        target.UpdateHpLabel();
        if (target.health <= 0 && CanDestroy && !target.TryFlee())
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.RemovePiece(target);
            target.CallDeferred(Node.MethodName.QueueFree);
        }
    }

    private void ApplyDamageToWeapon(Weapon target, float baseDamagePercent)
    {
        if (!IsInstanceValid(target)) return;
        // ✅ 力场保护：被存活黑弧力场保护的地图目标物，激光伤害无效
        if (!target.ignoreForceField && BlackArc.IsGridForceFieldProtected(target.grid)) return;
        // ✅ N屏隔离：不同屏的兵器不受跨图攻击
        if (IsScreenBlocked(target.grid)) return;
        int damage = Mathf.RoundToInt(target.maxHealth * baseDamagePercent);
        damage = Mathf.Max(1, damage);
        damage = Mathf.Max(0, damage - target.fixedDefense);
        if (damage <= 0) return;
        if (!CanDestroy)
            target.health = Mathf.Max(1, target.health - damage);
        else
            target.health -= damage;
        target.UpdateHpLabel();
        if (target.health <= 0)   // 兵器无逃跑模块（TryFlee 仅 Infantry 有）
            target.CallDeferred(Node.MethodName.QueueFree);
    }
}
