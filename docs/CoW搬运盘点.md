# Commander Wars 全量搬运盘点(aw-1-3 适配评估)

> 来源:github.com/Advancedwarroom/Commander_Wars(CoW 是 C++/Qt 的开源 AW 复刻,贴图 16px/24px,需放大到项目的 32px)
> 兵器(Black Cannon 族等)另算,本清单不含落地实现,仅供模块规划。

## 一、地形(49 个脚本,去掉 __BASE 母版)

### A. 纯装饰/数值地形 — 直接适配现有地形模块(加 GridType + 贴图 + 移动消耗 + 防御星)

| CoW 名 | 说明 | 适配评估 |
|---|---|---|
| WASTELAND 荒地 | 标准地形 | ✅ 新 GridType 即可 |
| DESERT / SNOW / WASTE 沙漠/雪原/废土 | 三大"气候皮肤"组,每组含 FOREST/MOUNTAIN/RUIN/STREET/WASTELAND/WELD/PIPELINE 等全套变体 | ✅ 数值同平原系,换皮即可(工作量大,建议做成"皮肤包"模块) |
| FOREST1/2/3、STREET1、BRIDGE1/BRIDGE2(旧桥)、DESERT_ROCK、LAKE、ROUGH_SEA | 基础地形的视觉变体 | ✅ 换皮 |
| RUIN 系列(3 随机变体)+ 沙漠/雪/废土版 | 废墟 | ✅ 项目已有 RUINS,补变体贴图即可 |
| PLAINS_DESTROYED / DESTROYEDWELD 系列 | 管道毁坏后的残留地 | ✅ 项目已有 BROKENPIPE 同类 |
| REAF 珊瑚礁 | 隐藏类 | ✅ 已有 REEF |
| TELEPORTTILE 传送点 | 0 MP 穿越 | ✅ 项目已有 TP,数值已对齐 |

### B. 有特殊机制的地形 — 需要新模块/新逻辑

| CoW 名 | 机制 | 模块建议 |
|---|---|---|
| PLASMA 等离子 | 不可通行;陨石被毁→相连等离子消失 | ✅ **本次已做** |
| METEOR 陨石 | 可被攻击;被毁→相连等离子消失+变平原 | ✅ **本次已做**(METEORITE) |
| CREEPER 蔓生藤蔓 | **startOfTurn:踩上去的单位每回合扣 1 HP(不会打死,保留 0.1)** | ⚠️ 已做地形,伤害机制待加 |
| FIRE 火 | **雾战中照亮周围 5 格** | ⚠️ 可挂到 LAVAFOG 同款视野模块 |
| FOG 雾 | **getVisionHide:雾战中隐藏该地形本身** | ⚠️ 视野模块扩展 |
| WASTE 废土(DoR) | **站上去视野 +1** | ⚠️ 视野模块扩展 |
| WALL 墙 | 完全阻挡通行 | ✅ 简单(同 PLASMA 无伤害版) |
| WEAK_WALL 脆弱墙 | 可被攻击摧毁,毁后变平原 | ⚠️ 类似管道接缝,但更简单 |
| WELD 焊缝(普通/沙漠/雪/废土 4 皮肤 × N-S/E-W 两方向) | 管道连接点,可被攻击摧毁 | ✅ 项目已有 PIPESEAM,可加皮肤 |
| DESERT_TRY_RIVER 干涸河 | 数值地形 | ✅ 换皮 |
| CREEPER 的四方向/对角连接贴图 | 连接渲染 | ✅ 本次已做方向变体 |

## 二、建筑(设施类)

### A. 常规设施 — 适配现有 Facility 模块

| CoW 名 | 机制 |
|---|---|
| airport / factory / harbour / hq / town | 标准:生产/维修/收入(项目已有) |
| temporary_airport / temporary_harbour | 临时机场/港口(项目已有 TempAirPort/TempPort) |
| radar 雷达 | **雾战照亮 3 格**;有收入 |
| tower 通信塔 | **占领后全军攻击 +10% 防御 +10%**;有收入 |
| silo / silo_rocket 导弹井 | 步兵发射,13 格范围 3 HP 伤害(项目已有 MissileSilo) |
| mine 矿场 | 高收入,不能维修/补给 |
| oilrig 石油钻井 | 海上高收入,不能维修/补给 |
| pipestation 管道站 | **生产/维修/补给管道单位(Piperunner)**;有收入 |
| depot 仓库 | **陆军每回合补给一半油弹**;有掩体 |
| labor 工厂 | **生产/维修/补给 Oozium** |
| powerplant 电厂 | **占领后能量条充能速度 +%**(CoW 特色,配合 CO 大招条) |
| field_base 前线基地 | **占领判定胜负(类 HQ)**;不能生产/维修/补给;有收入 |
| forthq 要塞化总部 | **onDestroyed:被毁时触发事件**(Fortified HQ) |
| amphibiousfactory 两栖工厂 | 生产陆+海军 |
| zblack_building_destroyed 黑洞建筑废墟 | 纯装饰残骸 |

