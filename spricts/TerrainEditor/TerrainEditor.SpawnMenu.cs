using Godot;
using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;

// TerrainEditor · SpawnMenu（由 TerrainEditor.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class TerrainEditor
{
// ========== 创建生成菜单 ==========
private void CreateSpawnMenu()
{
    spawnMenuPanel = new Panel();
    spawnMenuPanel.Name = "SpawnMenuPanel";
    spawnMenuPanel.CustomMinimumSize = new Vector2(400, 600);
    spawnMenuPanel.SetAnchorsPreset(LayoutPreset.Center);
    spawnMenuPanel.Visible = false;
    spawnMenuPanel.ZIndex = 1200;

    var panelStyle = new StyleBoxFlat();
    panelStyle.BgColor = new Color(0.1f, 0.12f, 0.18f, 0.97f);
    panelStyle.SetCornerRadiusAll(16);
    panelStyle.SetBorderWidthAll(3);
    panelStyle.BorderColor = new Color(0.4f, 0.7f, 0.9f);
    spawnMenuPanel.AddThemeStyleboxOverride("panel", panelStyle);

    // 标题
    spawnTitleLabel = new Label();
    TranslateGroup.SetText(spawnTitleLabel, "🎮 单位/兵器/设施生成器");
    spawnTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
    spawnTitleLabel.AddThemeFontSizeOverride("font_size", 20);
    spawnTitleLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.9f, 1f));
    spawnTitleLabel.CustomMinimumSize = new Vector2(0, 35);
    spawnTitleLabel.Position = new Vector2(0, 12);
    spawnTitleLabel.Size = new Vector2(320, 35);
    spawnMenuPanel.AddChild(spawnTitleLabel);


    // 模式选择器
    spawnModeSelector = new HBoxContainer();
    spawnModeSelector.Position = new Vector2(15, 50);
    spawnModeSelector.Size = new Vector2(370, 40);
    spawnModeSelector.AddThemeConstantOverride("separation", 8);

    spawnModeUnitBtn = new Button();
    TranslateGroup.SetText(spawnModeUnitBtn, "🚶 生成单位");
    spawnModeUnitBtn.CustomMinimumSize = new Vector2(90, 36);
    spawnModeUnitBtn.AddThemeFontSizeOverride("font_size", 12);
    spawnModeUnitBtn.Pressed += () => SwitchSpawnMode(SpawnMode.Unit);
    spawnModeSelector.AddChild(spawnModeUnitBtn);

    spawnModeWeaponBtn = new Button();
    TranslateGroup.SetText(spawnModeWeaponBtn, "⚔️ 生成兵器");
    spawnModeWeaponBtn.CustomMinimumSize = new Vector2(90, 36);
    spawnModeWeaponBtn.AddThemeFontSizeOverride("font_size", 12);
    spawnModeWeaponBtn.Pressed += () => SwitchSpawnMode(SpawnMode.Weapon);
    spawnModeSelector.AddChild(spawnModeWeaponBtn);

    spawnModeFacilityBtn = new Button();
    TranslateGroup.SetText(spawnModeFacilityBtn, "🏙️ 生成设施");
    spawnModeFacilityBtn.CustomMinimumSize = new Vector2(90, 36);
    spawnModeFacilityBtn.AddThemeFontSizeOverride("font_size", 12);
    spawnModeFacilityBtn.Pressed += () => SwitchSpawnMode(SpawnMode.Facility);
    spawnModeSelector.AddChild(spawnModeFacilityBtn);

    destroyModeBtn = new Button();
    TranslateGroup.SetText(destroyModeBtn, "💀 销毁模式");
    destroyModeBtn.CustomMinimumSize = new Vector2(90, 36);
    destroyModeBtn.AddThemeFontSizeOverride("font_size", 12);
    destroyModeBtn.Pressed += ToggleDestroyMode;
    spawnModeSelector.AddChild(destroyModeBtn);

    spawnMenuPanel.AddChild(spawnModeSelector);

    // ✅ 方向选择器（黑炮/大型黑炮专用）
    directionSelector = new HBoxContainer();
    directionSelector.Position = new Vector2(60, 92);
    directionSelector.Size = new Vector2(280, 30);
    directionSelector.AddThemeConstantOverride("separation", 6);
    directionSelector.Visible = false;

    var dirLabel = new Label();
    TranslateGroup.SetText(dirLabel, "朝向:");
    dirLabel.AddThemeFontSizeOverride("font_size", 12);
    dirLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.6f));
    dirLabel.CustomMinimumSize = new Vector2(40, 24);
    directionSelector.AddChild(dirLabel);

    string[] dirLabels = { TranslateGroup.T("⬆️ 上"), TranslateGroup.T("➡️ 右"), TranslateGroup.T("⬇️ 下"), TranslateGroup.T("⬅️ 左") };
    for (int i = 0; i < 4; i++)
    {
        int dirIndex = i; // 闭包捕获
        var dirBtn = new Button();
        dirBtn.Text = dirLabels[i];
        dirBtn.CustomMinimumSize = new Vector2(50, 26);
        dirBtn.AddThemeFontSizeOverride("font_size", 10);
        dirBtn.Pressed += () => {
            pendingCannonDirection = (BlackCannon.CannonDirection)dirIndex;
            UpdateDirectionButtonStyles();
        };
        dirBtn.Name = $"DirBtn_{dirIndex}";
        directionSelector.AddChild(dirBtn);
    }

    spawnMenuPanel.AddChild(directionSelector);

    // 提示标签
    spawnHintLabel = new Label();
    TranslateGroup.SetText(spawnHintLabel, "选择模式开始...");
    spawnHintLabel.HorizontalAlignment = HorizontalAlignment.Center;
    spawnHintLabel.AddThemeFontSizeOverride("font_size", 12);
    spawnHintLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.6f));
    spawnHintLabel.Position = new Vector2(10, 125);
    spawnHintLabel.Size = new Vector2(380, 25);
    spawnMenuPanel.AddChild(spawnHintLabel);

    // ✅ 搜索框（过滤当前模式的模板列表；中英文/场景名均可搜）
    spawnSearchBox = new LineEdit();
    spawnSearchBox.Name = "SpawnSearchBox";
    spawnSearchBox.PlaceholderText = TranslateGroup.T("🔍 搜索：单位/兵器/设施（如 港口/Port）");
    spawnSearchBox.CustomMinimumSize = new Vector2(370, 30);
    spawnSearchBox.Position = new Vector2(15, 152);
    spawnSearchBox.Size = new Vector2(370, 30);
    spawnSearchBox.AddThemeFontSizeOverride("font_size", 13);
    spawnSearchBox.TextChanged += text => RefreshSpawnButtons();
    spawnMenuPanel.AddChild(spawnSearchBox);

    // 滚动容器
    var scroll = new ScrollContainer();
    scroll.Position = new Vector2(15, 190);
    scroll.Size = new Vector2(370, 400);
    scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

    spawnButtonContainer = new VBoxContainer();
    spawnButtonContainer.AddThemeConstantOverride("separation", 6);
    spawnButtonContainer.Size = new Vector2(370, 0);

    scroll.AddChild(spawnButtonContainer);
    spawnMenuPanel.AddChild(scroll);

    var dragHandle = new Panel();
    dragHandle.Name = "DragHandle";
    dragHandle.CustomMinimumSize = new Vector2(400, 35);
    dragHandle.Position = new Vector2(0, 0);
    dragHandle.MouseFilter = MouseFilterEnum.Pass;
    spawnMenuPanel.AddChild(dragHandle);

    spawnTitleLabel.Position = new Vector2(0, 12 + 35);
    spawnTitleLabel.Size = new Vector2(400, 35);

    var closeBtn = new Button();
    closeBtn.Name = "CloseButton";
    closeBtn.Text = "✕";
    closeBtn.CustomMinimumSize = new Vector2(32, 32);
    closeBtn.Position = new Vector2(360, 10);
    closeBtn.ZIndex = 10;
    closeBtn.MouseFilter = MouseFilterEnum.Stop;
    closeBtn.Pressed += CloseSpawnMenu;
    spawnMenuPanel.AddChild(closeBtn);

    AddChild(spawnMenuPanel);
    MakeMenuDraggable(spawnMenuPanel, dragHandle);

}

