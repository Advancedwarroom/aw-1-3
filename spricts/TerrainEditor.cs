using Godot;
using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Reflection;

public class UnitTemplate
{
    public string DisplayName;
    public string ScenePath;
    public string Category;
    public string Team;
    public Color DisplayColor;
    public string Description;
}

public enum SpawnMode { None, Unit, Weapon, Facility }

public partial class TerrainEditor : Control
{
    [Export] public GameManager gameManager;
    [Export] public GridManager gridManager;

    // ========== UI 组件 ==========
    private Button modeToggleButton;

    // 主选择菜单（单位/兵器/格子）
    private Panel selectionMenuPanel;
    private VBoxContainer selectionButtonContainer;
    private Label selectionTitleLabel;

    // 地形编辑菜单
    private Panel terrainMenuPanel;
    private ScrollContainer terrainScrollContainer;
    private VBoxContainer terrainButtonContainer;
    private Label terrainTitleLabel;

    // 属性编辑器面板（通用：单位+兵器）
    private Panel propertyEditorPanel;
    private ScrollContainer propertyScrollContainer;
    private VBoxContainer propertyContainer;
    private Label propertyEditorTitle;
    private Button propertyEditorCloseBtn;
    private Button propertyEditorSaveBtn;

    private SpawnMode currentSpawnMode = SpawnMode.None;
    private string selectedTemplatePath = "";
    private string selectedTemplateCategory = "";
    // 自定义伤害字段UI（格子专用）
    private VBoxContainer customDamageContainer;
    private CheckBox canDestroyCheckBox;
    private SpinBox fixedDamageSpinBox;
    private SpinBox fixedAttackSpinBox;
    private CheckBox canOverMaxCheckBox;
    private SpinBox fixedAmmoSpinBox;
    private CheckBox ammoCanOverMaxCheckBox;
    private CheckBox ammoCanReachZeroCheckBox;
    private SpinBox fixedFuelSpinBox;
    private CheckBox fuelCanOverMaxCheckBox;
    private CheckBox fuelCanReachZeroCheckBox;
    private VBoxContainer ammoFuelContainer;

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

    // ========== ✅ 多格兵器放置预览模式 ==========
    private bool isMultiTilePreviewMode = false;
    private Weapon pendingMultiTileWeapon = null;
    private List<Grids> previewMultiTileGrids = new();
    private Vector2I pendingMultiTileAnchorPos = new Vector2I(-1, -1);
    private string pendingMultiTileScenePath = "";
    private string pendingMultiTileTeam = "";
    private BlackCannon.CannonDirection pendingCannonDirection = BlackCannon.CannonDirection.Up;

    // 状态
    public bool IsEditMode { get; private set; } = false;
    private Grids selectedGrid = null;
    private object currentEditingTarget = null;
    private bool isMenuOpen = false;

    // ========== ✅ 全局配置面板 ==========
    private Panel globalConfigPanel;
    private VBoxContainer globalConfigContainer;
    private CheckBox fogOfWarCheckBox;
    private Button openGlobalConfigBtn;

    // 缓存反射信息（同时缓存字段和属性）
    private Dictionary<Type, List<FieldInfo>> exportFieldsCache = new();
    private Dictionary<Type, List<PropertyInfo>> exportPropertiesCache = new();

public bool IsAnyMenuOpen()
{
    return selectionMenuPanel?.Visible == true 
        || terrainMenuPanel?.Visible == true 
        || propertyEditorPanel?.Visible == true 
        || spawnMenuPanel?.Visible == true
        || globalConfigPanel?.Visible == true
        || batchTerrainPanel?.Visible == true;
}
private bool fogStateBeforeEdit = false;
private readonly List<UnitTemplate> unitTemplates = new()
{
    // 步兵类
    new UnitTemplate { DisplayName = "🚶 步兵", ScenePath = "res://Prefabs/infantry(1).tscn", 
        Category = "步兵", Team = "Player1", DisplayColor = new Color(0.4f, 0.7f, 0.4f), 
        Description = "基础步兵单位，可占领城市" },
    new UnitTemplate { DisplayName = "🦾 机甲", ScenePath = "res://Prefabs/mech.tscn", 
        Category = "步兵", Team = "Player1", DisplayColor = new Color(0.5f, 0.6f, 0.4f), 
        Description = "反装甲步兵，移动力较低" },

    // 装甲类
    new UnitTemplate { DisplayName = "🚗 轻型坦克", ScenePath = "res://Prefabs/light_tank.tscn", 
        Category = "装甲", Team = "Player1", DisplayColor = new Color(0.6f, 0.5f, 0.3f), 
        Description = "快速装甲单位，攻防均衡" },
    new UnitTemplate { DisplayName = "🚙 重型坦克", ScenePath = "res://Prefabs/Md_Tank.tscn", 
        Category = "装甲", Team = "Player1", DisplayColor = new Color(0.55f, 0.45f, 0.25f), 
        Description = "重火力履带单位，对轻甲毁灭性，移动力5" },
    new UnitTemplate { DisplayName = "🚀 火箭炮", ScenePath = "res://Prefabs/Rocket.tscn", 
        Category = "装甲", Team = "Player1", DisplayColor = new Color(0.7f, 0.4f, 0.3f), 
        Description = "远程间接火力，射程3-5格" },
    new UnitTemplate { DisplayName = "🔫 自行火炮", ScenePath = "res://Prefabs/Artillery.tscn", 
        Category = "装甲", Team = "Player1", DisplayColor = new Color(0.6f, 0.45f, 0.35f), 
        Description = "中程火力支援，射程2-3格" },
    new UnitTemplate { DisplayName = "🚛 运输车", ScenePath = "res://Prefabs/apc.tscn", 
        Category = "装甲", Team = "Player1", DisplayColor = new Color(0.5f, 0.55f, 0.4f), 
        Description = "运输单位，可搭载步兵" },
        new UnitTemplate { 
    DisplayName = "🛡️ 防空高炮", 
    ScenePath = "res://Prefabs/AntiAir.tscn", 
    Category = "装甲", 
    Team = "Player1", 
    DisplayColor = new Color(0.5f, 0.7f, 0.3f),  
    Description = "对步兵/机甲毁灭性，移动力6，弹药9发，无副武器" 
},
    new UnitTemplate { 
    DisplayName = "🚙 侦察车", 
    ScenePath = "res://Prefabs/Recon.tscn", 
    Category = "装甲", 
    Team = "Player1", 
    DisplayColor = new Color(0.5f, 0.6f, 0.3f),  
    Description = "高视野侦察单位，移动力8，仅副武器，视野5" 
},
    new UnitTemplate { 
    DisplayName = "🎯 反坦克炮", 
    ScenePath = "res://Prefabs/Anti_Tank.tscn", 
    Category = "装甲", 
    Team = "Player1", 
    DisplayColor = new Color(0.55f, 0.5f, 0.3f),  
    Description = "射程1-3反装甲单位，可近战反击，对地高伤害，弹药6发" 
},


    new UnitTemplate { DisplayName = "🔦 照明车", ScenePath = "res://Prefabs/Flare.tscn", 
        Category = "装甲", Team = "Player1", DisplayColor = new Color(0.6f, 0.55f, 0.3f), 
        Description = "可发射照明弹驱散战争迷雾，移动力5，履带移动，仅副武器" },
    new UnitTemplate { DisplayName = "🏍️ 摩托兵", ScenePath = "res://Prefabs/Bike.tscn", 
        Category = "步兵", Team = "Player1", DisplayColor = new Color(0.5f, 0.5f, 0.35f), 
        Description = "快速步兵单位，可占领城市，移动力5，轮胎移动" },
    new UnitTemplate { DisplayName = "🚀 管道炮", ScenePath = "res://Prefabs/PipeRunner.tscn", 
        Category = "装甲", Team = "Player1", DisplayColor = new Color(0.5f, 0.4f, 0.6f), 
        Description = "仅限管道移动，远程间接火力，射程2-5" },

    // 特殊单位
    new UnitTemplate { DisplayName = "🦠 史莱姆", ScenePath = "res://Prefabs/oozium.tscn", 
        Category = "特殊", Team = "Player1", DisplayColor = new Color(0.4f, 0.3f, 0.6f), 
        Description = "吞噬型单位，可消灭任何敌人" },
    new UnitTemplate { DisplayName = "💣 飞弹", ScenePath = "res://Prefabs/FlyBomb.tscn", 
        Category = "特殊", Team = "Player1", DisplayColor = new Color(0.9f, 0.3f, 0.1f), 
        Description = "自爆单位，3格范围50%HP伤害，空军移动力9" },
};

private readonly List<UnitTemplate> weaponTemplates = new()
{
    new UnitTemplate { DisplayName = "⚫ 黑炮", ScenePath = "res://Prefabs/black_cannon.tscn", 
        Category = "兵器", Team = "Player1", DisplayColor = new Color(0.3f, 0.3f, 0.3f), 
        Description = "三角形范围兵器，固定百分比伤害" },
    new UnitTemplate { DisplayName = "🔴 激光炮", ScenePath = "res://Prefabs/Laser.tscn", 
        Category = "兵器", Team = "Player1", DisplayColor = new Color(0.9f, 0.2f, 0.2f), 
        Description = "直线激光兵器，穿透攻击，可配置角度/射程/冷却" },
    new UnitTemplate { DisplayName = "💎 黑水晶", ScenePath = "res://Prefabs/crystal.tscn", 
        Category = "兵器", Team = "Player1", DisplayColor = new Color(0.6f, 0.2f, 0.9f), 
        Description = "环形范围治疗+补给兵器，还原原版AW黑水晶机制" },
    new UnitTemplate { DisplayName = "🖤 大型黑炮", ScenePath = "res://Prefabs/large_cannon.tscn", 
        Category = "兵器", Team = "Player1", DisplayColor = new Color(0.2f, 0.2f, 0.2f), 
        Description = "3×3多格占据兵器，仅弱点可攻击，摧毁后仍不可通行" },
    new UnitTemplate { DisplayName = "☠️ 死光炮", ScenePath = "res://Prefabs/death_ray.tscn", 
        Category = "兵器", Team = "Player1", DisplayColor = new Color(0.8f, 0.1f, 0.8f), 
        Description = "3×3多格死光炮，四方向3格宽激光，摧毁后仍不可通行" },
};

private readonly List<UnitTemplate> facilityTemplates = new()
{
    new UnitTemplate { DisplayName = "🏙️ City", ScenePath = "res://Prefabs/City.tscn", 
        Category = "设施", Team = "Player0", DisplayColor = new Color(0.8f, 0.6f, 0.2f), 
        Description = "城市设施，可占领并提供补给" },
    new UnitTemplate { DisplayName = "🏭 Base", ScenePath = "res://Prefabs/Base.tscn", 
        Category = "设施", Team = "Player0", DisplayColor = new Color(0.7f, 0.5f, 0.3f), 
        Description = "基地设施，可占领、补给并生产地面单位" },
    new UnitTemplate { DisplayName = "✈️ Airport", ScenePath = "res://Prefabs/AirPort.tscn", 
        Category = "设施", Team = "Player0", DisplayColor = new Color(0.4f, 0.6f, 0.8f), 
        Description = "机场设施，可占领、补给并生产空军单位" },
};

// UI 引用
private Panel spawnMenuPanel;
private VBoxContainer spawnButtonContainer;
private Label spawnTitleLabel;
private Button spawnModeUnitBtn;
private Button spawnModeWeaponBtn;
private Button spawnModeFacilityBtn;
private Button destroyModeBtn;
private Label spawnHintLabel;
private HBoxContainer spawnModeSelector;
private HBoxContainer directionSelector; // ✅ 黑炮/大型黑炮 方向选择器
public override void _Ready()
{
    AddToGroup("terrain_editor");

    if (gameManager == null)
        gameManager = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
    if (gridManager == null)
        gridManager = GetTree().GetFirstNodeInGroup("grid_manager") as GridManager;

    CreateModeToggleButton();
    CreateSelectionMenu();
    CreateTerrainMenu();
    CreatePropertyEditor();
    CreateSpawnMenu();
    CreateGlobalConfigPanel();

    var viewportSize = GetViewport().GetVisibleRect().Size;
    modeToggleButton.SetAnchorsPreset(LayoutPreset.TopLeft);
    modeToggleButton.Position = new Vector2(viewportSize.X - 160, viewportSize.Y - 70);

    // ✅ 创建批量地形按钮（编辑模式专用）
    CreateBatchTerrainButton();
    batchTerrainBtn.SetAnchorsPreset(LayoutPreset.TopLeft);
    batchTerrainBtn.Position = new Vector2(viewportSize.X - 160, viewportSize.Y - 130);
    batchTerrainBtn.Visible = false; // 初始隐藏，进入编辑模式时显示
}

// ========== 修改 _Input 添加 ESC 关闭生成菜单 ==========
public override void _Input(InputEvent @event)
{
    if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
    {
        if (globalConfigPanel?.Visible == true)
        {
            CloseGlobalConfigPanel();
            return;
        }

        if (spawnMenuPanel?.Visible == true)
        {
            CloseSpawnMenu();
            return;
        }
        if (propertyEditorPanel?.Visible == true)
        {
            ClosePropertyEditor();
            return;
        }
        if (terrainMenuPanel?.Visible == true)
        {
            CloseTerrainMenu();
            return;
        }
        if (selectionMenuPanel?.Visible == true)
        {
            CloseSelectionMenu();
            isMenuOpen = false;
            return;
        }

        // ✅ ESC 退出批量地形模式
        if (isBatchTerrainMode)
        {
            StopBatchTerrainMode();
            return;
        }
        if (isMultiTilePreviewMode)
        {
            ClearMultiTilePreview();
            spawnHintLabel.Text = "取消多格兵器放置";
            spawnHintLabel.AddThemeColorOverride("font_color", Colors.Yellow);
            return;
        }
        if (batchTerrainPanel?.Visible == true)
        {
            CloseBatchTerrainPanel();
            return;
        }

        // ESC 打开全局配置面板
        OpenGlobalConfigPanel();
    }
}
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
    spawnTitleLabel.Text = "🎮 单位/兵器/设施生成器";
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
    spawnModeUnitBtn.Text = "🚶 生成单位";
    spawnModeUnitBtn.CustomMinimumSize = new Vector2(90, 36);
    spawnModeUnitBtn.AddThemeFontSizeOverride("font_size", 12);
    spawnModeUnitBtn.Pressed += () => SwitchSpawnMode(SpawnMode.Unit);
    spawnModeSelector.AddChild(spawnModeUnitBtn);

    spawnModeWeaponBtn = new Button();
    spawnModeWeaponBtn.Text = "⚔️ 生成兵器";
    spawnModeWeaponBtn.CustomMinimumSize = new Vector2(90, 36);
    spawnModeWeaponBtn.AddThemeFontSizeOverride("font_size", 12);
    spawnModeWeaponBtn.Pressed += () => SwitchSpawnMode(SpawnMode.Weapon);
    spawnModeSelector.AddChild(spawnModeWeaponBtn);

    spawnModeFacilityBtn = new Button();
    spawnModeFacilityBtn.Text = "🏙️ 生成设施";
    spawnModeFacilityBtn.CustomMinimumSize = new Vector2(90, 36);
    spawnModeFacilityBtn.AddThemeFontSizeOverride("font_size", 12);
    spawnModeFacilityBtn.Pressed += () => SwitchSpawnMode(SpawnMode.Facility);
    spawnModeSelector.AddChild(spawnModeFacilityBtn);

    destroyModeBtn = new Button();
    destroyModeBtn.Text = "💀 销毁模式";
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
    dirLabel.Text = "朝向:";
    dirLabel.AddThemeFontSizeOverride("font_size", 12);
    dirLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.6f));
    dirLabel.CustomMinimumSize = new Vector2(40, 24);
    directionSelector.AddChild(dirLabel);

    string[] dirLabels = { "⬆️ 上", "➡️ 右", "⬇️ 下", "⬅️ 左" };
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
    spawnHintLabel.Text = "选择模式开始...";
    spawnHintLabel.HorizontalAlignment = HorizontalAlignment.Center;
    spawnHintLabel.AddThemeFontSizeOverride("font_size", 12);
    spawnHintLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.6f));
    spawnHintLabel.Position = new Vector2(10, 125);
    spawnHintLabel.Size = new Vector2(380, 25);
    spawnMenuPanel.AddChild(spawnHintLabel);

    // 滚动容器
    var scroll = new ScrollContainer();
    scroll.Position = new Vector2(15, 155);
    scroll.Size = new Vector2(370, 435);
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

private void ToggleDestroyMode()
{
    isDestroyMode = !isDestroyMode;
    destroyModeBtn.Text = isDestroyMode ? "❌ 退出销毁" : "💀 销毁模式";

    if (isDestroyMode)
    {
        spawnHintLabel.Text = "💀 销毁模式：点击格子上的单位/兵器即可销毁";
        spawnHintLabel.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
        currentSpawnMode = SpawnMode.None;
        selectedTemplatePath = "";
        if (directionSelector != null) directionSelector.Visible = false;
        UpdateModeButtonStyles();
    }
    else
    {
        spawnHintLabel.Text = "选择模式开始...";
        spawnHintLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.6f));
    }
}

private string GetFriendlyTypeName(Infantry unit)
{
    return unit switch
    {
        Mech => "机甲",
        MdTank => "重坦",
        LightTank => "轻坦",
        Recon => "侦察车",
        Artillery => "火炮",
        Rocket => "火箭",
        AntiAir => "防空高炮",
        APC => "运输",
        Flare => "照明车",
        Bike => "摩托兵",
        PipeRunner => "管道炮",
        Oozium => "史莱姆",
                AntiTank => "反坦克炮",
        FlyBomb => "飞弹",
        Infantry => "步兵",
        _ => unit.GetType().Name
    };
}

private string GetFriendlyWeaponName(Weapon weapon)
{
    return weapon switch
    {
        DeathRay => "死光炮",
        BlackCannon => "黑炮",
        Laser => "激光炮",
        Crystal => "黑水晶",
        _ => weapon.GetType().Name
    };
}


private void SwitchSpawnMode(SpawnMode mode)
{
    currentSpawnMode = mode;
    isDestroyMode = false;
    destroyModeBtn.Text = "💀 销毁模式";

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
    if (directionSelector != null) directionSelector.Visible = false;

    if (currentSpawnMode == SpawnMode.None) return;

    List<UnitTemplate> templates;
    if (currentSpawnMode == SpawnMode.Unit)
        templates = unitTemplates;
    else if (currentSpawnMode == SpawnMode.Weapon)
        templates = weaponTemplates;
    else
        templates = facilityTemplates;

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
        SpawnMode.Unit => "🚶 选择单位模板，然后点击地图格子生成",
        SpawnMode.Weapon => "⚔️ 选择兵器模板，然后点击地图格子生成",
        SpawnMode.Facility => "🏙️ 选择设施模板，然后点击地图格子生成",
        _ => "选择模式开始..."
    };
    spawnHintLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.9f, 0.6f));
}

private Button CreateTemplateButton(UnitTemplate template)
{
    var btn = new Button();
    btn.CustomMinimumSize = new Vector2(270, 50);
    btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;

    btn.Text = $"{template.DisplayName}\n{template.Description}";
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

    var teamHBox = new HBoxContainer();
    teamHBox.Position = new Vector2(180, 12);
    teamHBox.Size = new Vector2(100, 26);
    teamHBox.AddThemeConstantOverride("separation", 4);

    var p1Btn = new Button();
    p1Btn.Text = "P1";
    p1Btn.CustomMinimumSize = new Vector2(36, 24);
    p1Btn.AddThemeFontSizeOverride("font_size", 10);
    p1Btn.Pressed += () => {
        template.Team = "Player1";
        UpdateTemplateTeamIndicator(btn, "Player1");
    };
    teamHBox.AddChild(p1Btn);

    var p2Btn = new Button();
    p2Btn.Text = "P2";
    p2Btn.CustomMinimumSize = new Vector2(36, 24);
    p2Btn.AddThemeFontSizeOverride("font_size", 10);
    p2Btn.Pressed += () => {
        template.Team = "Player2";
        UpdateTemplateTeamIndicator(btn, "Player2");
    };
    teamHBox.AddChild(p2Btn);

    var p0Btn = new Button();
    p0Btn.Text = "P0";
    p0Btn.CustomMinimumSize = new Vector2(30, 24);
    p0Btn.AddThemeFontSizeOverride("font_size", 10);
    p0Btn.Pressed += () => {
        template.Team = "Player0";
        UpdateTemplateTeamIndicator(btn, "Player0");
    };
    teamHBox.AddChild(p0Btn);

    var pN1Btn = new Button();
    pN1Btn.Text = "P-1";
    pN1Btn.CustomMinimumSize = new Vector2(30, 24);
    pN1Btn.AddThemeFontSizeOverride("font_size", 10);
    pN1Btn.Pressed += () => {
        template.Team = "Player";
        UpdateTemplateTeamIndicator(btn, "Player");
    };
    teamHBox.AddChild(pN1Btn);

    btn.AddChild(teamHBox);

    btn.Pressed += () => {
        selectedTemplatePath = template.ScenePath;
        selectedTemplateCategory = template.Category;
        spawnHintLabel.Text = $"✅ 已选择: {template.DisplayName} ({template.Team})\n点击地图格子生成...";
        spawnHintLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));

        // ✅ 黑炮/大型黑炮：显示方向选择器
        bool isCannon = template.ScenePath.Contains("black_cannon") || template.ScenePath.Contains("large_cannon") || template.ScenePath.Contains("death_ray");
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
    teamLabel.Text = team switch {
        "Player1" => "🔴P1",
        "Player2" => "🔵P2",
        "Player0" => "⚪P0",
        "Player" => "🟣P-1",
        _ => team
    };
    teamLabel.AddThemeColorOverride("font_color", team switch {
        "Player1" => new Color(1, 0.3f, 0.3f),
        "Player2" => new Color(0.3f, 0.5f, 1f),
        "Player0" => new Color(0.7f, 0.7f, 0.7f),
        "Player" => new Color(0.7f, 0.4f, 0.9f),
        _ => Colors.White
    });
}

private void ResetTemplateButtonStyle(Button btn)
{
    var style = new StyleBoxFlat();
    style.BgColor = new Color(0.25f, 0.25f, 0.3f, 0.6f);
    style.SetCornerRadiusAll(8);
    btn.AddThemeStyleboxOverride("normal", style);
}

private void HighlightTemplateButton(Button btn, Color color)
{
    var style = new StyleBoxFlat();
    style.BgColor = new Color(color.R * 0.6f, color.G * 0.6f, color.B * 0.6f, 0.95f);
    style.SetCornerRadiusAll(8);
    style.SetBorderWidthAll(2);
    style.BorderColor = Colors.Yellow;
    btn.AddThemeStyleboxOverride("normal", style);
}

private void SpawnUnitAtGrid(Grids grid, string scenePath, string team)
{
    if (string.IsNullOrEmpty(scenePath) || grid == null) return;

    var scene = GD.Load<PackedScene>(scenePath);
    if (scene == null)
    {
        spawnHintLabel.Text = $"❌ 错误：无法加载场景\n{scenePath}";
        spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
        return;
    }

    try
    {
        if (grid.weapon != null)
        {
            spawnHintLabel.Text = "❌ 该格子已有兵器，不能放置单位";
            spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
            return;
        }

        var unit = scene.Instantiate<Infantry>();
        if (unit == null)
        {
            spawnHintLabel.Text = "❌ 实例化失败";
            return;
        }

        // ✅ 关键修复：强制重置所有回合状态，覆盖场景预设值
        unit.isMoved = false;
        unit.isAttacked = false;
        unit.hasActed = false;
        unit.state = UnitState.Idle;
        unit.movePoints = unit.defaultMovePoints;
        unit.originalGrid = null;

        unit.Position = grid.Position;
        unit.team = team;
        int unitNumber = gameManager.unitManager.AllUnits.Count + 1;
        string typeName = GetFriendlyTypeName(unit);
        unit.Name = $"{team}_{typeName}_{unitNumber}";

        gameManager.unitManager.units.AddChild(unit);

        bool bound = gameManager.unitManager.BindUnitToGrid(unit, true);
        if (!bound)
        {
            unit.QueueFree();
            // ✅ 从父节点中移除，防止 RefreshUnitList 遍历到已销毁对象
            if (unit.GetParent() == gameManager.unitManager.units)
            {
                gameManager.unitManager.units.RemoveChild(unit);
            }
            spawnHintLabel.Text = "❌ 绑定格子失败";
            return;
        }

        // ✅ 关键修复：添加到 AllUnits 列表（编辑模式生成时缺少这一步）
        gameManager.unitManager.AllUnits.Add(unit);

        gameManager.RegisterUnit(unit, GetUnitCategory(unit));
        unit.OnClickPiece = gameManager.OnSelectPiece;
        unit.UpdateTeamVisual();
        unit.UpdateHpLabel();
        if (unit.sprite != null)
            unit.StartBreath();

        gameManager.UpdateUnitLists();
        gameManager.RefreshSpecializedUnitLists();

        spawnHintLabel.Text = $"✅ 生成成功: {unit.Name}";
        spawnHintLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));

    }
    catch (Exception e)
    {
        spawnHintLabel.Text = $"❌ 生成失败: {e.Message}";
        spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
    }
    // 导入时不在此处调用 RefreshUnitList，避免遍历已废弃对象
    // 统一在 ParseAndImportMap 结束后刷新
    gameManager.UpdateUnitLists();
}

private void SpawnWeaponAtGrid(Grids grid, string scenePath, string team)
{
    if (string.IsNullOrEmpty(scenePath) || grid == null) return;

    var scene = GD.Load<PackedScene>(scenePath);
    if (scene == null)
    {
        return;
    }

    try
    {
        if (grid.weapon != null)
        {
            spawnHintLabel.Text = "❌ 该格子已有兵器";
            spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
            return;
        }

        var blockingUnit = grid.infantries.FirstOrDefault(u => 
            u != null && IsInstanceValid(u) && u.overlapType == UnitOverlapType.NonOverlapping);
        if (blockingUnit != null)
        {
            spawnHintLabel.Text = $"❌ 该格子被 {blockingUnit.Name} 占据";
            spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
            return;
        }

        var weapon = scene.Instantiate<Weapon>();
        if (weapon == null)
        {
            spawnHintLabel.Text = "❌ 兵器实例化失败";
            return;
        }

        // ✅ 关键修复：重置兵器行动状态，覆盖场景预设值
        weapon.hasActed = false;

        // ✅ 关键修复：Instantiate 不会立即触发 _Ready，多格兵器属性需在 _Ready 前手动设置
        if (weapon is LargeCannon lc && !weapon.isMultiTile)
        {
            weapon.isMultiTile = true;
            weapon.size = lc.multiTileSize;
        }
        else if (weapon is DeathRay dr && !weapon.isMultiTile)
        {
            weapon.isMultiTile = true;
            weapon.size = dr.multiTileSize;
        }

        weapon.Position = grid.Position;
        weapon.team = team;
        weapon.Name = $"{team}_{weapon.GetType().Name}_{gameManager.weaponManager.AllWeapons.Count}";

        gameManager.weaponManager.weaponsNode.AddChild(weapon);

        bool bound = gameManager.weaponManager.BindWeaponToGrid(weapon, true);
        if (!bound)
        {
            weapon.QueueFree();
            // ✅ 从父节点中移除，防止遍历到已销毁对象
            if (weapon.GetParent() == gameManager.weaponManager.weaponsNode)
            {
                gameManager.weaponManager.weaponsNode.RemoveChild(weapon);
            }
            spawnHintLabel.Text = "❌ 兵器绑定格子失败";
            return;
        }
        gameManager.weaponManager.AllWeapons.Add(weapon);
        weapon.OnClickWeapon = gameManager.OnSelectWeapon;
        weapon.UpdateHpLabel();
        if (weapon is BlackCannon cannon)
        {
            cannon.direction = pendingCannonDirection;
            cannon.UpdateDirectionVisual();
            cannon.UpdateAmmoVisual();
        }

        gameManager.UpdateUnitLists();

        spawnHintLabel.Text = $"✅ 生成兵器: {weapon.Name}";
        spawnHintLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));

    }
    catch (Exception e)
    {
        spawnHintLabel.Text = $"❌ 生成失败: {e.Message}";
        spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
    }
}

private void DestroyTargetAtGrid(Grids grid)
{
    if (grid == null) return;

    // 检查是否点击了多格兵器
    var multiTileWeapon = grid.weapons.FirstOrDefault(w => w != null && IsInstanceValid(w) && w.isMultiTile);
    if (multiTileWeapon != null)
    {
        // 多格兵器：弹出整体确认销毁对话框
        ShowMultiTileDestroyConfirm(multiTileWeapon);
        return;
    }

    var allTargets = new List<object>();

    foreach (var unit in grid.infantries.ToList())
    {
        if (unit != null && IsInstanceValid(unit))
            allTargets.Add(unit);
    }

    foreach (var weapon in grid.weapons.ToList())
    {
        if (weapon != null && IsInstanceValid(weapon))
            allTargets.Add(weapon);
    }

    if (grid.city != null && IsInstanceValid(grid.city))
    {
        allTargets.Add(grid.city);
    }

    if (allTargets.Count == 0)
    {
        spawnHintLabel.Text = "⚠️ 该格子没有可销毁的目标";
        return;
    }

    if (allTargets.Count == 1)
    {
        ExecuteDestroy(allTargets[0]);
    }
    else
    {
        ShowDestroySelectionMenu(grid, allTargets);
    }
}

    // 多格兵器整体确认销毁对话框
    private void ShowMultiTileDestroyConfirm(Weapon weapon)
    {
        var dialog = new Control();
        dialog.Name = "MultiTileDestroyDialog";
        dialog.ZIndex = 600;
        dialog.SetAnchorsPreset(Control.LayoutPreset.Center);

        var bg = new ColorRect();
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bg.Color = new Color(0, 0, 0, 0.6f);
        dialog.AddChild(bg);

        var panel = new Panel();
        panel.CustomMinimumSize = new Vector2(360, 200);
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.15f, 0.15f, 0.2f, 0.98f);
        style.SetCornerRadiusAll(12);
        style.SetBorderWidthAll(2);
        style.BorderColor = new Color(0.8f, 0.2f, 0.2f);
        panel.AddThemeStyleboxOverride("panel", style);
        dialog.AddChild(panel);

        var title = new Label();
        title.Text = $"💀 确认销毁多格兵器?";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 18);
        title.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f));
        title.Position = new Vector2(0, 20);
        title.Size = new Vector2(360, 35);
        panel.AddChild(title);

        var info = new Label();
        info.Text = $"{weapon.Name}\n占据 {weapon.size.X}×{weapon.size.Y} 格\n摧毁后不可通行";
        info.HorizontalAlignment = HorizontalAlignment.Center;
        info.AddThemeFontSizeOverride("font_size", 12);
        info.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        info.Position = new Vector2(0, 60);
        info.Size = new Vector2(360, 50);
        panel.AddChild(info);

        var hbox = new HBoxContainer();
        hbox.Position = new Vector2(60, 130);
        hbox.Size = new Vector2(240, 40);
        hbox.AddThemeConstantOverride("separation", 20);

        var confirmBtn = new Button();
        confirmBtn.Text = "💀 确认销毁";
        confirmBtn.CustomMinimumSize = new Vector2(100, 36);
        confirmBtn.Pressed += () => {
            dialog.QueueFree();
            ExecuteDestroy(weapon);
        };
        hbox.AddChild(confirmBtn);

        var cancelBtn = new Button();
        cancelBtn.Text = "❌ 取消";
        cancelBtn.CustomMinimumSize = new Vector2(100, 36);
        cancelBtn.Pressed += () => dialog.QueueFree();
        hbox.AddChild(cancelBtn);

        panel.AddChild(hbox);
        GetTree().CurrentScene.AddChild(dialog);
    }

private void ExecuteDestroy(object target)
{
    try
    {
        if (target is Infantry unit)
        {
            string name = unit.Name;
            gameManager.RemoveUnit(unit);
            spawnHintLabel.Text = $"💀 已销毁单位: {name}";
        }
        else if (target is Weapon weapon)
        {
            string name = weapon.Name;
            if (weapon.isMultiTile)
            {
                // ✅ 多格兵器：彻底清理所有占据格子的引用
                foreach (var og in weapon.occupiedGrids.ToList())
                {
                    if (og != null && IsInstanceValid(og))
                    {
                        og.weapons.Remove(weapon);
                        if (og.weapon == weapon)
                            og.weapon = og.weapons.Count > 0 ? og.weapons[0] : null;
                    }
                }
                weapon.occupiedGrids.Clear();
                
                // 从 GameManager 清理引用
                var gm = gameManager;
                if (gm != null)
                {
                    gm.weapons.Remove(weapon);
                    if (gm.selectedWeapon == weapon) gm.selectedWeapon = null;
                    if (gm.weaponManager?.AllWeapons.Contains(weapon) == true)
                        gm.weaponManager.AllWeapons.Remove(weapon);
                }
                
                weapon.QueueFree();
                spawnHintLabel.Text = $"💀 已销毁多格兵器: {name}";
            }
            else
            {
                // 单格兵器走原有路径
                gameManager.RemoveWeapon(weapon);
                spawnHintLabel.Text = $"💀 已销毁兵器: {name}";
            }
        }
        else if (target is Facility facility)
        {
            string name = facility.Name;
            if (facility.ParentGrid != null)
            {
                facility.ParentGrid.city = null;
            }
            facility.QueueFree();
            spawnHintLabel.Text = $"💀 已销毁设施: {name}";
            gameManager.UpdateUnitLists();
        }

        spawnHintLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
        gameManager.UpdateUnitLists();
    }
    catch (Exception e)
    {
        spawnHintLabel.Text = $"❌ 销毁失败: {e.Message}";
    }
}

