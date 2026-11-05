// GameManager.GrandBoltBridge.cs
// 巨雷(GrandBolt)桥接成员: 攻击来源格(弱点判定) + 按注册名创建单位
using Godot;

public partial class GameManager
{
    /// <summary>当前攻击者所在格(巨雷TakeDamage判定弱点命中用; Infantry.Combat攻击流程写入)</summary>
    public Grids CurrentAttackerGrid { get; set; } = null;

    /// <summary>按单位注册名在指定格子创建单位(巨雷AI/生产同款链路)</summary>
    public Infantry UnitManager_CreateUnitAt(string unitName, Vector2I index, string team)
    {
        if (unitManager == null) return null;
        if (!UnitProductionDatabase.Units.TryGetValue(unitName, out var info)) return null;
        var scene = GD.Load<PackedScene>(info.ScenePath);
        if (scene == null) return null;
        return unitManager.SpawnUnit(scene, index, team);
    }
}
