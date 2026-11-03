using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · DamageMatrix（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{

[ExportGroup("攻防表")]
[Export] public bool counterUsePreDamageHp = false;   // 反击按挨打前血量计算（旧行为）；默认 false=AW 原作按挨打后血量
[Export] public Godot.Collections.Dictionary<string, int> attackMatrix = new();      // 主武器
[Export] public Godot.Collections.Dictionary<string, int> secondaryAttackMatrix = new(); // 副武器

// 默认副武器表（通用低伤害）
private static Dictionary<string, int> defaultSecondaryMatrix = null;
    private static Dictionary<string, int> defaultMatrix = null;
public void InitializeDefaultMatrix()
{

    if (defaultMatrix == null)
    {
        defaultMatrix = new Dictionary<string, int>
        {
            ["Infantry_Infantry"] = 55,   ["Infantry_Mech"] = 45,   ["Infantry_LightTank"] = 15,
            ["Infantry_Artillery"] = 25,  ["Infantry_Rocket"] = 25, ["Infantry_APC"] = 15,
            ["Infantry_Oozium"] = 35,       ["Infantry_MdTank"] = 4,
            ["Mech_Infantry"] = 65,       ["Mech_Mech"] = 55,       ["Mech_LightTank"] = 85,
            ["Mech_Artillery"] = 55,      ["Mech_Rocket"] = 65,     ["Mech_APC"] = 65,
            ["Mech_Oozium"] = 55,       ["Mech_MdTank"] = 15,
            ["LightTank_Infantry"] = 75,  ["LightTank_Mech"] = 70,  ["LightTank_LightTank"] = 55,
            ["LightTank_Artillery"] = 45, ["LightTank_Rocket"] = 55,["LightTank_APC"] = 45,
            ["LightTank_Oozium"] = 25,      ["LightTank_MdTank"] = 17,
            ["Artillery_Infantry"] = 90,  ["Artillery_Mech"] = 85,  ["Artillery_LightTank"] = 80,
            ["Artillery_Artillery"] = 70, ["Artillery_Rocket"] = 75,["Artillery_APC"] = 70,
            ["Artillery_Oozium"] = 15,      ["Artillery_MdTank"] = 45,
            ["Rocket_Infantry"] = 95,     ["Rocket_Mech"] = 90,     ["Rocket_LightTank"] = 90,
            ["Rocket_Artillery"] = 80,    ["Rocket_Rocket"] = 85,   ["Rocket_APC"] = 80,
            ["Rocket_Oozium"] = 20,         ["Rocket_MdTank"] = 55,
            ["Oozium_Infantry"] = 999,   ["Oozium_Mech"] = 999,    ["Oozium_LightTank"] = 999,
            ["Oozium_Artillery"] = 999,   ["Oozium_Rocket"] = 999,  ["Oozium_APC"] = 999,
            ["Oozium_Oozium"] = 999,       ["Oozium_MdTank"] = 999,
            ["APC_Infantry"] = 0,         ["APC_Mech"] = 0,         ["APC_LightTank"] = 0,
            ["APC_Artillery"] = 0,        ["APC_Rocket"] = 0,       ["APC_APC"] = 0,
            ["APC_Oozium"] = 0,         ["APC_MdTank"] = 0,
            ["MdTank_Infantry"] = 110,         ["MdTank_Mech"] = 95,         ["MdTank_LightTank"] = 85,
            ["MdTank_Artillery"] = 105,        ["MdTank_Rocket"] = 105,       ["MdTank_APC"] = 105,
            ["MdTank_Oozium"] = 45,         ["MdTank_MdTank"] = 55,
            ["AntiAir_Infantry"] = 105,     ["AntiAir_Mech"] = 105,         ["AntiAir_LightTank"] = 25,         
            ["AntiAir_Artillery"] = 50,     ["AntiAir_Rocket"] = 55,        ["AntiAir_APC"] = 50,           
            ["AntiAir_Oozium"] = 15,        ["AntiAir_MdTank"] = 10,        ["AntiAir_AntiAir"] = 45,
            ["Recon_Infantry"] = 70,        ["Recon_Mech"] = 65,           ["Recon_LightTank"] = 6,
            ["Recon_Artillery"] = 45,       ["Recon_Rocket"] = 55,         ["Recon_APC"] = 45,
            ["Recon_Oozium"] = 11,          ["Recon_MdTank"] = 1,          ["Recon_AntiAir"] = 4,
            ["Recon_Recon"] = 35,
            ["Infantry_Recon"] = 12,        ["Mech_Recon"] = 85,           ["LightTank_Recon"] = 85,
            ["Artillery_Recon"] = 80,       ["Rocket_Recon"] = 90,         ["APC_Recon"] = 0,
            ["Oozium_Recon"] = 999,         ["MdTank_Recon"] = 105,        ["AntiAir_Recon"] = 60,
            // ✅ FlyBomb：无武器，攻击值为0
            ["FlyBomb_Infantry"] = 0,     ["FlyBomb_Mech"] = 0,     ["FlyBomb_LightTank"] = 0,
            ["FlyBomb_Artillery"] = 0,    ["FlyBomb_Rocket"] = 0,   ["FlyBomb_APC"] = 0,
            ["FlyBomb_Oozium"] = 0,       ["FlyBomb_MdTank"] = 0,
            ["FlyBomb_AntiAir"] = 0,      ["FlyBomb_Recon"] = 0,    ["FlyBomb_FlyBomb"] = 0,
            // 其他单位打 FlyBomb：仅AntiAir有120，其他为0
            ["Infantry_FlyBomb"] = 0,     ["Mech_FlyBomb"] = 0,     ["LightTank_FlyBomb"] = 0,
            ["Artillery_FlyBomb"] = 0,    ["Rocket_FlyBomb"] = 0,   ["APC_FlyBomb"] = 0,
            ["Oozium_FlyBomb"] = 0,       ["MdTank_FlyBomb"] = 0,   ["Recon_FlyBomb"] = 0,
            ["AntiAir_FlyBomb"] = 120,
            // ✅ AntiTank：反坦克炮主武器数据
            ["AntiTank_Infantry"] = 75,   ["AntiTank_Mech"] = 65,    ["AntiTank_Recon"] = 75,
            ["AntiTank_AntiAir"] = 75,    ["AntiTank_LightTank"] = 75, ["AntiTank_MdTank"] = 65,
            ["AntiTank_Artillery"] = 65,  ["AntiTank_AntiTank"] = 55,  ["AntiTank_Rocket"] = 70,
            ["AntiTank_APC"] = 65,      ["AntiTank_FlyBomb"] = 0,
            // 其他单位打 AntiTank（主武器）
            ["Infantry_AntiTank"] = 0,    ["Mech_AntiTank"] = 55,    ["Recon_AntiTank"] = 0,
            ["AntiAir_AntiTank"] = 25,    ["LightTank_AntiTank"] = 30, ["MdTank_AntiTank"] = 35,
            ["Artillery_AntiTank"] = 55,  ["AntiTank_AntiTank"] = 75,  ["Rocket_AntiTank"] = 65,
            ["APC_AntiTank"] = 0,       ["FlyBomb_AntiTank"] = 0,
            // ✅ Flare / Bike 主武器数据
            ["Mech_Flare"] = 80,          ["Mech_Bike"] = 55,
            ["LightTank_Flare"] = 80,     ["LightTank_Bike"] = 70,
            ["MdTank_Flare"] = 90,        ["MdTank_Bike"] = 80,
            ["Artillery_Flare"] = 75,     ["Artillery_Bike"] = 85,
            ["Rocket_Flare"] = 85,        ["Rocket_Bike"] = 90,
            ["Oozium_Flare"] = 999,       ["Oozium_Bike"] = 999,
            ["AntiAir_Flare"] = 50,       ["AntiAir_Bike"] = 105,
            ["AntiTank_Flare"] = 75,      ["AntiTank_Bike"] = 65,
            ["PipeRunner_Infantry"] = 95,   ["PipeRunner_Mech"] = 90,
["PipeRunner_LightTank"] = 80,  ["PipeRunner_MdTank"] = 55,
["PipeRunner_Rocket"] = 85,     ["PipeRunner_Recon"] = 90,
["PipeRunner_APC"] = 80,        ["PipeRunner_AntiAir"] = 85,
["PipeRunner_Artillery"] = 80,  ["PipeRunner_FlyBomb"] = 120,
            ["Fighter_FlyBomb"] = 100,
            ["PipeRunner_Battleship"] = 55,
            ["PipeRunner_Carrier"] = 55,
            ["PipeRunner_Cruiser"] = 60,
            ["PipeRunner_Gunboat"] = 60,
            ["PipeRunner_Lander"] = 60,
            ["PipeRunner_Submarine"] = 60,
            ["PipeRunner_Bomber"] = 75,
            // ==== AWDS 官方值补录：机枪可对直升机（2026-11-03）====
            ["LightTank_BCopter"] = 10,
            ["LightTank_TCopter"] = 40,
            ["MdTank_BCopter"] = 12,
            ["MdTank_TCopter"] = 45,
            ["Recon_BCopter"] = 10,
            ["Recon_TCopter"] = 35,
            ["Infantry_BCopter"] = 7,
            ["Infantry_TCopter"] = 30,
            ["Mech_BCopter"] = 9,
            ["Mech_TCopter"] = 35,
            ["Rig_WarTank"] = 0,
            ["ProducibleBoat_WarTank"] = 0,
            ["Stealth_AntiAirMissile"] = 80,
            ["Fighter_Fighter"] = 55,
            ["AntiAir_Fighter"] = 65,
            ["PipeRunner_Fighter"] = 120,
            // 轰炸机：对地/对海毁灭打击（AW官方值），对空完全无助（无对空行=不能攻击）
            ["Bomber_Infantry"] = 110,
            ["Bomber_Mech"] = 110,
            ["Bomber_Bike"] = 105,
            ["Bomber_Oozium"] = 35,
            ["Bomber_Recon"] = 105,
            ["Bomber_APC"] = 105,
            ["Bomber_LightTank"] = 105,
            ["Bomber_MdTank"] = 95,
            ["Bomber_Artillery"] = 105,
            ["Bomber_Rocket"] = 105,
            ["Bomber_AntiAir"] = 95,
            ["Bomber_AntiTank"] = 105,
            ["Bomber_PipeRunner"] = 105,
            ["Bomber_Flare"] = 105,
            ["Bomber_RepairBoat"] = 75,
            // 轰炸机挨揍表
            ["Fighter_Bomber"] = 100,
            ["AntiAir_Bomber"] = 75,
            // 武装直升机主武器(导弹6发)：对车辆/兵器，对空无一格=打不了飞机
            ["BCopter_Recon"] = 55,      ["BCopter_APC"] = 60,
            ["BCopter_LightTank"] = 55,  ["BCopter_MdTank"] = 25,
            ["BCopter_Artillery"] = 65,  ["BCopter_Rocket"] = 65,
            ["BCopter_AntiAir"] = 25,    ["BCopter_AntiTank"] = 55,
            ["BCopter_PipeRunner"] = 55, ["BCopter_Flare"] = 55,
            ["BCopter_Bike"] = 55,       ["BCopter_Oozium"] = 25,
            ["BCopter_RepairBoat"] = 25,
            // 武装直升机副武器(机枪∞)：对步兵/直升机
            // 挨揍表：直升机系
            ["AntiAir_BCopter"] = 120,   ["Fighter_BCopter"] = 100,   ["PipeRunner_BCopter"] = 120,
            ["AntiAir_TCopter"] = 120, ["Fighter_TCopter"] = 100,   ["PipeRunner_TCopter"] = 120,
            // 防空导弹车(Anti-air Missile)：只对空，射程3-5（AW官方值）
            ["AntiAirMissile_Fighter"] = 100,  ["AntiAirMissile_Bomber"] = 100,
            ["AntiAirMissile_BCopter"] = 100,  ["AntiAirMissile_TCopter"] = 100,
            ["AntiAirMissile_FlyBomb"] = 120,
            // 防空导弹车挨揍表（AWDS矩阵Missiles列）：轻坦85 中坦105 轰105 武直65 步25 甲85
            ["Infantry_AntiAirMissile"] = 25,  ["Mech_AntiAirMissile"] = 85,
            ["Recon_AntiAirMissile"] = 28,     ["LightTank_AntiAirMissile"] = 85,
            ["MdTank_AntiAirMissile"] = 105,   ["Artillery_AntiAirMissile"] = 80,
            ["Rocket_AntiAirMissile"] = 90,    ["AntiAir_AntiAirMissile"] = 55,
            ["AntiTank_AntiAirMissile"] = 85,  ["PipeRunner_AntiAirMissile"] = 90,
            ["Flare_AntiAirMissile"] = 28,     ["Bike_AntiAirMissile"] = 10,
            ["Bomber_AntiAirMissile"] = 105,   ["BCopter_AntiAirMissile"] = 65,
            // 隐身战机(AWDS全量对空对地)：全能打，唯一例外水下潜艇
            ["Stealth_Infantry"] = 95,  ["Stealth_Mech"] = 90,    ["Stealth_Bike"] = 90,
            ["Stealth_Oozium"] = 15,    ["Stealth_Recon"] = 90,   ["Stealth_APC"] = 80,
            ["Stealth_LightTank"] = 80, ["Stealth_MdTank"] = 55,  ["Stealth_Artillery"] = 80,
            ["Stealth_Rocket"] = 85,    ["Stealth_AntiAir"] = 85, ["Stealth_AntiTank"] = 80,
            ["Stealth_PipeRunner"] = 80,["Stealth_Flare"] = 80,   ["Stealth_RepairBoat"] = 60,
            ["Stealth_Fighter"] = 65,   ["Stealth_Bomber"] = 75,  ["Stealth_BCopter"] = 105,
            ["Stealth_TCopter"] = 105,  ["Stealth_Stealth"] = 75, ["Stealth_FlyBomb"] = 120,
            // 隐身战机挨揍表（AWDS）：战斗机85 防空车75 导弹车100 管道炮75（打它必须先破隐）
            ["Fighter_Stealth"] = 85,   ["AntiAir_Stealth"] = 75,
            ["AntiAirMissile_Stealth"] = 100, ["PipeRunner_Stealth"] = 75,
["PipeRunner_PipeRunner"] = 80,["PipeRunner_Flare"] = 75,
["PipeRunner_AntiTank"] = 60,   ["PipeRunner_Bike"] = 90,
// ✅ 其他单位主武器攻击 PipeRunner
["AntiAir_PipeRunner"] = 85,    ["Artillery_PipeRunner"] = 70,
["MdTank_PipeRunner"] = 85,    ["Mech_PipeRunner"] = 55,
["PipeRunner_PipeRunner"] = 80,["Rocket_PipeRunner"] = 80,
["LightTank_PipeRunner"] = 55, ["AntiTank_PipeRunner"] = 85,
["Flare_PipeRunner"] = 12,      ["Bike_PipeRunner"] = 4,
            // ✅ RepairBoat：无武器，攻击值为0
            ["RepairBoat_Infantry"] = 0,  ["RepairBoat_Mech"] = 0,  ["RepairBoat_LightTank"] = 0,
            ["RepairBoat_Artillery"] = 0, ["RepairBoat_Rocket"] = 0,["RepairBoat_APC"] = 0,
            ["RepairBoat_Oozium"] = 0,    ["RepairBoat_MdTank"] = 0,["RepairBoat_FlyBomb"] = 0,
            ["RepairBoat_AntiAir"] = 0,   ["RepairBoat_Recon"] = 0, ["RepairBoat_Flare"] = 0,
            ["RepairBoat_Bike"] = 0,      ["RepairBoat_PipeRunner"] = 0,["RepairBoat_RepairBoat"] = 0,
            // 其他单位打 RepairBoat
            ["MdTank_RepairBoat"] = 35,   ["PipeRunner_RepairBoat"] = 60,
            ["Rocket_RepairBoat"] = 60,   ["LightTank_RepairBoat"] = 10,
            ["Artillery_RepairBoat"] = 55,["Infantry_RepairBoat"] = 0,
            ["Mech_RepairBoat"] = 0,      ["AntiAir_RepairBoat"] = 0,
            ["Recon_RepairBoat"] = 0,     ["Flare_RepairBoat"] = 0,
            ["Bike_RepairBoat"] = 0,      ["AntiTank_RepairBoat"] = 0,
            ["APC_RepairBoat"] = 0,       ["FlyBomb_RepairBoat"] = 0,

            // ==================== 海军线（AW3/AWDS 原作值；炮艇为 DoR 跨作适配） ====================
            // 战列舰（AWDS B Ship 行）：2-6 间接
            ["Battleship_Infantry"] = 95,  ["Battleship_Mech"] = 90,      ["Battleship_Bike"] = 90,
            ["Battleship_Recon"] = 90,     ["Battleship_APC"] = 80,       ["Battleship_LightTank"] = 80,
            ["Battleship_MdTank"] = 55,    ["Battleship_Artillery"] = 80, ["Battleship_Rocket"] = 85,
            ["Battleship_AntiAir"] = 85,   ["Battleship_AntiAirMissile"] = 90, ["Battleship_AntiTank"] = 55,
            ["Battleship_PipeRunner"] = 80,["Battleship_Flare"] = 90,     ["Battleship_Oozium"] = 20,
            ["Battleship_Dog"] = 90,      ["Battleship_RepairBoat"] = 95,
            ["Battleship_Battleship"] = 50, ["Battleship_Cruiser"] = 95, ["Battleship_Lander"] = 95,
            ["Battleship_Submarine"] = 95, ["Battleship_Carrier"] = 60,   ["Battleship_Gunboat"] = 95,
            // 巡洋舰（AWDS Cruiser 行）：对空 + 反潜
            ["Cruiser_Fighter"] = 85,      ["Cruiser_Bomber"] = 100,      ["Cruiser_BCopter"] = 105,
            ["Cruiser_TCopter"] = 105,     ["Cruiser_Stealth"] = 100,     ["Cruiser_FlyBomb"] = 120,
            ["Cruiser_Battleship"] = 5,    ["Cruiser_Cruiser"] = 25,      ["Cruiser_Lander"] = 25,
            ["Cruiser_Submarine"] = 90,    ["Cruiser_Carrier"] = 5,       ["Cruiser_Gunboat"] = 25,
            ["Cruiser_RepairBoat"] = 25,
            // 潜艇（AWDS Sub 行）：鱼雷只对海
            ["Submarine_Battleship"] = 65, ["Submarine_Cruiser"] = 25,    ["Submarine_Lander"] = 95,
            ["Submarine_Submarine"] = 55, ["Submarine_Carrier"] = 75,    ["Submarine_Gunboat"] = 95,
            ["Submarine_RepairBoat"] = 95,
            // 航母（AWDS Carrier 行）：防空导弹 3-8 间接，只对空
            ["Carrier_Fighter"] = 100,     ["Carrier_Bomber"] = 100,      ["Carrier_BCopter"] = 115,
            ["Carrier_TCopter"] = 115,     ["Carrier_Stealth"] = 100,     ["Carrier_FlyBomb"] = 120,
            // 炮艇（DoR 跨作适配：单发火箭，只对海 55）
            ["Gunboat_Battleship"] = 55,   ["Gunboat_Cruiser"] = 55,      ["Gunboat_Lander"] = 55,
            ["Gunboat_Submarine"] = 55,   ["Gunboat_Carrier"] = 55,      ["Gunboat_Gunboat"] = 55,
            ["Gunboat_RepairBoat"] = 55,
            // ===== 挨揍表：其他单位打海军（AW2/AWDS 原作值） =====
            // 轰炸机：对所有舰船 75
            ["Bomber_Battleship"] = 75,   ["Bomber_Cruiser"] = 75,       ["Bomber_Lander"] = 75,
            ["Bomber_Submarine"] = 75,    ["Bomber_Carrier"] = 75,       ["Bomber_Gunboat"] = 75,
            // 中坦（AW2 MdTank）：B舰55 巡洋25 登陆95 潜55
            ["MdTank_Battleship"] = 55,   ["MdTank_Cruiser"] = 25,       ["MdTank_Lander"] = 95,
            ["MdTank_Submarine"] = 55,    ["MdTank_Carrier"] = 55,       ["MdTank_Gunboat"] = 95,
            ["MdTank_RepairBoat"] = 95,
            // 轻坦（AW2 Tank）：对舰 chip 伤害
            ["LightTank_Battleship"] = 1, ["LightTank_Cruiser"] = 5,     ["LightTank_Lander"] = 10,
            ["LightTank_Submarine"] = 1,  ["LightTank_Carrier"] = 5,      ["LightTank_Gunboat"] = 10,
            // 火炮（AWDS Artillery 对海）
            ["Artillery_Battleship"] = 55,["Artillery_Cruiser"] = 55,    ["Artillery_Lander"] = 60,
            ["Artillery_Submarine"] = 60, ["Artillery_Carrier"] = 55,    ["Artillery_Gunboat"] = 60,
            // 火箭车（AWDS Rockets 对海）
            ["Rocket_Battleship"] = 55,   ["Rocket_Cruiser"] = 60,       ["Rocket_Lander"] = 60,
            ["Rocket_Submarine"] = 60,    ["Rocket_Carrier"] = 55,       ["Rocket_Gunboat"] = 60,
            // 隐身战机（AWDS Stealth 对海 60；水下潜艇打不了）
            ["Stealth_Battleship"] = 60, ["Stealth_Cruiser"] = 60,      ["Stealth_Lander"] = 60,
            ["Stealth_Carrier"] = 60,     ["Stealth_Gunboat"] = 60,      ["Stealth_Submarine"] = 0,

            // ✅ 矩阵空洞修复（2026-10-31审计：照明车机枪补全+互缺对补值）
            ["Flare_Infantry"] = 65,
            ["Flare_Mech"] = 50,
            ["Flare_Bike"] = 60,
            ["Flare_Recon"] = 65,
            ["Flare_APC"] = 45,
            ["Flare_LightTank"] = 35,
            ["Flare_MdTank"] = 25,
            ["Flare_Artillery"] = 45,
            ["Flare_Rocket"] = 45,
            ["Flare_AntiAir"] = 35,
            ["Flare_AntiTank"] = 35,
            ["Infantry_Flare"] = 55,
            ["Bike_Flare"] = 50,
            ["Recon_Flare"] = 70,
            ["Infantry_Bike"] = 65,
            ["Bike_Infantry"] = 70,
            ["Recon_Bike"] = 60,
            ["Bike_Recon"] = 35,
            ["AntiTank_Oozium"] = 35,
            ["PipeRunner_Oozium"] = 35,
            ["BCopter_TCopter"] = 65,
            
            ["Dog_Infantry"] = 28, ["Dog_Mech"] = 22, ["Dog_Bike"] = 32, ["Dog_Recon"] = 6,
            ["Dog_APC"] = 8, ["Dog_LightTank"] = 8, ["Dog_MdTank"] = 2, ["Dog_Artillery"] = 12,
            ["Dog_Rocket"] = 12, ["Dog_AntiAir"] = 12, ["Dog_AntiTank"] = 1, ["Dog_Oozium"] = 18,
            ["Dog_Flare"] = 28, ["Dog_AntiAirMissile"] = 12,
            ["AntiAir_Dog"] = 74, ["Artillery_Dog"] = 63, ["AntiTank_Dog"] = 52, ["Bike_Dog"] = 49,
            ["Bomber_Dog"] = 77, ["Flare_Dog"] = 46, ["Infantry_Dog"] = 38, ["LightTank_Dog"] = 52,
            ["MdTank_Dog"] = 77, ["Mech_Dog"] = 46, ["Oozium_Dog"] = 699, ["PipeRunner_Dog"] = 66,
            ["Recon_Dog"] = 49, ["Rocket_Dog"] = 66, ["Stealth_Dog"] = 66, ["Dog_Dog"] = 32,

            ["BCopter_Dog"] = 53, ["TCopter_Dog"] = 53,
            // ===== 原版补全 8 兵种（AWDS: 新型/巨型坦克 来自 AWBW 伤害表并保留本项目对原表的调参偏移；
            //       DoR: Rig/Duster/水上飞机/生产舰/突击舰 来自 DoR 原版伤害表；跨作组合按同定位兵种类比适配；
            //       弩级坦克=跨作原创值：攻防均取 重型坦克 与 巨型坦克 的平均，严格介于两者之间） =====
            ["Neotank_Infantry"] = 130, ["Neotank_Mech"] = 115, ["Neotank_Bike"] = 115, ["Neotank_LightTank"] = 105, ["Neotank_MdTank"] = 75, ["Neotank_Recon"] = 125,
            ["Neotank_APC"] = 125, ["Neotank_Artillery"] = 115, ["Neotank_Rocket"] = 125, ["Neotank_AntiAir"] = 11, ["Neotank_AntiTank"] = 40, ["Neotank_Flare"] = 105,
            ["Neotank_PipeRunner"] = 105, ["Neotank_RepairBoat"] = 100, ["Neotank_Oozium"] = 61, ["Neotank_FlyBomb"] = 0, ["Neotank_Fighter"] = 0, ["Neotank_Bomber"] = 0,
            ["Neotank_BCopter"] = 0, ["Neotank_TCopter"] = 0, ["Neotank_AntiAirMissile"] = 125, ["Neotank_Stealth"] = 0, ["Neotank_Dog"] = 125, ["Neotank_Battleship"] = 60,
            ["Neotank_Cruiser"] = 30, ["Neotank_Submarine"] = 60, ["Neotank_Lander"] = 100, ["Neotank_Carrier"] = 60, ["Neotank_Gunboat"] = 65, ["Neotank_Neotank"] = 55,
            ["Neotank_Megatank"] = 35, ["Neotank_Rig"] = 105, ["Neotank_Duster"] = 0, ["Neotank_Seaplane"] = 0, ["Neotank_ProducibleBoat"] = 12, ["Neotank_KamikazeBoat"] = 12,
            ["Neotank_WarTank"] = 55,
            ["Megatank_Infantry"] = 140, ["Megatank_Mech"] = 125, ["Megatank_Bike"] = 125, ["Megatank_LightTank"] = 180, ["Megatank_MdTank"] = 125, ["Megatank_Recon"] = 195,
            ["Megatank_APC"] = 195, ["Megatank_Artillery"] = 195, ["Megatank_Rocket"] = 195, ["Megatank_AntiAir"] = 91, ["Megatank_AntiTank"] = 64, ["Megatank_Flare"] = 168,
            ["Megatank_PipeRunner"] = 180, ["Megatank_RepairBoat"] = 165, ["Megatank_Oozium"] = 102, ["Megatank_FlyBomb"] = 0, ["Megatank_Fighter"] = 0, ["Megatank_Bomber"] = 0,
            ["Megatank_BCopter"] = 0, ["Megatank_TCopter"] = 0, ["Megatank_AntiAirMissile"] = 195, ["Megatank_Stealth"] = 0, ["Megatank_Dog"] = 135, ["Megatank_Battleship"] = 90,
            ["Megatank_Cruiser"] = 45, ["Megatank_Submarine"] = 90, ["Megatank_Lander"] = 135, ["Megatank_Carrier"] = 90, ["Megatank_Gunboat"] = 104, ["Megatank_Neotank"] = 115,
            ["Megatank_Megatank"] = 65, ["Megatank_Rig"] = 168, ["Megatank_Duster"] = 0, ["Megatank_Seaplane"] = 0, ["Megatank_ProducibleBoat"] = 19, ["Megatank_KamikazeBoat"] = 19,
            ["Megatank_WarTank"] = 95,
            ["Rig_Infantry"] = 0, ["Rig_Mech"] = 0, ["Rig_Bike"] = 0, ["Rig_LightTank"] = 0, ["Rig_MdTank"] = 0, ["Rig_Recon"] = 0,
            ["Rig_APC"] = 0, ["Rig_Artillery"] = 0, ["Rig_Rocket"] = 0, ["Rig_AntiAir"] = 0, ["Rig_AntiTank"] = 0, ["Rig_Flare"] = 0,
            ["Rig_PipeRunner"] = 0, ["Rig_RepairBoat"] = 0, ["Rig_Oozium"] = 0, ["Rig_FlyBomb"] = 0, ["Rig_Fighter"] = 0, ["Rig_Bomber"] = 0,
            ["Rig_BCopter"] = 0, ["Rig_TCopter"] = 0, ["Rig_AntiAirMissile"] = 0, ["Rig_Stealth"] = 0, ["Rig_Dog"] = 0, ["Rig_Battleship"] = 0,
            ["Rig_Cruiser"] = 0, ["Rig_Submarine"] = 0, ["Rig_Lander"] = 0, ["Rig_Carrier"] = 0, ["Rig_Gunboat"] = 0, ["Rig_Neotank"] = 0,
            ["Rig_Megatank"] = 0, ["Rig_Rig"] = 0, ["Rig_Duster"] = 0, ["Rig_Seaplane"] = 0, ["Rig_ProducibleBoat"] = 0, ["Rig_KamikazeBoat"] = 0,
            ["Duster_Infantry"] = 55, ["Duster_Mech"] = 45, ["Duster_Bike"] = 45, ["Duster_LightTank"] = 8, ["Duster_MdTank"] = 5, ["Duster_Recon"] = 18,
            ["Duster_APC"] = 15, ["Duster_Artillery"] = 15, ["Duster_Rocket"] = 20, ["Duster_AntiAir"] = 5, ["Duster_AntiTank"] = 5, ["Duster_Flare"] = 15,
            ["Duster_PipeRunner"] = 20, ["Duster_RepairBoat"] = 0, ["Duster_Oozium"] = 1, ["Duster_FlyBomb"] = 90, ["Duster_Fighter"] = 40, ["Duster_Bomber"] = 45,
            ["Duster_BCopter"] = 75, ["Duster_TCopter"] = 90, ["Duster_AntiAirMissile"] = 20, ["Duster_Stealth"] = 45, ["Duster_Dog"] = 55, ["Duster_Battleship"] = 0,
            ["Duster_Cruiser"] = 0, ["Duster_Submarine"] = 0, ["Duster_Lander"] = 0, ["Duster_Carrier"] = 0, ["Duster_Gunboat"] = 0, ["Duster_Neotank"] = 1,
            ["Duster_Megatank"] = 1, ["Duster_Rig"] = 15, ["Duster_Duster"] = 55, ["Duster_Seaplane"] = 45, ["Duster_ProducibleBoat"] = 0, ["Duster_KamikazeBoat"] = 0,
            ["Duster_WarTank"] = 3,
            ["Seaplane_Infantry"] = 90, ["Seaplane_Mech"] = 85, ["Seaplane_Bike"] = 85, ["Seaplane_LightTank"] = 75, ["Seaplane_MdTank"] = 65, ["Seaplane_Recon"] = 80,
            ["Seaplane_APC"] = 75, ["Seaplane_Artillery"] = 70, ["Seaplane_Rocket"] = 80, ["Seaplane_AntiAir"] = 45, ["Seaplane_AntiTank"] = 50, ["Seaplane_Flare"] = 80,
            ["Seaplane_PipeRunner"] = 80, ["Seaplane_RepairBoat"] = 85, ["Seaplane_Oozium"] = 55, ["Seaplane_FlyBomb"] = 95, ["Seaplane_Fighter"] = 45, ["Seaplane_Bomber"] = 55,
            ["Seaplane_BCopter"] = 85, ["Seaplane_TCopter"] = 95, ["Seaplane_AntiAirMissile"] = 70, ["Seaplane_Stealth"] = 55, ["Seaplane_Dog"] = 90, ["Seaplane_Battleship"] = 45,
            ["Seaplane_Cruiser"] = 40, ["Seaplane_Submarine"] = 55, ["Seaplane_Lander"] = 85, ["Seaplane_Carrier"] = 65, ["Seaplane_Gunboat"] = 105, ["Seaplane_Neotank"] = 55,
            ["Seaplane_Megatank"] = 38, ["Seaplane_Rig"] = 75, ["Seaplane_Duster"] = 65, ["Seaplane_Seaplane"] = 55, ["Seaplane_ProducibleBoat"] = 65, ["Seaplane_KamikazeBoat"] = 45,
            ["Seaplane_WarTank"] = 52,
            ["KamikazeBoat_Infantry"] = 75, ["KamikazeBoat_Mech"] = 70, ["KamikazeBoat_Bike"] = 70, ["KamikazeBoat_LightTank"] = 65, ["KamikazeBoat_MdTank"] = 50, ["KamikazeBoat_Recon"] = 70,
            ["KamikazeBoat_APC"] = 65, ["KamikazeBoat_Artillery"] = 70, ["KamikazeBoat_Rocket"] = 75, ["KamikazeBoat_AntiAir"] = 65, ["KamikazeBoat_AntiTank"] = 55, ["KamikazeBoat_Flare"] = 70,
            ["KamikazeBoat_PipeRunner"] = 75, ["KamikazeBoat_RepairBoat"] = 75, ["KamikazeBoat_Oozium"] = 40, ["KamikazeBoat_FlyBomb"] = 0, ["KamikazeBoat_Fighter"] = 0, ["KamikazeBoat_Bomber"] = 0,
            ["KamikazeBoat_BCopter"] = 0, ["KamikazeBoat_TCopter"] = 0, ["KamikazeBoat_AntiAirMissile"] = 75, ["KamikazeBoat_Stealth"] = 0, ["KamikazeBoat_Dog"] = 75, ["KamikazeBoat_Battleship"] = 45,
            ["KamikazeBoat_Cruiser"] = 65, ["KamikazeBoat_Submarine"] = 65, ["KamikazeBoat_Lander"] = 75, ["KamikazeBoat_Carrier"] = 50, ["KamikazeBoat_Gunboat"] = 95, ["KamikazeBoat_Neotank"] = 40,
            ["KamikazeBoat_Megatank"] = 28, ["KamikazeBoat_Rig"] = 65, ["KamikazeBoat_Duster"] = 0, ["KamikazeBoat_Seaplane"] = 0, ["KamikazeBoat_ProducibleBoat"] = 50, ["KamikazeBoat_KamikazeBoat"] = 45,
            ["KamikazeBoat_WarTank"] = 39,
            ["Infantry_Neotank"] = 4, ["Infantry_Megatank"] = 4, ["Infantry_Rig"] = 14, ["Infantry_WarTank"] = 4,
            ["Mech_Neotank"] = 15, ["Mech_Megatank"] = 5, ["Mech_Rig"] = 75, ["Mech_WarTank"] = 10,
            ["LightTank_Neotank"] = 17, ["LightTank_Megatank"] = 12, ["LightTank_Rig"] = 70, ["LightTank_ProducibleBoat"] = 8, ["LightTank_KamikazeBoat"] = 8, ["LightTank_WarTank"] = 14,
            ["MdTank_Neotank"] = 45, ["MdTank_Megatank"] = 25, ["MdTank_Rig"] = 90, ["MdTank_ProducibleBoat"] = 10, ["MdTank_KamikazeBoat"] = 10, ["MdTank_WarTank"] = 40,
            ["Recon_Neotank"] = 1, ["Recon_Megatank"] = 1, ["Recon_Rig"] = 45, ["Recon_WarTank"] = 1,
            ["Artillery_Neotank"] = 40, ["Artillery_Megatank"] = 15, ["Artillery_Rig"] = 70, ["Artillery_ProducibleBoat"] = 45, ["Artillery_KamikazeBoat"] = 45, ["Artillery_WarTank"] = 30,
            ["Rocket_Neotank"] = 50, ["Rocket_Megatank"] = 25, ["Rocket_Rig"] = 80, ["Rocket_ProducibleBoat"] = 55, ["Rocket_KamikazeBoat"] = 55, ["Rocket_WarTank"] = 40,
            ["AntiAir_Neotank"] = 5, ["AntiAir_Megatank"] = 1, ["AntiAir_Rig"] = 50, ["AntiAir_Duster"] = 75, ["AntiAir_Seaplane"] = 75, ["AntiAir_WarTank"] = 6,
            ["AntiTank_Neotank"] = 55, ["AntiTank_Megatank"] = 38, ["AntiTank_Rig"] = 65, ["AntiTank_WarTank"] = 52,
            ["Flare_Neotank"] = 1, ["Flare_Megatank"] = 1, ["Flare_Rig"] = 45, ["Flare_WarTank"] = 13,
            ["PipeRunner_Neotank"] = 50, ["PipeRunner_Megatank"] = 25, ["PipeRunner_Rig"] = 80, ["PipeRunner_Duster"] = 65, ["PipeRunner_Seaplane"] = 75, ["PipeRunner_ProducibleBoat"] = 60,
            ["PipeRunner_KamikazeBoat"] = 55, ["PipeRunner_WarTank"] = 40,
            ["Oozium_Neotank"] = 817, ["Oozium_Megatank"] = 454, ["Oozium_Rig"] = 999, ["Oozium_WarTank"] = 726,
            ["Fighter_Duster"] = 80, ["Fighter_Seaplane"] = 65,
            ["Bomber_Neotank"] = 90, ["Bomber_Megatank"] = 35, ["Bomber_Rig"] = 105, ["Bomber_ProducibleBoat"] = 85, ["Bomber_KamikazeBoat"] = 85, ["Bomber_WarTank"] = 65,
            ["BCopter_Neotank"] = 20, ["BCopter_Megatank"] = 10, ["BCopter_Rig"] = 65, ["BCopter_ProducibleBoat"] = 25, ["BCopter_KamikazeBoat"] = 25, ["BCopter_WarTank"] = 18,
            ["AntiAirMissile_Duster"] = 100, ["AntiAirMissile_Seaplane"] = 100,
            ["Stealth_Neotank"] = 45, ["Stealth_Megatank"] = 1, ["Stealth_Rig"] = 85, ["Stealth_Duster"] = 45, ["Stealth_Seaplane"] = 70, ["Stealth_ProducibleBoat"] = 45,
            ["Stealth_KamikazeBoat"] = 45, ["Stealth_WarTank"] = 28,
            ["Dog_Neotank"] = 2, ["Dog_Megatank"] = 1, ["Dog_Rig"] = 8, ["Dog_WarTank"] = 2,
            ["Battleship_Neotank"] = 50, ["Battleship_Megatank"] = 25, ["Battleship_Rig"] = 80, ["Battleship_ProducibleBoat"] = 60, ["Battleship_KamikazeBoat"] = 50, ["Battleship_WarTank"] = 40,
            ["Cruiser_Duster"] = 105, ["Cruiser_Seaplane"] = 105, ["Cruiser_ProducibleBoat"] = 38, ["Cruiser_KamikazeBoat"] = 38,
            ["Submarine_ProducibleBoat"] = 110, ["Submarine_KamikazeBoat"] = 80,
            ["Carrier_Duster"] = 100, ["Carrier_Seaplane"] = 100,
            ["Gunboat_ProducibleBoat"] = 40, ["Gunboat_KamikazeBoat"] = 40,
            ["WarTank_Infantry"] = 125, ["WarTank_Mech"] = 110, ["WarTank_Bike"] = 102, ["WarTank_LightTank"] = 132, ["WarTank_MdTank"] = 90, ["WarTank_Recon"] = 150,
            ["WarTank_APC"] = 150, ["WarTank_Artillery"] = 150, ["WarTank_Rocket"] = 150, ["WarTank_AntiAir"] = 46, ["WarTank_AntiTank"] = 50, ["WarTank_Flare"] = 129,
            ["WarTank_PipeRunner"] = 132, ["WarTank_RepairBoat"] = 130, ["WarTank_Oozium"] = 74, ["WarTank_FlyBomb"] = 0, ["WarTank_Fighter"] = 0, ["WarTank_Bomber"] = 0,
            ["WarTank_BCopter"] = 0, ["WarTank_TCopter"] = 0, ["WarTank_AntiAirMissile"] = 150, ["WarTank_Stealth"] = 0, ["WarTank_Dog"] = 106, ["WarTank_Battleship"] = 72,
            ["WarTank_Cruiser"] = 35, ["WarTank_Submarine"] = 72, ["WarTank_Lander"] = 115, ["WarTank_Carrier"] = 72, ["WarTank_Gunboat"] = 100, ["WarTank_Neotank"] = 80,
            ["WarTank_Megatank"] = 45, ["WarTank_Rig"] = 129, ["WarTank_Duster"] = 0, ["WarTank_Seaplane"] = 0, ["WarTank_ProducibleBoat"] = 14, ["WarTank_KamikazeBoat"] = 14,
            ["WarTank_WarTank"] = 60,

            // ✅ PipeWorker 管道构造者：装甲堪比新型坦克（承伤值同 Neotank）；自身无武器不反击
            ["Neotank_PipeWorker"] = 55, ["Megatank_PipeWorker"] = 115, ["Rig_PipeWorker"] = 0,
            ["Duster_PipeWorker"] = 1, ["Seaplane_PipeWorker"] = 55, ["KamikazeBoat_PipeWorker"] = 40,
            ["Infantry_PipeWorker"] = 4, ["Mech_PipeWorker"] = 15, ["LightTank_PipeWorker"] = 17,
            ["MdTank_PipeWorker"] = 45, ["Recon_PipeWorker"] = 1, ["Artillery_PipeWorker"] = 40,
            ["Rocket_PipeWorker"] = 50, ["AntiAir_PipeWorker"] = 5, ["AntiTank_PipeWorker"] = 55,
            ["Flare_PipeWorker"] = 1, ["PipeRunner_PipeWorker"] = 50, ["Oozium_PipeWorker"] = 817,
            ["Bomber_PipeWorker"] = 90, ["BCopter_PipeWorker"] = 20, ["Stealth_PipeWorker"] = 45,
            ["Dog_PipeWorker"] = 2, ["Battleship_PipeWorker"] = 50, ["WarTank_PipeWorker"] = 80,
            ["ProducibleBoat_PipeWorker"] = 0, ["Bike_PipeWorker"] = 1,
};
    }

    if (defaultSecondaryMatrix == null)
    {
        defaultSecondaryMatrix = new Dictionary<string, int>
        {
            ["BCopter_Infantry"] = 75,  ["BCopter_Mech"] = 75,
            ["BCopter_BCopter"] = 65,   ["BCopter_TCopter"] = 95,
            // 步兵副武器（机枪）——通用但低
            ["Infantry_Infantry"] = 45,   ["Infantry_Mech"] = 35,   ["Infantry_LightTank"] = 5,
            ["Infantry_Artillery"] = 15,  ["Infantry_Rocket"] = 15, ["Infantry_APC"] = 5,
            ["Infantry_Oozium"] = 30,       ["Infantry_MdTank"] = 0,
            
            // 机甲副武器（机枪）——通用
            ["Mech_Infantry"] = 55,       ["Mech_Mech"] = 45,       ["Mech_LightTank"] = 15,
            ["Mech_Artillery"] = 25,      ["Mech_Rocket"] = 35,     ["Mech_APC"] = 25,
            ["Mech_Oozium"] = 25,       ["Mech_MdTank"] = 10,
            
            // 坦克副武器（同轴机枪）——只能打步兵
            ["LightTank_Infantry"] = 35,  ["LightTank_Mech"] = 25,  ["LightTank_LightTank"] = 5,
            ["LightTank_Artillery"] = 5,  ["LightTank_Rocket"] = 5,   ["LightTank_APC"] = 5,
            ["LightTank_Oozium"] = 10,      ["APC_MdTank"] = 2,
            ["Infantry_AntiAir"] = 5,     ["Infantry_PipeRunner"] = 5,
            ["Mech_AntiAir"] = 55,       ["Mech_PipeRunner"] = 6,
            ["LightTank_MdTank"] = 2,    ["LightTank_AntiAir"] = 5,  ["LightTank_PipeRunner"] = 6,
            ["MdTank_AntiAir"] = 1,      ["MdTank_PipeRunner"] = 8,
            ["Artillery_AntiAir"] = 0,   ["Artillery_PipeRunner"] = 0,
            ["Rocket_AntiAir"] = 0,      ["Rocket_PipeRunner"] = 0,
            ["APC_AntiAir"] = 0,         ["APC_PipeRunner"] = 0,
            ["Oozium_AntiAir"] = 0,      ["Oozium_AntiTank"] = 0,    ["Oozium_PipeRunner"] = 0,
            ["Recon_PipeRunner"] = 30,
            ["AntiAir_PipeRunner"] = 15,
            ["AntiTank_Oozium"] = 0,     ["AntiTank_PipeRunner"] = 0,
            ["Flare_PipeRunner"] = 25,
            ["Bike_PipeRunner"] = 4,
            ["FlyBomb_PipeRunner"] = 0,
            ["PipeRunner_Infantry"] = 0, ["PipeRunner_Mech"] = 0,     ["PipeRunner_LightTank"] = 0,["PipeRunner_MdTank"] = 0,
            ["PipeRunner_Artillery"] = 0,["PipeRunner_Rocket"] = 0,   ["PipeRunner_APC"] = 0,   ["PipeRunner_Oozium"] = 0,
            ["PipeRunner_Recon"] = 0,    ["PipeRunner_AntiAir"] = 0, ["PipeRunner_AntiTank"] = 0,["PipeRunner_Flare"] = 0,
            ["PipeRunner_Bike"] = 0,     ["PipeRunner_FlyBomb"] = 0, ["PipeRunner_PipeRunner"] = 0,

            
            // 火炮/火箭——无副武器（或极低自卫）
            ["Artillery_Infantry"] = 0,  ["Artillery_Mech"] = 0,  ["Artillery_LightTank"] = 0,
            ["Artillery_Artillery"] = 0,  ["Artillery_Rocket"] = 0,   ["Artillery_APC"] = 0,
            ["Artillery_Oozium"] = 0,       ["Artillery_MdTank"] = 0,
            
            ["Rocket_Infantry"] = 0,     ["Rocket_Mech"] = 0,     ["Rocket_LightTank"] = 0,
            ["Rocket_Artillery"] = 0,     ["Rocket_Rocket"] = 0,    ["Rocket_APC"] = 0,
            ["Rocket_Oozium"] = 0,      ["Rocket_MdTank"] = 0,
            
            // Oozium——无副武器
            ["Oozium_Infantry"] = 0,      ["Oozium_Mech"] = 0,      ["Oozium_LightTank"] = 0,
            ["Oozium_Artillery"] = 0,     ["Oozium_Rocket"] = 0,    ["Oozium_APC"] = 0,
            ["Oozium_Oozium"] = 0,      ["Oozium_MdTank"] = 0,
            
            // APC——无
            ["APC_Infantry"] = 0,         ["APC_Mech"] = 0,         ["APC_LightTank"] = 0,
            ["APC_Artillery"] = 0,        ["APC_Rocket"] = 0,       ["APC_APC"] = 0,
            ["APC_Oozium"] = 0,           ["APC_MdTank"] = 0,
            // ✅ RepairBoat 副武器数据（无副武器）
            ["RepairBoat_Infantry"] = 0,  ["RepairBoat_Mech"] = 0,  ["RepairBoat_LightTank"] = 0,
            ["RepairBoat_Artillery"] = 0, ["RepairBoat_Rocket"] = 0,["RepairBoat_APC"] = 0,
            ["RepairBoat_Oozium"] = 0,    ["RepairBoat_MdTank"] = 0,["RepairBoat_FlyBomb"] = 0,
            ["RepairBoat_AntiAir"] = 0,   ["RepairBoat_Recon"] = 0, ["RepairBoat_Flare"] = 0,
            ["RepairBoat_Bike"] = 0,      ["RepairBoat_PipeRunner"] = 0,["RepairBoat_RepairBoat"] = 0,
            // 其他单位副武器打 RepairBoat
            ["MdTank_RepairBoat"] = 10,   ["PipeRunner_RepairBoat"] = 0,
            ["Rocket_RepairBoat"] = 0,    ["LightTank_RepairBoat"] = 1,
            ["Artillery_RepairBoat"] = 0, ["Infantry_RepairBoat"] = 0,
            ["Mech_RepairBoat"] = 0,      ["AntiAir_RepairBoat"] = 0,
            ["Recon_RepairBoat"] = 0,     ["Flare_RepairBoat"] = 0,
            ["Bike_RepairBoat"] = 0,      ["AntiTank_RepairBoat"] = 0,
            ["APC_RepairBoat"] = 0,       ["FlyBomb_RepairBoat"] = 0,

            ["MdTank_Infantry"] = 55,         ["MdTank_Mech"] = 45,         ["MdTank_LightTank"] = 45,
            ["MdTank_Artillery"] = 55,        ["MdTank_Rocket"] = 45,       ["MdTank_APC"] = 55,
            ["MdTank_Oozium"] = 35,         ["MdTank_MdTank"] = 25,

            ["AntiAir_Infantry"] = 55,     ["AntiAir_Mech"] = 54,         ["AntiAir_LightTank"] = 5,         
            ["AntiAir_Artillery"] = 20,     ["AntiAir_Rocket"] = 25,        ["AntiAir_APC"] = 20,           
            ["AntiAir_Oozium"] = 10,        ["AntiAir_MdTank"] = 1,        ["AntiAir_AntiAir"] = 15,

            ["Recon_Infantry"] = 70,        ["Recon_Mech"] = 65,           ["Recon_LightTank"] = 6,
            ["Recon_Artillery"] = 45,       ["Recon_Rocket"] = 55,         ["Recon_APC"] = 45,
            ["Recon_Oozium"] = 11,          ["Recon_MdTank"] = 1,          ["Recon_AntiAir"] = 4,
            ["Recon_Recon"] = 35,

            ["Infantry_Recon"] = 12,        ["Mech_Recon"] = 18,           ["LightTank_Recon"] = 40,
            ["Artillery_Recon"] = 80,       ["Rocket_Recon"] = 90,         ["APC_Recon"] = 0,
            ["Oozium_Recon"] = 0,           ["MdTank_Recon"] = 45,         ["AntiAir_Recon"] = 60,
            // ✅ FlyBomb：无副武器
            ["FlyBomb_Infantry"] = 0,     ["FlyBomb_Mech"] = 0,     ["FlyBomb_LightTank"] = 0,
            ["FlyBomb_Artillery"] = 0,    ["FlyBomb_Rocket"] = 0,   ["FlyBomb_APC"] = 0,
            ["FlyBomb_Oozium"] = 0,       ["FlyBomb_MdTank"] = 0,
            ["FlyBomb_AntiAir"] = 0,      ["FlyBomb_Recon"] = 0,    ["FlyBomb_FlyBomb"] = 0,
            // 其他单位副武器打 FlyBomb：均为0
            ["Infantry_FlyBomb"] = 0,     ["Mech_FlyBomb"] = 0,     ["LightTank_FlyBomb"] = 0,
            ["Artillery_FlyBomb"] = 0,    ["Rocket_FlyBomb"] = 0,   ["APC_FlyBomb"] = 0,
            ["Oozium_FlyBomb"] = 0,       ["MdTank_FlyBomb"] = 0,   ["Recon_FlyBomb"] = 0,
            ["AntiAir_FlyBomb"] = 0,
            // ✅ AntiTank：反坦克炮无副武器 / 其他单位副武器打AntiTank
            ["AntiTank_Infantry"] = 0,  ["AntiTank_Mech"] = 0,   ["AntiTank_Recon"] = 0,
            ["AntiTank_AntiAir"] = 0,   ["AntiTank_LightTank"] = 0, ["AntiTank_MdTank"] = 0,
            ["AntiTank_Artillery"] = 0, ["AntiTank_AntiTank"] = 0,  ["AntiTank_Rocket"] = 0,
            ["AntiTank_APC"] = 0,      ["AntiTank_FlyBomb"] = 0,
            // 其他单位副武器打 AntiTank
            ["Infantry_AntiTank"] = 30, ["Mech_AntiTank"] = 30,  ["Recon_AntiTank"] = 25,
            ["AntiAir_AntiTank"] = 0,   ["LightTank_AntiTank"] = 1, ["MdTank_AntiTank"] = 1,
            ["Artillery_AntiTank"] = 0, ["AntiTank_AntiTank"] = 0,  ["Rocket_AntiTank"] = 0,
            ["APC_AntiTank"] = 0,      ["FlyBomb_AntiTank"] = 0,
            // ✅ Flare / Bike 副武器数据
            // Flare 副武器（机枪）
            ["Flare_Infantry"] = 80,      ["Flare_Mech"] = 70,          ["Flare_Bike"] = 70,
            ["Flare_Recon"] = 60,         ["Flare_Flare"] = 50,         ["Flare_AntiAir"] = 45,
            ["Flare_LightTank"] = 10,     ["Flare_MdTank"] = 5,         ["Flare_Artillery"] = 45,
            ["Flare_AntiTank"] = 25,      ["Flare_Rocket"] = 55,        ["Flare_APC"] = 45,
            ["Flare_Oozium"] = 20,        ["Flare_FlyBomb"] = 0,
            // Bike 副武器（机枪）
            ["Bike_Infantry"] = 65,       ["Bike_Mech"] = 55,           ["Bike_Bike"] = 55,
            ["Bike_Recon"] = 35,          ["Bike_Flare"] = 15,          ["Bike_AntiAir"] = 35,
            ["Bike_LightTank"] = 1,       ["Bike_MdTank"] = 1,          ["Bike_Artillery"] = 15,
            ["Bike_AntiTank"] = 35,       ["Bike_Rocket"] = 20,         ["Bike_APC"] = 15,
            ["Bike_Oozium"] = 35,         ["Bike_FlyBomb"] = 0,
            // 其他单位副武器攻击 Flare
            ["Infantry_Flare"] = 10,      ["Mech_Flare"] = 15,          ["Recon_Flare"] = 30,
            ["AntiAir_Flare"] = 0,        ["LightTank_Flare"] = 35,     ["MdTank_Flare"] = 35,
            ["Artillery_Flare"] = 0,      ["Rocket_Flare"] = 0,         ["APC_Flare"] = 0,
            ["Oozium_Flare"] = 0,         ["AntiTank_Flare"] = 0,       ["FlyBomb_Flare"] = 0,
            // 其他单位副武器攻击 Bike
            ["Infantry_Bike"] = 45,       ["Mech_Bike"] = 55,           ["Recon_Bike"] = 30,
            ["AntiAir_Bike"] = 55,        ["LightTank_Bike"] = 25,      ["MdTank_Bike"] = 45,
            ["Artillery_Bike"] = 0,       ["Rocket_Bike"] = 0,          ["APC_Bike"] = 0,
            ["Oozium_Bike"] = 0,          ["AntiTank_Bike"] = 0,        ["FlyBomb_Bike"] = 0,
            // ✅ 副武器攻击 PipeRunner
            ["Mech_PipeRunner"] = 6,        ["MdTank_PipeRunner"] = 8,
            ["LightTank_PipeRunner"] = 6,
            // ===== 原版补全 8 兵种：副武器（机枪打直升机 / 生产舰对空机炮 / 其余按参照兵种回退） =====
            ["Neotank_BCopter"] = 22, ["Neotank_TCopter"] = 55,
            ["Megatank_BCopter"] = 22, ["Megatank_TCopter"] = 55,
            ["ProducibleBoat_Infantry"] = 0, ["ProducibleBoat_Mech"] = 0, ["ProducibleBoat_Bike"] = 0, ["ProducibleBoat_LightTank"] = 0, ["ProducibleBoat_MdTank"] = 0, ["ProducibleBoat_Recon"] = 0,
            ["ProducibleBoat_APC"] = 0, ["ProducibleBoat_Artillery"] = 0, ["ProducibleBoat_Rocket"] = 0, ["ProducibleBoat_AntiAir"] = 0, ["ProducibleBoat_AntiTank"] = 0, ["ProducibleBoat_Flare"] = 0,
            ["ProducibleBoat_PipeRunner"] = 0, ["ProducibleBoat_RepairBoat"] = 0, ["ProducibleBoat_Oozium"] = 0, ["ProducibleBoat_FlyBomb"] = 55, ["ProducibleBoat_Fighter"] = 35, ["ProducibleBoat_Bomber"] = 35,
            ["ProducibleBoat_BCopter"] = 45, ["ProducibleBoat_TCopter"] = 55, ["ProducibleBoat_AntiAirMissile"] = 0, ["ProducibleBoat_Stealth"] = 35, ["ProducibleBoat_Dog"] = 0, ["ProducibleBoat_Battleship"] = 0,
            ["ProducibleBoat_Cruiser"] = 0, ["ProducibleBoat_Submarine"] = 0, ["ProducibleBoat_Lander"] = 0, ["ProducibleBoat_Carrier"] = 0, ["ProducibleBoat_Gunboat"] = 0, ["ProducibleBoat_Neotank"] = 0,
            ["ProducibleBoat_Megatank"] = 0, ["ProducibleBoat_Rig"] = 0, ["ProducibleBoat_Duster"] = 40, ["ProducibleBoat_Seaplane"] = 40, ["ProducibleBoat_ProducibleBoat"] = 0, ["ProducibleBoat_KamikazeBoat"] = 0,
            ["Bike_Neotank"] = 1, ["Bike_Megatank"] = 1, ["Bike_Rig"] = 20, ["Bike_WarTank"] = 0,
            ["WarTank_BCopter"] = 11, ["WarTank_TCopter"] = 28, ["WarTank_Infantry"] = 55, ["WarTank_Mech"] = 45, ["WarTank_Bike"] = 45, ["WarTank_LightTank"] = 45,
            ["WarTank_MdTank"] = 25, ["WarTank_Recon"] = 45, ["WarTank_APC"] = 55, ["WarTank_Artillery"] = 55, ["WarTank_Rocket"] = 45, ["WarTank_AntiAir"] = 1,
            ["WarTank_AntiTank"] = 1, ["WarTank_Flare"] = 35, ["WarTank_PipeRunner"] = 8, ["WarTank_RepairBoat"] = 10, ["WarTank_Oozium"] = 35, ["WarTank_FlyBomb"] = 0,
        };
    }
}
// 查主武器表
public virtual int GetPrimaryDamageFromMatrix(Infantry target) => GetMatrixDamage(target, false);

/// <summary>
/// ✅ 统一查表入口：隐身专属表（仅隐身中）→ 实例表 → 默认表。
/// stealthMatrixOverride 此前只写在从未被调用的 GetBaseDamageFromMatrix 里，实际攻击/反击都不生效。
/// assumeStealthed：AI 估算用，假设"此刻是否隐身"（null=按当前状态）。
/// </summary>
public int GetMatrixDamage(Infantry target, bool secondary, bool? assumeStealthed = null)
{
    if (target == null) return 0;
    // 键优先级：本ID_目标ID → 参照_目标ID → 本ID_目标参照 → 参照_目标参照（原有兵种四个键相同）
    var keys = MatrixKeys(UnitTypeId, TemplateId, target.UnitTypeId, target.TemplateId);
    // 隐身攻防表替换：隐身时同一张表同时替换主/副武器（填 0 = 隐身状态下打不了该目标）
    if ((assumeStealthed ?? isStealthed) && stealthMatrixOverride != null && stealthMatrixOverride.Count > 0)
        foreach (var k in keys) if (stealthMatrixOverride.TryGetValue(k, out int sval)) return sval;
    // ✅ 雾战攻防表：优先级 隐身表 > 雾战表 > 默认/模板表（雾中隐身=上面已用隐身表返回）
    if (FogActive() && fogMatrixOverride != null && fogMatrixOverride.Count > 0)
        foreach (var k in keys) if (fogMatrixOverride.TryGetValue(k, out int fval)) return fval;
    var inst = secondary ? secondaryAttackMatrix : attackMatrix;
    var def = secondary ? defaultSecondaryMatrix : defaultMatrix;
    if (inst != null) foreach (var k in keys) if (inst.TryGetValue(k, out int v)) return v;
    if (def != null) foreach (var k in keys) if (def.TryGetValue(k, out int v)) return v;
    return 0;
}

private static List<string> MatrixKeys(string att, string attTpl, string def, string defTpl)
{
    var r = new List<string>(4);
    foreach (var k in new[] { $"{att}_{def}", $"{attTpl}_{def}", $"{att}_{defTpl}", $"{attTpl}_{defTpl}" })
        if (!r.Contains(k)) r.Add(k);
    return r;
}

/// <summary>AI 生产评估：按兵种ID+参照兵种查默认攻防表</summary>
public static int LookupDefaultMatrix(string att, string attTpl, string def, string defTpl, bool secondary = false)
{
    var m = secondary ? defaultSecondaryMatrix : defaultMatrix;
    if (m == null) return 0;
    foreach (var k in MatrixKeys(att, attTpl ?? att, def, defTpl ?? def)) if (m.TryGetValue(k, out int v)) return v;
    return 0;
}

// ✅ AI 生产评估用：按类型名查默认攻防表（无实例时使用）
public static int LookupDefaultMatrix(string attackerType, string defenderType, bool secondary = false)
{
    var m = secondary ? defaultSecondaryMatrix : defaultMatrix;
    if (m == null) return 0;
    return m.TryGetValue($"{attackerType}_{defenderType}", out int v) ? v : 0;
}

// 查副武器表
public virtual int GetSecondaryDamageFromMatrix(Infantry target) => GetMatrixDamage(target, true);

public virtual WeaponType SelectWeaponByMatrix(Infantry target)
{
    // 有主武器弹药 → 优先主武器
    if (hasPrimaryWeapon && CanUsePrimaryWeapon())
    {
        int primaryDamage = GetPrimaryDamageFromMatrix(target);
        if (primaryDamage > 0) return WeaponType.Primary;
    }
    
    // 没弹药或主武器打不了 → 副武器
    if (hasSecondaryWeapon)
    {
        int secondaryDamage = GetSecondaryDamageFromMatrix(target);
        if (secondaryDamage > 0) return WeaponType.Secondary;
    }
    
    return WeaponType.None;
}
}
