using Godot;
using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;

// TerrainEditor · GlobalConfig（由 TerrainEditor.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class TerrainEditor
{

    // ========== ✅ 全局配置面板 ==========
    private Panel globalConfigPanel;
    private VBoxContainer globalConfigContainer;
    private CheckBox fogOfWarCheckBox;
    private Button openGlobalConfigBtn;

    // 缓存反射信息（同时缓存字段和属性）
    private Dictionary<Type, List<FieldInfo>> exportFieldsCache = new();
    private Dictionary<Type, List<PropertyInfo>> exportPropertiesCache = new();
private bool fogStateBeforeEdit = false;
private readonly List<UnitTemplate> unitTemplates = new()
{
    // 步兵类
    new UnitTemplate { DisplayName = TranslateGroup.T("🚶 步兵"), ScenePath = "res://Prefabs/infantry(1).tscn", 
        Category = TranslateGroup.T("步兵"), Team = "Player1", DisplayColor = new Color(0.4f, 0.7f, 0.4f), 
        Description = TranslateGroup.T("基础步兵单位，可占领城市") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🦾 机甲"), ScenePath = "res://Prefabs/mech.tscn", 
        Category = TranslateGroup.T("步兵"), Team = "Player1", DisplayColor = new Color(0.5f, 0.6f, 0.4f), 
        Description = TranslateGroup.T("反装甲步兵，移动力较低") },

    // 装甲类
    new UnitTemplate { DisplayName = TranslateGroup.T("🚗 轻型坦克"), ScenePath = "res://Prefabs/light_tank.tscn", 
        Category = TranslateGroup.T("装甲"), Team = "Player1", DisplayColor = new Color(0.6f, 0.5f, 0.3f), 
        Description = TranslateGroup.T("快速装甲单位，攻防均衡") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🚙 重型坦克"), ScenePath = "res://Prefabs/Md_Tank.tscn", 
        Category = TranslateGroup.T("装甲"), Team = "Player1", DisplayColor = new Color(0.55f, 0.45f, 0.25f), 
        Description = TranslateGroup.T("重火力履带单位，对轻甲毁灭性，移动力5") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🚀 火箭炮"), ScenePath = "res://Prefabs/Rocket.tscn", 
        Category = TranslateGroup.T("装甲"), Team = "Player1", DisplayColor = new Color(0.7f, 0.4f, 0.3f), 
        Description = TranslateGroup.T("远程间接火力，射程3-5格") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🔫 自行火炮"), ScenePath = "res://Prefabs/Artillery.tscn", 
        Category = TranslateGroup.T("装甲"), Team = "Player1", DisplayColor = new Color(0.6f, 0.45f, 0.35f), 
        Description = TranslateGroup.T("中程火力支援，射程2-3格") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🚛 运输车"), ScenePath = "res://Prefabs/apc.tscn", 
        Category = TranslateGroup.T("装甲"), Team = "Player1", DisplayColor = new Color(0.5f, 0.55f, 0.4f), 
        Description = TranslateGroup.T("运输单位，可搭载步兵") },
        new UnitTemplate { 
    DisplayName = TranslateGroup.T("🛡️ 防空高炮"), 
    ScenePath = "res://Prefabs/AntiAir.tscn", 
    Category = TranslateGroup.T("装甲"), 
    Team = "Player1", 
    DisplayColor = new Color(0.5f, 0.7f, 0.3f),  
    Description = TranslateGroup.T("对步兵/机甲毁灭性，移动力6，弹药9发，无副武器") 
},
    new UnitTemplate { 
    DisplayName = TranslateGroup.T("🚙 侦察车"), 
    ScenePath = "res://Prefabs/Recon.tscn", 
    Category = TranslateGroup.T("装甲"), 
    Team = "Player1", 
    DisplayColor = new Color(0.5f, 0.6f, 0.3f),  
    Description = TranslateGroup.T("高视野侦察单位，移动力8，仅副武器，视野5") 
},
    new UnitTemplate { 
    DisplayName = TranslateGroup.T("🎯 反坦克炮"), 
    ScenePath = "res://Prefabs/Anti_Tank.tscn", 
    Category = TranslateGroup.T("装甲"), 
    Team = "Player1", 
    DisplayColor = new Color(0.55f, 0.5f, 0.3f),  
    Description = TranslateGroup.T("射程1-3反装甲单位，可近战反击，对地高伤害，弹药6发") 
},


    new UnitTemplate { DisplayName = TranslateGroup.T("🔦 照明车"), ScenePath = "res://Prefabs/Flare.tscn", 
        Category = TranslateGroup.T("装甲"), Team = "Player1", DisplayColor = new Color(0.6f, 0.55f, 0.3f), 
        Description = TranslateGroup.T("可发射照明弹驱散战争迷雾，移动力5，履带移动，仅副武器") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🏍️ 摩托兵"), ScenePath = "res://Prefabs/Bike.tscn", 
        Category = TranslateGroup.T("步兵"), Team = "Player1", DisplayColor = new Color(0.5f, 0.5f, 0.35f), 
        Description = TranslateGroup.T("快速步兵单位，可占领城市，移动力5，轮胎移动") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🚀 管道炮"), ScenePath = "res://Prefabs/PipeRunner.tscn", 
        Category = TranslateGroup.T("装甲"), Team = "Player1", DisplayColor = new Color(0.5f, 0.4f, 0.6f), 
        Description = TranslateGroup.T("仅限管道移动，远程间接火力，射程2-5") },

    // 特殊单位
    new UnitTemplate { DisplayName = TranslateGroup.T("🦠 史莱姆"), ScenePath = "res://Prefabs/oozium.tscn", 
        Category = TranslateGroup.T("特殊"), Team = "Player1", DisplayColor = new Color(0.4f, 0.3f, 0.6f), 
        Description = TranslateGroup.T("吞噬型单位，可消灭任何敌人") },
    new UnitTemplate { DisplayName = TranslateGroup.T("💣 飞弹"), ScenePath = "res://Prefabs/FlyBomb.tscn", 
        Category = TranslateGroup.T("特殊"), Team = "Player1", DisplayColor = new Color(0.9f, 0.3f, 0.1f), 
        Description = TranslateGroup.T("自爆单位，3格范围50%HP伤害，空军移动力9") },
    new UnitTemplate { DisplayName = TranslateGroup.T("✈ 战斗机"), ScenePath = "res://Prefabs/Fighter.tscn",
        Category = TranslateGroup.T("空军"), Team = "Player1", DisplayColor = new Color(0.45f, 0.65f, 0.95f),
        Description = TranslateGroup.T("制空战斗机，仅可攻击空中单位，弹药9发，日耗油5") },
    new UnitTemplate { DisplayName = TranslateGroup.T("✈ 轰炸机"), ScenePath = "res://Prefabs/Bomber.tscn",
        Category = TranslateGroup.T("空军"), Team = "Player1", DisplayColor = new Color(0.85f, 0.55f, 0.35f),
        Description = TranslateGroup.T("对地毁灭打击，完全无力对空，弹药9发，日耗油5") },
    new UnitTemplate { DisplayName = TranslateGroup.T("✈ 武装直升机"), ScenePath = "res://Prefabs/BCopter.tscn",
        Category = TranslateGroup.T("空军"), Team = "Player1", DisplayColor = new Color(0.75f, 0.45f, 0.45f),
        Description = TranslateGroup.T("导弹打车，机枪打人，打不了飞机，弹药6发，日耗油2") },
    new UnitTemplate { DisplayName = TranslateGroup.T("✈ 运输直升机"), ScenePath = "res://Prefabs/TCopter.tscn",
        Category = TranslateGroup.T("空军"), Team = "Player1", DisplayColor = new Color(0.55f, 0.65f, 0.85f),
        Description = TranslateGroup.T("空运1名步兵/机甲直取敌后，无武器，日耗油2") },
    new UnitTemplate { DisplayName = TranslateGroup.T("防空导弹车"), ScenePath = "res://Prefabs/AntiAirMissile.tscn",
        Category = TranslateGroup.T("陆军"), Team = "Player1", DisplayColor = new Color(0.6f, 0.5f, 0.7f),
        Description = TranslateGroup.T("纯对空间接，射程3-5，视野5，雾战之眼") },
    new UnitTemplate { DisplayName = TranslateGroup.T("✈ 隐身战机"), ScenePath = "res://Prefabs/Stealth.tscn",
        Category = TranslateGroup.T("空军"), Team = "Player1", DisplayColor = new Color(0.35f, 0.35f, 0.5f),
        Description = TranslateGroup.T("全能猎手，仅战斗机可见可打，隐身日耗油8，弹药6") },
    new UnitTemplate { DisplayName = TranslateGroup.T("机械狗"), ScenePath = "res://Prefabs/Dog.tscn",
        Category = TranslateGroup.T("陆军"), Team = "Player1", DisplayColor = new Color(0.55f, 0.55f, 0.6f),
        Description = TranslateGroup.T("电子嗅探者：1-6格闻出隐形与隐蔽，咬人仅步兵一半，跑得快烧油") },

    // 海军
    new UnitTemplate { DisplayName = TranslateGroup.T("🔧 修理船"), ScenePath = "res://Prefabs/RepairBoat.tscn", 
        Category = TranslateGroup.T("海军"), Team = "Player1", DisplayColor = new Color(0.3f, 0.6f, 0.8f), 
        Description = TranslateGroup.T("海军修理单位，可修复友军，可搭载步兵，Hover移动") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🚢 战列舰"), ScenePath = "res://Prefabs/Battleship.tscn",
        Category = TranslateGroup.T("海军"), Team = "Player1", DisplayColor = new Color(0.3f, 0.6f, 0.8f),
        Description = TranslateGroup.T("AW3原作值：28000/移动5/视野2/射程2-6间接/弹9/油99") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🛥️ 巡洋舰"), ScenePath = "res://Prefabs/Cruiser.tscn",
        Category = TranslateGroup.T("海军"), Team = "Player1", DisplayColor = new Color(0.3f, 0.6f, 0.8f),
        Description = TranslateGroup.T("AW3原作值：18000/移动6/视野3/对空105反潜90/可载2直升机") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🤿 潜艇"), ScenePath = "res://Prefabs/Submarine.tscn",
        Category = TranslateGroup.T("海军"), Team = "Player1", DisplayColor = new Color(0.3f, 0.6f, 0.8f),
        Description = TranslateGroup.T("AW3原作值：20000/移动5/视野5/弹6/油60；下潜复用隐身模块") },
    new UnitTemplate { DisplayName = TranslateGroup.T("⛴️ 登陆艇"), ScenePath = "res://Prefabs/Lander.tscn",
        Category = TranslateGroup.T("海军"), Team = "Player1", DisplayColor = new Color(0.3f, 0.6f, 0.8f),
        Description = TranslateGroup.T("AW3原作值：12000/移动6/油99/载2地面单位；浅滩和港口装卸") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🛳️ 航母"), ScenePath = "res://Prefabs/Carrier.tscn",
        Category = TranslateGroup.T("海军"), Team = "Player1", DisplayColor = new Color(0.3f, 0.6f, 0.8f),
        Description = TranslateGroup.T("AW3原作值：30000/移动5/视野4/射程3-8对空/载2飞机并内部补给") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🚤 炮艇"), ScenePath = "res://Prefabs/Gunboat.tscn",
        Category = TranslateGroup.T("海军"), Team = "Player1", DisplayColor = new Color(0.3f, 0.6f, 0.8f),
        Description = TranslateGroup.T("DoR跨作适配：5000/移动7/弹1/载1步兵系；靠港补弹") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🛡️ 新型坦克"), ScenePath = "res://Prefabs/Neotank.tscn",
        Category = TranslateGroup.T("装甲"), Team = "Player1", DisplayColor = new Color(0.6f, 0.5f, 0.3f),
        Description = TranslateGroup.T("AW2/DS原作值：22000/移动6/视野1/弹9+机枪/油99") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🛡️ 巨型坦克"), ScenePath = "res://Prefabs/Megatank.tscn",
        Category = TranslateGroup.T("装甲"), Team = "Player1", DisplayColor = new Color(0.6f, 0.5f, 0.3f),
        Description = TranslateGroup.T("DS原作值：28000/移动4/视野1/弹3+机枪/油50") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🛡️ 弩级坦克"), ScenePath = "res://Prefabs/WarTank.tscn",
        Category = TranslateGroup.T("装甲"), Team = "Player1", DisplayColor = new Color(0.6f, 0.5f, 0.3f),
        Description = TranslateGroup.T("DoR跨作适配：22000/移动4/视野2/弹5+机枪/油50；伤害介于重坦与巨型坦克之间") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🚧 工程车"), ScenePath = "res://Prefabs/Rig.tscn",
        Category = TranslateGroup.T("陆军"), Team = "Player1", DisplayColor = new Color(0.5f, 0.6f, 0.3f),
        Description = TranslateGroup.T("DoR原作值：5000/移动6/载1步兵系/补给/搭建临时机场·港口") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🛩️ 螺旋桨机"), ScenePath = "res://Prefabs/Duster.tscn",
        Category = TranslateGroup.T("空军"), Team = "Player1", DisplayColor = new Color(0.6f, 0.8f, 1f),
        Description = TranslateGroup.T("DoR原作值：13000/移动8/视野4/机枪弹9/油99") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🛩️ 水上飞机"), ScenePath = "res://Prefabs/Seaplane.tscn",
        Category = TranslateGroup.T("空军"), Team = "Player1", DisplayColor = new Color(0.6f, 0.8f, 1f),
        Description = TranslateGroup.T("DoR原作值：15000/移动7/视野4/导弹3/油40；只能由生产舰生产") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🛳️ 生产舰"), ScenePath = "res://Prefabs/ProducibleBoat.tscn",
        Category = TranslateGroup.T("海军"), Team = "Player1", DisplayColor = new Color(0.3f, 0.6f, 0.8f),
        Description = TranslateGroup.T("DoR航母：28000/移动5/防空炮射程1/载2空军/舰载起飞/生产水上飞机") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🚢 突击舰"), ScenePath = "res://Prefabs/KamikazeBoat.tscn",
        Category = TranslateGroup.T("海军"), Team = "Player1", DisplayColor = new Color(0.3f, 0.6f, 0.8f),
        Description = TranslateGroup.T("DoR战列舰：25000/移动5/视野3/弹6/射程3-5/可移动后攻击") },
};

private readonly List<UnitTemplate> weaponTemplates = new()
{
    new UnitTemplate { DisplayName = TranslateGroup.T("⚫ 黑炮"), ScenePath = "res://Prefabs/black_cannon.tscn", 
        Category = TranslateGroup.T("兵器"), Team = "Player1", DisplayColor = new Color(0.3f, 0.3f, 0.3f), 
        Description = TranslateGroup.T("三角形范围兵器，固定百分比伤害") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🔴 激光炮"), ScenePath = "res://Prefabs/Laser.tscn", 
        Category = TranslateGroup.T("兵器"), Team = "Player1", DisplayColor = new Color(0.9f, 0.2f, 0.2f), 
        Description = TranslateGroup.T("直线激光兵器，穿透攻击，可配置角度/射程/冷却") },
    new UnitTemplate { DisplayName = TranslateGroup.T("💎 黑水晶"), ScenePath = "res://Prefabs/crystal.tscn", 
        Category = TranslateGroup.T("兵器"), Team = "Player1", DisplayColor = new Color(0.6f, 0.2f, 0.9f), 
        Description = TranslateGroup.T("环形范围治疗+补给兵器，还原原版AW黑水晶机制") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🖤 大型黑炮"), ScenePath = "res://Prefabs/large_cannon.tscn", 
        Category = TranslateGroup.T("兵器"), Team = "Player1", DisplayColor = new Color(0.2f, 0.2f, 0.2f), 
        Description = TranslateGroup.T("3×3多格占据兵器，仅弱点可攻击，摧毁后仍不可通行") },
    new UnitTemplate { DisplayName = TranslateGroup.T("☠️ 死光炮"), ScenePath = "res://Prefabs/death_ray.tscn", 
        Category = TranslateGroup.T("兵器"), Team = "Player1", DisplayColor = new Color(0.8f, 0.1f, 0.8f), 
        Description = TranslateGroup.T("3×3多格死光炮，四方向3格宽激光，摧毁后仍不可通行") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🗼 黑尖塔"), ScenePath = "res://Prefabs/black_obelisk.tscn", 
        Category = TranslateGroup.T("兵器"), Team = "Player1", DisplayColor = new Color(0.45f, 0.3f, 0.9f), 
        Description = TranslateGroup.T("3×3多格黑尖塔（AW3），不攻击；每回合开始2格内我方单位+2HP并补满油弹，仅正门可受击，摧毁后仍不可通行") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🚀 导弹发射井"), ScenePath = "res://Prefabs/missile_silo.tscn", 
        Category = TranslateGroup.T("兵器"), Team = "Player0", DisplayColor = new Color(0.6f, 0.6f, 0.6f), 
        Description = TranslateGroup.T("AW原作导弹井：单位站上井口发射，5×5菱形3HP伤害；发射台模块15项全配置（势力/可摧毁/弹药/自动恢复/打击效果等）") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🛰️ 力场卫星 Black Onyx"), ScenePath = "res://Prefabs/ForceSatellite.tscn", 
        Category = TranslateGroup.T("太空军"), Team = "Player1", DisplayColor = new Color(0.6f, 0.3f, 0.9f), 
        Description = TranslateGroup.T("AW3黑玛瑙战斗卫星：宇宙战舰移动方式，无武器，HP270（=导弹伤害30×9，九次击落）；力场支援模块锁血/锁弹/锁油，仅攻击卫星可击伤(55%)") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🛰️ 攻击卫星"), ScenePath = "res://Prefabs/AttackSatellite.tscn", 
        Category = TranslateGroup.T("太空军"), Team = "Player1", DisplayColor = new Color(0.9f, 0.4f, 0.9f), 
        Description = TranslateGroup.T("AW3反卫星攻击卫星：宇宙战舰移动方式，普通弹药打同类65%/力场卫星55%；天基打击模块固定50%伤害") },
};

private readonly List<UnitTemplate> facilityTemplates = new()
{
    new UnitTemplate { DisplayName = "🏙️ City", ScenePath = "res://Prefabs/City.tscn", 
        Category = TranslateGroup.T("设施"), Team = "Player0", DisplayColor = new Color(0.8f, 0.6f, 0.2f), 
        Description = TranslateGroup.T("城市设施，可占领并提供补给") },
    new UnitTemplate { DisplayName = "🏭 Base", ScenePath = "res://Prefabs/Base.tscn", 
        Category = TranslateGroup.T("设施"), Team = "Player0", DisplayColor = new Color(0.7f, 0.5f, 0.3f), 
        Description = TranslateGroup.T("基地设施，可占领、补给并生产地面单位") },
    new UnitTemplate { DisplayName = "✈️ Airport", ScenePath = "res://Prefabs/AirPort.tscn", 
        Category = TranslateGroup.T("设施"), Team = "Player0", DisplayColor = new Color(0.4f, 0.6f, 0.8f), 
        Description = TranslateGroup.T("机场设施，可占领、补给并生产空军单位") },
    new UnitTemplate { DisplayName = "⚓ Port", ScenePath = "res://Prefabs/Port.tscn", 
        Category = TranslateGroup.T("设施"), Team = "Player0", DisplayColor = new Color(0.3f, 0.6f, 0.7f), 
        Description = TranslateGroup.T("港口设施，可占领、维修补给舰船并生产海军单位") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🛬 临时机场"), ScenePath = "res://Prefabs/TempAirPort.tscn", 
        Category = TranslateGroup.T("设施"), Team = "Player0", DisplayColor = new Color(0.4f, 0.6f, 0.8f), 
        Description = TranslateGroup.T("DoR 工程车搭建：可占领、维修补给空军；不能生产、无收入") },
    new UnitTemplate { DisplayName = TranslateGroup.T("⚓ 临时港口"), ScenePath = "res://Prefabs/TempPort.tscn", 
        Category = TranslateGroup.T("设施"), Team = "Player0", DisplayColor = new Color(0.3f, 0.6f, 0.7f), 
        Description = TranslateGroup.T("DoR 工程车搭建（海滩）：可占领、维修补给舰船；不能生产、无收入") },
};

// UI 引用
private Panel spawnMenuPanel;
private VBoxContainer spawnButtonContainer;
private Label spawnTitleLabel;
private Button spawnModeUnitBtn;
private Button spawnModeWeaponBtn;
private Button spawnModeFacilityBtn;
private Button destroyModeBtn;
public Label spawnHintLabel;
private HBoxContainer spawnModeSelector;
private HBoxContainer directionSelector; // ✅ 黑炮/大型黑炮 方向选择器

    // ========== ✅ 全局配置面板 ==========
    private void CreateGlobalConfigPanel()
    {
        openGlobalConfigBtn = new Button();
        openGlobalConfigBtn.Name = "OpenGlobalConfigBtn";
        TranslateGroup.SetText(openGlobalConfigBtn, "⚙️ 全局配置");
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
        TranslateGroup.SetText(titleLabel, "⚙️ 全局配置面板");
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
    TranslateGroup.SetText(hint, "格");
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
    TranslateGroup.SetText(modeLabel, "  模式:");
    modeLabel.CustomMinimumSize = new Vector2(50, 24);
    modeLabel.AddThemeFontSizeOverride("font_size", 11);
    hbox2.AddChild(modeLabel);

    var modeOption = new OptionButton();
    modeOption.AddItem(TranslateGroup.T("正常(菱形)"), 0);
    modeOption.AddItem(TranslateGroup.T("独立(攻击范围)"), 1);
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
                    unit.visionRange = VisionConfig.GetUnitVisionRange(unit.UnitTypeId, unit.TemplateId);
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

        var fogSection = CreateConfigSectionTitle(TranslateGroup.T("🌫️ 战争迷雾"));
        globalConfigContainer.AddChild(fogSection);

        var fogHBox = new HBoxContainer();
        fogHBox.AddThemeConstantOverride("separation", 10);

        var fogLabel = new Label();
        TranslateGroup.SetText(fogLabel, "启用战争迷雾:");
        fogLabel.CustomMinimumSize = new Vector2(120, 28);
        fogLabel.AddThemeFontSizeOverride("font_size", 13);
        fogHBox.AddChild(fogLabel);

        fogOfWarCheckBox = new CheckBox();
        fogOfWarCheckBox.ButtonPressed = gameManager?.IsFogOfWarEnabled() ?? true;
        fogOfWarCheckBox.CustomMinimumSize = new Vector2(28, 28);
        fogOfWarCheckBox.Toggled += OnFogCheckBoxToggled;
        fogHBox.AddChild(fogOfWarCheckBox);

        var fogHint = new Label();
        TranslateGroup.SetText(fogHint, "(编辑模式自动关闭)");
        fogHint.AddThemeFontSizeOverride("font_size", 10);
        fogHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        fogHBox.AddChild(fogHint);

        globalConfigContainer.AddChild(fogHBox);

        var unitSection = CreateConfigSectionTitle(TranslateGroup.T("👁️ 单位基础视野配置"));
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

        var weaponSection = CreateConfigSectionTitle(TranslateGroup.T("⚔️ 兵器基础视野配置"));
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
        var terrainSection = CreateConfigSectionTitle(TranslateGroup.T("🗺️ 全局默认地形视野加成"));
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
        var unitMatrixSection = CreateConfigSectionTitle(TranslateGroup.T("👁️ 单位类型×地形 专属加成矩阵"));
        globalConfigContainer.AddChild(unitMatrixSection);

        var unitMatrixBtn = new Button();
        TranslateGroup.SetText(unitMatrixBtn, "📊 打开单位地形加成矩阵编辑器");
        unitMatrixBtn.CustomMinimumSize = new Vector2(300, 40);
        unitMatrixBtn.AddThemeFontSizeOverride("font_size", 13);
        unitMatrixBtn.Pressed += OpenUnitTerrainMatrixEditor;
        globalConfigContainer.AddChild(unitMatrixBtn);

        // ✅ 新增：兵器类型×地形 专属加成矩阵配置
        var weaponMatrixSection = CreateConfigSectionTitle(TranslateGroup.T("⚔️ 兵器类型×地形 专属加成矩阵"));
        globalConfigContainer.AddChild(weaponMatrixSection);

        var weaponMatrixBtn = new Button();
        TranslateGroup.SetText(weaponMatrixBtn, "📊 打开兵器地形加成矩阵编辑器");
        weaponMatrixBtn.CustomMinimumSize = new Vector2(300, 40);
        weaponMatrixBtn.AddThemeFontSizeOverride("font_size", 13);
        weaponMatrixBtn.Pressed += OpenWeaponTerrainMatrixEditor;
        globalConfigContainer.AddChild(weaponMatrixBtn);

        // ✅ 资金系统配置
        var fundsSection = CreateConfigSectionTitle(TranslateGroup.T("💰 资金系统配置"));
        globalConfigContainer.AddChild(fundsSection);

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;

        // P1 现有资金
        var p1FundsHBox = new HBoxContainer();
        p1FundsHBox.AddThemeConstantOverride("separation", 10);
        var p1FundsLabel = new Label();
        TranslateGroup.SetText(p1FundsLabel, "P1 现有资金:");
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
        TranslateGroup.SetText(p1MaxLabel, "P1 资金上限:");
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
        TranslateGroup.SetText(p2FundsLabel, "P2 现有资金:");
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
        TranslateGroup.SetText(p2MaxLabel, "P2 资金上限:");
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
        var aiSection = CreateConfigSectionTitle(TranslateGroup.T("🤖 AI 控制与地图导出"));
        globalConfigContainer.AddChild(aiSection);

        var aiMgr = GetTree()?.GetFirstNodeInGroup("ai_manager") as AI_Manager;

        var p1AiHBox = new HBoxContainer();
        p1AiHBox.AddThemeConstantOverride("separation", 10);
        var p1AiLabel = new Label();
        TranslateGroup.SetText(p1AiLabel, "P1 AI 操控:");
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
        TranslateGroup.SetText(p2AiLabel, "P2 AI 操控:");
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
        TranslateGroup.SetText(autoEndLabel, "自动切换回合:");
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
        TranslateGroup.SetText(exportMapBtn, "📤 导出地图配置为 txt");
        exportMapBtn.CustomMinimumSize = new Vector2(300, 40);
        exportMapBtn.AddThemeFontSizeOverride("font_size", 13);
        exportMapBtn.Pressed += ExportMapToTxt;
        globalConfigContainer.AddChild(exportMapBtn);

        var importMapBtn = new Button();
        TranslateGroup.SetText(importMapBtn, "📥 导入地图配置");
        importMapBtn.CustomMinimumSize = new Vector2(300, 40);
        importMapBtn.AddThemeFontSizeOverride("font_size", 13);
        importMapBtn.Pressed += OpenImportDialog;
        globalConfigContainer.AddChild(importMapBtn);

        // ✅ 极简导出/导入按钮
        var compactExportBtn = new Button();
        TranslateGroup.SetText(compactExportBtn, "📤 极简导出");
        compactExportBtn.CustomMinimumSize = new Vector2(300, 40);
        compactExportBtn.AddThemeFontSizeOverride("font_size", 13);
        compactExportBtn.Pressed += ExportCompactMap;
        globalConfigContainer.AddChild(compactExportBtn);

        var compactImportBtn = new Button();

        // ✅ 胜利判定配置
        var victorySection = CreateConfigSectionTitle(TranslateGroup.T("🏆 胜利判定配置"));
        globalConfigContainer.AddChild(victorySection);

        var p1AnnihHBox = new HBoxContainer();
        p1AnnihHBox.AddThemeConstantOverride("separation", 10);
        var p1AnnihLabel = new Label();
        TranslateGroup.SetText(p1AnnihLabel, "P1 全灭胜利判定:");
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
        TranslateGroup.SetText(p2AnnihLabel, "P2 全灭胜利判定:");
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
        TranslateGroup.SetText(weaponVictoryLabel, "兵器摧毁胜利判定:");
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
        TranslateGroup.SetText(compactImportBtn, "📥 极简导入");
        compactImportBtn.CustomMinimumSize = new Vector2(300, 40);
        compactImportBtn.AddThemeFontSizeOverride("font_size", 13);
        compactImportBtn.Pressed += OpenCompactImportDialog;
        globalConfigContainer.AddChild(compactImportBtn);

        var awbwImportBtn = new Button();
        TranslateGroup.SetText(awbwImportBtn, "🌐 导入 AWBW 地图");
        awbwImportBtn.CustomMinimumSize = new Vector2(300, 40);
        awbwImportBtn.AddThemeFontSizeOverride("font_size", 13);
        awbwImportBtn.Pressed += OpenAWBWImportDialog;
        globalConfigContainer.AddChild(awbwImportBtn);

        var awmeImportBtn = new Button();
        TranslateGroup.SetText(awmeImportBtn, "🗺️ 导入 AW 地图 (.aws/.aw2/.awm/.awd)");
        awmeImportBtn.CustomMinimumSize = new Vector2(300, 40);
        awmeImportBtn.AddThemeFontSizeOverride("font_size", 13);
        awmeImportBtn.Pressed += OpenAWMEImportDialog;
        globalConfigContainer.AddChild(awmeImportBtn);

        var btnSeparator = new HSeparator();
        btnSeparator.CustomMinimumSize = new Vector2(0, 10);
        globalConfigContainer.AddChild(btnSeparator);

        // ✅ 新增：地图生成配置
        var mapGenSection = CreateConfigSectionTitle(TranslateGroup.T("🗺️ 生成地图"));
        globalConfigContainer.AddChild(mapGenSection);

        var mapGenHBox = new HBoxContainer();
        mapGenHBox.AddThemeConstantOverride("separation", 10);

        var widthLabel = new Label();
        TranslateGroup.SetText(widthLabel, "宽:");
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
        TranslateGroup.SetText(heightLabel, "高:");
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
        TranslateGroup.SetText(genMapBtn, "🗺️ 生成新地图");
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
        TranslateGroup.SetText(resetBtn, "🔄 重置默认");
        resetBtn.CustomMinimumSize = new Vector2(150, 36);
        resetBtn.AddThemeFontSizeOverride("font_size", 13);
        resetBtn.Pressed += () => {
            VisionConfig.ResetToDefaults();
            BuildGlobalConfigContent();
            RefreshAllUnitVision();
        };
        btnHBox.AddChild(resetBtn);

        var exportBtn = new Button();
        TranslateGroup.SetText(exportBtn, "📋 导出配置");
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

    // ✅ 新增：兵器地形加成矩阵编辑器
    private Panel weaponMatrixEditorPanel;

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
}