private void ShowDestroySelectionMenu(Grids grid, List<object> targets)
{
    foreach (var child in selectionButtonContainer.GetChildren())
        child.QueueFree();

    selectionTitleLabel.Text = $"💀 选择要销毁的目标 ({targets.Count}个)";

    foreach (var target in targets)
    {
        string displayName = "";
        Color color = Colors.White;
        Action onClick = null;

        if (target is Infantry unit)
        {
            string teamEmoji = unit.team switch { "Player1" => "🔴", "Player2" => "🔵", "Player0" => "⚪", "Player" => "🟣", _ => "⚪" };
            displayName = $"{teamEmoji} [单位:{unit.GetType().Name}] {unit.Name} HP:{Mathf.CeilToInt(unit.health/10f)}";
            color = new Color(0.8f, 0.3f, 0.3f);
            onClick = () => {
                ExecuteDestroy(unit);
                CloseSelectionMenu();
            };
        }
        else if (target is Weapon weapon)
        {
            string teamEmoji = weapon.team switch { "Player1" => "🔴", "Player2" => "🔵", "Player0" => "⚪", "Player" => "🟣", _ => "⚪" };
            displayName = $"{teamEmoji} [兵器:{weapon.GetType().Name}] {weapon.Name} HP:{Mathf.CeilToInt(weapon.health/10f)}";
            color = new Color(0.6f, 0.2f, 0.2f);
            onClick = () => {
                ExecuteDestroy(weapon);
                CloseSelectionMenu();
            };
        }
        else if (target is Facility facility)
        {
            string teamEmoji = facility.facilityTeam switch { "Player1" => "🔴", "Player2" => "🔵", "Player0" => "⚪", "Player" => "🟣", _ => "⚪" };
            displayName = $"{teamEmoji} [设施:{facility.GetType().Name}] {facility.Name} (Team:{facility.facilityTeam})";
            color = new Color(0.8f, 0.5f, 0.2f);
            onClick = () => {
                ExecuteDestroy(facility);
                CloseSelectionMenu();
            };
        }

        var btn = CreateSelectionButton(displayName, color, onClick);
        selectionButtonContainer.AddChild(btn);
    }

    var cancelBtn = CreateSelectionButton("❌ 取消", new Color(0.5f, 0.5f, 0.5f), CloseSelectionMenu);
    selectionButtonContainer.AddChild(cancelBtn);

    var viewportSize = GetViewport().GetVisibleRect().Size;
    selectionMenuPanel.Position = new Vector2(
        (viewportSize.X - selectionMenuPanel.Size.X) / 2,
        (viewportSize.Y - selectionMenuPanel.Size.Y) / 2
    );
    selectionMenuPanel.Visible = true;
    isMenuOpen = true;
}

private UnitCategory GetUnitCategory(Infantry unit)
{
    return unit switch
    {
        Mech _ => UnitCategory.Mech,
        Oozium _ => UnitCategory.Oozium,
        LightTank _ => UnitCategory.Tank,
        Recon _ => UnitCategory.Recon,
        APC _ => UnitCategory.APC,
        Flare _ => UnitCategory.Vehicle,
        Bike _ => UnitCategory.Infantry,
        PipeRunner _ => UnitCategory.PipeRunner,
        Artillery _ => UnitCategory.Artillery,
        Rocket _ => UnitCategory.Rocket,
        FlyBomb _ => UnitCategory.FlyBomb,
                MdTank _ => UnitCategory.MdTank,
        AntiAir _ => UnitCategory.AntiAir,
        AntiTank _ => UnitCategory.AntiTank,
        Infantry _ => UnitCategory.Infantry,
        _ => UnitCategory.Other
    };
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
    UpdateModeButtonStyles();

    foreach (var child in spawnButtonContainer.GetChildren())
        child.QueueFree();

    spawnHintLabel.Text = "选择生成模式...";
}

private void CloseSpawnMenu()
{
    spawnMenuPanel.Visible = false;
    currentSpawnMode = SpawnMode.None;
    isDestroyMode = false;
    selectedTemplatePath = "";
    if (directionSelector != null) directionSelector.Visible = false;
    isMenuOpen = false;
}

// ========== 修改后的格子点击处理（整合生成系统）=========
public void OnGridClickedForEdit(Grids grid)
{
    if (!IsEditMode || grid == null) return;

    selectedGrid = grid;

    // ✅ 批量地形放置模式：直接改地形，不经过选择菜单
    if (isBatchTerrainMode)
    {
        grid.gridType = batchTerrainType;
        UpdateGridVisual(grid);
        return;
    }

    // 优先处理生成模式
    if (currentSpawnMode != SpawnMode.None && !string.IsNullOrEmpty(selectedTemplatePath))
    {
        if (currentSpawnMode == SpawnMode.Unit)
            SpawnUnitAtGrid(grid, selectedTemplatePath, unitTemplates.First(t => t.ScenePath == selectedTemplatePath).Team);
        else if (currentSpawnMode == SpawnMode.Weapon)
        {
            // 检查是否多格兵器预览模式
            if (isMultiTilePreviewMode && !string.IsNullOrEmpty(pendingMultiTileScenePath))
            {
                // 二次点击：必须点击预览范围内才确认，范围外取消
                if (previewMultiTileGrids.Contains(grid))
                {
                    ConfirmMultiTilePlacement();
                }
                else
                {
                    // 点击范围外：取消预览
                    ClearMultiTilePreview();
                    spawnHintLabel.Text = "❌ 已取消预览（点击范围外）";
                    spawnHintLabel.AddThemeColorOverride("font_color", Colors.Yellow);
                }
                return;
            }
            else
            {
                // 首次点击：尝试预览
                TryPreviewMultiTileWeapon(grid, selectedTemplatePath, weaponTemplates.First(t => t.ScenePath == selectedTemplatePath).Team);
                return;
            }
        }
        else if (currentSpawnMode == SpawnMode.Facility)
            SpawnFacilityAtGrid(grid, selectedTemplatePath, facilityTemplates.First(t => t.ScenePath == selectedTemplatePath).Team);
        return;
    }

    // 处理销毁模式
    if (isDestroyMode)
    {
        DestroyTargetAtGrid(grid);
        return;
    }

    // 原有逻辑：收集所有可编辑对象
    var allTargets = new List<EditTarget>();

    var units = grid.infantries?
        .Where(u => u != null && IsInstanceValid(u))
        .OrderByDescending(u => u.health)
        .ToList() ?? new List<Infantry>();

    foreach (var unit in units)
    {
        string teamEmoji = unit.team switch { "Player1" => "🔴", "Player2" => "🔵", "Player0" => "⚪", "Player" => "🟣", _ => "⚪" };
        string hpText = unit.maxHealth <= 999 ? $"HP:{Mathf.CeilToInt(unit.health / 10f)}" : $"HP:{unit.health}";
        allTargets.Add(new EditTarget
        {
            TargetType = "unit",
            DisplayName = $"{teamEmoji} [单位:{unit.GetType().Name}] {unit.Name} ({hpText})",
            TargetObject = unit,
            Color = new Color(0.3f, 0.5f, 0.8f)
        });
    }

    var weapons = grid.weapons?
        .Where(w => w != null && IsInstanceValid(w))
        .ToList() ?? new List<Weapon>();

    foreach (var weapon in weapons)
    {
        string teamEmoji = weapon.team switch { "Player1" => "🔴", "Player2" => "🔵", "Player0" => "⚪", "Player" => "🟣", _ => "⚪" };
        string hpText = weapon.maxHealth <= 999 ? $"HP:{Mathf.CeilToInt(weapon.health / 10f)}" : $"HP:{weapon.health}";
        allTargets.Add(new EditTarget
        {
            TargetType = "weapon",
            DisplayName = $"{teamEmoji} [兵器:{weapon.GetType().Name}] {weapon.Name} ({hpText})",
            TargetObject = weapon,
            Color = new Color(0.7f, 0.4f, 0.3f)
        });
    }

    // 添加设施选项
    if (grid.city != null)
    {
        allTargets.Add(new EditTarget
        {
            TargetType = "facility",
            DisplayName = $"🏙️ [设施:{grid.city.GetType().Name}] {grid.city.facilityTeam}",
            TargetObject = grid.city,
            Color = new Color(0.8f, 0.6f, 0.2f)
        });
    }

    // 添加功能选项
    allTargets.Add(new EditTarget
    {
        TargetType = "grid",
        DisplayName = "🟫 修改格子地形/属性",
        TargetObject = grid,
        Color = new Color(0.4f, 0.7f, 0.4f)
    });

    allTargets.Add(new EditTarget
    {
        TargetType = "spawn",
        DisplayName = "🎮 生成新单位/兵器...",
        TargetObject = null,
        Color = new Color(0.4f, 0.6f, 0.9f)
    });

    if (allTargets.Count == 1)
    {
        OpenTerrainMenu(grid);
    }
    else
    {
        ShowMultiSelectMenu(allTargets);
    }
}

// 修改多选菜单，添加生成分支
private void ShowMultiSelectMenu(List<EditTarget> targets)
{
    foreach (var child in selectionButtonContainer.GetChildren())
        child.QueueFree();

    int objectCount = targets.Count(t => t.TargetType != "grid" && t.TargetType != "spawn");
    selectionTitleLabel.Text = $"格子 {selectedGrid.GridIndex} - 选择操作 ({objectCount}个对象)";

    foreach (var target in targets)
    {
        Button btn;
        switch (target.TargetType)
        {
            case "unit":
                btn = CreateSelectionButton(target.DisplayName, target.Color, () => {
                    CloseSelectionMenu();
                    OpenUnitEditor((Infantry)target.TargetObject);
                });
                break;
            case "weapon":
                btn = CreateSelectionButton(target.DisplayName, target.Color, () => {
                    CloseSelectionMenu();
                    OpenWeaponEditor((Weapon)target.TargetObject);
                });
                break;
            case "grid":
                btn = CreateSelectionButton(target.DisplayName, target.Color, () => {
                    CloseSelectionMenu();
                    OpenTerrainMenu(selectedGrid);
                });
                break;
            case "spawn":
                btn = CreateSelectionButton(target.DisplayName, target.Color, () => {
                    CloseSelectionMenu();
                    OpenSpawnMenu();
                });
                break;
            case "facility":
                btn = CreateSelectionButton(target.DisplayName, target.Color, () => {
                    CloseSelectionMenu();
                    OpenFacilityMenu(selectedGrid);
                });
                break;
            default:
                continue;
        }
        selectionButtonContainer.AddChild(btn);
    }

    var viewportSize = GetViewport().GetVisibleRect().Size;
    selectionMenuPanel.Position = new Vector2(
        (viewportSize.X - selectionMenuPanel.Size.X) / 2,
        (viewportSize.Y - selectionMenuPanel.Size.Y) / 2
    );
    selectionMenuPanel.Visible = true;
    isMenuOpen = true;
}

// 新增：打开兵器编辑器
private void OpenWeaponEditor(Weapon weapon)
{
    currentEditingTarget = weapon;
    propertyEditorTitle.Text = $"编辑兵器: {weapon.Name} ({weapon.GetType().Name})";

    BuildPropertyEditor(weapon);

    var viewportSize = GetViewport().GetVisibleRect().Size;
    propertyEditorPanel.Position = new Vector2(
        (viewportSize.X - propertyEditorPanel.Size.X) / 2,
        (viewportSize.Y - propertyEditorPanel.Size.Y) / 2
    );
    propertyEditorPanel.Visible = true;
    isMenuOpen = true;
}

// TerrainEditor.cs - 修改 CreateMemberControl，确保修改 team 时调用 RefreshTeamVisual
private void CreateMemberControl(object target, string name, Type memberType, Func<object> getter, Action<object> setter)
{
    var hbox = new HBoxContainer();
    hbox.CustomMinimumSize = new Vector2(0, 32);
    hbox.AddThemeConstantOverride("separation", 8);

    var nameLabel = new Label();
    nameLabel.Text = name;
    nameLabel.CustomMinimumSize = new Vector2(150, 28);
    nameLabel.AddThemeFontSizeOverride("font_size", 12);
    nameLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.9f));
    hbox.AddChild(nameLabel);

    var value = getter();

    Action<object> wrappedSetter = (newValue) => {
        setter(newValue);

        if (name == "team" && target is Node2D node)
        {
            RefreshTeamVisual(target);
        }
        else if (name == "facilityTeam" && target is Facility facility)
        {
            facility.UpdateCityVisual();
            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.UpdateUnitLists();
        }
        else
        {
            OnPropertyChanged(target, name);
        }
    };

    Control editor = CreateEditorForType(memberType, value, getter, wrappedSetter);

    if (editor != null)
    {
        editor.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hbox.AddChild(editor);
    }

    propertyContainer.AddChild(hbox);
}

private Control CreateEditorForType(Type propType, object value, Func<object> getter, Action<object> onChanged)
{
    if (propType == typeof(int))
    {
        var spinBox = new SpinBox();
        spinBox.MinValue = -999999;
        spinBox.MaxValue = 999999;
        spinBox.Value = (int)(value ?? 0);
        spinBox.CustomMinimumSize = new Vector2(100, 28);
        spinBox.AddThemeFontSizeOverride("font_size", 12);
        spinBox.ValueChanged += (newVal) => onChanged((int)newVal);
        return spinBox;
    }

    if (propType == typeof(float))
    {
        var spinBox = new SpinBox();
        spinBox.MinValue = -999999;
        spinBox.MaxValue = 999999;
        spinBox.Step = 0.01;
        spinBox.Value = (float)(value ?? 0f);
        spinBox.CustomMinimumSize = new Vector2(100, 28);
        spinBox.AddThemeFontSizeOverride("font_size", 12);
        spinBox.ValueChanged += (newVal) => onChanged((float)newVal);
        return spinBox;
    }

    if (propType == typeof(bool))
    {
        var checkBox = new CheckBox();
        checkBox.ButtonPressed = (bool)(value ?? false);
        checkBox.CustomMinimumSize = new Vector2(60, 28);
        checkBox.Toggled += (pressed) => onChanged(pressed);
        return checkBox;
    }

    if (propType == typeof(string))
    {
        var lineEdit = new LineEdit();
        lineEdit.Text = (string)(value ?? "");
        lineEdit.CustomMinimumSize = new Vector2(120, 28);
        lineEdit.AddThemeFontSizeOverride("font_size", 12);
        lineEdit.TextChanged += (newText) => onChanged(newText);
        return lineEdit;
    }

    if (propType.IsEnum)
    {
        var optionBtn = new OptionButton();
        var enumNames = Enum.GetNames(propType);

        for (int i = 0; i < enumNames.Length; i++)
        {
            optionBtn.AddItem(enumNames[i], i);
        }

        optionBtn.Selected = (int)(value ?? 0);
        optionBtn.CustomMinimumSize = new Vector2(120, 28);
        optionBtn.AddThemeFontSizeOverride("font_size", 12);
        optionBtn.ItemSelected += (index) => {
            var newValue = Enum.ToObject(propType, (int)index);
            onChanged(newValue);
        };
        return optionBtn;
    }

    if (propType == typeof(Color))
    {
        var colorPicker = new ColorPickerButton();
        colorPicker.Color = (Color)(value ?? Colors.White);
        colorPicker.CustomMinimumSize = new Vector2(80, 28);
        colorPicker.ColorChanged += (newColor) => onChanged(newColor);
        return colorPicker;
    }

    if (propType == typeof(Vector2))
    {
        var vec = (Vector2)(value ?? Vector2.Zero);
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 4);

        var xSpin = new SpinBox { MinValue = -99999, MaxValue = 99999, Step = 0.1, Value = vec.X, CustomMinimumSize = new Vector2(70, 28) };
        var ySpin = new SpinBox { MinValue = -99999, MaxValue = 99999, Step = 0.1, Value = vec.Y, CustomMinimumSize = new Vector2(70, 28) };

        var capturedGetter = getter;
        xSpin.ValueChanged += (v) => {
            var current = (Vector2)(capturedGetter?.Invoke() ?? Vector2.Zero);
            var newVec = new Vector2((float)v, current.Y);
            onChanged(newVec);
        };
        ySpin.ValueChanged += (v) => {
            var current = (Vector2)(capturedGetter?.Invoke() ?? Vector2.Zero);
            var newVec = new Vector2(current.X, (float)v);
            onChanged(newVec);
        };

        hbox.AddChild(new Label { Text = "X:", CustomMinimumSize = new Vector2(15, 20) });
        hbox.AddChild(xSpin);
        hbox.AddChild(new Label { Text = "Y:", CustomMinimumSize = new Vector2(15, 20) });
        hbox.AddChild(ySpin);
        return hbox;
    }

    if (propType == typeof(Vector2I))
    {
        var vec = (Vector2I)(value ?? Vector2I.Zero);
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 4);

        var xSpin = new SpinBox { MinValue = -99999, MaxValue = 99999, Value = vec.X, CustomMinimumSize = new Vector2(70, 28) };
        var ySpin = new SpinBox { MinValue = -99999, MaxValue = 99999, Value = vec.Y, CustomMinimumSize = new Vector2(70, 28) };

        var capturedGetter = getter;
        xSpin.ValueChanged += (v) => {
            var current = (Vector2I)(capturedGetter?.Invoke() ?? Vector2I.Zero);
            onChanged(new Vector2I((int)v, current.Y));
        };
        ySpin.ValueChanged += (v) => {
            var current = (Vector2I)(capturedGetter?.Invoke() ?? Vector2I.Zero);
            onChanged(new Vector2I(current.X, (int)v));
        };

        hbox.AddChild(new Label { Text = "X:", CustomMinimumSize = new Vector2(15, 20) });
        hbox.AddChild(xSpin);
        hbox.AddChild(new Label { Text = "Y:", CustomMinimumSize = new Vector2(15, 20) });
        hbox.AddChild(ySpin);
        return hbox;
    }

    if (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(Godot.Collections.Dictionary<,>))
    {
        var keyType = propType.GetGenericArguments()[0];
        var valueType = propType.GetGenericArguments()[1];
        var dict = value as System.Collections.IDictionary;

        if (keyType == typeof(float) && valueType == typeof(bool))
        {
            return CreateAngleConfigEditor(
                value as Godot.Collections.Dictionary<float, bool>,
                (newDict) => onChanged(newDict)
            );
        }
        if (keyType == typeof(string) && valueType == typeof(int))
        {
            var dictin = value as Godot.Collections.Dictionary<string, int>;
            bool isPrimary = (currentEditingTarget as Infantry)?.attackMatrix == dict;
            string title = isPrimary ? "主武器" : "副武器";
            Color color = isPrimary ? Colors.Red : Colors.Blue;
            return CreateAttackMatrixEditor(dictin, title, color, (newDict) => onChanged(newDict));
        }

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 2);

        var label = new Label();
        label.Text = $"Dictionary ({(dict?.Count ?? 0)} items) - 只读";
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", new Color(0.5f, 0.7f, 0.5f));
        vbox.AddChild(label);

        if (dict != null)
        {
            foreach (var key in dict.Keys)
            {
                var itemLabel = new Label();
                itemLabel.Text = $"  {key}: {dict[key]}";
                itemLabel.AddThemeFontSizeOverride("font_size", 10);
                itemLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
                vbox.AddChild(itemLabel);
            }
        }
        return vbox;
    }

    if (propType == typeof(Godot.Collections.Array<string>))
    {
        var array = (Godot.Collections.Array<string>)(value ?? new Godot.Collections.Array<string>());
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 2);

        var textEdit = new TextEdit();
        textEdit.Text = string.Join(",", array);
        textEdit.CustomMinimumSize = new Vector2(120, 60);
        textEdit.AddThemeFontSizeOverride("font_size", 11);

        textEdit.TextChanged += () => {
            var newArray = new Godot.Collections.Array<string>(
                textEdit.Text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToArray()
            );
            onChanged(newArray);
        };

        vbox.AddChild(textEdit);
        return vbox;
    }

    if (typeof(Node).IsAssignableFrom(propType))
    {
        var label = new Label();
        var node = value as Node;
        label.Text = node != null ? $"[{propType.Name}] {node.Name}" : "[null]";
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", new Color(0.5f, 0.7f, 0.5f));
        return label;
    }

    var readOnly = new Label();
    readOnly.Text = $"[{propType.Name}] {value ?? "null"}";
    readOnly.AddThemeFontSizeOverride("font_size", 11);
    readOnly.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
    return readOnly;
}
// ========== ✅ 新增：Laser角度配置专用编辑器 ==========
    private Control CreateAngleConfigEditor(Godot.Collections.Dictionary<float, bool> dict, Action<object> onChanged)
    {
        var mainVbox = new VBoxContainer();
        mainVbox.AddThemeConstantOverride("separation", 6);

        var titleLabel = new Label();
        titleLabel.Text = "角度配置 (°)";
        titleLabel.AddThemeFontSizeOverride("font_size", 13);
        titleLabel.AddThemeColorOverride("font_color", Colors.Yellow);
        mainVbox.AddChild(titleLabel);

        var hintLabel = new Label();
        hintLabel.Text = "勾选=启用该角度发射";
        hintLabel.AddThemeFontSizeOverride("font_size", 10);
        hintLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.6f));
        mainVbox.AddChild(hintLabel);

        var anglesContainer = new VBoxContainer();
        anglesContainer.Name = "AnglesContainer";
        anglesContainer.AddThemeConstantOverride("separation", 4);
        mainVbox.AddChild(anglesContainer);

        Action refreshAngles = () => { };
        refreshAngles = () => {
            foreach (var child in anglesContainer.GetChildren())
                child.QueueFree();

            if (dict == null || dict.Count == 0)
            {
                var emptyLabel = new Label();
                emptyLabel.Text = "(无配置)";
                emptyLabel.AddThemeFontSizeOverride("font_size", 11);
                emptyLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
                anglesContainer.AddChild(emptyLabel);
                return;
            }

            var sortedPairs = dict.OrderBy(kvp => kvp.Key).ToList();
            foreach (var kvp in sortedPairs)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 8);

                var angleLabel = new Label();
                angleLabel.Text = $"{kvp.Key:F1}°";
                angleLabel.CustomMinimumSize = new Vector2(60, 24);
                angleLabel.AddThemeFontSizeOverride("font_size", 12);
                angleLabel.AddThemeColorOverride("font_color", kvp.Value ? Colors.Green : Colors.Red);
                row.AddChild(angleLabel);

                var checkBox = new CheckBox();
                checkBox.ButtonPressed = kvp.Value;
                checkBox.CustomMinimumSize = new Vector2(24, 24);
                float capturedAngle = kvp.Key;
                bool currentValue = kvp.Value;
                checkBox.Toggled += (pressed) => {
                    dict[capturedAngle] = pressed;
                    onChanged(dict);
                    refreshAngles?.Invoke();
                    if (currentEditingTarget is Laser laser)
                    {
                        if (pressed) laser.EnableAngle(capturedAngle);
                        else laser.DisableAngle(capturedAngle);
                    }
                };
                row.AddChild(checkBox);

                var statusLabel = new Label();
                statusLabel.Text = currentValue ? "✓ 启用" : "✗ 禁用";
                statusLabel.AddThemeFontSizeOverride("font_size", 10);
                statusLabel.AddThemeColorOverride("font_color", currentValue ? new Color(0.3f, 1, 0.3f) : new Color(1, 0.3f, 0.3f));
                row.AddChild(statusLabel);

                var deleteBtn = new Button();
                deleteBtn.Text = "删除";
                deleteBtn.CustomMinimumSize = new Vector2(50, 22);
                deleteBtn.AddThemeFontSizeOverride("font_size", 10);
                float deleteAngle = kvp.Key;
                deleteBtn.Pressed += () => {
                    dict.Remove(deleteAngle);
                    onChanged(dict);
                    refreshAngles?.Invoke();
                    if (currentEditingTarget is Laser laser)
                        laser.DisableAngle(deleteAngle);
                };
                row.AddChild(deleteBtn);

                anglesContainer.AddChild(row);
            }
        };

        refreshAngles();

        var addSeparator = new HSeparator();
        addSeparator.CustomMinimumSize = new Vector2(0, 4);
        mainVbox.AddChild(addSeparator);

        var addTitle = new Label();
        addTitle.Text = "添加新角度:";
        addTitle.AddThemeFontSizeOverride("font_size", 12);
        addTitle.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.3f));
        mainVbox.AddChild(addTitle);

        var addRow = new HBoxContainer();
        addRow.AddThemeConstantOverride("separation", 6);

        var angleSpin = new SpinBox();
        angleSpin.MinValue = 0;
        angleSpin.MaxValue = 359;
        angleSpin.Step = 1;
        angleSpin.Value = 0;
        angleSpin.CustomMinimumSize = new Vector2(80, 28);
        angleSpin.AddThemeFontSizeOverride("font_size", 11);
        addRow.AddChild(angleSpin);

        var addBtn = new Button();
        addBtn.Text = "添加";
        addBtn.CustomMinimumSize = new Vector2(60, 28);
        addBtn.AddThemeFontSizeOverride("font_size", 11);
        addBtn.Pressed += () => {
            float newAngle = (float)angleSpin.Value;
            while (newAngle < 0) newAngle += 360;
            while (newAngle >= 360) newAngle -= 360;

            if (!dict.ContainsKey(newAngle))
            {
                dict[newAngle] = true;
                onChanged(dict);
                refreshAngles?.Invoke();
                if (currentEditingTarget is Laser laser)
                    laser.EnableAngle(newAngle);
            }
        };
        addRow.AddChild(addBtn);

        mainVbox.AddChild(addRow);

        var presetRow = new HBoxContainer();
        presetRow.AddThemeConstantOverride("separation", 4);
        var presetLabel = new Label();
        presetLabel.Text = "快速添加:";
        presetLabel.AddThemeFontSizeOverride("font_size", 10);
        presetRow.AddChild(presetLabel);

        float[] presets = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };
        foreach (var preset in presets)
        {
            var presetBtn = new Button();
            presetBtn.Text = $"{preset:F0}°";
            presetBtn.CustomMinimumSize = new Vector2(40, 24);
            presetBtn.AddThemeFontSizeOverride("font_size", 9);
            float capturedPreset = preset;
            presetBtn.Pressed += () => {
                if (!dict.ContainsKey(capturedPreset))
                {
                    dict[capturedPreset] = true;
                    onChanged(dict);
                    refreshAngles?.Invoke();
                    if (currentEditingTarget is Laser laser)
                        laser.EnableAngle(capturedPreset);
                }
            };
            presetRow.AddChild(presetBtn);
        }
        mainVbox.AddChild(presetRow);

        if (currentEditingTarget is Laser)
        {
            var applyBtn = new Button();
            applyBtn.Text = "🔄 应用角度配置到激光炮";
            applyBtn.CustomMinimumSize = new Vector2(200, 32);
            applyBtn.AddThemeFontSizeOverride("font_size", 12);
            applyBtn.AddThemeColorOverride("font_color", Colors.Yellow);
            var applyStyle = new StyleBoxFlat();
            applyStyle.BgColor = new Color(0.2f, 0.4f, 0.2f, 0.8f);
            applyStyle.SetCornerRadiusAll(6);
            applyBtn.AddThemeStyleboxOverride("normal", applyStyle);
            applyBtn.Pressed += () => {
                if (currentEditingTarget is Laser laser)
                {
                    laser.ApplyAngleConfigChanges();
                }
            };
            mainVbox.AddChild(applyBtn);
        }

        return mainVbox;
    }

    private Control CreateAttackMatrixEditor(
    Godot.Collections.Dictionary<string, int> dict, 
    string title,
    Color titleColor,
    Action<object> onChanged)
{
    var mainVbox = new VBoxContainer();
    mainVbox.AddThemeConstantOverride("separation", 4);

    var titleLabel = new Label();
    titleLabel.Text = $"⚔️ {title}";
    titleLabel.AddThemeFontSizeOverride("font_size", 13);
    titleLabel.AddThemeColorOverride("font_color", titleColor);
    mainVbox.AddChild(titleLabel);

    if (title == "主武器")
    {
        var mulLabel = new Label();
        mulLabel.Text = $"反击系数: {((Infantry)currentEditingTarget).counterMul:F1}";
        mulLabel.AddThemeFontSizeOverride("font_size", 10);
        mulLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        mainVbox.AddChild(mulLabel);
    }

    string attackerType = currentEditingTarget?.GetType().Name ?? "Unknown";
    string[] defenderTypes = { "Infantry", "Mech", "LightTank", "Artillery", "Rocket", "APC", "Oozium" };

    foreach (var defenderType in defenderTypes)
    {
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 8);

        var nameLabel = new Label();
        nameLabel.Text = $"→ {defenderType}:";
        nameLabel.CustomMinimumSize = new Vector2(100, 24);
        nameLabel.AddThemeFontSizeOverride("font_size", 11);
        hbox.AddChild(nameLabel);

        string key = $"{attackerType}_{defenderType}";
        int currentValue = dict.GetValueOrDefault(key, 0);

        var spinBox = new SpinBox();
        spinBox.MinValue = 0;
        spinBox.MaxValue = 999;
        spinBox.Value = currentValue;
        spinBox.CustomMinimumSize = new Vector2(80, 24);
        spinBox.AddThemeFontSizeOverride("font_size", 11);

        string capturedKey = key;
        spinBox.ValueChanged += (newVal) => {
            dict[capturedKey] = (int)newVal;
            onChanged(dict);
        };

        hbox.AddChild(spinBox);

        if (currentValue == 0)
        {
            var hintLabel = new Label();
            hintLabel.Text = "-";
            hintLabel.AddThemeColorOverride("font_color", Colors.Red);
            hbox.AddChild(hintLabel);
        }
        else if (title == "主武器")
        {
            float mul = ((Infantry)currentEditingTarget).counterMul;
            int counterVal = Mathf.RoundToInt(currentValue * mul);
            var counterLabel = new Label();
            counterLabel.Text = $"反:{counterVal}";
            counterLabel.AddThemeFontSizeOverride("font_size", 10);
            counterLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.8f, 0.5f));
            hbox.AddChild(counterLabel);
        }

        mainVbox.AddChild(hbox);
    }

    return mainVbox;
}

