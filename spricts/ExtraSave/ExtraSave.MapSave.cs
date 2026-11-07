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
            freeformScreens = gridMgr.freeformScreens
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
                if (unit == null || !IsInstanceValid(unit) || unit.grid == null) continue;
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
                // 记录格子坐标用于加载
                uData.fieldValues["gridX"] = unit.grid.GridIndex.X.ToString();
                uData.fieldValues["gridY"] = unit.grid.GridIndex.Y.ToString();
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
                // 记录格子坐标用于加载
                fData.fieldValues["gridX"] = grid.GridIndex.X.ToString();
                fData.fieldValues["gridY"] = grid.GridIndex.Y.ToString();
                mapData.facilities.Add(fData);
            }
        }

        Data.savedMaps[slotIndex] = mapData;
        SaveAllData();
        return true;
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
