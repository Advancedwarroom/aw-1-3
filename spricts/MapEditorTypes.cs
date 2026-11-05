// MapEditorHelper.cs - 编辑器大更新：图形填充、区域填充、地形填充、覆盖全图、选区复制粘贴（矩形+不规则）、对称放置
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// 由 MapEditorHelper.cs 拆出的共享类型

public enum FillShapeType
{
    Circle, Square, Rectangle, Diamond, Triangle
}
public enum SymmetryAxis
{
    None, Vertical, Horizontal
}
public enum SelectModeType
{
    None, Rectangle, Irregular
}
// ========== 选区数据 ==========
public class MapSelection
{
    public string id;
    public bool isFavorite;
    public Rect2I bounds;
    public List<SelectionCellData> cells = new();
    public long savedTimestamp;
    public bool isIrregular;
}
public class SelectionCellData
{
    public Vector2I gridPos;
    public GridType gridType;
    public Dictionary<string, string> gridProperties = new();
    public SavedUnitData unitData;
    public SavedWeaponData weaponData;
    public SavedFacilityData facilityData;
}
// ========== 撤销操作记录 ==========
public enum EditorActionType
{
    PlaceTerrain, PlaceUnit, PlaceWeapon, PlaceFacility,
    DeleteUnit, DeleteWeapon, DeleteFacility,
    ReplaceTexture, ClearMap, FillShape, FillArea, FillTerrain,
    PasteSelection, SymmetryPlace
}
public class EditorActionRecord
{
    public EditorActionType actionType;
    public Vector2I position;
    public GridType oldTerrain;
    public GridType newTerrain;
    public SavedUnitData oldUnitData;
    public SavedWeaponData oldWeaponData;
    public SavedFacilityData oldFacilityData;
    public SavedUnitData newUnitData;
    public SavedWeaponData newWeaponData;
    public SavedFacilityData newFacilityData;
    public List<EditorActionRecord> subActions = new();
    public long timestamp;
    // ✅ 贴图锁状态(批量笔刷/洪水填充写入): null=未携带, 撤销/前进时不触碰(兼容旧记录)
    public bool? oldAutoOptimizeDirection;
    public string oldManualCowTexturePath;
    public bool? newAutoOptimizeDirection;
    public string newManualCowTexturePath;
}
