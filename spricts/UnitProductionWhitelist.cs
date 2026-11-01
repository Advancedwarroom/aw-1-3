// UnitProductionWhitelist.cs - 全局"可生成单位白名单"
// 规格：仅限制"是否更新"填"是"的单位的【生产】！不限制编辑模式放置。
// Lab被占领后，其白名单单位加入全局解锁集合。
using Godot;
using System.Collections.Generic;

public static class UnitProductionWhitelist
{
    /// <summary>已解锁的可生成单位（Lab占领后加入）</summary>
    public static HashSet<string> UnlockedUnits = new HashSet<string>();

    /// <summary>清空（新对局重置）</summary>
    public static void Reset()
    {
        UnlockedUnits.Clear();
    }

    /// <summary>
    /// 判断单位是否可生产。
    /// isUpdatable=false（是否更新=否）→ 与白名单无关，直接可生产。
    /// isUpdatable=true（是否更新=是）→ 必须在全局白名单（已被Lab解锁）。
    /// </summary>
    public static bool CanProduce(string unitName, bool isUpdatable)
    {
        if (!isUpdatable) return true;
        return UnlockedUnits.Contains(unitName);
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

    // ========== 单位 isUpdatable 缓存 ==========
    private static Dictionary<string, bool> updatableCache = new Dictionary<string, bool>();

    /// <summary>获取单位类型的 isUpdatable（是否更新）标志（带缓存，从场景读取）</summary>
    public static bool GetIsUpdatable(string unitName)
    {
        if (string.IsNullOrEmpty(unitName)) return false;
        if (updatableCache.TryGetValue(unitName, out bool cached)) return cached;
        bool flag = false;
        try
        {
            var info = UnitProductionDatabase.GetInfo(unitName);
            if (!string.IsNullOrEmpty(info.ScenePath))
            {
                var scene = GD.Load<PackedScene>(info.ScenePath);
                if (scene != null)
                {
                    var inst = scene.Instantiate() as Infantry;
                    if (inst != null)
                    {
                        flag = inst.isUpdatable;
                        inst.QueueFree();
                    }
                }
            }
        }
        catch { flag = false; }
        updatableCache[unitName] = flag;
        return flag;
    }

    /// <summary>✅ 生产检查入口：结合单位的 isUpdatable 判断是否可生产</summary>
    public static bool CanProduceUnit(string unitName)
    {
        return CanProduce(unitName, GetIsUpdatable(unitName));
    }
}