private bool isDestroyMode = false;
private LineEdit spawnSearchBox;

private void ToggleDestroyMode()
{
    isDestroyMode = !isDestroyMode;
    destroyModeBtn.Text = isDestroyMode ? TranslateGroup.T("❌ 退出销毁") : TranslateGroup.T("💀 销毁模式");

    if (isDestroyMode)
    {
        TranslateGroup.SetText(spawnHintLabel, "💀 销毁模式：点击格子上的单位/兵器即可销毁");
        spawnHintLabel.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
        currentSpawnMode = SpawnMode.None;
        selectedTemplatePath = "";
        DestroySpawnGhost();   // ✅ 进入销毁模式即取消选择，销毁幽灵预览
        if (directionSelector != null) directionSelector.Visible = false;
        UpdateModeButtonStyles();
    }
    else
    {
        TranslateGroup.SetText(spawnHintLabel, "选择模式开始...");
        spawnHintLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.6f));
    }
}

private string GetFriendlyTypeName(Infantry unit)
{
    return unit switch
    {
        Mech => TranslateGroup.T("机甲"),
        MdTank => TranslateGroup.T("重坦"),
        LightTank => TranslateGroup.T("轻坦"),
        Recon => TranslateGroup.T("侦察车"),
        Artillery => TranslateGroup.T("火炮"),
        Rocket => TranslateGroup.T("火箭"),
        AntiAir => TranslateGroup.T("防空高炮"),
        APC => TranslateGroup.T("运输"),
        Flare => TranslateGroup.T("照明车"),
        Bike => TranslateGroup.T("摩托兵"),
        PipeRunner => TranslateGroup.T("管道炮"),
        Oozium => TranslateGroup.T("史莱姆"),
                AntiTank => TranslateGroup.T("反坦克炮"),
        FlyBomb => TranslateGroup.T("飞弹"),
            Fighter => TranslateGroup.T("战斗机"),
            Bomber => TranslateGroup.T("轰炸机"),
            BCopter => TranslateGroup.T("武装直升机"),
            TCopter => TranslateGroup.T("运输直升机"),
            AntiAirMissile => TranslateGroup.T("防空导弹"),
            Stealth => TranslateGroup.T("隐身战机"),
            Dog => TranslateGroup.T("机械狗"),
        Battleship => TranslateGroup.T("战列舰"),
        Cruiser => TranslateGroup.T("巡洋舰"),
        Submarine => TranslateGroup.T("潜艇"),
        Lander => TranslateGroup.T("登陆艇"),
        Carrier => TranslateGroup.T("航母"),
        Gunboat => TranslateGroup.T("炮艇"),
        Neotank => TranslateGroup.T("新型坦克"),
        Megatank => TranslateGroup.T("巨型坦克"),
        WarTank => TranslateGroup.T("弩级坦克"),
        Rig => TranslateGroup.T("工程车"),
        Duster => TranslateGroup.T("螺旋桨机"),
        Seaplane => TranslateGroup.T("水上飞机"),
        ProducibleBoat => TranslateGroup.T("生产舰"),
        KamikazeBoat => TranslateGroup.T("突击舰"),
        RepairBoat => TranslateGroup.T("修理船"),
        Infantry => TranslateGroup.T("步兵"),
        _ => unit.GetType().Name
    };
}

