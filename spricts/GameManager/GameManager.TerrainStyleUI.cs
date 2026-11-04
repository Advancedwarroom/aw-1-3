using Godot;

// GameManager · 游戏内"全图换风格"面板(非地图编辑器): 右上角🎨按钮 + 气候/风格下拉 + 应用
// 语义: 覆盖全图所有已放地块(Grids.RebakeCowStyle); 编辑器里的"画笔"切换互不影响
public partial class GameManager
{
    private Button terrainStyleBtn;
    private Panel terrainStylePanel;
    private OptionButton gsBiomeOption;
    private OptionButton gsStyleOption;

    private void CreateTerrainStyleUI()
    {
        var canvasLayer = GetTree()?.CurrentScene?.GetNodeOrNull<CanvasLayer>("CanvasLayer");
        if (canvasLayer == null) return;

        terrainStyleBtn = new Button();
        terrainStyleBtn.Name = "TerrainStyleBtn";
        TranslateGroup.SetText(terrainStyleBtn, "🎨 风格");
        terrainStyleBtn.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        terrainStyleBtn.OffsetLeft = -580;
        terrainStyleBtn.OffsetTop = 36;
        terrainStyleBtn.OffsetRight = -420;
        terrainStyleBtn.OffsetBottom = 72;
        terrainStyleBtn.AddThemeFontSizeOverride("font_size", 16);
        terrainStyleBtn.AddThemeColorOverride("font_color", Colors.White);
        var s = new StyleBoxFlat();
        s.BgColor = new Color(0.35f, 0.45f, 0.85f, 0.9f);
        s.SetCornerRadiusAll(8);
        terrainStyleBtn.AddThemeStyleboxOverride("normal", s);
        var sh = new StyleBoxFlat();
        sh.BgColor = new Color(0.45f, 0.55f, 0.95f, 0.95f);
        sh.SetCornerRadiusAll(8);
        terrainStyleBtn.AddThemeStyleboxOverride("hover", sh);
        terrainStyleBtn.ZIndex = 200;
        terrainStyleBtn.Pressed += ToggleTerrainStylePanel;
        canvasLayer.AddChild(terrainStyleBtn);

        terrainStylePanel = new Panel();
        terrainStylePanel.Name = "TerrainStylePanel";
        terrainStylePanel.CustomMinimumSize = new Vector2(300, 210);
        terrainStylePanel.SetAnchorsPreset(Control.LayoutPreset.Center);
        terrainStylePanel.Visible = false;
        terrainStylePanel.ZIndex = 1000;
        var ps = new StyleBoxFlat();
        ps.BgColor = new Color(0.15f, 0.15f, 0.2f, 0.95f);
        ps.SetCornerRadiusAll(12);
        ps.SetBorderWidthAll(2);
        ps.BorderColor = new Color(0.4f, 0.4f, 0.5f);
        terrainStylePanel.AddThemeStyleboxOverride("panel", ps);

        var title = new Label();
        TranslateGroup.SetText(title, "全图地形风格");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 18);
        title.AddThemeColorOverride("font_color", Colors.Yellow);
        title.Position = new Vector2(0, 10);
        title.Size = new Vector2(300, 30);
        terrainStylePanel.AddChild(title);

        var hint = new Label();
        TranslateGroup.SetText(hint, "⚠ 覆盖全图已放地块, 与编辑器画笔无关");
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.AddThemeColorOverride("font_color", new Color(1f, 0.7f, 0.4f));
        hint.Position = new Vector2(0, 40);
        hint.Size = new Vector2(300, 20);
        terrainStylePanel.AddChild(hint);

        var biomeLabel = new Label();
        TranslateGroup.SetText(biomeLabel, "气候");
        biomeLabel.AddThemeFontSizeOverride("font_size", 14);
        biomeLabel.Position = new Vector2(20, 72);
        terrainStylePanel.AddChild(biomeLabel);

        gsBiomeOption = new OptionButton();
        gsBiomeOption.Position = new Vector2(70, 68);
        gsBiomeOption.Size = new Vector2(210, 34);
        string[] biomeNames = { "常规", "雪", "雨", "废土", "沙漠" };
        foreach (var n in biomeNames) gsBiomeOption.AddItem(TranslateGroup.T(n));
        terrainStylePanel.AddChild(gsBiomeOption);

        var styleLabel = new Label();
        TranslateGroup.SetText(styleLabel, "风格");
        styleLabel.AddThemeFontSizeOverride("font_size", 14);
        styleLabel.Position = new Vector2(20, 116);
        terrainStylePanel.AddChild(styleLabel);

        gsStyleOption = new OptionButton();
        gsStyleOption.Position = new Vector2(70, 112);
        gsStyleOption.Size = new Vector2(210, 34);
        string[] styleNames = { "AW1", "SFC", "AW2", "DS", "DoR" };
        foreach (var n in styleNames) gsStyleOption.AddItem(n);
        terrainStylePanel.AddChild(gsStyleOption);

        var applyBtn = new Button();
        TranslateGroup.SetText(applyBtn, "✅ 应用全图");
        applyBtn.Position = new Vector2(20, 158);
        applyBtn.Size = new Vector2(180, 38);
        applyBtn.AddThemeFontSizeOverride("font_size", 15);
        applyBtn.Pressed += OnApplyGlobalTerrainStyle;
        terrainStylePanel.AddChild(applyBtn);

        var closeBtn = new Button();
        closeBtn.Text = "✕";
        closeBtn.Position = new Vector2(210, 158);
        closeBtn.Size = new Vector2(70, 38);
        closeBtn.Pressed += () => terrainStylePanel.Visible = false;
        terrainStylePanel.AddChild(closeBtn);

        canvasLayer.AddChild(terrainStylePanel);
    }

    private void ToggleTerrainStylePanel()
    {
        if (terrainStylePanel == null) return;
        terrainStylePanel.Visible = !terrainStylePanel.Visible;
        if (terrainStylePanel.Visible)
        {
            // 回显当前全局风格
            if (gsBiomeOption != null) gsBiomeOption.Selected = (int)terrainBiome;
            if (gsStyleOption != null) gsStyleOption.Selected = (int)terrainStyle;
        }
    }

    private void OnApplyGlobalTerrainStyle()
    {
        ApplyGlobalTerrainStyle(gsBiomeOption.Selected, gsStyleOption.Selected);
        terrainStylePanel.Visible = false;
    }
}