private void ApplyPreset(Godot.Collections.Dictionary<string, int> dict, string attackerType, string preset)
{
    string[] defenders = { "Infantry", "Mech", "LightTank", "Artillery", "Rocket", "APC", "Oozium" };

    foreach (var d in defenders)
    {
        string key = $"{attackerType}_{d}";
        int value = preset switch
        {
            "infantry" => d switch {
                "Infantry" => 55, "Mech" => 45, "LightTank" => 15,
                "Artillery" => 25, "Rocket" => 25, "APC" => 15, "Oozium" => 0, _ => 0
            },
            "armor" => d switch {
                "Infantry" => 75, "Mech" => 70, "LightTank" => 55,
                "Artillery" => 45, "Rocket" => 55, "APC" => 45, "Oozium" => 0, _ => 0
            },
            _ => 50
        };
        dict[key] = value;
    }
}

    // ========== ✅ 创建选择菜单（单位/兵器/格子 多选一） ==========
    private void CreateSelectionMenu()
    {
        selectionMenuPanel = new Panel();
        selectionMenuPanel.Name = "SelectionMenuPanel";
        selectionMenuPanel.CustomMinimumSize = new Vector2(320, 400);
        selectionMenuPanel.SetAnchorsPreset(LayoutPreset.Center);
        selectionMenuPanel.Visible = false;
        selectionMenuPanel.ZIndex = 1000;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.12f, 0.12f, 0.18f, 0.96f);
        panelStyle.SetCornerRadiusAll(16);
        panelStyle.SetBorderWidthAll(3);
        panelStyle.BorderColor = new Color(0.6f, 0.6f, 0.8f);
        selectionMenuPanel.AddThemeStyleboxOverride("panel", panelStyle);

        selectionTitleLabel = new Label();
        selectionTitleLabel.Name = "SelectionTitle";
        selectionTitleLabel.Text = "选择编辑目标";
        selectionTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        selectionTitleLabel.AddThemeFontSizeOverride("font_size", 20);
        selectionTitleLabel.AddThemeColorOverride("font_color", Colors.Yellow);
        selectionTitleLabel.CustomMinimumSize = new Vector2(0, 35);
        selectionTitleLabel.Position = new Vector2(0, 12);
        selectionTitleLabel.Size = new Vector2(320, 35);
        selectionMenuPanel.AddChild(selectionTitleLabel);


        var closeBtn = new Button();
        closeBtn.Text = "✕";
        closeBtn.CustomMinimumSize = new Vector2(32, 32);
        closeBtn.Position = new Vector2(280, 10);
        closeBtn.Pressed += CloseSelectionMenu;
        selectionMenuPanel.AddChild(closeBtn);

        var scroll = new ScrollContainer();
        scroll.Position = new Vector2(15, 55);
        scroll.Size = new Vector2(290, 330);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        selectionButtonContainer = new VBoxContainer();
        selectionButtonContainer.Name = "SelectionButtonContainer";
        selectionButtonContainer.AddThemeConstantOverride("separation", 8);
        selectionButtonContainer.Size = new Vector2(290, 0);

        scroll.AddChild(selectionButtonContainer);
        selectionMenuPanel.AddChild(scroll);

        AddChild(selectionMenuPanel);
            var dragHandle = new Panel();
    dragHandle.CustomMinimumSize = new Vector2(320, 30);
    dragHandle.Position = new Vector2(0, 0);
    var handleStyle = new StyleBoxFlat();
    handleStyle.BgColor = new Color(0.3f, 0.3f, 0.4f, 0.5f);
    handleStyle.SetCornerRadiusAll(8);
    dragHandle.AddThemeStyleboxOverride("panel", handleStyle);

    var dragHint = new Label();
    dragHint.Text = "⋮⋮ 拖拽移动";
    dragHint.AddThemeFontSizeOverride("font_size", 10);
    dragHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.8f));
    dragHint.Position = new Vector2(8, 2);
    dragHandle.AddChild(dragHint);
    }

    // ========== ✅ 创建通用属性编辑器（支持字段+属性） ==========
    private void CreatePropertyEditor()
    {
        propertyEditorPanel = new Panel();
        propertyEditorPanel.Name = "PropertyEditorPanel";
        propertyEditorPanel.CustomMinimumSize = new Vector2(380, 600);
        propertyEditorPanel.SetAnchorsPreset(LayoutPreset.Center);
        propertyEditorPanel.Visible = false;
        propertyEditorPanel.ZIndex = 1100;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.1f, 0.12f, 0.16f, 0.97f);
        panelStyle.SetCornerRadiusAll(12);
        panelStyle.SetBorderWidthAll(2);
        panelStyle.BorderColor = new Color(0.4f, 0.7f, 0.4f);
        propertyEditorPanel.AddThemeStyleboxOverride("panel", panelStyle);

        // ✅ 新增：拖拽手柄
        var dragHandle = new Panel();
        dragHandle.Name = "DragHandle";
        dragHandle.CustomMinimumSize = new Vector2(380, 30);
        dragHandle.Position = new Vector2(0, 0);
        var handleStyle = new StyleBoxFlat();
        handleStyle.BgColor = new Color(0.3f, 0.3f, 0.4f, 0.5f);
        handleStyle.SetCornerRadiusAll(8);
        dragHandle.AddThemeStyleboxOverride("panel", handleStyle);
        var dragHint = new Label();
        dragHint.Text = "⋮⋮ 拖拽移动";
        dragHint.AddThemeFontSizeOverride("font_size", 10);
        dragHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.8f));
        dragHint.Position = new Vector2(8, 2);
        dragHandle.AddChild(dragHint);
        propertyEditorPanel.AddChild(dragHandle);
        MakeMenuDraggable(propertyEditorPanel, dragHandle);

        var titleBar = new HBoxContainer();
        titleBar.Position = new Vector2(10, 35);
        titleBar.Size = new Vector2(360, 35);

        propertyEditorTitle = new Label();
        propertyEditorTitle.Name = "PropertyEditorTitle";
        propertyEditorTitle.Text = "属性编辑器";
        propertyEditorTitle.AddThemeFontSizeOverride("font_size", 18);
        propertyEditorTitle.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));
        propertyEditorTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titleBar.AddChild(propertyEditorTitle);

        propertyEditorCloseBtn = new Button();
        propertyEditorCloseBtn.Text = "✕";
        propertyEditorCloseBtn.CustomMinimumSize = new Vector2(35, 35);
        propertyEditorCloseBtn.Pressed += ClosePropertyEditor;
        titleBar.AddChild(propertyEditorCloseBtn);

        propertyEditorPanel.AddChild(titleBar);

        propertyScrollContainer = new ScrollContainer();
        propertyScrollContainer.Position = new Vector2(10, 75);
        propertyScrollContainer.Size = new Vector2(360, 475);
        propertyScrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        propertyContainer = new VBoxContainer();
        propertyContainer.Name = "PropertyContainer";
        propertyContainer.AddThemeConstantOverride("separation", 6);
        propertyContainer.Size = new Vector2(360, 0);

        propertyScrollContainer.AddChild(propertyContainer);
        propertyEditorPanel.AddChild(propertyScrollContainer);

        propertyEditorSaveBtn = new Button();
        propertyEditorSaveBtn.Text = "💾 保存修改";
        propertyEditorSaveBtn.CustomMinimumSize = new Vector2(200, 40);
        propertyEditorSaveBtn.Position = new Vector2(90, 580);
        propertyEditorSaveBtn.AddThemeFontSizeOverride("font_size", 16);
        propertyEditorSaveBtn.AddThemeColorOverride("font_color", Colors.White);

        var saveStyle = new StyleBoxFlat();
        saveStyle.BgColor = new Color(0.2f, 0.6f, 0.3f, 0.9f);
        saveStyle.SetCornerRadiusAll(8);
        propertyEditorSaveBtn.AddThemeStyleboxOverride("normal", saveStyle);

        propertyEditorSaveBtn.Pressed += OnSaveProperties;
        propertyEditorPanel.AddChild(propertyEditorSaveBtn);

        AddChild(propertyEditorPanel);
    }

    // ========== 模式切换按钮 ==========
    private void CreateModeToggleButton()
    {
        modeToggleButton = new Button();
        modeToggleButton.Name = "ModeToggleButton";
        modeToggleButton.Text = "🛠️ 编辑模式";
        modeToggleButton.CustomMinimumSize = new Vector2(140, 50);

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.3f, 0.5f, 0.3f, 0.9f);
        style.SetCornerRadiusAll(8);
        modeToggleButton.AddThemeStyleboxOverride("normal", style);

        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = new Color(0.4f, 0.6f, 0.4f, 0.95f);
        hoverStyle.SetCornerRadiusAll(8);
        modeToggleButton.AddThemeStyleboxOverride("hover", hoverStyle);

        modeToggleButton.AddThemeFontSizeOverride("font_size", 16);
        modeToggleButton.AddThemeColorOverride("font_color", Colors.White);
        modeToggleButton.Pressed += OnModeTogglePressed;

        AddChild(modeToggleButton);
    }

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
        terrainTitleLabel.Text = "选择地形类型";
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
    dragHint.Text = "⋮⋮ 拖拽移动";
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

        selectionTitleLabel.Text = $"格子 {grid.GridIndex} - 选择编辑目标";

        int optionCount = 0;

        foreach (var unit in units)
        {
            string typeName = unit.GetType().Name;
            string teamColor = unit.team switch { "Player1" => "🔴", "Player2" => "🔵", "Player0" => "⚪", "Player" => "🟣", _ => "⚪" };
            string hpText = $"HP:{Mathf.CeilToInt(unit.health / 10f)}";

            var btn = CreateSelectionButton(
                $"{teamColor} [单位:{typeName}] {unit.Name} ({hpText})",
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
                $"{teamColor} [兵器:{typeName}] {weapon.Name} ({hpText})",
                new Color(0.7f, 0.4f, 0.3f),
                () => OpenWeaponEditor(weapon)
            );
            selectionButtonContainer.AddChild(btn);
            optionCount++;
        }

        var gridBtn = CreateSelectionButton(
            "🟫 修改格子地形/属性",
            new Color(0.4f, 0.7f, 0.4f),
            () => OpenTerrainMenu(grid)
        );
        selectionButtonContainer.AddChild(gridBtn);
        optionCount++;

        // ✅ 设施更改按钮
        var facilityBtn = CreateSelectionButton(
            "🏙️ 设施更改",
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

    // ========== ✅ 打开单位编辑器 ==========
    private void OpenUnitEditor(Infantry unit)
    {
        CloseSelectionMenu();
        currentEditingTarget = unit;

        propertyEditorTitle.Text = $"编辑单位: {unit.Name} ({unit.GetType().Name})";

        BuildPropertyEditor(unit);

        var viewportSize = GetViewport().GetVisibleRect().Size;
        propertyEditorPanel.Position = new Vector2(
            (viewportSize.X - propertyEditorPanel.Size.X) / 2,
            (viewportSize.Y - propertyEditorPanel.Size.Y) / 2
        );
        propertyEditorPanel.Visible = true;
        isMenuOpen = true;
    }

    private void BuildPropertyEditor(object target)
    {
        foreach (var child in propertyContainer.GetChildren())
            child.QueueFree();


        if (target is Grids grid)
        {
            BuildGridPropertyEditor(grid);
            return;
        }

        Type targetType = target.GetType();

        var fields = GetExportFields(targetType);
        var properties = GetExportProperties(targetType);

        var allMembers = new List<(string group, string name, Type type, Func<object> getter, Action<object> setter)>();

        foreach (var field in fields)
        {
            string group = GetExportGroupName(field);
            var f = field;
            allMembers.Add((group, f.Name, f.FieldType, 
                () => f.GetValue(target),
                (v) => f.SetValue(target, v)));
        }

        foreach (var prop in properties)
        {
            string group = GetExportGroupName(prop);
            var p = prop;
            allMembers.Add((group, p.Name, p.PropertyType,
                () => p.GetValue(target),
                (v) => p.SetValue(target, v)));
        }

        var grouped = allMembers
            .GroupBy(m => m.group)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            if (!string.IsNullOrEmpty(group.Key))
            {
                var groupLabel = new Label();
                groupLabel.Text = $"=== {group.Key} ===";
                groupLabel.AddThemeFontSizeOverride("font_size", 14);
                groupLabel.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.2f));
                groupLabel.CustomMinimumSize = new Vector2(0, 25);
                propertyContainer.AddChild(groupLabel);
            }

            foreach (var member in group)
            {
                CreateMemberControl(target, member.name, member.type, member.getter, member.setter);
            }
        }


        if (target is Infantry infantry)
        {
            AddUnitVisionFields(infantry);
        }
        else if (target is Weapon weapon)
        {
            AddWeaponVisionFields(weapon);
            if (weapon is DeathRay dr)
        {
            dr.direction = pendingCannonDirection;
            dr.UpdateDirectionVisual();
            dr.UpdateAmmoVisual();
            dr.UpdateMultiTileVisual();
        }
        if (weapon is LargeCannon lc)
            {
                AddLargeCannonFields(lc);
            }
        }
    }

    private List<FieldInfo> GetExportFields(Type type)
    {
        if (exportFieldsCache.ContainsKey(type))
            return exportFieldsCache[type];

        var result = new List<FieldInfo>();

        var currentType = type;
        while (currentType != null && currentType != typeof(object))
        {
            var fields = currentType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(f => f.GetCustomAttribute<ExportAttribute>() != null)
                .ToList();

            result.AddRange(fields);
            currentType = currentType.BaseType;
        }

        exportFieldsCache[type] = result;
        return result;
    }

    private List<PropertyInfo> GetExportProperties(Type type)
    {
        if (exportPropertiesCache.ContainsKey(type))
            return exportPropertiesCache[type];

        var result = new List<PropertyInfo>();

        var currentType = type;
        while (currentType != null && currentType != typeof(object))
        {
            var props = currentType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.GetCustomAttribute<ExportAttribute>() != null)
                .Where(p => p.CanRead && p.CanWrite)
                .ToList();

            result.AddRange(props);
            currentType = currentType.BaseType;
        }

        exportPropertiesCache[type] = result;
        return result;
    }


// 替换原有的 BuildGridPropertyEditor 方法
private void BuildGridPropertyEditor(Grids grid)
{
    // 基础属性
    var basicLabel = new Label();
    basicLabel.Text = "=== 基础属性 ===";
    basicLabel.AddThemeFontSizeOverride("font_size", 14);
    basicLabel.AddThemeColorOverride("font_color", Colors.Yellow);
    propertyContainer.AddChild(basicLabel);

    CreateMemberControl(grid, "gridType", typeof(GridType), 
        () => grid.gridType, (v) => { grid.gridType = (GridType)v; });

    if (grid.city != null)
    {
        var cityLabel = new Label();
        cityLabel.Text = "=== City 属性 ===";
        cityLabel.AddThemeColorOverride("font_color", Colors.Yellow);
        propertyContainer.AddChild(cityLabel);

        CreateMemberControl(grid.city, "facilityTeam", typeof(string),
            () => grid.city.facilityTeam, (v) => { grid.city.facilityTeam = (string)v; });
        CreateMemberControl(grid.city, "healAmount", typeof(int),
            () => grid.city.healAmount, (v) => { grid.city.healAmount = (int)v; });
        CreateMemberControl(grid.city, "flareAmmoSupply", typeof(int),
            () => grid.city.flareAmmoSupply, (v) => { grid.city.flareAmmoSupply = (int)v; });
        CreateMemberControl(grid.city, "explodeAmmoSupply", typeof(int),
            () => grid.city.explodeAmmoSupply, (v) => { grid.city.explodeAmmoSupply = (int)v; });
        CreateMemberControl(grid.city, "primaryAmmoSupply", typeof(int),
            () => grid.city.primaryAmmoSupply, (v) => { grid.city.primaryAmmoSupply = (int)v; });
        CreateMemberControl(grid.city, "fuelSupply", typeof(int),
            () => grid.city.fuelSupply, (v) => { grid.city.fuelSupply = (int)v; });
        CreateMemberControl(grid.city, "capturePointsRequired", typeof(int),
            () => grid.city.capturePointsRequired, (v) => { grid.city.capturePointsRequired = (int)v; });

        // ✅ City 每回合资金收入
        if (grid.city is City city)
        {
            CreateMemberControl(city, "fundsPerTurn", typeof(int),
                () => city.fundsPerTurn, (v) => { city.fundsPerTurn = (int)v; });
        }

        // ✅ 生产模块
        var prodLabel = new Label();
        prodLabel.Text = "=== 生产模块 ===";
        prodLabel.AddThemeFontSizeOverride("font_size", 14);
        prodLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));
        propertyContainer.AddChild(prodLabel);

        CreateMemberControl(grid.city, "canProduce", typeof(bool),
            () => grid.city.canProduce, (v) => { grid.city.canProduce = (bool)v; });
        CreateMemberControl(grid.city, "producibleUnitNames", typeof(Godot.Collections.Array<string>),
            () => grid.city.producibleUnitNames, (v) => { grid.city.producibleUnitNames = (Godot.Collections.Array<string>)v; });

        // ✅ 占领进度（只读显示）
        var captureLabel = new Label();
        captureLabel.Text = $"当前占领中单位数: {grid.city.capturingUnits.Count}";
        captureLabel.AddThemeFontSizeOverride("font_size", 12);
        captureLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.6f));
        propertyContainer.AddChild(captureLabel);
    }

    // ✅ 格子视野系统
    var visionLabel = new Label();
    visionLabel.Text = "=== 格子视野系统 ===";
    visionLabel.AddThemeFontSizeOverride("font_size", 14);
    visionLabel.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.2f));
    propertyContainer.AddChild(visionLabel);

    CreateMemberControl(grid, "gridVisionMode", typeof(GridVisionMode),
        () => grid.gridVisionMode, (v) => { grid.gridVisionMode = (GridVisionMode)v; });
    CreateMemberControl(grid, "gridVisionRange", typeof(int),
        () => grid.gridVisionRange, (v) => { grid.gridVisionRange = (int)v; });
    CreateMemberControl(grid, "towerDirection", typeof(TowerDirection),
        () => grid.towerDirection, (v) => { grid.towerDirection = (TowerDirection)v; });
    CreateMemberControl(grid, "towerCanRotate", typeof(bool),
        () => grid.towerCanRotate, (v) => { grid.towerCanRotate = (bool)v; });
    CreateMemberControl(grid, "towerRayAngle", typeof(float),
        () => grid.towerRayAngle, (v) => { grid.towerRayAngle = (float)v; });
    CreateMemberControl(grid, "gridVisionIgnoreTerrain", typeof(bool),
        () => grid.gridVisionIgnoreTerrain, (v) => { grid.gridVisionIgnoreTerrain = (bool)v; });
    CreateMemberControl(grid, "visionBonus", typeof(int),
        () => grid.visionBonus, (v) => { grid.visionBonus = (int)v; });
    
    // ✅ 关键：requiresAdjacentVision — 用户提到的"不紧邻不显示"
    CreateMemberControl(grid, "requiresAdjacentVision", typeof(bool),
        () => grid.requiresAdjacentVision, (v) => { grid.requiresAdjacentVision = (bool)v; });
    
    // ✅ 新增：瞭望塔标识
    CreateMemberControl(grid, "isWatchtower", typeof(bool),
        () => grid.isWatchtower, (v) => { grid.isWatchtower = (bool)v; });

    // 自定义伤害
    var damageLabel = new Label();
    damageLabel.Text = "=== 自定义伤害 ===";
    damageLabel.AddThemeFontSizeOverride("font_size", 14);
    damageLabel.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
    propertyContainer.AddChild(damageLabel);

    CreateMemberControl(grid, "canDestroyUnit", typeof(bool),
        () => grid.canDestroyUnit, (v) => { grid.canDestroyUnit = (bool)v; });
    CreateMemberControl(grid, "fixedDamagePerTurn", typeof(int),
        () => grid.fixedDamagePerTurn, (v) => { grid.fixedDamagePerTurn = (int)v; });
    CreateMemberControl(grid, "fixedAttackPerTurn", typeof(int),
        () => grid.fixedAttackPerTurn, (v) => { grid.fixedAttackPerTurn = (int)v; });
    CreateMemberControl(grid, "canOverMaxHealth", typeof(bool),
        () => grid.canOverMaxHealth, (v) => { grid.canOverMaxHealth = (bool)v; });

    // 弹药/燃料
    var ammoLabel = new Label();
    ammoLabel.Text = "=== 弹药/燃料 ===";
    ammoLabel.AddThemeFontSizeOverride("font_size", 14);
    ammoLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.7f, 1f));
    propertyContainer.AddChild(ammoLabel);

    CreateMemberControl(grid, "fixedAmmoChangePerTurn", typeof(int),
        () => grid.fixedAmmoChangePerTurn, (v) => { grid.fixedAmmoChangePerTurn = (int)v; });
    CreateMemberControl(grid, "ammoCanOverMax", typeof(bool),
        () => grid.ammoCanOverMax, (v) => { grid.ammoCanOverMax = (bool)v; });
    CreateMemberControl(grid, "ammoCanReachZero", typeof(bool),
        () => grid.ammoCanReachZero, (v) => { grid.ammoCanReachZero = (bool)v; });
    CreateMemberControl(grid, "fixedFuelChangePerTurn", typeof(int),
        () => grid.fixedFuelChangePerTurn, (v) => { grid.fixedFuelChangePerTurn = (int)v; });
    CreateMemberControl(grid, "fuelCanOverMax", typeof(bool),
        () => grid.fuelCanOverMax, (v) => { grid.fuelCanOverMax = (bool)v; });
    CreateMemberControl(grid, "fuelCanReachZero", typeof(bool),
        () => grid.fuelCanReachZero, (v) => { grid.fuelCanReachZero = (bool)v; });
    
    // ✅ 新增：格子上的单位/兵器列表（只读）
    var unitsLabel = new Label();
    unitsLabel.Text = "=== 当前格子内容 ===";
    unitsLabel.AddThemeFontSizeOverride("font_size", 14);
    unitsLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.8f, 1f));
    propertyContainer.AddChild(unitsLabel);
    
    var unitCountLabel = new Label();
    unitCountLabel.Text = $"单位: {grid.infantries.Count}个 | 兵器: {grid.weapons.Count}个";
    unitCountLabel.AddThemeFontSizeOverride("font_size", 11);
    unitCountLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
    propertyContainer.AddChild(unitCountLabel);
}
    private void AddUnitVisionFields(Infantry unit)
    {
        var visionLabel = new Label();
        visionLabel.Text = "=== 战争迷雾视野 ===";
        visionLabel.AddThemeFontSizeOverride("font_size", 14);
        visionLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));
        propertyContainer.AddChild(visionLabel);

        CreateMemberControl(unit, "visionRange", typeof(int),
            () => unit.visionRange, (v) => { unit.visionRange = (int)v; });
        CreateMemberControl(unit, "useConfigVision", typeof(bool),
            () => unit.useConfigVision, (v) => { unit.useConfigVision = (bool)v; });
        CreateMemberControl(unit, "overrideGlobalTerrainBonus", typeof(bool),
            () => unit.overrideGlobalTerrainBonus, (v) => { unit.overrideGlobalTerrainBonus = (bool)v; });

        // ✅ 地形视野加成矩阵编辑器
        if (unit.overrideGlobalTerrainBonus)
        {
            var matrixLabel = new Label();
            matrixLabel.Text = "--- 单位专属地形视野加成 ---";
            matrixLabel.AddThemeFontSizeOverride("font_size", 12);
            matrixLabel.AddThemeColorOverride("font_color", Colors.Yellow);
            propertyContainer.AddChild(matrixLabel);

            var matrixEditor = CreateTerrainBonusMatrixEditor(
                unit.unitTerrainVisionBonus,
                (newDict) => { unit.unitTerrainVisionBonus = newDict; }
            );
            propertyContainer.AddChild(matrixEditor);
        }
    }

    // ✅ 新增：兵器视野字段
    private void AddWeaponVisionFields(Weapon weapon)
    {
        var visionLabel = new Label();
        visionLabel.Text = "=== 战争迷雾视野 ===";
        visionLabel.AddThemeFontSizeOverride("font_size", 14);
        visionLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));
        propertyContainer.AddChild(visionLabel);

        CreateMemberControl(weapon, "visionRange", typeof(int),
            () => weapon.visionRange, (v) => { weapon.visionRange = (int)v; });
        CreateMemberControl(weapon, "useConfigVision", typeof(bool),
            () => weapon.useConfigVision, (v) => { weapon.useConfigVision = (bool)v; });
        CreateMemberControl(weapon, "visionMode", typeof(VisionMode),
            () => weapon.visionMode, (v) => { weapon.visionMode = (VisionMode)v; });
        CreateMemberControl(weapon, "overrideGlobalTerrainBonus", typeof(bool),
            () => weapon.overrideGlobalTerrainBonus, (v) => { weapon.overrideGlobalTerrainBonus = (bool)v; });

        if (weapon.overrideGlobalTerrainBonus)
        {
            var matrixLabel = new Label();
            matrixLabel.Text = "--- 兵器专属地形视野加成 ---";
            matrixLabel.AddThemeFontSizeOverride("font_size", 12);
            matrixLabel.AddThemeColorOverride("font_color", Colors.Yellow);
            propertyContainer.AddChild(matrixLabel);

            var matrixEditor = CreateTerrainBonusMatrixEditor(
                weapon.weaponTerrainVisionBonus,
                (newDict) => { weapon.weaponTerrainVisionBonus = newDict; }
            );
            propertyContainer.AddChild(matrixEditor);
        }
    }

    // ✅ 新增：大型黑炮专属字段（方向旋转按钮、旋转开关、胜利判定）
    private void AddLargeCannonFields(LargeCannon lc)
    {
        var sectionLabel = new Label();
        sectionLabel.Text = "=== 大型黑炮专属 ===";
        sectionLabel.AddThemeFontSizeOverride("font_size", 14);
        sectionLabel.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.2f));
        sectionLabel.CustomMinimumSize = new Vector2(0, 28);
        propertyContainer.AddChild(sectionLabel);

        // 方向旋转按钮
        var dirLabel = new Label();
        dirLabel.Text = "🧭 方向:";
        dirLabel.AddThemeFontSizeOverride("font_size", 12);
        dirLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.9f));
        propertyContainer.AddChild(dirLabel);

        var dirHBox = new HBoxContainer();
        dirHBox.AddThemeConstantOverride("separation", 6);
        dirHBox.CustomMinimumSize = new Vector2(0, 36);

        string[] dirNames = { "⬆️上", "➡️右", "⬇️下", "⬅️左" };
        var dirValues = new[] { BlackCannon.CannonDirection.Up, BlackCannon.CannonDirection.Right, BlackCannon.CannonDirection.Down, BlackCannon.CannonDirection.Left };

        for (int i = 0; i < 4; i++)
        {
            var dirBtn = new Button();
            dirBtn.Text = dirNames[i];
            dirBtn.CustomMinimumSize = new Vector2(50, 32);
            dirBtn.AddThemeFontSizeOverride("font_size", 11);
            var capturedDir = dirValues[i];
            dirBtn.Pressed += () => {
                lc.direction = capturedDir;
                lc.UpdateDirectionVisual();
                OnPropertyChanged(lc, "direction");
                // 刷新只读信息
                RefreshLargeCannonInfo(lc);
            };
            dirHBox.AddChild(dirBtn);
        }
        propertyContainer.AddChild(dirHBox);

        // 可旋转开关
        var rotateHBox = new HBoxContainer();
        rotateHBox.AddThemeConstantOverride("separation", 8);
        rotateHBox.CustomMinimumSize = new Vector2(0, 32);
        var rotateLabel = new Label();
        rotateLabel.Text = "🔁 允许旋转:";
        rotateLabel.CustomMinimumSize = new Vector2(120, 28);
        rotateLabel.AddThemeFontSizeOverride("font_size", 12);
        rotateLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.9f));
        rotateHBox.AddChild(rotateLabel);
        var rotateCheck = new CheckBox();
        rotateCheck.ButtonPressed = lc.canRotate;
        rotateCheck.Toggled += (pressed) => {
            lc.canRotate = pressed;
        };
        rotateHBox.AddChild(rotateCheck);
        propertyContainer.AddChild(rotateHBox);

        // 介入胜利判定
        var victoryHBox = new HBoxContainer();
        victoryHBox.AddThemeConstantOverride("separation", 8);
        victoryHBox.CustomMinimumSize = new Vector2(0, 32);
        var victoryLabel = new Label();
        victoryLabel.Text = "🏆 介入胜利判定:";
        victoryLabel.CustomMinimumSize = new Vector2(120, 28);
        victoryLabel.AddThemeFontSizeOverride("font_size", 12);
        victoryLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.9f));
        victoryHBox.AddChild(victoryLabel);
        var victoryCheck = new CheckBox();
        victoryCheck.ButtonPressed = lc.contributesToVictory;
        victoryCheck.Toggled += (pressed) => {
            lc.contributesToVictory = pressed;
        };
        victoryHBox.AddChild(victoryCheck);
        propertyContainer.AddChild(victoryHBox);

        // 需要摧毁数量
        var countHBox = new HBoxContainer();
        countHBox.AddThemeConstantOverride("separation", 8);
        countHBox.CustomMinimumSize = new Vector2(0, 32);
        var countLabel = new Label();
        countLabel.Text = "📊 需摧毁数量:";
        countLabel.CustomMinimumSize = new Vector2(120, 28);
        countLabel.AddThemeFontSizeOverride("font_size", 12);
        countLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.9f));
        countHBox.AddChild(countLabel);
        var countSpin = new SpinBox();
        countSpin.MinValue = 1;
        countSpin.MaxValue = 99;
        countSpin.Value = lc.victoryCountRequired;
        countSpin.CustomMinimumSize = new Vector2(80, 28);
        countSpin.AddThemeFontSizeOverride("font_size", 12);
        countSpin.ValueChanged += (newVal) => {
            lc.victoryCountRequired = (int)newVal;
        };
        countHBox.AddChild(countSpin);
        propertyContainer.AddChild(countHBox);

        // 炮口和弱点信息（只读）
        var infoLabel = new Label();
        infoLabel.Name = "LargeCannonInfoLabel";
        infoLabel.Text = $"炮口: {lc.GetFirePointOffsetForDirection(lc.direction)} | 弱点: {lc.GetWeakPointOffsetForDirection(lc.direction)}";
        infoLabel.AddThemeFontSizeOverride("font_size", 11);
        infoLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        propertyContainer.AddChild(infoLabel);
    }

    private void RefreshLargeCannonInfo(LargeCannon lc)
    {
        foreach (var child in propertyContainer.GetChildren())
        {
            if (child is Label label && label.Name == "LargeCannonInfoLabel")
            {
                label.Text = $"炮口: {lc.GetFirePointOffsetForDirection(lc.direction)} | 弱点: {lc.GetWeakPointOffsetForDirection(lc.direction)}";
                break;
            }
        }
    }

    // ✅ 新增：地形视野加成矩阵编辑器
    private Control CreateTerrainBonusMatrixEditor(Godot.Collections.Dictionary<GridType, int> dict, Action<Godot.Collections.Dictionary<GridType, int>> onChanged)
    {
        var mainVbox = new VBoxContainer();
        mainVbox.AddThemeConstantOverride("separation", 4);

        var hintLabel = new Label();
        hintLabel.Text = "每个地形的视野加成（可正可负）";
        hintLabel.AddThemeFontSizeOverride("font_size", 10);
        hintLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.6f));
        mainVbox.AddChild(hintLabel);

        var scroll = new ScrollContainer();
        scroll.CustomMinimumSize = new Vector2(340, 200);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        var container = new VBoxContainer();
        container.AddThemeConstantOverride("separation", 2);

        var terrainTypes = Enum.GetValues(typeof(GridType));

        foreach (GridType terrain in terrainTypes)
        {
            var hbox = new HBoxContainer();
            hbox.AddThemeConstantOverride("separation", 8);
            hbox.CustomMinimumSize = new Vector2(0, 28);

            var nameLabel = new Label();
            nameLabel.Text = terrain.ToString();
            nameLabel.CustomMinimumSize = new Vector2(120, 24);
            nameLabel.AddThemeFontSizeOverride("font_size", 11);
            hbox.AddChild(nameLabel);

            int currentValue = dict.GetValueOrDefault(terrain, 0);

            var spinBox = new SpinBox();
            spinBox.MinValue = -10;
            spinBox.MaxValue = 10;
            spinBox.Value = currentValue;
            spinBox.CustomMinimumSize = new Vector2(70, 26);
            spinBox.AddThemeFontSizeOverride("font_size", 11);

            GridType capturedTerrain = terrain;
            spinBox.ValueChanged += (newVal) => {
                dict[capturedTerrain] = (int)newVal;
                onChanged(dict);
            };

            hbox.AddChild(spinBox);

            var signLabel = new Label();
            string sign = currentValue > 0 ? "+" : "";
            signLabel.Text = $"({sign}{currentValue})";
            signLabel.AddThemeFontSizeOverride("font_size", 10);
            signLabel.AddThemeColorOverride("font_color",
                currentValue > 0 ? new Color(0.3f, 1, 0.3f) :
                currentValue < 0 ? new Color(1, 0.3f, 0.3f) : new Color(0.7f, 0.7f, 0.7f));
            hbox.AddChild(signLabel);

            container.AddChild(hbox);
        }

        scroll.AddChild(container);
        mainVbox.AddChild(scroll);

        return mainVbox;
    }

private void OnPropertyChanged(object target, string propertyName)
{

    if (target is Grids grid)
    {

        if (propertyName == "gridType")
        {
            UpdateGridVisual(grid);

            if (terrainMenuPanel.Visible)
            {
                terrainTitleLabel.Text = $"选择地形类型 (当前: {GetTerrainDisplayName(grid.gridType)})";
            }
        }
        else if (propertyName.Contains("Vision") || propertyName == "requiresAdjacentVision" || propertyName == "isWatchtower")
        {

            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.fogOfWarManager?.ForceRefresh();
        }
        return;
    }

    if (propertyName == "team" && target is Node2D node)
    {
        RefreshTeamVisual(target);
    }
    else if (propertyName == "facilityTeam" && target is Facility facility)
    {
        facility.UpdateCityVisual();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.UpdateUnitLists();
    }
    else if (propertyName.Contains("health") || propertyName.Contains("Health"))
        {
            if (target is Infantry infantry) infantry.UpdateHpLabel();
            if (target is Weapon weapon) weapon.UpdateHpLabel();
        }
        else if (propertyName.Contains("Ammo") || propertyName.Contains("ammo"))
        {
            if (target is BlackCannon cannon) cannon.UpdateAmmoVisual();
        }
        else if (propertyName == "direction" && target is BlackCannon bc)
        {
            bc.UpdateDirectionVisual();
        }
    }

    private void RefreshTeamVisual(object target)
    {
        if (target is Infantry infantry)
        {
            infantry.UpdateHpLabel();

            if (infantry is Mech mech && mech.animSprite != null)
            {
                string animName = (mech.team == "Player2") ? "mech2" : "mech1";
                if (mech.animSprite.SpriteFrames.HasAnimation(animName))
                {
                    mech.animSprite.Play(animName);
                    mech.animSprite.Modulate = mech.normal;
                }
            }
            else if (infantry is LightTank tank && tank.animSprite != null)
            {
                string animName = (tank.team == "Player2") ? "lighttank2" : "lighttank1";
                if (tank.animSprite.SpriteFrames.HasAnimation(animName))
                {
                    tank.ShowAnimState(animName);
                    tank.animSprite.Modulate = tank.normal;
                }
            }
            else if (infantry is Artillery artillery && artillery.animSprite != null)
            {
                string animName = (artillery.team == "Player2") ? "artillery2" : "artillery1";
                if (artillery.animSprite.SpriteFrames.HasAnimation(animName))
                {
                    artillery.ShowAnimState(animName);
                    artillery.animSprite.Modulate = artillery.normal;
                }
            }
            else if (infantry is APC apc && apc.animSprite != null)
            {
                string animName = (apc.team == "Player2") ? "apc2" : "apc1";
                if (apc.animSprite.SpriteFrames.HasAnimation(animName))
                {
                    apc.ShowAnimState(animName);
                    apc.animSprite.Modulate = apc.normal;
                }
            }
            else if (infantry is Oozium oozium && oozium.animSprite != null)
            {
                string animName = (oozium.team == "Player2") ? "oozium2" : "oozium1";
                if (oozium.animSprite.SpriteFrames.HasAnimation(animName))
                {
                    oozium.animSprite.Play(animName);
                    oozium.animSprite.Modulate = oozium.normal;
                }
            }
            else if (infantry is MdTank mdTank && mdTank.MdSprite != null)
            {
                string animName = (mdTank.team == "Player2") ? "Md2" : "Md1";
                if (mdTank.MdSprite.SpriteFrames.HasAnimation(animName))
                {
                    mdTank.MdSprite.Play(animName);
                    mdTank.MdSprite.Modulate = mdTank.normal;
                }
            }
            else
            {
                infantry.UpdateTeamVisual();
            }

            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.UpdateUnitLists();
        }

        if (target is Weapon weapon && weapon.animSprite != null)
        {
            string teamPrefix = (weapon.team == "Player2") ? "cannon2" : "cannon1";

            if (weapon is BlackCannon cannon)
            {
                string dirSuffix = cannon.direction switch
                {
                    BlackCannon.CannonDirection.Up => "up",
                    BlackCannon.CannonDirection.Down => "down",
                    BlackCannon.CannonDirection.Left => "left",
                    BlackCannon.CannonDirection.Right => "right",
                    _ => "up"
                };
                string animName = $"{teamPrefix}_{dirSuffix}";
                if (weapon.animSprite.SpriteFrames.HasAnimation(animName))
                {
                    weapon.animSprite.Play(animName);
                    weapon.animSprite.Modulate = TeamHelper.GetTeamColor(weapon.team);
                }
            }
            else
            {
                if (weapon.animSprite.SpriteFrames.HasAnimation(teamPrefix))
                {
                    weapon.animSprite.Play(teamPrefix);
                    weapon.animSprite.Modulate = TeamHelper.GetTeamColor(weapon.team);
                }
            }

            weapon.UpdateHpLabel();
        }
    }



private void OnSaveProperties()
{
    if (currentEditingTarget == null) return;


    if (currentEditingTarget is Infantry unit)
    {
        unit.UpdateHpLabel();
        if (unit.sprite != null) unit.sprite.Modulate = unit.normal;

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.UpdateUnitLists();
    }

    if (currentEditingTarget is Weapon weapon)
    {
        weapon.UpdateHpLabel();
        if (weapon is BlackCannon cannon)
        {
            cannon.UpdateAmmoVisual();
        }
    }
    
    // ✅ 新增：处理格子保存
    if (currentEditingTarget is Grids grid)
    {
        UpdateGridVisual(grid);
        // 刷新战争迷雾（因为视野属性可能变了）
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.fogOfWarManager?.ForceRefresh();
    }

    ClosePropertyEditor();
}

    private string GetExportGroupName(MemberInfo member)
    {
        var groupAttr = member.GetCustomAttribute<ExportGroupAttribute>();
        if (groupAttr != null) return groupAttr.Name;

        var declaringType = member.DeclaringType;
        if (declaringType != null)
        {
            var members = declaringType.GetMembers(BindingFlags.Public | BindingFlags.Instance);
            string currentGroup = "";
            foreach (var m in members)
            {
                var group = m.GetCustomAttribute<ExportGroupAttribute>();
                if (group != null) currentGroup = group.Name;
                if (m == member) return currentGroup;
            }
        }

        return "";
    }

    private void CloseSelectionMenu()
    {
        selectionMenuPanel.Visible = false;
    }

