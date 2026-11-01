using Godot;
using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;

// TerrainEditor · TerrainMenu（由 TerrainEditor.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class TerrainEditor
{

    // ========== 原有地形菜单 ==========
    private void CreateTerrainMenu()
    {
        terrainMenuPanel = new Panel();
        terrainMenuPanel.Name = "TerrainMenuPanel";
        terrainMenuPanel.CustomMinimumSize = new Vector2(280, 680);
        terrainMenuPanel.SetAnchorsPreset(LayoutPreset.Center);
        terrainMenuPanel.Visible = false;
        terrainMenuPanel.ZIndex = 1000;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.15f, 0.15f, 0.2f, 0.95f);
        panelStyle.SetCornerRadiusAll(12);
        panelStyle.SetBorderWidthAll(2);
        panelStyle.BorderColor = new Color(0.4f, 0.4f, 0.5f);
        terrainMenuPanel.AddThemeStyleboxOverride("panel", panelStyle);

        terrainTitleLabel = new Label();
        TranslateGroup.SetText(terrainTitleLabel, "选择地形类型");
        terrainTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        terrainTitleLabel.AddThemeFontSizeOverride("font_size", 18);
        terrainTitleLabel.AddThemeColorOverride("font_color", Colors.Yellow);
        terrainTitleLabel.CustomMinimumSize = new Vector2(0, 30);
        terrainTitleLabel.Position = new Vector2(0, 10);
        terrainTitleLabel.Size = new Vector2(280, 30);
        terrainMenuPanel.AddChild(terrainTitleLabel);

        var closeButton = new Button();
        closeButton.Text = "✕";
        closeButton.CustomMinimumSize = new Vector2(30, 30);
        closeButton.Position = new Vector2(245, 8);
        closeButton.Pressed += CloseTerrainMenu;
        terrainMenuPanel.AddChild(closeButton);

        terrainScrollContainer = new ScrollContainer();
        terrainScrollContainer.Position = new Vector2(10, 45);
        terrainScrollContainer.Size = new Vector2(260, 240);
        terrainScrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        terrainButtonContainer = new VBoxContainer();
        terrainButtonContainer.AddThemeConstantOverride("separation", 4);
        terrainButtonContainer.Size = new Vector2(260, 0);

        foreach (var terrainType in availableTerrainTypes)
        {
            var btn = CreateTerrainButton(terrainType);
            terrainButtonContainer.AddChild(btn);
        }

        terrainScrollContainer.AddChild(terrainButtonContainer);
        terrainMenuPanel.AddChild(terrainScrollContainer);

        CreateCustomDamageUI();
        CreateAmmoFuelUI();

        AddChild(terrainMenuPanel);
    var dragHandle = new Panel();
    dragHandle.Name = "DragHandle";
    dragHandle.CustomMinimumSize = new Vector2(280, 30);
    dragHandle.Position = new Vector2(0, 0);
    var handleStyle = new StyleBoxFlat();
    handleStyle.BgColor = new Color(0.3f, 0.3f, 0.4f, 0.5f);
    handleStyle.SetCornerRadiusAll(8);
    dragHandle.AddThemeStyleboxOverride("panel", handleStyle);
    
    var dragHint = new Label();
    TranslateGroup.SetText(dragHint, "⋮⋮ 拖拽移动");
    dragHint.AddThemeFontSizeOverride("font_size", 10);
    dragHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.8f));
    dragHint.Position = new Vector2(8, 2);
    dragHandle.AddChild(dragHint);
    
    terrainMenuPanel.AddChild(dragHandle);
    MakeMenuDraggable(terrainMenuPanel, dragHandle);
    }

    private void ShowSelectionMenu(Grids grid, List<Infantry> units, List<Weapon> weapons)
    {
        foreach (var child in selectionButtonContainer.GetChildren())
            child.QueueFree();

        selectionTitleLabel.Text = $"{TranslateGroup.T("格子")} {grid.GridIndex} - {TranslateGroup.T("选择编辑目标")}";

        int optionCount = 0;

        foreach (var unit in units)
        {
            string typeName = unit.GetType().Name;
            string teamColor = unit.team switch { "Player1" => "🔴", "Player2" => "🔵", "Player0" => "⚪", "Player" => "🟣", _ => "⚪" };
            string hpText = $"HP:{Mathf.CeilToInt(unit.health / 10f)}";

            var btn = CreateSelectionButton(
                $"{teamColor} [{TranslateGroup.T("单位")}:{typeName}] {unit.Name} ({hpText})",
                new Color(0.3f, 0.5f, 0.8f),
                () => OpenUnitEditor(unit)
            );
            selectionButtonContainer.AddChild(btn);
            optionCount++;
        }

        foreach (var weapon in weapons)
        {
            string typeName = weapon.GetType().Name;
            string teamColor = weapon.team switch { "Player1" => "🔴", "Player2" => "🔵", "Player0" => "⚪", "Player" => "🟣", _ => "⚪" };
            string hpText = $"HP:{Mathf.CeilToInt(weapon.health / 10f)}";

            var btn = CreateSelectionButton(
                $"{teamColor} [{TranslateGroup.T("兵器")}:{typeName}] {weapon.Name} ({hpText})",
                new Color(0.7f, 0.4f, 0.3f),
                () => OpenWeaponEditor(weapon)
            );
            selectionButtonContainer.AddChild(btn);
            optionCount++;
        }

        var gridBtn = CreateSelectionButton(
            TranslateGroup.T("🟫 修改格子地形/属性"),
            new Color(0.4f, 0.7f, 0.4f),
            () => OpenTerrainMenu(grid)
        );
        selectionButtonContainer.AddChild(gridBtn);
        optionCount++;

        // ✅ 设施更改按钮
        var facilityBtn = CreateSelectionButton(
            TranslateGroup.T("🏙️ 设施更改"),
            new Color(0.8f, 0.6f, 0.2f),
            () => OpenFacilityMenu(grid)
        );
        selectionButtonContainer.AddChild(facilityBtn);
        optionCount++;


        var viewportSize = GetViewport().GetVisibleRect().Size;
        selectionMenuPanel.Position = new Vector2(
            (viewportSize.X - selectionMenuPanel.Size.X) / 2,
            (viewportSize.Y - selectionMenuPanel.Size.Y) / 2
        );
        selectionMenuPanel.Visible = true;
        isMenuOpen = true;
    }

    private Button CreateSelectionButton(string text, Color color, Action onPressed)
    {
        var btn = new Button();
        btn.CustomMinimumSize = new Vector2(280, 45);
        btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        btn.Text = text;
        btn.AddThemeFontSizeOverride("font_size", 14);
        btn.AddThemeColorOverride("font_color", Colors.White);

        var normalStyle = new StyleBoxFlat();
        normalStyle.BgColor = new Color(color.R * 0.3f, color.G * 0.3f, color.B * 0.3f, 0.8f);
        normalStyle.SetCornerRadiusAll(8);
        btn.AddThemeStyleboxOverride("normal", normalStyle);

        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = new Color(color.R * 0.5f, color.G * 0.5f, color.B * 0.5f, 0.9f);
        hoverStyle.SetCornerRadiusAll(8);
        btn.AddThemeStyleboxOverride("hover", hoverStyle);

        btn.Pressed += onPressed;
        return btn;
    }