private string GetFriendlyWeaponName(Weapon weapon)
{
    return weapon switch
    {
        DeathRay => TranslateGroup.T("死光炮"),
        BlackCannon => TranslateGroup.T("黑炮"),
        Laser => TranslateGroup.T("激光炮"),
        Crystal => TranslateGroup.T("黑水晶"),
        _ => weapon.GetType().Name
    };
}


private void SwitchSpawnMode(SpawnMode mode)
{
    currentSpawnMode = mode;
    isDestroyMode = false;
    TranslateGroup.SetText(destroyModeBtn, "💀 销毁模式");

    UpdateModeButtonStyles();
    RefreshSpawnButtons();
}

private void UpdateModeButtonStyles()
{
    ResetButtonStyle(spawnModeUnitBtn);
    ResetButtonStyle(spawnModeWeaponBtn);
    ResetButtonStyle(spawnModeFacilityBtn);
    ResetButtonStyle(destroyModeBtn);

    if (currentSpawnMode == SpawnMode.Unit)
        HighlightButton(spawnModeUnitBtn, new Color(0.3f, 0.6f, 0.3f));
    else if (currentSpawnMode == SpawnMode.Weapon)
        HighlightButton(spawnModeWeaponBtn, new Color(0.3f, 0.4f, 0.6f));
    else if (currentSpawnMode == SpawnMode.Facility)
        HighlightButton(spawnModeFacilityBtn, new Color(0.8f, 0.6f, 0.2f));
    else if (isDestroyMode)
        HighlightButton(destroyModeBtn, new Color(0.6f, 0.2f, 0.2f));
}

// ✅ 更新方向按钮高亮状态
private void UpdateDirectionButtonStyles()
{
    if (directionSelector == null) return;
    for (int i = 0; i < 4; i++)
    {
        var btn = directionSelector.GetNodeOrNull<Button>($"DirBtn_{i}");
        if (btn == null) continue;
        if ((BlackCannon.CannonDirection)i == pendingCannonDirection)
        {
            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.3f, 0.5f, 0.8f, 0.95f);
            style.SetCornerRadiusAll(6);
            style.SetBorderWidthAll(2);
            style.BorderColor = Colors.Yellow;
            btn.AddThemeStyleboxOverride("normal", style);
        }
        else
        {
            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.25f, 0.25f, 0.3f, 0.6f);
            style.SetCornerRadiusAll(6);
            btn.AddThemeStyleboxOverride("normal", style);
        }
    }
}

private void ResetButtonStyle(Button btn)
{
    var style = new StyleBoxFlat();
    style.BgColor = new Color(0.25f, 0.25f, 0.3f, 0.8f);
    style.SetCornerRadiusAll(6);
    btn.AddThemeStyleboxOverride("normal", style);
}

private void HighlightButton(Button btn, Color color)
{
    var style = new StyleBoxFlat();
    style.BgColor = new Color(color.R, color.G, color.B, 0.9f);
    style.SetCornerRadiusAll(6);
    btn.AddThemeStyleboxOverride("normal", style);
}