// 替换原有的 ClosePropertyEditor
private void ClosePropertyEditor()
{
    propertyEditorPanel.Visible = false;
    
    if (currentEditingTarget is Grids)
    {
        terrainMenuPanel.Visible = false;
        selectedGrid = null;
    }
    
    currentEditingTarget = null;
    isMenuOpen = false;
}
private void OpenTerrainMenu(Grids grid)
{
    CloseSelectionMenu();
    selectedGrid = grid;
    

    terrainTitleLabel.Text = $"选择地形类型 (当前: {GetTerrainDisplayName(grid.gridType)})";
    LoadCustomDamageValues(grid);
    LoadAmmoFuelValues(grid);
    terrainMenuPanel.Visible = true;
    
    currentEditingTarget = grid;
    propertyEditorTitle.Text = $"编辑格子: {grid.GridIndex} ({grid.gridType})";
    BuildPropertyEditor(grid);  
    propertyEditorPanel.Visible = true;
}

// ✅ 设施更改菜单：生成/摧毁/编辑设施
private void OpenFacilityMenu(Grids grid)
{
    CloseSelectionMenu();
    selectedGrid = grid;
    currentEditingTarget = grid.city;
    
    propertyEditorTitle.Text = $"设施更改 - 格子 {grid.GridIndex}";
    
    // 清空属性容器
    foreach (var child in propertyContainer.GetChildren())
        child.QueueFree();
    
    if (grid.city == null)
    {
        // 没有设施：显示生成按钮
        var noFacilityLabel = new Label();
        noFacilityLabel.Text = "当前格子没有设施";
        noFacilityLabel.AddThemeFontSizeOverride("font_size", 14);
        noFacilityLabel.AddThemeColorOverride("font_color", Colors.Gray);
        propertyContainer.AddChild(noFacilityLabel);
        
        var spawnBtn = new Button();
        spawnBtn.Text = "🏙️ 生成 City 设施";
        spawnBtn.CustomMinimumSize = new Vector2(200, 40);
        spawnBtn.Pressed += () => SpawnFacilityAtGrid(grid);
        propertyContainer.AddChild(spawnBtn);
    }
    else
    {
        // 有设施：显示编辑 + 摧毁按钮
        var facilityLabel = new Label();
        facilityLabel.Text = $"当前设施: {grid.city.GetType().Name}";
        facilityLabel.AddThemeFontSizeOverride("font_size", 14);
        facilityLabel.AddThemeColorOverride("font_color", Colors.Yellow);
        propertyContainer.AddChild(facilityLabel);
        
        // 编辑设施属性
        BuildPropertyEditor(grid.city);
        
        // 摧毁按钮
        var destroyBtn = new Button();
        destroyBtn.Text = "💥 摧毁设施";
        destroyBtn.CustomMinimumSize = new Vector2(200, 40);
        destroyBtn.AddThemeColorOverride("font_color", Colors.Red);
        destroyBtn.Pressed += () => DestroyFacilityAtGrid(grid);
        propertyContainer.AddChild(destroyBtn);
    }
    
    propertyEditorPanel.Visible = true;
}

// ✅ 在格子生成 City 设施（OpenFacilityMenu 用，默认 Player0）
private void SpawnFacilityAtGrid(Grids grid)
{
    SpawnFacilityAtGrid(grid, "res://Prefabs/City.tscn", "Player0");
    OpenFacilityMenu(grid);
}

// ✅ 在格子生成设施（spawnMenu 用，支持模板参数）
private void SpawnFacilityAtGrid(Grids grid, string scenePath, string team)
{
    if (grid.city != null)
    {
        spawnHintLabel.Text = "❌ 该格子已有设施";
        spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
        return;
    }
    
    if (string.IsNullOrEmpty(scenePath))
    {
        spawnHintLabel.Text = "❌ 场景路径为空";
        spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
        return;
    }
    
    var scene = GD.Load<PackedScene>(scenePath);
    if (scene == null)
    {
        GD.PushError($"[TerrainEditor] 无法加载场景: {scenePath}");
        spawnHintLabel.Text = $"❌ 无法加载场景: {scenePath}";
        spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
        return;
    }
    
    var facility = scene.Instantiate<Facility>();
    if (facility == null)
    {
        spawnHintLabel.Text = "❌ 设施实例化失败";
        spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
        return;
    }
    
    facility.Name = "Facility";
    grid.AddChild(facility);
    grid.city = facility;
    
    // 设置势力
    facility.facilityTeam = team;
    facility.healAmount = 20;facility.flareAmmoSupply = 999;facility.explodeAmmoSupply = 999;facility.primaryAmmoSupply = 999;facility.fuelSupply = 999;
    facility.capturePointsRequired = 20;
    
    if (facility is City city)
    {
        city.UpdateCityVisual();
    }
    else
    {
        facility.UpdateCityVisual();
    }
    
    
    spawnHintLabel.Text = $"✅ 生成设施: {facility.GetType().Name} ({team})";
    spawnHintLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));
    
    gameManager?.UpdateUnitLists();
    gameManager?.RefreshSpecializedUnitLists();
}

// ✅ 摧毁格子上的设施
private void DestroyFacilityAtGrid(Grids grid)
{
    if (grid.city == null) return;
    
    grid.city.QueueFree();
    grid.city = null;
    
    
    // 刷新菜单
    OpenFacilityMenu(grid);
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
    private void CreateCustomDamageUI()
    {
        customDamageContainer = new VBoxContainer();
        customDamageContainer.Position = new Vector2(10, 290);
        customDamageContainer.Size = new Vector2(260, 160);
        customDamageContainer.AddThemeConstantOverride("separation", 5);

        var sectionTitle = new Label();
        sectionTitle.Text = "=== 格子自定义伤害 ===";
        sectionTitle.AddThemeFontSizeOverride("font_size", 13);
        sectionTitle.AddThemeColorOverride("font_color", Colors.Yellow);
        customDamageContainer.AddChild(sectionTitle);

        var destroyHBox = new HBoxContainer();
        var destroyLabel = new Label { Text = "可摧毁单位:", CustomMinimumSize = new Vector2(100, 22) };
        destroyLabel.AddThemeFontSizeOverride("font_size", 11);
        canDestroyCheckBox = new CheckBox { ButtonPressed = true };
        destroyHBox.AddChild(destroyLabel);
        destroyHBox.AddChild(canDestroyCheckBox);
        customDamageContainer.AddChild(destroyHBox);

        var damageHBox = new HBoxContainer();
        var damageLabel = new Label { Text = "固定伤害/回合:", CustomMinimumSize = new Vector2(100, 22) };
        damageLabel.AddThemeFontSizeOverride("font_size", 11);
        fixedDamageSpinBox = new SpinBox { MinValue = -999, MaxValue = 999, Value = 0, CustomMinimumSize = new Vector2(80, 22) };
        fixedDamageSpinBox.AddThemeFontSizeOverride("font_size", 11);
        damageHBox.AddChild(damageLabel);
        damageHBox.AddChild(fixedDamageSpinBox);
        customDamageContainer.AddChild(damageHBox);

        var attackHBox = new HBoxContainer();
        var attackLabel = new Label { Text = "固定攻击/回合:", CustomMinimumSize = new Vector2(100, 22) };
        attackLabel.AddThemeFontSizeOverride("font_size", 11);
        fixedAttackSpinBox = new SpinBox { MinValue = -999, MaxValue = 999, Value = 0, CustomMinimumSize = new Vector2(80, 22) };
        fixedAttackSpinBox.AddThemeFontSizeOverride("font_size", 11);
        attackHBox.AddChild(attackLabel);
        attackHBox.AddChild(fixedAttackSpinBox);
        customDamageContainer.AddChild(attackHBox);

        var overMaxHBox = new HBoxContainer();
        var overMaxLabel = new Label { Text = "可超最大血量:", CustomMinimumSize = new Vector2(100, 22) };
        overMaxLabel.AddThemeFontSizeOverride("font_size", 11);
        canOverMaxCheckBox = new CheckBox { ButtonPressed = false };
        overMaxHBox.AddChild(overMaxLabel);
        overMaxHBox.AddChild(canOverMaxCheckBox);
        customDamageContainer.AddChild(overMaxHBox);

        terrainMenuPanel.AddChild(customDamageContainer);
    }

    private void CreateAmmoFuelUI()
    {
        ammoFuelContainer = new VBoxContainer();
        ammoFuelContainer.Position = new Vector2(10, 455);
        ammoFuelContainer.Size = new Vector2(260, 220);
        ammoFuelContainer.AddThemeConstantOverride("separation", 5);

        var sectionTitle = new Label();
        sectionTitle.Text = "=== 弹药/燃料系统 ===";
        sectionTitle.AddThemeFontSizeOverride("font_size", 13);
        sectionTitle.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.2f));
        ammoFuelContainer.AddChild(sectionTitle);

        var ammoTitle = new Label();
        ammoTitle.Text = "【弹药】";
        ammoTitle.AddThemeFontSizeOverride("font_size", 12);
        ammoTitle.AddThemeColorOverride("font_color", new Color(0.9f, 0.7f, 0.2f));
        ammoFuelContainer.AddChild(ammoTitle);

        var ammoHBox = new HBoxContainer();
        var ammoLabel = new Label { Text = "弹药变化/回合:", CustomMinimumSize = new Vector2(100, 22) };
        ammoLabel.AddThemeFontSizeOverride("font_size", 11);
        fixedAmmoSpinBox = new SpinBox { MinValue = -999, MaxValue = 999, Value = 0, CustomMinimumSize = new Vector2(80, 22) };
        fixedAmmoSpinBox.AddThemeFontSizeOverride("font_size", 11);
        ammoHBox.AddChild(ammoLabel);
        ammoHBox.AddChild(fixedAmmoSpinBox);
        ammoFuelContainer.AddChild(ammoHBox);

        var ammoOverHBox = new HBoxContainer();
        var ammoOverLabel = new Label { Text = "可超弹药上限:", CustomMinimumSize = new Vector2(100, 22) };
        ammoOverLabel.AddThemeFontSizeOverride("font_size", 11);
        ammoCanOverMaxCheckBox = new CheckBox { ButtonPressed = false };
        ammoOverHBox.AddChild(ammoOverLabel);
        ammoOverHBox.AddChild(ammoCanOverMaxCheckBox);
        ammoFuelContainer.AddChild(ammoOverHBox);

        var ammoZeroHBox = new HBoxContainer();
        var ammoZeroLabel = new Label { Text = "可归零:", CustomMinimumSize = new Vector2(100, 22) };
        ammoZeroLabel.AddThemeFontSizeOverride("font_size", 11);
        ammoCanReachZeroCheckBox = new CheckBox { ButtonPressed = true };
        ammoZeroHBox.AddChild(ammoZeroLabel);
        ammoZeroHBox.AddChild(ammoCanReachZeroCheckBox);
        ammoFuelContainer.AddChild(ammoZeroHBox);

        var separator = new HSeparator();
        separator.CustomMinimumSize = new Vector2(0, 4);
        ammoFuelContainer.AddChild(separator);

        var fuelTitle = new Label();
        fuelTitle.Text = "【燃料】";
        fuelTitle.AddThemeFontSizeOverride("font_size", 12);
        fuelTitle.AddThemeColorOverride("font_color", new Color(0.2f, 0.7f, 0.9f));
        ammoFuelContainer.AddChild(fuelTitle);

        var fuelHBox = new HBoxContainer();
        var fuelLabel = new Label { Text = "燃料变化/回合:", CustomMinimumSize = new Vector2(100, 22) };
        fuelLabel.AddThemeFontSizeOverride("font_size", 11);
        fixedFuelSpinBox = new SpinBox { MinValue = -999, MaxValue = 999, Value = 0, CustomMinimumSize = new Vector2(80, 22) };
        fixedFuelSpinBox.AddThemeFontSizeOverride("font_size", 11);
        fuelHBox.AddChild(fuelLabel);
        fuelHBox.AddChild(fixedFuelSpinBox);
        ammoFuelContainer.AddChild(fuelHBox);

        var fuelOverHBox = new HBoxContainer();
        var fuelOverLabel = new Label { Text = "可超燃料上限:", CustomMinimumSize = new Vector2(100, 22) };
        fuelOverLabel.AddThemeFontSizeOverride("font_size", 11);
        fuelCanOverMaxCheckBox = new CheckBox { ButtonPressed = false };
        fuelOverHBox.AddChild(fuelOverLabel);
        fuelOverHBox.AddChild(fuelCanOverMaxCheckBox);
        ammoFuelContainer.AddChild(fuelOverHBox);

        var fuelZeroHBox = new HBoxContainer();
        var fuelZeroLabel = new Label { Text = "可归零:", CustomMinimumSize = new Vector2(100, 22) };
        fuelZeroLabel.AddThemeFontSizeOverride("font_size", 11);
        fuelCanReachZeroCheckBox = new CheckBox { ButtonPressed = true };
        fuelZeroHBox.AddChild(fuelZeroLabel);
        fuelZeroHBox.AddChild(fuelCanReachZeroCheckBox);
        ammoFuelContainer.AddChild(fuelZeroHBox);

        terrainMenuPanel.AddChild(ammoFuelContainer);
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
            GridType.GROUND => "🟫 平原",
            GridType.FOREST => "🌲 森林",
            GridType.ROAD => "🛣️ 公路",
            GridType.SEA => "🌊 海洋",
            GridType.RIVER => "🏞️ 河流",
            GridType.HILL => "⛰️ 山地",
            GridType.METEORITE => "☄️ 陨石(兵器设施)",
            GridType.PIPE => "🔧 管道",
            GridType.LAVA => "🌋 岩浆",
            GridType.BEACH => "🏖️ 沙滩",
            GridType.TP => "✨ 传送点",
            GridType.REEF => "🪸 珊瑚礁",
            GridType.WHIRLPOOL => "🌀 漩涡",
            GridType.LAVASIDE => "🔥 岩浆滩",
            GridType.SEAFOG => "🌫️ 海上迷雾",
            GridType.LANDFOG => "🌫️ 陆上迷雾",
            GridType.WATERFALL => "💧 瀑布",
            GridType.CLIFF => "🪨 悬崖",
            GridType.SLOPE => "📐 斜坡",
            GridType.CAVE => "🕳️ 塌方穴",
            GridType.HOLE => "🕳️ 人工洞",
            GridType.PIPESEAM => "⚙️ 管道接缝(兵器设施)",
            GridType.TRACK => "🛤️ 铁轨",
            GridType.STATION => "🚉 站台",
            GridType.BRIDGE => "🌉 桥梁",
            GridType.LAVABRIDGE => "🔥🌉 岩浆桥梁",
            GridType.PASSABLEPIPE => "🔧空心管道",
            GridType.SHIPGATE => "⚓ 船闸",
            GridType.OVERPASS => "🌉 立交桥",
            GridType.BROKENPIPE => "💥 破碎管道",
            GridType.RUINS => "🏚️ 废墟",
            GridType.BROKENTRACK => "💥🛤️ 破碎铁路",
            GridType.LAVAFOG => "🌋🌫️ 岩浆迷雾",
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
            _ => 0.0f
        };
        return bonus > 0 ? $"[防御{bonus * 100:F0}%]" : "[无防御]";
    }

    private void OnModeTogglePressed() => SetEditMode(!IsEditMode);


public void SetEditMode(bool editMode)
{
    if (IsEditMode == editMode) return;
    
    IsEditMode = editMode;

    if (IsEditMode)
    {
        modeToggleButton.Text = "🎮 游玩模式";
        var style = new StyleBoxFlat { BgColor = new Color(0.5f, 0.3f, 0.3f, 0.9f) };
        style.SetCornerRadiusAll(8);
        modeToggleButton.AddThemeStyleboxOverride("normal", style);
        var hoverStyle = new StyleBoxFlat { BgColor = new Color(0.6f, 0.4f, 0.4f, 0.95f) };
        hoverStyle.SetCornerRadiusAll(8);
        modeToggleButton.AddThemeStyleboxOverride("hover", hoverStyle);

        gameManager?.gridManager?.CloseRange();
        gameManager?.gridManager?.HideAttackRange();
        gameManager?.gridManager?.ClearWeaponRange();
        var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
        actionMenu?.Hide();
        gameManager?.ClearSelectedInfantry();

        // ✅ 关键修复：记录当前迷雾状态（从 FogOfWarManager 直接读取）
        var fowManager = GetTree()?.GetFirstNodeInGroup("fog_of_war_manager") as FogOfWarManager;
        
        // ✅ 确保 FogOfWarManager 已初始化
        if (fowManager != null && !fowManager.isInitialized)
        {
            // 强制初始化
            fowManager.CallDeferred(nameof(FogOfWarManager.DeferredInit));
        }
        
        fogStateBeforeEdit = fowManager?.isFogOfWarEnabled ?? true;

        // ✅ 关闭迷雾
        gameManager?.SetFogOfWarEnabled(false);
        
        // ✅ 显示批量地形按钮
        if (batchTerrainBtn != null)
            batchTerrainBtn.Visible = true;
        
        // ✅ 同步全局配置面板中的复选框
        if (fogOfWarCheckBox != null)
        {
            fogOfWarCheckBox.ButtonPressed = false;
            fogOfWarCheckBox.Disabled = true;  // 编辑模式下禁用
        }
    }
    else
    {
        modeToggleButton.Text = "🛠️ 编辑模式";
        var style = new StyleBoxFlat { BgColor = new Color(0.3f, 0.5f, 0.3f, 0.9f) };
        style.SetCornerRadiusAll(8);
        modeToggleButton.AddThemeStyleboxOverride("normal", style);
        var hoverStyle = new StyleBoxFlat { BgColor = new Color(0.4f, 0.6f, 0.4f, 0.95f) };
        hoverStyle.SetCornerRadiusAll(8);
        modeToggleButton.AddThemeStyleboxOverride("hover", hoverStyle);

        CloseAllMenus();

        // ✅ 隐藏批量地形按钮，退出批量模式
        if (batchTerrainBtn != null)
            batchTerrainBtn.Visible = false;
        StopBatchTerrainMode();

        
        // ✅ 关键修复：先恢复迷雾状态，再解锁UI
        gameManager?.SetFogOfWarEnabled(fogStateBeforeEdit);
        
        // ✅ 关键修复：强制刷新迷雾（确保遮罩已创建）
        var fowManager = GetTree()?.GetFirstNodeInGroup("fog_of_war_manager") as FogOfWarManager;
        if (fogStateBeforeEdit && fowManager != null)
        {
            // 使用 CallDeferred 确保在下一帧执行，给 SetFogOfWarEnabled 时间完成初始化
            CallDeferred(nameof(DeferredRefreshFog));
        }
        
        // ✅ 解锁全局配置面板中的复选框并同步状态
        if (fogOfWarCheckBox != null)
        {
            fogOfWarCheckBox.Disabled = false;
            fogOfWarCheckBox.ButtonPressed = fogStateBeforeEdit;
        }

        // ✅ 修复：切回游玩模式时只刷新状态，不立即判定胜负
        // 避免空地图/刚导入地图时误报平局
        CallDeferred(nameof(DeferredCheckVictoryOnExitEdit));
    }
}

private void DeferredRefreshFog()
{
    var fowManager = GetTree()?.GetFirstNodeInGroup("fog_of_war_manager") as FogOfWarManager;
    if (fowManager != null)
    {
        fowManager.ForceRefresh();
    }
}

// ✅ 新增：切回游玩模式时只刷新状态，不立即判定胜负
// 避免空地图/刚导入地图时误报平局
private void DeferredCheckVictoryOnExitEdit()
{
    var gm = gameManager;
    if (gm != null && !gm.gameEnded)
    {
        // 只刷新迷雾，不判定胜负
        gm.fogOfWarManager?.ForceRefresh();
        // 注意：不调用 gm.CheckVictoryCondition()
        // 胜负判定应在正常游戏流程中（单位死亡、回合切换时）自动触发
    }
}

   private void CloseAllMenus()
    {
        selectionMenuPanel.Visible = false;
        terrainMenuPanel.Visible = false;
        propertyEditorPanel.Visible = false;
        spawnMenuPanel.Visible = false;
        globalConfigPanel.Visible = false;
        if (batchTerrainPanel != null) batchTerrainPanel.Visible = false;
        isMenuOpen = false;
        selectedGrid = null;
        currentEditingTarget = null;
        currentSpawnMode = SpawnMode.None;
        isDestroyMode = false;
        selectedTemplatePath = "";
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
        propertyEditorTitle.Text = $"编辑格子: {selectedGrid.GridIndex} ({selectedGrid.gridType})";
    }
    

}    private void UpdateGridVisual(Grids grid)
    {
        var sprite = grid.GetNodeOrNull<Sprite2D>("Sprite2D");
        if (sprite == null) return;

        Color terrainColor = terrainColors.GetValueOrDefault(grid.gridType, Colors.White);
        var tween = CreateTween();
        tween.TweenProperty(sprite, "modulate", terrainColor, 0.3f)
             .SetTrans(Tween.TransitionType.Sine)
             .SetEase(Tween.EaseType.InOut);
    }

    private void LoadCustomDamageValues(Grids grid)
    {
        if (grid == null) return;
        canDestroyCheckBox.ButtonPressed = grid.canDestroyUnit;
        fixedDamageSpinBox.Value = grid.fixedDamagePerTurn;
        fixedAttackSpinBox.Value = grid.fixedAttackPerTurn;
        canOverMaxCheckBox.ButtonPressed = grid.canOverMaxHealth;
    }

    private void LoadAmmoFuelValues(Grids grid)
    {
        if (grid == null) return;
        fixedAmmoSpinBox.Value = grid.fixedAmmoChangePerTurn;
        ammoCanOverMaxCheckBox.ButtonPressed = grid.ammoCanOverMax;
        ammoCanReachZeroCheckBox.ButtonPressed = grid.ammoCanReachZero;
        fixedFuelSpinBox.Value = grid.fixedFuelChangePerTurn;
        fuelCanOverMaxCheckBox.ButtonPressed = grid.fuelCanOverMax;
        fuelCanReachZeroCheckBox.ButtonPressed = grid.fuelCanReachZero;
    }

    private void SaveCustomDamageValues(Grids grid)
    {
        if (grid == null) return;
        grid.canDestroyUnit = canDestroyCheckBox.ButtonPressed;
        grid.fixedDamagePerTurn = (int)fixedDamageSpinBox.Value;
        grid.fixedAttackPerTurn = (int)fixedAttackSpinBox.Value;
        grid.canOverMaxHealth = canOverMaxCheckBox.ButtonPressed;
    }

    private void SaveAmmoFuelValues(Grids grid)
    {
        if (grid == null) return;
        grid.fixedAmmoChangePerTurn = (int)fixedAmmoSpinBox.Value;
        grid.ammoCanOverMax = ammoCanOverMaxCheckBox.ButtonPressed;
        grid.ammoCanReachZero = ammoCanReachZeroCheckBox.ButtonPressed;
        grid.fixedFuelChangePerTurn = (int)fixedFuelSpinBox.Value;
        grid.fuelCanOverMax = fuelCanOverMaxCheckBox.ButtonPressed;
        grid.fuelCanReachZero = fuelCanReachZeroCheckBox.ButtonPressed;
    }

    public bool ShouldBlockUnitOperations()
    {
        return IsEditMode || isMenuOpen;
    }

    private bool isDraggingMenu = false;
    private Vector2 dragOffset = Vector2.Zero;
    private Control currentDraggedMenu = null;

    private void MakeMenuDraggable(Control menuPanel, Control dragHandle = null)
    {
        Control handle = dragHandle ?? menuPanel;

        handle.GuiInput += (InputEvent @event) => {
            if (@event is InputEventMouseButton mouseEvent)
            {
                if (mouseEvent.ButtonIndex == MouseButton.Left)
                {
                    if (mouseEvent.Pressed)
                    {
                        isDraggingMenu = true;
                        currentDraggedMenu = menuPanel;
                        dragOffset = menuPanel.GlobalPosition - handle.GetGlobalMousePosition();
                    }
                    else
                    {
                        isDraggingMenu = false;
                        currentDraggedMenu = null;
                    }
                }
            }
        };
    }

    public override void _Process(double delta)
    {
        if (isDraggingMenu && currentDraggedMenu != null)
        {
            Vector2 newPosition = currentDraggedMenu.GetGlobalMousePosition() + dragOffset;

            var viewportSize = GetViewport().GetVisibleRect().Size;
            newPosition.X = Mathf.Clamp(newPosition.X, 0, viewportSize.X - currentDraggedMenu.Size.X);
            newPosition.Y = Mathf.Clamp(newPosition.Y, 0, viewportSize.Y - currentDraggedMenu.Size.Y);

            currentDraggedMenu.GlobalPosition = newPosition;
        }

        // ✅ 批量地形滑动铺设：按住鼠标左键在地图上拖动时自动铺设
        if (isBatchTerrainMode && IsEditMode && Input.IsMouseButtonPressed(MouseButton.Left))
        {
            if (gridManager != null && gridManager.map != null)
            {
                // ✅ 修复坐标偏移：使用视口坐标+CanvasTransform转换，正确处理Camera缩放/偏移
                Vector2 viewportPos = GetViewport().GetMousePosition();
                Transform2D canvasTransform = GetViewport().GetCanvasTransform();
                Vector2 worldPos = canvasTransform.AffineInverse() * viewportPos;
                
                int x = (int)((worldPos.X - gridManager.startPos.X) / gridManager.gridSize.X);
                int y = (int)((worldPos.Y - gridManager.startPos.Y) / gridManager.gridSize.Y);

                if (x >= 0 && x < gridManager.map.GetLength(0) && y >= 0 && y < gridManager.map.GetLength(1))
                {
                    var grid = gridManager.map[x, y];
                    if (grid != null && grid != lastBatchGrid)
                    {
                        lastBatchGrid = grid;
                        grid.gridType = batchTerrainType;
                        UpdateGridVisual(grid);
                    }
                }
            }
        }
    }

    // ========== ✅ 全局配置面板 ==========
    private void CreateGlobalConfigPanel()
    {
        openGlobalConfigBtn = new Button();
        openGlobalConfigBtn.Name = "OpenGlobalConfigBtn";
        openGlobalConfigBtn.Text = "⚙️ 全局配置";
        openGlobalConfigBtn.CustomMinimumSize = new Vector2(120, 40);
        openGlobalConfigBtn.AddThemeFontSizeOverride("font_size", 14);

        var btnStyle = new StyleBoxFlat();
        btnStyle.BgColor = new Color(0.3f, 0.4f, 0.6f, 0.9f);
        btnStyle.SetCornerRadiusAll(8);
        openGlobalConfigBtn.AddThemeStyleboxOverride("normal", btnStyle);

        openGlobalConfigBtn.Pressed += OpenGlobalConfigPanel;
        AddChild(openGlobalConfigBtn);

        var viewportSize = GetViewport().GetVisibleRect().Size;
        openGlobalConfigBtn.SetAnchorsPreset(LayoutPreset.TopLeft);
        openGlobalConfigBtn.Position = new Vector2(viewportSize.X - 290, viewportSize.Y - 70);

        globalConfigPanel = new Panel();
        globalConfigPanel.Name = "GlobalConfigPanel";
        globalConfigPanel.CustomMinimumSize = new Vector2(400, 600);
        globalConfigPanel.SetAnchorsPreset(LayoutPreset.Center);
        globalConfigPanel.Visible = false;
        globalConfigPanel.ZIndex = 1300;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.08f, 0.1f, 0.15f, 0.98f);
        panelStyle.SetCornerRadiusAll(16);
        panelStyle.SetBorderWidthAll(3);
        panelStyle.BorderColor = new Color(0.5f, 0.7f, 0.9f);
        globalConfigPanel.AddThemeStyleboxOverride("panel", panelStyle);

        var titleLabel = new Label();
        titleLabel.Text = "⚙️ 全局配置面板";
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        titleLabel.AddThemeFontSizeOverride("font_size", 22);
        titleLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.9f, 1f));
        titleLabel.CustomMinimumSize = new Vector2(0, 40);
        titleLabel.Position = new Vector2(0, 15);
        titleLabel.Size = new Vector2(400, 40);
        globalConfigPanel.AddChild(titleLabel);

        var closeBtn = new Button();
        closeBtn.Text = "✕";
        closeBtn.CustomMinimumSize = new Vector2(32, 32);
        closeBtn.Position = new Vector2(360, 12);
        closeBtn.Pressed += CloseGlobalConfigPanel;
        globalConfigPanel.AddChild(closeBtn);

        var scroll = new ScrollContainer();
        scroll.Position = new Vector2(20, 60);
        scroll.Size = new Vector2(360, 520);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        globalConfigContainer = new VBoxContainer();
        globalConfigContainer.AddThemeConstantOverride("separation", 10);
        globalConfigContainer.Size = new Vector2(360, 0);

        scroll.AddChild(globalConfigContainer);
        globalConfigPanel.AddChild(scroll);

        AddChild(globalConfigPanel);
        MakeMenuDraggable(globalConfigPanel);

        BuildGlobalConfigContent();
    }
private Label CreateConfigSectionTitle(string text)
{
    var label = new Label();
    label.Text = text;
    label.AddThemeFontSizeOverride("font_size", 15);
    label.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.3f));
    label.CustomMinimumSize = new Vector2(0, 28);
    return label;
}

private HBoxContainer CreateVisionConfigRow(string name, int currentValue, Action<int> onChanged)
{
    var hbox = new HBoxContainer();
    hbox.AddThemeConstantOverride("separation", 8);
    hbox.CustomMinimumSize = new Vector2(0, 32);

    var nameLabel = new Label();
    nameLabel.Text = name;
    nameLabel.CustomMinimumSize = new Vector2(100, 24);
    nameLabel.AddThemeFontSizeOverride("font_size", 12);
    hbox.AddChild(nameLabel);

    var spinBox = new SpinBox();
    spinBox.MinValue = 0;
    spinBox.MaxValue = 20;
    spinBox.Value = currentValue;
    spinBox.CustomMinimumSize = new Vector2(80, 28);
    spinBox.AddThemeFontSizeOverride("font_size", 12);
    spinBox.ValueChanged += (newVal) => onChanged((int)newVal);
    hbox.AddChild(spinBox);

    var hint = new Label();
    hint.Text = "格";
    hint.AddThemeFontSizeOverride("font_size", 11);
    hint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
    hbox.AddChild(hint);

    return hbox;
}

private VBoxContainer CreateWeaponVisionConfigRow(string name, int currentRange, bool isIndependent,
    Action<int, bool> onChanged)
{
    var vbox = new VBoxContainer();
    vbox.AddThemeConstantOverride("separation", 4);

    var hbox1 = new HBoxContainer();
    hbox1.AddThemeConstantOverride("separation", 8);

    var nameLabel = new Label();
    nameLabel.Text = name;
    nameLabel.CustomMinimumSize = new Vector2(100, 24);
    nameLabel.AddThemeFontSizeOverride("font_size", 12);
    hbox1.AddChild(nameLabel);

    var spinBox = new SpinBox();
    spinBox.MinValue = 0;
    spinBox.MaxValue = 20;
    spinBox.Value = currentRange;
    spinBox.CustomMinimumSize = new Vector2(80, 28);
    spinBox.AddThemeFontSizeOverride("font_size", 12);
    hbox1.AddChild(spinBox);

    vbox.AddChild(hbox1);

    var hbox2 = new HBoxContainer();
    hbox2.AddThemeConstantOverride("separation", 8);

    var modeLabel = new Label();
    modeLabel.Text = "  模式:";
    modeLabel.CustomMinimumSize = new Vector2(50, 24);
    modeLabel.AddThemeFontSizeOverride("font_size", 11);
    hbox2.AddChild(modeLabel);

    var modeOption = new OptionButton();
    modeOption.AddItem("正常(菱形)", 0);
    modeOption.AddItem("独立(攻击范围)", 1);
    modeOption.Selected = isIndependent ? 1 : 0;
    modeOption.CustomMinimumSize = new Vector2(140, 28);
    modeOption.AddThemeFontSizeOverride("font_size", 11);
    hbox2.AddChild(modeOption);

    vbox.AddChild(hbox2);

    spinBox.ValueChanged += (newRange) => {
        onChanged((int)newRange, modeOption.Selected == 1);
    };
    modeOption.ItemSelected += (index) => {
        onChanged((int)spinBox.Value, index == 1);
    };

    return vbox;
}