private void OpenTerrainMenu(Grids grid)
{
    CloseSelectionMenu();
    selectedGrid = grid;
    

    terrainTitleLabel.Text = $"{TranslateGroup.T("选择地形类型")} ({TranslateGroup.T("当前")}: {GetTerrainDisplayName(grid.gridType)})";
    LoadCustomDamageValues(grid);
    LoadAmmoFuelValues(grid);
    terrainMenuPanel.Visible = true;
    
    currentEditingTarget = grid;
    propertyEditorTitle.Text = $"{TranslateGroup.T("编辑格子")}: {grid.GridIndex} ({grid.gridType})";
    BuildPropertyEditor(grid);  
    propertyEditorPanel.Visible = true;
}

// 替换原有的 CloseTerrainMenu
private void CloseTerrainMenu()
{
    SaveCustomDamageValues(selectedGrid);
    SaveAmmoFuelValues(selectedGrid);
    terrainMenuPanel.Visible = false;

    if (currentEditingTarget is Grids)
    {
        propertyEditorPanel.Visible = false;
        currentEditingTarget = null;
    }
    
    isMenuOpen = false;
    selectedGrid = null;
}

    private Button CreateTerrainButton(GridType terrainType)
    {
        var btn = new Button();
        btn.CustomMinimumSize = new Vector2(240, 36);
        btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        Color terrainColor = terrainColors.GetValueOrDefault(terrainType, Colors.White);
        string displayName = GetTerrainDisplayName(terrainType);
        string defenseText = GetTerrainDefenseText(terrainType);
        btn.Text = $"{displayName}  {defenseText}";

        var normalStyle = new StyleBoxFlat();
        normalStyle.BgColor = new Color(terrainColor.R * 0.3f, terrainColor.G * 0.3f, terrainColor.B * 0.3f, 0.8f);
        normalStyle.SetCornerRadiusAll(6);
        btn.AddThemeStyleboxOverride("normal", normalStyle);

        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = new Color(terrainColor.R * 0.5f, terrainColor.G * 0.5f, terrainColor.B * 0.5f, 0.9f);
        hoverStyle.SetCornerRadiusAll(6);
        btn.AddThemeStyleboxOverride("hover", hoverStyle);

        var pressedStyle = new StyleBoxFlat();
        pressedStyle.BgColor = new Color(terrainColor.R * 0.7f, terrainColor.G * 0.7f, terrainColor.B * 0.7f, 1.0f);
        pressedStyle.SetCornerRadiusAll(6);
        btn.AddThemeStyleboxOverride("pressed", pressedStyle);

        var colorRect = new ColorRect();
        colorRect.Color = terrainColor;
        colorRect.CustomMinimumSize = new Vector2(6, 28);
        colorRect.Position = new Vector2(4, 4);
        btn.AddChild(colorRect);

        btn.AddThemeFontSizeOverride("font_size", 13);
        btn.AddThemeColorOverride("font_color", Colors.White);

        GridType capturedType = terrainType;
        btn.Pressed += () => OnTerrainSelected(capturedType);

        return btn;
    }

    private string GetTerrainDisplayName(GridType type)
    {
        return type switch
        {
            GridType.GROUND => TranslateGroup.T("🟫 平原"),
            GridType.FOREST => TranslateGroup.T("🌲 森林"),
            GridType.ROAD => TranslateGroup.T("🛣️ 公路"),
            GridType.SEA => TranslateGroup.T("🌊 海洋"),
            GridType.RIVER => TranslateGroup.T("🏞️ 河流"),
            GridType.HILL => TranslateGroup.T("⛰️ 山地"),
            GridType.METEORITE => TranslateGroup.T("☄️ 陨石(兵器设施)"),
            GridType.PIPE => TranslateGroup.T("🔧 管道"),
            GridType.LAVA => TranslateGroup.T("🌋 岩浆"),
            GridType.BEACH => TranslateGroup.T("🏖️ 沙滩"),
            GridType.TP => TranslateGroup.T("✨ 传送点"),
            GridType.REEF => TranslateGroup.T("🪸 珊瑚礁"),
            GridType.WHIRLPOOL => TranslateGroup.T("🌀 漩涡"),
            GridType.LAVASIDE => TranslateGroup.T("🔥 岩浆滩"),
            GridType.SEAFOG => TranslateGroup.T("🌫️ 海上迷雾"),
            GridType.LANDFOG => TranslateGroup.T("🌫️ 陆上迷雾"),
            GridType.WATERFALL => TranslateGroup.T("💧 瀑布"),
            GridType.CLIFF => TranslateGroup.T("🪨 悬崖"),
            GridType.SLOPE => TranslateGroup.T("📐 斜坡"),
            GridType.CAVE => TranslateGroup.T("🕳️ 塌方穴"),
            GridType.HOLE => TranslateGroup.T("🕳️ 人工洞"),
            GridType.PIPESEAM => TranslateGroup.T("⚙️ 管道接缝(兵器设施)"),
            GridType.TRACK => TranslateGroup.T("🛤️ 铁轨"),
            GridType.STATION => TranslateGroup.T("🚉 站台"),
            GridType.BRIDGE => TranslateGroup.T("🌉 桥梁"),
            GridType.LAVABRIDGE => TranslateGroup.T("🔥🌉 岩浆桥梁"),
            GridType.PASSABLEPIPE => TranslateGroup.T("🔧空心管道"),
            GridType.SHIPGATE => TranslateGroup.T("⚓ 船闸"),
            GridType.OVERPASS => TranslateGroup.T("🌉 立交桥"),
            GridType.BROKENPIPE => TranslateGroup.T("💥 破碎管道"),
            GridType.RUINS => TranslateGroup.T("🏚️ 废墟"),
            GridType.BROKENTRACK => TranslateGroup.T("💥🛤️ 破碎铁路"),
            GridType.LAVAFOG => TranslateGroup.T("🌋🌫️ 岩浆迷雾"),
            GridType.PLASMA => TranslateGroup.T("💠 等离子"),
            GridType.CREEPER => TranslateGroup.T("🌿 蔓生藤蔓"),
            GridType.GATE => TranslateGroup.T("🚪 门(兵器设施)"),
            _ => type.ToString()
        };
    }

    private string GetTerrainDefenseText(GridType type)
    {
        float bonus = type switch
        {
            GridType.GROUND => 0.10f, GridType.FOREST => 0.20f,
            GridType.HILL => 0.40f, GridType.ROAD => 0.0f, GridType.SEA => 0.0f,
            GridType.RIVER => 0.0f, GridType.BEACH => 0.10f, GridType.TP => 0.0f,
            GridType.REEF => 0.10f, GridType.WHIRLPOOL => 0.10f, GridType.LAVASIDE => 0.10f,
            GridType.SEAFOG => 0.50f, GridType.LANDFOG => 0.50f, GridType.WATERFALL => 0.20f,
            GridType.CLIFF => 0.30f, GridType.SLOPE => 0.20f, GridType.CAVE => 0.40f,
            GridType.HOLE => 0.40f, GridType.PIPESEAM => 0.10f, GridType.METEORITE => 0.10f,
            GridType.TRACK => 0.0f, GridType.STATION => 0.20f, GridType.BRIDGE => 0.0f,
            GridType.LAVABRIDGE => 0.0f, GridType.PASSABLEPIPE => 0.10f, GridType.SHIPGATE => 0.10f,
            GridType.OVERPASS => 0.30f, GridType.BROKENPIPE => 0.20f, GridType.RUINS => 0.30f,
            GridType.BROKENTRACK => 0.20f, GridType.LAVAFOG => 0.50f,
            GridType.PLASMA => 0.0f,   // 等离子：无地形星
            GridType.CREEPER => 0.0f,  // 蔓生藤蔓：无地形星
            GridType.GATE => 0.10f,    // 门：1星（同船闸）
            _ => 0.0f
        };
        return bonus > 0 ? $"[防御{bonus * 100:F0}%]" : TranslateGroup.T("[无防御]");
    }



private void OnTerrainSelected(GridType newTerrain)
{
    if (selectedGrid == null) return;
    
    SaveCustomDamageValues(selectedGrid);
    SaveAmmoFuelValues(selectedGrid);

    GridType oldTerrain = selectedGrid.gridType;
    selectedGrid.gridType = newTerrain;

    UpdateGridVisual(selectedGrid);
    
    // ✅ 新增：刷新属性编辑器标题
    if (propertyEditorPanel.Visible && currentEditingTarget == selectedGrid)
    {
        propertyEditorTitle.Text = $"{TranslateGroup.T("编辑格子")}: {selectedGrid.GridIndex} ({selectedGrid.gridType})";
    }
    

}
    public void UpdateGridVisual(Grids grid)
    {
        var sprite = grid.GetNodeOrNull<Sprite2D>("Sprite2D");
        if (sprite == null) return;

        Color terrainColor = terrainColors.GetValueOrDefault(grid.gridType, Colors.White);
        var tween = CreateTween();
        tween.TweenProperty(sprite, "modulate", terrainColor, 0.3f)
             .SetTrans(Tween.TransitionType.Sine)
             .SetEase(Tween.EaseType.InOut);
    }
}