private void RefreshSpawnButtons()
{
    foreach (var child in spawnButtonContainer.GetChildren())
        child.QueueFree();

    selectedTemplatePath = "";
    selectedTemplateCategory = "";
    DestroySpawnGhost();   // ✅ 刷新列表即清空选择，销毁幽灵预览
    if (directionSelector != null) directionSelector.Visible = false;

    if (currentSpawnMode == SpawnMode.None) return;

    List<UnitTemplate> templates;
    if (currentSpawnMode == SpawnMode.Unit)
        templates = unitTemplates;
    else if (currentSpawnMode == SpawnMode.Weapon)
        templates = weaponTemplates;
    else
        templates = facilityTemplates;

    // ✅ 搜索过滤（按显示名/描述/场景路径，不区分大小写）
    string keyword = spawnSearchBox?.Text?.Trim() ?? "";
    if (keyword.Length > 0)
    {
        templates = templates.Where(t =>
            (t.DisplayName?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            || (t.Description?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            || (t.ScenePath?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
        ).ToList();
    }

    var grouped = templates.GroupBy(t => t.Category).OrderBy(g => g.Key);

    foreach (var group in grouped)
    {
        var categoryLabel = new Label();
        categoryLabel.Text = $"=== {group.Key} ===";
        categoryLabel.AddThemeFontSizeOverride("font_size", 14);
        categoryLabel.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.3f));
        categoryLabel.CustomMinimumSize = new Vector2(0, 25);
        spawnButtonContainer.AddChild(categoryLabel);

        foreach (var template in group)
        {
            var btn = CreateTemplateButton(template);
            spawnButtonContainer.AddChild(btn);
        }
    }

    spawnHintLabel.Text = currentSpawnMode switch
    {
        SpawnMode.Unit => TranslateGroup.T("🚶 选择单位模板，然后点击地图格子生成"),
        SpawnMode.Weapon => TranslateGroup.T("⚔️ 选择兵器模板，然后点击地图格子生成"),
        SpawnMode.Facility => TranslateGroup.T("🏙️ 选择设施模板，然后点击地图格子生成"),
        _ => TranslateGroup.T("选择模式开始...")
    };
    spawnHintLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.9f, 0.6f));
}

private Button CreateTemplateButton(UnitTemplate template)
{
    var btn = new Button();
    btn.CustomMinimumSize = new Vector2(270, 50);
    btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;

    btn.Text = template.DisplayName;   // ✅ 修复：长介绍不再撑爆按钮挡放置
        btn.TooltipText = template.Description;   // 悬停小卡看全文
    btn.AddThemeFontSizeOverride("font_size", 12);
    btn.AddThemeColorOverride("font_color", Colors.White);
    btn.Alignment = HorizontalAlignment.Left;

    var normalStyle = new StyleBoxFlat();
    normalStyle.BgColor = new Color(template.DisplayColor.R * 0.3f, template.DisplayColor.G * 0.3f, 
        template.DisplayColor.B * 0.3f, 0.8f);
    normalStyle.SetCornerRadiusAll(8);
    btn.AddThemeStyleboxOverride("normal", normalStyle);

    var hoverStyle = new StyleBoxFlat();
    hoverStyle.BgColor = new Color(template.DisplayColor.R * 0.5f, template.DisplayColor.G * 0.5f, 
        template.DisplayColor.B * 0.5f, 0.9f);
    hoverStyle.SetCornerRadiusAll(8);
    btn.AddThemeStyleboxOverride("hover", hoverStyle);

    var pressedStyle = new StyleBoxFlat();
    pressedStyle.BgColor = new Color(template.DisplayColor.R * 0.7f, template.DisplayColor.G * 0.7f,
        template.DisplayColor.B * 0.7f, 1.0f);
    pressedStyle.SetCornerRadiusAll(8);
    btn.AddThemeStyleboxOverride("pressed", pressedStyle);

    // ✅ 模板图标：实例化场景取 AnimatedSprite2D/Sprite2D 首帧（带静态缓存），取不到则不加图标保持纯文字
    var iconInfo = GetSceneIconInfo(template.ScenePath);
    if (iconInfo?.Texture != null)
    {
        var iconRect = new TextureRect();
        iconRect.Name = "TemplateIcon";
        iconRect.Texture = iconInfo.Texture;
        iconRect.Position = new Vector2(5, (50 - templateIconSize) / 2f);
        iconRect.Size = new Vector2(templateIconSize, templateIconSize);
        iconRect.CustomMinimumSize = new Vector2(templateIconSize, templateIconSize);
        iconRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        iconRect.MouseFilter = MouseFilterEnum.Ignore;
        btn.AddChild(iconRect);

        // ✅ 文字右移避开图标（保留 emoji 文字，图标是"加"不是"换"）
        float textMargin = 5 + templateIconSize + 6;
        normalStyle.ContentMarginLeft = textMargin;
        hoverStyle.ContentMarginLeft = textMargin;
        pressedStyle.ContentMarginLeft = textMargin;
    }

    // ✅ 可滚动的势力选择区
    var teamScroll = new ScrollContainer();
    teamScroll.Position = new Vector2(180, 8);
    teamScroll.Size = new Vector2(88, 34);
    teamScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;
    teamScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
    teamScroll.Name = "TeamScroll";
    
    var teamHBox = new HBoxContainer();
    teamHBox.AddThemeConstantOverride("separation", 4);
    teamHBox.Name = "TeamHBox";

    // ✅ 动态生成所有活跃势力的选择按钮
    foreach (var faction in TeamHelper.GetActiveFactions())
    {
        string fid = faction.Id;
        string shortName = TeamHelper.GetTeamShortName(fid);
        var fBtn = new Button();
        fBtn.Text = shortName;
        fBtn.CustomMinimumSize = new Vector2(32, 24);
        fBtn.AddThemeFontSizeOverride("font_size", 9);
        // 用势力主色作为按钮色调提示
        var fStyle = new StyleBoxFlat();
        fStyle.BgColor = faction.PrimaryColor.Darkened(0.3f);
        fStyle.SetCornerRadiusAll(4);
        fBtn.AddThemeStyleboxOverride("normal", fStyle);
        fBtn.Pressed += () => {
            template.Team = fid;
            UpdateTemplateTeamIndicator(btn, fid);
        };
        teamHBox.AddChild(fBtn);
    }

    teamScroll.AddChild(teamHBox);
    btn.AddChild(teamScroll);

    btn.Pressed += () => {
        selectedTemplatePath = template.ScenePath;
        selectedTemplateCategory = template.Category;
        spawnHintLabel.Text = $"✅ {TranslateGroup.T("已选择")}: {template.DisplayName} ({template.Team})\n{TranslateGroup.T("点击地图格子生成")}...";
        spawnHintLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));

        // ✅ 黑炮/大型黑炮/幽灵炮/Gate门：显示方向选择器
        bool isCannon = template.ScenePath.Contains("black_cannon") || template.ScenePath.Contains("large_cannon") || template.ScenePath.Contains("death_ray") || template.ScenePath.Contains("SuperFactory") || template.ScenePath.Contains("ghost_cannon") || template.ScenePath.Contains("Gate.tscn");
        if (directionSelector != null)
        {
            directionSelector.Visible = isCannon;
            if (isCannon)
            {
                pendingCannonDirection = BlackCannon.CannonDirection.Up; // 默认朝上
                UpdateDirectionButtonStyles();
            }
        }

        foreach (var child in spawnButtonContainer.GetChildren())
        {
            if (child is Button b && b != btn)
                ResetTemplateButtonStyle(b);
        }
        HighlightTemplateButton(btn, template.DisplayColor);

        ShowSpawnGhost(template.ScenePath);   // ✅ 选中模板后显示光标幽灵预览
    };

    return btn;
}

