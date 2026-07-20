// VisionConfig.cs - 重构版：支持单位类型×地形的独立视野加成矩阵
// 核心变更：
// 1. 保留基础视野默认值表
// 2. 新增单位类型×地形的视野加成矩阵（每个单位在不同地形上视野加成不同）
// 3. 每个单位/兵器实例可通过 Inspector 覆盖默认矩阵
using Godot;
using System.Collections.Generic;

public static class VisionConfig
{
    // ========== 单位类型基础视野配置（默认射程）==========
    private static Dictionary<string, int> unitVisionRange = new()
    {
        ["Infantry"] = 2, ["Mech"] = 2, ["LightTank"] = 3,
        ["MdTank"] = 3, ["Artillery"] = 2, ["Rocket"] = 2,
        ["APC"] = 3, ["Oozium"] = 2, ["AntiAir"] = 5,
        ["Recon"] = 5, ["FlyBomb"] = 1, ["AntiTank"] = 2,
        ["Flare"] = 2, ["Bike"] = 2,["PipeRunner"] = 4,
    };

    // ========== 单位类型额外视野加成（全局）==========
    private static Dictionary<string, int> unitTypeVisionBonus = new()
    {
        ["AntiAir"] = 0,
        ["Recon"] = 0,
    };

    // ========== 兵器类型基础视野配置 ==========
    private static Dictionary<string, int> weaponVisionRange = new()
    {
        ["BlackCannon"] = 2,
        ["Laser"] = 1,
    };

    // ========== 兵器视野模式配置 ==========
    private static Dictionary<string, bool> weaponIndependentMode = new()
    {
        ["BlackCannon"] = true,
        ["Laser"] = true,
    };

    // ========== ✅ 核心新增：单位类型 × 地形 的视野加成矩阵 ==========
    // 每个单位类型在不同地形上的视野加成（可正可负）
    // 如果某单位类型没有某地形条目，则使用全局默认加成
    private static Dictionary<string, Dictionary<GridType, int>> unitTerrainVisionBonus = new()
    {
        // 步兵：森林视野差，山地视野好
        ["Infantry"] = new()
        {
            [GridType.FOREST] = -1, [GridType.HILL] = 1,
            [GridType.GROUND] = 0,
            [GridType.SEAFOG] = -3, [GridType.LANDFOG] = -3,
            [GridType.CLIFF] = 1, [GridType.CAVE] = -2,
        },
        // 机甲：和步兵类似但森林更好
        ["Mech"] = new()
        {
            [GridType.FOREST] = 0, [GridType.HILL] = 2,
            [GridType.GROUND] = 0,
            [GridType.SEAFOG] = -2, [GridType.LANDFOG] = -2,
        },
        // 轻坦：开阔地好，森林差
        ["LightTank"] = new()
        {

        },
        // 重坦：和轻坦类似但城市更好
        ["MdTank"] = new()
        {

        },
        // 火炮：山地视野极好（制高点）
        ["Artillery"] = new()
        {

        },
        // 火箭炮：和火炮类似
        ["Rocket"] = new()
        {

        },
        // 运输车：普通
        ["APC"] = new()
        {

        },
        // 史莱姆：不受地形影响（特殊）
        ["Oozium"] = new()
        {

        },
        // 防空高炮：开阔地极好
        ["AntiAir"] = new()
        {

        },
        // 侦察车：全地形优秀
        ["Recon"] = new()
        {
            [GridType.GROUND] = 0, [GridType.ROAD] = 0,
            [GridType.HILL] = 0, [GridType.FOREST] = 0,
            [GridType.SEAFOG] = 0,
            [GridType.LANDFOG] = 0,
        },
        // 飞弹：高空飞行，不受地形影响
        ["FlyBomb"] = new()
        {
            [GridType.GROUND] = 0, [GridType.ROAD] = 0,
            [GridType.HILL] = 0, [GridType.FOREST] = 0,
            [GridType.SEA] = 0,
            [GridType.SEAFOG] = 0, [GridType.LANDFOG] = 0,
            [GridType.LAVA] = 0, [GridType.LAVAFOG] = 0,
        },
        // 反坦克炮：普通地面视野
        ["AntiTank"] = new()
        {

        },
        // 照明炮
        ["Flare"] = new()
        {

        },
        // 摩托兵
        ["Bike"] = new()
        {

        },
    };

