// TerrainRegistry.cs
// 地形注册表: 枚举之外的可扩展层 —— 内置地形从GridType反射种子化; 自定义地形运行时Register/JSON持久化, 不写死
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>一条地形定义(数据驱动, 等价CoW的每地形JS脚本槽位)</summary>
public class TerrainDefinition
{
    public string id;             // 唯一id: 内置=枚举名; 自定义="custom_xxx"
    public string displayName;    // 显示名
    public int colorRgb = 0x808080;
    public string baseTerrain;    // 基底地形(继承移动/防御, 对标CoW baseTerrainId链)
    public string biomeSkin;      // 皮肤组(DESERT/SNOW/WASTE/常规)
    public bool builtIn;          // true=来自GridType枚举
}

public static class TerrainRegistry
{
    public static readonly Dictionary<string, TerrainDefinition> defs = new();
    public const string CustomTerrainJsonPath = "user://custom_terrains.json";

    static TerrainRegistry()
    {
        foreach (GridType t in Enum.GetValues<GridType>())
            defs[t.ToString()] = new TerrainDefinition
            {
                id = t.ToString(), displayName = t.ToString(), builtIn = true,
            };
    }

    /// <summary>运行时添加地形(编辑器"新增地形"入口); 返回是否成功(重复id拒绝)</summary>
    public static bool Register(TerrainDefinition def)
    {
        if (def == null || string.IsNullOrEmpty(def.id) || defs.ContainsKey(def.id)) return false;
        defs[def.id] = def;
        return true;
    }

    public static TerrainDefinition Get(string id) => defs.TryGetValue(id, out var d) ? d : null;

    public static void SaveCustom()
    {
        var customs = defs.Values.Where(d => !d.builtIn)
            .Select(d => new Godot.Collections.Dictionary { ["id"] = d.id, ["name"] = d.displayName, ["base"] = d.baseTerrain, ["skin"] = d.biomeSkin }).ToArray();
        var arr = new Godot.Collections.Array();
        foreach (var d in customs) arr.Add(d);
        FileAccess.Open(CustomTerrainJsonPath, FileAccess.ModeFlags.Write)?.StoreString(Json.Stringify(arr));
    }

    public static int LoadCustom()
    {
        using var f = FileAccess.Open(CustomTerrainJsonPath, FileAccess.ModeFlags.Read);
        if (f == null) return 0;
        var arr = Json.ParseString(f.GetAsText()).AsGodotArray();
        int n = 0;
        foreach (var v in arr)
        {
            var d = v.AsGodotDictionary();
            if (Register(new TerrainDefinition { id = d["id"].AsString(), displayName = d["name"].AsString(), baseTerrain = d["base"].AsString(), biomeSkin = d["skin"].AsString() })) n++;
        }
        return n;
    }
}
