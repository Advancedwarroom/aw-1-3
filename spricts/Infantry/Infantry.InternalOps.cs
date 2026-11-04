using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// Infantry · InternalOps（内部单位操作：选中运输车后，经操作面板直接指挥舱内单位）
// 设计来源：开创人 2026-11-04 原型机五件套规格（运输模块 × 内部单位操作面板联动）
public partial class Infantry
{
    [ExportGroup("内部单位操作")]
    [Export] public bool canOperateLoadedUnits = false;            // 1. 搭载后能否操作内部单位
    [Export] public int maxLoadedOperationsPerTurn = 1;            // 2. 每回合内部单位操作数上限
    [Export] public Godot.Collections.Array<string> loadedActionBlacklist = new(); // 3. 行为黑名单（如 "Stealth"）
    [Export] public float loadedOperationFactor = 0.5f;            // 4. 效果削弱/增强系数（0.5=减半）
    [Export] public bool stealthSyncsToTransport = false;          // 5. 隐身效果同步到运输车（通用同步模块·隐身先行）

    // 运行状态
    public int loadedOperationsUsedThisTurn = 0;

    /// <summary>舱内可操作的单位（有货 + 开关开 + 黑名单不过滤由调用方判断行为）</summary>
    public List<Infantry> GetOperableLoadedUnits()
    {
        if (!canOperateLoadedUnits) return new List<Infantry>();
        return transportedUnits.Where(u => u != null && IsInstanceValid(u)).ToList();
    }

    /// <summary>某行为是否在黑名单中（行为ID：Attack/Capture/Stealth/TearFlag）</summary>
    public bool IsActionBlacklisted(string actionId)
    {
        if (string.IsNullOrEmpty(actionId)) return false;
        return loadedActionBlacklist.Any(b => string.Equals(b, actionId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>本回合还能操作几次</summary>
    public int GetRemainingLoadedOperations()
    {
        return Math.Max(0, maxLoadedOperationsPerTurn - loadedOperationsUsedThisTurn);
    }

    /// <summary>执行一次舱内操作（面板调用）：计数+1</summary>
    public void ConsumeLoadedOperation()
    {
        loadedOperationsUsedThisTurn++;
    }

    /// <summary>系数换算（攻击伤害/占领进度/撕旗进度/隐身消耗共用，负数钳到1）</summary>
    public int ApplyLoadedFactor(int value)
    {
        return Math.Max(1, (int)Math.Round(value * loadedOperationFactor));
    }

    /// <summary>回合开始重置（由 TurnManager 的舱内追踪同款入口调用）</summary>
    public void ResetLoadedOperationTracking()
    {
        loadedOperationsUsedThisTurn = 0;
    }
}
