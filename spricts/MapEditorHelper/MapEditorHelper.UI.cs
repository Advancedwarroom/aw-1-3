// MapEditorHelper.cs - 编辑器大更新：图形填充、区域填充、地形填充、覆盖全图、选区复制粘贴（矩形+不规则）、对称放置
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// MapEditorHelper · UI（由 MapEditorHelper.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class MapEditorHelper
{

    // ========== UI 创建 ==========
    private void CreateHelperUI()
    {
        helperToggleBtn = new Button();
        helperToggleBtn.Name = "HelperToggleBtn";
        TranslateGroup.SetText(helperToggleBtn, "🛠️ 编辑器助手");
        helperToggleBtn.CustomMinimumSize = new Vector2(120, 40);
        helperToggleBtn.Pressed += OnHelperToggle;
        AddChild(helperToggleBtn);

        helperPanel = new Panel();
        helperPanel.Name = "HelperPanel";
        var vpSize = GetViewport().GetVisibleRect().Size;
        helperPanel.CustomMinimumSize = new Vector2(300, Mathf.Min(580, vpSize.Y * 0.8f));
        helperPanel.Visible = false;
        helperPanel.MouseFilter = Control.MouseFilterEnum.Pass;
        AddChild(helperPanel);

        var scroll = new ScrollContainer();
        scroll.Name = "HelperScroll";
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        helperPanel.AddChild(scroll);

        var vbox = new VBoxContainer();
        vbox.Name = "HelperVBox";
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        vbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.AddChild(vbox);

        var titleBar = new HBoxContainer();
        titleBar.Name = "TitleBar";
        titleBar.CustomMinimumSize = new Vector2(0, 30);
        vbox.AddChild(titleBar);

        var titleLabel = new Label();
        TranslateGroup.SetText(titleLabel, "=== 编辑器助手 ===");
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titleBar.AddChild(titleLabel);

        var closeBtn = new Button();
        closeBtn.Text = "X";
        closeBtn.CustomMinimumSize = new Vector2(30, 30);
        closeBtn.Pressed += () => helperPanel.Visible = false;
        titleBar.AddChild(closeBtn);
        titleBar.GuiInput += OnTitleBarGuiInput;

        statusHintLabel = new Label();
        statusHintLabel.Name = "StatusHint";
        TranslateGroup.SetText(statusHintLabel, "就绪");
        statusHintLabel.HorizontalAlignment = HorizontalAlignment.Center;
        statusHintLabel.AddThemeColorOverride("font_color", Colors.Gray);
        statusHintLabel.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(statusHintLabel);

        // ✅ 操作按钮栏（确认/取消）—— 手机友好
        actionButtonBar = new HBoxContainer();
        actionButtonBar.Name = "ActionButtonBar";
        actionButtonBar.Visible = false;
        actionButtonBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        vbox.AddChild(actionButtonBar);

        confirmBtn = new Button();
        TranslateGroup.SetText(confirmBtn, "✅ 确认");
        confirmBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        confirmBtn.Pressed += OnConfirmAction;
        actionButtonBar.AddChild(confirmBtn);

        cancelBtn = new Button();
        TranslateGroup.SetText(cancelBtn, "❌ 取消");
        cancelBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        cancelBtn.Pressed += OnCancelAction;
        actionButtonBar.AddChild(cancelBtn);

        vbox.AddChild(CreateSectionLabel(TranslateGroup.T("📐 图形填充")));
        var shapeGrid = new GridContainer();
        shapeGrid.Columns = 3;
        vbox.AddChild(shapeGrid);
        AddShapeButton(shapeGrid, TranslateGroup.T("⭕ 圆"), FillShapeType.Circle);
        AddShapeButton(shapeGrid, TranslateGroup.T("⬜ 方"), FillShapeType.Square);
        AddShapeButton(shapeGrid, TranslateGroup.T("▭ 长方"), FillShapeType.Rectangle);
        AddShapeButton(shapeGrid, TranslateGroup.T("◆ 菱形"), FillShapeType.Diamond);
        AddShapeButton(shapeGrid, TranslateGroup.T("🔺 三角"), FillShapeType.Triangle);

        var shapeTerrainBtn = new Button();
        TranslateGroup.SetText(shapeTerrainBtn, "选择填充地形...");
        shapeTerrainBtn.Pressed += () => OpenTerrainPicker((gt) => fillShapeTerrain = gt);
        vbox.AddChild(shapeTerrainBtn);

        vbox.AddChild(CreateSectionLabel(TranslateGroup.T("🎨 填充工具")));
        var areaFillBtn = new Button();
        TranslateGroup.SetText(areaFillBtn, "区域填充(相邻)");
        areaFillBtn.Pressed += () => StartAreaFillMode(true);
        vbox.AddChild(areaFillBtn);

        var terrainFillBtn = new Button();
        TranslateGroup.SetText(terrainFillBtn, "地形填充(全图)");
        terrainFillBtn.Pressed += () => StartTerrainFillMode();
        vbox.AddChild(terrainFillBtn);

        var fillTerrainPicker = new Button();
        TranslateGroup.SetText(fillTerrainPicker, "选择目标新地形...");
        fillTerrainPicker.Pressed += () => OpenTerrainPicker((gt) => areaFillNewTerrain = gt);
        vbox.AddChild(fillTerrainPicker);

        vbox.AddChild(CreateSectionLabel(TranslateGroup.T("🗑️ 覆盖全图")));
        var clearMapBtn = new Button();
        TranslateGroup.SetText(clearMapBtn, "覆盖全图并清空实体");
        clearMapBtn.AddThemeColorOverride("font_color", Colors.Red);
        clearMapBtn.Pressed += StartClearMapMode;
        vbox.AddChild(clearMapBtn);

        var clearMapTerrainBtn = new Button();
        TranslateGroup.SetText(clearMapTerrainBtn, "选择覆盖地形...");
        clearMapTerrainBtn.Pressed += () => OpenTerrainPicker((gt) => clearMapTerrain = gt);
        vbox.AddChild(clearMapTerrainBtn);

        vbox.AddChild(CreateSectionLabel(TranslateGroup.T("📋 选区操作")));
        var rectSelectBtn = new Button();
        TranslateGroup.SetText(rectSelectBtn, "🔲 矩形选区");
        rectSelectBtn.Pressed += () => StartSelectMode(SelectModeType.Rectangle);
        vbox.AddChild(rectSelectBtn);

        irregularSelectModeBtn = new Button();
        TranslateGroup.SetText(irregularSelectModeBtn, "✏️ 不规则选区");
        irregularSelectModeBtn.Pressed += OnIrregularSelectBtnPressed;
        vbox.AddChild(irregularSelectModeBtn);

        var pasteBtn = new Button();
        TranslateGroup.SetText(pasteBtn, "📋 粘贴选区");
        pasteBtn.Pressed += ShowPasteMenu;
        vbox.AddChild(pasteBtn);

        var symmetryBtn = new Button();
        TranslateGroup.SetText(symmetryBtn, "🔀 对称: 关");
        symmetryBtn.Name = "SymmetryBtn";
        symmetryBtn.Pressed += ToggleSymmetryMode;
        vbox.AddChild(symmetryBtn);

        vbox.AddChild(CreateSectionLabel(TranslateGroup.T("↩️ 撤销/前进")));
        var undoRedoHBox = new HBoxContainer();
        vbox.AddChild(undoRedoHBox);
        var undoBtn = new Button();
        TranslateGroup.SetText(undoBtn, "↩️ 撤销");
        undoBtn.Pressed += Undo;
        undoRedoHBox.AddChild(undoBtn);
        var redoBtn = new Button();
        TranslateGroup.SetText(redoBtn, "↪️ 前进");
        redoBtn.Pressed += Redo;
        undoRedoHBox.AddChild(redoBtn);

        var historyLabel = new Label();
        historyLabel.Name = "HistoryLabel";
        TranslateGroup.SetText(historyLabel, "历史: 0/0");
        historyLabel.AddThemeColorOverride("font_color", Colors.Gray);
        vbox.AddChild(historyLabel);

        vbox.AddChild(CreateSectionLabel(TranslateGroup.T("🖼️ 贴图替换")));
        var textureReplaceBtn = new Button();
        TranslateGroup.SetText(textureReplaceBtn, "替换单位/兵器贴图");
        textureReplaceBtn.Pressed += OpenTextureReplaceDialog;
        vbox.AddChild(textureReplaceBtn);

        var vp = GetViewport().GetVisibleRect().Size;
        helperPanel.Position = new Vector2(vp.X - 320, 50);
        helperToggleBtn.Position = new Vector2(vp.X - 150, 10);
    }

    private Label CreateSectionLabel(string text)
    {
        var label = new Label();
        label.Text = text;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AddThemeColorOverride("font_color", Colors.Yellow);
        label.AddThemeFontSizeOverride("font_size", 14);
        return label;
    }

    private void AddShapeButton(GridContainer parent, string text, FillShapeType shape)
    {
        var btn = new Button();
        btn.Text = text;
        btn.CustomMinimumSize = new Vector2(80, 35);
        btn.Pressed += () => StartShapeFillMode(shape);
        parent.AddChild(btn);
    }

    private void OnTitleBarGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton btn && btn.ButtonIndex == MouseButton.Left)
        {
            if (btn.Pressed)
            {
                isHelperPanelDragging = true;
                helperPanelDragOffset = helperPanel.GetGlobalMousePosition() - helperPanel.GlobalPosition;
            }
            else
            {
                isHelperPanelDragging = false;
            }
        }
    }

    public override void _Process(double delta)
    {
        if (isHelperPanelDragging && helperPanel != null)
        {
            helperPanel.GlobalPosition = helperPanel.GetGlobalMousePosition() - helperPanelDragOffset;
        }
    }

    private void OnHelperToggle()
    {
        helperPanel.Visible = !helperPanel.Visible;
        if (helperPanel.Visible) helperPanel.Size = helperPanel.CustomMinimumSize;
    }

    private void UpdateStatusHint(string text)
    {
        if (statusHintLabel != null) statusHintLabel.Text = text;
    }

    private void ShowActionButtons(bool show)
    {
        if (actionButtonBar != null) actionButtonBar.Visible = show;
    }

    private void OnConfirmAction()
    {
        if (IsShapeFillMode && isDraggingShapeEdge)
        {
            ApplyShapeFill();
            CancelShapeFillMode();
        }
        else if (IsClearMapMode)
        {
            ApplyClearMap();
        }
        else if (IsSelectMode)
        {
            CreateIrregularSelection();
        }
        ShowActionButtons(false);
    }

    private void OnCancelAction()
    {
        CancelAllModes();
        ShowActionButtons(false);
    }

    private void OpenTerrainPicker(Action<GridType> onSelected)
    {
        var dialog = new AcceptDialog();
        dialog.Title = TranslateGroup.T("选择地形");
        dialog.Size = new Vector2I(400, 300);
        var scroll = new ScrollContainer();
        scroll.CustomMinimumSize = new Vector2(380, 250);
        dialog.AddChild(scroll);
        var grid = new GridContainer();
        grid.Columns = 4;
        scroll.AddChild(grid);
        foreach (GridType gt in Enum.GetValues(typeof(GridType)))
        {
            var btn = new Button();
            btn.Text = gt.ToString();
            btn.CustomMinimumSize = new Vector2(90, 35);
            btn.Pressed += () => { onSelected(gt); dialog.QueueFree(); };
            grid.AddChild(btn);
        }
        AddChild(dialog);
        dialog.PopupCentered();
    }

    // ✅ 一键退出全部编辑模式（TerrainEditor 进入销毁模式前调用，防止模式残留吞点击）
    public void ExitAllModes()
    {
        bool hadAny = IsSelectMode || IsPasteMode || IsSymmetryMode
            || IsAreaFillMode || IsTerrainFillMode || IsShapeFillMode
            || !string.IsNullOrEmpty(pendingTexturePath);
        IsSelectMode = false;
        IsPasteMode = false;
        IsSymmetryMode = false;
        IsAreaFillMode = false;
        IsTerrainFillMode = false;
        IsShapeFillMode = false;
        pendingTexturePath = "";
        // 对称：连带清轴线和预览
        CurrentSymmetryAxis = SymmetryAxis.None;
        symmetryAxisLine = -1;
        ClearSymmetryPreview();
        // 形状填充拖拽中途中断 / 粘贴选区作废
        isDraggingShapeEdge = false;
        currentPasteSelection = null;
        if (hadAny)
            UpdateStatusHint(TranslateGroup.T("已退出全部编辑模式"));
    }
}
