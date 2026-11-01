// GameManager.cs - 添加完整的单位统计系统
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// 由 GameManager.cs 拆出的共享类型

public enum UnitCategory
{
    Infantry,   
    Tank,       
    MdTank,     
    Vehicle,   
    Mech,       
    Oozium,     

    Rocket,     
    FlyBomb,    // 飞弹/自爆单位
    Artillery,
    APC, 
    Recon,
    AntiAir,
    AntiTank,
    PipeRunner,
    Bike,
    Flare,
    RepairBoat,
    Fighter,    // 战斗机
    Bomber,    // 轰炸机
    BCopter,    // 武装直升机
    TCopter,    // 运输直升机
    AntiAirMissile,    // 防空导弹车
    Stealth,    // 隐身战机
    Dog,    // 机械狗

    Battleship,    // 战列舰
    Cruiser,      // 巡洋舰
    Submarine,    // 潜艇
    Lander,       // 登陆艇
    Carrier,      // 航母
    Gunboat,      // 炮艇
    Neotank,        // 新型坦克
    Megatank,       // 巨型坦克
    WarTank,        // 弩级坦克
    Rig,            // 工程车
    Duster,         // 螺旋桨机
    Seaplane,       // 水上飞机
    ProducibleBoat, // 生产舰
    KamikazeBoat,   // 突击舰

    Other,       // 其他未分类单位
    PipeWorker,  // 管道构造者（黑洞稀有单位）
}
