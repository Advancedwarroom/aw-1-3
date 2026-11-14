using Godot;
using System;

// ============================================================
// Infantry.IllegalTerrain.cs —— 非法地形存在策略（开创人 2026-11-11 定案）
// 背景：编辑模式允许把任何单位放到任何格子（「山上的重坦」这类战役设计依赖非法放置）。
// 但「非法放置后单位怎么办」不该由系统硬编码，而是由单位自己 Export 选择：
//   模式0 自动检查（Warside）：脚下非法 → 回合开始走标准死亡管线自毁（尸化/野化拦截生效）；编辑模式拒绝放置
//   模式1 宽容（默认）：脚下非法无所谓，照常操作；只是不能「进入」其它非法格（移动消耗≥999 的既有规则不变）
// 非法判定：按本单位移动类型进入脚下这格的消耗 ≥999（港口等特例由 GetMoveCostOnGrid 处理）
// ============================================================

public partial class Infantry
{
    [ExportGroup("非法地形存在策略")]
    [Export(PropertyHint.Enum, "自动检查(Warside):非法地形自毁,宽容:停留可操作")]
    public int illegalTerrainBehavior = 1;   // 0=自动检查自毁 1=宽容停留（默认宽容：非法地形好好待着，照常操作）

    /// <summary>脚下是否为非法地形（按本单位移动类型的进入消耗判定，≥999=不可通行=非法）。</summary>
    public bool IsOnIllegalTerrain()
    {
        if (grid == null) return false;
        // ✅ issue#1 二期：多格单位严格非法地形检查 + 剩余n格安全阈值
        if (isMultiTileUnit && illegalTerrainStrictMultiCell && footprintGrids != null && footprintGrids.Count > 0)
        {
            int legal = 0;
            foreach (var g in footprintGrids)
            {
                if (g == null) continue;
                if (GetMoveCostOnGrid(g) >= 999)
                {
                    if (illegalTerrainMinSafeCells <= 0) return true;   // 阈值关闭=任一非法即非法
                }
                else legal++;
            }
            return illegalTerrainMinSafeCells > 0 && legal < illegalTerrainMinSafeCells;
        }
        return GetMoveCostOnGrid(grid) >= 999;
    }

    /// <summary>
    /// 回合开始自动检查（仅模式0生效）。返回 true=本回合已自毁。
    /// 自毁走标准死亡管线：TryFlee 内尸化/亡语拦截与野化逃逸全部生效。
    /// </summary>
    public bool ApplyIllegalTerrainAutoCheck()
    {
        if (illegalTerrainBehavior != 0) return false;
        if (!IsOnIllegalTerrain()) return false;
        health = 0;
        UpdateHpLabel();
        if (!TryFlee())
        {
            gameManager?.RemovePiece(this);
            CallDeferred(Node.MethodName.QueueFree);
        }
        return true;
    }
}
