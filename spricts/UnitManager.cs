// UnitManager.cs - 通用单位管理器
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class UnitManager : Node
{
    [Export] public GridManager gridManager;
    [Export] public Node units;  // 存放单位的父节点
    [Export] public bool allowUnitOverlap = false;


    public List<Infantry> AllUnits { get; private set; } = new List<Infantry>();

    /// <summary>全部存活步兵(巨雷EMP/吞噬链用, 别名AllUnits)</summary>
    public List<Infantry> AllInfantries => AllUnits;

    public override void _Ready()
    {
        if (gridManager == null)
            gridManager = GetTree().GetFirstNodeInGroup("grid_manager") as GridManager;

        // 获取 Units 节点
        units = GetNodeOrNull("Units");
        if (units == null)
        {
            return;
        }

        // 初始化已有单位
        RefreshUnitList();
    }

    // 刷新单位列表（从场景中收集）
public void RefreshUnitList()
{
    AllUnits.Clear();
    
    foreach (var child in units.GetChildren())
    {
        // ✅ 跳过已销毁的对象
        if (!IsInstanceValid(child)) continue;
        
        // ✅ 同时检查 Infantry 和它的子类 Oozium
        if (child is Infantry unit)
        {
            if (!IsInstanceValid(unit)) continue;
            
            AllUnits.Add(unit);
            BindUnitToGrid(unit);
        }
    }
}

    // 绑定单位到格子
public bool BindUnitToGrid(Infantry unit, bool forceRebind = false)
{
    if (unit == null || !IsInstanceValid(unit)) return false;

    // 如果已有格子且不是强制重新绑定，跳过
    if (unit.grid != null && !forceRebind) return true;

    // ✅ 修复：先保存旧格子引用，用于后续清理
    var oldGrid = unit.grid;

    // 计算格子坐标
    Vector2I gridPos = WorldToGrid(unit.Position);

    // 检查是否在地图范围内
    if (!IsValidGrid(gridPos))
    {
        return false;
    }

    var targetGrid = gridManager.map[gridPos.X, gridPos.Y];
    if (targetGrid == null)
    {
        return false;
    }

    // ✅ issue#1 多格机动单位：任何单位不可绑定到他方多格足印占据格
    if (targetGrid.multiTileOccupant != null && targetGrid.multiTileOccupant != unit
        && IsInstanceValid(targetGrid.multiTileOccupant) && !targetGrid.multiTileOccupant.IsQueuedForDeletion())
    {
        return false;
    }

    // ✅ issue#1 多格机动单位：足印预检（越界/真空/他方足印重叠→拒绝，避免绑到一半卡死）
    if (unit != null && unit.isMultiTileUnit)
    {
        var gmPre = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (!unit.CheckFootprintFit(gridPos, gmPre)) return false;
    }

    // ✅ 修复：Overlapping 和 Oozium 类型可以共存，只有 NonOverlapping 才检查冲突
    if (unit.overlapType == UnitOverlapType.NonOverlapping && 
        targetGrid.infantries.Count > 0)
    {
        // 检查是否已经有其他 NonOverlapping 单位
        var blockingUnit = targetGrid.infantries.FirstOrDefault(u => 
            u != unit && 
            u.overlapType == UnitOverlapType.NonOverlapping);
        
        if (blockingUnit != null)
        {
            return false;
        }
    }

    // ✅ 修复：只有当单位真的换格子时才清理和重新绑定
    if (oldGrid != targetGrid)
    {
        // 从旧格子移除
        if (oldGrid != null)
        {
            oldGrid.infantries.Remove(unit);
            if (oldGrid.infantry == unit)
            {
                oldGrid.infantry = oldGrid.infantries.Count > 0 ? oldGrid.infantries[0] : null;
            }
        }

        // 绑定到新格子
        unit.grid = targetGrid;
        if (!targetGrid.infantries.Contains(unit))
        {
            targetGrid.infantries.Add(unit);
        }
        
        // 更新主单位引用（只有 NonOverlapping 才设为唯一主单位）
        if (unit.overlapType == UnitOverlapType.NonOverlapping)
        {
            targetGrid.infantry = unit;
        }
        else if (targetGrid.infantry == null)
        {
            // Overlapping/Oozium 只有在没有主单位时才设为主单位
            targetGrid.infantry = unit;
        }
    }
    else
    {
        // ✅ 修复：同一个格子，确保单位在列表中
        if (!targetGrid.infantries.Contains(unit))
        {
            targetGrid.infantries.Add(unit);
        }
        unit.grid = targetGrid;
    }

    // 对齐位置到格子中心
    unit.Position = targetGrid.Position;

    // ✅ 新增：绑定成功后，通知GameManager更新统计
    var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
    if (gm != null)
    {
        if (!gm.unitCategories.ContainsKey(unit))
        {
            gm.CallDeferred(nameof(gm.RefreshSpecializedUnitLists));
        }
        gm.CallDeferred(nameof(gm.UpdateUnitLists));
    }

    // ✅ issue#1 多格机动单位：绑定成功后占据足印（锚点格已在 infantries，侧格走 multiTileOccupant）
    if (unit.isMultiTileUnit)
    {
        unit.OccupyFootprint(gm);
        unit.CaptureSpriteBaseX();   // ✅ issue#1：捕获贴图基准X（行走 FlipH 偏移补偿用）

        // ✅ issue#1 可选碾压：移动落脚时碾碎足印侧格上的敌方单格单位（复用 GrandBolt 蠕动碾压语义）
        if (unit.crushOccupantsOnMove && unit.footprintGrids != null)
        {
            foreach (var fg in unit.footprintGrids)
            {
                if (fg == null || fg == unit.grid) continue;
                foreach (var victim in (fg.infantries ?? new List<Infantry>()).ToList())
                {
                    if (victim == null || !IsInstanceValid(victim)) continue;
                    if (!TeamHelper.IsEnemyForAttacker(unit.team, victim.team)) continue;
                    victim.health = 0;
                    victim.UpdateHpLabel();
                    if (!victim.TryFlee())
                    {
                        gm.RemovePiece(victim);
                        victim.CallDeferred(Node.MethodName.QueueFree);
                    }
                }
            }
        }
    }

    return true;
}


   public Infantry SpawnUnit(PackedScene unitScene, Vector2I gridPos, string team = "Player1")
{
    if (!IsValidGrid(gridPos))
    {
        return null;
    }
    
    var grid = gridManager.map[gridPos.X, gridPos.Y];
    if (grid == null)
    {
        return null;
    }
    
    var unit = unitScene.Instantiate<Infantry>();
    if (unit == null)
    {
        return null;
    }
    
    unit.Position = grid.Position;
    unit.team = team;
    string typeName = unit.UnitTypeId;
    unit.Name = $"{team}_{typeName}_{AllUnits.Count}";
    
    units.AddChild(unit);
    
    if (BindUnitToGrid(unit))
    {
        AllUnits.Add(unit);
        
        // 注册点击事件
        var gameManager = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gameManager != null)
        {
            unit.OnClickPiece = gameManager.OnSelectPiece;
        }
        
        return unit;
    }
    else
    {
        unit.QueueFree();
        return null;
    }
}
   
    // 移除单位
   public void RemoveUnit(Infantry unit)
{
    if (unit == null) return;
    unit.OnDestroyed();
    // 从格子解绑
    unit.ClearFootprint();   // ✅ issue#1 多格机动单位：死亡/移除时清足印侧格引用
    if (unit.grid != null)
    {
        unit.grid.infantries.Remove(unit);
        if (unit.grid.infantry == unit)
        {
            unit.grid.infantry = unit.grid.infantries.Count > 0 ? unit.grid.infantries[0] : null;
        }
        unit.grid = null;
    }
    
    // 从列表移除
    AllUnits.Remove(unit);
    
    // ✅ 新增：通知GameManager更新统计
    var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
    gm?.CallDeferred(nameof(gm.RefreshSpecializedUnitLists));
    gm?.CallDeferred(nameof(gm.UpdateUnitLists));
    
    // 销毁节点
    unit.QueueFree();
} 


    
    // 获取格子上的所有单位
    public List<Infantry> GetUnitsAt(Vector2I gridPos)
    {
        if (!IsValidGrid(gridPos)) return new List<Infantry>();
        var grid = gridManager.map[gridPos.X, gridPos.Y];
        return grid?.infantries.ToList() ?? new List<Infantry>();
    }
    
    // 获取某队伍的所有单位
    public List<Infantry> GetUnitsByTeam(string team)
    {
        return AllUnits.Where(u => u.team == team).ToList();
    }
    
    
    // 世界坐标转格子坐标
    public Vector2I WorldToGrid(Vector2 worldPos)
    {
        int x = (int)(worldPos.X - gridManager.startPos.X) / gridManager.gridSize.X;
        int y = (int)(worldPos.Y - gridManager.startPos.Y) / gridManager.gridSize.Y;
        return new Vector2I(x, y);
    }
    
    // 格子坐标转世界坐标
    public Vector2 GridToWorld(Vector2I gridPos)
    {
        return new Vector2(
            gridPos.X * gridManager.gridSize.X + gridManager.startPos.X,
            gridPos.Y * gridManager.gridSize.Y + gridManager.startPos.Y
        );
    }
    
    // 检查格子是否有效
    public bool IsValidGrid(Vector2I gridPos)
    {
        return gridPos.X >= 0 && gridPos.X < gridManager.searchRange.X &&
               gridPos.Y >= 0 && gridPos.Y < gridManager.searchRange.Y;
    }
    

