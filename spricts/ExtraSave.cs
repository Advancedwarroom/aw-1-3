using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

// ==================== 主管理类 ====================

public partial class ExtraSave : Node
{
    public const int MAX_SLOTS = 10;
    public const int MAX_FAVORITES = 5;
    public const int MAX_MAP_SLOTS = 5;
    public const int MAX_TEXTURE_SIZE = 32;
    public const string SAVE_FILE_NAME = "user://ExtraSaveData.json";
    public const string CUSTOM_TEXTURE_DIR = "user://CustomTextures/";
    public const string CUSTOM_MOVE_TYPES_FILE = "user://CustomMoveTypes.json";
    public const string CUSTOM_TERRAIN_TYPES_FILE = "user://CustomTerrainTypes.json";

    public ExtraSaveData Data { get; private set; } = new();

    // 运行时缓存：自定义移动方式
    public Dictionary<string, CustomMoveTypeData> CustomMoveTypes { get; private set; } = new();
    // 运行时缓存：自定义地形
    public Dictionary<string, CustomTerrainTypeData> CustomTerrainTypes { get; private set; } = new();

    // 内置移动方式列表（用于显示和配置）
    public static readonly string[] BuiltInMoveTypes = new[]
    {
        "Infantry", "Mech", "Oozium", "Treads", "Naval", "AirPlane", 
        "AirShip", "Drone", "AeroSpacer", "HeliCopter", "SpaceShiper",
        "Hover", "PipeRunner", "Tire", "LAVARUNNER", "LAVAHOVER",
        "Train", "GasTrain", "FASTER", "Missile"
    };

    // 内置地形列表
    public static readonly string[] BuiltInTerrainTypes = new[]
    {
        "GROUND", "FOREST", "ROAD", "SEA", "RIVER", "HILL", "METEORITE",
        "PIPE", "LAVA", "BEACH", "TP", "REEF", "WHIRLPOOL", "LAVASIDE",
        "SEAFOG", "LANDFOG", "WATERFALL", "CLIFF", "SLOPE", "CAVE", "HOLE",
        "PIPESEAM", "TRACK", "STATION", "BRIDGE", "LAVABRIDGE", "PASSABLEPIPE",
        "SHIPGATE", "OVERPASS", "BROKENPIPE", "RUINS", "BROKENTRACK", "LAVAFOG"
    };

    public static ExtraSave Instance { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        AddToGroup("extra_save");

        // 确保目录存在
        EnsureDirectoryExists(CUSTOM_TEXTURE_DIR);
        EnsureDirectoryExists(GodotPathCombine(CUSTOM_TEXTURE_DIR, TranslateGroup.T("单位")));
        EnsureDirectoryExists(GodotPathCombine(CUSTOM_TEXTURE_DIR, TranslateGroup.T("兵器")));
        EnsureDirectoryExists(GodotPathCombine(CUSTOM_TEXTURE_DIR, TranslateGroup.T("设施")));
        EnsureDirectoryExists(GodotPathCombine(CUSTOM_TEXTURE_DIR, TranslateGroup.T("地形")));
        EnsureDirectoryExists(GodotPathCombine(CUSTOM_TEXTURE_DIR, TranslateGroup.T("移动方式")));

        LoadAllData();
        InitializeDefaultSlots();

        GD.Print(TranslateGroup.T("[ExtraSave] 初始化完成，版本: ") + Data.version);
    }

    private void EnsureDirectoryExists(string path)
    {
        if (!DirAccess.DirExistsAbsolute(path))
        {
            DirAccess.MakeDirRecursiveAbsolute(path);
        }
    }