    // ========== ✅ 兵器类型 × 地形 的视野加成矩阵 ==========
    private static Dictionary<string, Dictionary<GridType, int>> weaponTerrainVisionBonus = new()
    {
        ["BlackCannon"] = new()
        {
            [GridType.GROUND] = 0, [GridType.HILL] = 1,
            [GridType.OVERPASS] = 2,
        },
        ["Laser"] = new()
        {
            [GridType.GROUND] = 0, [GridType.HILL] = 1,
            [GridType.OVERPASS] = 1,
        },
    };

    // ========== 全局默认地形视野加成（当单位类型没有特定条目时回退）==========
    private static Dictionary<GridType, int> defaultTerrainVisionBonus = new()
    {
        [GridType.GROUND] = 0, [GridType.FOREST] = -1,
        [GridType.ROAD] = 0,
        [GridType.SEA] = 0, [GridType.RIVER] = 0,
        [GridType.HILL] = 2, [GridType.METEORITE] = 0,
        [GridType.PIPE] = 0, [GridType.LAVA] = -1,
        [GridType.BEACH] = 0, [GridType.TP] = 0,
        [GridType.REEF] = -1, [GridType.WHIRLPOOL] = -2,
        [GridType.LAVASIDE] = -1, [GridType.SEAFOG] = -3,
        [GridType.LANDFOG] = -3, [GridType.WATERFALL] = 0,
        [GridType.CLIFF] = 1, [GridType.SLOPE] = 0,
        [GridType.CAVE] = -2, [GridType.HOLE] = -1,
        [GridType.PIPESEAM] = 0, [GridType.TRACK] = 0,
        [GridType.STATION] = 1, [GridType.BRIDGE] = 0,
        [GridType.LAVABRIDGE] = 0, [GridType.PASSABLEPIPE] = 0,
        [GridType.SHIPGATE] = 0, [GridType.OVERPASS] = 1,
        [GridType.BROKENPIPE] = 0, [GridType.RUINS] = -1,
        [GridType.BROKENTRACK] = 0, [GridType.LAVAFOG] = -4,
    };

    // ========== 公共API：基础视野 ==========
    public static int GetUnitVisionRange(string unitTypeName)
    {
        if (unitVisionRange.TryGetValue(unitTypeName, out int range))
            return range;
        return 2;
    }

    public static void SetUnitVisionRange(string unitTypeName, int range)
    {
        unitVisionRange[unitTypeName] = Mathf.Max(0, range);
    }

    // ========== 单位类型额外视野加成（保留兼容）==========
    public static int GetUnitTypeVisionBonus(string unitTypeName)
    {
        if (unitTypeVisionBonus.TryGetValue(unitTypeName, out int bonus))
            return bonus;
        return 0;
    }

    public static void SetUnitTypeVisionBonus(string unitTypeName, int bonus)
    {
        unitTypeVisionBonus[unitTypeName] = bonus;
    }

    // ========== 兵器类型×地形 视野加成（新增）==========
    public static int GetWeaponTerrainVisionBonus(string weaponTypeName, GridType gridType)
    {
        if (weaponTerrainVisionBonus.TryGetValue(weaponTypeName, out var terrainDict))
        {
            if (terrainDict.TryGetValue(gridType, out int bonus))
                return bonus;
        }
        if (defaultTerrainVisionBonus.TryGetValue(gridType, out int defaultBonus))
            return defaultBonus;
        return 0;
    }

    public static void SetWeaponTerrainBonus(string weaponTypeName, GridType gridType, int bonus)
    {
        if (!weaponTerrainVisionBonus.ContainsKey(weaponTypeName))
            weaponTerrainVisionBonus[weaponTypeName] = new Dictionary<GridType, int>();
        weaponTerrainVisionBonus[weaponTypeName][gridType] = bonus;
    }

    public static int GetWeaponVisionRange(string weaponTypeName)
    {
        if (weaponVisionRange.TryGetValue(weaponTypeName, out int range))
            return range;
        return 2;
    }

    public static void SetWeaponVisionRange(string weaponTypeName, int range)
    {
        weaponVisionRange[weaponTypeName] = Mathf.Max(0, range);
    }

