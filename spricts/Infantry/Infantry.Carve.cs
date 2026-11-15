// Infantry.Carve.cs — ✅ 开创人2026-11-15 山脉开道P1：开天辟地——把999的山咬穿成山道
using Godot;
using System;

public partial class Infantry
{
    [ExportGroup("山脉开道")]
    [Export] public bool canCarveMountains = false;      // 可开凿（矿工/开道车/犀牛工兵型）
    [Export] public int carveCostPerCell = 500;          // 资金/格（愚公也得吃饭）
    [Export] public int carveFuelPerCell = 3;            // 燃油/格
    [Export] public int carveMoveCost = 3;             // 凿出的山道移动耗
    [Export] public bool carveOnlyMountain = true;      // 仅限999山（false=任何高耗地形都能凿）

    /// <summary>开凿相邻山格：资金+燃油结算→格子永久变山道。P2接按钮/统一基底。</summary>
    public bool TryCarveMountain(Grids target)
    {
        if (!canCarveMountains || target == null || target.carvedPath) return false;
        if (grid == null) return false;
        int baseCost = GetMoveCost(target.gridType);
        if (carveOnlyMountain ? baseCost < 999 : baseCost <= 1) return false;
        int dist = Mathf.Abs(target.GridIndex.X - grid.GridIndex.X) + Mathf.Abs(target.GridIndex.Y - grid.GridIndex.Y);
        if (dist != 1) return false;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return false;
        if (gm.GetTeamFunds(team) < carveCostPerCell) return false;
        if (fuel < carveFuelPerCell) return false;
        gm.SetTeamFunds(team, gm.GetTeamFunds(team) - carveCostPerCell);
        fuel -= carveFuelPerCell;
        ActionResourceRuntime.EvalFuelMisfire(this, -carveFuelPerCell);   // ✅ A9 燃油哑火
        target.carvedPath = true;
        target.carvedMoveCost = carveMoveCost;
        return true;
    }
}