private void UpdateTemplateTeamIndicator(Button btn, string team)
{
    var teamLabel = btn.GetNodeOrNull<Label>("TeamLabel");
    if (teamLabel == null)
    {
        teamLabel = new Label();
        teamLabel.Name = "TeamLabel";
        teamLabel.Position = new Vector2(220, 4);
        teamLabel.Size = new Vector2(60, 20);
        teamLabel.AddThemeFontSizeOverride("font_size", 10);
        btn.AddChild(teamLabel);
    }
    teamLabel.Text = TeamHelper.GetTeamShortName(team);
    teamLabel.AddThemeColorOverride("font_color", TeamHelper.GetTeamColor(team));

    // ✅ 队伍染色：图标随队伍提亮变色（未换队伍时保持默认白）
    var iconRect = btn.GetNodeOrNull<TextureRect>("TemplateIcon");
    if (iconRect != null)
        iconRect.Modulate = TeamHelper.GetTeamColor(team).Lightened(teamIconTintLighten);
}

private void ResetTemplateButtonStyle(Button btn)
{
    var style = new StyleBoxFlat();
    style.BgColor = new Color(0.25f, 0.25f, 0.3f, 0.6f);
    style.SetCornerRadiusAll(8);
    // ✅ 有图标的按钮保持文字右移，避免选中态切换时文字压图标
    if (btn.GetNodeOrNull<TextureRect>("TemplateIcon") != null)
        style.ContentMarginLeft = 5 + templateIconSize + 6;
    btn.AddThemeStyleboxOverride("normal", style);
}

private void HighlightTemplateButton(Button btn, Color color)
{
    var style = new StyleBoxFlat();
    style.BgColor = new Color(color.R * 0.6f, color.G * 0.6f, color.B * 0.6f, 0.95f);
    style.SetCornerRadiusAll(8);
    style.SetBorderWidthAll(2);
    style.BorderColor = Colors.Yellow;
    // ✅ 有图标的按钮保持文字右移
    if (btn.GetNodeOrNull<TextureRect>("TemplateIcon") != null)
        style.ContentMarginLeft = 5 + templateIconSize + 6;
    btn.AddThemeStyleboxOverride("normal", style);
}
// ========== 菜单控制 ==========
private void OpenSpawnMenu()
{
    CloseAllMenus();
    spawnMenuPanel.Visible = true;
    isMenuOpen = true;
    currentSpawnMode = SpawnMode.None;
    isDestroyMode = false;
    selectedTemplatePath = "";
    DestroySpawnGhost();   // ✅ 打开菜单重置选择，销毁幽灵预览
    UpdateModeButtonStyles();

    foreach (var child in spawnButtonContainer.GetChildren())
        child.QueueFree();

    TranslateGroup.SetText(spawnHintLabel, "选择生成模式...");
}

