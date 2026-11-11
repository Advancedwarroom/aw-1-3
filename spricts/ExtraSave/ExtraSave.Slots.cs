using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

// ExtraSave · Slots（由 ExtraSave.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class ExtraSave
{

    // ==================== 初始化默认槽位 ====================

    private void InitializeDefaultSlots()
    {
        EnsureSlotList(ref Data.savedUnits, TranslateGroup.T("单位"));
        EnsureSlotList(ref Data.savedWeapons, TranslateGroup.T("兵器"));
        EnsureSlotList(ref Data.savedFacilities, TranslateGroup.T("设施"));
        EnsureSlotList(ref Data.savedGridTerrains, TranslateGroup.T("地形"));
        EnsureSlotList(ref Data.savedMaps, TranslateGroup.T("地图"));
    }

    private void EnsureSlotList<T>(ref List<T> list, string prefix) where T : class
    {
        int maxSlots = typeof(T) == typeof(SavedMapData) ? MAX_MAP_SLOTS : MAX_SLOTS;

        while (list.Count < maxSlots)
        {
            list.Add(null);
        }
    }

    // ==================== 保存操作（带自动淘汰） ====================

    /// <summary>
    /// 保存单位到槽位，自动淘汰最旧的（修改时间优先）
    /// </summary>
    public bool SaveUnit(Infantry unit, int slotIndex, string customName = null)
    {
        if (unit == null || slotIndex < 0 || slotIndex >= MAX_SLOTS) return false;

        var data = new SavedUnitData
        {
            slotName = $"{TranslateGroup.T("单位")}{slotIndex + 1}",
            displayName = customName ?? unit.Name ?? $"{TranslateGroup.T("单位")}{slotIndex + 1}",
            scenePath = GetScenePathForInstance(unit),
            typeName = unit.GetType().Name,
            team = unit.team,
            savedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            lastUsedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            fieldValues = SerializeObjectFields(unit),
            attackMatrix = ToSystemDict(unit.attackMatrix),
            secondaryAttackMatrix = ToSystemDict(unit.secondaryAttackMatrix)
        };

        Data.savedUnits[slotIndex] = data;
        SaveAllData();
        return true;
    }

    public bool SaveWeapon(Weapon weapon, int slotIndex, string customName = null)
    {
        if (weapon == null || slotIndex < 0 || slotIndex >= MAX_SLOTS) return false;

        var data = new SavedWeaponData
        {
            slotName = $"{TranslateGroup.T("兵器")}{slotIndex + 1}",
            displayName = customName ?? weapon.Name ?? $"{TranslateGroup.T("兵器")}{slotIndex + 1}",
            scenePath = GetScenePathForInstance(weapon),
            typeName = weapon.GetType().Name,
            team = weapon.team,
            savedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            lastUsedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            fieldValues = SerializeObjectFields(weapon)
        };

        // 保存多格兵器属性
        data.isMultiTile = weapon.isMultiTile;
        data.sizeX = weapon.size.X;
        data.sizeY = weapon.size.Y;
        if (weapon is BlackCannon bc)
            data.direction = (int)bc.direction;
        else if (weapon is DeathRay dr)
            data.direction = (int)dr.direction;
        else if (weapon is LargeCannon lc)
            data.direction = (int)lc.direction;

        Data.savedWeapons[slotIndex] = data;
        SaveAllData();
        return true;
    }

    public bool SaveFacility(Facility facility, int slotIndex, string customName = null)
    {
        if (facility == null || slotIndex < 0 || slotIndex >= MAX_SLOTS) return false;

        var data = new SavedFacilityData
        {
            slotName = $"{TranslateGroup.T("设施")}{slotIndex + 1}",
            displayName = customName ?? facility.Name ?? $"{TranslateGroup.T("设施")}{slotIndex + 1}",
            scenePath = GetScenePathForInstance(facility),
            typeName = facility.GetType().Name,
            team = facility.facilityTeam,
            savedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            lastUsedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            fieldValues = SerializeObjectFields(facility)
        };

        Data.savedFacilities[slotIndex] = data;
        SaveAllData();
        return true;
    }

    public bool SaveGridTerrain(Grids grid, int slotIndex, string customName = null)
    {
        if (grid == null || slotIndex < 0 || slotIndex >= MAX_SLOTS) return false;

        var data = new SavedGridTerrainData
        {
            slotName = $"{TranslateGroup.T("地形")}{slotIndex + 1}",
            displayName = customName ?? $"{TranslateGroup.T("地形")}{slotIndex + 1}",
            savedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            lastUsedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            gridType = grid.gridType,
            terrainDefenseBonus = grid.TerrainDefenseBonus,
            canDestroyUnit = grid.canDestroyUnit,
            fixedDamagePerTurn = grid.fixedDamagePerTurn,
            fixedAttackPerTurn = grid.fixedAttackPerTurn,
            canOverMaxHealth = grid.canOverMaxHealth,
            fixedMaxHealthChangePerTurn = grid.fixedMaxHealthChangePerTurn,
            fixedWeightChangePerTurn = grid.fixedWeightChangePerTurn,
            fixedAmmoChangePerTurn = grid.fixedAmmoChangePerTurn,
            ammoCanOverMax = grid.ammoCanOverMax,
            ammoCanReachZero = grid.ammoCanReachZero,
            ammoChangePrimary = grid.ammoChangePrimary,
            ammoChangeExplode = grid.ammoChangeExplode,
            ammoChangeFlare = grid.ammoChangeFlare,
            ammoChangeStealth = grid.ammoChangeStealth,
            ammoChangeReveal = grid.ammoChangeReveal,
            ammoChangeCamo = grid.ammoChangeCamo,
            ammoChangeProduction = grid.ammoChangeProduction,
            ammoChangeConstruction = grid.ammoChangeConstruction,
            ammoChangeSpecial = grid.ammoChangeSpecial,
            ammoChangePushRebound = grid.ammoChangePushRebound,
            ammoChangePushMitigation = grid.ammoChangePushMitigation,
            ammoChangeAntiGravityTransfer = grid.ammoChangeAntiGravityTransfer,
            ammoMaxChangePrimary = grid.ammoMaxChangePrimary,
            ammoMaxChangeExplode = grid.ammoMaxChangeExplode,
            ammoMaxChangeFlare = grid.ammoMaxChangeFlare,
            ammoMaxChangeStealth = grid.ammoMaxChangeStealth,
            ammoMaxChangeReveal = grid.ammoMaxChangeReveal,
            ammoMaxChangeCamo = grid.ammoMaxChangeCamo,
            ammoMaxChangeProduction = grid.ammoMaxChangeProduction,
            ammoMaxChangeConstruction = grid.ammoMaxChangeConstruction,
            ammoMaxChangeSpecial = grid.ammoMaxChangeSpecial,
            ammoMaxChangePushRebound = grid.ammoMaxChangePushRebound,
            ammoMaxChangePushMitigation = grid.ammoMaxChangePushMitigation,
            ammoMaxChangeAntiGravityTransfer = grid.ammoMaxChangeAntiGravityTransfer,
            fixedFuelChangePerTurn = grid.fixedFuelChangePerTurn,
            fixedMaxFuelChangePerTurn = grid.fixedMaxFuelChangePerTurn,
            fuelCanOverMax = grid.fuelCanOverMax,
            fuelCanReachZero = grid.fuelCanReachZero,
            gridVisionRange = grid.gridVisionRange,
            gridVisionMode = (int)grid.gridVisionMode,
            towerDirection = (int)grid.towerDirection,
            towerCanRotate = grid.towerCanRotate,
            towerRayAngle = grid.towerRayAngle,
            gridVisionIgnoreTerrain = grid.gridVisionIgnoreTerrain,
            visionBonus = grid.visionBonus,
            requiresAdjacentVision = grid.requiresAdjacentVision,
            isWatchtower = grid.isWatchtower
        };

        Data.savedGridTerrains[slotIndex] = data;
        SaveAllData();
        return true;
    }

    // ==================== 自动淘汰最旧的（非收藏） ====================
    /// <summary>
    /// 修改时间 > 使用时间。如果修改时间相同，比较使用时间。
    /// 返回要淘汰的槽位索引，-1表示无需淘汰（有空位）
    /// </summary>
    public int FindAutoEvictSlot<T>(List<T> list, List<string> favorites) where T : class
    {
        // 优先找空的
        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i];
            bool isEmpty = false;
            if (item is SavedUnitData u) isEmpty = string.IsNullOrEmpty(u.scenePath) && u.fieldValues.Count == 0;
            else if (item is SavedWeaponData w) isEmpty = string.IsNullOrEmpty(w.scenePath) && w.fieldValues.Count == 0;
            else if (item is SavedFacilityData f) isEmpty = string.IsNullOrEmpty(f.scenePath) && f.fieldValues.Count == 0;
            else if (item is SavedGridTerrainData g) isEmpty = g.savedTimestamp == 0;
            else if (item is SavedMapData m) isEmpty = m.width == 0;

            if (isEmpty) return i;
        }

        // 找最旧的非收藏（按 savedTimestamp 降序，修改时间优先）
        long oldestTime = long.MaxValue;
        int oldestIndex = -1;

        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i];
            string slotName = "";
            bool isFav = false;
            long timestamp = 0;
            long usedTime = 0;

            if (item is SavedUnitData u) { slotName = u.slotName; isFav = u.isFavorite; timestamp = u.savedTimestamp; usedTime = u.lastUsedTimestamp; }
            else if (item is SavedWeaponData w) { slotName = w.slotName; isFav = w.isFavorite; timestamp = w.savedTimestamp; usedTime = w.lastUsedTimestamp; }
            else if (item is SavedFacilityData f) { slotName = f.slotName; isFav = f.isFavorite; timestamp = f.savedTimestamp; usedTime = f.lastUsedTimestamp; }
            else if (item is SavedGridTerrainData g) { slotName = g.slotName; isFav = g.isFavorite; timestamp = g.savedTimestamp; usedTime = g.lastUsedTimestamp; }
            else if (item is SavedMapData m) { slotName = m.slotName; timestamp = m.savedTimestamp; usedTime = m.lastUsedTimestamp; }

            if (isFav || favorites.Contains(slotName)) continue;

            // 比较：savedTimestamp 优先，相同则比较 lastUsedTimestamp
            long effectiveTime = timestamp * 1000000000L + usedTime; // 组合时间戳
            if (effectiveTime < oldestTime) { oldestTime = effectiveTime; oldestIndex = i; }
        }

        return oldestIndex;
    }

    /// <summary>
    /// 智能保存：如果指定槽位被占用，自动找到最旧的非收藏槽位淘汰
    /// </summary>
    public bool SmartSaveUnit(Infantry unit, int preferredSlot, string customName = null)
    {
        if (unit == null) return false;

        // 检查指定槽位是否为空
        bool isEmpty = preferredSlot >= 0 && preferredSlot < MAX_SLOTS && 
            Data.savedUnits[preferredSlot].savedTimestamp == 0;

        if (!isEmpty)
        {
            // 找要淘汰的槽位
            int evictSlot = FindAutoEvictSlot(Data.savedUnits, Data.favoriteUnitSlots);
            if (evictSlot < 0) return false; // 全满了且都是收藏
            preferredSlot = evictSlot;
        }

        return SaveUnit(unit, preferredSlot, customName);
    }

    public bool SmartSaveWeapon(Weapon weapon, int preferredSlot, string customName = null)
    {
        if (weapon == null) return false;
        bool isEmpty = preferredSlot >= 0 && preferredSlot < MAX_SLOTS && 
            Data.savedWeapons[preferredSlot].savedTimestamp == 0;
        if (!isEmpty)
        {
            int evictSlot = FindAutoEvictSlot(Data.savedWeapons, Data.favoriteWeaponSlots);
            if (evictSlot < 0) return false;
            preferredSlot = evictSlot;
        }
        return SaveWeapon(weapon, preferredSlot, customName);
    }

    public bool SmartSaveFacility(Facility facility, int preferredSlot, string customName = null)
    {
        if (facility == null) return false;
        bool isEmpty = preferredSlot >= 0 && preferredSlot < MAX_SLOTS && 
            Data.savedFacilities[preferredSlot].savedTimestamp == 0;
        if (!isEmpty)
        {
            int evictSlot = FindAutoEvictSlot(Data.savedFacilities, Data.favoriteFacilitySlots);
            if (evictSlot < 0) return false;
            preferredSlot = evictSlot;
        }
        return SaveFacility(facility, preferredSlot, customName);
    }

    public bool SmartSaveGridTerrain(Grids grid, int preferredSlot, string customName = null)
    {
        if (grid == null) return false;
        bool isEmpty = preferredSlot >= 0 && preferredSlot < MAX_SLOTS && 
            Data.savedGridTerrains[preferredSlot].savedTimestamp == 0;
        if (!isEmpty)
        {
            int evictSlot = FindAutoEvictSlot(Data.savedGridTerrains, Data.favoriteGridSlots);
            if (evictSlot < 0) return false;
            preferredSlot = evictSlot;
        }
        return SaveGridTerrain(grid, preferredSlot, customName);
    }

    // ==================== 收藏系统 ====================

    public bool ToggleFavorite<T>(List<T> list, List<string> favorites, int slotIndex) where T : class
    {
        if (slotIndex < 0 || slotIndex >= list.Count) return false;

        var item = list[slotIndex];
        string slotName = "";
        if (item is SavedUnitData u) slotName = u.slotName;
        else if (item is SavedWeaponData w) slotName = w.slotName;
        else if (item is SavedFacilityData f) slotName = f.slotName;
        else if (item is SavedGridTerrainData g) slotName = g.slotName;

        if (favorites.Contains(slotName))
        {
            favorites.Remove(slotName);
            if (item is SavedUnitData u2) u2.isFavorite = false;
            else if (item is SavedWeaponData w2) w2.isFavorite = false;
            else if (item is SavedFacilityData f2) f2.isFavorite = false;
            else if (item is SavedGridTerrainData g2) g2.isFavorite = false;
        }
        else
        {
            if (favorites.Count >= MAX_FAVORITES) return false;
            favorites.Add(slotName);
            if (item is SavedUnitData u2) u2.isFavorite = true;
            else if (item is SavedWeaponData w2) w2.isFavorite = true;
            else if (item is SavedFacilityData f2) f2.isFavorite = true;
            else if (item is SavedGridTerrainData g2) g2.isFavorite = true;
        }

        SaveAllData();
        return true;
    }

    public bool ToggleUnitFavorite(int slotIndex) => ToggleFavorite(Data.savedUnits, Data.favoriteUnitSlots, slotIndex);
    public bool ToggleWeaponFavorite(int slotIndex) => ToggleFavorite(Data.savedWeapons, Data.favoriteWeaponSlots, slotIndex);
    public bool ToggleFacilityFavorite(int slotIndex) => ToggleFavorite(Data.savedFacilities, Data.favoriteFacilitySlots, slotIndex);
    public bool ToggleGridFavorite(int slotIndex) => ToggleFavorite(Data.savedGridTerrains, Data.favoriteGridSlots, slotIndex);

    // ==================== 改名 ====================

    public bool RenameUnit(int slotIndex, string newName)
    {
        if (slotIndex < 0 || slotIndex >= Data.savedUnits.Count || string.IsNullOrWhiteSpace(newName)) return false;
        Data.savedUnits[slotIndex].displayName = newName;
        Data.savedUnits[slotIndex].savedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(); // 改名也算修改
        SaveAllData();
        return true;
    }

    public bool RenameWeapon(int slotIndex, string newName)
    {
        if (slotIndex < 0 || slotIndex >= Data.savedWeapons.Count || string.IsNullOrWhiteSpace(newName)) return false;
        Data.savedWeapons[slotIndex].displayName = newName;
        Data.savedWeapons[slotIndex].savedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
        SaveAllData();
        return true;
    }

    public bool RenameFacility(int slotIndex, string newName)
    {
        if (slotIndex < 0 || slotIndex >= Data.savedFacilities.Count || string.IsNullOrWhiteSpace(newName)) return false;
        Data.savedFacilities[slotIndex].displayName = newName;
        Data.savedFacilities[slotIndex].savedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
        SaveAllData();
        return true;
    }

    public bool RenameGridTerrain(int slotIndex, string newName)
    {
        if (slotIndex < 0 || slotIndex >= Data.savedGridTerrains.Count || string.IsNullOrWhiteSpace(newName)) return false;
        Data.savedGridTerrains[slotIndex].displayName = newName;
        Data.savedGridTerrains[slotIndex].savedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
        SaveAllData();
        return true;
    }

    public bool RenameMap(int slotIndex, string newName)
    {
        if (slotIndex < 0 || slotIndex >= Data.savedMaps.Count || string.IsNullOrWhiteSpace(newName)) return false;
        Data.savedMaps[slotIndex].displayName = newName;
        Data.savedMaps[slotIndex].savedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
        SaveAllData();
        return true;
    }

    // ==================== 更新使用时间（加载/放置时调用） ====================
    public void UpdateUnitLastUsed(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < Data.savedUnits.Count)
        {
            Data.savedUnits[slotIndex].lastUsedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
            SaveAllData();
        }
    }
    public void UpdateWeaponLastUsed(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < Data.savedWeapons.Count)
        {
            Data.savedWeapons[slotIndex].lastUsedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
            SaveAllData();
        }
    }
    public void UpdateFacilityLastUsed(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < Data.savedFacilities.Count)
        {
            Data.savedFacilities[slotIndex].lastUsedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
            SaveAllData();
        }
    }
    public void UpdateGridLastUsed(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < Data.savedGridTerrains.Count)
        {
            Data.savedGridTerrains[slotIndex].lastUsedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
            SaveAllData();
        }
    }
    public void UpdateMapLastUsed(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < Data.savedMaps.Count)
        {
            Data.savedMaps[slotIndex].lastUsedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
            SaveAllData();
        }
    }

    // ==================== 获取保存数据（供外部读取） ====================
    public SavedUnitData GetSavedUnit(int slotIndex) => (slotIndex >= 0 && slotIndex < Data.savedUnits.Count) ? Data.savedUnits[slotIndex] : null;
    public SavedWeaponData GetSavedWeapon(int slotIndex) => (slotIndex >= 0 && slotIndex < Data.savedWeapons.Count) ? Data.savedWeapons[slotIndex] : null;
    public SavedFacilityData GetSavedFacility(int slotIndex) => (slotIndex >= 0 && slotIndex < Data.savedFacilities.Count) ? Data.savedFacilities[slotIndex] : null;
    public SavedGridTerrainData GetSavedGridTerrain(int slotIndex) => (slotIndex >= 0 && slotIndex < Data.savedGridTerrains.Count) ? Data.savedGridTerrains[slotIndex] : null;
    public SavedMapData GetSavedMap(int slotIndex) => (slotIndex >= 0 && slotIndex < Data.savedMaps.Count) ? Data.savedMaps[slotIndex] : null;

    // ==================== 保存数据直接获取（供TerrainEditor使用） ====================
    /// <summary>
    /// 直接获取单位保存数据（用于TerrainEditor生成）
    /// </summary>
    public SavedUnitData GetSavedUnitData(int slotIndex) => GetSavedUnit(slotIndex);
    public SavedWeaponData GetSavedWeaponData(int slotIndex) => GetSavedWeapon(slotIndex);
    public SavedFacilityData GetSavedFacilityData(int slotIndex) => GetSavedFacility(slotIndex);
    public SavedGridTerrainData GetSavedGridData(int slotIndex) => GetSavedGridTerrain(slotIndex);

    // ==================== 清空槽位 ====================
    public void ClearUnitSlot(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < Data.savedUnits.Count)
        {
            Data.savedUnits[slotIndex] = new SavedUnitData { slotName = $"{TranslateGroup.T("单位")}{slotIndex + 1}" };
            SaveAllData();
        }
    }
    public void ClearWeaponSlot(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < Data.savedWeapons.Count)
        {
            Data.savedWeapons[slotIndex] = new SavedWeaponData { slotName = $"{TranslateGroup.T("兵器")}{slotIndex + 1}" };
            SaveAllData();
        }
    }
    public void ClearFacilitySlot(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < Data.savedFacilities.Count)
        {
            Data.savedFacilities[slotIndex] = new SavedFacilityData { slotName = $"{TranslateGroup.T("设施")}{slotIndex + 1}" };
            SaveAllData();
        }
    }
    public void ClearGridSlot(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < Data.savedGridTerrains.Count)
        {
            Data.savedGridTerrains[slotIndex] = new SavedGridTerrainData { slotName = $"{TranslateGroup.T("地形")}{slotIndex + 1}" };
            SaveAllData();
        }
    }
    public void ClearMapSlot(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < Data.savedMaps.Count)
        {
            Data.savedMaps[slotIndex] = new SavedMapData { slotName = $"{TranslateGroup.T("地图")}{slotIndex + 1}" };
            SaveAllData();
        }
    }
}
