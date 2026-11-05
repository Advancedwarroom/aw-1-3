using Godot;
using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;

// TerrainEditor · BatchTerrain（由 TerrainEditor.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class TerrainEditor
{

    // ========== ✅ 批量地形放置模式 ==========
    private bool isBatchTerrainMode = false;
    private GridType batchTerrainType = GridType.GROUND;
    private Grids lastBatchGrid = null;
    private Button batchTerrainBtn;
    private Panel batchTerrainPanel;
    private VBoxContainer batchTerrainButtonContainer;
    private Label batchTerrainTitleLabel;
    private Button batchTerrainCloseBtn;
    private Label batchTerrainHint;
    private OptionButton batchStyleOption;
    private OptionButton batchBiomeOption;
    private OptionButton batchVariantOption;
    private bool batchVariantAuto = true;
    private string batchVariantTexturePath = "";

    // ========== ✅ 提取地形功能 ==========
    public SavedGridTerrainData ExtractedTerrain { get; private set; } = null;

    // ========== ✅ 批量地形放置系统 ==========
    private void CreateBatchTerrainButton()
    {
        batchTerrainBtn = new Button();
        batchTerrainBtn.Name = "BatchTerrainBtn";
        TranslateGroup.SetText(batchTerrainBtn, "🗺️ 批量地形");
        batchTerrainBtn.CustomMinimumSize = new Vector2(140, 50);

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.3f, 0.5f, 0.5f, 0.9f);
        style.SetCornerRadiusAll(8);
        batchTerrainBtn.AddThemeStyleboxOverride("normal", style);

        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = new Color(0.4f, 0.6f, 0.6f, 0.95f);
        hoverStyle.SetCornerRadiusAll(8);
        batchTerrainBtn.AddThemeStyleboxOverride("hover", hoverStyle);

        batchTerrainBtn.AddThemeFontSizeOverride("font_size", 14);
        batchTerrainBtn.AddThemeColorOverride("font_color", Colors.White);
        batchTerrainBtn.Pressed += OnBatchTerrainBtnPressed;

        AddChild(batchTerrainBtn);
    }

    private void OnBatchTerrainBtnPressed()
    {
        if (isBatchTerrainMode)
        {
            StopBatchTerrainMode();
        }
        else
        {
            OpenBatchTerrainPanel();
        }
    }

    private void CreateBatchTerrainPanel()
    {
        batchTerrainPanel = new Panel();
        batchTerrainPanel.Name = "BatchTerrainPanel";
        batchTerrainPanel.CustomMinimumSize = new Vector2(340, 600);
        batchTerrainPanel.SetAnchorsPreset(LayoutPreset.Center);
        batchTerrainPanel.Visible = false;
        batchTerrainPanel.ZIndex = 1000;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.1f, 0.12f, 0.18f, 0.97f);
        panelStyle.SetCornerRadiusAll(16);
        panelStyle.SetBorderWidthAll(3);
        panelStyle.BorderColor = new Color(0.5f, 0.8f, 0.6f);
        batchTerrainPanel.AddThemeStyleboxOverride("panel", panelStyle);

        // 标题
        batchTerrainTitleLabel = new Label();
        TranslateGroup.SetText(batchTerrainTitleLabel, "🗺️ 批量地形放置");
        batchTerrainTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        batchTerrainTitleLabel.AddThemeFontSizeOverride("font_size", 20);
        batchTerrainTitleLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.9f, 0.7f));
        batchTerrainTitleLabel.CustomMinimumSize = new Vector2(0, 40);
        batchTerrainTitleLabel.Position = new Vector2(0, 15);
        batchTerrainTitleLabel.Size = new Vector2(340, 40);
        batchTerrainPanel.AddChild(batchTerrainTitleLabel);

        // 关闭按钮
        batchTerrainCloseBtn = new Button();
        batchTerrainCloseBtn.Text = "✕";
        batchTerrainCloseBtn.CustomMinimumSize = new Vector2(32, 32);
        batchTerrainCloseBtn.Position = new Vector2(300, 12);
        batchTerrainCloseBtn.Pressed += CloseBatchTerrainPanel;
        batchTerrainPanel.AddChild(batchTerrainCloseBtn);

        // 提示
        var hint = new Label();
        TranslateGroup.SetText(hint, "🖱️ 点击地形选择，在地图滑动铺设\nESC 结束批量放置");
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.AddThemeColorOverride("font_color", new Color(0.7f, 0.8f, 0.6f));
        hint.Position = new Vector2(10, 55);
        hint.Size = new Vector2(320, 40);
        batchTerrainPanel.AddChild(hint);

        CreateBatchConfigRow("🎨 风格",100,out batchStyleOption);
        foreach(var s in Enum.GetValues<TerrainStyleManager.Style>())batchStyleOption.AddItem(s.ToString());
        batchStyleOption.Selected=(int)TerrainStyleManager.CurrentStyle;
        batchStyleOption.ItemSelected+=idx=>{if(Enum.IsDefined(typeof(TerrainStyleManager.Style),(int)idx))TerrainStyleManager.CurrentStyle=(TerrainStyleManager.Style)(int)idx;};
        CreateBatchConfigRow("🌦️ 天气",132,out batchBiomeOption);
        string[] biomeNames={"常规","雪","雨","废土","沙漠"};
        foreach(var n in biomeNames)batchBiomeOption.AddItem(n);
        batchBiomeOption.Selected=(int)TerrainStyleManager.CurrentBiome;
        batchBiomeOption.ItemSelected+=idx=>{if(Enum.IsDefined(typeof(TerrainStyleManager.Biome),(int)idx))TerrainStyleManager.CurrentBiome=(TerrainStyleManager.Biome)(int)idx;};
        CreateBatchConfigRow("🧩 贴图配置",164,out batchVariantOption);
        batchVariantOption.ItemSelected+=OnBatchVariantSelected;
        var scroll=new ScrollContainer();
        scroll.Position=new Vector2(10,200);scroll.Size=new Vector2(320,280);
        scroll.HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled;

        batchTerrainButtonContainer = new VBoxContainer();
        batchTerrainButtonContainer.AddThemeConstantOverride("separation", 6);
        batchTerrainButtonContainer.Size = new Vector2(320, 0);

        foreach (var terrainType in availableTerrainTypes)
        {
            var btn = CreateBatchTerrainSelectButton(terrainType);
            batchTerrainButtonContainer.AddChild(btn);
        }

        scroll.AddChild(batchTerrainButtonContainer);
        batchTerrainPanel.AddChild(scroll);

        // 提取当前地形按钮
        var extractBtn = new Button();
        TranslateGroup.SetText(extractBtn, "📋 提取当前选中地形");
        extractBtn.CustomMinimumSize = new Vector2(300, 36);
        extractBtn.Position = new Vector2(20, 525);
        extractBtn.AddThemeFontSizeOverride("font_size", 12);
        var extractStyle = new StyleBoxFlat();
        extractStyle.BgColor = new Color(0.3f, 0.5f, 0.7f, 0.9f);
        extractStyle.SetCornerRadiusAll(8);
        extractBtn.AddThemeStyleboxOverride("normal", extractStyle);
        extractBtn.Pressed += () => {
            if (selectedGrid != null)
                ExtractTerrainFromGrid(selectedGrid);
            else
                TranslateGroup.SetText(spawnHintLabel, "❌ 请先点击地图选择一个格子");
        };
        batchTerrainPanel.AddChild(extractBtn);

        // 结束放置按钮
        var endBtn = new Button();
        TranslateGroup.SetText(endBtn, "❌ 结束放置");
        endBtn.CustomMinimumSize = new Vector2(300, 40);
        endBtn.Position = new Vector2(20, 565);
        endBtn.AddThemeFontSizeOverride("font_size", 14);
        endBtn.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.6f));
        var endStyle = new StyleBoxFlat();
        endStyle.BgColor = new Color(0.4f, 0.2f, 0.2f, 0.9f);
        endStyle.SetCornerRadiusAll(8);
        endBtn.AddThemeStyleboxOverride("normal", endStyle);
        endBtn.Pressed += StopBatchTerrainMode;
        batchTerrainPanel.AddChild(endBtn);

        // ✅ 拖拽手柄（只在标题栏区域可拖拽）
        var dragHandle = new Panel();
        dragHandle.Name = "DragHandle";
        dragHandle.CustomMinimumSize = new Vector2(340, 55);
        dragHandle.Position = new Vector2(0, 0);
        dragHandle.MouseFilter = MouseFilterEnum.Pass;
        var handleStyle = new StyleBoxFlat();
        handleStyle.BgColor = new Color(0.3f, 0.3f, 0.4f, 0.3f);
        handleStyle.SetCornerRadiusAll(16);
        dragHandle.AddThemeStyleboxOverride("panel", handleStyle);
        var dragHint = new Label();
        TranslateGroup.SetText(dragHint, "⋮⋮ 拖拽移动");
        dragHint.AddThemeFontSizeOverride("font_size", 10);
        dragHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.8f));
        dragHint.Position = new Vector2(8, 2);
        dragHandle.AddChild(dragHint);
        batchTerrainPanel.AddChild(dragHandle);

        AddChild(batchTerrainPanel);
        MakeMenuDraggable(batchTerrainPanel, dragHandle);
    }

    private void CreateBatchConfigRow(string labelText,int y,out OptionButton option)
    {
        var label=new Label();TranslateGroup.SetText(label,labelText);label.AddThemeFontSizeOverride("font_size",12);
        label.AddThemeColorOverride("font_color",new Color(0.7f,0.85f,0.75f));label.Position=new Vector2(16,y+4);
        label.Size=new Vector2(76,24);batchTerrainPanel.AddChild(label);
        option=new OptionButton();option.Position=new Vector2(96,y);option.Size=new Vector2(228,28);
        option.AddThemeFontSizeOverride("font_size",11);batchTerrainPanel.AddChild(option);
    }
    private void RebuildBatchVariantOptions()
    {
        if(batchVariantOption==null)return;batchVariantOption.Clear();batchVariantOption.AddItem("🔄 自动优化（邻格适配）");
        if(TerrainStyleManager.TryGetCowMapping(batchTerrainType,out string cowDir,out _))
        {
            string dirFs=ProjectSettings.GlobalizePath($"{TerrainStyleManager.CowRoot}/{cowDir}");
            if(Directory.Exists(dirFs)){int idx=1;foreach(var f in Directory.GetFiles(dirFs,"*.png").OrderBy(f=>f)){
                string resPath=$"{TerrainStyleManager.CowRoot}/{cowDir}/{Path.GetFileName(f)}";
                string display=Path.GetFileNameWithoutExtension(f);if(display.Length>30)display=display[..30]+"…";
                batchVariantOption.AddItem(display);var tex=ResourceLoader.Exists(resPath)?GD.Load<Texture2D>(resPath):null;
                if(tex!=null)batchVariantOption.SetItemIcon(idx,tex);batchVariantOption.SetItemMetadata(idx,resPath);idx++;
            }}
        }
        batchVariantOption.Selected=0;batchVariantAuto=true;batchVariantTexturePath="";
    }
    private void OnBatchVariantSelected(long idx)
    {
        if(idx==0){batchVariantAuto=true;batchVariantTexturePath="";}
        else{batchVariantAuto=false;batchVariantTexturePath=batchVariantOption.GetItemMetadata((int)idx).AsString();}
    }
    private void ApplyBatchDirectionConfig(Grids grid)
    {
        if(grid==null)return;
        if(batchVariantAuto){grid.autoOptimizeDirection=true;grid.manualCowTexturePath="";}
        else{grid.autoOptimizeDirection=false;grid.manualCowTexturePath=batchVariantTexturePath;}
        var map=gridManager?.map;if(map==null)return;var gi=grid.GridIndex;
        for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++){
            int nx=gi.X+dx,ny=gi.Y+dy;
            if(nx>=0&&nx<map.GetLength(0)&&ny>=0&&ny<map.GetLength(1))map[nx,ny]?.RefreshDirectionFromNeighbors(map);
        }
    }
    private Button CreateBatchTerrainSelectButton(GridType terrainType)
    {
        var btn = new Button();
        btn.CustomMinimumSize = new Vector2(300, 42);
        btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        // 获取地形颜色和防御信息
        Color terrainColor = terrainColors.GetValueOrDefault(terrainType, Colors.White);
        string defenseText = GetTerrainDefenseText(terrainType);

        btn.Text = $"  {GetTerrainDisplayName(terrainType)}  {defenseText}";
        btn.AddThemeFontSizeOverride("font_size", 13);
        btn.AddThemeColorOverride("font_color", Colors.White);
        btn.Alignment = HorizontalAlignment.Left;

        // 使用地形颜色作为按钮背景
        var normalStyle = new StyleBoxFlat();
        normalStyle.BgColor = new Color(terrainColor.R * 0.3f, terrainColor.G * 0.3f, terrainColor.B * 0.3f, 0.85f);
        normalStyle.SetCornerRadiusAll(6);
        btn.AddThemeStyleboxOverride("normal", normalStyle);

        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = new Color(terrainColor.R * 0.5f, terrainColor.G * 0.5f, terrainColor.B * 0.5f, 0.95f);
        hoverStyle.SetCornerRadiusAll(6);
        btn.AddThemeStyleboxOverride("hover", hoverStyle);

        var pressedStyle = new StyleBoxFlat();
        pressedStyle.BgColor = new Color(terrainColor.R * 0.7f, terrainColor.G * 0.7f, terrainColor.B * 0.7f, 1.0f);
        pressedStyle.SetCornerRadiusAll(6);
        pressedStyle.SetBorderWidthAll(2);
        pressedStyle.BorderColor = Colors.Yellow;
        btn.AddThemeStyleboxOverride("pressed", pressedStyle);

        GridType capturedType = terrainType;
        btn.Pressed += () => StartBatchTerrainMode(capturedType);

        return btn;
    }

    private void OpenBatchTerrainPanel()
    {
        if (batchTerrainPanel == null)
            CreateBatchTerrainPanel();

        CloseAllMenus();
        batchTerrainPanel.Visible = true;
        isMenuOpen = true;

        // 更新标题显示当前状态
        if (isBatchTerrainMode)
        {
            batchTerrainTitleLabel.Text = $"🗺️ {TranslateGroup.T("批量地形")} - {TranslateGroup.T("当前")}: {GetTerrainDisplayName(batchTerrainType)}";
        }
        else
        {
            TranslateGroup.SetText(batchTerrainTitleLabel, "🗺️ 批量地形放置");
        }
    }

    private void CloseBatchTerrainPanel()
    {
        if (batchTerrainPanel != null)
            batchTerrainPanel.Visible = false;
        if (!isBatchTerrainMode)
            isMenuOpen = false;
    }

    private void StartBatchTerrainMode(GridType type)
    {
        batchTerrainType = type;
        isBatchTerrainMode = true;
        lastBatchGrid = null;
        RebuildBatchVariantOptions();
        CloseBatchTerrainPanel();
        UpdateBatchTerrainButtonVisual();
    }

    private void StopBatchTerrainMode()
    {
        isBatchTerrainMode = false;
        batchTerrainType = GridType.GROUND;
        lastBatchGrid = null;
        CloseBatchTerrainPanel();
        UpdateBatchTerrainButtonVisual();
    }

    public void ExtractTerrainFromGrid(Grids grid)
    {
        if (grid == null) return;
        ExtractedTerrain = new SavedGridTerrainData
        {
            slotName = "extracted",
            displayName = $"{TranslateGroup.T("提取")}:{grid.gridType}",
            gridType = grid.gridType,
            canDestroyUnit = grid.canDestroyUnit,
            fixedDamagePerTurn = grid.fixedDamagePerTurn,
            fixedAttackPerTurn = grid.fixedAttackPerTurn,
            fixedMaxHealthChangePerTurn = grid.fixedMaxHealthChangePerTurn,
            ammoChangePrimary = grid.ammoChangePrimary,
            ammoChangeExplode = grid.ammoChangeExplode,
            ammoChangeFlare = grid.ammoChangeFlare,
            ammoChangeStealth = grid.ammoChangeStealth,
            ammoChangeReveal = grid.ammoChangeReveal,
            ammoChangeCamo = grid.ammoChangeCamo,
            ammoChangeProduction = grid.ammoChangeProduction,
            ammoChangeConstruction = grid.ammoChangeConstruction,
            ammoMaxChangePrimary = grid.ammoMaxChangePrimary,
            ammoMaxChangeExplode = grid.ammoMaxChangeExplode,
            ammoMaxChangeFlare = grid.ammoMaxChangeFlare,
            ammoMaxChangeStealth = grid.ammoMaxChangeStealth,
            ammoMaxChangeReveal = grid.ammoMaxChangeReveal,
            ammoMaxChangeCamo = grid.ammoMaxChangeCamo,
            ammoMaxChangeProduction = grid.ammoMaxChangeProduction,
            ammoMaxChangeConstruction = grid.ammoMaxChangeConstruction,
            fixedFuelChangePerTurn = grid.fixedFuelChangePerTurn,
            fixedMaxFuelChangePerTurn = grid.fixedMaxFuelChangePerTurn,
            visionBonus = grid.visionBonus,
            savedTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds()
        };
        batchTerrainType = grid.gridType;
        RebuildBatchVariantOptions();
        if (batchTerrainBtn != null)
            batchTerrainBtn.Text = $"🗺️ {GetTerrainDisplayName(grid.gridType)}";
        spawnHintLabel.Text = $"✅ {TranslateGroup.T("已提取地形")}: {GetTerrainDisplayName(grid.gridType)}，{TranslateGroup.T("点击批量地形按钮直接铺设")}";
    }

    private void UpdateBatchTerrainButtonVisual()
    {
        if (batchTerrainBtn == null) return;

        if (isBatchTerrainMode)
        {
            batchTerrainBtn.Text = $"🗺️ {GetTerrainDisplayName(batchTerrainType)}";
            var activeStyle = new StyleBoxFlat();
            activeStyle.BgColor = new Color(0.5f, 0.8f, 0.5f, 0.95f);
            activeStyle.SetCornerRadiusAll(8);
            activeStyle.SetBorderWidthAll(2);
            activeStyle.BorderColor = Colors.Yellow;
            batchTerrainBtn.AddThemeStyleboxOverride("normal", activeStyle);
        }
        else
        {
            TranslateGroup.SetText(batchTerrainBtn, "🗺️ 批量地形");
            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.3f, 0.5f, 0.5f, 0.9f);
            style.SetCornerRadiusAll(8);
            batchTerrainBtn.AddThemeStyleboxOverride("normal", style);
        }
    }
}