private void CloseSpawnMenu()
{
    spawnMenuPanel.Visible = false;
    currentSpawnMode = SpawnMode.None;
    isDestroyMode = false;
    selectedTemplatePath = "";
    DestroySpawnGhost();   // ✅ 关闭菜单（含ESC）时销毁幽灵预览
    if (directionSelector != null) directionSelector.Visible = false;
    isMenuOpen = false;
}

    // ==================== 自定义配置加载模式 ====================

    private SpawnMode pendingSpawnMode = SpawnMode.None;
    private string pendingSpawnScenePath = "";
    private string pendingSpawnTeam = "";
    private SavedGridTerrainData pendingTerrainData = null;

    /// <summary>
    /// 设置生成模式（供CustomConfigUI调用，加载保存的单位/兵器/设施）
    /// </summary>
    public void SetSpawnMode(SpawnMode mode, string scenePath, string team)
    {
        pendingSpawnMode = mode;
        pendingSpawnScenePath = scenePath;
        pendingSpawnTeam = team;
        pendingTerrainData = null;

        // 关闭其他菜单，进入等待点击状态
        CloseAllMenus();

        string typeName = mode switch
        {
            SpawnMode.Unit => TranslateGroup.T("单位"),
            SpawnMode.Weapon => TranslateGroup.T("兵器"),
            SpawnMode.Facility => TranslateGroup.T("设施"),
            _ => TranslateGroup.T("对象")
        };

        // 显示提示
        var hint = new Label();
        hint.Text = $"🖱️ {TranslateGroup.T("点击地图格子放置")}{typeName}...ESC{TranslateGroup.T("取消")}";
        hint.AddThemeFontSizeOverride("font_size", 14);
        hint.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.7f));
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.SetAnchorsPreset(LayoutPreset.CenterTop);
        hint.Position = new Vector2(0, 80);
        hint.ZIndex = 2000;
        hint.Name = "SpawnModeHint";
        AddChild(hint);

        // 3秒后自动消失
        var timer = new Timer();
        timer.WaitTime = 3.0f;
        timer.OneShot = true;
        timer.Timeout += () => { 
            var h = GetNodeOrNull<Label>("SpawnModeHint");
            h?.QueueFree();
            timer.QueueFree(); 
        };
        AddChild(timer);
        timer.Start();
    }

    /// <summary>
    /// 设置地形应用模式（供CustomConfigUI调用，加载保存的地形）
    /// </summary>
    public void SetApplyTerrainMode(SavedGridTerrainData data)
    {
        pendingTerrainData = data;
        pendingSpawnMode = SpawnMode.None;
        pendingSpawnScenePath = "";

        CloseAllMenus();

        var hint = new Label();
        hint.Text = TranslateGroup.T("🖱️ 点击地图格子应用地形 ") + "\"" + data.displayName + "\"...\n" + TranslateGroup.T("ESC取消");
        hint.AddThemeFontSizeOverride("font_size", 14);
        hint.AddThemeColorOverride("font_color", new Color(0.7f, 1f, 0.4f));
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.SetAnchorsPreset(LayoutPreset.CenterTop);
        hint.Position = new Vector2(0, 80);
        hint.ZIndex = 2000;
        hint.Name = "TerrainModeHint";
        AddChild(hint);

        var timer = new Timer();
        timer.WaitTime = 3.0f;
        timer.OneShot = true;
        timer.Timeout += () => { 
            var h = GetNodeOrNull<Label>("TerrainModeHint");
            h?.QueueFree();
            timer.QueueFree(); 
        };
        AddChild(timer);
        timer.Start();
    }

    /// <summary>
    /// 取消待处理的生成/应用模式
    /// </summary>
    public void CancelPendingMode()
    {
        pendingSpawnMode = SpawnMode.None;
        pendingSpawnScenePath = "";
        pendingSpawnTeam = "";
        pendingTerrainData = null;

        var h1 = GetNodeOrNull<Label>("SpawnModeHint");
        h1?.QueueFree();
        var h2 = GetNodeOrNull<Label>("TerrainModeHint");
        h2?.QueueFree();
    }

    /// <summary>
    /// 处理待生成模式的格子点击
    /// </summary>
    private bool HandlePendingSpawn(Grids grid)
    {
        if (pendingSpawnMode == SpawnMode.None && pendingTerrainData == null) return false;

        if (pendingTerrainData != null)
        {
            ApplySavedGridTerrain(grid, pendingTerrainData);
            pendingTerrainData = null;
            var h = GetNodeOrNull<Label>("TerrainModeHint");
            h?.QueueFree();
            return true;
        }

        if (string.IsNullOrEmpty(pendingSpawnScenePath)) return false;

        switch (pendingSpawnMode)
        {
            case SpawnMode.Unit:
                SpawnUnitAtGrid(grid, pendingSpawnScenePath, pendingSpawnTeam);
                break;
            case SpawnMode.Weapon:
                // 检查是否多格兵器
                var scene = GD.Load<PackedScene>(pendingSpawnScenePath);
                if (scene != null)
                {
                    var temp = scene.Instantiate<Weapon>();
                    if (temp != null)
                    {
                        // ✅ 关键修复：Instantiate不会触发_Ready，多格属性需手动检测
                        if (temp is LargeCannon lc && !temp.isMultiTile)
                        {
                            temp.isMultiTile = true;
                            temp.size = lc.multiTileSize;
                        }
                        else if (temp is DeathRay dr && !temp.isMultiTile)
                        {
                            temp.isMultiTile = true;
                            temp.size = dr.multiTileSize;
                        }
                        else if (temp is BlackObelisk bo && !temp.isMultiTile)
                        {
                            temp.isMultiTile = true;
                            temp.size = bo.multiTileSize;
                        }
                        else if (temp is SuperFactory sf0 && !temp.isMultiTile)
                        {
                            temp.isMultiTile = true;
                            temp.size = sf0.factorySize;
                        }

                        bool isMulti = temp.isMultiTile;
                        temp.QueueFree();

                        if (isMulti)
                        {
                            // ✅ 进入预览模式，不清除pending！
                            // 确认/取消由 OnGridClickedForEdit 最前面的统一逻辑处理
                            TryPreviewMultiTileWeapon(grid, pendingSpawnScenePath, pendingSpawnTeam);
                            return true;
                        }
                        else
                        {
                            SpawnWeaponAtGrid(grid, pendingSpawnScenePath, pendingSpawnTeam);
                        }
                    }
                }
                break;
            case SpawnMode.Facility:
                SpawnFacilityAtGrid(grid, pendingSpawnScenePath, pendingSpawnTeam);
                break;
        }

        // 只有非预览模式才清除pending状态
        pendingSpawnMode = SpawnMode.None;
        pendingSpawnScenePath = "";
        var hint = GetNodeOrNull<Label>("SpawnModeHint");
        hint?.QueueFree();

        return true;
    }

