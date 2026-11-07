using Godot;

// 格子画笔（开创人2026-11-07批2）：有点就扣、没格就拓；选屏/所有屏两模式
public partial class TerrainEditor
{
    public bool isCellBrushMode = false;
    public int brushTargetScreen = -1;   // -1=所有屏，否则=目标屏号

    private void HandleBrushClickAt(Vector2 worldPos)
    {
        var gm = gridManager;
        if (gm == null) return;
        var idx = new Vector2I(
            Mathf.RoundToInt((worldPos.X - gm.startPos.X) / gm.gridSize.X),
            Mathf.RoundToInt((worldPos.Y - gm.startPos.Y) / gm.gridSize.Y));
        var g = gm.GetGridAt(idx);

        if (g != null)
        {
            if (brushTargetScreen >= 0 && g.screenId != brushTargetScreen)
            { ShowImportHint(TranslateGroup.T("该格不属于目标屏，未扣")); return; }
            if (!gm.RemoveCellAt(idx, out var err)) { ShowImportHint(err); return; }
        }
        else
        {
            bool ok = false;
            foreach (var d in new[] { new Vector2I(1, 0), new Vector2I(-1, 0), new Vector2I(0, 1), new Vector2I(0, -1) })
            {
                var n = gm.GetGridAt(idx + d);
                if (n == null) continue;
                if (brushTargetScreen < 0 || n.screenId == brushTargetScreen) { ok = true; break; }
            }
            if (!ok)
            {
                ShowImportHint(brushTargetScreen < 0
                    ? TranslateGroup.T("只能拓到已有格子的紧邻处")
                    : TranslateGroup.T("只能拓到目标屏格子的紧邻处"));
                return;
            }
            gm.AddCellAt(idx);
        }

        gm.freeformScreens = true;
        gm.RecalculateScreensFromCells();
        var ov = gm.GetNodeOrNull<GridLineOverlay>("GridLineOverlay");
        if (ov != null) ov.QueueRedraw();
        ShowImportHint($"{TranslateGroup.T("当前屏数")}: {gm.screenCount}");
    }
}
