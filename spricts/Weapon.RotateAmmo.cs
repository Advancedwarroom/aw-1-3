// Weapon.RotateAmmo.cs - 兵器旋转弹：与单位弹药表第14种同口径（开创人令 2026-11-15）
// 消费点在 AI 转炮瞄准（AI_Manager.Weapons）；武器攻击时的自对准旋转属攻击动作，不另计费。
using Godot;

public partial class Weapon
{
    [ExportGroup("旋转弹")]
    /// <summary>最大值m：0=弹药关闭=旋转无限次；池上限=充能超出不计载</summary>
    [Export] public int maxRotateAmmo = 0;
    /// <summary>当前值（现有库存）</summary>
    [Export] public int currentRotateAmmo = 0;
    /// <summary>冷却池开关：关=固定机会（不耗弹不攒弹）</summary>
    [Export] public bool rotatePoolEnabled = false;
    /// <summary>每回合免费旋转次数n；第n次后待机（兵器默认0=不触发，AI转炮是瞄准动作）</summary>
    [Export] public int rotatePerTurnBeforeActed = 0;
    /// <summary>每k个自己回合充能1发（0=不自动充能）</summary>
    [Export] public int rotateChargeEveryNTurns = 1;
    /// <summary>一次性连发上限：连转n次后锁死，须等一个完整充能轮回（不影响攒弹，只拦调用）</summary>
    [Export] public int rotateMaxConsecutiveUses = 3;
    /// <summary>没使用是否阻拦回合刷新：开=有存货时充能被拦；关=照常累加（超m不计载）</summary>
    [Export] public bool rotateUnusedBlocksRefresh = false;

    public int rotateConsecutiveUses = 0;
    public int rotateLockTurns = 0;
    public int rotateChargeCounter = 0;

    public bool RotateAmmoEnabled => maxRotateAmmo > 0;

    public bool CanRotateByAmmo()
    {
        if (!RotateAmmoEnabled) return true;
        if (rotateLockTurns > 0) return false;
        return rotatePoolEnabled ? currentRotateAmmo >= 1 : true;
    }

    public bool ConsumeRotateAmmo()
    {
        if (!RotateAmmoEnabled) return true;
        if (rotatePoolEnabled)
        {
            if (currentRotateAmmo < 1) return false;
            currentRotateAmmo--;
        }
        rotateConsecutiveUses++;
        if (rotateMaxConsecutiveUses > 0 && rotateConsecutiveUses >= rotateMaxConsecutiveUses)
            rotateLockTurns = Mathf.Max(1, rotateChargeEveryNTurns);
        return true;
    }

    /// <summary>✅ 自己回合开始：锁递减+充能周期（Turn.cs 本势力武器循环统一调用）</summary>
    public void RotateAmmoTick()
    {
        if (!RotateAmmoEnabled) return;
        if (rotateLockTurns > 0)
        {
            rotateLockTurns--;
            if (rotateLockTurns == 0) rotateConsecutiveUses = 0;
        }
        if (!rotatePoolEnabled || rotateChargeEveryNTurns <= 0) return;
        rotateChargeCounter++;
        if (rotateChargeCounter < rotateChargeEveryNTurns) return;
        rotateChargeCounter = 0;
        if (rotateUnusedBlocksRefresh && currentRotateAmmo > 0) return;
        if (currentRotateAmmo < maxRotateAmmo) currentRotateAmmo++;
    }
}