// ========== ✅ 场景图标提取（生成菜单/幽灵预览共用，带静态缓存） ==========
private class SceneIconInfo
{
    public Texture2D Texture;
    public Vector2 Scale = Vector2.One;
}
private static readonly Dictionary<string, SceneIconInfo> _sceneIconCache = new();

// ✅ 取场景首帧贴图：实例化一次取 AnimatedSprite2D/Sprite2D 首帧后立即释放；结果（含失败null）进缓存避免重复实例化
private SceneIconInfo GetSceneIconInfo(string scenePath)
{
    if (string.IsNullOrEmpty(scenePath)) return null;
    if (_sceneIconCache.TryGetValue(scenePath, out var cached)) return cached;

    SceneIconInfo info = null;
    var scene = GD.Load<PackedScene>(scenePath);
    if (scene != null)
    {
        Node inst = null;
        try
        {
            inst = scene.Instantiate();   // ✅ 不入树，_Ready 不会执行，只读场景自带视觉
            info = ExtractIconFromInstance(inst);
        }
        catch (Exception) { info = null; }
        inst?.QueueFree();   // ✅ 取完立即释放临时实例
    }
    _sceneIconCache[scenePath] = info;
    return info;
}

// ✅ 优先 AnimatedSprite2D（"AnimatedSprite2D"/"Base2D"）默认动画第0帧；否则第一个非空贴图的 Sprite2D
private SceneIconInfo ExtractIconFromInstance(Node inst)
{
    if (inst == null) return null;

    var anim = inst.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D")
        ?? inst.GetNodeOrNull<AnimatedSprite2D>("Base2D")
        ?? FindFirstAnimSprite(inst);
    if (anim != null)
    {
        var tex = GetAnimFirstFrame(anim);
        if (tex != null) return new SceneIconInfo { Texture = tex, Scale = anim.Scale };
    }

    var sprite = inst.GetNodeOrNull<Sprite2D>("Sprite2D");
    if (sprite?.Texture == null) sprite = inst.GetNodeOrNull<Sprite2D>("BaseSprite");
    if (sprite?.Texture == null) sprite = FindFirstTexturedSprite(inst);
    if (sprite?.Texture != null)
        return new SceneIconInfo { Texture = sprite.Texture, Scale = sprite.Scale };
    return null;
}

// ✅ 取 AnimatedSprite2D 当前/默认/首个动画的第 0 帧
private Texture2D GetAnimFirstFrame(AnimatedSprite2D anim)
{
    var frames = anim?.SpriteFrames;
    if (frames == null) return null;
    StringName animName = anim.Animation;
    if (frames.HasAnimation(animName) && frames.GetFrameCount(animName) > 0)
        return frames.GetFrameTexture(animName, 0);
    if (frames.HasAnimation("default") && frames.GetFrameCount("default") > 0)
        return frames.GetFrameTexture("default", 0);
    var names = frames.GetAnimationNames();
    if (names.Length > 0 && frames.GetFrameCount(names[0]) > 0)
        return frames.GetFrameTexture(names[0], 0);
    return null;
}

private AnimatedSprite2D FindFirstAnimSprite(Node node)
{
    foreach (var child in node.GetChildren())
    {
        if (child is AnimatedSprite2D a) return a;
        var found = FindFirstAnimSprite(child);
        if (found != null) return found;
    }
    return null;
}

private Sprite2D FindFirstTexturedSprite(Node node)
{
    foreach (var child in node.GetChildren())
    {
        if (child is Sprite2D s && s.Texture != null) return s;
        var found = FindFirstTexturedSprite(child);
        if (found != null) return found;
    }
    return null;
}

