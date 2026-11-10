// WeaponBuildDatabase.cs - 兵器建造注册表：建造白名单 "Weapon:模板键" → 场景路径
// 与兵器编辑器 23 模板同源；键 = res://Prefabs/*.tscn 文件名去扩展名（全 ASCII 稳定键名）。
using System.Collections.Generic;

public static class WeaponBuildDatabase
{
    private static readonly Dictionary<string, string> Table = new()
    {
        ["black_cannon"] = "res://Prefabs/black_cannon.tscn",
        ["Laser"] = "res://Prefabs/Laser.tscn",
        ["crystal"] = "res://Prefabs/crystal.tscn",
        ["large_cannon"] = "res://Prefabs/large_cannon.tscn",
        ["death_ray"] = "res://Prefabs/death_ray.tscn",
        ["black_obelisk"] = "res://Prefabs/black_obelisk.tscn",
        ["missile_silo"] = "res://Prefabs/missile_silo.tscn",
        ["PipeSeam"] = "res://Prefabs/PipeSeam.tscn",
        ["SuperFactory"] = "res://Prefabs/SuperFactory.tscn",
        ["Gate"] = "res://Prefabs/Gate.tscn",
        ["BlackArc"] = "res://Prefabs/BlackArc.tscn",
        ["GhostCannon"] = "res://Prefabs/GhostCannon.tscn",
        ["GrandBoltPart"] = "res://Prefabs/GrandBoltPart.tscn",
        ["TheNest"] = "res://Prefabs/TheNest.tscn",
        ["NestCenter"] = "res://Prefabs/NestCenter.tscn",
        ["NestCannonFixed"] = "res://Prefabs/NestCannonFixed.tscn",
        ["NestCannonRotating"] = "res://Prefabs/NestCannonRotating.tscn",
        ["NestMortar"] = "res://Prefabs/NestMortar.tscn",
        ["GrandBolt"] = "res://Prefabs/GrandBolt.tscn",
        ["GrandBoltModuleA"] = "res://Prefabs/GrandBoltModuleA.tscn",
        ["GrandBoltModuleB"] = "res://Prefabs/GrandBoltModuleB.tscn",
        ["GrandBoltModuleC"] = "res://Prefabs/GrandBoltModuleC.tscn",
        ["GrandBoltModuleCore"] = "res://Prefabs/GrandBoltModuleCore.tscn",
    };

    public static string GetScenePath(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        return Table.TryGetValue(key.Trim(), out var p) ? p : null;
    }

    public static bool ContainsKey(string key) => GetScenePath(key) != null;

    /// 全部模板键（建造 UI 显示/校验用）
    public static IEnumerable<string> Keys => Table.Keys;
}
