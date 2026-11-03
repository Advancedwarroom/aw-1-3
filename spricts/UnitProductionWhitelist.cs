// UnitProductionWhitelist.cs - 全局"可生成单位白名单"
// 规格：仅限制"是否更新"填"是"的建筑的【生产】！
//   - 建筑isUpdatable=true  → 生产受白名单限制（单位必须在白名单中）
//   - 建筑isUpdatable=false → 不受限制
//   - 编辑模式放置、单位使用均不受影响
// Lab被占领后，其白名单单位加入/移出全局集合（支持分层占领解锁）。
using Godot;
using System.Collections.Generic;

public static class UnitProductionWhitelist
{
    /// <summary>已解锁的可生成单位（Lab占领后加入）</summary>
    public static HashSet<string> UnlockedUnits = new HashSet<string>();

    /// <summary>需实验室解锁的圈定单位（开创人圈定，见下；常规单位不在此列=默认可产）</summary>
    public static HashSet<string> LabLockedUnits = new HashSet<string> { "Neotank", "Rig", "WarTank", "ProducibleBoat", "PipeRunner", "FlyBomb", "Oozium", "RepairBoat" };   // 开创人2026-11-03圈定8单位（黑炸弹=FlyBomb/黑船=RepairBoat为中性化名）；隐形战机/航母/巨型坦克=盟军造物，默认可产

    /// <summary>清空（新对局重置）</summary>
    public static void Reset()
    {
        UnlockedUnits.Clear();
    }

    /// <summary>判断单位是否在全局白名单中（是否已解锁）</summary>
    public static bool CanProduce(string unitName)
    {
        if (string.IsNullOrEmpty(unitName)) return true;
        if (!LabLockedUnits.Contains(unitName)) return true;   // 常规单位默认可产，与实验室解绑
        return UnlockedUnits.Contains(unitName);               // 圈定单位需占领实验室解锁
    }

    /// <summary>解锁单位（Lab占领时调用）</summary>
    public static void UnlockUnit(string unitName)
    {
        if (!string.IsNullOrEmpty(unitName))
            UnlockedUnits.Add(unitName);
    }

    /// <summary>解锁一批单位（Lab占领时调用）</summary>
    public static void UnlockUnits(IEnumerable<string> unitNames)
    {
        foreach (var n in unitNames)
            UnlockUnit(n);
    }

    /// <summary>锁定（移除）单位（Lab占领时调用，支持减成）</summary>
    public static void LockUnit(string unitName)
    {
        if (!string.IsNullOrEmpty(unitName))
            UnlockedUnits.Remove(unitName);
    }

    /// <summary>锁定（移除）一批单位</summary>
    public static void LockUnits(IEnumerable<string> unitNames)
    {
        foreach (var n in unitNames)
            LockUnit(n);
    }
}