// ========== ✅ 光标幽灵预览（选中模板后跟随鼠标、吸附最近格子） ==========
private Sprite2D spawnGhost = null;

// ✅ 幽灵加在 GridsNode（世界坐标系）下，避免加在 UI 层导致坐标错乱
private void ShowSpawnGhost(string scenePath)
{
    DestroySpawnGhost();
    if (!enableGhostPreview || string.IsNullOrEmpty(scenePath)) return;
    var info = GetSceneIconInfo(scenePath);
    if (info?.Texture == null) return;
    if (gridManager?.GridsNode == null) return;

    spawnGhost = new Sprite2D();
    spawnGhost.Name = "SpawnGhostPreview";
    spawnGhost.Texture = info.Texture;
    spawnGhost.Scale = info.Scale;
    spawnGhost.Centered = true;
    spawnGhost.ZIndex = 500;
    spawnGhost.Modulate = new Color(1f, 1f, 1f, ghostPreviewAlpha);
    gridManager.GridsNode.AddChild(spawnGhost);
    UpdateSpawnGhostPosition();
}

private void DestroySpawnGhost()
{
    if (spawnGhost != null && IsInstanceValid(spawnGhost))
        spawnGhost.QueueFree();
    spawnGhost = null;
}

// ✅ 每帧由 _Process 调用：视口鼠标坐标→世界坐标→吸附最近格子（与批量地形同一套坐标换算）
private void UpdateSpawnGhostPosition()
{
    if (spawnGhost == null || !IsInstanceValid(spawnGhost)) { spawnGhost = null; return; }
    if (gridManager?.map == null) { spawnGhost.Visible = false; return; }

    Vector2 viewportPos = GetViewport().GetMousePosition();
    Transform2D canvasTransform = GetViewport().GetCanvasTransform();
    Vector2 worldPos = canvasTransform.AffineInverse() * viewportPos;

    int x = Mathf.FloorToInt((worldPos.X - gridManager.startPos.X) / gridManager.gridSize.X);
    int y = Mathf.FloorToInt((worldPos.Y - gridManager.startPos.Y) / gridManager.gridSize.Y);

    if (x >= 0 && x < gridManager.map.GetLength(0) && y >= 0 && y < gridManager.map.GetLength(1))
    {
        var grid = gridManager.map[x, y];
        if (grid != null)
        {
            // ✅ 与真实放置逻辑一致（unit.Position = grid.Position），幽灵是 GridsNode 子节点故用局部坐标
            spawnGhost.Position = grid.Position;
            spawnGhost.Visible = true;
            return;
        }
    }
    spawnGhost.Visible = false;   // ✅ 鼠标在地图外时隐藏
}

// ========== ✅ 选择/销毁菜单对象图标 ==========
// ✅ 给按钮文字前加对象实际 sprite 首帧小图标（对象是地图上的活体实例，直接读其视觉节点）
private void AddLiveObjectIcon(Button btn, object target)
{
    if (btn == null || target == null) return;
    Texture2D tex = null;

    if (target is Infantry unit)
    {
        tex = GetAnimFirstFrame(unit.animSprite);
        if (tex == null && unit.sprite?.Texture != null) tex = unit.sprite.Texture;
        if (tex == null) tex = FindFirstTexturedSprite(unit)?.Texture;
    }
    else if (target is Weapon weapon)
    {
        tex = GetAnimFirstFrame(weapon.animSprite);
        if (tex == null) tex = weapon.GetNodeOrNull<Sprite2D>("Sprite2D")?.Texture;
        if (tex == null) tex = FindFirstTexturedSprite(weapon)?.Texture;
    }
    else if (target is Facility facility)
    {
        tex = GetAnimFirstFrame(facility.animSprite);
        if (tex == null) tex = facility.GetNodeOrNull<Sprite2D>("Sprite2D")?.Texture;
        if (tex == null) tex = FindFirstTexturedSprite(facility)?.Texture;
    }
    if (tex == null) return;   // ✅ 取不到就保持纯文字现状，不留占位图

    var icon = new TextureRect();
    icon.Name = "ObjectIcon";
    icon.Texture = tex;
    icon.Position = new Vector2(4, (45 - selectionIconSize) / 2f);   // ✅ 选择按钮高45（CreateSelectionButton）
    icon.Size = new Vector2(selectionIconSize, selectionIconSize);
    icon.CustomMinimumSize = new Vector2(selectionIconSize, selectionIconSize);
    icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
    icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
    icon.MouseFilter = MouseFilterEnum.Ignore;
    btn.AddChild(icon);

    // ✅ 文字右移避开图标（只改本按钮自己的 StyleBox 实例，不影响其他按钮）
    float margin = 4 + selectionIconSize + 6;
    if (btn.GetThemeStylebox("normal") is StyleBoxFlat normalSb) normalSb.ContentMarginLeft = margin;
    if (btn.GetThemeStylebox("hover") is StyleBoxFlat hoverSb) hoverSb.ContentMarginLeft = margin;
    if (btn.GetThemeStylebox("pressed") is StyleBoxFlat pressedSb) pressedSb.ContentMarginLeft = margin;
}
}
