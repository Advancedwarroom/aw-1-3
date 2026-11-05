// TerrainEditor.MultiTileUnit.cs - 多格单位放置预览（issue#1 多格机动单位·三期②）
// 照抄武器预览范式：实例化模板读足印 → 边界/占据检查 → 绿色预览 → 二次点击确认，ESC 取消。
// 单格单位在此判断后直通直生（含对称），旧行为不变。
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class TerrainEditor
{
    // 预览状态
    private bool isMultiTileUnitPreviewMode = false;
    private Vector2I pendingMultiTileUnitAnchorPos;
    private string pendingMultiTileUnitTemplatePath = "";
    private string pendingMultiTileUnitTeam = "";
    private readonly List<Grids> previewMultiTileUnitGrids = new();

    /// <summary>单位放置统一入口：多格→预览；单格→直生（含对称）</summary>
    private void TryPreviewMultiTileUnit(Grids grid, string scenePath, string team)
    {
        if (string.IsNullOrEmpty(scenePath) || grid == null)
        {
            SpawnUnitAtGrid(grid, scenePath, team);
            TrySpawnSymmetric(grid, scenePath, team, SpawnMode.Unit);
            return;
        }

        var scene = GD.Load<PackedScene>(scenePath);
        var tempUnit = scene?.Instantiate<Infantry>();
        if (tempUnit == null)
        {
            SpawnUnitAtGrid(grid, scenePath, team);
            TrySpawnSymmetric(grid, scenePath, team, SpawnMode.Unit);
            return;
        }

        // 非多格单位：直生（旧行为）
        if (!tempUnit.isMultiTileUnit)
        {
            tempUnit.QueueFree();
            SpawnUnitAtGrid(grid, scenePath, team);
            TrySpawnSymmetric(grid, scenePath, team, SpawnMode.Unit);
            return;
        }

        var anchorPos = grid.GridIndex;
        var footprintIndices = tempUnit.GetFootprintIndices(anchorPos);
        tempUnit.QueueFree();

        var gm = gameManager;
        if (gm?.gridManager == null) return;

        // 边界检查
        foreach (var idx in footprintIndices)
        {
            if (idx.X < 0 || idx.X >= gm.gridManager.searchRange.X ||
                idx.Y < 0 || idx.Y >= gm.gridManager.searchRange.Y)
            {
                TranslateGroup.SetText(spawnHintLabel, "❌ 超出地图范围，无法放置");
                spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
                return;
            }
        }

        // 占据检查：兵器 / 非重叠单位 / 他方多格足印
        foreach (var idx in footprintIndices)
        {
            var g = gm.gridManager.map[idx.X, idx.Y];
            if (g == null)
            {
                TranslateGroup.SetText(spawnHintLabel, "❌ 目标区域包含无效格子");
                spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
                return;
            }
            if (g.weapon != null && IsInstanceValid(g.weapon))
            {
                TranslateGroup.SetText(spawnHintLabel, "❌ 该区域已有兵器占据，无法放置");
                spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
                return;
            }
            var blockingUnit = g.infantries.FirstOrDefault(u =>
                u != null && IsInstanceValid(u) && u.overlapType == UnitOverlapType.NonOverlapping);
            if (blockingUnit != null)
            {
                spawnHintLabel.Text = $"❌ {TranslateGroup.T("该区域被")} {blockingUnit.Name} {TranslateGroup.T("占据")}";
                spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
                return;
            }
            if (g.multiTileOccupant != null && IsInstanceValid(g.multiTileOccupant))
            {
                spawnHintLabel.Text = $"❌ {TranslateGroup.T("该区域被多格单位")} {g.multiTileOccupant.Name} {TranslateGroup.T("占据")}";
                spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
                return;
            }
        }

        // 清除旧预览并显示新预览（绿色与兵器蓝色区分）
        ClearMultiTileUnitPreview();
        previewMultiTileUnitGrids.Clear();
        foreach (var idx in footprintIndices)
        {
            var g = gm.gridManager.map[idx.X, idx.Y];
            if (g != null)
            {
                previewMultiTileUnitGrids.Add(g);
                g.attackRangeIcon?.Show();
                if (g.attackRangeIcon != null)
                    g.attackRangeIcon.Modulate = new Color(0.4f, 1.0f, 0.5f, 0.8f);   // 绿色预览
            }
        }

        isMultiTileUnitPreviewMode = true;
        pendingMultiTileUnitAnchorPos = anchorPos;
        pendingMultiTileUnitTemplatePath = scenePath;
        pendingMultiTileUnitTeam = team;

        spawnHintLabel.Text = $"🟢 {TranslateGroup.T("预览多格单位")} {footprintIndices.Count}{TranslateGroup.T("格范围")}\n{TranslateGroup.T("再次点击确认放置，按")}ESC{TranslateGroup.T("取消")}";
        spawnHintLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.5f));
    }

    /// <summary>确认放置（GridClick 预览块调用）</summary>
    private void ConfirmMultiTileUnitPlacement()
    {
        var path = pendingMultiTileUnitTemplatePath;
        var team = pendingMultiTileUnitTeam;
        var anchorGrid = previewMultiTileUnitGrids.FirstOrDefault(g => g.GridIndex == pendingMultiTileUnitAnchorPos);
        ClearMultiTileUnitPreview();
        if (anchorGrid != null && !string.IsNullOrEmpty(path))
        {
            SpawnUnitAtGrid(anchorGrid, path, team);
            TrySpawnSymmetric(anchorGrid, path, team, SpawnMode.Unit);
        }
    }

    /// <summary>清除预览状态与图标</summary>
    private void ClearMultiTileUnitPreview()
    {
        foreach (var g in previewMultiTileUnitGrids)
            g?.attackRangeIcon?.Hide();
        previewMultiTileUnitGrids.Clear();
        isMultiTileUnitPreviewMode = false;
        pendingMultiTileUnitTemplatePath = "";
        pendingMultiTileUnitTeam = "";
    }
}
