using Godot;
using System;

// Facility · Mine —— 矿藏设施模块（开创人 2026-11-25）
// "会死的矿"：有库存的限时油田。拥有者每次结算收入时扣库存、枯竭即停产、设施消失并原地变地形。
// 灵感血统：《潮汐战线》陨星矿 —— 它做成内容，这里做成物种（每个数值都是 Export）。
public partial class Facility
{
    [ExportGroup("矿藏设施模块（Mine · 开创人2026-11-25）")]
    [Export] public bool mineEnabled = false;                    // 是否为可开采矿藏
    [Export] public int mineResourcePerTurn = 1;               // 每回合产出份数（默认1）
    [Export] public int mineStockMin = 5;                      // 开局总量随机下限（如5-10）
    [Export] public int mineStockMax = 10;                     // 开局总量随机上限
    [Export] public int mineResourceFundsValue = 0;            // 每份资源折算资金（0=纯消耗型）
    [Export] public string mineDepletedTerrain = "Mountain";   // 枯竭后变为此地形（GridType名，空=不变）
    [Export] public int mineStock = -1;                        // 运行时库存（-1=未初始化；存档随Export持久化）
}

public partial class GameManager
{
    // ========== 矿藏 tick：拥有者收入结算时调用（库存扣减→变现→枯竭） ==========
    public void TickMine(Facility mine, Grids grid, string team)
    {
        if (mine == null || grid == null) return;
        if (mine.mineStock < 0)
            mine.mineStock = (int)GD.RandRange(mine.mineStockMin, mine.mineStockMax + 1);   // 开局掷总量
        if (mine.mineStock <= 0) return;
        int produced = Mathf.Min(Mathf.Max(1, mine.mineResourcePerTurn), mine.mineStock);
        mine.mineStock = Mathf.Max(0, mine.mineStock - produced);
        // 资源变现：每份折算资金
        if (mine.mineResourceFundsValue != 0 && !string.IsNullOrWhiteSpace(team))
            SetTeamFunds(team, GetTeamFunds(team) + produced * mine.mineResourceFundsValue);
        if (mine.mineStock == 0)
            DepleteMine(mine, grid);
    }

    // ========== 枯竭：设施消失＋地形变形（"地图会死"的核心手） ==========
    public void DepleteMine(Facility mine, Grids grid)
    {
        if (mine == null || grid == null) return;
        // 地形变形：采完变山地/废墟/任意 GridType
        if (!string.IsNullOrWhiteSpace(mine.mineDepletedTerrain)
            && Enum.TryParse<GridType>(mine.mineDepletedTerrain, true, out var gt))
        {
            grid.gridType = gt;
            gridManager?.RefreshAllTerrainDirections();
        }
        // 设施离场：清引用＋销毁节点（运行时路径，不走编辑删除、不触发战斗死亡事件）
        grid.city = null;
        mine.QueueFree();
        UpdateUnitLists();
    }
}
