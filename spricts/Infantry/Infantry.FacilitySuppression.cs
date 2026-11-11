using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · FacilitySuppression（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{
    [ExportGroup("设施压制")]
    [Export] public bool canSuppressFacilities = false; // 压制开关：站敌楼干扰 x-y 环内设施
    [Export] public int suppressRangeMin = 0;           // x（含自身格）
    [Export] public int suppressRangeMax = 1;           // y
    [Export] public bool suppressCanCrossScreens = false; // 设施压制光环是否允许跨屏
    [Export] public int suppressIncomeMode = 0;         // 资金：0不干扰 1制止 2百分比
    [Export] public int suppressIncomePercent = 0;      // 百分比（负=减弱 正=增强）
    [Export] public int suppressDefenseMode = 0;        // 掩体：0不干扰 1制止 2百分比
    [Export] public int suppressDefensePercent = 0;
    [Export] public int suppressProductionMode = 0;     // 生产：0不干扰 1制止（百分比待接生产UI）
    [Export] public int suppressProductionPercent = 0;  // 作用于生产资金消耗
    [Export] public bool suppressSupplyStop = false;    // 补给：只制止/不制止

    // ========== 设施压制（Aura：每回合刷新施加，状态自带衰减）==========
    public virtual void OnTurnStartFacilitySuppression()
    {
        if (!canSuppressFacilities || grid == null) return;
        var gm = gameManager;
        if (gm?.gridManager?.map == null || gm?.unitManager == null) return;
        var origin = grid.GridIndex;
        for (int dx = -suppressRangeMax; dx <= suppressRangeMax; dx++)
            for (int dy = -suppressRangeMax; dy <= suppressRangeMax; dy++)
            {
                int dist = Mathf.Abs(dx) + Mathf.Abs(dy);
                if (dist < suppressRangeMin || dist > suppressRangeMax) continue;
                var pos = new Vector2I(origin.X + dx, origin.Y + dy);
                if (!gm.unitManager.IsValidGrid(pos)) continue;
                var targetGrid = gm.gridManager.map[pos.X, pos.Y];
                if (targetGrid == null || !gm.gridManager.IsRangeAllowed(grid, targetGrid, suppressCanCrossScreens)) continue;
                var f = targetGrid.city;
                if (f == null || f.facilityTeam == team) continue;   // 只压敌方建筑
                if (suppressIncomeMode == 1) f.AddStatus(FacilityStatusType.NoIncome, 2, team);
                else if (suppressIncomeMode == 2) f.SetIncomeSuppression(suppressIncomePercent);
                if (suppressProductionMode == 1) f.AddStatus(FacilityStatusType.CannotProduce, 2, team);
                else if (suppressProductionMode == 2) f.productionSuppressPercent = suppressProductionPercent;
                if (suppressDefenseMode == 1) f.AddStatus(FacilityStatusType.NoCover, 2, team);
                else if (suppressDefenseMode == 2) f.defenseSuppressPercent = suppressDefensePercent;
                if (suppressSupplyStop) f.AddStatus(FacilityStatusType.NoSupply, 2, team);
            }
    }
}
