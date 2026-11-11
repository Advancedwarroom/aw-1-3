// MapEditorHelper.cs - 编辑器大更新：图形填充、区域填充、地形填充、覆盖全图、选区复制粘贴（矩形+不规则）、对称放置
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// MapEditorHelper · SelectPaste（由 MapEditorHelper.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class MapEditorHelper
{

    // ========== 选区复制粘贴（矩形 + 不规则） ==========
    public bool IsSelectMode { get; private set; } = false;
    public SelectModeType CurrentSelectMode { get; private set; } = SelectModeType.None;
    public bool IsPasteMode { get; private set; } = false;

    // 矩形选区
    private Vector2I selectStartPos = new Vector2I(-1, -1);
    private Vector2I selectEndPos = new Vector2I(-1, -1);

    // 不规则选区
    private HashSet<Vector2I> irregularSelectedCells = new();
    private List<Grids> irregularPreviewGrids = new();
    private Color irregularSelectColor = new Color(0, 0.8f, 1, 0.5f);

    // 选区剪贴板
    private List<MapSelection> selectionClipboard = new();
    private MapSelection currentPasteSelection = null;
    private const int MAX_SELECTIONS = 5;
    private const int MAX_FAVORITE_SELECTIONS = 3;

    // ========== 选区复制粘贴（矩形 + 不规则） ==========
    private void StartSelectMode(SelectModeType mode)
    {
        CancelAllModes();
        IsSelectMode = true;
        CurrentSelectMode = mode;

        if (mode == SelectModeType.Rectangle)
        {
            selectStartPos = new Vector2I(-1, -1);
            selectEndPos = new Vector2I(-1, -1);
            UpdateStatusHint(TranslateGroup.T("矩形选区: 点击确定起点，再次点击确定终点"));
        }
        else if (mode == SelectModeType.Irregular)
        {
            irregularSelectedCells.Clear();
            ClearIrregularPreview();
            UpdateStatusHint(TranslateGroup.T("不规则选区: 点击格子选中/取消，再次点击按钮完成选区"));
        }
    }

    // 不规则选区按钮的特殊逻辑：点击开启，再次点击关闭并保存
    private void OnIrregularSelectBtnPressed()
    {
        if (IsSelectMode && CurrentSelectMode == SelectModeType.Irregular)
        {
            // 再次点击：关闭模式并保存选区
            if (irregularSelectedCells.Count > 0)
            {
                CreateIrregularSelection();
            }
            else
            {
                UpdateStatusHint(TranslateGroup.T("未选择任何格子，已取消"));
            }
            CancelAllModes();
        }
        else
        {
            // 第一次点击：开启不规则选区
            StartSelectMode(SelectModeType.Irregular);
        }
    }

    public void OnGridClickForSelect(Grids grid)
    {
        if (!IsSelectMode || grid == null) return;
        var pos = grid.GridIndex;

        if (CurrentSelectMode == SelectModeType.Rectangle)
        {
            if (selectStartPos.X < 0)
            {
                selectStartPos = pos;
                UpdateStatusHint(TranslateGroup.T("已确定起点，点击终点完成矩形选区"));
            }
            else
            {
                selectEndPos = pos;
                CreateRectangleSelection(selectStartPos, selectEndPos);
                IsSelectMode = false;
                CurrentSelectMode = SelectModeType.None;
            }
        }
        else if (CurrentSelectMode == SelectModeType.Irregular)
        {
            // 不规则选区：点击选中/取消
            if (irregularSelectedCells.Contains(pos))
            {
                irregularSelectedCells.Remove(pos);
                grid.SelfModulate = Colors.White;
                UpdateStatusHint($"{TranslateGroup.T("已取消")} ({pos.X},{pos.Y}) | {TranslateGroup.T("当前共")} {irregularSelectedCells.Count} {TranslateGroup.T("格")}");
            }
            else
            {
                irregularSelectedCells.Add(pos);
                grid.SelfModulate = irregularSelectColor;
                UpdateStatusHint($"{TranslateGroup.T("已选中")} ({pos.X},{pos.Y}) | {TranslateGroup.T("当前共")} {irregularSelectedCells.Count} {TranslateGroup.T("格")}");
            }
        }
    }

    private void CreateRectangleSelection(Vector2I start, Vector2I end)
    {
        if (gridManager?.map == null) return;
        int minX = Mathf.Min(start.X, end.X);
        int maxX = Mathf.Max(start.X, end.X);
        int minY = Mathf.Min(start.Y, end.Y);
        int maxY = Mathf.Max(start.Y, end.Y);

        var cells = new List<SelectionCellData>();
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                if (x < 0 || x >= gridManager.map.GetLength(0) || y < 0 || y >= gridManager.map.GetLength(1))
                    continue;
                var g = gridManager.map[x, y];
                if (g == null) continue;
                cells.Add(CreateCellData(g, new Vector2I(x - minX, y - minY)));
            }
        }

        var selection = new MapSelection
        {
            id = $"sel_rect_{DateTimeOffset.Now.ToUnixTimeMilliseconds()}",
            isFavorite = false,
            bounds = new Rect2I(minX, minY, maxX - minX + 1, maxY - minY + 1),
            cells = cells,
            savedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            isIrregular = false
        };

        ManageSelectionClipboard(selection);
        UpdateStatusHint($"{TranslateGroup.T("矩形选区已保存")}: {cells.Count} {TranslateGroup.T("格")} | {TranslateGroup.T("共")} {selectionClipboard.Count}/5 {TranslateGroup.T("个选区")}");
    }

    private void CreateIrregularSelection()
    {
        if (gridManager?.map == null || irregularSelectedCells.Count == 0) return;

        // 计算边界
        int minX = irregularSelectedCells.Min(p => p.X);
        int maxX = irregularSelectedCells.Max(p => p.X);
        int minY = irregularSelectedCells.Min(p => p.Y);
        int maxY = irregularSelectedCells.Max(p => p.Y);

        var cells = new List<SelectionCellData>();
        foreach (var pos in irregularSelectedCells)
        {
            if (pos.X < 0 || pos.X >= gridManager.map.GetLength(0) || pos.Y < 0 || pos.Y >= gridManager.map.GetLength(1))
                continue;
            var g = gridManager.map[pos.X, pos.Y];
            if (g == null) continue;
            cells.Add(CreateCellData(g, new Vector2I(pos.X - minX, pos.Y - minY)));
        }

        var selection = new MapSelection
        {
            id = $"sel_irr_{DateTimeOffset.Now.ToUnixTimeMilliseconds()}",
            isFavorite = false,
            bounds = new Rect2I(minX, minY, maxX - minX + 1, maxY - minY + 1),
            cells = cells,
            savedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            isIrregular = true
        };

        ManageSelectionClipboard(selection);
        ClearIrregularPreview();
        UpdateStatusHint($"{TranslateGroup.T("不规则选区已保存")}: {cells.Count} {TranslateGroup.T("格")} | {TranslateGroup.T("共")} {selectionClipboard.Count}/5 {TranslateGroup.T("个选区")}");
    }

    private SelectionCellData CreateCellData(Grids grid, Vector2I relativePos)
    {
        var cell = new SelectionCellData
        {
            gridPos = relativePos,
            gridType = grid.gridType
        };
        cell.gridProperties["canDestroyUnit"] = grid.canDestroyUnit.ToString();
        cell.gridProperties["fixedDamagePerTurn"] = grid.fixedDamagePerTurn.ToString();
        cell.gridProperties["fixedAttackPerTurn"] = grid.fixedAttackPerTurn.ToString();
        cell.gridProperties["fixedMaxHealthChangePerTurn"] = grid.fixedMaxHealthChangePerTurn.ToString();
        cell.gridProperties["fixedWeightChangePerTurn"] = grid.fixedWeightChangePerTurn.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (Infantry.AmmoKind k in Infantry.AllAmmoKinds)
        {
            cell.gridProperties[$"ammoChange#{(int)k}"] = grid.GetAmmoKindChange(k).ToString();
            cell.gridProperties[$"ammoMaxChange#{(int)k}"] = grid.GetAmmoKindMaxChange(k).ToString();
        }
        cell.gridProperties["fixedMaxFuelChangePerTurn"] = grid.fixedMaxFuelChangePerTurn.ToString();
        cell.gridProperties["visionBonus"] = grid.visionBonus.ToString();
        if (grid.infantry != null) cell.unitData = SerializeUnit(grid.infantry);
        if (grid.weapon != null) cell.weaponData = SerializeWeapon(grid.weapon);
        if (grid.city != null) cell.facilityData = SerializeFacility(grid.city);
        return cell;
    }

    private void ClearIrregularPreview()
    {
        foreach (var pos in irregularSelectedCells)
        {
            if (pos.X < 0 || pos.X >= gridManager.map.GetLength(0) || pos.Y < 0 || pos.Y >= gridManager.map.GetLength(1))
                continue;
            var g = gridManager.map[pos.X, pos.Y];
            if (g != null && IsInstanceValid(g))
                g.SelfModulate = Colors.White;
        }
        irregularSelectedCells.Clear();
    }

    private void ManageSelectionClipboard(MapSelection newSelection)
    {
        while (selectionClipboard.Count >= MAX_SELECTIONS)
        {
            var toRemove = selectionClipboard.FirstOrDefault(s => !s.isFavorite);
            if (toRemove == null) break;
            selectionClipboard.Remove(toRemove);
        }
        selectionClipboard.Add(newSelection);
    }

    private void ShowPasteMenu()
    {
        if (selectionClipboard.Count == 0)
        {
            UpdateStatusHint(TranslateGroup.T("没有可粘贴的选区！"));
            return;
        }

        var dialog = new AcceptDialog();
        dialog.Title = TranslateGroup.T("选择要粘贴的选区");
        dialog.Size = new Vector2I(450, 400);

        var scroll = new ScrollContainer();
        scroll.CustomMinimumSize = new Vector2(430, 330);
        dialog.AddChild(scroll);

        var vbox = new VBoxContainer();
        scroll.AddChild(vbox);

        for (int i = 0; i < selectionClipboard.Count; i++)
        {
            var sel = selectionClipboard[i];
            var hbox = new HBoxContainer();
            vbox.AddChild(hbox);

            var favLabel = new Label();
            favLabel.Text = sel.isFavorite ? "⭐" : "  ";
            hbox.AddChild(favLabel);

            var typeLabel = new Label();
            typeLabel.Text = sel.isIrregular ? "✏️" : "🔲";
            hbox.AddChild(typeLabel);

            var infoLabel = new Label();
            infoLabel.Text = $"[{i+1}] {sel.bounds.Size.X}x{sel.bounds.Size.Y} | {sel.cells.Count}{TranslateGroup.T("格")}";
            infoLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hbox.AddChild(infoLabel);

            var pasteBtn = new Button();
            TranslateGroup.SetText(pasteBtn, "粘贴");
            pasteBtn.Pressed += () => { StartPasteMode(sel); dialog.QueueFree(); };
            hbox.AddChild(pasteBtn);

            var favBtn = new Button();
            favBtn.Text = sel.isFavorite ? TranslateGroup.T("取消收藏") : TranslateGroup.T("收藏");
            favBtn.Pressed += () =>
            {
                if (sel.isFavorite)
                {
                    sel.isFavorite = false;
                    TranslateGroup.SetText(favBtn, "收藏");
                    favLabel.Text = "  ";
                }
                else
                {
                    int favCount = selectionClipboard.Count(s => s.isFavorite);
                    if (favCount >= MAX_FAVORITE_SELECTIONS)
                    {
                        UpdateStatusHint(TranslateGroup.T("收藏已达上限(3个)！"));
                        return;
                    }
                    sel.isFavorite = true;
                    TranslateGroup.SetText(favBtn, "取消收藏");
                    favLabel.Text = "⭐";
                }
            };
            hbox.AddChild(favBtn);

            var delBtn = new Button();
            TranslateGroup.SetText(delBtn, "删除");
            delBtn.AddThemeColorOverride("font_color", Colors.Red);
            delBtn.Pressed += () =>
            {
                selectionClipboard.Remove(sel);
                hbox.QueueFree();
            };
            hbox.AddChild(delBtn);
        }

        AddChild(dialog);
        dialog.PopupCentered();
    }

    private void StartPasteMode(MapSelection selection)
    {
        CancelAllModes();
        IsPasteMode = true;
        currentPasteSelection = selection;
        UpdateStatusHint(TranslateGroup.T("粘贴模式: 点击目标位置粘贴选区，ESC取消"));
    }

    public void OnGridClickForPaste(Grids grid)
    {
        if (!IsPasteMode || grid == null || currentPasteSelection == null) return;

        var offset = grid.GridIndex;
        var batchRecord = new EditorActionRecord
        {
            actionType = EditorActionType.PasteSelection,
            subActions = new List<EditorActionRecord>()
        };

        foreach (var cell in currentPasteSelection.cells)
        {
            int targetX = offset.X + cell.gridPos.X;
            int targetY = offset.Y + cell.gridPos.Y;

            if (targetX < 0 || targetX >= gridManager.map.GetLength(0) ||
                targetY < 0 || targetY >= gridManager.map.GetLength(1))
                continue;

            var g = gridManager.map[targetX, targetY];
            if (g == null) continue;

            var sub = new EditorActionRecord
            {
                actionType = EditorActionType.PasteSelection,
                position = g.GridIndex,
                oldTerrain = g.gridType
            };
            if (g.infantry != null) sub.oldUnitData = SerializeUnit(g.infantry);
            if (g.weapon != null) sub.oldWeaponData = SerializeWeapon(g.weapon);
            if (g.city != null) sub.oldFacilityData = SerializeFacility(g.city);
            batchRecord.subActions.Add(sub);

            g.gridType = cell.gridType;
            g.BakeCowBrush(); // ✅ 放置/修改地形时把当前画笔风格烘焙进格子
            g.UpdateTerrainIcon();

            if (cell.gridProperties.TryGetValue("canDestroyUnit", out string cdu))
                g.canDestroyUnit = bool.Parse(cdu);
            if (cell.gridProperties.TryGetValue("fixedDamagePerTurn", out string fdp))
                g.fixedDamagePerTurn = int.Parse(fdp);
            if (cell.gridProperties.TryGetValue("fixedAttackPerTurn", out string fap))
                g.fixedAttackPerTurn = int.Parse(fap);
            if (cell.gridProperties.TryGetValue("fixedMaxHealthChangePerTurn", out string fmh))
                g.fixedMaxHealthChangePerTurn = int.Parse(fmh);
            if (cell.gridProperties.TryGetValue("fixedWeightChangePerTurn", out string fw))
                g.fixedWeightChangePerTurn = float.Parse(fw, System.Globalization.CultureInfo.InvariantCulture);
            foreach (Infantry.AmmoKind k in Infantry.AllAmmoKinds)
            {
                if (cell.gridProperties.TryGetValue($"ammoChange#{(int)k}", out string ac))
                    g.SetAmmoKindChange(k, int.Parse(ac));
                if (cell.gridProperties.TryGetValue($"ammoMaxChange#{(int)k}", out string amc))
                    g.SetAmmoKindMaxChange(k, int.Parse(amc));
            }
            if (cell.gridProperties.TryGetValue("fixedMaxFuelChangePerTurn", out string fmf))
                g.fixedMaxFuelChangePerTurn = int.Parse(fmf);
            if (cell.gridProperties.TryGetValue("visionBonus", out string vb))
                g.visionBonus = int.Parse(vb);

            if (cell.unitData != null) SpawnUnitFromData(g, cell.unitData);
            if (cell.weaponData != null) SpawnWeaponFromData(g, cell.weaponData);
            if (cell.facilityData != null) SpawnFacilityFromData(g, cell.facilityData);
        }

        PushAction(batchRecord);
        UpdateStatusHint($"{TranslateGroup.T("已粘贴")} {currentPasteSelection.cells.Count} {TranslateGroup.T("个格子")}");
        CancelAllModes();
    }
}
