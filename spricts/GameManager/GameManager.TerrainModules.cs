// GameManager.TerrainModules.cs
// 地形模块回合执行: Creeper扩散(冷却/全局上限/曼哈顿距离/黑名单) —— 回合开始统一处理
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class GameManager
{
    /// <summary>地形模块回合计数(每回合开始+1, 冷却按此计)</summary>
    public static int terrainModuleTurnCount = 0;

    /// <summary>回合开始: 全图遍历可扩散地形, 向四邻尝试扩散(每个源每回合最多扩一格)</summary>
    private void ProcessTerrainSpreadTurnStart()
    {
        var gm = GetTree()?.GetFirstNodeInGroup("grid_manager") as GridManager;
        if (gm?.map == null) return;
        int w = gm.map.GetLength(0), h = gm.map.GetLength(1);
        int currentTurn = ++terrainModuleTurnCount;
        var sources = new List<Grids>();
        foreach (var g in gm.grids)
            if (g != null && IsInstanceValid(g) && g.spreadEnabled) sources.Add(g);
        foreach (var src in sources)
        {
            foreach (var d in new[] { new Vector2I(1, 0), new Vector2I(-1, 0), new Vector2I(0, 1), new Vector2I(0, -1) })
            {
                int nx = src.GridIndex.X + d.X, ny = src.GridIndex.Y + d.Y;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                if (src.TrySpreadTo(gm.map[nx, ny], currentTurn, gm.map)) break; // 扩一格即止, 下回合继续
            }
        }
    }
}
