// MapEditorHelper.cs - 编辑器大更新：图形填充、区域填充、地形填充、覆盖全图、选区复制粘贴（矩形+不规则）、对称放置
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// MapEditorHelper · AreaFill（由 MapEditorHelper.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class MapEditorHelper
{

    // ========== 区域/地形填充状态 ==========
    public bool IsAreaFillMode { get; private set; } = false;
    public bool IsTerrainFillMode { get; private set; } = false;
    private GridType areaFillTargetTerrain = GridType.GROUND;
    private GridType areaFillNewTerrain = GridType.GROUND;
    private bool areaFillIncludeAdjacent = false;

    // ========== 区域填充（相邻相同地形） ==========
    private void StartAreaFillMode(bool includeAdjacent)
    {
        CancelAllModes();
        IsAreaFillMode = true;
        areaFillIncludeAdjacent = includeAdjacent;
        UpdateStatusHint($"{TranslateGroup.T("区域填充")}: {TranslateGroup.T("点击目标格子，相邻相同地形替换为")} {areaFillNewTerrain}");
    }

    private void StartTerrainFillMode()
    {
        CancelAllModes();
        IsTerrainFillMode = true;
        areaFillIncludeAdjacent = false;
        UpdateStatusHint($"{TranslateGroup.T("地形填充")}: {TranslateGroup.T("点击目标格子，全图相同地形替换为")} {areaFillNewTerrain}");
    }

    public void OnGridClickForAreaFill(Grids grid)
    {
        if ((!IsAreaFillMode && !IsTerrainFillMode) || grid == null || gridManager?.map == null) return;
        GridType targetType = grid.gridType;
        var toFill = new List<Grids>();
        int w = gridManager.map.GetLength(0);
        int h = gridManager.map.GetLength(1);

        if (IsTerrainFillMode)
        {
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (gridManager.map[x, y]?.gridType == targetType)
                        toFill.Add(gridManager.map[x, y]);
        }
        else
        {
            var visited = new HashSet<Vector2I>();
            var queue = new Queue<Grids>();
            queue.Enqueue(grid);
            visited.Add(grid.GridIndex);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                toFill.Add(current);
                var neighbors = new Vector2I[]
                {
                    new Vector2I(current.GridIndex.X + 1, current.GridIndex.Y),
                    new Vector2I(current.GridIndex.X - 1, current.GridIndex.Y),
                    new Vector2I(current.GridIndex.X, current.GridIndex.Y + 1),
                    new Vector2I(current.GridIndex.X, current.GridIndex.Y - 1)
                };
                foreach (var n in neighbors)
                {
                    if (n.X >= 0 && n.X < w && n.Y >= 0 && n.Y < h && !visited.Contains(n))
                    {
                        var ng = gridManager.map[n.X, n.Y];
                        if (ng != null && ng.gridType == targetType)
                        {
                            visited.Add(n);
                            queue.Enqueue(ng);
                        }
                    }
                }
            }
        }

        var batchRecord = new EditorActionRecord
        {
            actionType = IsTerrainFillMode ? EditorActionType.FillTerrain : EditorActionType.FillArea,
            subActions = new List<EditorActionRecord>()
        };
        foreach (var g in toFill)
        {
            batchRecord.subActions.Add(new EditorActionRecord
            {
                actionType = batchRecord.actionType,
                position = g.GridIndex,
                oldTerrain = g.gridType,
                newTerrain = areaFillNewTerrain
            });
            g.gridType = areaFillNewTerrain;
            g.BakeCowBrush(); // ✅ 放置/修改地形时把当前画笔风格烘焙进格子
            g.UpdateTerrainIcon();
        }
        PushAction(batchRecord);
        UpdateStatusHint($"{TranslateGroup.T("已填充")} {toFill.Count} {TranslateGroup.T("个格子")}");
        CancelAllModes();
    }
}
