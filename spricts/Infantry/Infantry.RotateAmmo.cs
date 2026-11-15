// Infantry.RotateAmmo.cs - 旋转弹：弹药表第14种 + 冷却池/连发锁/n次待机规则（开创人令 2026-11-15）
// 与主弹药互不影响，两套硬性限制独立；maxRotateAmmo<=0=弹药关闭=旋转无限次（两种模式都不启用）。
using Godot;

public partial class Infantry
{
    [ExportGroup("旋转弹（弹药表第14种）")]
    /// <summary>最大值m：0=弹药关闭=旋转无限次；池上限=充能超出不计载</summary>
    [Export] public int maxRotateAmmo = 0;
    /// <summary>当前值（现有库存）</summary>
    [Export] public int currentRotateAmmo = 0;
    /// <summary>冷却池开关：关=原作模式，每回合固定免费机会(rotatePerTurnBeforeActed次)，不耗弹不攒弹</summary>
    [Export] public bool rotatePoolEnabled = false;
    /// <summary>每回合免费旋转次数n；第n次后进入和攻击一样的待机（两种模式通用；0=不限次数不触发待机）</summary>
    [Export] public int rotatePerTurnBeforeActed = 1;
    /// <summary>每k个自己回合充能1发（0=不自动充能）</summary>
    [Export] public int rotateChargeEveryNTurns = 1;
    /// <summary>一次性连发上限：连转n次后锁死，须等一个完整充能轮回（不影响攒弹，只拦调用）</summary>
    [Export] public int rotateMaxConsecutiveUses = 3;
    /// <summary>没使用是否阻拦回合刷新：开=有存货时充能被拦（先花再充）；关=照常累加（超m不计载）</summary>
    [Export] public bool rotateUnusedBlocksRefresh = false;

    // 运行时状态
    public int rotateUsesThisTurn = 0;      // 本回合已转次数（n次待机规则）
    public int rotateConsecutiveUses = 0;   // 连续使用计数（连发锁）
    public int rotateLockTurns = 0;         // 连发锁剩余回合（>0=锁定）
    public int rotateChargeCounter = 0;     // 充能周期计数

    /// <summary>弹药是否启用（maxRotateAmmo>0）</summary>
    public bool RotateAmmoEnabled => maxRotateAmmo > 0;

    /// <summary>旋转资格（弹药+连发锁判定；不含地形/侧格/每回合次数）</summary>
    public bool CanRotateByAmmo()
    {
        if (!isMultiTileUnit || !canRotate) return false;
        if (!RotateAmmoEnabled) return true;                        // 弹药关闭=无限次
        if (rotateLockTurns > 0) return false;                      // 连发锁
        return rotatePoolEnabled ? currentRotateAmmo >= 1 : true;   // 池模式要弹；固定模式免费
    }

    /// <summary>✅ 消费一次旋转：耗弹+连发计数（弹药关闭=免费不计数）</summary>
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
            rotateLockTurns = Mathf.Max(1, rotateChargeEveryNTurns);   // 用满连发：锁一个完整充能轮回（从头冷却）
        return true;
    }

    /// <summary>✅ 自己回合开始：本回合计数复位 + 锁递减 + 充能周期（GameManager 本势力阶段统一调用）</summary>
    public void RotateAmmoTick()
    {
        rotateUsesThisTurn = 0;
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
        // 充能：未使用阻拦刷新（开=有存货不充）；超过上限m不计载
        if (rotateUnusedBlocksRefresh && currentRotateAmmo > 0) return;
        if (currentRotateAmmo < maxRotateAmmo) currentRotateAmmo++;
    }
}
