using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

// ExtraSave · Serialization（由 ExtraSave.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class ExtraSave
{

    // ==================== 序列化辅助 ====================

    private Godot.Collections.Array SerializeUnitList(List<SavedUnitData> list)
    {
        var arr = new Godot.Collections.Array();
        foreach (var item in list)
        {
            if (item == null) continue;
            var d = new Godot.Collections.Dictionary();
            d["slotName"] = item.slotName;
            d["displayName"] = item.displayName;
            d["scenePath"] = item.scenePath;
            d["typeName"] = item.typeName;
            d["team"] = item.team;
            d["isFavorite"] = item.isFavorite;
            d["savedTimestamp"] = item.savedTimestamp;
            d["lastUsedTimestamp"] = item.lastUsedTimestamp;
            d["fieldValues"] = DictToGodot(item.fieldValues);
            d["customTexturePath"] = item.customTexturePath;
            d["useCustomTexture"] = item.useCustomTexture;
            d["attackMatrix"] = DictToGodot(item.attackMatrix);
            d["secondaryAttackMatrix"] = DictToGodot(item.secondaryAttackMatrix);
            arr.Add(d);
        }
        return arr;
    }

    private List<SavedUnitData> ParseUnitList(Godot.Collections.Array arr)
    {
        var list = new List<SavedUnitData>();
        foreach (var item in arr)
        {
            var d = item.AsGodotDictionary();
            if (d == null) continue;
            list.Add(new SavedUnitData
            {
                slotName = d.GetValueOrDefault("slotName", "").AsString(),
                displayName = d.GetValueOrDefault("displayName", "").AsString(),
                scenePath = d.GetValueOrDefault("scenePath", "").AsString(),
                typeName = d.GetValueOrDefault("typeName", "").AsString(),
                team = d.GetValueOrDefault("team", "Player1").AsString(),
                isFavorite = d.GetValueOrDefault("isFavorite", false).AsBool(),
                savedTimestamp = d.GetValueOrDefault("savedTimestamp", 0).AsInt64(),
                lastUsedTimestamp = d.GetValueOrDefault("lastUsedTimestamp", 0).AsInt64(),
                fieldValues = ParseStringDict(d.GetValueOrDefault("fieldValues", new Variant()).AsGodotDictionary()),
                customTexturePath = d.GetValueOrDefault("customTexturePath", "").AsString(),
                useCustomTexture = d.GetValueOrDefault("useCustomTexture", false).AsBool(),
                attackMatrix = ParseIntDict(d.GetValueOrDefault("attackMatrix", new Variant()).AsGodotDictionary()),
                secondaryAttackMatrix = ParseIntDict(d.GetValueOrDefault("secondaryAttackMatrix", new Variant()).AsGodotDictionary())
            });
        }
        return list;
    }

    private Godot.Collections.Array SerializeWeaponList(List<SavedWeaponData> list)
    {
        var arr = new Godot.Collections.Array();
        foreach (var item in list)
        {
            if (item == null) continue;
            var d = new Godot.Collections.Dictionary();
            d["slotName"] = item.slotName;
            d["displayName"] = item.displayName;
            d["scenePath"] = item.scenePath;
            d["typeName"] = item.typeName;
            d["team"] = item.team;
            d["isFavorite"] = item.isFavorite;
            d["savedTimestamp"] = item.savedTimestamp;
            d["lastUsedTimestamp"] = item.lastUsedTimestamp;
            d["fieldValues"] = DictToGodot(item.fieldValues);
            d["customTexturePath"] = item.customTexturePath;
            d["useCustomTexture"] = item.useCustomTexture;
            d["isMultiTile"] = item.isMultiTile;
            d["sizeX"] = item.sizeX;
            d["sizeY"] = item.sizeY;
            d["direction"] = item.direction;
            arr.Add(d);
        }
        return arr;
    }

    private List<SavedWeaponData> ParseWeaponList(Godot.Collections.Array arr)
    {
        var list = new List<SavedWeaponData>();
        foreach (var item in arr)
        {
            var d = item.AsGodotDictionary();
            if (d == null) continue;
            list.Add(new SavedWeaponData
            {
                slotName = d.GetValueOrDefault("slotName", "").AsString(),
                displayName = d.GetValueOrDefault("displayName", "").AsString(),
                scenePath = d.GetValueOrDefault("scenePath", "").AsString(),
                typeName = d.GetValueOrDefault("typeName", "").AsString(),
                team = d.GetValueOrDefault("team", "Player1").AsString(),
                isFavorite = d.GetValueOrDefault("isFavorite", false).AsBool(),
                savedTimestamp = d.GetValueOrDefault("savedTimestamp", 0).AsInt64(),
                lastUsedTimestamp = d.GetValueOrDefault("lastUsedTimestamp", 0).AsInt64(),
                fieldValues = ParseStringDict(d.GetValueOrDefault("fieldValues", new Variant()).AsGodotDictionary()),
                customTexturePath = d.GetValueOrDefault("customTexturePath", "").AsString(),
                useCustomTexture = d.GetValueOrDefault("useCustomTexture", false).AsBool(),
                isMultiTile = d.GetValueOrDefault("isMultiTile", false).AsBool(),
                sizeX = d.GetValueOrDefault("sizeX", 1).AsInt32(),
                sizeY = d.GetValueOrDefault("sizeY", 1).AsInt32(),
                direction = d.GetValueOrDefault("direction", 0).AsInt32()
            });
        }
        return list;
    }

    private Godot.Collections.Array SerializeFacilityList(List<SavedFacilityData> list)
    {
        var arr = new Godot.Collections.Array();
        foreach (var item in list)
        {
            if (item == null) continue;
            var d = new Godot.Collections.Dictionary();
            d["slotName"] = item.slotName;
            d["displayName"] = item.displayName;
            d["scenePath"] = item.scenePath;
            d["typeName"] = item.typeName;
            d["team"] = item.team;
            d["isFavorite"] = item.isFavorite;
            d["savedTimestamp"] = item.savedTimestamp;
            d["lastUsedTimestamp"] = item.lastUsedTimestamp;
            d["fieldValues"] = DictToGodot(item.fieldValues);
            d["customTexturePath"] = item.customTexturePath;
            d["useCustomTexture"] = item.useCustomTexture;
            arr.Add(d);
        }
        return arr;
    }

    private List<SavedFacilityData> ParseFacilityList(Godot.Collections.Array arr)
    {
        var list = new List<SavedFacilityData>();
        foreach (var item in arr)
        {
            var d = item.AsGodotDictionary();
            if (d == null) continue;
            list.Add(new SavedFacilityData
            {
                slotName = d.GetValueOrDefault("slotName", "").AsString(),
                displayName = d.GetValueOrDefault("displayName", "").AsString(),
                scenePath = d.GetValueOrDefault("scenePath", "").AsString(),
                typeName = d.GetValueOrDefault("typeName", "").AsString(),
                team = d.GetValueOrDefault("team", "Player0").AsString(),
                isFavorite = d.GetValueOrDefault("isFavorite", false).AsBool(),
                savedTimestamp = d.GetValueOrDefault("savedTimestamp", 0).AsInt64(),
                lastUsedTimestamp = d.GetValueOrDefault("lastUsedTimestamp", 0).AsInt64(),
                fieldValues = ParseStringDict(d.GetValueOrDefault("fieldValues", new Variant()).AsGodotDictionary()),
                customTexturePath = d.GetValueOrDefault("customTexturePath", "").AsString(),
                useCustomTexture = d.GetValueOrDefault("useCustomTexture", false).AsBool()
            });
        }
        return list;
    }

    private Godot.Collections.Array SerializeGridList(List<SavedGridTerrainData> list)
    {
        var arr = new Godot.Collections.Array();
        foreach (var item in list)
        {
            if (item == null) continue;
            var d = new Godot.Collections.Dictionary();
            d["slotName"] = item.slotName;
            d["displayName"] = item.displayName;
            d["isFavorite"] = item.isFavorite;
            d["savedTimestamp"] = item.savedTimestamp;
            d["lastUsedTimestamp"] = item.lastUsedTimestamp;
            d["gridType"] = (int)item.gridType;
            d["terrainDefenseBonus"] = item.terrainDefenseBonus;
            d["canDestroyUnit"] = item.canDestroyUnit;
            d["fixedDamagePerTurn"] = item.fixedDamagePerTurn;
            d["fixedAttackPerTurn"] = item.fixedAttackPerTurn;
            d["canOverMaxHealth"] = item.canOverMaxHealth;
            d["fixedMaxHealthChangePerTurn"] = item.fixedMaxHealthChangePerTurn;
            d["fixedAmmoChangePerTurn"] = item.fixedAmmoChangePerTurn;
            d["ammoCanOverMax"] = item.ammoCanOverMax;
            d["ammoCanReachZero"] = item.ammoCanReachZero;
            d["ammoChangePrimary"] = item.ammoChangePrimary;
            d["ammoChangeExplode"] = item.ammoChangeExplode;
            d["ammoChangeFlare"] = item.ammoChangeFlare;
            d["ammoChangeStealth"] = item.ammoChangeStealth;
            d["ammoChangeReveal"] = item.ammoChangeReveal;
            d["ammoChangeCamo"] = item.ammoChangeCamo;
            d["ammoChangeProduction"] = item.ammoChangeProduction;
            d["ammoChangeConstruction"] = item.ammoChangeConstruction;
            d["ammoMaxChangePrimary"] = item.ammoMaxChangePrimary;
            d["ammoMaxChangeExplode"] = item.ammoMaxChangeExplode;
            d["ammoMaxChangeFlare"] = item.ammoMaxChangeFlare;
            d["ammoMaxChangeStealth"] = item.ammoMaxChangeStealth;
            d["ammoMaxChangeReveal"] = item.ammoMaxChangeReveal;
            d["ammoMaxChangeCamo"] = item.ammoMaxChangeCamo;
            d["ammoMaxChangeProduction"] = item.ammoMaxChangeProduction;
            d["ammoMaxChangeConstruction"] = item.ammoMaxChangeConstruction;
            d["fixedFuelChangePerTurn"] = item.fixedFuelChangePerTurn;
            d["fixedMaxFuelChangePerTurn"] = item.fixedMaxFuelChangePerTurn;
            d["fuelCanOverMax"] = item.fuelCanOverMax;
            d["fuelCanReachZero"] = item.fuelCanReachZero;
            d["gridVisionRange"] = item.gridVisionRange;
            d["gridVisionMode"] = item.gridVisionMode;
            d["towerDirection"] = item.towerDirection;
            d["towerCanRotate"] = item.towerCanRotate;
            d["towerRayAngle"] = item.towerRayAngle;
            d["gridVisionIgnoreTerrain"] = item.gridVisionIgnoreTerrain;
            d["visionBonus"] = item.visionBonus;
            d["requiresAdjacentVision"] = item.requiresAdjacentVision;
            d["isWatchtower"] = item.isWatchtower;
            d["customTexturePath"] = item.customTexturePath;
            d["useCustomTexture"] = item.useCustomTexture;
            d["terrainColorHex"] = item.terrainColorHex;
            arr.Add(d);
        }
        return arr;
    }

    private List<SavedGridTerrainData> ParseGridList(Godot.Collections.Array arr)
    {
        var list = new List<SavedGridTerrainData>();
        foreach (var item in arr)
        {
            var d = item.AsGodotDictionary();
            if (d == null) continue;
            list.Add(new SavedGridTerrainData
            {
                slotName = d.GetValueOrDefault("slotName", "").AsString(),
                displayName = d.GetValueOrDefault("displayName", "").AsString(),
                isFavorite = d.GetValueOrDefault("isFavorite", false).AsBool(),
                savedTimestamp = d.GetValueOrDefault("savedTimestamp", 0).AsInt64(),
                lastUsedTimestamp = d.GetValueOrDefault("lastUsedTimestamp", 0).AsInt64(),
                gridType = (GridType)d.GetValueOrDefault("gridType", 0).AsInt32(),
                terrainDefenseBonus = d.GetValueOrDefault("terrainDefenseBonus", 0f).AsSingle(),
                canDestroyUnit = d.GetValueOrDefault("canDestroyUnit", false).AsBool(),
                fixedDamagePerTurn = d.GetValueOrDefault("fixedDamagePerTurn", 0).AsInt32(),
                fixedAttackPerTurn = d.GetValueOrDefault("fixedAttackPerTurn", 0).AsInt32(),
                canOverMaxHealth = d.GetValueOrDefault("canOverMaxHealth", false).AsBool(),
                fixedMaxHealthChangePerTurn = d.GetValueOrDefault("fixedMaxHealthChangePerTurn", 0).AsInt32(),
                fixedAmmoChangePerTurn = d.GetValueOrDefault("fixedAmmoChangePerTurn", 0).AsInt32(),
                ammoCanOverMax = d.GetValueOrDefault("ammoCanOverMax", false).AsBool(),
                ammoCanReachZero = d.GetValueOrDefault("ammoCanReachZero", true).AsBool(),
                ammoChangePrimary = d.GetValueOrDefault("ammoChangePrimary", 0).AsInt32(),
                ammoChangeExplode = d.GetValueOrDefault("ammoChangeExplode", 0).AsInt32(),
                ammoChangeFlare = d.GetValueOrDefault("ammoChangeFlare", 0).AsInt32(),
                ammoChangeStealth = d.GetValueOrDefault("ammoChangeStealth", 0).AsInt32(),
                ammoChangeReveal = d.GetValueOrDefault("ammoChangeReveal", 0).AsInt32(),
                ammoChangeCamo = d.GetValueOrDefault("ammoChangeCamo", 0).AsInt32(),
                ammoChangeProduction = d.GetValueOrDefault("ammoChangeProduction", 0).AsInt32(),
                ammoChangeConstruction = d.GetValueOrDefault("ammoChangeConstruction", 0).AsInt32(),
                ammoMaxChangePrimary = d.GetValueOrDefault("ammoMaxChangePrimary", 0).AsInt32(),
                ammoMaxChangeExplode = d.GetValueOrDefault("ammoMaxChangeExplode", 0).AsInt32(),
                ammoMaxChangeFlare = d.GetValueOrDefault("ammoMaxChangeFlare", 0).AsInt32(),
                ammoMaxChangeStealth = d.GetValueOrDefault("ammoMaxChangeStealth", 0).AsInt32(),
                ammoMaxChangeReveal = d.GetValueOrDefault("ammoMaxChangeReveal", 0).AsInt32(),
                ammoMaxChangeCamo = d.GetValueOrDefault("ammoMaxChangeCamo", 0).AsInt32(),
                ammoMaxChangeProduction = d.GetValueOrDefault("ammoMaxChangeProduction", 0).AsInt32(),
                ammoMaxChangeConstruction = d.GetValueOrDefault("ammoMaxChangeConstruction", 0).AsInt32(),
                fixedFuelChangePerTurn = d.GetValueOrDefault("fixedFuelChangePerTurn", 0).AsInt32(),
                fixedMaxFuelChangePerTurn = d.GetValueOrDefault("fixedMaxFuelChangePerTurn", 0).AsInt32(),
                fuelCanOverMax = d.GetValueOrDefault("fuelCanOverMax", false).AsBool(),
                fuelCanReachZero = d.GetValueOrDefault("fuelCanReachZero", true).AsBool(),
                gridVisionRange = d.GetValueOrDefault("gridVisionRange", 0).AsInt32(),
                gridVisionMode = d.GetValueOrDefault("gridVisionMode", 0).AsInt32(),
                towerDirection = d.GetValueOrDefault("towerDirection", 0).AsInt32(),
                towerCanRotate = d.GetValueOrDefault("towerCanRotate", false).AsBool(),
                towerRayAngle = d.GetValueOrDefault("towerRayAngle", 45f).AsSingle(),
                gridVisionIgnoreTerrain = d.GetValueOrDefault("gridVisionIgnoreTerrain", false).AsBool(),
                visionBonus = d.GetValueOrDefault("visionBonus", 0).AsInt32(),
                requiresAdjacentVision = d.GetValueOrDefault("requiresAdjacentVision", false).AsBool(),
                isWatchtower = d.GetValueOrDefault("isWatchtower", false).AsBool(),
                customTexturePath = d.GetValueOrDefault("customTexturePath", "").AsString(),
                useCustomTexture = d.GetValueOrDefault("useCustomTexture", false).AsBool(),
                terrainColorHex = d.GetValueOrDefault("terrainColorHex", "ffffff").AsString()
            });
        }
        return list;
    }

    private Godot.Collections.Array SerializeMapList(List<SavedMapData> list)
    {
        var arr = new Godot.Collections.Array();
        foreach (var item in list)
        {
            if (item == null) continue;
            var d = new Godot.Collections.Dictionary();
            d["slotName"] = item.slotName;
            d["displayName"] = item.displayName;
            d["savedTimestamp"] = item.savedTimestamp;
            d["lastUsedTimestamp"] = item.lastUsedTimestamp;
            d["width"] = item.width;
            d["height"] = item.height;
            d["fogOfWarEnabled"] = item.fogOfWarEnabled;
            d["localWeatherEnabled"] = item.localWeatherEnabled;
            d["weatherStacks"] = item.weatherStacks;
            d["terrainIds"] = new Godot.Collections.Array<int>(item.terrainIds);
            d["units"] = SerializeUnitList(item.units);
            d["weapons"] = SerializeWeaponList(item.weapons);
            d["facilities"] = SerializeFacilityList(item.facilities);
            d["p1Funds"] = item.p1Funds;
            d["p2Funds"] = item.p2Funds;
            d["screenCount"] = item.screenCount;
            d["screenWidths"] = new Godot.Collections.Array<int>(item.screenWidths);
            d["screenHeights"] = new Godot.Collections.Array<int>(item.screenHeights);
            d["screenYOffsets"] = new Godot.Collections.Array<int>(item.screenYOffsets);
            d["screenGap"] = item.screenGap;
            d["freeformScreens"] = item.freeformScreens;
            arr.Add(d);
        }
        return arr;
    }

    private static List<int> ParseIntList(Godot.Collections.Array arr)
    {
        var list = new List<int>();
        if (arr == null) return list;
        foreach (var v in arr) list.Add(v.AsInt32());
        return list;
    }

    private List<SavedMapData> ParseMapList(Godot.Collections.Array arr)
    {
        var list = new List<SavedMapData>();
        foreach (var item in arr)
        {
            var d = item.AsGodotDictionary();
            if (d == null) continue;
            var mapData = new SavedMapData
            {
                slotName = d.GetValueOrDefault("slotName", "").AsString(),
                displayName = d.GetValueOrDefault("displayName", "").AsString(),
                savedTimestamp = d.GetValueOrDefault("savedTimestamp", 0).AsInt64(),
                lastUsedTimestamp = d.GetValueOrDefault("lastUsedTimestamp", 0).AsInt64(),
                width = d.GetValueOrDefault("width", 0).AsInt32(),
                height = d.GetValueOrDefault("height", 0).AsInt32(),
                fogOfWarEnabled = d.GetValueOrDefault("fogOfWarEnabled", false).AsBool(),
                localWeatherEnabled = d.GetValueOrDefault("localWeatherEnabled", false).AsBool(),
                weatherStacks = d.GetValueOrDefault("weatherStacks", "").AsString(),
                p1Funds = d.GetValueOrDefault("p1Funds", 0).AsInt32(),
                p2Funds = d.GetValueOrDefault("p2Funds", 0).AsInt32(),
                screenCount = d.GetValueOrDefault("screenCount", 1).AsInt32(),
                screenWidths = ParseIntList(d.GetValueOrDefault("screenWidths", new Variant()).AsGodotArray()),
                screenHeights = ParseIntList(d.GetValueOrDefault("screenHeights", new Variant()).AsGodotArray()),
                screenYOffsets = ParseIntList(d.GetValueOrDefault("screenYOffsets", new Variant()).AsGodotArray()),
                screenGap = d.GetValueOrDefault("screenGap", 1).AsInt32(),
                freeformScreens = d.GetValueOrDefault("freeformScreens", false).AsBool()
            };

            var terrainArr = d.GetValueOrDefault("terrainIds", new Variant()).AsGodotArray();
            foreach (var t in terrainArr) mapData.terrainIds.Add(t.AsInt32());

            mapData.units = ParseUnitList(d.GetValueOrDefault("units", new Variant()).AsGodotArray());
            mapData.weapons = ParseWeaponList(d.GetValueOrDefault("weapons", new Variant()).AsGodotArray());
            mapData.facilities = ParseFacilityList(d.GetValueOrDefault("facilities", new Variant()).AsGodotArray());

            list.Add(mapData);
        }
        return list;
    }

    private Godot.Collections.Array SerializeCustomMoveTypeList(List<CustomMoveTypeData> list)
    {
        var arr = new Godot.Collections.Array();
        foreach (var item in list)
        {
            var d = new Godot.Collections.Dictionary();
            d["id"] = item.id;
            d["displayName"] = item.displayName;
            d["isBuiltIn"] = item.isBuiltIn;
            d["moveCosts"] = DictToGodot(item.moveCosts);
            d["customTexturePath"] = item.customTexturePath;
            d["useCustomTexture"] = item.useCustomTexture;
            arr.Add(d);
        }
        return arr;
    }

    private List<CustomMoveTypeData> ParseCustomMoveTypeList(Godot.Collections.Array arr)
    {
        var list = new List<CustomMoveTypeData>();
        foreach (var item in arr)
        {
            var d = item.AsGodotDictionary();
            if (d == null) continue;
            list.Add(new CustomMoveTypeData
            {
                id = d.GetValueOrDefault("id", "").AsString(),
                displayName = d.GetValueOrDefault("displayName", "").AsString(),
                isBuiltIn = d.GetValueOrDefault("isBuiltIn", false).AsBool(),
                moveCosts = ParseIntDict(d.GetValueOrDefault("moveCosts", new Variant()).AsGodotDictionary()),
                customTexturePath = d.GetValueOrDefault("customTexturePath", "").AsString(),
                useCustomTexture = d.GetValueOrDefault("useCustomTexture", false).AsBool()
            });
        }
        return list;
    }

    private Godot.Collections.Array SerializeCustomTerrainTypeList(List<CustomTerrainTypeData> list)
    {
        var arr = new Godot.Collections.Array();
        foreach (var item in list)
        {
            var d = new Godot.Collections.Dictionary();
            d["id"] = item.id;
            d["displayName"] = item.displayName;
            d["isBuiltIn"] = item.isBuiltIn;
            d["moveCosts"] = DictToGodot(item.moveCosts);
            d["defenseBonus"] = item.defenseBonus;
            d["terrainColorHex"] = item.terrainColorHex;
            d["customTexturePath"] = item.customTexturePath;
            d["useCustomTexture"] = item.useCustomTexture;
            arr.Add(d);
        }
        return arr;
    }

    private List<CustomTerrainTypeData> ParseCustomTerrainTypeList(Godot.Collections.Array arr)
    {
        var list = new List<CustomTerrainTypeData>();
        foreach (var item in arr)
        {
            var d = item.AsGodotDictionary();
            if (d == null) continue;
            list.Add(new CustomTerrainTypeData
            {
                id = d.GetValueOrDefault("id", "").AsString(),
                displayName = d.GetValueOrDefault("displayName", "").AsString(),
                isBuiltIn = d.GetValueOrDefault("isBuiltIn", false).AsBool(),
                moveCosts = ParseIntDict(d.GetValueOrDefault("moveCosts", new Variant()).AsGodotDictionary()),
                defenseBonus = d.GetValueOrDefault("defenseBonus", 0f).AsSingle(),
                terrainColorHex = d.GetValueOrDefault("terrainColorHex", "ffffff").AsString(),
                customTexturePath = d.GetValueOrDefault("customTexturePath", "").AsString(),
                useCustomTexture = d.GetValueOrDefault("useCustomTexture", false).AsBool()
            });
        }
        return list;
    }

    private Godot.Collections.Dictionary DictToGodot(Dictionary<string, string> dict)
    {
        var gd = new Godot.Collections.Dictionary();
        foreach (var kvp in dict) gd[kvp.Key] = kvp.Value;
        return gd;
    }

    private Godot.Collections.Dictionary DictToGodot(Dictionary<string, int> dict)
    {
        var gd = new Godot.Collections.Dictionary();
        foreach (var kvp in dict) gd[kvp.Key] = kvp.Value;
        return gd;
    }

    private Dictionary<string, string> ParseStringDict(Godot.Collections.Dictionary gd)
    {
        var dict = new Dictionary<string, string>();
        foreach (var k in gd.Keys) dict[k.AsString()] = gd[k].AsString();
        return dict;
    }

    private Dictionary<string, int> ParseIntDict(Godot.Collections.Dictionary gd)
    {
        var dict = new Dictionary<string, int>();
        foreach (var k in gd.Keys) dict[k.AsString()] = gd[k].AsInt32();
        return dict;
    }

    private List<string> ParseStringList(Godot.Collections.Array arr)
    {
        var list = new List<string>();
        foreach (var item in arr) list.Add(item.AsString());
        return list;
    }

    // ==================== 序列化辅助方法 ====================

    private Dictionary<string, string> SerializeObjectFields(object obj)
    {
        var result = new Dictionary<string, string>();
        if (obj == null) return result;

        var type = obj.GetType();
        var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(f => f.GetCustomAttribute<ExportAttribute>() != null);

        foreach (var field in fields)
        {
            try
            {
                var value = field.GetValue(obj);
                if (value != null)
                {
                    result[field.Name] = ValueToString(value);
                }
            }
            catch (System.Exception ex) { GD.PushWarning($"[ExtraSave] {ex.GetType().Name}: {ex.Message}"); }
        }

        var props = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ExportAttribute>() != null && p.CanRead && p.CanWrite);

        foreach (var prop in props)
        {
            try
            {
                var value = prop.GetValue(obj);
                if (value != null)
                {
                    result[prop.Name] = ValueToString(value);
                }
            }
            catch (System.Exception ex) { GD.PushWarning($"[ExtraSave] {ex.GetType().Name}: {ex.Message}"); }
        }

        return result;
    }

    private string ValueToString(object value)
    {
        if (value == null) return "null";
        if (value is bool b) return b ? "true" : "false";
        if (value is int i) return i.ToString();
        if (value is uint ui) return ui.ToString();
        if (value is long l) return l.ToString();
        if (value is float f) return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (value is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (value is string s) return s;
        if (value is Color c) return c.ToHtml();
        if (value is Vector2 v2) return $"({v2.X},{v2.Y})";
        if (value is Vector2I v2i) return $"({v2i.X},{v2i.Y})";
        if (value.GetType().IsEnum) return ((int)value).ToString();
        return value.ToString();
    }

    private string GetScenePathForInstance(Node instance)
    {
        if (instance == null) return "";
        // 尝试从场景文件路径推断
        var sceneFile = instance.SceneFilePath;
        if (!string.IsNullOrEmpty(sceneFile)) return sceneFile;

        //  fallback：根据类型名推断
        return $"res://Prefabs/{instance.GetType().Name.ToLower()}.tscn";
    }

    private Dictionary<string, int> ToSystemDict(Godot.Collections.Dictionary<string, int> godotDict)
    {
        var dict = new Dictionary<string, int>();
        if (godotDict == null) return dict;
        foreach (var kvp in godotDict) dict[kvp.Key] = kvp.Value;
        return dict;
    }
}
