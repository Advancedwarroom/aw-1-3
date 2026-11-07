// EditorSaveSlots.cs - 编辑器保存槽位核心逻辑（无UI，由CustomConfigUI调用）
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public partial class EditorSaveSlots : Node
{
    [Export] public ExtraSave extraSave;
    [Export] public TerrainEditor terrainEditor;
    [Export] public GridManager gridManager;
    [Export] public GameManager gameManager;

    // 等待点击目标保存
    private int pendingSaveSlotIndex = -1;
    private string pendingSaveType = "";
    public bool IsWaitingForSaveTarget => pendingSaveSlotIndex >= 0;

    // 等待点击目标加载
    private int pendingLoadSlotIndex = -1;
    private string pendingLoadType = "";
    public bool IsWaitingForLoadTarget => pendingLoadSlotIndex >= 0;

    // 数据变化通知（供CustomConfigUI订阅刷新）
    [Signal] public delegate void SlotDataChangedEventHandler(string type);

    public override void _Ready()
    {
        AddToGroup("editor_save_slots");
        if (extraSave == null)
            extraSave = GetTree().GetFirstNodeInGroup("extra_save") as ExtraSave;
        if (terrainEditor == null)
            terrainEditor = GetTree().GetFirstNodeInGroup("terrain_editor") as TerrainEditor;
        if (gridManager == null)
            gridManager = GetTree().GetFirstNodeInGroup("grid_manager") as GridManager;
        if (gameManager == null)
            gameManager = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
    }

    // ========== 保存/加载等待模式 ==========
    public void SaveToSlot(int index, string type)
    {
        pendingSaveSlotIndex = index;
        pendingSaveType = type;
        var typeName = type switch { "unit" => TranslateGroup.T("单位"), "weapon" => TranslateGroup.T("兵器"), "facility" => TranslateGroup.T("设施"), "grid" => TranslateGroup.T("地形"), _ => type };
        if (terrainEditor != null)
            terrainEditor.spawnHintLabel.Text = $"💾 {TranslateGroup.T("请点击地图上的")}{typeName}{TranslateGroup.T("来保存到槽位")} {index + 1}";
    }

    public void OnGridClickedForSave(Grids grid)
    {
        if (pendingSaveSlotIndex < 0 || extraSave == null) return;
        var data = extraSave.Data;
        int index = pendingSaveSlotIndex;
        string type = pendingSaveType;

        switch (type)
        {
            case "unit":
                if (grid.infantry != null)
                {
                    var saved = SerializeUnitToSlot(grid.infantry, index);
                    EnsureListSize(data.savedUnits, ExtraSave.MAX_SLOTS);
                    data.savedUnits[index] = saved;
                    if (terrainEditor != null)
                        terrainEditor.spawnHintLabel.Text = $"✅ {TranslateGroup.T("单位已保存到槽位")} {index + 1}";
                }
                break;
            case "weapon":
                if (grid.weapon != null)
                {
                    var saved = SerializeWeaponToSlot(grid.weapon, index);
                    EnsureListSize(data.savedWeapons, ExtraSave.MAX_SLOTS);
                    data.savedWeapons[index] = saved;
                    if (terrainEditor != null)
                        terrainEditor.spawnHintLabel.Text = $"✅ {TranslateGroup.T("兵器已保存到槽位")} {index + 1}";
                }
                break;
            case "facility":
                if (grid.city != null)
                {
                    var saved = SerializeFacilityToSlot(grid.city, index);
                    EnsureListSize(data.savedFacilities, ExtraSave.MAX_SLOTS);
                    data.savedFacilities[index] = saved;
                    if (terrainEditor != null)
                        terrainEditor.spawnHintLabel.Text = $"✅ {TranslateGroup.T("设施已保存到槽位")} {index + 1}";
                }
                break;
            case "grid":
                {
                    var saved = SerializeGridToSlot(grid, index);
                    EnsureListSize(data.savedGridTerrains, ExtraSave.MAX_SLOTS);
                    data.savedGridTerrains[index] = saved;
                    if (terrainEditor != null)
                        terrainEditor.spawnHintLabel.Text = $"✅ {TranslateGroup.T("地形已保存到槽位")} {index + 1}";
                }
                break;
        }

        pendingSaveSlotIndex = -1;
        pendingSaveType = "";
        extraSave.SaveAllData();
        EmitSignal(SignalName.SlotDataChanged, type);
    }

    public void LoadFromSlot(int index, string type)
    {
        pendingLoadSlotIndex = index;
        pendingLoadType = type;
        var typeName = type switch { "unit" => TranslateGroup.T("单位"), "weapon" => TranslateGroup.T("兵器"), "facility" => TranslateGroup.T("设施"), "grid" => TranslateGroup.T("地形"), _ => type };
        if (terrainEditor != null)
            terrainEditor.spawnHintLabel.Text = $"📂 {TranslateGroup.T("请点击地图上的格子来加载")}{typeName}{TranslateGroup.T("到槽位")} {index + 1}";
    }

    public void OnGridClickedForLoad(Grids grid)
    {
        if (pendingLoadSlotIndex < 0 || extraSave == null) return;
        var data = extraSave.Data;
        int index = pendingLoadSlotIndex;
        string type = pendingLoadType;

        switch (type)
        {
            case "unit":
                if (index < data.savedUnits.Count && data.savedUnits[index] != null)
                    SpawnUnitFromSlot(data.savedUnits[index], grid);
                break;
            case "weapon":
                if (index < data.savedWeapons.Count && data.savedWeapons[index] != null)
                    SpawnWeaponFromSlot(data.savedWeapons[index], grid);
                break;
            case "facility":
                if (index < data.savedFacilities.Count && data.savedFacilities[index] != null)
                    SpawnFacilityFromSlot(data.savedFacilities[index], grid);
                break;
            case "grid":
                if (index < data.savedGridTerrains.Count && data.savedGridTerrains[index] != null)
                    ApplyGridTerrainFromSlot(data.savedGridTerrains[index], grid);
                break;
        }

        pendingLoadSlotIndex = -1;
        pendingLoadType = "";
        extraSave.SaveAllData();
        EmitSignal(SignalName.SlotDataChanged, type);
        if (terrainEditor != null)
            terrainEditor.spawnHintLabel.Text = $"✅ {TranslateGroup.T("已加载到地图")}";
    }

    // ========== 地图保存/加载（public，供CustomConfigUI调用） ==========
    public void SaveMapToSlot(int index)
    {
        if (extraSave == null || gameManager == null || gridManager == null) return;
        extraSave.SaveCurrentMap(index, "", gameManager, gridManager);
        EmitSignal(SignalName.SlotDataChanged, "map");
    }

    public void LoadMapFromSlot(int index)
    {
        if (extraSave == null || gameManager == null || gridManager == null || terrainEditor == null) return;
        extraSave.LoadMapToGame(index, gameManager, gridManager, terrainEditor);
        EmitSignal(SignalName.SlotDataChanged, "map");
    }

    public void ClearSlot(int index, string type)
    {
        if (extraSave == null) return;
        var data = extraSave.Data;
        switch (type)
        {
            case "unit":
                if (index < data.savedUnits.Count) data.savedUnits[index] = null;
                data.favoriteUnitSlots.Remove($"unit_{index}");
                break;
            case "weapon":
                if (index < data.savedWeapons.Count) data.savedWeapons[index] = null;
                data.favoriteWeaponSlots.Remove($"weapon_{index}");
                break;
            case "facility":
                if (index < data.savedFacilities.Count) data.savedFacilities[index] = null;
                data.favoriteFacilitySlots.Remove($"facility_{index}");
                break;
            case "grid":
                if (index < data.savedGridTerrains.Count) data.savedGridTerrains[index] = null;
                data.favoriteGridSlots.Remove($"grid_{index}");
                break;
        }
        extraSave.SaveAllData();
        EmitSignal(SignalName.SlotDataChanged, type);
    }

    public void ClearMapSlot(int index)
    {
        if (extraSave == null) return;
        if (index < extraSave.Data.savedMaps.Count)
            extraSave.Data.savedMaps[index] = null;
        extraSave.SaveAllData();
        EmitSignal(SignalName.SlotDataChanged, "map");
    }

    // ========== 收藏 ==========
    public bool IsSlotFavorite(int index, string type)
    {
        if (extraSave == null) return false;
        var key = $"{type}_{index}";
        return type switch
        {
            "unit" => extraSave.Data.favoriteUnitSlots.Contains(key),
            "weapon" => extraSave.Data.favoriteWeaponSlots.Contains(key),
            "facility" => extraSave.Data.favoriteFacilitySlots.Contains(key),
            "grid" => extraSave.Data.favoriteGridSlots.Contains(key),
            _ => false
        };
    }

    public void ToggleFavorite(int index, string type)
    {
        if (extraSave == null) return;
        var key = $"{type}_{index}";
        var list = type switch
        {
            "unit" => extraSave.Data.favoriteUnitSlots,
            "weapon" => extraSave.Data.favoriteWeaponSlots,
            "facility" => extraSave.Data.favoriteFacilitySlots,
            "grid" => extraSave.Data.favoriteGridSlots,
            _ => null
        };
        if (list == null) return;

        if (list.Contains(key))
        {
            list.Remove(key);
        }
        else
        {
            if (list.Count >= ExtraSave.MAX_FAVORITES)
            {
                GD.PrintErr(TranslateGroup.T("[EditorSaveSlots] 收藏已达上限"));
                return;
            }
            list.Add(key);
        }
        extraSave.SaveAllData();
        EmitSignal(SignalName.SlotDataChanged, type);
    }

    // ========== 显示名称/时间 ==========
    public string GetSlotDisplayName(object slotData)
    {
        return slotData switch
        {
            SavedUnitData u => u.displayName,
            SavedWeaponData w => w.displayName,
            SavedFacilityData f => f.displayName,
            SavedGridTerrainData g => g.displayName,
            SavedMapData m => m.displayName,
            _ => TranslateGroup.T("未知")
        };
    }

    public string GetSlotTimeString(object slotData)
    {
        long ts = slotData switch
        {
            SavedUnitData u => u.savedTimestamp,
            SavedWeaponData w => w.savedTimestamp,
            SavedFacilityData f => f.savedTimestamp,
            SavedGridTerrainData g => g.savedTimestamp,
            SavedMapData m => m.savedTimestamp,
            _ => 0
        };
        return ts > 0 ? DateTimeOffset.FromUnixTimeSeconds(ts).ToString("MM-dd HH:mm") : "";
    }

    // ========== 工具方法 ==========
    private void EnsureListSize<T>(List<T> list, int size) where T : class
    {
        while (list.Count < size) list.Add(null);
    }

    // ========== 序列化 ==========
    private SavedUnitData SerializeUnitToSlot(Infantry unit, int slotIndex)
    {
        return new SavedUnitData
        {
            slotName = $"unit_{slotIndex}",
            displayName = unit.Name ?? unit.GetType().Name,
            scenePath = unit.SceneFilePath,
            typeName = unit.GetType().Name,
            team = unit.team,
            savedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            lastUsedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            fieldValues = SerializeObjectFields(unit)
        };
    }

    private SavedWeaponData SerializeWeaponToSlot(Weapon weapon, int slotIndex)
    {
        var data = new SavedWeaponData
        {
            slotName = $"weapon_{slotIndex}",
            displayName = weapon.Name ?? weapon.GetType().Name,
            scenePath = weapon.SceneFilePath,
            typeName = weapon.GetType().Name,
            team = weapon.team,
            savedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            lastUsedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            isMultiTile = weapon.isMultiTile,
            sizeX = weapon.size.X,
            sizeY = weapon.size.Y,
            fieldValues = SerializeObjectFields(weapon)
        };
        // ✅ direction 可能是字段（BlackCannon）也可能是属性（SuperFactory）——property 时 GetField 返回 null 会静默丢方向
        var dirField = weapon.GetType().GetField("direction", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (dirField != null)
            data.direction = (int)dirField.GetValue(weapon);
        else
        {
            var dirProp = weapon.GetType().GetProperty("direction", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (dirProp != null && dirProp.PropertyType == typeof(BlackCannon.CannonDirection))
                data.direction = (int)dirProp.GetValue(weapon);
        }
        return data;
    }

    private SavedFacilityData SerializeFacilityToSlot(Facility facility, int slotIndex)
    {
        return new SavedFacilityData
        {
            slotName = $"facility_{slotIndex}",
            displayName = facility.Name ?? facility.GetType().Name,
            scenePath = facility.SceneFilePath,
            typeName = facility.GetType().Name,
            team = facility.facilityTeam,
            savedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            lastUsedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            fieldValues = SerializeObjectFields(facility)
        };
    }

    private SavedGridTerrainData SerializeGridToSlot(Grids grid, int slotIndex)
    {
        return new SavedGridTerrainData
        {
            slotName = $"grid_{slotIndex}",
            displayName = grid.gridType.ToString(),
            gridType = grid.gridType,
            canDestroyUnit = grid.canDestroyUnit,
            fixedDamagePerTurn = grid.fixedDamagePerTurn,
            fixedAttackPerTurn = grid.fixedAttackPerTurn,
            fixedMaxHealthChangePerTurn = grid.fixedMaxHealthChangePerTurn, // ✦优化：最大血量增减
            visionBonus = grid.visionBonus,
            savedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            lastUsedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds()
        };
    }

    private SavedUnitData SerializeUnitFull(Infantry unit, Vector2I pos)
    {
        var data = SerializeUnitToSlot(unit, 0);
        data.positionX = pos.X;
        data.positionY = pos.Y;
        data.fieldValues["gridX"] = pos.X.ToString();
        data.fieldValues["gridY"] = pos.Y.ToString();
        return data;
    }

    private SavedWeaponData SerializeWeaponFull(Weapon weapon, Vector2I pos)
    {
        var data = SerializeWeaponToSlot(weapon, 0);
        data.positionX = pos.X;
        data.positionY = pos.Y;
        data.fieldValues["gridX"] = pos.X.ToString();
        data.fieldValues["gridY"] = pos.Y.ToString();
        return data;
    }

    private SavedFacilityData SerializeFacilityFull(Facility facility, Vector2I pos)
    {
        var data = SerializeFacilityToSlot(facility, 0);
        data.positionX = pos.X;
        data.positionY = pos.Y;
        data.fieldValues["gridX"] = pos.X.ToString();
        data.fieldValues["gridY"] = pos.Y.ToString();
        return data;
    }

    // ========== 反序列化/生成 ==========
    private void SpawnUnitFromSlot(SavedUnitData data, Grids grid)
    {
        if (terrainEditor != null)
        {
            terrainEditor.SpawnSavedUnitAtGrid(grid, data);
        }
        else
        {
            if (string.IsNullOrEmpty(data.scenePath)) return;
            var scene = GD.Load<PackedScene>(data.scenePath);
            if (scene == null) return;
            var unit = scene.Instantiate<Infantry>();
            if (unit == null) return;
            unit.team = data.team;
            grid.AddChild(unit);
            grid.infantry = unit;
            grid.infantries.Add(unit);
            unit.GlobalPosition = grid.GlobalPosition;
        }
        data.lastUsedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
    }

    private void SpawnWeaponFromSlot(SavedWeaponData data, Grids grid)
    {
        if (terrainEditor != null)
        {
            terrainEditor.SpawnSavedWeaponAtGrid(grid, data);
        }
        else
        {
            if (string.IsNullOrEmpty(data.scenePath)) return;
            var scene = GD.Load<PackedScene>(data.scenePath);
            if (scene == null) return;
            var weapon = scene.Instantiate<Weapon>();
            if (weapon == null) return;
            weapon.team = data.team;
            weapon.isMultiTile = data.isMultiTile;
            weapon.size.X = data.sizeX;
            weapon.size.Y = data.sizeY;
            grid.AddChild(weapon);
            grid.weapon = weapon;
            grid.weapons.Add(weapon);
            weapon.GlobalPosition = grid.GlobalPosition;
        }
        data.lastUsedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
    }

    private void SpawnFacilityFromSlot(SavedFacilityData data, Grids grid)
    {
        if (terrainEditor != null)
        {
            terrainEditor.SpawnSavedFacilityAtGrid(grid, data);
        }
        else
        {
            if (string.IsNullOrEmpty(data.scenePath)) return;
            var scene = GD.Load<PackedScene>(data.scenePath);
            if (scene == null) return;
            var facility = scene.Instantiate<Facility>();
            if (facility == null) return;
            facility.facilityTeam = data.team;
            grid.AddChild(facility);
            grid.city = facility;
            facility.GlobalPosition = grid.GlobalPosition;
        }
        data.lastUsedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
    }

    private void ApplyGridTerrainFromSlot(SavedGridTerrainData data, Grids grid)
    {
        grid.gridType = data.gridType;
        grid.canDestroyUnit = data.canDestroyUnit;
        grid.fixedDamagePerTurn = data.fixedDamagePerTurn;
        grid.fixedAttackPerTurn = data.fixedAttackPerTurn;
        grid.fixedMaxHealthChangePerTurn = data.fixedMaxHealthChangePerTurn;
        grid.visionBonus = data.visionBonus;
        grid.UpdateTerrainIcon();
        data.lastUsedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
    }

    public void CancelPendingSave()
    {
        pendingSaveSlotIndex = -1;
        pendingSaveType = "";
    }

    public void CancelPendingLoad()
    {
        pendingLoadSlotIndex = -1;
        pendingLoadType = "";
    }

    // ========== 字段序列化工具（从 ExtraSave 复制） ==========
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
            catch (System.Exception ex) { GD.PushWarning($"[EditorSaveSlots] {ex.GetType().Name}: {ex.Message}"); }
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
            catch (System.Exception ex) { GD.PushWarning($"[EditorSaveSlots] {ex.GetType().Name}: {ex.Message}"); }
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
}
