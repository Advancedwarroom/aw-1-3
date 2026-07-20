using Godot;
using System.Collections.Generic;

public static class UnitProductionDatabase
{
    public readonly struct UnitInfo
    {
        public readonly string ScenePath;
        public readonly int Cost;
        public readonly string DisplayName;
        public UnitInfo(string scenePath, int cost, string displayName)
        {
            ScenePath = scenePath;
            Cost = cost;
            DisplayName = displayName;
        }
    }

    public static readonly Dictionary<string, UnitInfo> Units = new()
    {
        ["Infantry"]   = new("res://Prefabs/infantry(1).tscn", 1000,  "步兵"),
        ["Mech"]       = new("res://Prefabs/mech.tscn",         3000,  "机甲"),
        ["Bike"]       = new("res://Prefabs/Bike.tscn",         2500,  "摩托兵"),
        ["Oozium"]     = new("res://Prefabs/oozium.tscn",       20000, "史莱姆"),
        ["LightTank"]  = new("res://Prefabs/light_tank.tscn",   7000,  "轻型坦克"),
        ["MdTank"]     = new("res://Prefabs/Md_Tank.tscn",      16000, "重型坦克"),
        ["Rocket"]     = new("res://Prefabs/Rocket.tscn",       15000, "火箭炮"),
        ["Artillery"]  = new("res://Prefabs/Artillery.tscn",    6000,  "自行火炮"),
        ["APC"]        = new("res://Prefabs/apc.tscn",          5000,  "运输车"),
        ["AntiAir"]    = new("res://Prefabs/AntiAir.tscn",      8000,  "防空高炮"),
        ["Recon"]      = new("res://Prefabs/Recon.tscn",        4000,  "侦察车"),
        ["AntiTank"]   = new("res://Prefabs/Anti_Tank.tscn",    11000, "反坦克炮"),
        ["Flare"]      = new("res://Prefabs/Flare.tscn",        5000,  "照明车"),
        ["FlyBomb"]    = new("res://Prefabs/FlyBomb.tscn",      25000, "飞弹"),
    };

    public static bool HasUnit(string unitName) => Units.ContainsKey(unitName);

    public static UnitInfo GetInfo(string unitName)
    {
        if (Units.TryGetValue(unitName, out var info))
            return info;
        return new UnitInfo("", 0, unitName);
    }
}
