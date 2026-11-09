using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

// ExtraSave · MapSave（由 ExtraSave.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class ExtraSave
{

    // ==================== 地图保存/加载 ====================

    public bool SaveCurrentMap(int slotIndex, string customName, GameManager gm, GridManager gridMgr)
    {
        if (slotIndex < 0 || slotIndex >= MAX_MAP_SLOTS || gm == null || gridMgr == null) return false;

        var mapData = new SavedMapData
        {
            slotName = $"{TranslateGroup.T("地图")}{slotIndex + 1}",
            displayName = customName ?? $"{TranslateGroup.T("地图")}{slotIndex + 1}",
            savedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            lastUsedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            width = gridMgr.map?.GetLength(0) ?? 0,
            height = gridMgr.map?.GetLength(1) ?? 0,
            fogOfWarEnabled = gm.fogOfWarManager?.isFogOfWarEnabled ?? false,
            localWeatherEnabled = WeatherManager.Instance?.localWeatherEnabled ?? false,
            weatherStacks = WeatherManager.Instance?.SerializeStacks() ?? "",
            p1Funds = gm.p1Funds,
            p2Funds = gm.p2Funds,
            screenCount = gridMgr.screenCount,
            screenWidths = new List<int>(gridMgr.screenWidths ?? Array.Empty<int>()),
            screenHeights = new List<int>(gridMgr.screenHeights ?? Array.Empty<int>()),
            screenYOffsets = new List<int>(gridMgr.screenYOffsets ?? Array.Empty<int>()),
            screenGap = gridMgr.screenGap,
            freeformScreens = gridMgr.freeformScreens,
            teamFunds = TeamHelper.GetActiveFactionIds().ToDictionary(id => id, id => gm.GetTeamFunds(id))
        };

        // 保存地形
        if (gridMgr.map != null)
        {
            for (int y = 0; y < gridMgr.map.GetLength(1); y++)
            {
                for (int x = 0; x < gridMgr.map.GetLength(0); x++)
                {
                    var grid = gridMgr.map[x, y];
                    mapData.terrainIds.Add(grid != null ? (int)grid.gridType : -1);   // ✅ 真空存-1（开创人2026-11-07：GROUND=0与真空撞车，自由拓扑读档毁形状的根源）
                }
            }
        }

        // 保存单位
        if (gm.unitManager?.AllUnits != null)
        {
            foreach (var unit in gm.unitManager.AllUnits)
            {
                if (unit == null || !IsInstanceValid(unit)) continue;
                bool carried = unit.grid == null && unit.isTransported;
                if (unit.grid == null && !carried) continue;
                var uData = new SavedUnitData
                {
                    slotName = unit.Name,
                    displayName = unit.Name,
                    scenePath = GetScenePathForInstance(unit),
                    typeName = unit.GetType().Name,
                    team = unit.team,
                    fieldValues = SerializeObjectFields(unit),
                    attackMatrix = ToSystemDict(unit.attackMatrix),
                    secondaryAttackMatrix = ToSystemDict(unit.secondaryAttackMatrix)
                };
                EmpowermentPersistence.AddRuntimeFields(unit, uData.fieldValues);
                // 普通单位记录自身格；舱内单位记录宿主格和宿主身份，读档生成后立即重新装载。
                Grids saveGrid = unit.grid;
                if (carried)
                {
                    var unitHost = gm.unitManager.AllUnits.FirstOrDefault(host => host != null && host.transportedUnits.Contains(unit));
                    var weaponHost = gm.weaponManager?.AllWeapons.FirstOrDefault(host => host != null && host.shelteredUnits.Contains(unit));
                    saveGrid = unitHost?.grid ?? weaponHost?.grid;
                    if (unitHost != null) { uData.fieldValues["transportHostKind"] = "Unit"; uData.fieldValues["transportHostName"] = unitHost.Name; }
                    if (weaponHost != null) { uData.fieldValues["transportHostKind"] = "Weapon"; uData.fieldValues["transportHostName"] = weaponHost.Name; }
                }
                if (saveGrid == null) continue;
                uData.fieldValues["gridX"] = saveGrid.GridIndex.X.ToString();
                uData.fieldValues["gridY"] = saveGrid.GridIndex.Y.ToString();
                mapData.units.Add(uData);
            }
        }

        // 保存兵器
        if (gm.weaponManager?.AllWeapons != null)
        {
            foreach (var weapon in gm.weaponManager.AllWeapons)
            {
                if (weapon == null || !IsInstanceValid(weapon) || weapon.grid == null) continue;
                var wData = new SavedWeaponData
                {
                    slotName = weapon.Name,
                    displayName = weapon.Name,
                    scenePath = GetScenePathForInstance(weapon),
                    typeName = weapon.GetType().Name,
                    team = weapon.team,
                    fieldValues = SerializeObjectFields(weapon)
                };
                EmpowermentPersistence.AddRuntimeFields(weapon, wData.fieldValues);
                // 记录格子坐标用于加载
                wData.fieldValues["gridX"] = weapon.grid.GridIndex.X.ToString();
                wData.fieldValues["gridY"] = weapon.grid.GridIndex.Y.ToString();
                mapData.weapons.Add(wData);
            }
        }

        // 保存设施
        if (gridMgr.grids != null)
        {
            foreach (var grid in gridMgr.grids)
            {
                if (grid?.city == null || !IsInstanceValid(grid.city)) continue;
                var fData = new SavedFacilityData
                {
                    slotName = grid.city.Name,
                    displayName = grid.city.Name,
                    scenePath = GetScenePathForInstance(grid.city),
                    typeName = grid.city.GetType().Name,
                    team = grid.city.facilityTeam,
                    fieldValues = SerializeObjectFields(grid.city)
                };
                EmpowermentPersistence.AddRuntimeFields(source: grid.city, fields: fData.fieldValues);
                // 记录格子坐标用于加载
                fData.fieldValues["gridX"] = grid.GridIndex.X.ToString();
                fData.fieldValues["gridY"] = grid.GridIndex.Y.ToString();
                mapData.facilities.Add(fData);
            }
        }

        // ✅ 开创人2026-11-08：格子自定义（[x,y]{Test:...} 与 txt 导入同格式）+ 自定义类型注册表随图
        if (gridMgr.grids != null)
        {
            foreach (var grid in gridMgr.grids)
            {
                if (grid == null) continue;
                string gDiff = SerializeGridCustoms(grid);
                if (!string.IsNullOrEmpty(gDiff))
                    mapData.gridCustoms.Add($"[{grid.GridIndex.X},{grid.GridIndex.Y}]{gDiff}");
            }
        }
        if (CustomMoveTypes != null) mapData.customMoveTypes = new List<CustomMoveTypeData>(CustomMoveTypes.Values);
        if (CustomTerrainTypes != null) mapData.customTerrainTypes = new List<CustomTerrainTypeData>(CustomTerrainTypes.Values);

        Data.savedMaps[slotIndex] = mapData;
        SaveAllData();
        return true;
    }

    // ✅ 格子自定义序列化：new Grids() 模板 + 基础类型过滤（与 TerrainEditor.GetGridCustomsDiff 同格式同语义；两处独立实现，互改需同步）
    private string SerializeGridCustoms(Grids grid)
    {
        if (grid == null) return "";
        var template = new Grids();
        var diffs = new List<string>();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        foreach (var f in grid.GetType().GetFields(flags))
        {
            if (f.GetCustomAttribute<ExportAttribute>() == null || !IsBasicType(f.FieldType)) continue;
            object v1 = f.GetValue(grid), v2 = f.GetValue(template);
            if (!GridValuesEqual(v1, v2)) diffs.Add($"{f.Name}:{FormatGridValue(v1)}");
        }
        foreach (var p in grid.GetType().GetProperties(flags))
        {
            if (p.GetCustomAttribute<ExportAttribute>() == null || !p.CanRead || !p.CanWrite || !IsBasicType(p.PropertyType)) continue;
            object v1 = p.GetValue(grid), v2 = p.GetValue(template);
            if (!GridValuesEqual(v1, v2)) diffs.Add($"{p.Name}:{FormatGridValue(v1)}");
        }
        return diffs.Count == 0 ? "" : "{Test:true;" + string.Join(";", diffs) + "}";
    }

    private static bool IsBasicType(Type t)
        => t.IsEnum || t == typeof(bool) || t == typeof(int) || t == typeof(uint) || t == typeof(long)
        || t == typeof(float) || t == typeof(double) || t == typeof(string)
        || t == typeof(Color) || t == typeof(Vector2) || t == typeof(Vector2I) || t == typeof(Vector3) || t == typeof(Vector3I);

    private static bool GridValuesEqual(object a, object b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.GetType() == b.GetType() && a.Equals(b);
    }

    private static string FormatGridValue(object val)
    {
        if (val == null) return "null";
        if (val is bool b) return b ? "true" : "false";
        if (val is float f) return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (val is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return val.ToString();
    }

    private Grids FindGridByIndex(GridManager gridMgr, string xStr, string yStr)
    {
        if (int.TryParse(xStr, out int x) && int.TryParse(yStr, out int y))
        {
            if (x >= 0 && x < gridMgr.map.GetLength(0) && y >= 0 && y < gridMgr.map.GetLength(1))
                return gridMgr.map[x, y];
        }
        return null;
    }

    public SavedMapData GetMapData(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= Data.savedMaps.Count) return null;
        return Data.savedMaps[slotIndex];
    }
}