    public static bool IsWeaponIndependentMode(string weaponTypeName)
    {
        if (weaponIndependentMode.TryGetValue(weaponTypeName, out bool mode))
            return mode;
        return false;
    }

    public static void SetWeaponIndependentMode(string weaponTypeName, bool independent)
    {
        weaponIndependentMode[weaponTypeName] = independent;
    }

    // ========== ✅ 核心新增：获取单位类型×地形的视野加成 ==========
    public static int GetUnitTerrainVisionBonus(string unitTypeName, GridType gridType)
    {
        // 1. 先查单位类型的专属矩阵
        if (unitTerrainVisionBonus.TryGetValue(unitTypeName, out var terrainDict))
        {
            if (terrainDict.TryGetValue(gridType, out int bonus))
                return bonus;
        }
        // 2. 回退到全局默认
        if (defaultTerrainVisionBonus.TryGetValue(gridType, out int defaultBonus))
            return defaultBonus;
        return 0;
    }



    public static int GetDefaultTerrainVisionBonus(GridType gridType)
    {
        if (defaultTerrainVisionBonus.TryGetValue(gridType, out int bonus))
            return bonus;
        return 0;
    }
public static void SetTerrainVisionBonus(GridType gridType, int bonus)
    {
        defaultTerrainVisionBonus[gridType] = bonus;
    }

    public static void SetUnitTerrainBonus(string unitTypeName, GridType gridType, int bonus)
    {
        if (!unitTerrainVisionBonus.ContainsKey(unitTypeName))
            unitTerrainVisionBonus[unitTypeName] = new Dictionary<GridType, int>();
        unitTerrainVisionBonus[unitTypeName][gridType] = bonus;
    }

    // ========== ✅ 获取某单位类型的完整地形加成表（用于编辑器显示）==========
    public static Dictionary<GridType, int> GetUnitTerrainBonusTable(string unitTypeName)
    {
        var result = new Dictionary<GridType, int>();
        // 先填入全局默认值
        foreach (var kvp in defaultTerrainVisionBonus)
            result[kvp.Key] = kvp.Value;
        // 再覆盖单位专属值
        if (unitTerrainVisionBonus.TryGetValue(unitTypeName, out var terrainDict))
        {
            foreach (var kvp in terrainDict)
                result[kvp.Key] = kvp.Value;
        }
        return result;
    }

    // ========== 获取所有配置（用于全局配置面板）==========
    public static Dictionary<string, int> GetAllUnitVisionConfigs()
    {
        return new Dictionary<string, int>(unitVisionRange);
    }

    public static Dictionary<string, int> GetAllWeaponVisionConfigs()
    {
        return new Dictionary<string, int>(weaponVisionRange);
    }

    public static Dictionary<GridType, int> GetAllTerrainVisionConfigs()
    {
        return new Dictionary<GridType, int>(defaultTerrainVisionBonus);
    }

    // ========== ✅ 获取所有单位类型的地形加成矩阵（用于编辑器）==========
    public static Dictionary<string, Dictionary<GridType, int>> GetAllUnitTerrainBonus()
    {
        var result = new Dictionary<string, Dictionary<GridType, int>>();
        foreach (var kvp in unitTerrainVisionBonus)
        {
            result[kvp.Key] = new Dictionary<GridType, int>(kvp.Value);
        }
        return result;
    }