### B. 兵器类建筑 — 另算(兵器单独规划)

已有对应:Black Cannon 族(4 方向)、Deathray(4 方向)、Laser、Minicannon(4 方向)、Crystal、Fortress、Black Hole Factory(4 气候皮肤)、Grand Bolt(眼×3+中段×5 拼装)、Monolith=Black Obelisk、Talon Gun(+destroyed)、Nest(中心/工厂/激光×3/迫击炮×2 拼装)、Gate(+destroyed,2 方向)、Omega Base(装饰)、Volcano(4 皮肤)。

## 三、单位(77 个)

### A. 标准 AW 单位 — 适配现有 Infantry 模块

infantry, mech, motorbike, recon, flare, sniper(步兵反步兵,山上视野+3,可占领), light_tank, heavy_tank(=Md_Tank), megatank, neotank, artillery, rocketthrower(=Rocket), missile, apc, piperunner, artillerycraft, fighter, bomber, duster, stealthbomber(隐身:相邻敌军才现形), k_heli, t_heli, transportplane(运输机:载2地面,只能路/桥/机场装货,任意卸), waterplane(水上飞机), lander, black_boat, gunboat, cannonboat(导弹艇), torpedoboat, frigate, destroyer, cruiser, battlecruiser, battleship, aircraftcarrier, submarine, black_bomb(自爆:大范围爆炸伤所有邻近单位), flak(=AntiAir), antitankcannon(=Anti_Tank), hovercraft, heavy_hovercraft, hoverflak。

### B. 特殊机制单位 — 需要新模块

| 单位 | 机制 |
|---|---|
| hoellium (Oozium) | 碾压路径上所有单位即毁;天气免疫 | ✅ 项目已有 |
| watermine 水雷 | **隐形;非空军踩上触发爆炸 5 HP** |
| ZCOUNIT_INTEL_TRUCK 情报卡车 | **展开力场:周围己方单位全部隐身(仍可被攻击)** |
| ZCOUNIT_IRON_SHIELD_GENERATOR 铁盾发生器 | **给友军单位巨额减伤** |
| ZCOUNIT_CHAPERON 护卫 | **每回合可无效一次对己方单位的间接攻击(按存活数)** |
| ZCOUNIT_KIROV 基洛夫 | 空军巨兽:极高防御低机动,不吃油,对地海军 |
| ZCOUNIT_MISSILE_SUB 导弹潜艇 | 间接攻击潜艇(导弹) |
| ZCOUNIT_SIEGE_CANNON 攻城炮 | **不动超过 1 回合后火力大增** |
| ZCOUNIT_SMUGGLER 走私者 | 机动补给单位;**任意建筑上免费维修+补给** |
| ZCOUNIT_AUTO_TANK / COMMANDO / PARTISAN / ROYAL_GUARD / AT_CYCLE / RANGER / CRYSTAL_TANK / HOT_TANK(岩浆地形火力+)/ NEOSPIDER_TANK / TANK_HUNTER / REPAIR_TANK / LOGIC_TRUCK / AT_CYCLE | CoW 自创派系(有技能钩子 postBattleActions 等) |
| ZCOUNIT 系列 buildedUnit 钩子 | 出厂强化(stealthbomber/submarine 也有) |

## 四、直接适配现有模块的"零成本"清单(建议第一批搬运)

1. **地形换皮**:DESERT/SNOW/WASTE 三气候组全套 + RUIN 三变体 + WALL + WASTELAND + WEAK_WALL(套管道接缝逻辑)
2. **设施**:radar(视野光环)/tower(攻击防御 buff)/mine/oilrig/depot(半补给)/labor(Oozium 工厂)/pipestation(管道单位工厂)/field_base(类 HQ 胜负)/amphibiousfactory/powerplant
3. **单位**:sniper/transportplane/watermine/black_bomb 等标准但有专属小机制的单位

## 五、需要你规划新模块的机制

- **回合开始地形伤害**(creeper 1HP)
- **地形视野系统**(fire 照 5 格 / waste +1 / fog 隐藏地形)
- **范围 buff 单位**(隐身力场/减伤/无效化间接攻击)
- **踩踏触发**(水雷爆炸)
- **回合状态成长**(攻城炮蓄力)
- **CO 能量条**(powerplant 加速充能——项目还没有能量条系统的话整个跳过)