    private void LoadCustomMoveTypes()
    {
        if (!Godot.FileAccess.FileExists(CUSTOM_MOVE_TYPES_FILE)) return;

        try
        {
            using var file = Godot.FileAccess.Open(CUSTOM_MOVE_TYPES_FILE, Godot.FileAccess.ModeFlags.Read);
            var dict = Json.ParseString(file.GetAsText()).AsGodotDictionary();
            if (dict == null) return;

            CustomMoveTypes.Clear();
            Data.customMoveTypes.Clear();

            foreach (var k in dict.Keys)
            {
                var d = dict[k].AsGodotDictionary();
                if (d == null) continue;

                var data = new CustomMoveTypeData
                {
                    id = d.GetValueOrDefault("id", "").AsString(),
                    displayName = d.GetValueOrDefault("displayName", "").AsString(),
                    isBuiltIn = d.GetValueOrDefault("isBuiltIn", false).AsBool(),
                    moveCosts = ParseIntDict(d.GetValueOrDefault("moveCosts", new Variant()).AsGodotDictionary()),
                    customTexturePath = d.GetValueOrDefault("customTexturePath", "").AsString(),
                    useCustomTexture = d.GetValueOrDefault("useCustomTexture", false).AsBool()
                };

                CustomMoveTypes[data.id] = data;
                Data.customMoveTypes.Add(data);
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ExtraSave] {TranslateGroup.T("加载自定义移动方式失败")}: {e.Message}");
        }
    }

    private void LoadCustomTerrainTypes()
    {
        if (!Godot.FileAccess.FileExists(CUSTOM_TERRAIN_TYPES_FILE)) return;

        try
        {
            using var file = Godot.FileAccess.Open(CUSTOM_TERRAIN_TYPES_FILE, Godot.FileAccess.ModeFlags.Read);
            var dict = Json.ParseString(file.GetAsText()).AsGodotDictionary();
            if (dict == null) return;

            CustomTerrainTypes.Clear();
            Data.customTerrainTypes.Clear();

            foreach (var k in dict.Keys)
            {
                var d = dict[k].AsGodotDictionary();
                if (d == null) continue;

                var data = new CustomTerrainTypeData
                {
                    id = d.GetValueOrDefault("id", "").AsString(),
                    displayName = d.GetValueOrDefault("displayName", "").AsString(),
                    isBuiltIn = d.GetValueOrDefault("isBuiltIn", false).AsBool(),
                    moveCosts = ParseIntDict(d.GetValueOrDefault("moveCosts", new Variant()).AsGodotDictionary()),
                    defenseBonus = d.GetValueOrDefault("defenseBonus", 0f).AsSingle(),
                    terrainColorHex = d.GetValueOrDefault("terrainColorHex", "ffffff").AsString(),
                    customTexturePath = d.GetValueOrDefault("customTexturePath", "").AsString(),
                    useCustomTexture = d.GetValueOrDefault("useCustomTexture", false).AsBool()
                };

                CustomTerrainTypes[data.id] = data;
                Data.customTerrainTypes.Add(data);
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ExtraSave] {TranslateGroup.T("加载自定义地形失败")}: {e.Message}");
        }
    }

