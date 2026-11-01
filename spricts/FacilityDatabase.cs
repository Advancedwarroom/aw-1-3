// FacilityDatabase.cs - 建筑场景数据库（建造模块/存档通用）
using Godot;
using System.Collections.Generic;

/// <summary>
/// 建筑类型 → 场景路径 的统一登记表。
/// 建造模块的白名单、地图导入导出、内部建造的产出都从这里解析场景。
/// </summary>
public static class FacilityDatabase
{
    private static readonly Dictionary<string, string> scenePaths = new()
    {
        ["City"]   = "res://Prefabs/City.tscn",
        ["Base"]   = "res://Prefabs/Base.tscn",
        ["AirPort"] = "res://Prefabs/AirPort.tscn",
        ["Port"]   = "res://Prefabs/Port.tscn",
        // DoR Rig 搭建的临时设施：可维修/补给，不能生产、无收入
        ["TempAirPort"] = "res://Prefabs/TempAirPort.tscn",
        ["TempPort"]    = "res://Prefabs/TempPort.tscn",
        // ✅ 中转口：Rig 在原管道接缝（管道废墟）上建造的供能中继站，接管超级兵工厂
        ["RelayJunction"] = "res://Prefabs/RelayJunction.tscn",
        // ✅ 总部：占领敌方HQ即获胜（改变team则Lose）
        ["HQ"] = "res://Prefabs/HQ.tscn",
        // ✅ 雷达：占领后去雾（可配范围/自动化/强视野/强侦查/照明塔）
        ["Radar"] = "res://Prefabs/Radar.tscn",
        // ✅ 通信塔：占领后给己方单位加成/减成
        ["ComTower"] = "res://Prefabs/ComTower.tscn",
        // ✅ 秘密实验室：占领后解锁单位生产白名单
        ["Lab"] = "res://Prefabs/Lab.tscn",
    };

    // ✅ 建筑地形要求（未登记=任意地形）：DoR 原作——临时机场只能建在平原，临时港口建在海滩
    private static readonly Dictionary<string, GridType[]> allowedTerrains = new()
    {
        ["TempAirPort"] = new[] { GridType.GROUND },
        ["TempPort"]    = new[] { GridType.BEACH },
        // ✅ 中转口只能建在管道废墟（原管道接缝位置）
        ["RelayJunction"] = new[] { GridType.BROKENPIPE },
    };

    public static bool IsTerrainAllowed(string facilityType, GridType terrain)
    {
        if (string.IsNullOrEmpty(facilityType) || !allowedTerrains.TryGetValue(facilityType, out var list)) return true;
        return System.Array.IndexOf(list, terrain) >= 0;
    }

    /// <summary>按类型名取场景路径（未登记返回 null）</summary>
    public static string GetScenePath(string facilityType)
    {
        if (string.IsNullOrEmpty(facilityType)) return null;
        return scenePaths.GetValueOrDefault(facilityType);
    }

    /// <summary>全部已登记建筑类型（建造白名单选项）</summary>
    public static IEnumerable<string> AllTypes => scenePaths.Keys;
}
