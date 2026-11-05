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
        terrainMenuPanel.CustomMinimumSize = new Vector2(280, 950); // ✦优化：加高容纳全种类弹药编辑
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

        // ========== ✅ CoW 整体风格选择条(气候x风格, 全局即时生效) ==========
        var styleBar = new HBoxContainer();
        styleBar.Position = new Vector2(10, 42);
        styleBar.Size = new Vector2(260, 32);
        styleBar.AddThemeConstantOverride("separation", 4);

        var biomeLabel = new Label();
        TranslateGroup.SetText(biomeLabel, "气候");
        biomeLabel.AddThemeFontSizeOverride("font_size", 12);
        styleBar.AddChild(biomeLabel);

        cowBiomeOption = new OptionButton();
        cowBiomeOption.CustomMinimumSize = new Vector2(100, 30);
        string[] biomeNames = { "常规", "雪", "雨", "废土", "沙漠" };
        for (int i = 0; i < biomeNames.Length; i++) cowBiomeOption.AddItem(TranslateGroup.T(biomeNames[i]), i);
        cowBiomeOption.ItemSelected += OnCowBiomeSelected;
        styleBar.AddChild(cowBiomeOption);

        var styleLabel = new Label();
        TranslateGroup.SetText(styleLabel, "风格");
        styleLabel.AddThemeFontSizeOverride("font_size", 12);
        styleBar.AddChild(styleLabel);

        cowStyleOption = new OptionButton();
        cowStyleOption.CustomMinimumSize = new Vector2(105, 30);
        string[] styleNames = { "AW1", "SFC", "AW2", "DS", "DoR" };
        for (int i = 0; i < styleNames.Length; i++) cowStyleOption.AddItem(styleNames[i], i);
        cowStyleOption.ItemSelected += OnCowStyleSelected;
        styleBar.AddChild(cowStyleOption);

        terrainMenuPanel.AddChild(styleBar);

        terrainScrollContainer = new ScrollContainer();
        terrainScrollContainer.Position = new Vector2(10, 78);
        terrainScrollContainer.Size = new Vector2(260, 207);
        terrainScrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        terrainButtonContainer = new VBoxContainer();
        terrainButtonContainer.AddThemeConstantOverride("separation", 4);
        terrainButtonContainer.Size = new Vector2(260, 0);

        CreateTerrainButtons();

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

    // ✅ CoW 风格条回显 + 重置方向选择器并重建分组列表
    SyncCowStyleOptions();
    dirAutoMode = true;
    manualDirMask = 0;
    expandedTerrainGroup = null;
    CreateTerrainButtons();

    terrainTitleLabel.Text = $"{TranslateGroup.T("选择地形类型")} ({TranslateGroup.T("当前")}: {GetTerrainDisplayName(grid.gridType)})";
    LoadCustomDamageValues(grid);
    LoadAmmoFuelValues(grid);
    terrainMenuPanel.Visible = true;
    
    currentEditingTarget = grid;
    propertyEditorTitle.Text = $"{TranslateGroup.T("编辑格子")}: {grid.GridIndex} ({grid.gridType})";
    BuildPropertyEditor(grid);  
    propertyEditorPanel.Visible = true;
    BuildTerrainModuleCsvUI(grid);
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

    // ✅ 地形图标缓存：惰性从 grid.tscn 实例化一个格子读取现成的 GridType→贴图映射，读完即释放
    private static Dictionary<GridType, Texture2D> _terrainMenuIconCache = null;

    private Texture2D GetTerrainIconTexture(GridType type)
    {
        if (_terrainMenuIconCache == null)
        {
            _terrainMenuIconCache = new Dictionary<GridType, Texture2D>();
            var gridScene = GD.Load<PackedScene>("res://Prefabs/grid.tscn");
            if (gridScene != null)
            {
                var probe = gridScene.Instantiate<Grids>();   // ✅ 不入树，_Ready 不会执行，只读导出映射
                if (probe != null)
                {
                    foreach (var kvp in probe.terrainIconTextures)
                        _terrainMenuIconCache[kvp.Key] = kvp.Value;
                    probe.QueueFree();   // ✅ 取完立即释放临时格子
                }
            }
        }
        return _terrainMenuIconCache.TryGetValue(type, out var tex) ? tex : null;
    }

    // ========== ✅ CoW 风格选择 + 分组地形列表 ==========
    private OptionButton cowBiomeOption;
    private OptionButton cowStyleOption;
    private readonly Dictionary<string, List<GridType>> terrainGroups = new();
    private readonly List<string> terrainGroupOrder = new();
    private string expandedTerrainGroup = null;
    private int manualDirMask = 0;          // 手动方向掩码(仅方向型地形)
    private bool dirAutoMode = true;
    private Label dirHintLabel = null;
    private readonly Dictionary<int, Button> dirToggleBtns = new();
    private readonly Dictionary<GridType, Button> terrainButtonMap = new();

    private void OnCowBiomeSelected(long index)
    {
        // ✅ 画笔语义: 只影响之后放置的地形, 不动已放好的地块(对齐 CoW 预备栏)
        TerrainStyleManager.CurrentBiome = (TerrainStyleManager.Biome)(int)index;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm != null) gm.terrainBiome = TerrainStyleManager.CurrentBiome;
    }

    private void OnCowStyleSelected(long index)
    {
        TerrainStyleManager.CurrentStyle = (TerrainStyleManager.Style)(int)index;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm != null) gm.terrainStyle = TerrainStyleManager.CurrentStyle;
    }

    /// <summary>把管理器状态回显到两个下拉框</summary>
    private void SyncCowStyleOptions()
    {
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm != null) TerrainStyleManager.ApplyFromGameManager(gm);
        if (cowBiomeOption != null) cowBiomeOption.Selected = (int)TerrainStyleManager.CurrentBiome;
        if (cowStyleOption != null) cowStyleOption.Selected = (int)TerrainStyleManager.CurrentStyle;
    }

    /// <summary>按 CoW 目录把地形分组成"一个地形名"; 未映射的各自单列</summary>
    private void BuildTerrainGroups()
    {
        terrainGroups.Clear();
        terrainGroupOrder.Clear();
        foreach (var t in availableTerrainTypes)
        {
            string key = TerrainStyleManager.TryGetCowMapping(t, out string dir, out _)
                ? $"cow:{dir}"
                : $"other:{t}";
            if (!terrainGroups.ContainsKey(key))
            {
                terrainGroups[key] = new List<GridType>();
                terrainGroupOrder.Add(key);
            }
            terrainGroups[key].Add(t);
        }
    }

    private string GetTerrainGroupLabel(string key, List<GridType> types)
    {
        if (key.StartsWith("other:"))
        {
            return GetTerrainDisplayName(types[0]);
        }
        string dir = key.Substring(4);
        string cn = dir switch
        {
            "plains" => "平原", "forest" => "森林", "mountain" => "山脉", "sea" => "海洋",
            "street" => "道路", "river" => "河流", "beach" => "海滩", "wasteland" => "荒地",
            "ruins" => "废墟", "reaf" => "珊瑚礁", "teleporttile" => "传送点", "pipeline" => "管道",
            "bridge" => "桥梁", "plasma" => "等离子", "meteor" => "陨石", "creeper" => "蔓藤",
            "wall" => "墙体", "fog" => "雾", "desert" => "沙漠", "desert_forest" => "沙漠森林",
            "desert_path" => "沙漠道路", "desert_pipeline" => "沙漠管道", "desert_try_river" => "干涸河",
            "snow" => "雪原", "snow_forest" => "雪原森林", "snow_street" => "雪原道路",
            "snow_Pipeline" => "雪原管道", "waste" => "废土", "waste_forest" => "废土森林",
            "waste_path" => "废土道路", "waste_Pipeline" => "废土管道",
            _ => dir,
        };
        return TranslateGroup.T(cn);
    }

    private void CreateTerrainButtons()
    {
        foreach (var child in terrainButtonContainer.GetChildren())
            child.QueueFree();
        terrainButtonMap.Clear();

        BuildTerrainGroups();
        var currentTerrain = selectedGrid?.gridType ?? GridType.GROUND;
        if (expandedTerrainGroup == null)
        {
            foreach (var key in terrainGroupOrder)
                if (terrainGroups[key].Contains(currentTerrain)) { expandedTerrainGroup = key; break; }
            expandedTerrainGroup ??= terrainGroupOrder.Count > 0 ? terrainGroupOrder[0] : null;
        }

        foreach (var key in terrainGroupOrder)
        {
            var types = terrainGroups[key];
            bool expanded = key == expandedTerrainGroup;
            bool containsSelected = types.Contains(currentTerrain);

            var header = new Button();
            header.Text = $"{(expanded ? "▼" : "▶")} {GetTerrainGroupLabel(key, types)}  ({types.Count})";
            header.CustomMinimumSize = new Vector2(260, 38);
            header.AddThemeFontSizeOverride("font_size", 14);
            header.AddThemeColorOverride("font_color", containsSelected ? Colors.LimeGreen : Colors.Gold);
            string capturedKey = key;
            header.Pressed += () =>
            {
                expandedTerrainGroup = expandedTerrainGroup == capturedKey ? null : capturedKey;
                CreateTerrainButtons();
            };
            terrainButtonContainer.AddChild(header);

            if (!expanded) continue;
            foreach (var t in types)
            {
                var btn = CreateTerrainButton(t);
                btn.Text = $"   └ {btn.Text}";
                terrainButtonContainer.AddChild(btn);
                terrainButtonMap[t] = btn;
            }
            if (types.Contains(currentTerrain) && IsCowDirectional(currentTerrain))
                CreateDirectionPickerRow();
        }
    }

    /// <summary>该地形在 CoW 映射里属于方向型(有 N/E/S/W 变体)</summary>
    private bool IsCowDirectional(GridType t)
    {
        if (!TerrainStyleManager.TryGetCowMapping(t, out string dir, out _)) return false;
        return dir is "street" or "bridge" or "pipeline" or "plasma" or "wall" or "creeper"
            or "sea" or "river" or "reaf" or "teleporttile" or "meteor"
            or "snow_Pipeline" or "waste_Pipeline" or "desert_pipeline"
            or "snow_street" or "waste_path" or "desert_path";
    }

    /// <summary>方向变体选择行: 自动/手动四向开关, 手动即时预览到选中格</summary>
    private void CreateDirectionPickerRow()
    {
        dirToggleBtns.Clear();
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 3);
        row.CustomMinimumSize = new Vector2(260, 30);

        var autoBtn = new Button();
        TranslateGroup.SetText(autoBtn, "自动");
        autoBtn.CustomMinimumSize = new Vector2(52, 28);
        autoBtn.ToggleMode = true;
        autoBtn.ButtonPressed = dirAutoMode;
        autoBtn.Pressed += () =>
        {
            dirAutoMode = true;
            manualDirMask = 0;
            autoBtn.ButtonPressed = true;
            foreach (var kv in dirToggleBtns) kv.Value.ButtonPressed = false;
            ApplyManualDirectionIfAny();
        };
        row.AddChild(autoBtn);

        (int bit, string label)[] dirs = { (1, "N"), (2, "E"), (4, "S"), (8, "W") };
        foreach (var (bit, label) in dirs)
        {
            var tb = new Button();
            tb.Text = label;
            tb.CustomMinimumSize = new Vector2(44, 28);
            tb.ToggleMode = true;
            tb.ButtonPressed = !dirAutoMode && (manualDirMask & bit) != 0;
            int captured = bit;
            tb.Pressed += () =>
            {
                dirAutoMode = false;
                if (tb.ButtonPressed) manualDirMask |= captured; else manualDirMask &= ~captured;
                ApplyManualDirectionIfAny();
                if (dirHintLabel != null) dirHintLabel.Text = $"{TranslateGroup.T("手动")}= {manualDirMask}";
            };
            dirToggleBtns[bit] = tb;
            row.AddChild(tb);
        }

        dirHintLabel = new Label();
        dirHintLabel.AddThemeFontSizeOverride("font_size", 10);
        dirHintLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.8f, 0.9f));
        dirHintLabel.Text = dirAutoMode ? TranslateGroup.T("按邻格自动") : $"{TranslateGroup.T("手动")}= {manualDirMask}";
        row.AddChild(dirHintLabel);

        terrainButtonContainer.AddChild(row);
    }

    /// <summary>手动模式下把方向掩码写到目标格并即时重绘</summary>
    private void ApplyManualDirectionIfAny()
    {
        if (dirAutoMode || selectedGrid == null) return;
        selectedGrid.terrainDirection = manualDirMask;
        selectedGrid.UpdateCowTerrainIcon();
        selectedGrid.UpdateDirectionVariant();
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
        AttachTerrainPreview(btn, terrainType);

        // ✅ 地形贴图预览图标：复用 grid.tscn 的 terrainIconTextures 映射（与地图实际显示一致），取不到则保持纯文字
        var terrainTex = GetTerrainIconTexture(terrainType);
        if (terrainTex != null)
        {
            var iconRect = new TextureRect();
            iconRect.Name = "TerrainIcon";
            iconRect.Texture = terrainTex;
            iconRect.Position = new Vector2(14, (36 - terrainIconPreviewSize) / 2f);
            iconRect.Size = new Vector2(terrainIconPreviewSize, terrainIconPreviewSize);
            iconRect.CustomMinimumSize = new Vector2(terrainIconPreviewSize, terrainIconPreviewSize);
            iconRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            iconRect.MouseFilter = MouseFilterEnum.Ignore;
            btn.AddChild(iconRect);

            // ✅ 文字右移避开图标（保留 emoji 文字，图标是"加"不是"换"）
            float textMargin = 14 + terrainIconPreviewSize + 6;
            normalStyle.ContentMarginLeft = textMargin;
            hoverStyle.ContentMarginLeft = textMargin;
            pressedStyle.ContentMarginLeft = textMargin;
        }

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
            GridType.WALL => TranslateGroup.T("🧱 墙"),
            GridType.WEAK_WALL => TranslateGroup.T("🧱 脆弱墙"),
            GridType.WASTELAND => TranslateGroup.T("🏜️ 荒地"),
            GridType.RUINS2 => TranslateGroup.T("🏚️ 废墟·变体2"),
            GridType.RUINS3 => TranslateGroup.T("🏚️ 废墟·变体3"),
            GridType.DESERT_GROUND => TranslateGroup.T("🏜️ 沙漠平原"),
            GridType.DESERT_ROAD => TranslateGroup.T("🏜️ 沙漠公路"),
            GridType.DESERT_FOREST => TranslateGroup.T("🏜️ 沙漠森林"),
            GridType.DESERT_HILL => TranslateGroup.T("🏜️ 沙漠山地"),
            GridType.DESERT_RUINS => TranslateGroup.T("🏜️ 沙漠废墟"),
            GridType.DESERT_WASTELAND => TranslateGroup.T("🏜️ 沙漠荒地"),
            GridType.DESERT_PIPE => TranslateGroup.T("🏜️ 沙漠管道"),
            GridType.DESERT_PIPESEAM => TranslateGroup.T("🏜️ 沙漠管道接缝"),
            GridType.DESERT_BROKENPIPE => TranslateGroup.T("🏜️ 沙漠破碎管道"),
            GridType.SNOW_GROUND => TranslateGroup.T("❄️ 雪平原"),
            GridType.SNOW_ROAD => TranslateGroup.T("❄️ 雪街道"),
            GridType.SNOW_FOREST => TranslateGroup.T("❄️ 雪森林"),
            GridType.SNOW_HILL => TranslateGroup.T("❄️ 雪山地"),
            GridType.SNOW_RUINS => TranslateGroup.T("❄️ 雪废墟"),
            GridType.SNOW_WASTELAND => TranslateGroup.T("❄️ 雪荒地"),
            GridType.SNOW_PIPE => TranslateGroup.T("❄️ 雪管道"),
            GridType.SNOW_PIPESEAM => TranslateGroup.T("❄️ 雪管道接缝"),
            GridType.SNOW_BROKENPIPE => TranslateGroup.T("❄️ 雪破碎管道"),
            GridType.WASTE_GROUND => TranslateGroup.T("☢️ 废土平原"),
            GridType.WASTE_ROAD => TranslateGroup.T("☢️ 废土公路"),
            GridType.WASTE_FOREST => TranslateGroup.T("☢️ 废土森林"),
            GridType.WASTE_HILL => TranslateGroup.T("☢️ 废土山地"),
            GridType.WASTE_RUINS => TranslateGroup.T("☢️ 废土废墟"),
            GridType.WASTE_WASTELAND => TranslateGroup.T("☢️ 废土荒地"),
            GridType.WASTE_PIPE => TranslateGroup.T("☢️ 废土管道"),
            GridType.WASTE_PIPESEAM => TranslateGroup.T("☢️ 废土管道接缝"),
            GridType.WASTE_BROKENPIPE => TranslateGroup.T("☢️ 废土破碎管道"),
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
            GridType.WALL => 0.00f,
            GridType.WEAK_WALL => 0.00f,
            GridType.WASTELAND => 0.20f,
            GridType.RUINS2 => 0.30f,
            GridType.RUINS3 => 0.30f,
            GridType.DESERT_GROUND => 0.10f,
            GridType.SNOW_GROUND => 0.10f,
            GridType.WASTE_GROUND => 0.10f,
            GridType.DESERT_ROAD => 0.00f,
            GridType.SNOW_ROAD => 0.00f,
            GridType.WASTE_ROAD => 0.00f,
            GridType.DESERT_FOREST => 0.20f,
            GridType.SNOW_FOREST => 0.20f,
            GridType.WASTE_FOREST => 0.20f,
            GridType.DESERT_HILL => 0.40f,
            GridType.SNOW_HILL => 0.40f,
            GridType.WASTE_HILL => 0.40f,
            GridType.DESERT_RUINS => 0.30f,
            GridType.SNOW_RUINS => 0.30f,
            GridType.WASTE_RUINS => 0.30f,
            GridType.DESERT_WASTELAND => 0.20f,
            GridType.SNOW_WASTELAND => 0.20f,
            GridType.WASTE_WASTELAND => 0.20f,
            GridType.DESERT_PIPE => 0.00f,
            GridType.SNOW_PIPE => 0.00f,
            GridType.WASTE_PIPE => 0.00f,
            GridType.DESERT_PIPESEAM => 0.10f,
            GridType.SNOW_PIPESEAM => 0.10f,
            GridType.WASTE_PIPESEAM => 0.10f,
            GridType.DESERT_BROKENPIPE => 0.20f,
            GridType.SNOW_BROKENPIPE => 0.20f,
            GridType.WASTE_BROKENPIPE => 0.20f,
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

    // ✅ 放置即烘焙当前画笔风格, 之后切换风格不影响本地块
    selectedGrid.cowBakedBiome = (int)TerrainStyleManager.CurrentBiome;
    selectedGrid.cowBakedStyle = (int)TerrainStyleManager.CurrentStyle;

    UpdateGridVisual(selectedGrid);

    // ✅ 换地形后方向选择器回到自动, 并重建分组列表(刷新方向行)
    dirAutoMode = true;
    manualDirMask = 0;
    CreateTerrainButtons();

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