    /// <summary>
    /// 从保存的地图数据加载到游戏中（ TerrainEditor 调用）
    /// </summary>
    public bool LoadMapToGame(int slotIndex, GameManager gm, GridManager gridMgr, TerrainEditor terrainEditor)
    {
        if (slotIndex < 0 || slotIndex >= Data.savedMaps.Count) return false;
        var mapData = Data.savedMaps[slotIndex];
        if (mapData.width == 0) return false;

        // 更新时间戳
        UpdateMapLastUsed(slotIndex);

        if (gm == null || gridMgr == null || terrainEditor == null) return false;

        // 1. 清理现有实体
        terrainEditor.ClearAllMapEntities();

        // 2. 调整地图大小
        gridMgr.ResizeMap(mapData.width, mapData.height);

        // ✅ 分屏结构（开创人2026-11-07）：矩形模式Init前应用屏参数；自由拓扑先全铺建格、地形后挖真空再洪泛重算
        bool hasScreens = mapData.screenWidths != null && mapData.screenWidths.Count > 0;
        bool freeform = hasScreens && mapData.freeformScreens;
        if (hasScreens && !freeform)
        {
            gridMgr.screenCount = Mathf.Max(1, mapData.screenCount);
            gridMgr.screenWidths = mapData.screenWidths.ToArray();
            gridMgr.screenHeights = mapData.screenHeights.Count == gridMgr.screenCount ? mapData.screenHeights.ToArray() : Enumerable.Repeat(mapData.height, gridMgr.screenCount).ToArray();
            gridMgr.screenYOffsets = mapData.screenYOffsets.Count == gridMgr.screenCount ? mapData.screenYOffsets.ToArray() : new int[gridMgr.screenCount];
            gridMgr.screenGap = mapData.screenGap;
        }
        if (freeform) gridMgr.screenCount = 1;   // 自由拓扑：先铺满矩形建格（真实拓扑由真空+洪泛恢复）
        gridMgr.Init();

        // 3. 加载地形
        int terrainIdx = 0;
        for (int y = 0; y < mapData.height; y++)
        {
            for (int x = 0; x < mapData.width; x++)
            {
                if (terrainIdx < mapData.terrainIds.Count)
                {
                    var grid = gridMgr.map[x, y];
                    if (grid != null)
                    {
                        grid.gridType = (GridType)mapData.terrainIds[terrainIdx];
                        terrainEditor.UpdateGridVisual(grid);
                    }
                }
                terrainIdx++;
            }
        }


        if (freeform)
        {
            // 按存档真空挖格（-1=无格）+ 洪泛重算屏号（连通块=屏，屏号不用存）
            var deadCells = new List<Grids>();
            int ti2 = 0;
            for (int y = 0; y < mapData.height; y++)
                for (int x = 0; x < mapData.width; x++)
                {
                    if (ti2 < mapData.terrainIds.Count && mapData.terrainIds[ti2] == -1)
                    {
                        var dg = gridMgr.map[x, y];
                        if (dg != null) deadCells.Add(dg);
                    }
                    ti2++;
                }
            foreach (var dg in deadCells)
            {
                gridMgr.grids.Remove(dg);
                gridMgr.map[dg.GridIndex.X, dg.GridIndex.Y] = null;
                gridMgr.GridsNode.RemoveChild(dg);
                dg.Free();
            }
            gridMgr.screenGap = mapData.screenGap;
            gridMgr.freeformScreens = true;
            gridMgr.RecalculateScreensFromCells();
        }

        // 4. 加载设施
        foreach (var fData in mapData.facilities)
        {
            if (string.IsNullOrEmpty(fData.scenePath)) continue;
            var grid = FindGridByIndex(gridMgr, fData.fieldValues.GetValueOrDefault("gridX", "0"), fData.fieldValues.GetValueOrDefault("gridY", "0"));
            if (grid == null) continue;
            terrainEditor.SpawnSavedFacilityAtGrid(grid, fData);
        }

        // 5. 加载兵器
        foreach (var wData in mapData.weapons)
        {
            if (string.IsNullOrEmpty(wData.scenePath)) continue;
            var grid = FindGridByIndex(gridMgr, wData.fieldValues.GetValueOrDefault("gridX", "0"), wData.fieldValues.GetValueOrDefault("gridY", "0"));
            if (grid == null) continue;
            terrainEditor.SpawnSavedWeaponAtGrid(grid, wData);
        }

        // 6. 加载单位
        foreach (var uData in mapData.units)
        {
            if (string.IsNullOrEmpty(uData.scenePath)) continue;
            var grid = FindGridByIndex(gridMgr, uData.fieldValues.GetValueOrDefault("gridX", "0"), uData.fieldValues.GetValueOrDefault("gridY", "0"));
            if (grid == null) continue;
            terrainEditor.SpawnSavedUnitAtGrid(grid, uData);
        }

        // 7. 加载资金
        gm.p1Funds = mapData.p1Funds;
        gm.p2Funds = mapData.p2Funds;

        // 8. ✅ issue#1 恢复区域天气（开关/基底先落，DeserializeStacks 内部 ClearAllWeather+AddWeather 受开关门控）
        var wmLoad = WeatherManager.Instance;
        if (wmLoad != null)
        {
            wmLoad.localWeatherEnabled = mapData.localWeatherEnabled;
            wmLoad.DeserializeStacks(mapData.weatherStacks);
        }

        gm.fogOfWarManager?.RefreshFog();
        gm.UpdateUnitLists();
        gm.RefreshSpecializedUnitLists();

        return true;
    }

    // Godot 路径拼接（统一使用正斜杠）
    private string GodotPathCombine(params string[] parts)
    {
        return string.Join("/", parts).Replace("\\", "/");
    }
}
