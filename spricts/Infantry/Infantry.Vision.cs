using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · Vision（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{

// ========== 攻防表系统 ==========

// ========== ✅ 战争迷雾视野系统 ==========
[ExportGroup("战争迷雾视野")]
[Export] public int visionRange = -1;  // -1=使用配置表默认值，>=0=覆盖配置表
[Export] public bool useConfigVision = true;  // 是否使用VisionConfig配置表

// ✅ 新增：单位独立的视野加成矩阵（覆盖全局默认值）
// 格式：地形类型 → 加成值（可正可负）
[ExportGroup("单位独立地形视野加成")]
[Export] public Godot.Collections.Dictionary<GridType, int> unitTerrainVisionBonus = new();
[Export] public bool overrideGlobalTerrainBonus = false;  // 是否覆盖全局地形加成

// ✅ 新增：P-1 单位视野共享标记（Inspector可配置）
[ExportGroup("P-1 视野共享")]
[Export] public bool isPublicVisionUnit = false;  // 是否将视野共享给所有势力（P-1单位默认true）

// 运行时实际视野（由VisionConfig或Inspector覆盖计算得出）
public int ActualVisionRange 
{
    get 
    {
        // ✅ CO 模块系统·模块4（视野加成）
        int coBonus = CoModuleSystem.GetVisionBonus(this);
        if (!useConfigVision && visionRange >= 0) return Mathf.Max(0, visionRange + coBonus);
        return Mathf.Max(0, VisionConfig.GetUnitVisionRange(UnitTypeId, TemplateId) + coBonus);
    }
}

// ✅ 获取该单位在指定地形上的视野加成（优先使用单位专属矩阵）
public int GetTerrainVisionBonus(GridType gridType)
{
    if (overrideGlobalTerrainBonus && unitTerrainVisionBonus != null && unitTerrainVisionBonus.Count > 0)
    {
        if (unitTerrainVisionBonus.TryGetValue(gridType, out int bonus))
            return bonus;
        return 0; // 单位矩阵中未定义的地形=0加成
    }
    // 使用全局配置
    return VisionConfig.GetUnitTerrainVisionBonus(TemplateId, gridType);
}
}