    public static void ResetToDefaults()
    {
        unitVisionRange = new()
        {
            ["Infantry"] = 2, ["Mech"] = 2, ["LightTank"] = 3,
            ["MdTank"] = 1, ["Artillery"] = 1, ["Rocket"] = 1,
            ["APC"] = 1, ["Oozium"] = 2, ["AntiAir"] = 2,
            ["Recon"] = 5,
            ["Flare"] = 2, ["Bike"] = 2,["PipeRunner"] = 4,
        };

        weaponVisionRange = new()
        {
            ["BlackCannon"] = 2, ["Laser"] = 1,
        };

        weaponIndependentMode = new()
        {
            ["BlackCannon"] = true, ["Laser"] = true,
        };

        defaultTerrainVisionBonus = new()
        {
            [GridType.GROUND] = 0, [GridType.FOREST] = -1,
            [GridType.HILL] = 2, [GridType.LAVA] = -1, [GridType.WHIRLPOOL] = -2,
            [GridType.SEAFOG] = -3, [GridType.LANDFOG] = -3, [GridType.CLIFF] = 1,
            [GridType.CAVE] = -2, [GridType.OVERPASS] = 1, [GridType.LAVAFOG] = -4,
        };

        // 重置单位专属矩阵
        unitTerrainVisionBonus = new()
        {
            ["Infantry"] = new()
            {
                [GridType.FOREST] = 0, [GridType.HILL] = 1,
                [GridType.GROUND] = 0,
                [GridType.SEAFOG] = 0, [GridType.LANDFOG] = 0,
                [GridType.CLIFF] = 0, [GridType.CAVE] = 0,
            },
            ["Mech"] = new()
            {
                [GridType.FOREST] = 0, [GridType.HILL] = 1,
                [GridType.GROUND] = 0,
                [GridType.SEAFOG] = 0, [GridType.LANDFOG] = 0,
            },
            ["LightTank"] = new()
            {
                [GridType.FOREST] = 0, [GridType.HILL] = 0,
                [GridType.GROUND] = 0,
                [GridType.ROAD] = 0, [GridType.SEAFOG] = 0,
            },
            ["MdTank"] = new()
            {
                [GridType.FOREST] = 0, [GridType.HILL] = 0,
                [GridType.GROUND] = 0,
                [GridType.ROAD] = 0, [GridType.SEAFOG] = 0,
            },
            ["Artillery"] = new()
            {
                [GridType.HILL] = 0, [GridType.FOREST] = 0,
                [GridType.GROUND] = 0,
                [GridType.CLIFF] = 0, [GridType.OVERPASS] = 0,
            },
            ["Rocket"] = new()
            {
                [GridType.HILL] = 0, [GridType.FOREST] = 0,
                [GridType.GROUND] = 0,
                [GridType.CLIFF] = 0, [GridType.OVERPASS] = 0,
            },
            ["APC"] = new()
            {
                [GridType.GROUND] = 0, [GridType.ROAD] = 0,
                [GridType.FOREST] = 0, [GridType.HILL] = 0,
            },
            ["Oozium"] = new()
            {
                [GridType.GROUND] = 0, [GridType.FOREST] = 0,
                [GridType.HILL] = 0,
                [GridType.SEAFOG] = 0, [GridType.LANDFOG] = 0,
                [GridType.LAVA] = 0, [GridType.LAVAFOG] = 0,
            },
            ["AntiAir"] = new()
            {
                [GridType.GROUND] = 0, [GridType.HILL] = 0,
                [GridType.FOREST] = 0,
                [GridType.OVERPASS] = 0, [GridType.CLIFF] = 0,
            },
            ["Recon"] = new()
            {
                [GridType.GROUND] = 0, [GridType.ROAD] = 0,
                [GridType.HILL] = 0, [GridType.FOREST] = 0,
                [GridType.SEAFOG] = 0,
                [GridType.LANDFOG] = 0,
            },
            ["Flare"] = new()
            {

            },
            ["Bike"] = new()
            {

            },
            ["PipeRunner"] = new()
{
},
        };
    }

    public static string ExportConfig()
    {
        string result = "=== 视野配置表 ===\n\n";

        result += "[单位基础视野]\n";
        foreach (var kvp in unitVisionRange)
            result += $"{kvp.Key}: {kvp.Value}\n";

        result += "\n[兵器基础视野]\n";
        foreach (var kvp in weaponVisionRange)
        {
            string mode = weaponIndependentMode.GetValueOrDefault(kvp.Key, false) ? "[独立]" : "[正常]";
            result += $"{kvp.Key}: {kvp.Value} {mode}\n";
        }

        result += "\n[全局默认地形视野加成]\n";
        foreach (var kvp in defaultTerrainVisionBonus)
        {
            string sign = kvp.Value > 0 ? "+" : "";
            result += $"{kvp.Key}: {sign}{kvp.Value}\n";
        }

        result += "\n[单位类型×地形 专属加成矩阵]\n";
        foreach (var unitKvp in unitTerrainVisionBonus)
        {
            result += $"\n--- {unitKvp.Key} ---\n";
            foreach (var terrainKvp in unitKvp.Value)
            {
                string sign = terrainKvp.Value > 0 ? "+" : "";
                result += $"  {terrainKvp.Key}: {sign}{terrainKvp.Value}\n";
            }
        }

        return result;
    }
}
