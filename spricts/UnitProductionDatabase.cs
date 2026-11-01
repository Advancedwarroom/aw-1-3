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

    /// <summary>全部可生产兵种（内置 + res://Prefabs/CustomUnits/ 下自动注册的自定义兵种）</summary>
    public static Dictionary<string, UnitInfo> Units { get { EnsureBuiltinCosts(); EnsureCustomUnits(); return builtinUnits; } }

    // ✅ 内置兵种造价读检查器：下表里的数字只是兜底（场景加载失败 / cost≤0 时才用）
    //    真正的造价 = 各兵种 .tscn 根节点检查器里的 cost（勾不勾 useDefaultConfig 都一样）
    private static bool builtinCostsLoaded = false;
    private static void EnsureBuiltinCosts()
    {
        if (builtinCostsLoaded) return;
        builtinCostsLoaded = true;
        foreach (var id in new List<string>(builtinUnits.Keys))
        {
            var info = builtinUnits[id];
            var node = GD.Load<PackedScene>(info.ScenePath)?.Instantiate();
            if (node is not Infantry u) { node?.Free(); GD.PushWarning($"[生产] {info.ScenePath} 无法读取检查器造价，沿用兜底 {info.Cost}"); continue; }
            int cost = ResolveCost(u, info.Cost);
            if (cost != info.Cost) GD.Print($"[生产] {id} 造价按检查器 {cost}（兜底表 {info.Cost}）");
            builtinUnits[id] = new UnitInfo(info.ScenePath, cost, info.DisplayName);
            u.Free();
        }
    }

    /// <summary>单位的生产造价 = 检查器 cost（勾选 useDefaultConfig 也不再被 UnitRegistry 覆盖，与单位自身取值一致）</summary>
    private static int ResolveCost(Infantry u, int fallback) => u.cost > 0 ? u.cost : fallback;

    // ✅ 不写代码造新兵种：把单位场景放进此目录并填写 unitTypeId，即自动加入生产数据库
    //    工厂/机场/港口的 producibleUnitNames 里填这个 unitTypeId 就能生产
    public const string CustomUnitDir = "res://Prefabs/CustomUnits/";
    private static bool customLoaded = false;

    private static void EnsureCustomUnits()
    {
        if (customLoaded) return;
        customLoaded = true;
        using var dir = DirAccess.Open(CustomUnitDir);
        if (dir == null) return;
        foreach (var f in dir.GetFiles())
        {
            string fn = f.EndsWith(".remap") ? f[..^6] : f;   // 导出包里场景带 .remap 后缀
            if (!fn.EndsWith(".tscn") && !fn.EndsWith(".scn")) continue;
            string path = CustomUnitDir + fn;
            var node = GD.Load<PackedScene>(path)?.Instantiate();
            if (node is not Infantry u) { node?.Free(); GD.PushWarning($"[自定义兵种] {path} 根节点不是单位脚本，已跳过"); continue; }
            string id = u.unitTypeId;
            if (string.IsNullOrEmpty(id)) { GD.PushWarning($"[自定义兵种] {path} 未填写 unitTypeId，已跳过"); u.Free(); continue; }
            if (builtinUnits.ContainsKey(id)) { GD.PushWarning($"[自定义兵种] {path} 的 unitTypeId \"{id}\" 与已有兵种重名，已跳过"); u.Free(); continue; }
            int cost = ResolveCost(u, UnitRegistry.Get(u.TemplateId)?.Cost ?? 0);
            builtinUnits[id] = new UnitInfo(path, cost, string.IsNullOrEmpty(u.unitDisplayName) ? id : u.unitDisplayName);
            GD.Print($"[自定义兵种] 注册 {id}（{builtinUnits[id].DisplayName}） 造价{cost} ← {path}");
            u.Free();
        }
    }

    private static readonly Dictionary<string, UnitInfo> builtinUnits = new()
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
        ["Fighter"]    = new("res://Prefabs/Fighter.tscn",       20000, "战斗机"),
        ["Bomber"]     = new("res://Prefabs/Bomber.tscn",        22000, "轰炸机"),
        ["BCopter"]    = new("res://Prefabs/BCopter.tscn",        9000, "武装直升机"),
        ["TCopter"]    = new("res://Prefabs/TCopter.tscn",        5000, "运输直升机"),
        ["AntiAirMissile"] = new("res://Prefabs/AntiAirMissile.tscn", 12000, "防空导弹"),
        ["Stealth"]      = new("res://Prefabs/Stealth.tscn",          24000, "隐身战机"),
        ["Dog"]          = new("res://Prefabs/Dog.tscn",              1500, "机械狗"),
        // ==================== 海军线（AW3 原作造价；炮艇 DoR 跨作适配） ====================
        ["Battleship"]   = new("res://Prefabs/Battleship.tscn",      28000, "战列舰"),
        ["Cruiser"]      = new("res://Prefabs/Cruiser.tscn",          18000, "巡洋舰"),
        ["Submarine"]    = new("res://Prefabs/Submarine.tscn",        20000, "潜艇"),
        ["Lander"]       = new("res://Prefabs/Lander.tscn",           12000, "登陆艇"),
        ["Carrier"]      = new("res://Prefabs/Carrier.tscn",           30000, "航母"),
        ["Gunboat"]      = new("res://Prefabs/Gunboat.tscn",           5000, "炮艇"),
        // ==================== 新增原版兵种（DS 原作值 / DoR 原作值；弩级坦克跨作适配） ====================
        ["Neotank"]        = new("res://Prefabs/Neotank.tscn", 22000, "新型坦克"),
        ["Megatank"]       = new("res://Prefabs/Megatank.tscn", 28000, "巨型坦克"),
        ["WarTank"]        = new("res://Prefabs/WarTank.tscn", 22000, "弩级坦克"),
        ["Rig"]            = new("res://Prefabs/Rig.tscn", 5000, "工程车"),
        ["PipeWorker"]     = new("res://Prefabs/PipeWorker.tscn", 7500, "管道构造者"),
        ["Duster"]         = new("res://Prefabs/Duster.tscn", 13000, "螺旋桨机"),
        ["Seaplane"]       = new("res://Prefabs/Seaplane.tscn", 15000, "水上飞机"),
        ["ProducibleBoat"] = new("res://Prefabs/ProducibleBoat.tscn", 28000, "生产舰"),
        ["KamikazeBoat"]   = new("res://Prefabs/KamikazeBoat.tscn", 25000, "突击舰"),
    };

    public static bool HasUnit(string unitName) => Units.ContainsKey(unitName);

    /// <summary>全部可生产单位名（超级兵工厂全列表模式）</summary>
    public static System.Collections.Generic.IEnumerable<string> GetAllUnitNames() => Units.Keys;

    public static UnitInfo GetInfo(string unitName)
    {
        if (Units.TryGetValue(unitName, out var info))
            return info;
        return new UnitInfo("", 0, unitName);
    }
}