public bool CanMoveTo(Vector2I gridPos, Infantry unit)
{
    if (!IsValidGrid(gridPos)) return false;
    
    var grid = gridManager.map[gridPos.X, gridPos.Y];
        // ✅ 隐身重叠方式2=不可重叠：隐身时拒绝进入被占格
        if (unit != null && unit.isStealthed && unit.stealthOverlapMode == 2 && grid != null
            && (grid.infantries.Count > 0 || grid.weapons.Count > 0))
            return false;
    if (grid == null) return false;
    // ✅ 使用 GetMoveCost 检查地形是否可通行（替代硬编码的 METEORITE 检查）
    if (unit != null)
    {
        int moveCost = unit.GetMoveCostOnGrid(grid);
        if (moveCost >= 999 || moveCost == int.MaxValue)
            return false; // 该单位无法通过这种地形
    }
    else
    {
        // 如果没有单位信息，保守检查：陨石不可通行
        if (grid.gridType == GridType.METEORITE) return false;
    }

    // ✅ 兵器阻挡（非Oozium不能通过兵器格子）
    if (unit is not Oozium && grid.weapon != null && !grid.weapon.AllowsPassage(unit) && !grid.weapon.CanShelterUnit(unit))
        return false;
    // ✅ issue#1 多格机动单位：他方多格足印占据格一律不可进入（敌我皆挡；死亡残留惰性失效）
    if (grid.multiTileOccupant != null && grid.multiTileOccupant != unit
        && IsInstanceValid(grid.multiTileOccupant) && !grid.multiTileOccupant.IsQueuedForDeletion())
        return false;
    
    // ✅ 友方单位不阻挡路径，敌方单位阻挡在GridManager.CalculateMoveRangeGeneric中处理
    return true;
}
}
