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
    new UnitTemplate { DisplayName = TranslateGroup.T("🔧 管道构造者"), ScenePath = "res://Prefabs/PipeWorker.tscn",
        Category = TranslateGroup.T("特殊"), Team = "Player1", DisplayColor = new Color(0.5f, 0.4f, 0.6f),
        Description = TranslateGroup.T("黑洞军稀有单位：履带6格同新型坦克，无攻击力，装甲堪比新型坦克；临格建造/修复管道接缝（建造弹药3发，2回合20进度）") },
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
    // ✅ 太空军卫星（Infantry 系，属单位层；此前误收录在兵器层已修正）
    new UnitTemplate { DisplayName = TranslateGroup.T("🛰️ 力场卫星 Black Onyx"), ScenePath = "res://Prefabs/ForceSatellite.tscn",
        Category = TranslateGroup.T("太空军"), Team = "Player1", DisplayColor = new Color(0.6f, 0.3f, 0.9f),
        Description = TranslateGroup.T("AW3黑玛瑙战斗卫星：宇宙战舰移动方式，无武器，HP270（=导弹伤害30×9，九次击落）；力场支援模块锁血/锁弹/锁油，仅攻击卫星可击伤(55%)") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🛰️ 攻击卫星 The Black Amber"), ScenePath = "res://Prefabs/AttackSatellite.tscn",
        Category = TranslateGroup.T("太空军"), Team = "Player1", DisplayColor = new Color(0.9f, 0.4f, 0.9f),
        Description = TranslateGroup.T("攻击卫星 The Black Amber（AW3）：宇宙战舰移动方式，普通弹药打同类65%/力场卫星55%；天基打击模块固定50%伤害") },
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
    new UnitTemplate { DisplayName = TranslateGroup.T("⚙️ 管道接缝"), ScenePath = "res://Prefabs/PipeSeam.tscn", 
        Category = TranslateGroup.T("兵器"), Team = "Player0", DisplayColor = new Color(0.5f, 0.5f, 0.6f), 
        Description = TranslateGroup.T("Black Hole 管道动脉唯一可攻击部位：99HP，防御≈中型坦克（受击×0.45），摧毁后地形变管道废墟；管道构造者可临格修复") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🏭 超级兵工厂"), ScenePath = "res://Prefabs/SuperFactory.tscn", 
        Category = TranslateGroup.T("兵器"), Team = "Player0", DisplayColor = new Color(0.6f, 0.5f, 0.4f), 
        Description = TranslateGroup.T("AW2原作Black Hole Factory：3×4多格12格；三生产口免费即刻生产（每回合3个）；免疫一切伤害，摧毁联动管道接缝使其瘫痪变中立；Rig可建中转口接管；缴获红利3个免费即刻单位；可旋转90/180/270（带碰撞检查）") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🚪 门 Gate"), ScenePath = "res://Prefabs/Gate.tscn", 
        Category = TranslateGroup.T("兵器"), Team = "Player1", DisplayColor = new Color(0.6f, 0.5f, 0.3f), 
        Description = TranslateGroup.T("门模块（CW素材）：1.是否开启 2.所属势力 3.血量999 4.白名单（友/敌/全）+gateSize宽×高全自由度；血量耗尽前只有白名单能通过，耗尽后残骸敞开全通行，占据格地形恢复平原") },
    // ✅ 补录此前未收录的兵器（AW原作特殊兵器）
    new UnitTemplate { DisplayName = TranslateGroup.T("🌌 黑色弧光 Black Arc"), ScenePath = "res://Prefabs/BlackArc.tscn",
        Category = TranslateGroup.T("兵器"), Team = "Player1", DisplayColor = new Color(0.35f, 0.3f, 0.45f),
        Description = TranslateGroup.T("AW原作黑色弧形兵器：可被攻击扣血；拥有天基打击（轨道轰炸）能力，点击兵器弹出天基打击按钮→十字准星瞄准→以目标格为核心的x-y范围扣血；天基打击模块全Export可拼装") },
    new UnitTemplate { DisplayName = TranslateGroup.T("👻 幽灵炮 Ghost Cannon"), ScenePath = "res://Prefabs/GhostCannon.tscn",
        Category = TranslateGroup.T("兵器"), Team = "Player0", DisplayColor = new Color(0.5f, 0.55f, 0.65f),
        Description = TranslateGroup.T("AW2/3原作特殊兵器：只能被防空/反空单位和空军/天空军单位攻击扣血，陆军/海军无法攻击；伤害类别限制模块（Weapon基类可拼装Export）") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🧩 大雷弹·弱点节点（拆分版）"), ScenePath = "res://Prefabs/GrandBoltPart.tscn",
        Category = TranslateGroup.T("兵器"), Team = "Player0", DisplayColor = new Color(0.8f, 0.5f, 0.2f),
        Description = TranslateGroup.T("大雷弹拆分版：放置3个弱点节点，全毁=胜利（victoryCountRequired=3）") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🦉 巢穴·整体版 The Nest"), ScenePath = "res://Prefabs/TheNest.tscn",
        Category = TranslateGroup.T("兵器"), Team = "Player0", DisplayColor = new Color(0.55f, 0.35f, 0.6f),
        Description = TranslateGroup.T("DoR最终兵器Owl's Nest：5门可毁炮即巢穴全部（3固定+2旋转），全灭=过载爆炸=胜利；左右出兵口（左陆军/右空军）每日各产1单位，被堵跳过（Export配置）") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🦉 巢穴·中枢（拆分版）"), ScenePath = "res://Prefabs/NestCenter.tscn",
        Category = TranslateGroup.T("兵器"), Team = "Player0", DisplayColor = new Color(0.5f, 0.5f, 0.55f),
        Description = TranslateGroup.T("巢穴拆分·中枢/出兵口标记：原作中巢穴无独立本体，此件仅作出兵口占位与视觉锚点（Export配置）") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🦉 巢穴·固定炮（拆分版）"), ScenePath = "res://Prefabs/NestCannonFixed.tscn",
        Category = TranslateGroup.T("兵器"), Team = "Player0", DisplayColor = new Color(0.65f, 0.4f, 0.35f),
        Description = TranslateGroup.T("巢穴拆分·固定炮：8HP直线炮，Destroy计数参与陷落（5件）") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🦉 巢穴·旋转炮（拆分版）"), ScenePath = "res://Prefabs/NestCannonRotating.tscn",
        Category = TranslateGroup.T("兵器"), Team = "Player0", DisplayColor = new Color(0.6f, 0.5f, 0.3f),
        Description = TranslateGroup.T("巢穴拆分·旋转炮：8HP，可旋转，与固定炮交替开火（Export）") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🦉 巢穴·迫击炮（拆分版）"), ScenePath = "res://Prefabs/NestMortar.tscn",
        Category = TranslateGroup.T("兵器"), Team = "Player0", DisplayColor = new Color(0.45f, 0.55f, 0.4f),
        Description = TranslateGroup.T("巢穴拆分·迫击炮：不可摧毁，导弹井式洗列（左右3格+整列），5HP砸最贵集群（Export配置）") },
    new UnitTemplate { DisplayName = TranslateGroup.T("⚡ 巨型雷电 Grand Bolt"), ScenePath = "res://Prefabs/GrandBolt.tscn",
        Category = TranslateGroup.T("兵器"), Team = "Player0", DisplayColor = new Color(0.75f, 0.35f, 0.75f),
        Description = TranslateGroup.T("AWDS最终关Von Bolt巨型兵器：约19宽×13高不规则多格，3个弱点（各有冷却回合），摧毁全部弱点=胜利；可Export开关：蠕动（每回合平移1格）/困兽犹斗（弱点全毁后中心大弱点+产Oozium）") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🔴 巨雷模块·弱点A"), ScenePath = "res://Prefabs/GrandBoltModuleA.tscn",
        Category = TranslateGroup.T("兵器"), Team = "Player0", DisplayColor = new Color(0.85f, 0.3f, 0.3f),
        Description = TranslateGroup.T("模块化巨雷(学CoW多件分置, 独立新版不动整体版)：弱点模块A, 独立放置/独立摧毁, 死后显dead锁血") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🔴 巨雷模块·弱点B"), ScenePath = "res://Prefabs/GrandBoltModuleB.tscn",
        Category = TranslateGroup.T("兵器"), Team = "Player0", DisplayColor = new Color(0.85f, 0.3f, 0.3f),
        Description = TranslateGroup.T("模块化巨雷：弱点模块B") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🔴 巨雷模块·弱点C"), ScenePath = "res://Prefabs/GrandBoltModuleC.tscn",
        Category = TranslateGroup.T("兵器"), Team = "Player0", DisplayColor = new Color(0.85f, 0.3f, 0.3f),
        Description = TranslateGroup.T("模块化巨雷：弱点模块C") },
    new UnitTemplate { DisplayName = TranslateGroup.T("💥 巨雷模块·核心"), ScenePath = "res://Prefabs/GrandBoltModuleCore.tscn",
        Category = TranslateGroup.T("兵器"), Team = "Player0", DisplayColor = new Color(0.9f, 0.6f, 0.2f),
        Description = TranslateGroup.T("模块化巨雷：中心核心模块(困兽犹斗核心), 独立放置/独立摧毁") },
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
    // ✅ 补录此前未收录的设施（HQ/实验室/雷达/通信塔/中转口）
    new UnitTemplate { DisplayName = TranslateGroup.T("🏳️ 大本营 HQ"), ScenePath = "res://Prefabs/HQ.tscn",
        Category = TranslateGroup.T("设施"), Team = "Player0", DisplayColor = new Color(0.9f, 0.75f, 0.2f),
        Description = TranslateGroup.T("总部（Headquarters）：占领敌方HQ即获胜；被占领时原归属队伍直接判负（GameManager.Victory 处理）；Export 可配置改变team则Lose") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🔬 实验室 Lab"), ScenePath = "res://Prefabs/Lab.tscn",
        Category = TranslateGroup.T("设施"), Team = "Player0", DisplayColor = new Color(0.7f, 0.7f, 0.8f),
        Description = TranslateGroup.T("AW2原作：占领解锁新型坦克(Neotank)科技；实验室模块Export：是否开启/占领n个后新增或减少全局白名单（1个后、2个后…无数种组合）") },
    new UnitTemplate { DisplayName = TranslateGroup.T("📡 雷达 Radar"), ScenePath = "res://Prefabs/Radar.tscn",
        Category = TranslateGroup.T("设施"), Team = "Player0", DisplayColor = new Color(0.5f, 0.75f, 0.6f),
        Description = TranslateGroup.T("占领后清除迷雾；雷达模块：x-y范围去雾/自动化/强视野/强侦查；照明塔模块：x-y选目标点去雾（如照明弹，无弹药）/是否自动化，全Export可拼装") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🗼 通信塔 Com Tower"), ScenePath = "res://Prefabs/ComTower.tscn",
        Category = TranslateGroup.T("设施"), Team = "Player0", DisplayColor = new Color(0.8f, 0.6f, 0.75f),
        Description = TranslateGroup.T("AWDS原作：占领后提升己方单位攻击力；通信塔模块Export：是否开启/占领后对血量、弹药、燃料、攻击力、防御力、移动力的加成减成/是否自动化") },
    new UnitTemplate { DisplayName = TranslateGroup.T("🔌 中转口 Relay Junction"), ScenePath = "res://Prefabs/RelayJunction.tscn",
        Category = TranslateGroup.T("设施"), Team = "Player0", DisplayColor = new Color(0.55f, 0.5f, 0.6f),
        Description = TranslateGroup.T("Rig在管道废墟（原接缝位置）临格建造（2回合20进度，期间受攻击扣进度）；完成即绑定最近超级兵工厂→兵工厂归属改为建造方（触发缴获红利）；供能中继站") },
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

        // ✅ 天气系统（模块15）：编辑模式可改当前天气 + 出现权重可调
        var weatherSection = CreateConfigSectionTitle(TranslateGroup.T("🌦️ 天气系统"));
        globalConfigContainer.AddChild(weatherSection);

        // 当前天气（立即生效）
        var weatherHBox = new HBoxContainer();
        weatherHBox.AddThemeConstantOverride("separation", 10);
        var weatherLabel = new Label();
        TranslateGroup.SetText(weatherLabel, "当前天气:");
        weatherLabel.CustomMinimumSize = new Vector2(90, 28);
        weatherLabel.AddThemeFontSizeOverride("font_size", 13);
        weatherHBox.AddChild(weatherLabel);

        var weatherOption = new OptionButton();
        weatherOption.Name = "WeatherTypeOption";
        foreach (WeatherType wt in Enum.GetValues(typeof(WeatherType)))
            weatherOption.AddItem(TranslateGroup.T(WeatherSystem.WeatherName(wt)));
        weatherOption.Selected = (int)WeatherSystem.Current;
        weatherOption.CustomMinimumSize = new Vector2(110, 28);
        weatherOption.AddThemeFontSizeOverride("font_size", 12);
        weatherOption.ItemSelected += (index) => {
            WeatherSystem.SetWeather((WeatherType)(int)index, 1);   // 立即生效（持续1天，之后按权重轮换）
            ShowImportHint($"🌦️ {TranslateGroup.T("天气已切换")}: {WeatherSystem.WeatherName(WeatherSystem.Current)}");
        };
        weatherHBox.AddChild(weatherOption);
        globalConfigContainer.AddChild(weatherHBox);

        // 随机轮换开关
        var weatherRandomHBox = new HBoxContainer();
        weatherRandomHBox.AddThemeConstantOverride("separation", 10);
        var weatherRandomLabel = new Label();
        TranslateGroup.SetText(weatherRandomLabel, "每日随机轮换:");
        weatherRandomLabel.CustomMinimumSize = new Vector2(90, 28);
        weatherRandomLabel.AddThemeFontSizeOverride("font_size", 13);
        weatherRandomHBox.AddChild(weatherRandomLabel);
        var weatherRandomCheck = new CheckBox();
        weatherRandomCheck.ButtonPressed = WeatherSystem.RandomWeather;
        weatherRandomCheck.CustomMinimumSize = new Vector2(28, 28);
        weatherRandomCheck.Toggled += (pressed) => WeatherSystem.SetRandomWeather(pressed);
        weatherRandomHBox.AddChild(weatherRandomCheck);
        var weatherRandomHint = new Label();
        TranslateGroup.SetText(weatherRandomHint, "(关闭=保持当前天气)");
        weatherRandomHint.AddThemeFontSizeOverride("font_size", 10);
        weatherRandomHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        weatherRandomHBox.AddChild(weatherRandomHint);
        globalConfigContainer.AddChild(weatherRandomHBox);

        // ✅ issue#1 区域天气总开关（= Enable Local Weather）；基底天气已废除，全球天气只听 CO（挨着随机轮换开关）
        var wm = (Engine.GetMainLoop() as SceneTree)?.GetFirstNodeInGroup("weather_manager") as WeatherManager;
        var localWeatherHBox = new HBoxContainer();
        localWeatherHBox.AddThemeConstantOverride("separation", 10);
        var localWeatherLabel = new Label();
        TranslateGroup.SetText(localWeatherLabel, "区域天气:");
        localWeatherLabel.CustomMinimumSize = new Vector2(90, 28);
        localWeatherLabel.AddThemeFontSizeOverride("font_size", 13);
        localWeatherHBox.AddChild(localWeatherLabel);
        var localWeatherCheck = new CheckBox();
        localWeatherCheck.ButtonPressed = wm?.localWeatherEnabled ?? false;
        localWeatherCheck.CustomMinimumSize = new Vector2(28, 28);
        localWeatherCheck.Toggled += (pressed) => { if (wm != null) wm.localWeatherEnabled = pressed; };
        localWeatherHBox.AddChild(localWeatherCheck);
        var regionalWeatherEditBtn = new Button { CustomMinimumSize = new Vector2(112, 30) };
        TranslateGroup.SetText(regionalWeatherEditBtn, "格子涂抹 / 删除");
        regionalWeatherEditBtn.Pressed += () =>
        {
            if (!IsEditMode)
            {
                ShowImportHint(TranslateGroup.T("请先进入编辑模式，再使用区域天气画笔"));
                return;
            }
            localWeatherCheck.ButtonPressed = true;
            OpenRegionalWeatherEditor();
        };
        localWeatherHBox.AddChild(regionalWeatherEditBtn);
        globalConfigContainer.AddChild(localWeatherHBox);

        // 出现权重（晴/雨/雪/沙暴/雾）
        var weightNames = new[] { "晴权重", "雨权重", "雪权重", "沙暴权重", "雾权重" };
        for (int wi = 0; wi < 5; wi++)
        {
            var weightHBox = new HBoxContainer();
            weightHBox.AddThemeConstantOverride("separation", 10);
            var weightLabel = new Label();
            TranslateGroup.SetText(weightLabel, weightNames[wi] + ":");
            weightLabel.CustomMinimumSize = new Vector2(90, 26);
            weightLabel.AddThemeFontSizeOverride("font_size", 12);
            weightHBox.AddChild(weightLabel);
            var weightSpin = new SpinBox();
            weightSpin.MinValue = 0;
            weightSpin.MaxValue = 100;
            weightSpin.Step = 5;
            weightSpin.Value = WeatherSystem.Chances[wi];
            weightSpin.CustomMinimumSize = new Vector2(90, 26);
            weightSpin.AddThemeFontSizeOverride("font_size", 12);
            int widx = wi;
            weightSpin.ValueChanged += (newVal) => WeatherSystem.Chances[widx] = (int)newVal;
            weightHBox.AddChild(weightSpin);
            globalConfigContainer.AddChild(weightHBox);
        }
        var weightHint = new Label();
        TranslateGroup.SetText(weightHint, "(总和参考100，按比例抽取；雾默认0=仅CO技能/规则指定)");
        weightHint.AddThemeFontSizeOverride("font_size", 10);
        weightHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        globalConfigContainer.AddChild(weightHint);

        // 随机持续天数（min~max）
        var durationHBox = new HBoxContainer();
        durationHBox.AddThemeConstantOverride("separation", 8);
        var durationLabel = new Label();
        TranslateGroup.SetText(durationLabel, "随机持续天数:");
        durationLabel.CustomMinimumSize = new Vector2(90, 26);
        durationLabel.AddThemeFontSizeOverride("font_size", 12);
        durationHBox.AddChild(durationLabel);
        var durMinSpin = new SpinBox();
        durMinSpin.MinValue = 1;
        durMinSpin.MaxValue = 7;
        durMinSpin.Value = WeatherSystem.RandomDurationMin;
        durMinSpin.CustomMinimumSize = new Vector2(70, 26);
        durMinSpin.AddThemeFontSizeOverride("font_size", 12);
        durationHBox.AddChild(durMinSpin);
        var durationMidLabel = new Label();
        durationMidLabel.Text = "~";
        durationMidLabel.AddThemeFontSizeOverride("font_size", 12);
        durationHBox.AddChild(durationMidLabel);
        var durMaxSpin = new SpinBox();
        durMaxSpin.MinValue = 1;
        durMaxSpin.MaxValue = 7;
        durMaxSpin.Value = WeatherSystem.RandomDurationMax;
        durMaxSpin.CustomMinimumSize = new Vector2(70, 26);
        durMaxSpin.AddThemeFontSizeOverride("font_size", 12);
        durationHBox.AddChild(durMaxSpin);
        globalConfigContainer.AddChild(durationHBox);

        // 联动约束：min ≤ max
        durMinSpin.ValueChanged += (newVal) => {
            WeatherSystem.RandomDurationMin = Mathf.Clamp((int)newVal, 1, 7);
            if (durMaxSpin.Value < WeatherSystem.RandomDurationMin)
                durMaxSpin.Value = WeatherSystem.RandomDurationMin;
        };
        durMaxSpin.ValueChanged += (newVal) => {
            WeatherSystem.RandomDurationMax = Mathf.Clamp((int)newVal, 1, 7);
            if (durMinSpin.Value > WeatherSystem.RandomDurationMax)
                durMinSpin.Value = WeatherSystem.RandomDurationMax;
        };

        // ✅ CO 编辑器（自定义 CO + 头像上传，存 user://co/）
        var coEditorSection = CreateConfigSectionTitle(TranslateGroup.T("🎖️ CO 系统"));
        globalConfigContainer.AddChild(coEditorSection);

        var coEditorBtn = new Button();
        TranslateGroup.SetText(coEditorBtn, "🎖️ 打开 CO 编辑器（自定义CO/头像/30模块拼装）");
        coEditorBtn.CustomMinimumSize = new Vector2(320, 40);
        coEditorBtn.AddThemeFontSizeOverride("font_size", 13);
        coEditorBtn.Pressed += () => {
            var ui = GetTree()?.GetFirstNodeInGroup("co_editor_ui") as CoEditorUI;
            if (ui == null)
            {
                ui = new CoEditorUI();
                ui.Name = "CoEditorUI";
                GetTree().Root.AddChild(ui);
            }
            ui.Open();
        };
        globalConfigContainer.AddChild(coEditorBtn);

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

        // ✅ N屏地图：屏数（每屏均为上述宽×高，横向并列成一张大图）
        var screensLabel = new Label();
        TranslateGroup.SetText(screensLabel, "屏数:");
        screensLabel.CustomMinimumSize = new Vector2(40, 28);
        screensLabel.AddThemeFontSizeOverride("font_size", 13);
        mapGenHBox.AddChild(screensLabel);

        var screensSpin = new SpinBox();
        screensSpin.Name = "MapScreensSpin";
        screensSpin.MinValue = 1;
        screensSpin.MaxValue = 100;
        screensSpin.Value = gridManager?.GetScreenCount() ?? 1;
        screensSpin.CustomMinimumSize = new Vector2(60, 28);
        screensSpin.AddThemeFontSizeOverride("font_size", 12);

        var shapeLabel = new Label();
        shapeLabel.AddThemeFontSizeOverride("font_size", 12);
        TranslateGroup.SetText(shapeLabel, "形状");
        mapGenHBox.AddChild(shapeLabel);
        var shapeOpt = new OptionButton();
        shapeOpt.Name = "MapShapeOpt";
        shapeOpt.CustomMinimumSize = new Vector2(72, 28);
        shapeOpt.AddThemeFontSizeOverride("font_size", 12);
        foreach (var sn in new[] { "矩形", "菱形", "圆形", "椭圆", "三角形" }) shapeOpt.AddItem(TranslateGroup.T(sn));
        mapGenHBox.AddChild(shapeOpt);
        var gridLineCheck = new CheckBox();
        gridLineCheck.Name = "GridLineCheck";
        gridLineCheck.AddThemeFontSizeOverride("font_size", 12);
        TranslateGroup.SetText(gridLineCheck, "格线");
        gridLineCheck.Toggled += (on) =>
        {
            if (gridManager == null) return;
            var ov = gridManager.GetNodeOrNull<GridLineOverlay>("GridLineOverlay");
            if (on)
            {
                if (ov == null) { ov = new GridLineOverlay { Name = "GridLineOverlay" }; gridManager.AddChild(ov); }
                ov.Setup(gridManager);
            }
            else if (ov != null) ov.QueueFree();
        };
        mapGenHBox.AddChild(gridLineCheck);
        var brushCheck = new CheckBox();
        brushCheck.Name = "CellBrushCheck";
        brushCheck.AddThemeFontSizeOverride("font_size", 12);
        TranslateGroup.SetText(brushCheck, "格子画笔");
        brushCheck.Toggled += (on) =>
        {
            isCellBrushMode = on;
            if (on && gridManager != null)
            {
                var glc = mapGenHBox.GetNodeOrNull<CheckBox>("GridLineCheck");
                if (glc != null) glc.ButtonPressed = true;   // 开笔强制格线显示（开创人规矩）
                var bo = mapGenHBox.GetNodeOrNull<OptionButton>("BrushScreenOpt");
                if (bo != null)
                {
                    bo.Clear();
                    bo.AddItem(TranslateGroup.T("所有屏"));
                    for (int s = 0; s < gridManager.screenCount; s++)
                        bo.AddItem($"{TranslateGroup.T("屏")}{s + 1}");
                    bo.Selected = 0;
                }
            }
        };
        mapGenHBox.AddChild(brushCheck);
        var brushOpt = new OptionButton();
        brushOpt.Name = "BrushScreenOpt";
        brushOpt.CustomMinimumSize = new Vector2(72, 28);
        brushOpt.AddThemeFontSizeOverride("font_size", 12);
        brushOpt.AddItem(TranslateGroup.T("所有屏"));
        brushOpt.ItemSelected += (sel) => { brushTargetScreen = (int)sel - 1; };   // 0=所有屏
        mapGenHBox.AddChild(brushOpt);
        mapGenHBox.AddChild(screensSpin);

        // ✅ 每屏独立宽（容器先声明，生成按钮的回调要用）
        var perScreenWidthBox = new VBoxContainer();
        var screenPreviewStrip = new HBoxContainer();
        void RebuildScreenPreview()
        {
            foreach (var c in screenPreviewStrip.GetChildren()) c.QueueFree();
            int n = (int)screensSpin.Value;
            if (n <= 1) return;
            int total = 0; var ws = new int[n];
            for (int i = 0; i < n; i++)
            {
                var sp = perScreenWidthBox.GetNodeOrNull<SpinBox>($"ScreenW{i}");
                ws[i] = sp != null ? (int)sp.Value : (int)widthSpin.Value;
                total += ws[i];
            }
            for (int i = 0; i < n; i++)
            {
                var pv = new ColorRect
                {
                    CustomMinimumSize = new Vector2(Math.Max(10, 340 * ws[i] / total), 20),
                    Color = new Color(0.25f + 0.18f * (i % 4), 0.55f, 0.9f - 0.12f * (i % 3), 0.9f),
                    TooltipText = $"屏{i + 1}: {ws[i]}列",
                };
                screenPreviewStrip.AddChild(pv);
            }
        }
        void RebuildPerScreenWidthUI()
        {
            foreach (var c in perScreenWidthBox.GetChildren()) c.QueueFree();
            int n = (int)screensSpin.Value;
            if (n <= 1) { RebuildScreenPreview(); return; }
            for (int i = 0; i < n; i++)
            {
                var r = new HBoxContainer();
                var lb = new Label(); TranslateGroup.SetText(lb, $"屏{i + 1} 宽:"); lb.CustomMinimumSize = new Vector2(56, 24);
                r.AddChild(lb);
                var sp = new SpinBox { MinValue = 5, MaxValue = 300, Value = (gridManager != null && i < gridManager.screenWidths.Length) ? gridManager.screenWidths[i] : (int)widthSpin.Value, CustomMinimumSize = new Vector2(90, 26) };
                sp.Name = $"ScreenW{i}";   // ✅ 取真实各屏宽，整体宽只是缺省
                sp.ValueChanged += (v) => RebuildScreenPreview();
                r.AddChild(sp);
                var sph = new SpinBox { MinValue = 5, MaxValue = 300, Value = (gridManager != null && gridManager.screenHeights != null && i < gridManager.screenHeights.Length && gridManager.screenHeights[i] > 0) ? gridManager.screenHeights[i] : (int)heightSpin.Value, CustomMinimumSize = new Vector2(90, 26) };   // ✅ 取真实各屏高
                sph.Name = $"ScreenH{i}";
                r.AddChild(new Label { Text = TranslateGroup.T("高:") });
                r.AddChild(sph);
                perScreenWidthBox.AddChild(r);
            }
            RebuildScreenPreview();
        }
        screensSpin.ValueChanged += (v) => RebuildPerScreenWidthUI();
        widthSpin.ValueChanged += (v) => RebuildScreenPreview();

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
            int sc = (int)screensSpin.Value;
            int[] ws = null; int[] hs = null;
            if (sc > 0)
            {
                ws = new int[sc]; hs = new int[sc];
                for (int k = 0; k < sc; k++)
                {
                    var sp = perScreenWidthBox.GetNodeOrNull<SpinBox>($"ScreenW{k}");
                    var sh = perScreenWidthBox.GetNodeOrNull<SpinBox>($"ScreenH{k}");
                    ws[k] = sp != null ? (int)sp.Value : w;
                    hs[k] = sh != null ? (int)sh.Value : h;
                    if (sp == null || sh == null) ShowImportHint(TranslateGroup.T("⚠️ 部分屏未单独设置宽高，已用整体宽高兜底（分屏优先，整体仅缺省）"));
                }
            }
            var shapeOpt2 = mapGenHBox.GetNodeOrNull<OptionButton>("MapShapeOpt");
            GenerateNewMap(w, h, sc, ws, hs, (MapShape)(shapeOpt2?.Selected ?? 0));
        };
        mapGenHBox.AddChild(genMapBtn);

        globalConfigContainer.AddChild(mapGenHBox);
        globalConfigContainer.AddChild(perScreenWidthBox);
        globalConfigContainer.AddChild(screenPreviewStrip);
        RebuildPerScreenWidthUI();

        // ✅ N屏地图：屏数与传送管理
        var nscreensSection = CreateConfigSectionTitle(TranslateGroup.T("🖥️ N屏与传送"));
        globalConfigContainer.AddChild(nscreensSection);

        var nscreensHBox = new HBoxContainer();
        nscreensHBox.AddThemeConstantOverride("separation", 10);

        var curScreensLabel = new Label();
        TranslateGroup.SetText(curScreensLabel, "当前屏数:");
        curScreensLabel.CustomMinimumSize = new Vector2(70, 28);
        curScreensLabel.AddThemeFontSizeOverride("font_size", 13);
        nscreensHBox.AddChild(curScreensLabel);

        var curScreensSpin = new SpinBox();
        curScreensSpin.Name = "CurrentScreensSpin";
        curScreensSpin.MinValue = 1;
        curScreensSpin.MaxValue = 100;
        curScreensSpin.Value = gridManager?.GetScreenCount() ?? 1;
        curScreensSpin.CustomMinimumSize = new Vector2(60, 28);
        curScreensSpin.AddThemeFontSizeOverride("font_size", 12);
        nscreensHBox.AddChild(curScreensSpin);

        var applyScreensBtn = new Button();
        TranslateGroup.SetText(applyScreensBtn, "应用屏数");
        applyScreensBtn.CustomMinimumSize = new Vector2(90, 30);
        applyScreensBtn.AddThemeFontSizeOverride("font_size", 12);
        applyScreensBtn.Pressed += () => {
            if (gridManager == null) return;
            gridManager.screenCount = (int)curScreensSpin.Value;
            gridManager.RefreshScreenIds();
            ShowImportHint($"✅ {TranslateGroup.T("已应用屏数")}: {gridManager.GetScreenCount()}");
        };
        nscreensHBox.AddChild(applyScreensBtn);

        globalConfigContainer.AddChild(nscreensHBox);

        // 传送编辑模式：点选源格→目标格成链（关闭面板后点地图格子）
        var tpEditBtn = new Button();
        TranslateGroup.SetText(tpEditBtn, "🔗 进入传送编辑模式");
        tpEditBtn.CustomMinimumSize = new Vector2(300, 36);
        tpEditBtn.AddThemeFontSizeOverride("font_size", 13);
        tpEditBtn.Pressed += () => {
            // ✅ 拨动式（修复：再按无法退出→卡死生成菜单）
            if (isTeleportLinkMode) CancelTeleportLinkMode();
            else EnterTeleportLinkMode();
            TranslateGroup.SetText(tpEditBtn, isTeleportLinkMode ? "⏹ 退出传送编辑模式" : "🔗 进入传送编辑模式");
        };
        globalConfigContainer.AddChild(tpEditBtn);

        // 互传默认开关
        var tpMutualCheck = new CheckButton();
        TranslateGroup.SetText(tpMutualCheck, "互传模式（默认单向）");
        tpMutualCheck.ButtonPressed = teleportMutualDefault;
        tpMutualCheck.Toggled += (on) => teleportMutualDefault = on;
        globalConfigContainer.AddChild(tpMutualCheck);

        // 传送链路列表（进入面板时刷新）
        var tpListContainer = new VBoxContainer();
        tpListContainer.Name = "TeleportListContainer";
        globalConfigContainer.AddChild(tpListContainer);
        RefreshTeleportPanelList(tpListContainer);

        // ✅ 盟友机制：俩势力一伙（运行时全局生效：不能互攻、AI不视为目标、共胜负）
        var allianceSection = CreateConfigSectionTitle(TranslateGroup.T("🤝 同盟设置"));
        globalConfigContainer.AddChild(allianceSection);

        var allianceListContainer = new VBoxContainer();
        allianceListContainer.Name = "AllianceListContainer";

        var allianceHBox = new HBoxContainer();
        allianceHBox.AddThemeConstantOverride("separation", 10);

        var factionTeams = new string[] { TeamHelper.Player1, TeamHelper.Player2, TeamHelper.Player3, TeamHelper.Player4, TeamHelper.Player5, TeamHelper.Player6, TeamHelper.Player7, TeamHelper.Player8 };
        var allyA = new OptionButton();
        var allyB = new OptionButton();
        foreach (var ft in factionTeams)
        {
            allyA.AddItem(ft);
            allyB.AddItem(ft);
        }
        allyA.Selected = 0;
        allyB.Selected = 2;
        allyA.CustomMinimumSize = new Vector2(110, 28);
        allyB.CustomMinimumSize = new Vector2(110, 28);
        allianceHBox.AddChild(allyA);
        allianceHBox.AddChild(allyB);

        var allyToggleBtn = new Button();
        TranslateGroup.SetText(allyToggleBtn, "结盟/解盟");
        allyToggleBtn.CustomMinimumSize = new Vector2(100, 30);
        allyToggleBtn.AddThemeFontSizeOverride("font_size", 12);
        allyToggleBtn.Pressed += () => {
            string a = factionTeams[allyA.Selected];
            string b = factionTeams[allyB.Selected];
            bool now = TeamHelper.AreAllied(a, b);
            TeamHelper.SetAlliance(a, b, !now);
            ShowImportHint($"🤝 {a} × {b}: {(!now ? TranslateGroup.T("已结盟") : TranslateGroup.T("已解盟"))}");
            RefreshAlliancePanelList(allianceListContainer);
        };
        allianceHBox.AddChild(allyToggleBtn);

        globalConfigContainer.AddChild(allianceHBox);

        globalConfigContainer.AddChild(allianceListContainer);
        RefreshAlliancePanelList(allianceListContainer);

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
        GridType.RUINS, GridType.BROKENTRACK, GridType.LAVAFOG,
        // ✅ DoR新增：等离子/蔓生藤蔓/门（陨石毁→相连等离子消失；Gate门模块白名单通行）
        GridType.PLASMA, GridType.CREEPER, GridType.GATE,
        // ✅ CW墙系零成本项 + 三气候皮肤组
        GridType.WALL, GridType.WEAK_WALL, GridType.WASTELAND, GridType.RUINS2, GridType.RUINS3,
        GridType.DESERT_GROUND, GridType.SNOW_GROUND, GridType.WASTE_GROUND,
        GridType.DESERT_ROAD, GridType.SNOW_ROAD, GridType.WASTE_ROAD,
        GridType.DESERT_FOREST, GridType.SNOW_FOREST, GridType.WASTE_FOREST,
        GridType.DESERT_HILL, GridType.SNOW_HILL, GridType.WASTE_HILL,
        GridType.DESERT_RUINS, GridType.SNOW_RUINS, GridType.WASTE_RUINS,
        GridType.DESERT_WASTELAND, GridType.SNOW_WASTELAND, GridType.WASTE_WASTELAND,
        GridType.DESERT_PIPE, GridType.SNOW_PIPE, GridType.WASTE_PIPE,
        GridType.DESERT_PIPESEAM, GridType.SNOW_PIPESEAM, GridType.WASTE_PIPESEAM,
        GridType.DESERT_BROKENPIPE, GridType.SNOW_BROKENPIPE, GridType.WASTE_BROKENPIPE,
        // ✅ CoW补齐六地形
        GridType.FIRE, GridType.LAKE, GridType.ROUGH_SEA, GridType.STREET,
        GridType.PATH, GridType.DESERT_TRY_RIVER, GridType.MINE
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
        { GridType.LAVAFOG, new Color(0.7f, 0.2f, 0.1f) },
        { GridType.PLASMA, new Color(0.3f, 0.8f, 0.9f) },   // 等离子：青色
        { GridType.CREEPER, new Color(0.2f, 0.5f, 0.2f) },   // 蔓生藤蔓：深绿
        { GridType.GATE, new Color(0.6f, 0.5f, 0.3f) },     // 门：木色
        { GridType.WALL, new Color(0.62f, 0.58f, 0.52f) },
        { GridType.WEAK_WALL, new Color(0.55f, 0.5f, 0.44f) },
        { GridType.WASTELAND, new Color(0.45f, 0.38f, 0.28f) },
        { GridType.RUINS2, new Color(0.42f, 0.37f, 0.32f) },
        { GridType.RUINS3, new Color(0.44f, 0.39f, 0.33f) },
        { GridType.DESERT_GROUND, new Color(0.85f, 0.75f, 0.45f) },
        { GridType.DESERT_ROAD, new Color(0.9f, 0.82f, 0.6f) },
        { GridType.DESERT_FOREST, new Color(0.55f, 0.6f, 0.3f) },
        { GridType.DESERT_HILL, new Color(0.7f, 0.6f, 0.35f) },
        { GridType.DESERT_RUINS, new Color(0.72f, 0.62f, 0.45f) },
        { GridType.DESERT_WASTELAND, new Color(0.65f, 0.55f, 0.35f) },
        { GridType.DESERT_PIPE, new Color(0.75f, 0.65f, 0.4f) },
        { GridType.DESERT_PIPESEAM, new Color(0.8f, 0.7f, 0.45f) },
        { GridType.DESERT_BROKENPIPE, new Color(0.6f, 0.5f, 0.3f) },
        { GridType.SNOW_GROUND, new Color(0.85f, 0.9f, 0.95f) },
        { GridType.SNOW_ROAD, new Color(0.75f, 0.82f, 0.9f) },
        { GridType.SNOW_FOREST, new Color(0.55f, 0.7f, 0.75f) },
        { GridType.SNOW_HILL, new Color(0.7f, 0.78f, 0.88f) },
        { GridType.SNOW_RUINS, new Color(0.72f, 0.78f, 0.85f) },
        { GridType.SNOW_WASTELAND, new Color(0.65f, 0.72f, 0.82f) },
        { GridType.SNOW_PIPE, new Color(0.62f, 0.72f, 0.85f) },
        { GridType.SNOW_PIPESEAM, new Color(0.68f, 0.78f, 0.9f) },
        { GridType.SNOW_BROKENPIPE, new Color(0.5f, 0.6f, 0.72f) },
        { GridType.WASTE_GROUND, new Color(0.55f, 0.4f, 0.42f) },
        { GridType.WASTE_ROAD, new Color(0.62f, 0.46f, 0.46f) },
        { GridType.WASTE_FOREST, new Color(0.42f, 0.35f, 0.4f) },
        { GridType.WASTE_HILL, new Color(0.48f, 0.36f, 0.4f) },
        { GridType.WASTE_RUINS, new Color(0.52f, 0.42f, 0.44f) },
        { GridType.WASTE_WASTELAND, new Color(0.45f, 0.35f, 0.38f) },
        { GridType.WASTE_PIPE, new Color(0.58f, 0.45f, 0.5f) },
        { GridType.WASTE_PIPESEAM, new Color(0.63f, 0.5f, 0.55f) },
        { GridType.WASTE_BROKENPIPE, new Color(0.42f, 0.32f, 0.36f) },
        // ✅ CoW补齐六地形
        { GridType.FIRE, new Color(0.95f, 0.45f, 0.1f) },
        { GridType.LAKE, new Color(0.25f, 0.55f, 0.85f) },
        { GridType.ROUGH_SEA, new Color(0.15f, 0.35f, 0.65f) },
        { GridType.STREET, new Color(0.62f, 0.62f, 0.65f) },
        { GridType.PATH, new Color(0.72f, 0.6f, 0.45f) },
        { GridType.DESERT_TRY_RIVER, new Color(0.85f, 0.75f, 0.55f) },
        { GridType.MINE, new Color(0.3f, 0.3f, 0.3f) },
    };
}