private HBoxContainer CreateTerrainVisionRow(GridType terrain, int currentBonus, Action<int> onChanged)
{
    var hbox = new HBoxContainer();
    hbox.AddThemeConstantOverride("separation", 8);
    hbox.CustomMinimumSize = new Vector2(0, 28);

    var nameLabel = new Label();
    nameLabel.Text = terrain.ToString();
    nameLabel.CustomMinimumSize = new Vector2(120, 24);
    nameLabel.AddThemeFontSizeOverride("font_size", 11);
    hbox.AddChild(nameLabel);

    var spinBox = new SpinBox();
    spinBox.MinValue = -10;
    spinBox.MaxValue = 10;
    spinBox.Value = currentBonus;
    spinBox.CustomMinimumSize = new Vector2(70, 26);
    spinBox.AddThemeFontSizeOverride("font_size", 11);
    spinBox.ValueChanged += (newVal) => onChanged((int)newVal);
    hbox.AddChild(spinBox);

    var signLabel = new Label();
    string sign = currentBonus > 0 ? "+" : "";
    signLabel.Text = $"({sign}{currentBonus})";
    signLabel.AddThemeFontSizeOverride("font_size", 10);
    signLabel.AddThemeColorOverride("font_color",
        currentBonus > 0 ? new Color(0.3f, 1, 0.3f) :
        currentBonus < 0 ? new Color(1, 0.3f, 0.3f) : new Color(0.7f, 0.7f, 0.7f));
    hbox.AddChild(signLabel);

    return hbox;
}
private void OpenGlobalConfigPanel()
{
    CloseAllMenus();
    globalConfigPanel.Visible = true;
    isMenuOpen = true;

    // ✅ 关键修复：从 FogOfWarManager 直接读取当前状态，而不是依赖复选框
    var fowManager = GetTree()?.GetFirstNodeInGroup("fog_of_war_manager") as FogOfWarManager;
    bool currentFogState = fowManager?.isFogOfWarEnabled ?? gameManager?.IsFogOfWarEnabled() ?? true;
    
    if (fogOfWarCheckBox != null)
    {
        // 断开信号避免循环触发
        var callable = new Callable(this, nameof(OnFogCheckBoxToggled));
        if (fogOfWarCheckBox.IsConnected(BaseButton.SignalName.Toggled, callable))
        {
            fogOfWarCheckBox.Disconnect(BaseButton.SignalName.Toggled, callable);
        }
        
        fogOfWarCheckBox.ButtonPressed = currentFogState;
        fogOfWarCheckBox.Disabled = IsEditMode;  // 编辑模式下禁用
        
        // 重新连接信号
        fogOfWarCheckBox.Connect(BaseButton.SignalName.Toggled, callable);
    }
}
private void RefreshAllUnitVision()
    {
        if (gameManager?.unitManager?.AllUnits != null)
        {
            foreach (var unit in gameManager.unitManager.AllUnits)
            {
                if (unit != null && IsInstanceValid(unit) && unit.useConfigVision)
                {
                    unit.visionRange = VisionConfig.GetUnitVisionRange(unit.GetType().Name);
                }
            }
        }

        if (gameManager?.weaponManager?.AllWeapons != null)
        {
            foreach (var weapon in gameManager.weaponManager.AllWeapons)
            {
                if (weapon != null && IsInstanceValid(weapon) && weapon.useConfigVision)
                {
                    weapon.visionRange = VisionConfig.GetWeaponVisionRange(weapon.GetType().Name);
                    weapon.visionMode = VisionConfig.IsWeaponIndependentMode(weapon.GetType().Name)
                        ? VisionMode.Independent : VisionMode.Normal;
                }
            }
        }

        gameManager?.fogOfWarManager?.RefreshFog();
    }



    private void BuildGlobalConfigContent()
    {
        foreach (var child in globalConfigContainer.GetChildren())
            child.QueueFree();

        var fogSection = CreateConfigSectionTitle("🌫️ 战争迷雾");
        globalConfigContainer.AddChild(fogSection);

        var fogHBox = new HBoxContainer();
        fogHBox.AddThemeConstantOverride("separation", 10);

        var fogLabel = new Label();
        fogLabel.Text = "启用战争迷雾:";
        fogLabel.CustomMinimumSize = new Vector2(120, 28);
        fogLabel.AddThemeFontSizeOverride("font_size", 13);
        fogHBox.AddChild(fogLabel);

        fogOfWarCheckBox = new CheckBox();
        fogOfWarCheckBox.ButtonPressed = gameManager?.IsFogOfWarEnabled() ?? true;
        fogOfWarCheckBox.CustomMinimumSize = new Vector2(28, 28);
        fogOfWarCheckBox.Toggled += OnFogCheckBoxToggled;
        fogHBox.AddChild(fogOfWarCheckBox);

        var fogHint = new Label();
        fogHint.Text = "(编辑模式自动关闭)";
        fogHint.AddThemeFontSizeOverride("font_size", 10);
        fogHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        fogHBox.AddChild(fogHint);

        globalConfigContainer.AddChild(fogHBox);

        var unitSection = CreateConfigSectionTitle("👁️ 单位基础视野配置");
        globalConfigContainer.AddChild(unitSection);

        var unitConfigs = VisionConfig.GetAllUnitVisionConfigs();
        foreach (var kvp in unitConfigs)
        {
            var row = CreateVisionConfigRow(kvp.Key, kvp.Value, (newVal) => {
                VisionConfig.SetUnitVisionRange(kvp.Key, newVal);
                RefreshAllUnitVision();
            });
            globalConfigContainer.AddChild(row);
        }

        var weaponSection = CreateConfigSectionTitle("⚔️ 兵器基础视野配置");
        globalConfigContainer.AddChild(weaponSection);

        var weaponConfigs = VisionConfig.GetAllWeaponVisionConfigs();
        foreach (var kvp in weaponConfigs)
        {
            var row = CreateWeaponVisionConfigRow(kvp.Key, kvp.Value,
                VisionConfig.IsWeaponIndependentMode(kvp.Key),
                (newRange, newMode) => {
                    VisionConfig.SetWeaponVisionRange(kvp.Key, newRange);
                    VisionConfig.SetWeaponIndependentMode(kvp.Key, newMode);
                    RefreshAllUnitVision();
                });
            globalConfigContainer.AddChild(row);
        }

        // ✅ 全局默认地形加成
        var terrainSection = CreateConfigSectionTitle("🗺️ 全局默认地形视野加成");
        globalConfigContainer.AddChild(terrainSection);

        var terrainConfigs = VisionConfig.GetAllTerrainVisionConfigs();
        foreach (var kvp in terrainConfigs)
        {
            var row = CreateTerrainVisionRow(kvp.Key, kvp.Value, (newVal) => {
                VisionConfig.SetTerrainVisionBonus(kvp.Key, newVal);
                RefreshAllUnitVision();
            });
            globalConfigContainer.AddChild(row);
        }

        // ✅ 新增：单位类型×地形 专属加成矩阵配置
        var unitMatrixSection = CreateConfigSectionTitle("👁️ 单位类型×地形 专属加成矩阵");
        globalConfigContainer.AddChild(unitMatrixSection);

        var unitMatrixBtn = new Button();
        unitMatrixBtn.Text = "📊 打开单位地形加成矩阵编辑器";
        unitMatrixBtn.CustomMinimumSize = new Vector2(300, 40);
        unitMatrixBtn.AddThemeFontSizeOverride("font_size", 13);
        unitMatrixBtn.Pressed += OpenUnitTerrainMatrixEditor;
        globalConfigContainer.AddChild(unitMatrixBtn);

        // ✅ 新增：兵器类型×地形 专属加成矩阵配置
        var weaponMatrixSection = CreateConfigSectionTitle("⚔️ 兵器类型×地形 专属加成矩阵");
        globalConfigContainer.AddChild(weaponMatrixSection);

        var weaponMatrixBtn = new Button();
        weaponMatrixBtn.Text = "📊 打开兵器地形加成矩阵编辑器";
        weaponMatrixBtn.CustomMinimumSize = new Vector2(300, 40);
        weaponMatrixBtn.AddThemeFontSizeOverride("font_size", 13);
        weaponMatrixBtn.Pressed += OpenWeaponTerrainMatrixEditor;
        globalConfigContainer.AddChild(weaponMatrixBtn);

        // ✅ 资金系统配置
        var fundsSection = CreateConfigSectionTitle("💰 资金系统配置");
        globalConfigContainer.AddChild(fundsSection);

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;

        // P1 现有资金
        var p1FundsHBox = new HBoxContainer();
        p1FundsHBox.AddThemeConstantOverride("separation", 10);
        var p1FundsLabel = new Label();
        p1FundsLabel.Text = "P1 现有资金:";
        p1FundsLabel.CustomMinimumSize = new Vector2(120, 28);
        p1FundsLabel.AddThemeFontSizeOverride("font_size", 13);
        p1FundsHBox.AddChild(p1FundsLabel);
        var p1FundsSpin = new SpinBox();
        p1FundsSpin.MinValue = 0;
        p1FundsSpin.MaxValue = 999999;
        p1FundsSpin.Value = gm?.p1Funds ?? 0;
        p1FundsSpin.CustomMinimumSize = new Vector2(100, 28);
        p1FundsSpin.AddThemeFontSizeOverride("font_size", 12);
        p1FundsSpin.ValueChanged += (newVal) => {
            if (gm != null) gm.p1Funds = (int)newVal;
        };
        p1FundsHBox.AddChild(p1FundsSpin);
        globalConfigContainer.AddChild(p1FundsHBox);

        // P1 资金上限
        var p1MaxHBox = new HBoxContainer();
        p1MaxHBox.AddThemeConstantOverride("separation", 10);
        var p1MaxLabel = new Label();
        p1MaxLabel.Text = "P1 资金上限:";
        p1MaxLabel.CustomMinimumSize = new Vector2(120, 28);
        p1MaxLabel.AddThemeFontSizeOverride("font_size", 13);
        p1MaxHBox.AddChild(p1MaxLabel);
        var p1MaxSpin = new SpinBox();
        p1MaxSpin.MinValue = 0;
        p1MaxSpin.MaxValue = 9999999;
        p1MaxSpin.Value = gm?.p1FundsMax ?? 999999;
        p1MaxSpin.CustomMinimumSize = new Vector2(100, 28);
        p1MaxSpin.AddThemeFontSizeOverride("font_size", 12);
        p1MaxSpin.ValueChanged += (newVal) => {
            if (gm != null) gm.p1FundsMax = (int)newVal;
        };
        p1MaxHBox.AddChild(p1MaxSpin);
        globalConfigContainer.AddChild(p1MaxHBox);

        // P2 现有资金
        var p2FundsHBox = new HBoxContainer();
        p2FundsHBox.AddThemeConstantOverride("separation", 10);
        var p2FundsLabel = new Label();
        p2FundsLabel.Text = "P2 现有资金:";
        p2FundsLabel.CustomMinimumSize = new Vector2(120, 28);
        p2FundsLabel.AddThemeFontSizeOverride("font_size", 13);
        p2FundsHBox.AddChild(p2FundsLabel);
        var p2FundsSpin = new SpinBox();
        p2FundsSpin.MinValue = 0;
        p2FundsSpin.MaxValue = 999999;
        p2FundsSpin.Value = gm?.p2Funds ?? 0;
        p2FundsSpin.CustomMinimumSize = new Vector2(100, 28);
        p2FundsSpin.AddThemeFontSizeOverride("font_size", 12);
        p2FundsSpin.ValueChanged += (newVal) => {
            if (gm != null) gm.p2Funds = (int)newVal;
        };
        p2FundsHBox.AddChild(p2FundsSpin);
        globalConfigContainer.AddChild(p2FundsHBox);

        // P2 资金上限
        var p2MaxHBox = new HBoxContainer();
        p2MaxHBox.AddThemeConstantOverride("separation", 10);
        var p2MaxLabel = new Label();
        p2MaxLabel.Text = "P2 资金上限:";
        p2MaxLabel.CustomMinimumSize = new Vector2(120, 28);
        p2MaxLabel.AddThemeFontSizeOverride("font_size", 13);
        p2MaxHBox.AddChild(p2MaxLabel);
        var p2MaxSpin = new SpinBox();
        p2MaxSpin.MinValue = 0;
        p2MaxSpin.MaxValue = 9999999;
        p2MaxSpin.Value = gm?.p2FundsMax ?? 999999;
        p2MaxSpin.CustomMinimumSize = new Vector2(100, 28);
        p2MaxSpin.AddThemeFontSizeOverride("font_size", 12);
        p2MaxSpin.ValueChanged += (newVal) => {
            if (gm != null) gm.p2FundsMax = (int)newVal;
        };
        p2MaxHBox.AddChild(p2MaxSpin);
        globalConfigContainer.AddChild(p2MaxHBox);

        // ✅ AI 与地图导出配置
        var aiSection = CreateConfigSectionTitle("🤖 AI 控制与地图导出");
        globalConfigContainer.AddChild(aiSection);

        var aiMgr = GetTree()?.GetFirstNodeInGroup("ai_manager") as AI_Manager;

        var p1AiHBox = new HBoxContainer();
        p1AiHBox.AddThemeConstantOverride("separation", 10);
        var p1AiLabel = new Label();
        p1AiLabel.Text = "P1 AI 操控:";
        p1AiLabel.CustomMinimumSize = new Vector2(120, 28);
        p1AiLabel.AddThemeFontSizeOverride("font_size", 13);
        p1AiHBox.AddChild(p1AiLabel);
        var p1AiCheck = new CheckBox();
        p1AiCheck.ButtonPressed = aiMgr?.p1AIEnabled ?? false;
        p1AiCheck.CustomMinimumSize = new Vector2(28, 28);
        p1AiCheck.Toggled += (pressed) => {
            if (aiMgr != null) aiMgr.p1AIEnabled = pressed;
        };
        p1AiHBox.AddChild(p1AiCheck);
        globalConfigContainer.AddChild(p1AiHBox);

        var p2AiHBox = new HBoxContainer();
        p2AiHBox.AddThemeConstantOverride("separation", 10);
        var p2AiLabel = new Label();
        p2AiLabel.Text = "P2 AI 操控:";
        p2AiLabel.CustomMinimumSize = new Vector2(120, 28);
        p2AiLabel.AddThemeFontSizeOverride("font_size", 13);
        p2AiHBox.AddChild(p2AiLabel);
        var p2AiCheck = new CheckBox();
        p2AiCheck.ButtonPressed = aiMgr?.p2AIEnabled ?? false;
        p2AiCheck.CustomMinimumSize = new Vector2(28, 28);
        p2AiCheck.Toggled += (pressed) => {
            if (aiMgr != null) aiMgr.p2AIEnabled = pressed;
        };
        p2AiHBox.AddChild(p2AiCheck);
        globalConfigContainer.AddChild(p2AiHBox);

        var autoEndHBox = new HBoxContainer();
        autoEndHBox.AddThemeConstantOverride("separation", 10);
        var autoEndLabel = new Label();
        autoEndLabel.Text = "自动切换回合:";
        autoEndLabel.CustomMinimumSize = new Vector2(120, 28);
        autoEndLabel.AddThemeFontSizeOverride("font_size", 13);
        autoEndHBox.AddChild(autoEndLabel);
        var autoEndCheck = new CheckBox();
        autoEndCheck.ButtonPressed = aiMgr?.autoEndTurn ?? false;
        autoEndCheck.CustomMinimumSize = new Vector2(28, 28);
        autoEndCheck.Toggled += (pressed) => {
            if (aiMgr != null) aiMgr.autoEndTurn = pressed;
        };
        autoEndHBox.AddChild(autoEndCheck);
        globalConfigContainer.AddChild(autoEndHBox);

        var exportMapBtn = new Button();
        exportMapBtn.Text = "📤 导出地图配置为 txt";
        exportMapBtn.CustomMinimumSize = new Vector2(300, 40);
        exportMapBtn.AddThemeFontSizeOverride("font_size", 13);
        exportMapBtn.Pressed += ExportMapToTxt;
        globalConfigContainer.AddChild(exportMapBtn);

        var importMapBtn = new Button();
        importMapBtn.Text = "📥 导入地图配置";
        importMapBtn.CustomMinimumSize = new Vector2(300, 40);
        importMapBtn.AddThemeFontSizeOverride("font_size", 13);
        importMapBtn.Pressed += OpenImportDialog;
        globalConfigContainer.AddChild(importMapBtn);

        // ✅ 极简导出/导入按钮
        var compactExportBtn = new Button();
        compactExportBtn.Text = "📤 极简导出";
        compactExportBtn.CustomMinimumSize = new Vector2(300, 40);
        compactExportBtn.AddThemeFontSizeOverride("font_size", 13);
        compactExportBtn.Pressed += ExportCompactMap;
        globalConfigContainer.AddChild(compactExportBtn);

        var compactImportBtn = new Button();

        // ✅ 胜利判定配置
        var victorySection = CreateConfigSectionTitle("🏆 胜利判定配置");
        globalConfigContainer.AddChild(victorySection);

        var p1AnnihHBox = new HBoxContainer();
        p1AnnihHBox.AddThemeConstantOverride("separation", 10);
        var p1AnnihLabel = new Label();
        p1AnnihLabel.Text = "P1 全灭胜利判定:";
        p1AnnihLabel.CustomMinimumSize = new Vector2(150, 28);
        p1AnnihLabel.AddThemeFontSizeOverride("font_size", 13);
        p1AnnihHBox.AddChild(p1AnnihLabel);
        var p1AnnihCheck = new CheckBox();
        p1AnnihCheck.ButtonPressed = gm?.p1AnnihilationVictoryEnabled ?? true;
        p1AnnihCheck.CustomMinimumSize = new Vector2(28, 28);
        p1AnnihCheck.Toggled += (pressed) => {
            if (gm != null) gm.p1AnnihilationVictoryEnabled = pressed;
        };
        p1AnnihHBox.AddChild(p1AnnihCheck);
        globalConfigContainer.AddChild(p1AnnihHBox);

        var p2AnnihHBox = new HBoxContainer();
        p2AnnihHBox.AddThemeConstantOverride("separation", 10);
        var p2AnnihLabel = new Label();
        p2AnnihLabel.Text = "P2 全灭胜利判定:";
        p2AnnihLabel.CustomMinimumSize = new Vector2(150, 28);
        p2AnnihLabel.AddThemeFontSizeOverride("font_size", 13);
        p2AnnihHBox.AddChild(p2AnnihLabel);
        var p2AnnihCheck = new CheckBox();
        p2AnnihCheck.ButtonPressed = gm?.p2AnnihilationVictoryEnabled ?? true;
        p2AnnihCheck.CustomMinimumSize = new Vector2(28, 28);
        p2AnnihCheck.Toggled += (pressed) => {
            if (gm != null) gm.p2AnnihilationVictoryEnabled = pressed;
        };
        p2AnnihHBox.AddChild(p2AnnihCheck);
        globalConfigContainer.AddChild(p2AnnihHBox);

        var weaponVictoryHBox = new HBoxContainer();
        weaponVictoryHBox.AddThemeConstantOverride("separation", 10);
        var weaponVictoryLabel = new Label();
        weaponVictoryLabel.Text = "兵器摧毁胜利判定:";
        weaponVictoryLabel.CustomMinimumSize = new Vector2(150, 28);
        weaponVictoryLabel.AddThemeFontSizeOverride("font_size", 13);
        weaponVictoryHBox.AddChild(weaponVictoryLabel);
        var weaponVictoryCheck = new CheckBox();
        weaponVictoryCheck.ButtonPressed = gm?.weaponVictoryEnabled ?? false;
        weaponVictoryCheck.CustomMinimumSize = new Vector2(28, 28);
        weaponVictoryCheck.Toggled += (pressed) => {
            if (gm != null) gm.weaponVictoryEnabled = pressed;
        };
        weaponVictoryHBox.AddChild(weaponVictoryCheck);
        globalConfigContainer.AddChild(weaponVictoryHBox);
        compactImportBtn.Text = "📥 极简导入";
        compactImportBtn.CustomMinimumSize = new Vector2(300, 40);
        compactImportBtn.AddThemeFontSizeOverride("font_size", 13);
        compactImportBtn.Pressed += OpenCompactImportDialog;
        globalConfigContainer.AddChild(compactImportBtn);

        var awbwImportBtn = new Button();
        awbwImportBtn.Text = "🌐 导入 AWBW 地图";
        awbwImportBtn.CustomMinimumSize = new Vector2(300, 40);
        awbwImportBtn.AddThemeFontSizeOverride("font_size", 13);
        awbwImportBtn.Pressed += OpenAWBWImportDialog;
        globalConfigContainer.AddChild(awbwImportBtn);

        var awmeImportBtn = new Button();
        awmeImportBtn.Text = "🗺️ 导入 AW 地图 (.aws/.aw2/.awm/.awd)";
        awmeImportBtn.CustomMinimumSize = new Vector2(300, 40);
        awmeImportBtn.AddThemeFontSizeOverride("font_size", 13);
        awmeImportBtn.Pressed += OpenAWMEImportDialog;
        globalConfigContainer.AddChild(awmeImportBtn);

        var btnSeparator = new HSeparator();
        btnSeparator.CustomMinimumSize = new Vector2(0, 10);
        globalConfigContainer.AddChild(btnSeparator);

        // ✅ 新增：地图生成配置
        var mapGenSection = CreateConfigSectionTitle("🗺️ 生成地图");
        globalConfigContainer.AddChild(mapGenSection);

        var mapGenHBox = new HBoxContainer();
        mapGenHBox.AddThemeConstantOverride("separation", 10);

        var widthLabel = new Label();
        widthLabel.Text = "宽:";
        widthLabel.CustomMinimumSize = new Vector2(40, 28);
        widthLabel.AddThemeFontSizeOverride("font_size", 13);
        mapGenHBox.AddChild(widthLabel);

        var widthSpin = new SpinBox();
        widthSpin.Name = "MapWidthSpin";
        widthSpin.MinValue = 1;
        widthSpin.MaxValue = 100;
        widthSpin.Value = gridManager?.map?.GetLength(0) ?? 20;
        widthSpin.CustomMinimumSize = new Vector2(70, 28);
        widthSpin.AddThemeFontSizeOverride("font_size", 12);
        mapGenHBox.AddChild(widthSpin);

        var heightLabel = new Label();
        heightLabel.Text = "高:";
        heightLabel.CustomMinimumSize = new Vector2(40, 28);
        heightLabel.AddThemeFontSizeOverride("font_size", 13);
        mapGenHBox.AddChild(heightLabel);

        var heightSpin = new SpinBox();
        heightSpin.Name = "MapHeightSpin";
        heightSpin.MinValue = 1;
        heightSpin.MaxValue = 100;
        heightSpin.Value = gridManager?.map?.GetLength(1) ?? 15;
        heightSpin.CustomMinimumSize = new Vector2(70, 28);
        heightSpin.AddThemeFontSizeOverride("font_size", 12);
        mapGenHBox.AddChild(heightSpin);

        var genMapBtn = new Button();
        genMapBtn.Text = "🗺️ 生成新地图";
        genMapBtn.CustomMinimumSize = new Vector2(140, 36);
        genMapBtn.AddThemeFontSizeOverride("font_size", 13);
        var genStyle = new StyleBoxFlat();
        genStyle.BgColor = new Color(0.2f, 0.5f, 0.3f, 0.9f);
        genStyle.SetCornerRadiusAll(8);
        genMapBtn.AddThemeStyleboxOverride("normal", genStyle);
        genMapBtn.Pressed += () => {
            int w = (int)widthSpin.Value;
            int h = (int)heightSpin.Value;
            GenerateNewMap(w, h);
        };
        mapGenHBox.AddChild(genMapBtn);

        globalConfigContainer.AddChild(mapGenHBox);

        var btnHBox = new HBoxContainer();
        btnHBox.AddThemeConstantOverride("separation", 10);

        var resetBtn = new Button();
        resetBtn.Text = "🔄 重置默认";
        resetBtn.CustomMinimumSize = new Vector2(150, 36);
        resetBtn.AddThemeFontSizeOverride("font_size", 13);
        resetBtn.Pressed += () => {
            VisionConfig.ResetToDefaults();
            BuildGlobalConfigContent();
            RefreshAllUnitVision();
        };
        btnHBox.AddChild(resetBtn);

        var exportBtn = new Button();
        exportBtn.Text = "📋 导出配置";
        exportBtn.CustomMinimumSize = new Vector2(150, 36);
        exportBtn.AddThemeFontSizeOverride("font_size", 13);
        exportBtn.Pressed += () => {
            var info = VisionConfig.ExportConfig();
        };
        btnHBox.AddChild(exportBtn);

        globalConfigContainer.AddChild(btnHBox);
    }

    // ✅ 新增：单位地形加成矩阵编辑器窗口
    private Panel unitMatrixEditorPanel;
private void OpenUnitTerrainMatrixEditor()
{
    if (unitMatrixEditorPanel == null) CreateUnitMatrixEditorPanel();

    var container = unitMatrixEditorPanel.GetNodeOrNull<VBoxContainer>("MatrixContainer");
    if (container != null)
    {
        foreach (var child in container.GetChildren()) 
            child.QueueFree();

        var allUnitTypes = new List<string>(VisionConfig.GetAllUnitVisionConfigs().Keys);
        var allTerrainTypes = new List<GridType>();
        foreach (GridType gt in Enum.GetValues(typeof(GridType)))
            allTerrainTypes.Add(gt);

        if (allUnitTypes.Count == 0)
        {
            var emptyLabel = new Label();
            emptyLabel.Text = "⚠️ 无单位类型配置";
            emptyLabel.AddThemeFontSizeOverride("font_size", 14);
            emptyLabel.AddThemeColorOverride("font_color", Colors.Red);
            container.AddChild(emptyLabel);
        }
        else
        {
            foreach (var unitType in allUnitTypes)
            {
                var unitLabel = new Label();
                unitLabel.Text = $"=== {unitType} ===";
                unitLabel.AddThemeFontSizeOverride("font_size", 14);
                unitLabel.AddThemeColorOverride("font_color", Colors.Yellow);
                container.AddChild(unitLabel);

                var table = VisionConfig.GetUnitTerrainBonusTable(unitType);
                foreach (var terrain in allTerrainTypes)
                {
                    var hbox = new HBoxContainer();
                    hbox.AddThemeConstantOverride("separation", 8);

                    var terrainLabel = new Label();
                    terrainLabel.Text = terrain.ToString();
                    terrainLabel.CustomMinimumSize = new Vector2(100, 24);
                    terrainLabel.AddThemeFontSizeOverride("font_size", 11);
                    hbox.AddChild(terrainLabel);

                    int currentValue = table.ContainsKey(terrain) ? table[terrain] : 0;

                    var spinBox = new SpinBox();
                    spinBox.MinValue = -10;
                    spinBox.MaxValue = 10;
                    spinBox.Value = currentValue;
                    spinBox.CustomMinimumSize = new Vector2(70, 26);
                    spinBox.AddThemeFontSizeOverride("font_size", 11);

                    string capturedUnit = unitType;
                    GridType capturedTerrain = terrain;
                    spinBox.ValueChanged += (newVal) => {
                        VisionConfig.SetUnitTerrainBonus(capturedUnit, capturedTerrain, (int)newVal);
                        RefreshAllUnitVision();
                    };

                    hbox.AddChild(spinBox);

                    var signLabel = new Label();
                    string sign = currentValue > 0 ? "+" : "";
                    signLabel.Text = $"({sign}{currentValue})";
                    signLabel.AddThemeFontSizeOverride("font_size", 10);
                    signLabel.AddThemeColorOverride("font_color",
                        currentValue > 0 ? new Color(0.3f, 1, 0.3f) :
                        currentValue < 0 ? new Color(1, 0.3f, 0.3f) : new Color(0.7f, 0.7f, 0.7f));
                    hbox.AddChild(signLabel);

                    container.AddChild(hbox);
                }
            }
        }
    }

    unitMatrixEditorPanel.Visible = true;
    isMenuOpen = true;
    container?.QueueSort();
}

