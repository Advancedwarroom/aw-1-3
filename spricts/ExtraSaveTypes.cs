using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

// 由 ExtraSave.cs 拆出的共享类型

// ==================== 数据定义 ====================

[Serializable]
public class SavedUnitData
{
    public string slotName = "";
    public string displayName = "";
    public string scenePath = "";
    public string typeName = "";
    public string team = "Player1";
    public bool isFavorite = false;
    public long savedTimestamp = 0;      // 最后保存时间（修改优先级最高）
    public long lastUsedTimestamp = 0;     // 最后使用时间（加载/放置时更新）

    // 所有Export字段的序列化数据
    public Dictionary<string, string> fieldValues = new();

    // 自定义贴图路径
    public string customTexturePath = "";
    public bool useCustomTexture = false;

    // 攻防表
    public Dictionary<string, int> attackMatrix = new();
    public Dictionary<string, int> secondaryAttackMatrix = new();

    // 地图位置（用于战局地图保存）
    public int positionX = 0;
    public int positionY = 0;
}
[Serializable]
public class SavedWeaponData
{
    public string slotName = "";
    public string displayName = "";
    public string scenePath = "";
    public string typeName = "";
    public string team = "Player1";
    public bool isFavorite = false;
    public long savedTimestamp = 0;
    public long lastUsedTimestamp = 0;
    public Dictionary<string, string> fieldValues = new();
    public string customTexturePath = "";
    public bool useCustomTexture = false;
    
    // 添加这些缺失的字段
    public bool isMultiTile = false;
    public int sizeX = 1;
    public int sizeY = 1;
    public int direction = 0;

    // 地图位置（用于战局地图保存）
    public int positionX = 0;
    public int positionY = 0;
}
[Serializable]
public class SavedFacilityData
{
    public string slotName = "";
    public string displayName = "";
    public string scenePath = "";
    public string typeName = "";
    public string team = "Player0";
    public bool isFavorite = false;
    public long savedTimestamp = 0;
    public long lastUsedTimestamp = 0;
    public Dictionary<string, string> fieldValues = new();
    public string customTexturePath = "";
    public bool useCustomTexture = false;

    // 地图位置（用于战局地图保存）
    public int positionX = 0;
    public int positionY = 0;
}
[Serializable]
public class SavedGridTerrainData
{
    public string slotName = "";
    public string displayName = "";
    public bool isFavorite = false;
    public long savedTimestamp = 0;
    public long lastUsedTimestamp = 0;

    // 地形基础属性
    public GridType gridType = GridType.GROUND;
    public float terrainDefenseBonus = 0f;
    public bool canDestroyUnit = false;
    public int fixedDamagePerTurn = 0;
    public int fixedAttackPerTurn = 0;
    public bool canOverMaxHealth = false;
    public int fixedMaxHealthChangePerTurn = 0; // ✦优化：最大血量增减
    public int fixedAmmoChangePerTurn = 0;
    public bool ammoCanOverMax = false;
    public bool ammoCanReachZero = true;
    public int ammoChangePrimary = 0;       // ✦优化：全种类弹药增减
    public int ammoChangeExplode = 0;
    public int ammoChangeFlare = 0;
    public int ammoChangeStealth = 0;
    public int ammoChangeReveal = 0;
    public int ammoChangeCamo = 0;
    public int ammoChangeProduction = 0;
    public int ammoChangeConstruction = 0;
    public int ammoMaxChangePrimary = 0;    // ✦优化：全种类弹药上限增减
    public int ammoMaxChangeExplode = 0;
    public int ammoMaxChangeFlare = 0;
    public int ammoMaxChangeStealth = 0;
    public int ammoMaxChangeReveal = 0;
    public int ammoMaxChangeCamo = 0;
    public int ammoMaxChangeProduction = 0;
    public int ammoMaxChangeConstruction = 0;
    public int fixedFuelChangePerTurn = 0;
    public int fixedMaxFuelChangePerTurn = 0; // ✦优化：燃油上限增减
    public bool fuelCanOverMax = false;
    public bool fuelCanReachZero = true;
    public int gridVisionRange = 0;
    public int gridVisionMode = 0; // GridVisionMode serialized as int
    public int towerDirection = 0; // TowerDirection serialized as int
    public bool towerCanRotate = false;
    public float towerRayAngle = 45f;
    public bool gridVisionIgnoreTerrain = false;
    public int visionBonus = 0;
    public bool requiresAdjacentVision = false;
    public bool isWatchtower = false;

    // 自定义贴图
    public string customTexturePath = "";
    public bool useCustomTexture = false;
    public string terrainColorHex = "ffffff";
}
[Serializable]
public class SavedMapData
{
    public string slotName = "";
    public string displayName = "";
    public long savedTimestamp = 0;
    public long lastUsedTimestamp = 0;
    public int width = 0;
    public int height = 0;
    public bool fogOfWarEnabled = false;
    // ✅ issue#1 区域天气存档（开关+格子栈序列化串；基底已废除，全球天气只听 CO）
    public bool localWeatherEnabled = false;
    public string weatherStacks = "";

    // 地形矩阵
    public List<int> terrainIds = new();
    // 单位列表
    public List<SavedUnitData> units = new();
    // 兵器列表
    public List<SavedWeaponData> weapons = new();
    // 设施列表
    public List<SavedFacilityData> facilities = new();
    // 资金
    public int p1Funds = 0;
    public int p2Funds = 0;
}
[Serializable]
public class CustomMoveTypeData
{
    public string id = "";
    public string displayName = "";
    // key: 地形类型名, value: 移动消耗
    public Dictionary<string, int> moveCosts = new();
    public bool isBuiltIn = false;
    public string customTexturePath = "";
    public bool useCustomTexture = false;
}
[Serializable]
public class CustomTerrainTypeData
{
    public string id = "";
    public string displayName = "";
    // key: 移动方式类型名, value: 移动消耗
    public Dictionary<string, int> moveCosts = new();
    public float defenseBonus = 0f;
    public string terrainColorHex = "ffffff";
    public string customTexturePath = "";
    public bool useCustomTexture = false;
    public bool isBuiltIn = false;
}
[Serializable]
public class ExtraSaveData
{
    public int version = 2; // 版本升级
    public long lastSaveTime = 0;

    // 各10个槽位
    public List<SavedUnitData> savedUnits = new();
    public List<SavedWeaponData> savedWeapons = new();
    public List<SavedFacilityData> savedFacilities = new();
    public List<SavedGridTerrainData> savedGridTerrains = new();

    // 5个地图槽位
    public List<SavedMapData> savedMaps = new();

    // 自定义移动方式
    public List<CustomMoveTypeData> customMoveTypes = new();
    // 自定义地形
    public List<CustomTerrainTypeData> customTerrainTypes = new();

    // 收藏列表（存储slotName）
    public List<string> favoriteUnitSlots = new();
    public List<string> favoriteWeaponSlots = new();
    public List<string> favoriteFacilitySlots = new();
    public List<string> favoriteGridSlots = new();
}
