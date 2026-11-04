// MapEditorHelper.cs - 编辑器大更新：图形填充、区域填充、地形填充、覆盖全图、选区复制粘贴（矩形+不规则）、对称放置
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// MapEditorHelper · ShapeFill（由 MapEditorHelper.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class MapEditorHelper
{

    // ========== 图形填充状态 ==========
    public bool IsShapeFillMode { get; private set; } = false;
    private FillShapeType currentFillShape = FillShapeType.Circle;
    private GridType fillShapeTerrain = GridType.GROUND;
    private Vector2I shapeAnchorPos = new Vector2I(-1, -1);
    private Vector2I shapeCurrentPos = new Vector2I(-1, -1);
    private bool isDraggingShapeEdge = false;
    private List<Grids> shapePreviewGrids = new();
    private Color shapePreviewColor = new Color(0, 1, 0, 0.4f);

    // ========== 图形填充模式 ==========
    private void StartShapeFillMode(FillShapeType shape)
    {
        CancelAllModes();
        IsShapeFillMode = true;
        currentFillShape = shape;
        shapeAnchorPos = new Vector2I(-1, -1);
        shapeCurrentPos = new Vector2I(-1, -1);
        isDraggingShapeEdge = false;
        ShowActionButtons(true);
        UpdateStatusHint($"{TranslateGroup.T("图形填充")}: {shape} | {TranslateGroup.T("点击确定中心，拖拽调整大小，按确认填充")}");
    }

    public void OnGridClickForShapeFill(Grids grid)
    {
        if (!IsShapeFillMode || grid == null || gridManager?.map == null) return;
        var pos = grid.GridIndex;

        if (shapeAnchorPos.X < 0)
        {
            // 第一次点击：确定中心点
            shapeAnchorPos = pos;
            shapeCurrentPos = pos;
            isDraggingShapeEdge = true;
            ShowShapePreview();
            UpdateStatusHint(TranslateGroup.T("移动鼠标调整大小，按确认填充或取消"));
        }
        // 后续由确认/取消按钮控制
    }

    public void OnGridHoverForShapeFill(Grids grid)
    {
        if (!IsShapeFillMode || !isDraggingShapeEdge || grid == null) return;
        if (shapeAnchorPos.X < 0) return;
        shapeCurrentPos = grid.GridIndex;
        ShowShapePreview();
    }

    private void ShowShapePreview()
    {
        ClearShapePreview();
        var affected = GetShapeGrids(shapeAnchorPos, shapeCurrentPos, currentFillShape);
        foreach (var g in affected)
        {
            if (g != null && IsInstanceValid(g))
            {
                // ✅ 用 attackRangeIcon 显示蓝色预览（和多格兵器统一）
                g.attackRangeIcon?.Show();
                if (g.attackRangeIcon != null)
                    g.attackRangeIcon.Modulate = new Color(0.3f, 0.6f, 1.0f, 0.8f); // 蓝色
                shapePreviewGrids.Add(g);
            }
        }
    }

    private void ClearShapePreview()
    {
        foreach (var g in shapePreviewGrids)
        {
            if (g != null && IsInstanceValid(g))
            {
                g.attackRangeIcon?.Hide();
                if (g.attackRangeIcon != null)
                    g.attackRangeIcon.Modulate = new Color(1, 0.2f, 0.2f, 0.9f);
            }
        }
        shapePreviewGrids.Clear();
    }

    private List<Grids> GetShapeGrids(Vector2I center, Vector2I edge, FillShapeType shape)
    {
        var result = new List<Grids>();
        if (gridManager?.map == null) return result;
        int dx = Mathf.Abs(edge.X - center.X);
        int dy = Mathf.Abs(edge.Y - center.Y);
        int radius = Mathf.Max(dx, dy);
        int w = gridManager.map.GetLength(0);
        int h = gridManager.map.GetLength(1);

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                bool inside = false;
                switch (shape)
                {
                    case FillShapeType.Circle:
                        inside = (x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y) <= radius * radius;
                        break;
                    case FillShapeType.Square:
                        inside = Mathf.Abs(x - center.X) <= radius && Mathf.Abs(y - center.Y) <= radius;
                        break;
                    case FillShapeType.Rectangle:
                        int minX = Mathf.Min(center.X, edge.X);
                        int maxX = Mathf.Max(center.X, edge.X);
                        int minY = Mathf.Min(center.Y, edge.Y);
                        int maxY = Mathf.Max(center.Y, edge.Y);
                        inside = x >= minX && x <= maxX && y >= minY && y <= maxY;
                        break;
                    case FillShapeType.Diamond:
                        inside = Mathf.Abs(x - center.X) + Mathf.Abs(y - center.Y) <= radius;
                        break;
                    case FillShapeType.Triangle:
                        int triHeight = Mathf.Abs(edge.Y - center.Y);
                        int triHalfBase = Mathf.Abs(edge.X - center.X);
                        if (triHeight == 0) triHeight = 1;
                        int relY = y - center.Y;
                        if (relY >= 0 && relY <= triHeight)
                        {
                            float ratio = 1f - (float)relY / triHeight;
                            int allowedHalfBase = Mathf.RoundToInt(triHalfBase * ratio);
                            inside = Mathf.Abs(x - center.X) <= allowedHalfBase;
                        }
                        break;
                }
                if (inside && x >= 0 && x < w && y >= 0 && y < h)
                {
                    var g = gridManager.map[x, y];
                    if (g != null) result.Add(g);
                }
            }
        }
        return result;
    }

    private bool IsPointInsideShape(Vector2I point, Vector2I center, Vector2I edge, FillShapeType shape)
    {
        var grids = GetShapeGrids(center, edge, shape);
        return grids.Any(g => g.GridIndex == point);
    }

    private void ApplyShapeFill()
    {
        var affected = GetShapeGrids(shapeAnchorPos, shapeCurrentPos, currentFillShape);
        var batchRecord = new EditorActionRecord
        {
            actionType = EditorActionType.FillShape,
            subActions = new List<EditorActionRecord>()
        };
        foreach (var g in affected)
        {
            if (g == null || !IsInstanceValid(g)) continue;
            batchRecord.subActions.Add(new EditorActionRecord
            {
                actionType = EditorActionType.FillShape,
                position = g.GridIndex,
                oldTerrain = g.gridType,
                newTerrain = fillShapeTerrain
            });
            g.gridType = fillShapeTerrain;
            g.BakeCowBrush(); // ✅ 放置/修改地形时把当前画笔风格烘焙进格子
            g.UpdateTerrainIcon();
        }
        PushAction(batchRecord);
        UpdateStatusHint($"{TranslateGroup.T("已填充")} {affected.Count} {TranslateGroup.T("个格子")}");
    }

    private void CancelShapeFillMode()
    {
        ClearShapePreview();
        IsShapeFillMode = false;
        shapeAnchorPos = new Vector2I(-1, -1);
        shapeCurrentPos = new Vector2I(-1, -1);
        isDraggingShapeEdge = false;
        ShowActionButtons(false);
    }
}
