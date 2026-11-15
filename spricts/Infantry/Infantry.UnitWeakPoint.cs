// Infantry.UnitWeakPoint.cs
// ✅ 开创人2026-11-15: 多格单位受击弱点格——完整移植兵器 IsWeakPointGrid 机制（LargeCannon/DeathRay/GrandBolt 同款三调用点构型）。
// 总开关关闭=全部足印格可打（没有打不了的地方）；开启=仅弱点格可被选择为目标，弱点随朝向旋转。
// 差异表：每格受击倍率（1=全伤/0=免疫/>1=加伤；留空=全1；越界回退首项）。
// 打格效果差异（精度崩/哑火）随准备冷却模块同批落地，本文件不造空实现。
using Godot;
using System;
using System.Collections.Generic;

public partial class Infantry
{
    [ExportGroup("受击弱点格（多格单位）")]
    [Export] public bool unitWeakPointEnabled = false;                    // 总开关：false=全足印格可打
    [Export] public int[] unitWeakPointCellIndices = new int[] { };       // 弱点格足印序号（0=锚点格恒可打；1~n同attackAnchorCells序号语义）
    [Export] public bool unitWeakPointRotatesWithFacing = true;           // 弱点随朝向旋转
    [Export] public float[] weakCellDamageFactors = new float[] { };      // 每格受击倍率（1=全伤，0=免疫，>1=加伤；留空=全1）

    // 可被打的目标格：开关关=全足印格；开=锚点格+弱点格（随朝向旋转）。单格单位=自身格。
    public List<Grids> GetUnitHittableGrids()
    {
        var result = new List<Grids>();
        if (grid == null) return result;
        if (!isMultiTileUnit) { result.Add(grid); return result; }
        var offs = GetFootprintIndices(grid.GridIndex, unitFacing);
        if (!unitWeakPointEnabled)
        {
            foreach (var off in offs)
            {
                var g = GetGridByIndexSafe(off);
                if (g != null) result.Add(g);
            }
            return result;
        }
        var picked = new List<int> { 0 };
        if (unitWeakPointCellIndices != null)
        {
            foreach (var idx in unitWeakPointCellIndices)
                if (!picked.Contains(idx)) picked.Add(idx);
        }
        foreach (var idx in picked)
        {
            if (idx >= 0 && idx < offs.Count)
            {
                var g = GetGridByIndexSafe(offs[idx]);
                if (g != null) result.Add(g);
            }
        }
        return result;
    }

    public bool IsUnitHittableGrid(Grids g)
    {
        if (g == null) return false;
        if (!isMultiTileUnit) return g == grid;
        if (!unitWeakPointEnabled) return true;   // ✅ 全打有效：没有打不了的地方
        return GetUnitHittableGrids().Contains(g);
    }

    // 目标格→足印序号（差异表查用）；非本足印格返回-1
    public int GetUnitCellIndexOf(Grids g)
    {
        if (!isMultiTileUnit || grid == null || g == null) return -1;
        var offs = GetFootprintIndices(grid.GridIndex, unitFacing);
        for (int i = 0; i < offs.Count; i++)
            if (offs[i] == g.GridIndex) return i;
        return -1;
    }

    // 受击倍率：开关关/未配置/非本足印格=1（全伤）
    public float GetWeakCellDamageFactor(Grids g)
    {
        if (!unitWeakPointEnabled || weakCellDamageFactors == null || weakCellDamageFactors.Length == 0) return 1f;
        int idx = GetUnitCellIndexOf(g);
        if (idx < 0) return 1f;
        if (idx < weakCellDamageFactors.Length) return weakCellDamageFactors[idx];
        return weakCellDamageFactors[0];
    }

    private Grids GetGridByIndexSafe(Vector2I idx)
    {
        try
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            var grids = gm?.gridManager?.grids;
            if (grids == null) return null;
            if (idx.X < 0 || idx.Y < 0 || idx.X >= grids.GetLength(0) || idx.Y >= grids.GetLength(1)) return null;
            return grids[idx.X, idx.Y];
        }
        catch { return null; }
    }
}