private void OpenWeaponTerrainMatrixEditor()
{
    if (weaponMatrixEditorPanel == null) CreateWeaponMatrixEditorPanel();

    var container = weaponMatrixEditorPanel.GetNodeOrNull<VBoxContainer>("MatrixContainer");
    if (container != null)
    {
        foreach (var child in container.GetChildren()) 
            child.QueueFree();

        var allWeaponTypes = new List<string>(VisionConfig.GetAllWeaponVisionConfigs().Keys);
        var allTerrainTypes = new List<GridType>();
        foreach (GridType gt in Enum.GetValues(typeof(GridType)))
            allTerrainTypes.Add(gt);

        if (allWeaponTypes.Count == 0)
        {
            var emptyLabel = new Label();
            emptyLabel.Text = "⚠️ 无兵器类型配置";
            emptyLabel.AddThemeFontSizeOverride("font_size", 14);
            emptyLabel.AddThemeColorOverride("font_color", Colors.Red);
            container.AddChild(emptyLabel);
        }
        else
        {
            foreach (var weaponType in allWeaponTypes)
            {
                var weaponLabel = new Label();
                weaponLabel.Text = $"=== {weaponType} ===";
                weaponLabel.AddThemeFontSizeOverride("font_size", 14);
                weaponLabel.AddThemeColorOverride("font_color", Colors.Yellow);
                container.AddChild(weaponLabel);

                foreach (var terrain in allTerrainTypes)
                {
                    var hbox = new HBoxContainer();
                    hbox.AddThemeConstantOverride("separation", 8);

                    var terrainLabel = new Label();
                    terrainLabel.Text = terrain.ToString();
                    terrainLabel.CustomMinimumSize = new Vector2(100, 24);
                    terrainLabel.AddThemeFontSizeOverride("font_size", 11);
                    hbox.AddChild(terrainLabel);

                    int currentVal = VisionConfig.GetWeaponTerrainVisionBonus(weaponType, terrain);

                    var spinBox = new SpinBox();
                    spinBox.MinValue = -10;
                    spinBox.MaxValue = 10;
                    spinBox.Value = currentVal;
                    spinBox.CustomMinimumSize = new Vector2(70, 26);
                    spinBox.AddThemeFontSizeOverride("font_size", 11);

                    string capturedWeapon = weaponType;
                    GridType capturedTerrain = terrain;
                    spinBox.ValueChanged += (newVal) => {
                        VisionConfig.SetWeaponTerrainBonus(capturedWeapon, capturedTerrain, (int)newVal);
                        RefreshAllUnitVision();
                    };

                    hbox.AddChild(spinBox);
                    container.AddChild(hbox);
                }
            }
        }
    }

    weaponMatrixEditorPanel.Visible = true;
    isMenuOpen = true;
    container?.QueueSort();
}

    private void CreateUnitMatrixEditorPanel()
    {
        unitMatrixEditorPanel = new Panel();
        unitMatrixEditorPanel.Name = "UnitMatrixEditorPanel";
        unitMatrixEditorPanel.CustomMinimumSize = new Vector2(450, 600);
        unitMatrixEditorPanel.SetAnchorsPreset(LayoutPreset.Center);
        unitMatrixEditorPanel.Visible = false;
        unitMatrixEditorPanel.ZIndex = 1400;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.08f, 0.1f, 0.15f, 0.98f);
        panelStyle.SetCornerRadiusAll(16);
        panelStyle.SetBorderWidthAll(3);
        panelStyle.BorderColor = new Color(0.5f, 0.7f, 0.9f);
        unitMatrixEditorPanel.AddThemeStyleboxOverride("panel", panelStyle);

        var titleLabel = new Label();
        titleLabel.Text = "📊 单位类型×地形 视野加成矩阵";
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        titleLabel.AddThemeFontSizeOverride("font_size", 20);
        titleLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.9f, 1f));
        titleLabel.CustomMinimumSize = new Vector2(0, 40);
        titleLabel.Position = new Vector2(0, 15);
        titleLabel.Size = new Vector2(450, 40);
        unitMatrixEditorPanel.AddChild(titleLabel);

        var closeBtn = new Button();
        closeBtn.Text = "✕";
        closeBtn.CustomMinimumSize = new Vector2(32, 32);
        closeBtn.Position = new Vector2(410, 12);
        closeBtn.Pressed += () => { unitMatrixEditorPanel.Visible = false; isMenuOpen = false; };
        unitMatrixEditorPanel.AddChild(closeBtn);

        var scroll = new ScrollContainer();
        scroll.Position = new Vector2(20, 60);
        scroll.Size = new Vector2(410, 520);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        var container = new VBoxContainer();
        container.Name = "MatrixContainer";
        container.AddThemeConstantOverride("separation", 6);
        container.Size = new Vector2(410, 0);

        scroll.AddChild(container);
        unitMatrixEditorPanel.AddChild(scroll);

        AddChild(unitMatrixEditorPanel);
        MakeMenuDraggable(unitMatrixEditorPanel);
    }

    // ✅ 新增：兵器地形加成矩阵编辑器
    private Panel weaponMatrixEditorPanel;
    private void CreateWeaponMatrixEditorPanel()
    {
        weaponMatrixEditorPanel = new Panel();
        weaponMatrixEditorPanel.Name = "WeaponMatrixEditorPanel";
        weaponMatrixEditorPanel.CustomMinimumSize = new Vector2(450, 400);
        weaponMatrixEditorPanel.SetAnchorsPreset(LayoutPreset.Center);
        weaponMatrixEditorPanel.Visible = false;
        weaponMatrixEditorPanel.ZIndex = 1400;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.08f, 0.1f, 0.15f, 0.98f);
        panelStyle.SetCornerRadiusAll(16);
        panelStyle.SetBorderWidthAll(3);
        panelStyle.BorderColor = new Color(0.7f, 0.5f, 0.9f);
        weaponMatrixEditorPanel.AddThemeStyleboxOverride("panel", panelStyle);

        var titleLabel = new Label();
        titleLabel.Text = "📊 兵器类型×地形 视野加成矩阵";
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        titleLabel.AddThemeFontSizeOverride("font_size", 20);
        titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.5f, 1f));
        titleLabel.CustomMinimumSize = new Vector2(0, 40);
        titleLabel.Position = new Vector2(0, 15);
        titleLabel.Size = new Vector2(450, 40);
        weaponMatrixEditorPanel.AddChild(titleLabel);

        var closeBtn = new Button();
        closeBtn.Text = "✕";
        closeBtn.CustomMinimumSize = new Vector2(32, 32);
        closeBtn.Position = new Vector2(410, 12);
        closeBtn.Pressed += () => { weaponMatrixEditorPanel.Visible = false; isMenuOpen = false; };
        weaponMatrixEditorPanel.AddChild(closeBtn);

        var scroll = new ScrollContainer();
        scroll.Position = new Vector2(20, 60);
        scroll.Size = new Vector2(410, 320);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        var container = new VBoxContainer();
        container.Name = "MatrixContainer";
        container.AddThemeConstantOverride("separation", 6);
        container.Size = new Vector2(410, 0);

        scroll.AddChild(container);
        weaponMatrixEditorPanel.AddChild(scroll);

        AddChild(weaponMatrixEditorPanel);
        MakeMenuDraggable(weaponMatrixEditorPanel);
    }

    private void OnFogCheckBoxToggled(bool pressed)
{
    if (IsEditMode) return; // 编辑模式下忽略
    gameManager?.SetFogOfWarEnabled(pressed);
}
    private void CloseGlobalConfigPanel()
    {
        globalConfigPanel.Visible = false;
        isMenuOpen = false;
    }

    // ✅ 新的统一入口
    private struct EditTarget
    {
        public string TargetType;
        public string DisplayName;
        public object TargetObject;
        public Color Color;
    }

    // 所有可用地形类型
    private readonly GridType[] availableTerrainTypes = new GridType[]
    {
        GridType.GROUND, GridType.FOREST, GridType.ROAD,
        GridType.SEA, GridType.RIVER, GridType.HILL, GridType.METEORITE,
        GridType.PIPE, GridType.LAVA, GridType.BEACH, GridType.TP,
        GridType.REEF, GridType.WHIRLPOOL, GridType.LAVASIDE,
        GridType.SEAFOG, GridType.LANDFOG, GridType.WATERFALL,
        GridType.CLIFF, GridType.SLOPE, GridType.CAVE, GridType.HOLE,
        GridType.PIPESEAM, GridType.TRACK, GridType.STATION,
        GridType.BRIDGE, GridType.LAVABRIDGE, GridType.PASSABLEPIPE,
        GridType.SHIPGATE, GridType.OVERPASS, GridType.BROKENPIPE,
        GridType.RUINS, GridType.BROKENTRACK, GridType.LAVAFOG
    };

    private readonly Dictionary<GridType, Color> terrainColors = new()
    {
        { GridType.GROUND, new Color(0.4f, 0.7f, 0.3f) },
        { GridType.FOREST, new Color(0.1f, 0.5f, 0.1f) },
        { GridType.ROAD, new Color(0.6f, 0.6f, 0.5f) },
        { GridType.SEA, new Color(0.2f, 0.4f, 0.8f) },
        { GridType.RIVER, new Color(0.3f, 0.6f, 0.9f) },
        { GridType.HILL, new Color(0.5f, 0.4f, 0.2f) },
        { GridType.METEORITE, new Color(0.25f, 0.25f, 0.28f) },
        { GridType.PIPE, new Color(0.4f, 0.4f, 0.5f) },
        { GridType.LAVA, new Color(0.9f, 0.2f, 0.1f) },
        { GridType.BEACH, new Color(0.9f, 0.8f, 0.5f) },
        { GridType.TP, new Color(0.8f, 0.2f, 0.8f) },
        { GridType.REEF, new Color(0.2f, 0.7f, 0.6f) },
        { GridType.WHIRLPOOL, new Color(0.1f, 0.3f, 0.5f) },
        { GridType.LAVASIDE, new Color(0.8f, 0.3f, 0.1f) },
        { GridType.SEAFOG, new Color(0.5f, 0.6f, 0.7f) },
        { GridType.LANDFOG, new Color(0.6f, 0.6f, 0.5f) },
        { GridType.WATERFALL, new Color(0.4f, 0.7f, 0.9f) },
        { GridType.CLIFF, new Color(0.5f, 0.5f, 0.4f) },
        { GridType.SLOPE, new Color(0.6f, 0.5f, 0.3f) },
        { GridType.CAVE, new Color(0.3f, 0.25f, 0.2f) },
        { GridType.HOLE, new Color(0.2f, 0.2f, 0.15f) },
        { GridType.PIPESEAM, new Color(0.5f, 0.5f, 0.6f) },
        { GridType.TRACK, new Color(0.4f, 0.4f, 0.4f) },
        { GridType.STATION, new Color(0.6f, 0.6f, 0.5f) },
        { GridType.BRIDGE, new Color(0.5f, 0.5f, 0.6f) },
        { GridType.LAVABRIDGE, new Color(0.8f, 0.2f, 0.1f) },
        { GridType.PASSABLEPIPE, new Color(0.5f, 0.5f, 0.5f) },
        { GridType.SHIPGATE, new Color(0.3f, 0.5f, 0.6f) },
        { GridType.OVERPASS, new Color(0.7f, 0.7f, 0.8f) },
        { GridType.BROKENPIPE, new Color(0.3f, 0.3f, 0.3f) },
        { GridType.RUINS, new Color(0.4f, 0.35f, 0.3f) },
        { GridType.BROKENTRACK, new Color(0.3f, 0.3f, 0.25f) },
        { GridType.LAVAFOG, new Color(0.7f, 0.2f, 0.1f) }
    };

    // ✅ 导出地图配置为 txt（增强版：全图、价值统计、真实资金、Test差异、迷雾状态）
    private void ExportMapToTxt()
    {
        if (gridManager?.map == null)
        {
            return;
        }

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var grid in gridManager.grids)
        {
            if (grid == null) continue;
            minX = System.Math.Min(minX, grid.GridIndex.X);
            minY = System.Math.Min(minY, grid.GridIndex.Y);
            maxX = System.Math.Max(maxX, grid.GridIndex.X);
            maxY = System.Math.Max(maxY, grid.GridIndex.Y);
        }
        if (minX == int.MaxValue) { minX = 0; minY = 0; maxX = 0; maxY = 0; }
        int width = maxX - minX + 1;
        int height = maxY - minY + 1;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== AW-1-3 Map Export ===");
        sb.AppendLine($"Width: {width}");
        sb.AppendLine($"Height: {height}");
        sb.AppendLine($"ExportTime: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        bool fogEnabled = gameManager?.fogOfWarManager?.isFogOfWarEnabled ?? false;
        sb.AppendLine($"FogOfWar: {(fogEnabled ? "Enabled" : "Disabled")}");
        sb.AppendLine();

        sb.AppendLine("=== Terrain Matrix [x,y]=Type ===");
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Grids grid = null;
                if (x >= 0 && x < gridManager.map.GetLength(0) && y >= 0 && y < gridManager.map.GetLength(1))
                    grid = gridManager.map[x, y];
                if (grid != null)
                    sb.Append($"[{x},{y}]={grid.gridType}  ");
                else
                    sb.Append($"[{x},{y}]=NULL  ");
            }
            sb.AppendLine();
        }
        sb.AppendLine();

        sb.AppendLine("=== Units [x,y] Type Team HP ===");
        if (gameManager?.unitManager?.AllUnits != null)
        {
            foreach (var unit in gameManager.unitManager.AllUnits)
            {
                if (unit != null && IsInstanceValid(unit) && unit.grid != null)
                {
                    int hx = unit.grid.GridIndex.X;
                    int hy = unit.grid.GridIndex.Y;
                    string typeName = unit.GetType().Name;
                    int hp = Mathf.CeilToInt(unit.health / 10f);
                    string diff = GetUnitDiffString(unit);
                    sb.AppendLine($"[{hx},{hy}] {typeName} {unit.team} HP{hp}{diff}");
                }
            }
        }
        sb.AppendLine();

        sb.AppendLine("=== Weapons [x,y] Type Team HP ===");
        if (gameManager?.weaponManager?.AllWeapons != null)
        {
            foreach (var weapon in gameManager.weaponManager.AllWeapons)
            {
                if (weapon != null && IsInstanceValid(weapon) && weapon.grid != null)
                {
                    int hx = weapon.grid.GridIndex.X;
                    int hy = weapon.grid.GridIndex.Y;
                    string typeName = weapon.GetType().Name;
                    int hp = Mathf.CeilToInt(weapon.health / 10f);
                    string diff = GetWeaponDiffString(weapon);
                    sb.AppendLine($"[{hx},{hy}] {typeName} {weapon.team} HP{hp}{diff}");
                }
            }
        }
        sb.AppendLine();

        sb.AppendLine("=== Facilities [x,y] Type Team ===");
        if (gridManager?.grids != null)
        {
            foreach (var grid in gridManager.grids)
            {
                if (grid?.city != null && IsInstanceValid(grid.city))
                {
                    int hx = grid.GridIndex.X;
                    int hy = grid.GridIndex.Y;
                    string typeName = grid.city.GetType().Name;
                    string team = grid.city.facilityTeam ?? "None";
                    sb.AppendLine($"[{hx},{hy}] {typeName} {team}");
                }
            }
        }
        sb.AppendLine();

        sb.AppendLine("=== Unit Value Statistics ===");
        long pn1Value = 0, p0Value = 0, p1Value = 0, p2Value = 0;
        if (gameManager?.unitManager?.AllUnits != null)
        {
            foreach (var unit in gameManager.unitManager.AllUnits)
            {
                if (unit != null && IsInstanceValid(unit))
                {
                    switch (unit.team)
                    {
                        case "Player": pn1Value += unit.cost; break;
                        case "Player0": p0Value += unit.cost; break;
                        case "Player1": p1Value += unit.cost; break;
                        case "Player2": p2Value += unit.cost; break;
                    }
                }
            }
        }
        if (gameManager?.weaponManager?.AllWeapons != null)
        {
            foreach (var weapon in gameManager.weaponManager.AllWeapons)
            {
                if (weapon != null && IsInstanceValid(weapon))
                {
                    switch (weapon.team)
                    {
                        case "Player": pn1Value += weapon.cost; break;
                        case "Player0": p0Value += weapon.cost; break;
                        case "Player1": p1Value += weapon.cost; break;
                        case "Player2": p2Value += weapon.cost; break;
                    }
                }
            }
        }
        long totalValue = pn1Value + p0Value + p1Value + p2Value;
        sb.AppendLine($"P-1 (Player):  {pn1Value} G");
        sb.AppendLine($"P0  (Neutral): {p0Value} G");
        sb.AppendLine($"P1  (Red):     {p1Value} G");
        sb.AppendLine($"P2  (Blue):    {p2Value} G");
        sb.AppendLine($"Total:         {totalValue} G");
        sb.AppendLine();

        sb.AppendLine("=== Funds Statistics ===");
        int realP1Funds = gameManager?.p1Funds ?? 0;
        int realP2Funds = gameManager?.p2Funds ?? 0;
        sb.AppendLine($"P1  (Red):     {realP1Funds} G");
        sb.AppendLine($"P2  (Blue):    {realP2Funds} G");
        sb.AppendLine();

        sb.AppendLine("=== End of Export ===");

        string content = sb.ToString();
        string fileName = $"AW13_MapExport_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        string savedPath = "";
        bool savedToDownloads = false;

        try
        {
            string downloadDir = OS.GetSystemDir(OS.SystemDir.Downloads);
            if (!string.IsNullOrEmpty(downloadDir) && Directory.Exists(downloadDir))
            {
                string downloadPath = Path.Combine(downloadDir, fileName);
                File.WriteAllText(downloadPath, content);
                savedPath = downloadPath;
                savedToDownloads = true;
            }
        }
        catch (Exception ex)
        {
        }

        if (!savedToDownloads)
        {
            try
            {
                string userDir = OS.GetUserDataDir();
                string userPath = Path.Combine(userDir, fileName);
                File.WriteAllText(userPath, content);
                savedPath = userPath;
            }
            catch (Exception ex)
            {
            }
        }

        try
        {
            DisplayServer.ClipboardSet(content);
        }
        catch (Exception ex)
        {
        }

        string hintText;
        if (savedToDownloads)
            hintText = $"✅ 已导出到下载目录:\n{savedPath}\n📋 内容已复制到剪贴板";
        else if (!string.IsNullOrEmpty(savedPath))
            hintText = $"⚠️ 已保存到应用目录:\n{savedPath}\n📋 内容已复制到剪贴板\n(手机可在剪贴板粘贴分享)";
        else
            hintText = "❌ 文件保存失败\n📋 但内容已复制到剪贴板\n请手动粘贴保存";

        var hintLabel = new Label();
        hintLabel.Text = hintText;
        hintLabel.AddThemeFontSizeOverride("font_size", 13);
        hintLabel.AddThemeColorOverride("font_color", savedToDownloads ? new Color(0.3f, 1f, 0.3f) : new Color(1f, 0.8f, 0.2f));
        hintLabel.HorizontalAlignment = HorizontalAlignment.Center;
        hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hintLabel.Position = new Vector2(0, globalConfigPanel.Size.Y - 80);
        hintLabel.Size = new Vector2(globalConfigPanel.Size.X, 70);
        globalConfigPanel.AddChild(hintLabel);

        var timer = new Timer();
        timer.WaitTime = 3.0f;
        timer.OneShot = true;
        timer.Timeout += () => { hintLabel.QueueFree(); timer.QueueFree(); };
        globalConfigPanel.AddChild(timer);
        timer.Start();
    }

    // ========== ✅ 模板差异比较 ==========
    private Dictionary<string, object> _templateInstanceCache = new();

    private object GetTemplateInstance(Type type, string scenePath)
    {
        string key = $"{type.Name}:{scenePath}";
        if (_templateInstanceCache.ContainsKey(key)) return _templateInstanceCache[key];

        var scene = GD.Load<PackedScene>(scenePath);
        if (scene == null) return null;

        var instance = scene.Instantiate();
        if (instance == null || !type.IsInstanceOfType(instance))
        {
            instance?.QueueFree();
            return null;
        }
        _templateInstanceCache[key] = instance;
        return instance;
    }

    private string GetUnitDiffString(Infantry unit)
    {
        string scenePath = GetScenePathForUnitType(unit.GetType().Name);
        if (string.IsNullOrEmpty(scenePath)) return "";

        var template = GetTemplateInstance(unit.GetType(), scenePath);
        if (template == null) return "";

        var diffs = GetDiffs(unit, template);
        if (diffs.Count == 0) return "";
        return "{Test:true;" + string.Join(";", diffs) + "}";
    }

    private string GetWeaponDiffString(Weapon weapon)
    {
        string scenePath = GetScenePathForWeaponType(weapon.GetType().Name);
        if (string.IsNullOrEmpty(scenePath)) return "";

        var template = GetTemplateInstance(weapon.GetType(), scenePath);
        if (template == null) return "";

        var diffs = GetDiffs(weapon, template);
        if (diffs.Count == 0) return "";
        return "{Test:true;" + string.Join(";", diffs) + "}";
    }

    private string GetFacilityDiffString(Facility facility)
    {
        string scenePath = GetScenePathForFacilityType(facility.GetType().Name);
        if (string.IsNullOrEmpty(scenePath)) return "";

        var template = GetTemplateInstance(facility.GetType(), scenePath);
        if (template == null) return "";

        var diffs = GetDiffs(facility, template);
        if (diffs.Count == 0) return "";
        return "{Test:true;" + string.Join(";", diffs) + "}";
    }

    private List<string> GetDiffs(object instance, object template)
    {
        var diffs = new List<string>();
        var type = instance.GetType();

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.GetCustomAttribute<ExportAttribute>() != null);
        foreach (var field in fields)
        {
            var v1 = field.GetValue(instance);
            var v2 = field.GetValue(template);
            if (!AreValuesEqual(v1, v2))
                diffs.Add($"{field.Name}:{FormatValue(v1)}");
        }

        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ExportAttribute>() != null && p.CanRead && p.CanWrite);
        foreach (var prop in props)
        {
            var v1 = prop.GetValue(instance);
            var v2 = prop.GetValue(template);
            if (!AreValuesEqual(v1, v2))
                diffs.Add($"{prop.Name}:{FormatValue(v1)}");
        }

        return diffs;
    }

    private bool AreValuesEqual(object a, object b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.GetType() != b.GetType()) return false;
        if (a is Godot.Collections.Dictionary dictA && b is Godot.Collections.Dictionary dictB)
            return dictA.Count == dictB.Count;
        if (a is Godot.Collections.Array arrA && b is Godot.Collections.Array arrB)
            return arrA.Count == arrB.Count;
        return a.Equals(b);
    }

    private string FormatValue(object val)
    {
        if (val == null) return "null";
        if (val is bool b) return b ? "true" : "false";
        if (val is float f) return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (val is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return val.ToString();
    }

    private string GetScenePathForUnitType(string typeName)
    {
        var tmpl = unitTemplates.FirstOrDefault(t => System.IO.Path.GetFileNameWithoutExtension(t.ScenePath).Replace("_", "").Replace("(1)", "").Equals(typeName, StringComparison.OrdinalIgnoreCase));
        if (tmpl != null) return tmpl.ScenePath;
        tmpl = unitTemplates.FirstOrDefault(t => t.ScenePath.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0);
        return tmpl?.ScenePath ?? "";
    }

    private string GetScenePathForWeaponType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return "";

        // ✅ 显式 AWME 类型名映射（避免 IndexOf 模糊匹配）
        var awmeNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["black_cannon"] = "res://Prefabs/black_cannon.tscn",
            ["blackcannon"] = "res://Prefabs/black_cannon.tscn",
            ["BlackCannon"] = "res://Prefabs/black_cannon.tscn",
            ["large_cannon"] = "res://Prefabs/large_cannon.tscn",
            ["largecannon"] = "res://Prefabs/large_cannon.tscn",
            ["LargeCannon"] = "res://Prefabs/large_cannon.tscn",
            ["Laser"] = "res://Prefabs/Laser.tscn",
            ["death_ray"] = "res://Prefabs/death_ray.tscn",
            ["DeathRay"] = "res://Prefabs/death_ray.tscn",
            ["Crystal"] = "res://Prefabs/crystal.tscn",
            ["crystal"] = "res://Prefabs/crystal.tscn",
        };
        if (awmeNameMap.TryGetValue(typeName, out string explicitPath))
        {
            return explicitPath;
        }

        var tmpl = weaponTemplates.FirstOrDefault(t => System.IO.Path.GetFileNameWithoutExtension(t.ScenePath).Replace("_", "").Equals(typeName, StringComparison.OrdinalIgnoreCase));
        if (tmpl != null) return tmpl.ScenePath;
        tmpl = weaponTemplates.FirstOrDefault(t => t.ScenePath.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0);
        return tmpl?.ScenePath ?? "";
    }

    private string GetScenePathForFacilityType(string typeName)
    {
        var tmpl = facilityTemplates.FirstOrDefault(t => System.IO.Path.GetFileNameWithoutExtension(t.ScenePath).Replace("_", "").Equals(typeName, StringComparison.OrdinalIgnoreCase));
        if (tmpl != null) return tmpl.ScenePath;
        tmpl = facilityTemplates.FirstOrDefault(t => t.ScenePath.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0);
        return tmpl?.ScenePath ?? "";
    }

    // ========== ✅ 地图导入 ==========
    private void OpenImportDialog()
    {
        var dialog = new Panel();
        dialog.Name = "ImportDialog";
        dialog.CustomMinimumSize = new Vector2(420, 320);
        dialog.SetAnchorsPreset(LayoutPreset.Center);
        dialog.ZIndex = 1400;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.08f, 0.1f, 0.15f, 0.98f);
        panelStyle.SetCornerRadiusAll(16);
        panelStyle.SetBorderWidthAll(3);
        panelStyle.BorderColor = new Color(0.5f, 0.7f, 0.9f);
        dialog.AddThemeStyleboxOverride("panel", panelStyle);

        var title = new Label();
        title.Text = "📥 导入地图配置";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", new Color(0.4f, 0.9f, 1f));
        title.CustomMinimumSize = new Vector2(0, 40);
        title.Position = new Vector2(0, 15);
        title.Size = new Vector2(420, 40);
        dialog.AddChild(title);

        var closeBtn = new Button();
        closeBtn.Text = "✕";
        closeBtn.CustomMinimumSize = new Vector2(32, 32);
        closeBtn.Position = new Vector2(380, 12);
        closeBtn.Pressed += () => dialog.QueueFree();
        dialog.AddChild(closeBtn);

        var vbox = new VBoxContainer();
        vbox.Position = new Vector2(30, 70);
        vbox.Size = new Vector2(360, 220);
        vbox.AddThemeConstantOverride("separation", 12);

        var fileBtn = new Button();
        fileBtn.Text = "📁 选择 txt 文件导入";
        fileBtn.CustomMinimumSize = new Vector2(360, 45);
        fileBtn.AddThemeFontSizeOverride("font_size", 14);
        fileBtn.Pressed += () => {
            dialog.QueueFree();
            OpenFileImportDialog();
        };
        vbox.AddChild(fileBtn);

        var clipBtn = new Button();
        clipBtn.Text = "📋 从剪贴板导入";
        clipBtn.CustomMinimumSize = new Vector2(360, 45);
        clipBtn.AddThemeFontSizeOverride("font_size", 14);
        clipBtn.Pressed += () => {
            dialog.QueueFree();
            ImportMapFromClipboard();
        };
        vbox.AddChild(clipBtn);

        var hint = new Label();
        hint.Text = "提示：导入会清空当前所有单位、兵器和设施，\n并重建地图。请确保已导出备份！";
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.AddThemeColorOverride("font_color", new Color(0.8f, 0.6f, 0.3f));
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(hint);

        dialog.AddChild(vbox);
        AddChild(dialog);
    }

    private void OpenFileImportDialog()
    {
        var fd = new FileDialog();
        fd.FileMode = FileDialog.FileModeEnum.OpenFile;
        fd.Access = FileDialog.AccessEnum.Filesystem;
        fd.Filters = new string[] { "*.txt ; Text Files" };
        fd.Title = "选择地图导出文件";
        fd.Size = new Vector2I(800, 600);
        fd.FileSelected += (path) => {
            try
            {
                string content = File.ReadAllText(path);
                ShowImportConfirmDialog(content);
            }
            catch (Exception ex)
            {
                ShowImportHint($"❌ 读取文件失败:\n{ex.Message}");
            }
            fd.QueueFree();
        };
        fd.Canceled += () => fd.QueueFree();
        AddChild(fd);
        fd.PopupCentered();
    }

    private void ImportMapFromClipboard()
    {
        string content = DisplayServer.ClipboardGet();
        if (string.IsNullOrWhiteSpace(content))
        {
            ShowImportHint("❌ 剪贴板为空或无法读取");
            return;
        }
        ShowImportConfirmDialog(content);
    }

    private void ShowImportConfirmDialog(string content)
    {
        var confirm = new ConfirmationDialog();
        confirm.Title = "⚠️ 确认导入";
        confirm.DialogText = "导入将清空当前所有单位、兵器和设施，\n并用文件内容重建地图。\n\n是否继续？";
        confirm.Size = new Vector2I(400, 180);
        confirm.Confirmed += () => {
            ParseAndImportMap(content);
            confirm.QueueFree();
        };
        confirm.Canceled += () => confirm.QueueFree();
        AddChild(confirm);
        confirm.PopupCentered();
    }

    private void ShowImportHint(string text)
    {
        var hint = new Label();
        hint.Text = text;
        hint.AddThemeFontSizeOverride("font_size", 13);
        hint.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.2f));
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.Position = new Vector2(0, globalConfigPanel.Size.Y - 80);
        hint.Size = new Vector2(globalConfigPanel.Size.X, 70);
        globalConfigPanel.AddChild(hint);
        var t = new Timer();
        t.WaitTime = 3.0f;
        t.OneShot = true;
        t.Timeout += () => { hint.QueueFree(); t.QueueFree(); };
        globalConfigPanel.AddChild(t);
        t.Start();
    }

    private void ClearAllMapEntities()
    {
        // ✅ 批量清理，避免逐个调用 gameManager.RemoveUnit 触发胜利检查
        
        // ✅ 关键修复：清理已失败势力记录，避免导入后延迟判定失败
        if (gameManager != null)
        {
            gameManager.ResetForMapImport();
        }
        
        // ✅ 导入后自动进入编辑模式
        if (!IsEditMode)
        {
            SetEditMode(true);
        }
        
        // 1. 清理单位 - 直接从 units 子节点遍历，确保包含所有对象
        if (gameManager?.unitManager?.units != null)
        {
            var allChildren = gameManager.unitManager.units.GetChildren().ToList();
            foreach (var child in allChildren)
            {
                if (child is Infantry unit)
                {
                    if (IsInstanceValid(unit))
                    {
                        gameManager.unitCategories.Remove(unit);
                        if (unit.grid != null)
                        {
                            unit.grid.infantries.Remove(unit);
                            if (unit.grid.infantry == unit)
                                unit.grid.infantry = unit.grid.infantries.Count > 0 ? unit.grid.infantries[0] : null;
                        }
                        // ✅ 使用 Free() 立即释放，避免 QueueFree 延迟导致遍历到废弃对象
                        unit.Free();
                    }
                    else
                    {
                        try { gameManager.unitManager.units.RemoveChild(unit); } catch { }
                    }
                }
            }
            gameManager.unitManager.AllUnits.Clear();
        }

        // 2. 清理兵器
        if (gameManager?.weaponManager?.weaponsNode != null)
        {
            var allWeapons = gameManager.weaponManager.weaponsNode.GetChildren().ToList();
            foreach (var child in allWeapons)
            {
                if (child is Weapon weapon)
                {
                    if (IsInstanceValid(weapon))
                    {
                        if (weapon.grid != null)
                        {
                            weapon.grid.weapons.Remove(weapon);
                            if (weapon.grid.weapon == weapon)
                                weapon.grid.weapon = null;
                        }
                        weapon.Free();
                    }
                    else
                    {
                        try { gameManager.weaponManager.weaponsNode.RemoveChild(weapon); } catch { }
                    }
                }
            }
            gameManager.weaponManager.AllWeapons.Clear();
        }

        // 3. 清理所有格子的引用
        if (gridManager?.grids != null)
        {
            foreach (var grid in gridManager.grids)
            {
                if (grid == null) continue;
                grid.infantries.Clear();
                grid.infantry = null;
                grid.weapons.Clear();
                grid.weapon = null;
                if (grid.city != null && IsInstanceValid(grid.city))
                {
                    grid.city.Free();
                }
                grid.city = null;
            }
        }

        // ✅ 重置 GameManager 的选中状态，防止引用已删除的单位
        if (gameManager != null)
        {
            gameManager.selectedInfantry = null;
            gameManager.selectedWeapon = null;
        }

    }

    private void ParseAndImportMap(string content)
    {
        try
        {
            if (gameManager != null) gameManager.blockVictoryCheck = true; // ✅ 屏蔽胜利检查
            ClearAllMapEntities();

            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            // ✅ 先扫描一遍计算地图尺寸
            int mapWidth = 0, mapHeight = 0;
            string scanSection = "";
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("=== ") && line.EndsWith(" ==="))
                {
                    scanSection = line.Substring(4, line.Length - 8).Trim();
                    continue;
                }

                if (scanSection == "Terrain Matrix [x,y]=Type")
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(part, @"\[(\d+),(\d+)\]=(.+?)");
                        if (m.Success)
                        {
                            int tx = int.Parse(m.Groups[1].Value);
                            int ty = int.Parse(m.Groups[2].Value);
                            mapWidth = Math.Max(mapWidth, tx + 1);
                            mapHeight = Math.Max(mapHeight, ty + 1);
                        }
                    }
                }
                else if (scanSection == "Units [x,y] Type Team HP" || scanSection == "Weapons [x,y] Type Team HP" || scanSection == "Facilities [x,y] Type Team")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d+),(\d+)\]");
                    if (m.Success)
                    {
                        int x = int.Parse(m.Groups[1].Value);
                        int y = int.Parse(m.Groups[2].Value);
                        mapWidth = Math.Max(mapWidth, x + 1);
                        mapHeight = Math.Max(mapHeight, y + 1);
                    }
                }
            }

            if (mapWidth > 0 && mapHeight > 0)
            {
                gridManager?.ResizeMap(mapWidth, mapHeight);
                // ✅ 关键：ResizeMap 后必须重新获取 grids 引用
                gridManager?.Init();
            }

            int lineIdx = 0;
            string section = "";

            while (lineIdx < lines.Length)
            {
                string line = lines[lineIdx].Trim();
                lineIdx++;

                if (line.StartsWith("=== ") && line.EndsWith(" ==="))
                {
                    section = line.Substring(4, line.Length - 8).Trim();
                    continue;
                }
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (section == "Terrain Matrix [x,y]=Type")
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(part, @"\[(\d+),(\d+)\]=(.+?)");
                        if (m.Success)
                        {
                            int tx = int.Parse(m.Groups[1].Value);
                            int ty = int.Parse(m.Groups[2].Value);
                            string tType = m.Groups[3].Value.Trim();
                            if (tx >= 0 && tx < gridManager.map.GetLength(0) && ty >= 0 && ty < gridManager.map.GetLength(1))
                            {
                                var g = gridManager.map[tx, ty];
                                if (g != null)
                                {
                                    if (Enum.TryParse<GridType>(tType, out var gt))
                                    {
                                        g.gridType = gt;
                                    }
                                    else
                                    {
                                        // ✅ 无法解析的地形类型默认 GROUND，并记录日志
                                        g.gridType = GridType.GROUND;
                                    }
                                }
                            }
                        }
                    }
                }
                else if (section == "Units [x,y] Type Team HP")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d+),(\d+)\]\s+(\w+)\s+(\w+)\s+HP(\d+)(.*)");
                    if (m.Success)
                    {
                        int ux = int.Parse(m.Groups[1].Value);
                        int uy = int.Parse(m.Groups[2].Value);
                        string uType = m.Groups[3].Value;
                        string uTeam = m.Groups[4].Value;
                        int uHp = int.Parse(m.Groups[5].Value);
                        string diffPart = m.Groups[6].Value;

                        string scenePath = GetScenePathForUnitType(uType);
                        if (!string.IsNullOrEmpty(scenePath) && ux >= 0 && ux < gridManager.map.GetLength(0) && uy >= 0 && uy < gridManager.map.GetLength(1))
                        {
                            var grid = gridManager.map[ux, uy];
                            if (grid != null)
                            {
                                SpawnUnitAtGrid(grid, scenePath, uTeam);
                                var spawned = grid.infantries.LastOrDefault();
                                if (spawned != null)
                                {
                                    spawned.health = uHp * 10;
                                    spawned.UpdateHpLabel();
                                    ApplyDiffString(spawned, diffPart);
                                }
                            }
                        }
                    }
                }
                else if (section == "Weapons [x,y] Type Team HP")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d+),(\d+)\]\s+(\w+)\s+(\w+)\s+HP(\d+)(.*)");
                    if (m.Success)
                    {
                        int wx = int.Parse(m.Groups[1].Value);
                        int wy = int.Parse(m.Groups[2].Value);
                        string wType = m.Groups[3].Value;
                        string wTeam = m.Groups[4].Value;
                        int wHp = int.Parse(m.Groups[5].Value);
                        string diffPart = m.Groups[6].Value;

                        string scenePath = GetScenePathForWeaponType(wType);
                        if (!string.IsNullOrEmpty(scenePath) && wx >= 0 && wx < gridManager.map.GetLength(0) && wy >= 0 && wy < gridManager.map.GetLength(1))
                        {
                            var grid = gridManager.map[wx, wy];
                            if (grid != null)
                            {
                                SpawnWeaponAtGrid(grid, scenePath, wTeam);
                                var spawned = grid.weapon;
                                if (spawned != null)
                                {
                                    spawned.health = wHp * 10;
                                    spawned.UpdateHpLabel();
                                    ApplyDiffString(spawned, diffPart);
                                }
                            }
                        }
                    }
                }
                else if (section == "Facilities [x,y] Type Team")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d+),(\d+)\]\s+(\w+)\s+(\w+)");
                    if (m.Success)
                    {
                        int fx = int.Parse(m.Groups[1].Value);
                        int fy = int.Parse(m.Groups[2].Value);
                        string fType = m.Groups[3].Value;
                        string fTeam = m.Groups[4].Value;

                        string scenePath = GetScenePathForFacilityType(fType);
                        if (!string.IsNullOrEmpty(scenePath) && fx >= 0 && fx < gridManager.map.GetLength(0) && fy >= 0 && fy < gridManager.map.GetLength(1))
                        {
                            var grid = gridManager.map[fx, fy];
                            if (grid != null)
                                SpawnFacilityAtGrid(grid, scenePath, fTeam);
                        }
                    }
                }
                else if (section == "Funds Statistics")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"P1\s+\(Red\):\s*(\d+)");
                    if (m.Success && gameManager != null) gameManager.p1Funds = int.Parse(m.Groups[1].Value);
                    m = System.Text.RegularExpressions.Regex.Match(line, @"P2\s+\(Blue\):\s*(\d+)");
                    if (m.Success && gameManager != null) gameManager.p2Funds = int.Parse(m.Groups[1].Value);
                }
            }

            if (gameManager != null) gameManager.blockVictoryCheck = false; // ✅ 恢复胜利检查
            gameManager?.unitManager?.RefreshUnitList();
            gameManager?.UpdateUnitLists();
            gameManager?.RefreshSpecializedUnitLists();
            gameManager?.fogOfWarManager?.RefreshFog();
            gameManager?.ResetGameState(); // ✅ 重置游戏状态，防止胜利面板残留
            ShowImportHint("✅ 地图导入成功！");
        }
        catch (Exception ex)
        {
            if (gameManager != null) gameManager.blockVictoryCheck = false; // ✅ 异常时也要恢复
            ShowImportHint($"❌ 导入失败:\n{ex.Message}");
        }
    }

    private void ApplyDiffString(object target, string diffPart)
    {
        if (target == null || string.IsNullOrWhiteSpace(diffPart)) return;

        // 使用嵌套花括号计数器提取 Test 内容
        int startIdx = diffPart.IndexOf("{Test:");
        if (startIdx < 0) return;
        startIdx += 6; // 跳过 "{Test:"

        int braceCount = 1;
        int endIdx = startIdx;
        while (endIdx < diffPart.Length && braceCount > 0)
        {
            if (diffPart[endIdx] == '{') braceCount++;
            else if (diffPart[endIdx] == '}') braceCount--;
            endIdx++;
        }
        if (braceCount != 0) return; // 花括号不匹配，跳过

        string inner = diffPart.Substring(startIdx, endIdx - startIdx - 1);
        if (string.IsNullOrWhiteSpace(inner)) return;

        var items = inner.Split(';');
        var type = target.GetType();
        int successCount = 0;
        int skipCount = 0;

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item)) continue;

            var kv = item.Split(new[] { ':' }, 2);
            if (kv.Length < 2) continue;
            string key = kv[0].Trim();
            string valStr = kv[1].Trim();
            if (string.IsNullOrEmpty(key) || key == "true") continue;

            // 跳过中文字段名
            if (ContainsChinese(key)) { skipCount++; continue; }

            // ✅ 值层面过滤：跳过节点引用/贴图/空集合/复杂值
            if (ShouldSkipValue(valStr)) { skipCount++; continue; }

            var field = type.GetField(key, BindingFlags.Public | BindingFlags.Instance);
            if (field != null && field.GetCustomAttribute<ExportAttribute>() != null)
            {
                if (!IsSupportedType(field.FieldType)) { skipCount++; continue; }
                try
                {
                    object val = ParseValue(valStr, field.FieldType);
                    if (val != null) { field.SetValue(target, val); successCount++; }
                }
                catch { skipCount++; }
            }
            else
            {
                var prop = type.GetProperty(key, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.GetCustomAttribute<ExportAttribute>() != null && prop.CanWrite)
                {
                    if (!IsSupportedType(prop.PropertyType)) { skipCount++; continue; }
                    try
                    {
                        object val = ParseValue(valStr, prop.PropertyType);
                        if (val != null) { prop.SetValue(target, val); successCount++; }
                    }
                    catch { skipCount++; }
                }
            }
        }

        // 只打印一次总结，不打印每个字段
        if (successCount > 0 || skipCount > 0);
    }

    // ✅ 值层面：跳过节点引用/贴图/空集合等不需要导入的值
    private bool ShouldSkipValue(string valStr)
    {
        if (string.IsNullOrEmpty(valStr)) return true;
        // 节点引用格式：<NodeName#ID>
        if (valStr.StartsWith("<") && valStr.EndsWith(">")) return true;
        // Godot 集合类型格式（如 Dictionary、Array 的 ToString）
        if (valStr == "{}" || valStr == "[]" || valStr == "{ }" || valStr == "[ ]") return true;
        // 包含空字典或空数组的格式
        if (valStr.Contains("<")) return true; // 任何包含 < 的（节点引用）
        return false;
    }

    // ✅ 检测字符串是否包含中文
    private bool ContainsChinese(string str)
    {
        if (string.IsNullOrEmpty(str)) return false;
        foreach (char c in str)
        {
            if (c >= 0x4E00 && c <= 0x9FFF) return true; // CJK 统一表意文字
            if (c >= 0x3400 && c <= 0x4DBF) return true; // CJK 扩展A
            if (c >= 0x3000 && c <= 0x303F) return true; // CJK 标点符号
        }
        return false;
    }

    // ✅ 判断类型是否为支持的基本类型
    private bool IsSupportedType(Type t)
    {
        if (t == null) return false;
        if (t == typeof(bool)) return true;
        if (t == typeof(int)) return true;
        if (t == typeof(uint)) return true;
        if (t == typeof(long)) return true;
        if (t == typeof(float)) return true;
        if (t == typeof(double)) return true;
        if (t == typeof(string)) return true;
        if (t.IsEnum) return true;
        if (t == typeof(Color)) return true;
        if (t == typeof(Vector2)) return true;
        if (t == typeof(Vector2I)) return true;
        if (t == typeof(Vector3)) return true;
        if (t == typeof(Vector3I)) return true;
        return false;
    }

    private object ParseValue(string str, Type targetType)
    {
        if (targetType == null || str == null) return null;

        // ✅ 如果是字符串类型，直接返回（包括中文内容）
        if (targetType == typeof(string)) return str;

        if (targetType == typeof(bool))
        {
            if (bool.TryParse(str, out bool bv)) return bv;
            if (str == "1" || str.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (str == "0" || str.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }
        if (targetType == typeof(int))
        {
            if (int.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out int iv)) return iv;
            return null;
        }
        if (targetType == typeof(uint))
        {
            if (uint.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out uint uv)) return uv;
            return null;
        }
        if (targetType == typeof(long))
        {
            if (long.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out long lv)) return lv;
            return null;
        }
        if (targetType == typeof(float))
        {
            if (float.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float fv)) return fv;
            return null;
        }
        if (targetType == typeof(double))
        {
            if (double.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dv)) return dv;
            return null;
        }
        if (targetType.IsEnum)
        {
            if (int.TryParse(str, out int ev)) return ev;
            if (Enum.TryParse(targetType, str, true, out object ev2)) return ev2;
            return null;
        }
        // ✅ Godot 颜色类型解析 (R,G,B,A) 或 Color 构造函数
        if (targetType == typeof(Color))
        {
            // 尝试从 Godot 的 ToString 格式解析，如 "Color(1, 0, 0, 1)"
            var colorMatch = System.Text.RegularExpressions.Regex.Match(str, @"Color\(([-\d.]+),\s*([-\d.]+),\s*([-\d.]+)(?:,\s*([-\d.]+))?\)");
            if (colorMatch.Success)
            {
                float r = float.Parse(colorMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                float g = float.Parse(colorMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                float b = float.Parse(colorMatch.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                float a = colorMatch.Groups[4].Success ? float.Parse(colorMatch.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture) : 1.0f;
                return new Color(r, g, b, a);
            }
            // 尝试从 HTML 颜色格式解析（如 #FF0000）
            try
            {
                if (str.StartsWith("#"))
                {
                    return new Color(str);
                }
            }
            catch { }
            return null;
        }
        // ✅ Godot Vector2 类型解析
        if (targetType == typeof(Vector2))
        {
            var vec2Match = System.Text.RegularExpressions.Regex.Match(str, @"\(([-\d.]+),\s*([-\d.]+)\)");
            if (vec2Match.Success)
            {
                float x = float.Parse(vec2Match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                float y = float.Parse(vec2Match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                return new Vector2(x, y);
            }
            return null;
        }
        // ✅ Godot Vector2I 类型解析
        if (targetType == typeof(Vector2I))
        {
            var vec2iMatch = System.Text.RegularExpressions.Regex.Match(str, @"\(([-\d.]+),\s*([-\d.]+)\)");
            if (vec2iMatch.Success)
            {
                int x = int.Parse(vec2iMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                int y = int.Parse(vec2iMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                return new Vector2I(x, y);
            }
            return null;
        }
        // ✅ Godot Vector3 类型解析
        if (targetType == typeof(Vector3))
        {
            var vec3Match = System.Text.RegularExpressions.Regex.Match(str, @"\(([-\d.]+),\s*([-\d.]+),\s*([-\d.]+)\)");
            if (vec3Match.Success)
            {
                float x = float.Parse(vec3Match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                float y = float.Parse(vec3Match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                float z = float.Parse(vec3Match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                return new Vector3(x, y, z);
            }
            return null;
        }
        // ✅ Godot Vector3I 类型解析
        if (targetType == typeof(Vector3I))
        {
            var vec3iMatch = System.Text.RegularExpressions.Regex.Match(str, @"\(([-\d.]+),\s*([-\d.]+),\s*([-\d.]+)\)");
            if (vec3iMatch.Success)
            {
                int x = int.Parse(vec3iMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                int y = int.Parse(vec3iMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                int z = int.Parse(vec3iMatch.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                return new Vector3I(x, y, z);
            }
            return null;
        }
        return null;
    }

    // ========== ✅ 极简/紧凑导出格式 ==========

    private readonly Dictionary<string, string> compactUnitAbbr = new()
    {
        { "Infantry", "Inf" }, { "Mech", "Mch" }, { "Oozium", "Ooz" },
        { "LightTank", "LTk" }, { "MdTank", "MTk" }, { "Rocket", "Rck" },
        { "Artillery", "Art" }, { "APC", "APC" }, { "Recon", "Rec" },
        { "AntiAir", "AA" }, { "AntiTank", "AT" }, { "Flare", "Flr" },
        { "Bike", "Bke" }, { "FlyBomb", "FBo" },
        { "PipeRunner", "PRun" }
    };
    private readonly Dictionary<string, string> compactWeaponAbbr = new()
    {
        { "BlackCannon", "BCn" }, { "Laser", "Lsr" }, { "Crystal", "Cry" }
    };
    private readonly Dictionary<string, string> compactFacilityAbbr = new()
    {
        { "City", "Cty" }, { "Base", "Bse" }, { "AirPort", "Apt" }
    };
    private readonly Dictionary<string, string> compactTeamAbbr = new()
    {
        { "Player1", "P1" }, { "Player2", "P2" }, { "Player0", "P0" }, { "Player", "PN" }
    };

    // 反向映射（缩写→完整名）
    private string AbbrToUnitType(string abbr) => compactUnitAbbr.FirstOrDefault(kv => kv.Value == abbr).Key ?? abbr;
    private string AbbrToWeaponType(string abbr) => compactWeaponAbbr.FirstOrDefault(kv => kv.Value == abbr).Key ?? abbr;
    private string AbbrToFacilityType(string abbr) => compactFacilityAbbr.FirstOrDefault(kv => kv.Value == abbr).Key ?? abbr;
    private string AbbrToTeam(string abbr) => compactTeamAbbr.FirstOrDefault(kv => kv.Value == abbr).Key ?? abbr;

    private string UnitTypeToAbbr(string typeName) => compactUnitAbbr.GetValueOrDefault(typeName, typeName);
    private string WeaponTypeToAbbr(string typeName) => compactWeaponAbbr.GetValueOrDefault(typeName, typeName);
    private string FacilityTypeToAbbr(string typeName) => compactFacilityAbbr.GetValueOrDefault(typeName, typeName);
    private string TeamToAbbr(string team) => compactTeamAbbr.GetValueOrDefault(team, team);

    private int TerrainToNumber(GridType gt) => (int)gt + 1;
    private GridType NumberToTerrain(int num)
    {
        var values = Enum.GetValues<GridType>();
        if (num >= 1 && num <= values.Length) return values[num - 1];
        return GridType.GROUND; // 默认
    }

    // ✅ 极简导出
    private void ExportCompactMap()
    {
        if (gridManager?.map == null) { ShowImportHint("❌ GridManager 未初始化"); return; }

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var grid in gridManager.grids)
        { if (grid == null) continue; minX = Math.Min(minX, grid.GridIndex.X); minY = Math.Min(minY, grid.GridIndex.Y); maxX = Math.Max(maxX, grid.GridIndex.X); maxY = Math.Max(maxY, grid.GridIndex.Y); }
        if (minX == int.MaxValue) { minX = 0; minY = 0; maxX = 0; maxY = 0; }
        int width = maxX - minX + 1, height = maxY - minY + 1;
        bool fog = gameManager?.fogOfWarManager?.isFogOfWarEnabled ?? false;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== AW Compact ===");
        sb.AppendLine($"W:{width} H:{height} F:{(fog ? 1 : 0)}");
        sb.AppendLine();

        sb.AppendLine("=== T ===");
        for (int y = minY; y <= maxY; y++)
        {
            sb.Append($"{y}:");
            for (int x = minX; x <= maxX; x++)
            {
                var grid = (x >= 0 && x < gridManager.map.GetLength(0) && y >= 0 && y < gridManager.map.GetLength(1)) ? gridManager.map[x, y] : null;
                int num = grid != null ? TerrainToNumber(grid.gridType) : 0;
                sb.Append($" {num}");
            }
            sb.AppendLine();
        }
        sb.AppendLine();

        sb.AppendLine("=== U ===");
        if (gameManager?.unitManager?.AllUnits != null)
        {
            foreach (var unit in gameManager.unitManager.AllUnits)
            {
                if (unit == null || !IsInstanceValid(unit) || unit.grid == null) continue;
                int hx = unit.grid.GridIndex.X, hy = unit.grid.GridIndex.Y;
                string abbr = UnitTypeToAbbr(unit.GetType().Name);
                string team = TeamToAbbr(unit.team);
                int hp = Mathf.CeilToInt(unit.health / 10f);
                string diff = GetUnitDiffString(unit);
                sb.AppendLine($"[{hx},{hy}] {abbr} {team} {hp}{diff}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("=== W ===");
        if (gameManager?.weaponManager?.AllWeapons != null)
        {
            foreach (var weapon in gameManager.weaponManager.AllWeapons)
            {
                if (weapon == null || !IsInstanceValid(weapon) || weapon.grid == null) continue;
                int hx = weapon.grid.GridIndex.X, hy = weapon.grid.GridIndex.Y;
                string abbr = WeaponTypeToAbbr(weapon.GetType().Name);
                string team = TeamToAbbr(weapon.team);
                int hp = Mathf.CeilToInt(weapon.health / 10f);
                string diff = GetWeaponDiffString(weapon);
                sb.AppendLine($"[{hx},{hy}] {abbr} {team} {hp}{diff}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("=== F ===");
        if (gridManager?.grids != null)
        {
            foreach (var grid in gridManager.grids)
            {
                if (grid?.city == null || !IsInstanceValid(grid.city)) continue;
                int hx = grid.GridIndex.X, hy = grid.GridIndex.Y;
                string abbr = FacilityTypeToAbbr(grid.city.GetType().Name);
                string team = TeamToAbbr(grid.city.facilityTeam ?? "Player0");
                string diff = GetFacilityDiffString(grid.city);
                sb.AppendLine($"[{hx},{hy}] {abbr} {team}{diff}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("=== $ ===");
        sb.AppendLine($"P1:{gameManager?.p1Funds ?? 0}");
        sb.AppendLine($"P2:{gameManager?.p2Funds ?? 0}");
        sb.AppendLine();
        sb.AppendLine("=== E ===");

        string content = sb.ToString();
        string fileName = $"AW13_Compact_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        try
        {
            string dir = OS.GetSystemDir(OS.SystemDir.Downloads);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                string path = Path.Combine(dir, fileName);
                File.WriteAllText(path, content);
                DisplayServer.ClipboardSet(content);
                ShowImportHint($"✅ 极简导出成功！\n📁 {path}\n📋 已复制到剪贴板");
                return;
            }
        }
        catch { }
        try
        {
            string path = Path.Combine(OS.GetUserDataDir(), fileName);
            File.WriteAllText(path, content);
            DisplayServer.ClipboardSet(content);
            ShowImportHint($"✅ 极简导出成功！\n📁 {path}\n📋 已复制到剪贴板");
        }
        catch (Exception ex) { ShowImportHint($"❌ 导出失败: {ex.Message}"); }
    }

    // ✅ 极简导入
    private void ParseCompactMap(string content)
    {
        try
        {
            if (gameManager != null) gameManager.blockVictoryCheck = true;
            ClearAllMapEntities();

            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            // ✅ 先扫描一遍计算地图尺寸
            int mapWidth = 0, mapHeight = 0;
            string scanSection = "";
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("=== ") && line.EndsWith(" ==="))
                {
                    scanSection = line.Substring(4, line.Length - 8).Trim();
                    continue;
                }
                if (scanSection == "T")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"^(\d+):\s+(.+)");
                    if (m.Success)
                    {
                        int y = int.Parse(m.Groups[1].Value);
                        var nums = m.Groups[2].Value.Trim().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                        mapWidth = Math.Max(mapWidth, nums.Length);
                        mapHeight = Math.Max(mapHeight, y + 1);
                    }
                }
                else if (scanSection == "U" || scanSection == "W" || scanSection == "F")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d+),(\d+)\]");
                    if (m.Success)
                    {
                        int x = int.Parse(m.Groups[1].Value);
                        int y = int.Parse(m.Groups[2].Value);
                        mapWidth = Math.Max(mapWidth, x + 1);
                        mapHeight = Math.Max(mapHeight, y + 1);
                    }
                }
            }

            if (mapWidth > 0 && mapHeight > 0)
            {
                gridManager?.ResizeMap(mapWidth, mapHeight);
                // ✅ 关键：ResizeMap 后必须重新获取 grids 引用
                gridManager?.Init();
            }

            string section = "";
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("=== ") && line.EndsWith(" ==="))
                {
                    section = line.Substring(4, line.Length - 8).Trim();
                    continue;
                }

                if (section == "T")
                {
                    // y: n1 n2 n3 ...
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"^(\d+):\s+(.+)");
                    if (m.Success)
                    {
                        int y = int.Parse(m.Groups[1].Value);
                        var nums = m.Groups[2].Value.Trim().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int x = 0; x < nums.Length; x++)
                        {
                            if (int.TryParse(nums[x], out int num))
                            {
                                if (x >= 0 && x < gridManager.map.GetLength(0) && y >= 0 && y < gridManager.map.GetLength(1))
                                {
                                    var g = gridManager.map[x, y];
                                    if (g != null) g.gridType = NumberToTerrain(num);
                                }
                            }
                        }
                    }
                }
                else if (section == "U")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d+),(\d+)\]\s+(\w+)\s+(\w+)\s+(\d+)(.*)");
                    if (m.Success)
                    {
                        int ux = int.Parse(m.Groups[1].Value), uy = int.Parse(m.Groups[2].Value);
                        string uAbbr = m.Groups[3].Value, tAbbr = m.Groups[4].Value;
                        int uHp = int.Parse(m.Groups[5].Value);
                        string diffPart = m.Groups[6].Value;
                        string fullType = AbbrToUnitType(uAbbr);
                        string fullTeam = AbbrToTeam(tAbbr);
                        string scenePath = GetScenePathForUnitType(fullType);
                        if (!string.IsNullOrEmpty(scenePath) && ux >= 0 && ux < gridManager.map.GetLength(0) && uy >= 0 && uy < gridManager.map.GetLength(1))
                        {
                            var grid = gridManager.map[ux, uy];
                            if (grid != null)
                            {
                                SpawnUnitAtGrid(grid, scenePath, fullTeam);
                                var spawned = grid.infantries.LastOrDefault();
                                if (spawned != null)
                                {
                                    spawned.health = uHp * 10;
                                    spawned.UpdateHpLabel();
                                    ApplyDiffString(spawned, diffPart);
                                }
                            }
                        }
                    }
                }
                else if (section == "W")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d+),(\d+)\]\s+(\w+)\s+(\w+)\s+(\d+)(.*)");
                    if (m.Success)
                    {
                        int wx = int.Parse(m.Groups[1].Value), wy = int.Parse(m.Groups[2].Value);
                        string wAbbr = m.Groups[3].Value, tAbbr = m.Groups[4].Value;
                        int wHp = int.Parse(m.Groups[5].Value);
                        string diffPart = m.Groups[6].Value;
                        string fullType = AbbrToWeaponType(wAbbr);
                        string fullTeam = AbbrToTeam(tAbbr);
                        string scenePath = GetScenePathForWeaponType(fullType);
                        if (!string.IsNullOrEmpty(scenePath) && wx >= 0 && wx < gridManager.map.GetLength(0) && wy >= 0 && wy < gridManager.map.GetLength(1))
                        {
                            var grid = gridManager.map[wx, wy];
                            if (grid != null)
                            {
                                SpawnWeaponAtGrid(grid, scenePath, fullTeam);
                                var spawned = grid.weapon;
                                if (spawned != null)
                                {
                                    spawned.health = wHp * 10;
                                    spawned.UpdateHpLabel();
                                    ApplyDiffString(spawned, diffPart);
                                }
                            }
                        }
                    }
                }
                else if (section == "F")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d+),(\d+)\]\s+(\w+)\s+(\w+)(.*)");
                    if (m.Success)
                    {
                        int fx = int.Parse(m.Groups[1].Value), fy = int.Parse(m.Groups[2].Value);
                        string fAbbr = m.Groups[3].Value, tAbbr = m.Groups[4].Value;
                        string diffPart = m.Groups[5].Value;
                        string fullType = AbbrToFacilityType(fAbbr);
                        string fullTeam = AbbrToTeam(tAbbr);
                        string scenePath = GetScenePathForFacilityType(fullType);
                        if (!string.IsNullOrEmpty(scenePath) && fx >= 0 && fx < gridManager.map.GetLength(0) && fy >= 0 && fy < gridManager.map.GetLength(1))
                        {
                            var grid = gridManager.map[fx, fy];
                            if (grid != null)
                            {
                                SpawnFacilityAtGrid(grid, scenePath, fullTeam);
                                var spawned = grid.city;
                                if (spawned != null) ApplyDiffString(spawned, diffPart);
                            }
                        }
                    }
                }
                else if (section == "$")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"P1:(\d+)");
                    if (m.Success && gameManager != null) gameManager.p1Funds = int.Parse(m.Groups[1].Value);
                    m = System.Text.RegularExpressions.Regex.Match(line, @"P2:(\d+)");
                    if (m.Success && gameManager != null) gameManager.p2Funds = int.Parse(m.Groups[1].Value);
                }
            }

            // ✅ 重新收集所有单位和兵器（确保 AllUnits/AllWeapons 列表正确）
            gameManager?.unitManager?.RefreshUnitList();
            gameManager?.weaponManager?.RefreshWeaponList();

            // ✅ 重新设置所有回调和视觉状态
            if (gameManager != null)
            {
                foreach (var unit in gameManager.unitManager?.AllUnits ?? new List<Infantry>())
                {
                    if (unit != null && IsInstanceValid(unit))
                    {
                        unit.OnClickPiece = gameManager.OnSelectPiece;
                        unit.UpdateTeamVisual();
                        unit.UpdateHpLabel();
                    }
                }
                foreach (var weapon in gameManager.weaponManager?.AllWeapons ?? new List<Weapon>())
                {
                    if (weapon != null && IsInstanceValid(weapon))
                    {
                        weapon.OnClickWeapon = gameManager.OnSelectWeapon;
                        weapon.UpdateHpLabel();
                    }
                }
            }

            // ✅ 重置游戏状态为初始回合
            if (gameManager != null)
            {
                gameManager.turnPhase = 1;
                gameManager.selectedInfantry = null;
                gameManager.UpdateTurnLabel();
            }

            gameManager?.unitManager?.RefreshUnitList();
            gameManager?.UpdateUnitLists();
            gameManager?.RefreshSpecializedUnitLists();
            gameManager?.fogOfWarManager?.RefreshFog();
            gameManager?.ResetGameState();
            ShowImportHint("✅ 极简导入成功！");
        }
        catch (Exception ex)
        {
            if (gameManager != null) gameManager.blockVictoryCheck = false;
            ShowImportHint($"❌ 导入失败:\n{ex.Message}");
        }
        finally
        {
            // ✅ 关键修复：不再在这里重置 blockVictoryCheck
            // 因为 GameManager.ResetForMapImport() 已经设置了3秒保护计时器
            // 这里立即重置会导致保护失效，触发延迟判定
            // 保护计时器会在3秒后自动解除并调用 CheckVictoryCondition
            GD.Print("[Import] ParseAndImportMap 完成，保护计时器由 GameManager 管理");
        }
    }

    // ✅ 极简导入对话框
    private void OpenCompactImportDialog()
    {
        var dialog = new Panel();
        dialog.Name = "CompactImportDialog";
        dialog.CustomMinimumSize = new Vector2(420, 240);
        dialog.SetAnchorsPreset(LayoutPreset.Center);
        dialog.ZIndex = 1400;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.08f, 0.1f, 0.15f, 0.98f);
        panelStyle.SetCornerRadiusAll(16);
        panelStyle.SetBorderWidthAll(3);
        panelStyle.BorderColor = new Color(0.5f, 0.7f, 0.9f);
        dialog.AddThemeStyleboxOverride("panel", panelStyle);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);
        vbox.OffsetLeft = 20; vbox.OffsetRight = -20;
        vbox.OffsetTop = 20; vbox.OffsetBottom = -20;
        vbox.AddThemeConstantOverride("separation", 12);

        var title = new Label();
        title.Text = "📥 极简导入";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", new Color(0.4f, 0.9f, 1f));
        vbox.AddChild(title);

        var fileBtn = new Button();
        fileBtn.Text = "📁 选择 txt 文件";
        fileBtn.CustomMinimumSize = new Vector2(360, 45);
        fileBtn.AddThemeFontSizeOverride("font_size", 14);
        fileBtn.Pressed += () => {
            dialog.QueueFree();
            var fd = new FileDialog();
            fd.FileMode = FileDialog.FileModeEnum.OpenFile;
            fd.Access = FileDialog.AccessEnum.Filesystem;
            fd.Filters = new string[] { "*.txt ; Text Files" };
            fd.Title = "选择极简导出文件";
            fd.Size = new Vector2I(800, 600);
            fd.FileSelected += (path) => {
                try { ParseCompactMap(File.ReadAllText(path)); } catch (Exception ex) { ShowImportHint($"❌ 读取失败: {ex.Message}"); }
                fd.QueueFree();
            };
            fd.Canceled += () => fd.QueueFree();
            AddChild(fd); fd.PopupCentered();
        };
        vbox.AddChild(fileBtn);

        var clipBtn = new Button();
        clipBtn.Text = "📋 从剪贴板导入";
        clipBtn.CustomMinimumSize = new Vector2(360, 45);
        clipBtn.AddThemeFontSizeOverride("font_size", 14);
        clipBtn.Pressed += () => {
            dialog.QueueFree();
            string content = DisplayServer.ClipboardGet();
            if (string.IsNullOrWhiteSpace(content)) { ShowImportHint("❌ 剪贴板为空"); return; }
            ParseCompactMap(content);
        };
        vbox.AddChild(clipBtn);

        var hint = new Label();
        hint.Text = "极简格式：每行一个对象，无复杂Test块\n更可靠、更紧凑";
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.AddThemeColorOverride("font_color", new Color(0.8f, 0.6f, 0.3f));
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(hint);

        dialog.AddChild(vbox);
        AddChild(dialog);
    }

    // ========== ✅ AWBW 地图导入 ==========
    private void OpenAWBWImportDialog()
    {
        var dialog = new Panel();
        dialog.Name = "AWBWImportDialog";
        dialog.CustomMinimumSize = new Vector2(420, 280);
        dialog.SetAnchorsPreset(LayoutPreset.Center);
        dialog.ZIndex = 1400;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.08f, 0.1f, 0.15f, 0.98f);
        panelStyle.SetCornerRadiusAll(16);
        panelStyle.SetBorderWidthAll(3);
        panelStyle.BorderColor = new Color(0.5f, 0.7f, 0.9f);
        dialog.AddThemeStyleboxOverride("panel", panelStyle);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);
        vbox.OffsetLeft = 20; vbox.OffsetRight = -20;
        vbox.OffsetTop = 20; vbox.OffsetBottom = -20;
        vbox.AddThemeConstantOverride("separation", 12);

        var title = new Label();
        title.Text = "🌐 导入 AWBW 地图";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", new Color(0.4f, 0.9f, 1f));
        vbox.AddChild(title);

        var fileBtn = new Button();
        fileBtn.Text = "📁 选择 AWBW 导出 txt 文件";
        fileBtn.CustomMinimumSize = new Vector2(360, 45);
        fileBtn.AddThemeFontSizeOverride("font_size", 14);
        fileBtn.Pressed += () => {
            // ✅ Android：无存储权限则先发起请求并提示授权后重试
            if (!GameManager.EnsureStoragePermission())
            {
                ShowImportHint("⚠️ 需要存储权限才能导入文件\n请在系统设置中允许「所有文件访问」后重试");
                return;
            }
            dialog.QueueFree();
            var fd = new FileDialog();
            fd.FileMode = FileDialog.FileModeEnum.OpenFile;
            fd.Access = FileDialog.AccessEnum.Filesystem;
            fd.Filters = new string[] { "*.txt ; Text Files" };
            fd.Title = "选择 AWBW 地图导出文件";
            fd.Size = new Vector2I(800, 600);
            // ✅ Android：默认打开下载目录，方便找到手机下载的文件
            if (OS.GetName() == "Android")
                fd.CurrentDir = OS.GetSystemDir(OS.SystemDir.Downloads);
            fd.FileSelected += (path) => {
                try { ParseAWBWMap(File.ReadAllText(path)); }
                catch (Exception ex) { ShowImportHint($"❌ 读取失败: {ex.Message}"); }
                fd.QueueFree();
            };
            fd.Canceled += () => fd.QueueFree();
            AddChild(fd); fd.PopupCentered();
        };
        vbox.AddChild(fileBtn);

        var clipBtn = new Button();
        clipBtn.Text = "📋 从剪贴板导入 AWBW 地图";
        clipBtn.CustomMinimumSize = new Vector2(360, 45);
        clipBtn.AddThemeFontSizeOverride("font_size", 14);
        clipBtn.Pressed += () => {
            dialog.QueueFree();
            string content = DisplayServer.ClipboardGet();
            if (string.IsNullOrWhiteSpace(content)) { ShowImportHint("❌ 剪贴板为空"); return; }
            ParseAWBWMap(content);
        };
        vbox.AddChild(clipBtn);

        var hint = new Label();
        hint.Text = "AWBW 格式：每行逗号分隔的地形ID\n不支持的地形将智能平替为最接近类型\n完全不支持的将保留为 GROUND";
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.AddThemeColorOverride("font_color", new Color(0.8f, 0.6f, 0.3f));
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(hint);

        var closeBtn = new Button();
        closeBtn.Text = "❌ 关闭";
        closeBtn.CustomMinimumSize = new Vector2(360, 40);
        closeBtn.AddThemeFontSizeOverride("font_size", 14);
        closeBtn.AddThemeColorOverride("font_color", new Color(1, 0.5f, 0.5f));
        closeBtn.Pressed += () => dialog.QueueFree();
        vbox.AddChild(closeBtn);

        dialog.AddChild(vbox);
        AddChild(dialog);
    }

    /// <summary>
    /// AWBW 地形 ID → (GridType 地形, string 设施类型, string 设施归属)
    /// 设施类型为空表示不生成设施
    /// </summary>
    private (GridType terrain, string facilityType, string facilityTeam) GetAWBWTerrainMapping(int id)
    {
        switch (id)
        {
            // 基础地形
            case 1: return (GridType.GROUND, null, null);          // Plain
            case 2: return (GridType.HILL, null, null);             // Mountain
            case 3: return (GridType.FOREST, null, null);          // Wood
            case 28: return (GridType.SEA, null, null);            // Sea
            case 33: return (GridType.REEF, null, null);           // Reef
            case 195: return (GridType.TP, null, null);             // Teleporter

            // 河流 (4-14) → 平替为 RIVER
            case 4: case 5: case 6: case 7: case 8:
            case 9: case 10: case 11: case 12: case 13: case 14:
                return (GridType.RIVER, null, null);

            // 道路 (15-27) → 平替为 ROAD
            case 15: case 16: case 17: case 18: case 19:
            case 20: case 21: case 22: case 23: case 24:
            case 25: case 26: case 27:
                return (GridType.ROAD, null, null);

            // 浅滩 (29-32) → 平替为 BEACH
            case 29: case 30: case 31: case 32:
                return (GridType.BEACH, null, null);

            // 管道 (101-110) → 平替为 PIPE
            case 101: case 102: case 103: case 104: case 105:
            case 106: case 107: case 108: case 109: case 110:
                return (GridType.PIPE, null, null);

            // 管道接缝 (113-114) → PIPESEAM
            case 113: case 114:
                return (GridType.PIPESEAM, null, null);

            // 管道废墟 (115-116) → BROKENPIPE
            case 115: case 116:
                return (GridType.BROKENPIPE, null, null);

            // 中性设施 (34-36) → Player0
            case 34: return (GridType.GROUND, "City", "Player0");      // Neutral City
            case 35: return (GridType.GROUND, "Base", "Player0");      // Neutral Base
            case 36: return (GridType.GROUND, "AirPort", "Player0");   // Neutral Airport

            // Orange Star 设施 (38-41) → Player1
            case 38: return (GridType.GROUND, "City", "Player1");      // Orange Star City
            case 39: return (GridType.GROUND, "Base", "Player1");      // Orange Star Base
            case 40: return (GridType.GROUND, "AirPort", "Player1");  // Orange Star Airport

            // Blue Moon 设施 (43-46) → Player2
            case 43: return (GridType.GROUND, "City", "Player2");      // Blue Moon City
            case 44: return (GridType.GROUND, "Base", "Player2");      // Blue Moon Base
            case 45: return (GridType.GROUND, "AirPort", "Player2");  // Blue Moon Airport

            // Green Earth 设施 (48-50) → Player0
            case 48: return (GridType.GROUND, "City", "Player0");      // Green Earth City
            case 49: return (GridType.GROUND, "Base", "Player0");      // Green Earth Base
            case 50: return (GridType.GROUND, "AirPort", "Player0");  // Green Earth Airport

            // Yellow Comet 设施 (53-55) → Player0
            case 53: return (GridType.GROUND, "City", "Player0");      // Yellow Comet City
            case 54: return (GridType.GROUND, "Base", "Player0");      // Yellow Comet Base
            case 55: return (GridType.GROUND, "AirPort", "Player0");  // Yellow Comet Airport

            // Red Fire 设施 (81-83) → Player0
            case 81: return (GridType.GROUND, "City", "Player0");      // Red Fire City
            case 82: return (GridType.GROUND, "Base", "Player0");      // Red Fire Base
            case 83: return (GridType.GROUND, "AirPort", "Player0");  // Red Fire Airport

            // Grey Sky 设施 (86-88) → Player0
            case 86: return (GridType.GROUND, "City", "Player0");      // Grey Sky City
            case 87: return (GridType.GROUND, "Base", "Player0");      // Grey Sky Base
            case 88: return (GridType.GROUND, "AirPort", "Player0");  // Grey Sky Airport

            // Black Hole 设施 (91-93) → Player0
            case 91: return (GridType.GROUND, "City", "Player0");      // Black Hole City
            case 92: return (GridType.GROUND, "Base", "Player0");      // Black Hole Base
            case 93: return (GridType.GROUND, "AirPort", "Player0");  // Black Hole Airport

            // Brown Desert 设施 (96-98) → Player0
            case 96: return (GridType.GROUND, "City", "Player0");      // Brown Desert City
            case 97: return (GridType.GROUND, "Base", "Player0");      // Brown Desert Base
            case 98: return (GridType.GROUND, "AirPort", "Player0");  // Brown Desert Airport

            // Amber Blossom 设施 (117-119) → Player0
            case 117: return (GridType.GROUND, "AirPort", "Player0");  // Amber Blossom Airport
            case 118: return (GridType.GROUND, "Base", "Player0");      // Amber Blossom Base
            case 119: return (GridType.GROUND, "City", "Player0");      // Amber Blossom City

            // Jade Sun 设施 (122-124) → Player0
            case 122: return (GridType.GROUND, "AirPort", "Player0");  // Jade Sun Airport
            case 123: return (GridType.GROUND, "Base", "Player0");      // Jade Sun Base
            case 124: return (GridType.GROUND, "City", "Player0");      // Jade Sun City

            // Cobalt Ice 设施 (149-151) → Player0
            case 149: return (GridType.GROUND, "AirPort", "Player0");  // Cobalt Ice Airport
            case 150: return (GridType.GROUND, "Base", "Player0");      // Cobalt Ice Base
            case 151: return (GridType.GROUND, "City", "Player0");      // Cobalt Ice City

            // Pink Cosmos 设施 (156-158) → Player0
            case 156: return (GridType.GROUND, "AirPort", "Player0");  // Pink Cosmos Airport
            case 157: return (GridType.GROUND, "Base", "Player0");      // Pink Cosmos Base
            case 158: return (GridType.GROUND, "City", "Player0");      // Pink Cosmos City

            // Teal Galaxy 设施 (163-165) → Player0
            case 163: return (GridType.GROUND, "AirPort", "Player0");  // Teal Galaxy Airport
            case 164: return (GridType.GROUND, "Base", "Player0");      // Teal Galaxy Base
            case 165: return (GridType.GROUND, "City", "Player0");      // Teal Galaxy City

            // Purple Lightning 设施 (170-172) → Player0
            case 170: return (GridType.GROUND, "AirPort", "Player0");  // Purple Lightning Airport
            case 171: return (GridType.GROUND, "Base", "Player0");      // Purple Lightning Base
            case 172: return (GridType.GROUND, "City", "Player0");      // Purple Lightning City

            // Acid Rain 设施 (181-183) → Player0
            case 181: return (GridType.GROUND, "AirPort", "Player0");  // Acid Rain Airport
            case 182: return (GridType.GROUND, "Base", "Player0");      // Acid Rain Base
            case 183: return (GridType.GROUND, "City", "Player0");      // Acid Rain City

            // White Nova 设施 (188-190) → Player0
            case 188: return (GridType.GROUND, "AirPort", "Player0");  // White Nova Airport
            case 189: return (GridType.GROUND, "Base", "Player0");      // White Nova Base
            case 190: return (GridType.GROUND, "City", "Player0");      // White Nova City

            // Azure Asteroid 设施 (196-198) → Player0
            case 196: return (GridType.GROUND, "AirPort", "Player0");  // Azure Asteroid Airport
            case 197: return (GridType.GROUND, "Base", "Player0");      // Azure Asteroid Base
            case 198: return (GridType.GROUND, "City", "Player0");      // Azure Asteroid City

            // Noir Eclipse 设施 (203-205) → Player0
            case 203: return (GridType.GROUND, "AirPort", "Player0");  // Noir Eclipse Airport
            case 204: return (GridType.GROUND, "Base", "Player0");      // Noir Eclipse Base
            case 205: return (GridType.GROUND, "City", "Player0");      // Noir Eclipse City

            // Silver Claw 设施 (210-212) → Player0
            case 210: return (GridType.GROUND, "AirPort", "Player0");  // Silver Claw Airport
            case 211: return (GridType.GROUND, "Base", "Player0");      // Silver Claw Base
            case 212: return (GridType.GROUND, "City", "Player0");      // Silver Claw City

            // Umber Wilds 设施 (217-219) → Player0
            case 217: return (GridType.GROUND, "AirPort", "Player0");  // Umber Wilds Airport
            case 218: return (GridType.GROUND, "Base", "Player0");      // Umber Wilds Base
            case 219: return (GridType.GROUND, "City", "Player0");      // Umber Wilds City

            // 所有其他不支持的 ID → 跳过，返回 GROUND
            default:
                return (GridType.GROUND, null, null);
        }
    }

    private void ParseAWBWMap(string content)
    {
        try
        {
            if (gameManager != null) gameManager.blockVictoryCheck = true;
            ClearAllMapEntities();

            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            // ✅ 计算 AWBW 地图尺寸
            int mapWidth = 0;
            int mapHeight = 0;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var ids = line.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                mapWidth = Math.Max(mapWidth, ids.Length);
                mapHeight++;
            }

            if (mapWidth > 0 && mapHeight > 0)
            {
                gridManager?.ResizeMap(mapWidth, mapHeight);
                // ✅ 关键：ResizeMap 后必须重新获取 grids 引用
                gridManager?.Init();
            }

            int importedTiles = 0;
            int skippedTiles = 0;
            int facilityCount = 0;
            var unknownIds = new HashSet<int>();

            for (int y = 0; y < lines.Length; y++)
            {
                string line = lines[y].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var ids = line.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int x = 0; x < ids.Length; x++)
                {
                    if (!int.TryParse(ids[x].Trim(), out int tileId))
                        continue;

                    var (terrain, facilityType, facilityTeam) = GetAWBWTerrainMapping(tileId);

                    // 记录未知ID：不是 Plain(1) 且返回 GROUND+无设施 的，视为未映射
                    bool isKnown = tileId == 1 || terrain != GridType.GROUND || facilityType != null;
                    if (!isKnown)
                    {
                        unknownIds.Add(tileId);
                        skippedTiles++;
                    }
                    else
                    {
                        importedTiles++;
                    }

                    // 应用地形到对应格子
                    if (x >= 0 && x < gridManager.map.GetLength(0) && y >= 0 && y < gridManager.map.GetLength(1))
                    {
                        var grid = gridManager.map[x, y];
                        if (grid != null)
                        {
                            grid.gridType = terrain;

                            // 生成设施
                            if (!string.IsNullOrEmpty(facilityType))
                            {
                                string scenePath = GetScenePathForFacilityType(facilityType);
                                if (!string.IsNullOrEmpty(scenePath))
                                {
                                    SpawnFacilityAtGrid(grid, scenePath, facilityTeam ?? "Player0");
                                    facilityCount++;
                                }
                            }
                        }
                    }
                }
            }

            // 刷新所有格子视觉
            foreach (var grid in gridManager.grids)
            {
                if (grid != null && IsInstanceValid(grid))
                {
                    UpdateGridVisual(grid);
                }
            }

            // 刷新游戏状态
            gameManager?.unitManager?.RefreshUnitList();
            gameManager?.UpdateUnitLists();
            gameManager?.RefreshSpecializedUnitLists();
            gameManager?.fogOfWarManager?.RefreshFog();
            gameManager?.ResetGameState();

            string unknownInfo = unknownIds.Count > 0
                ? $"\n未映射ID: {string.Join(", ", unknownIds.OrderBy(i => i))}"
                : "";
            ShowImportHint($"✅ AWBW 导入成功！\n导入: {importedTiles} 格 | 设施: {facilityCount} 个 | 跳过: {skippedTiles} 格{unknownInfo}");
        }
        catch (Exception ex)
        {
            if (gameManager != null) gameManager.blockVictoryCheck = false;
            ShowImportHint($"❌ AWBW 导入失败:\n{ex.Message}");
        }
        finally
        {
            // ✅ 关键修复：不再在这里重置 blockVictoryCheck
            // 因为 GameManager.ResetForMapImport() 已经设置了3秒保护计时器
            // 这里立即重置会导致保护失效，触发延迟判定
            // 保护计时器会在3秒后自动解除并调用 CheckVictoryCondition
            GD.Print("[Import] ParseAndImportMap 完成，保护计时器由 GameManager 管理");
        }
    }

    // ========== ✅ 批量地形放置系统 ==========
    private void CreateBatchTerrainButton()
    {
        batchTerrainBtn = new Button();
        batchTerrainBtn.Name = "BatchTerrainBtn";
        batchTerrainBtn.Text = "🗺️ 批量地形";
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
        batchTerrainTitleLabel.Text = "🗺️ 批量地形放置";
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
        hint.Text = "🖱️ 点击地形选择，在地图滑动铺设\nESC 结束批量放置";
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.AddThemeColorOverride("font_color", new Color(0.7f, 0.8f, 0.6f));
        hint.Position = new Vector2(10, 55);
        hint.Size = new Vector2(320, 40);
        batchTerrainPanel.AddChild(hint);

        // 滚动区域
        var scroll = new ScrollContainer();
        scroll.Position = new Vector2(10, 100);
        scroll.Size = new Vector2(320, 420);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

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

        // 结束放置按钮
        var endBtn = new Button();
        endBtn.Text = "❌ 结束放置";
        endBtn.CustomMinimumSize = new Vector2(300, 40);
        endBtn.Position = new Vector2(20, 530);
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
        dragHint.Text = "⋮⋮ 拖拽移动";
        dragHint.AddThemeFontSizeOverride("font_size", 10);
        dragHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.8f));
        dragHint.Position = new Vector2(8, 2);
        dragHandle.AddChild(dragHint);
        batchTerrainPanel.AddChild(dragHandle);

        AddChild(batchTerrainPanel);
        MakeMenuDraggable(batchTerrainPanel, dragHandle);
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
            batchTerrainTitleLabel.Text = $"🗺️ 批量地形 - 当前: {GetTerrainDisplayName(batchTerrainType)}";
        }
        else
        {
            batchTerrainTitleLabel.Text = "🗺️ 批量地形放置";
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
            batchTerrainBtn.Text = "🗺️ 批量地形";
            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.3f, 0.5f, 0.5f, 0.9f);
            style.SetCornerRadiusAll(8);
            batchTerrainBtn.AddThemeStyleboxOverride("normal", style);
        }
    }

    // ========== ✅ AWS Map Editor 多格式导入 (.aws/.aw2/.awm/.awd) ==========
    private void OpenAWMEImportDialog()
    {
        var dialog = new Panel();
        dialog.Name = "AWMEImportDialog";
        dialog.CustomMinimumSize = new Vector2(420, 280);
        dialog.SetAnchorsPreset(LayoutPreset.Center);
        dialog.ZIndex = 1400;

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.08f, 0.12f, 0.18f, 0.97f);
        bg.SetCornerRadiusAll(16);
        bg.SetBorderWidthAll(3);
        bg.BorderColor = new Color(0.6f, 0.9f, 0.4f);
        dialog.AddThemeStyleboxOverride("panel", bg);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("margin_left", 20);
        vbox.AddThemeConstantOverride("margin_right", 20);
        vbox.AddThemeConstantOverride("margin_top", 16);
        vbox.AddThemeConstantOverride("margin_bottom", 16);
        vbox.AddThemeConstantOverride("separation", 12);

        var title = new Label();
        title.Text = "🗺️ 导入 AW 地图";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", new Color(0.5f, 0.95f, 0.4f));
        vbox.AddChild(title);

        var fileBtn = new Button();
        fileBtn.Text = "📁 选择 AW 地图文件";
        fileBtn.CustomMinimumSize = new Vector2(360, 45);
        fileBtn.AddThemeFontSizeOverride("font_size", 14);
        fileBtn.Pressed += () => {
            // ✅ Android：无存储权限则先发起请求并提示授权后重试
            if (!GameManager.EnsureStoragePermission())
            {
                ShowImportHint("⚠️ 需要存储权限才能导入文件\n请在系统设置中允许「所有文件访问」后重试");
                return;
            }
            dialog.QueueFree();
            var fd = new FileDialog();
            fd.FileMode = FileDialog.FileModeEnum.OpenFile;
            fd.Access = FileDialog.AccessEnum.Filesystem;
            fd.Filters = new string[] { "*.aws ; AW Advance Wars", "*.aw2 ; AW2 Black Hole Rising", "*.awm ; AWM Advance Wars Map", "*.awd ; AWDS Dual Strike" };
            fd.Title = "选择 AW 地图文件";
            fd.Size = new Vector2I(800, 600);
            // ✅ Android：默认打开下载目录，方便找到手机下载的文件
            if (OS.GetName() == "Android")
                fd.CurrentDir = OS.GetSystemDir(OS.SystemDir.Downloads);
            fd.FileSelected += (path) => {
                try {
                    byte[] fileData = File.ReadAllBytes(path);
                    ParseAWMEMap(fileData);
                }
                catch (Exception ex) { ShowImportHint($"❌ 读取失败: {ex.Message}"); }
                fd.QueueFree();
            };
            fd.Canceled += () => fd.QueueFree();
            AddChild(fd);
            fd.PopupCentered();
        };
        vbox.AddChild(fileBtn);

        var hint = new Label();
        hint.Text = "支持 .aws / .aw2 / .awm / .awd 格式\n未知地形将保留为 GROUND\n不支持的单位/设施/兵器将自动跳过";
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.AddThemeColorOverride("font_color", new Color(0.8f, 0.6f, 0.3f));
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(hint);

        var closeBtn = new Button();
        closeBtn.Text = "❌ 关闭";
        closeBtn.CustomMinimumSize = new Vector2(360, 40);
        closeBtn.AddThemeFontSizeOverride("font_size", 13);
        closeBtn.Pressed += () => dialog.QueueFree();
        vbox.AddChild(closeBtn);

        dialog.AddChild(vbox);
        AddChild(dialog);
    }

    private void ParseAWMEMap(byte[] fileData)
    {
        try
        {
            if (fileData == null || fileData.Length < 10)
            {
                ShowImportHint("❌ 文件过短，无法解析");
                return;
            }

            if (gameManager != null) gameManager.blockVictoryCheck = true;
            ClearAllMapEntities();

            string header = System.Text.Encoding.ASCII.GetString(fileData, 0, 9);
            int offset = 10;
            int width = 0, height = 0;

            if (header == "AWSMap001")
            {
                if (fileData.Length < 13) { ShowImportHint("❌ AWS 文件过短"); return; }
                width = fileData[10];
                height = fileData[11]; 
                offset = 13;
            }
            else if (header == "AW2Map001" || header == "AWMap 001")
            {
                width = 30;
                height = 20;
                offset = 10;
            }
            else if (header == "AWDMap001")
            {
                if (fileData.Length < 11) { ShowImportHint("❌ AWD 文件过短"); return; }
                width = 30;
                height = 20;
                offset = 11;
            }
            else
            {
                ShowImportHint("❌ 不支持的文件格式");
                return;
            }

            // ✅ 重新调整地图大小，创建/删除格子以匹配导入尺寸
            gridManager?.ResizeMap(width, height);

            int gridCount = width * height;
            int terrainDataSize = gridCount * 2;
            int unitDataSize = gridCount * 2;

            if (fileData.Length < offset + terrainDataSize + unitDataSize)
            {
                ShowImportHint("❌ 文件数据不完整");
                return;
            }

            int importedTiles = 0;
            int skippedTiles = 0;
            int facilityCount = 0;
            int unitCount = 0;
            int weaponCount = 0;
            var unknownTerrainIds = new HashSet<int>();
            var unknownFacilityIds = new HashSet<int>();
            var unknownUnitIds = new HashSet<int>();
            var unknownWeaponIds = new HashSet<int>();

            // ✅ 修复：读取地形数据（纯地形层，不包含设施/单位/兵器）
            // AWS 格式是列优先（Column-major）：x * height + y
            // 每个格子 2 字节：Byte0=地形ID(0-255), Byte1=地形变体/方向
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    int idx = offset + (x * height + y) * 2;
                    if (idx + 1 >= fileData.Length) continue;

                    // ✅ 修复：地形层使用单字节地形ID（0-255）
                    byte terrainId = fileData[idx];
                    byte terrainVariant = fileData[idx + 1];  // 变体/方向，暂时保留

                    if (x >= gridManager.map.GetLength(0) || y >= gridManager.map.GetLength(1))
                        continue;

                    var grid = gridManager.map[x, y];
                    if (grid == null) continue;

                    // 纯地形解析（0-255）
                    var terrain = GetAWMETerrainMapping((int)terrainId);
                    if (terrain == GridType.GROUND && terrainId != 0)
                    {
                        unknownTerrainIds.Add((int)terrainId);
                        skippedTiles++;
                    }
                    else
                    {
                        importedTiles++;
                    }
                    grid.gridType = terrain;
                }
            }

            // ✅ 修复：读取单位/设施/兵器数据（单位层）
            // 单位层同样是列优先，每个格子 2 字节
            // 0xFFFF 表示空
            int unitOffset = offset + width * height * 2;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    int idx = unitOffset + (x * height + y) * 2;
                    if (idx + 1 >= fileData.Length) continue;

                    ushort rawVal = (ushort)(fileData[idx] | (fileData[idx + 1] << 8));

                    // 空格子检测
                    if (rawVal == 0xFFFF) continue;

                    if (x >= gridManager.map.GetLength(0) || y >= gridManager.map.GetLength(1))
                        continue;

                    var grid = gridManager.map[x, y];
                    if (grid == null) continue;

                    // 设施 (300-499)
                    if (rawVal >= 300 && rawVal <= 499)
                    {
                        var (facType, facTeam) = GetAWMEFacilityMapping((int)rawVal);
                        if (!string.IsNullOrEmpty(facType))
                        {
                            string scenePath = GetScenePathForFacilityType(facType);
                            if (!string.IsNullOrEmpty(scenePath))
                            {
                                SpawnFacilityAtGrid(grid, scenePath, facTeam ?? "Player0");
                                facilityCount++;
                            }
                            else
                            {
                                unknownFacilityIds.Add((int)rawVal);
                            }
                        }
                        else
                        {
                            unknownFacilityIds.Add((int)rawVal);
                        }
                    }
                    // 单位 (500-899)
                    else if (rawVal >= 500 && rawVal <= 899)
                    {
                        var (unitType, unitTeam) = GetAWMEUnitMapping((int)rawVal);
                        if (!string.IsNullOrEmpty(unitType))
                        {
                            string scenePath = GetScenePathForUnitType(unitType);
                            if (!string.IsNullOrEmpty(scenePath))
                            {
                                SpawnUnitAtGrid(grid, scenePath, unitTeam ?? "Player1");
                                unitCount++;
                            }
                            else
                            {
                                unknownUnitIds.Add((int)rawVal);
                            }
                        }
                        else
                        {
                            unknownUnitIds.Add((int)rawVal);
                        }
                    }
                    // 兵器 (900-1299)
                    else if (rawVal >= 900 && rawVal <= 1299)
                    {
                        var (wepType, wepTeam, variant) = GetAWMEWeaponMapping((int)rawVal);
                        if (!string.IsNullOrEmpty(wepType))
                        {
                            string scenePath = GetScenePathForWeaponType(wepType);
                            if (!string.IsNullOrEmpty(scenePath))
                            {
                                SpawnWeaponAtGrid(grid, scenePath, wepTeam ?? "Player0");
                                var spawned = grid.weapon;
                                if (spawned is BlackCannon cannon)
                                {
                                    cannon.direction = variant switch
                                    {
                                        0 => BlackCannon.CannonDirection.Down,
                                        1 => BlackCannon.CannonDirection.Up,
                                        2 => BlackCannon.CannonDirection.Right,
                                        3 => BlackCannon.CannonDirection.Left,
                                        _ => BlackCannon.CannonDirection.Down
                                    };
                                    cannon.UpdateDirectionVisual();
                                }
                                else if (spawned is Laser laser)
                                {
                                    laser.SetFireAngles(new List<float> { 0f, 90f, 180f, 270f });
                                    laser.UpdateLaserVisual();
                                }
                                weaponCount++;
                            }
                            else
                            {
                                unknownWeaponIds.Add((int)rawVal);
                            }
                        }
                        else
                        {
                            unknownWeaponIds.Add((int)rawVal);
                        }
                    }
                    else
                    {
                        unknownUnitIds.Add((int)rawVal);
                    }
                }
            }

            foreach (var grid in gridManager.grids)
            {
                if (grid != null && IsInstanceValid(grid))
                {
                    UpdateGridVisual(grid);
                }
            }

            // ✅ 关键：导入完成后刷新所有列表和状态
            gameManager?.unitManager?.RefreshUnitList();
            gameManager?.weaponManager?.RefreshWeaponList();
            gameManager?.UpdateUnitLists();
            gameManager?.RefreshSpecializedUnitLists();
            gameManager?.fogOfWarManager?.RefreshFog();
            // ✅ 不再调用 ResetGameState()，因为 ResetForMapImport 已经设置了保护计时器
            // 保护计时器会在3秒后自动解除并检查胜利条件

            string summary = $"✅ AW 地图导入成功！\n尺寸: {width}×{height} | 导入: {importedTiles} 格";
            if (facilityCount > 0) summary += $" | 设施: {facilityCount} 个";
            if (unitCount > 0) summary += $" | 单位: {unitCount} 个";
            if (weaponCount > 0) summary += $" | 兵器: {weaponCount} 个";
            if (skippedTiles > 0) summary += $" | 跳过: {skippedTiles} 格";

            string unknownInfo = "";
            if (unknownTerrainIds.Count > 0)
                unknownInfo += $"\n未知地形: {string.Join(", ", unknownTerrainIds.OrderBy(i => i).Take(20))}";
            if (unknownFacilityIds.Count > 0)
                unknownInfo += $"\n未知设施: {string.Join(", ", unknownFacilityIds.OrderBy(i => i).Take(20))}";
            if (unknownUnitIds.Count > 0)
                unknownInfo += $"\n未知单位: {string.Join(", ", unknownUnitIds.OrderBy(i => i).Take(20))}";
            if (unknownWeaponIds.Count > 0)
                unknownInfo += $"\n未知兵器: {string.Join(", ", unknownWeaponIds.OrderBy(i => i).Take(20))}";

            ShowImportHint(summary + unknownInfo);
        }
        catch (Exception ex)
        {
            if (gameManager != null) gameManager.blockVictoryCheck = false;
            ShowImportHint($"❌ AW 地图导入失败:\n{ex.Message}");
        }
        finally
        {
            // ✅ 关键修复：不再在这里重置 blockVictoryCheck
            // 因为 GameManager.ResetForMapImport() 已经设置了3秒保护计时器
            // 这里立即重置会导致保护失效，触发延迟判定
            // 保护计时器会在3秒后自动解除并调用 CheckVictoryCondition
            GD.Print("[Import] ParseAndImportMap 完成，保护计时器由 GameManager 管理");
        }
    }

    private GridType GetAWMETerrainMapping(int value)
    {
        if (value < 0 || value > 299) return GridType.GROUND;

        switch (value)
        {
            case 0: return GridType.GROUND;
            case 1: return GridType.ROAD;
            case 2: return GridType.BRIDGE;
            case 3: case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 11: case 12: case 13: case 14:
                return GridType.RIVER;
            case 15: case 17: case 18: case 19: case 20: case 21: case 22: case 23: case 24: case 25: case 26: case 27:
                return GridType.ROAD;
            case 16: return GridType.PIPE;
            case 28: return GridType.SEA;
            case 29: case 31: case 32: case 39:
                return GridType.BEACH;
            case 30: return GridType.REEF;
            case 33: return GridType.REEF;
            case 60: return GridType.SEA;
            case 90: return GridType.FOREST;
            case 150: return GridType.HILL;
            case 167: return GridType.GROUND;
            case 226: return GridType.PIPESEAM;
            default:
                int x = value % 30;
                int y = value / 30;
                if (y == 0 && x == 16) return GridType.PIPE;
                if (y == 7 && x == 16) return GridType.PIPESEAM;
                return GridType.GROUND;
        }
    }

    private (string typeName, string team) GetAWMEFacilityMapping(int value)
    {
        if (value < 300 || value > 499) return (null, null);

        int baseVal = value - 300;
        int x = baseVal % 10;
        int y = baseVal / 10;

        string team = y == 0 ? "Player1" : (y == 1 ? "Player2" : "Player0");

        string typeName = x switch
        {
            0 => "HQ",
            1 => "City",
            2 => "Base",
            3 => "AirPort",
            4 => "Port",
            5 => "Tower",
            6 => "Lab",
            _ => null
        };

        if (string.IsNullOrEmpty(typeName)) return (null, null);

        if (typeName == "HQ" || typeName == "Port" || typeName == "Tower" || typeName == "Lab")
            return (null, null);

        return (typeName, team);
    }

    private (string typeName, string team) GetAWMEUnitMapping(int value)
    {
        if (value < 500 || value > 899) return (null, null);

        int baseVal = value - 500;
        int x = baseVal % 20;
        int y = baseVal / 20;

        string team = (y / 2) switch
        {
            0 => "Player1",
            1 => "Player2",
            _ => "Player0"
        };

        string typeName = x switch
        {
            0 => "infantry",
            20 => "mech",
            1 => "Md_Tank",
            21 => "light_tank",
            2 => "Recon",
            22 => "apc",
            3 => "Artillery",
            23 => "Rocket",
            4 => "AntiAir",
            24 => "Anti_Tank",
            12 => "oozium",
            32 => "FlyBomb",
            _ => null
        };

        return (typeName, team);
    }

    private (string typeName, string team, int variant) GetAWMEWeaponMapping(int value)
    {
        if (value < 900 || value > 1299) return (null, null, -1);

        int baseVal = value - 900;
        int x = baseVal % 20;
        int y = baseVal / 20;

        string team = (y / 2) switch
        {
            0 => "Player1",
            1 => "Player2",
            _ => "Player0"
        };

        // ✅ 修复：扩大映射范围，支持 AW2/AWD 中 DeathRay 和 BlackCannon 的不同编码
        // 同时支持 y=0,1,2,3 等多种变体，避免映射到错误类型
        string typeName = x switch
        {
            // MiniCannon (单格小炮) - 4方向
            0 => "black_cannon",
            1 => "black_cannon",

            // L-Cannon / Laser - 支持多 y 值
            2 => "Laser",

            // BlackCrystal / Crystal - 支持多 y 值  
            3 => "Crystal",

            // ✅ 关键修复：BlackCannon (大型黑炮) → LargeCannon
            // 支持所有 y 值，避免被错误映射
            4 => "large_cannon",

            // ✅ 关键修复：DeathRay → DeathRay
            // 支持所有 y 值（y=0,1,2,3），避免被错误映射为 Laser 或其他
            5 => "death_ray",

            // BlackObelisk / Crystal
            6 => "Crystal",

            // 扩展：处理更多可能的编码位置
            7 => "black_cannon",   // 额外的 MiniCannon 变体
            8 => "Laser",         // 额外的 Laser 变体
            9 => "Crystal",       // 额外的 Crystal 变体
            10 => "large_cannon", // 额外的 LargeCannon 变体
            11 => "death_ray",    // 额外的 DeathRay 变体
            12 => "Crystal",      // 额外的 Obelisk 变体

            _ => null
        };

        // Variant: 0=Down, 1=Up, 2=Right, 3=Left
        // ✅ 修复：根据 y 值计算正确的方向，而不是固定值
        int variant = x switch
        {
            0 => y % 4 switch { 0 => 1, 1 => 2, 2 => 0, 3 => 3, _ => 0 },
            1 => y % 4 switch { 0 => 3, 1 => 0, 2 => 1, 3 => 2, _ => 0 },
            2 => y % 4,           // Laser: 根据 y 值循环方向
            3 => 0,               // Crystal: 无方向
            4 => y % 4,           // ✅ LargeCannon: 根据 y 值正确设置方向
            5 => y % 4,           // ✅ DeathRay: 根据 y 值正确设置方向
            6 => 0,               // Obelisk: 无方向
            7 => y % 4,
            8 => y % 4,
            9 => 0,
            10 => y % 4,
            11 => y % 4,
            12 => 0,
            _ => 0
        };

        return (typeName, team, variant);
    }


    // ========== ✅ 多格兵器放置预览系统 ==========
    private void TryPreviewMultiTileWeapon(Grids grid, string scenePath, string team)
    {
        if (string.IsNullOrEmpty(scenePath) || grid == null) return;

        var scene = GD.Load<PackedScene>(scenePath);
        if (scene == null) return;

        var tempWeapon = scene.Instantiate<Weapon>();
        if (tempWeapon == null) return;

        // ✅ 关键修复：Instantiate 不会触发 _Ready，多格兵器的 isMultiTile 需要在 _Ready 中设置
        // 但临时实例化时 _Ready 还没执行，所以这里手动检测并设置
        if (tempWeapon is LargeCannon lc && !tempWeapon.isMultiTile)
        {
            tempWeapon.isMultiTile = true;
            tempWeapon.size = lc.multiTileSize;
        }
        if (tempWeapon is DeathRay dr && !tempWeapon.isMultiTile)
        {
            tempWeapon.isMultiTile = true;
            tempWeapon.size = dr.multiTileSize;
        }

        // 如果不是多格兵器，直接生成（不走预览流程）
        if (!tempWeapon.isMultiTile)
        {
            tempWeapon.QueueFree();
            SpawnWeaponAtGrid(grid, scenePath, team);
            return;
        }

        // 计算预览范围
        var anchorPos = grid.GridIndex;
        var occupiedIndices = tempWeapon.GetOccupiedIndices(anchorPos);
        tempWeapon.QueueFree();

        // 检查是否超出地图限制
        var gm = gameManager;
        if (gm?.gridManager == null) return;
        foreach (var idx in occupiedIndices)
        {
            if (idx.X < 0 || idx.X >= gm.gridManager.searchRange.X ||
                idx.Y < 0 || idx.Y >= gm.gridManager.searchRange.Y)
            {
                spawnHintLabel.Text = "❌ 超出地图范围，无法放置";
                spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
                return;
            }
        }

        // ✅ 检查目标区域是否已被占据（兵器/非重叠单位）
        foreach (var idx in occupiedIndices)
        {
            var g = gm.gridManager.map[idx.X, idx.Y];
            if (g == null) continue;
            // 已有兵器
            if (g.weapon != null && IsInstanceValid(g.weapon))
            {
                spawnHintLabel.Text = "❌ 该区域已有兵器占据，无法放置";
                spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
                return;
            }
            // 非重叠单位
            var blockingUnit = g.infantries.FirstOrDefault(u =>
                u != null && IsInstanceValid(u) && u.overlapType == UnitOverlapType.NonOverlapping);
            if (blockingUnit != null)
            {
                spawnHintLabel.Text = $"❌ 该区域被 {blockingUnit.Name} 占据";
                spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
                return;
            }
        }

        // 清除之前的预览
        ClearMultiTilePreview();

        // 显示预览范围（蓝色攻击范围图标）
        previewMultiTileGrids.Clear();
        foreach (var idx in occupiedIndices)
        {
            var g = gm.gridManager.map[idx.X, idx.Y];
            if (g != null)
            {
                previewMultiTileGrids.Add(g);
                g.attackRangeIcon?.Show();
                if (g.attackRangeIcon != null)
                    g.attackRangeIcon.Modulate = new Color(0.3f, 0.6f, 1.0f, 0.8f); // 蓝色预览
            }
        }

        // 保存预览状态
        isMultiTilePreviewMode = true;
        pendingMultiTileAnchorPos = anchorPos;
        pendingMultiTileScenePath = scenePath;
        pendingMultiTileTeam = team;

        spawnHintLabel.Text = $"🔵 预览 {tempWeapon.GetType().Name} {occupiedIndices.Count}格范围\n再次点击确认放置，按ESC取消";
        spawnHintLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.7f, 1f));
    }

    private void ConfirmMultiTilePlacement()
    {
        if (!isMultiTilePreviewMode || string.IsNullOrEmpty(pendingMultiTileScenePath)) return;

        var gm = gameManager;
        if (gm?.gridManager == null) return;

        var anchorGrid = gm.gridManager.map[pendingMultiTileAnchorPos.X, pendingMultiTileAnchorPos.Y];
        if (anchorGrid == null)
        {
            ClearMultiTilePreview();
            return;
        }

        var scene = GD.Load<PackedScene>(pendingMultiTileScenePath);
        if (scene == null)
        {
            ClearMultiTilePreview();
            return;
        }

        var weapon = scene.Instantiate<Weapon>();
        if (weapon == null)
        {
            ClearMultiTilePreview();
            return;
        }

        // 摧毁预览范围内的所有单位、兵器、设施
        foreach (var g in previewMultiTileGrids)
        {
            if (g == null || !IsInstanceValid(g)) continue;
            // 摧毁单位
            foreach (var unit in g.infantries.ToList())
            {
                if (unit != null && IsInstanceValid(unit))
                {
                    gameManager.RemoveUnit(unit);
                }
            }
            // 摧毁兵器（排除即将放置的）
            foreach (var w in g.weapons.ToList())
            {
                if (w != null && IsInstanceValid(w) && w != weapon)
                {
                    gameManager.weaponManager.RemoveWeapon(w);
                }
            }
            // 摧毁设施
            if (g.city != null && IsInstanceValid(g.city))
            {
                g.city.QueueFree();
                g.city = null;
            }
        }

        weapon.Position = anchorGrid.Position;
        weapon.team = pendingMultiTileTeam;
        weapon.Name = $"{pendingMultiTileTeam}_{weapon.GetType().Name}_{gameManager.weaponManager.AllWeapons.Count}";

        gameManager.weaponManager.weaponsNode.AddChild(weapon);

        bool bound = gameManager.weaponManager.BindWeaponToGrid(weapon, true);
        if (!bound)
        {
            weapon.QueueFree();
            if (weapon.GetParent() == gameManager.weaponManager.weaponsNode)
                gameManager.weaponManager.weaponsNode.RemoveChild(weapon);
            spawnHintLabel.Text = "❌ 多格兵器绑定失败";
            spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
            ClearMultiTilePreview();
            return;
        }

        gameManager.weaponManager.AllWeapons.Add(weapon);
        weapon.OnClickWeapon = gameManager.OnSelectWeapon;
        weapon.UpdateHpLabel();
        if (weapon is BlackCannon cannon)
        {
            cannon.direction = pendingCannonDirection;
            cannon.UpdateDirectionVisual();
            cannon.UpdateAmmoVisual();
        }
        if (weapon is DeathRay dr)
        {
            dr.direction = pendingCannonDirection;
            dr.UpdateDirectionVisual();
            dr.UpdateAmmoVisual();
            dr.UpdateMultiTileVisual();
        }
        if (weapon is LargeCannon lc)
        {
            lc.direction = pendingCannonDirection;
            lc.UpdateDirectionVisual();
            lc.UpdateAmmoVisual();
            lc.UpdateMultiTileVisual();
        }

        gameManager.UpdateUnitLists();

        spawnHintLabel.Text = $"✅ 生成多格兵器: {weapon.Name} ({weapon.size.X}×{weapon.size.Y})";
        spawnHintLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));

        ClearMultiTilePreview();
    }

    private void ClearMultiTilePreview()
    {
        foreach (var g in previewMultiTileGrids)
        {
            if (g != null && IsInstanceValid(g))
            {
                g.attackRangeIcon?.Hide();
                if (g.attackRangeIcon != null)
                    g.attackRangeIcon.Modulate = new Color(1, 0.2f, 0.2f, 0.9f);
            }
        }
        previewMultiTileGrids.Clear();
        isMultiTilePreviewMode = false;
        pendingMultiTileWeapon = null;
        pendingMultiTileAnchorPos = new Vector2I(-1, -1);
        pendingMultiTileScenePath = "";
        pendingMultiTileTeam = "";
    }

    // ✅ 新增：生成新地图
    private void GenerateNewMap(int width, int height)
    {
        if (gridManager == null)
        {
            ShowImportHint("❌ GridManager 未初始化");
            return;
        }

        // 确认对话框
        var confirm = new ConfirmationDialog();
        confirm.Title = "⚠️ 确认生成新地图";
        confirm.DialogText = $"将生成 {width}×{height} 的新地图，\n当前所有单位、兵器、设施将被清空！\n\n是否继续？";
        confirm.Size = new Vector2I(400, 200);
        confirm.Confirmed += () => {
            confirm.QueueFree();
            ExecuteGenerateMap(width, height);
        };
        confirm.Canceled += () => confirm.QueueFree();
        AddChild(confirm);
        confirm.PopupCentered();
    }

    private void ExecuteGenerateMap(int width, int height)
    {
        try
        {
            // 清空所有实体
            ClearAllMapEntities();

            // 调整地图大小
            gridManager.ResizeMap(width, height);
            gridManager.Init();

            // 所有格子默认设为平原
            foreach (var grid in gridManager.grids)
            {
                if (grid != null)
                {
                    grid.gridType = GridType.GROUND;
                    UpdateGridVisual(grid);
                }
            }

            // 重置游戏状态
            gameManager?.ResetGameState();
            gameManager?.UpdateUnitLists();
            gameManager?.RefreshSpecializedUnitLists();

            ShowImportHint($"✅ 地图生成成功！\n尺寸: {width}×{height}");
        }
        catch (Exception ex)
        {
            ShowImportHint($"❌ 生成失败: {ex.Message}");
        }
    }
}
