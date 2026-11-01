# CO 三十模块体系设计（aw-1-3）

> 数据来源：[Commander_Wars](https://github.com/Advancedwarroom/Commander_Wars) 的 `resources/scripts/cos/co_*.js`（76 个原作 CO 完整定义：技能数值、COP/SCOP、CO Zone、AI 用大招逻辑、台词、音乐），以及 `mods/{aw,aw2,awds,awdc}_co/scripts/cos/`（同一 CO 在四代作品里的差异覆盖）。
> 目标：**所有 CO（原作搬运 + 玩家自定义）都由 30 大模块拼装而成**。每个模块有 常态(D2D) / 小COP / 大SCOP 三档效果；四代（DoR 系）CO 额外拥有第 30 号"区域型"模块。

## 一、三十大模块总表

| # | 模块 | 定义 | CoW 引擎已有挂钩（hook） | 现状评估 |
|---|---|---|---|---|
| 1 | 伤害加成 | 我方攻击力百分比/固定值加成、真伤、按敌方 CO 反制 | `getOffensiveBonus` `getEnemyPowersOffBonus` `getAttackHpBonus` `getTrueDamage` | ✅ 现成 |
| 2 | 防御加成 | 防御百分比、地形防御星、减伤 | `getDeffensiveBonus` `getTerrainDefenseModifier` `getEnemyTerrainDefenseModifier` `getDamageReduction` `getDeffensiveReduction` | ✅ 现成 |
| 3 | 攻击范围加成 | 射程/最小射程增减 | `getFirerangeModifier` `getMinFirerangeModifier` | ✅ 现成 |
| 4 | 视野加成 | 视野范围、完美视野（穿透地形/雾） | `getVisionrangeModifier` `getPerfectVision` | ✅ 现成 |
| 5 | 移动模块 | 移动力加成、移动消耗减少、改变某地形对某移动方式的可通行性 | `getMovementpointModifier` `getMovementcostModifier` `getMovementFuelCostModifier` `getFuelCostModifier` | ✅ 现成 |
| 6 | 多次移动加成 | 同一回合可再次移动/行动 | 无现成 hook（Eagle 系靠 power 动画重置行动标记） | ⚠️ 需自研 |
| 7 | 生成单位模块 | 直接生成单位/兵器到场上 | 无通用 hook（地图脚本 `spawnUnit` 系） | ⚠️ 需模块化 |
| 8 | 血量/弹药/燃料/消耗品变更 | 回血、扣血、补弹补油 | `postAnimationHeal`（powerHeal）、`getRepairBonus` | ✅ 现成 |
| 9 | 区域型导弹打击 | 陨石/导弹/EMP弹/反物质弹/天基导弹/原子弹等对区域内造成特殊影响；真人可选操作或 AI 自动索敌 | 无 CO hook（地图脚本武器：BlackCannon/DeathRay/Silo 等） | ⚠️ 需模块化（aw-1-3 已有黑炮/死光等可参考） |
| 10 | 逆转（反击）/联动模块 | 攻防逆转、先制反击、多功能互相联动 | `getFirstStrike` | ⚠️ 部分现成 |
| 11 | 经济模块 | 改变经济（收入增减），或经经济影响其他模块 | `getIncomeReduction` `getBonusIncome` | ✅ 现成 |
| 12 | 生产模块 | 生产价格折扣、可在更多建筑生产 | `getCostModifier`（价格）+ `getCOUnits`（可生产列表） | ✅ 现成 |
| 13 | 运气加成 | 攻防表固定值上下浮动（运气上限/下限） | `getBonusLuck` `getBonusMisfortune` `getAverageLuckDamage` | ✅ 现成 |
| 14 | 隐藏/反隐藏 | 隐藏/显露单位、血量、燃料弹药缺失标识 | `getHpHidden` `getPerfectHpView` | ✅ 现成 |
| 15 | 天气/战争迷雾模块 | 改变/适应天气与迷雾 | `getWeatherImmune` | ⚠️ 扩展（天气系统本体） |
| 16 | 升级模块 | 单位提升等级（老兵经验体系） | 无 hook | ⚠️ 需自研 |
| 17 | 升级系统 | CO 等级共四级，不同单位种类/CO 单位类型每级加成不同 | 无 hook | ⚠️ 需自研 |
| 18 | 效率/速率加减成 | 例如城市占领加速 | `getCaptureBonus` | ✅ 现成 |
| 19 | CO 专属单位模块 | 某 CO 能/不能生产某单位；某单位在该 CO 下机制/种类改变（数值改变归模块 1/2） | `getCOUnits` | ✅ 现成 |
| 20 | 时限模块 | 永久/临时、可叠加/不可叠加、最高叠加层数 | 无通用 hook（脚本 `startOfTurn` 计时） | ⚠️ 需自研 |
| 21 | 操作权改变 | 我方单位/建筑/兵器所属变敌方、敌单位归我方、临时操控 | 无 CO hook（脚本 capture/steal） | ⚠️ 需模块化 |
| 22 | 势力改变（全局模块） | 改变势力队伍、存活状态、操作者（CPU/AI/真人） | 无 CO hook（脚本改 owner） | ⚠️ 需模块化 |
| 23 | 外交模块 | 接受/赠予资金、建筑等 | 无 | ⚠️ 需自研 |
| 24 | 地形模块 | 临时改变地形（更换/新增/拆除建筑）、给建筑脚本加特殊状态（如工厂禁产） | `getAdditionalBuildingActions` | ⚠️ 需扩展 |
| 25 | 状态/脚本模块 | 给单位新增状态：燃烧、EMP 瘫痪、回合持续扣血、生病（减伤减防）等 | 无通用 hook（aw-1-3 已有 stun 瘫痪可复用） | ⚠️ 需自研 |
| 26 | AI 模块 | 按资金/血量/AI 自动判断/索敌 | `getAiUsePower` `getAiCoUnitBonus` `getAiCoBuildRatioModifier` | ✅ 现成 |
| 27 | CO 能量条模块 | 干扰/增减某 CO 能量条现有能量；增长/缩短 COP 长度、SCOP 长度或总长度 | `getPowerChargeBonus` | ✅ 现成 |
| 28 | 多屏/微空间分割合并模块 | 多屏战争专用（屏间传送、分屏合并、某屏失败残存 CO 死亡） | 无（aw-1-3 已有 TP 传送系统可复用） | ⚠️ 需自研 |
| 29 | 特殊限定加成 | 以上任意加成的限定版（只能在城市上/只能对空/只能白天……） | 所有 hook 的函数签名天然带 unit/pos/attacker/defender 参数，可按条件返回 | ✅ 机制天然支持 |
| 30 | 区域型（四代新增） | CO 单位/搭载其的单位区域范围内如何、范围外如何；范围受前 30 模块及 COP/SCOP 影响变大缩小 | `getCOUnitRange` `inCORange` `getCustomUnitZoneBoost` | ✅ 现成 |

### 模块拼装规则

- 一个 CO = 1..N 个模块的组合，每个模块各带 **常态(D2D) / 小COP / 大SCOP** 三档参数。
- 模块间可互相联动（模块 10）：例如"经济模块收入影响能量条积累速率"（模块 11 → 27）。
- CoW 的 `coGenerator`（自定义 CO 生成器 UI）已内置 20 种能力模板可参考：attackhpbonus / bonusluck / bonusmisfortune / capturebonus / damagereduction / deffensivebonus / firerangemodifier / firststrike / hphidden / minfirerangemodifier / movementpointmodifier / offensivebonus / perfecthpview / repairbonus / terraindefensemodifier / truedamage / visionrangemodifier（每种均有全局版和单位版），位置 `resources/ui/coGenerator/`。

## 二、能量条积累模块（COP/SCOP 专用）

能量来源（可多选拼接，各自配系数）：

1. 我方造成伤害
2. 我方被造成伤害
3. 我方损失单位/兵器/设施数
4. 我方生产单位数量
5. 敌方坚持回合数
6. 我方消耗资金数
7. 初始/现有资金数

统计口径两种（可同时挂不同系数）：

- **范围口径**：只统计 CO 单位或搭载其 CO 单位的区域范围内（范围大小可与加成范围不同，可更小或更大）；
- **全局口径**：全地图统计。

CoW 对应实现：`getAiUsePower(co, powerSurplus, ...)` 的 `powerSurplus` 即能量盈余，各 CO 自带 AI 放大时机判断（如 Andy：开局优先放 SCOP）。

## 三、配合模块（多 CO 联动）

- 多个 CO 可配合**同时发动 COP/SCOP**（CoW 的 Tag Power：`GameEnums.PowerMode_Tagpower`，两人合体大）；
- 一局内多次移动/CO 单位被摧毁 → 死亡失败，或永久不可使用该 COP/SCOP 与常态加减成；
- **多屏规则**：多屏战争下某一屏失败时，若该屏有残存 CO，则该 CO 死亡（全覆盖含玩家自设计脑洞）。

## 四、区域型模块（第 30 号，四代 DoR 系）

四代 CO 的核心差异：不靠全局 D2D，而是靠 **CO Zone**。

- 生效载体：CO 单位本体，或搭载该 CO 单位的载具（运输舰装载 CO 时区域跟随载具）；
- 区域内：我方单位获得 Zone 内效果（`getCustomUnitZoneBoost` / 各 hook 的 `inCORange` 分支）；
- 区域外：获得另一种效果（通常为负或为 0，如 Greyfield 区域外防御下降）；
- 范围：`getCOUnitRange`（CoW 中 DoR 系 CO 基本为 3，Javier 等可变）；
- 缩放：区域大小/强度受前 30 模块及 COP/SCOP 影响（COP 期间可全场化、SCOP 期间范围扩大——自研时做成模块参数）。

CoW 中 DoR 系 CO（Brenner/Lin/Will/Isabella/Gage/Tasha/Tabitha/Penny/Caulder 等）全部已实现此模式，数值可直接搬。

## 五、自定义 CO 体系（玩家脑洞全覆盖）

### 5.1 基础信息（三件套，零门槛）

| 项 | 说明 |
|---|---|
| 自定义头像 | 玩家上传图片（CoW 用 `resources/images/co/<army>/co_<id>+face.png` 系列四态：face/nrm/mini/info，可参考裁切规格） |
| 自定义名称 | 默认英文 ID，玩家可改 |
| 自定义介绍 | Bio/Hits/Miss/台词（CoW 每个原作 CO 都带全套：`getBio` `getHits` `getMiss` `getPowerSentences` `getVictorySentences` `getDefeatSentences`） |

### 5.2 技能槽位（四槽）

| 槽位 | 说明 | CoW 对应 |
|---|---|---|
| None | 常态被动（D2D） | `getOffensiveBonus` 等的 default 分支 |
| COP | 小技能（能量条满一档） | `activatePower` + `getPowerName`/`getPowerDescription` |
| SCOP | 大技能（能量条满二档） | `activateSuperpower` + `getSuperPowerName`/`getSuperPowerDescription` |
| CO Zone | 区域型（可选，四代模式） | `getCOUnitRange` + `inCORange` 分支 |

### 5.3 组装方式

- 每个 CO 从 30 大模块中勾选 1..N 个，每模块配三档数值（常态/COP/SCOP），CO Zone 模块配第四档（区域内/区域外两套）；
- 能量条模块（第 27 号）选择积累来源与口径（范围/全局）；
- 时限模块（第 20 号）标注每个效果的 永久/临时、可叠加/不可叠加、最高层数；
- 特殊限定（第 29 号）给任意模块加条件（地形/单位类型/天气/回合……）。

## 六、76 个原作 CO 全量盘点（CoW `resources/scripts/cos`）

> 模块号见第一节。星数=CoW 基础版 init 的 COP★/SCOP★（四代差异覆盖见第七节）。
> 全部 76 个 CO 均已实现：AI 放大时机、COP/SCOP 动画、专属音乐、台词全套——搬运时这些配置可直接翻译。

| CO | 阵营 | COP★ | SCOP★ | Zone | 模块组合 | 专属单位 |
|---|---|---|---|---|---|---|
| Adam | BD | Decimation (4) | Cartesian Cleanup (5) | 3 | 1,2,5,26 | - |
| Adder | BH | Sideslip (2) | Sidewinder (3) | 3 | 1,2,5,19,26,27 | Hot Tank |
| Aira | PF | Gust Storm (4) | Hurricannon (4) | 3 | 1,2,5,26 | - |
| Alexander | BM | Peace Riots (3) | Inevitable Revolution (4) | 3 | 1,2,11,18,19,26 | Partisan |
| Alexis | PF | Crystal Brace (4) | Crystal Edge (4) | 3 | 1,2,8,19,26 | Repair Tank |
| Amy | PF | Fluid Dynamics (3) | Photon Discharge (5) | 2 | 1,2,5,12,19,26 | - |
| Andy | OS | Hyper Repair (3) | Hyper Upgrade (3) | 3 | 1,2,5,19,26 | Repair Tank |
| Beast | DM | Steroids (3) | The Beast's Law (3) | 2 | 1,2,5,19,26 | At Cycle |
| Brenner | AC | Reinforce (4) | Lifeline (2) | 3 | 1,2,19,26 | Repair Tank |
| Cairn | BD | Call of the Wild (2) | Force of Nature (3) | 4 | 1,2,3,4,5,19,26,30 | Ranger |
| Cassidy | TI | Rampage (3) | Scorched Earth (4) | 2 | 1,2,26 | - |
| Caulder | DM | Mass Regeneration (7) | Perfect Healing (5) | 3 | 1,2,19,26 | Crystal Tank |
| Colin | BM | Gold Rush (2) | Power of Money (4) | 1 | 1,2,12,19,26 | Logic Truck |
| Conrad | TI | Calculated Strike (3) | Precision Assault (4) | 3 | 1,2,4,13,19,26 | Intel Truck |
| Davis | TI | Survival Plans (3) | The Stronger Side (3) | 3 | 1,2,13,19,26 | - |
| Drake | GE | Tsunami (4) | Typhoon (3) | 2 | 1,2,5,19,26 | Missile Sub |
| Eagle | GE | Lightning Drive (3) | Lightning Strike (6) | 3 | 1,2,5,19,26 | Kirov |
| Epoch | MA | Short Circuit (1) | Shocker (4) | 3 | 1,2,3,5,13,14,19,26 | Auto Tank |
| Flak | BH | Brute Force (3) | Barbaric Blow (3) | 3 | 1,2,13,19,26 | At Cycle |
| Forsythe | BD | Power of Honor (5) | Knights of the Desert (1) | 5 | 1,2,19,26 | Chaperon |
| Gage | BD | Longshot (3) | Sniper's Shot (3) | 2 | 1,2,3,19,26 | Siege Cannon |
| Graves | DM | Plague (4) | Perdition (3) | 3 | 1,2,26 | - |
| Greyfield | TI | Supply Chain (3) | High Command (3) | 3 | 1,2,5,19,26 | Missile Sub |
| Grimm | YC | Knuckleduster (3) | Haymaker (3) | 3 | 1,2,19,26 | At Cycle |
| Grit | BM | Snipe Attack (3) | Super Snipe (3) | 2 | 1,2,3,19,26 | Siege Cannon |
| Hachi | OS | Barter (3) | Merchant Union (2) | 1 | 1,2,12,19,24,26 | Smuggler |
| Hawke | BH | Black Wave (5) | Black Storm (4) | 3 | 1,2,26 | - |
| IK-486-B7 | MA | Wall of Defense (3) | Great Counterstrike (4) | 3 | 1,2,3,19,26 | Chaperon |
| Isabella | AC | Deep Strike (3) | Overlord (3) | 2 | 1,2,3,5,26 | - |
| Jake | OS | Beat Down (3) | Block Rock (3) | 3 | 1,2,3,5,19,26 | Tank Hunter |
| Javier | GE | Tower Shield (3) | Tower of Power (3) | 3 | 1,2,19,26 | Chaperon |
| Jess | GE | Turbo Charge (3) | Overdrive (3) | 3 | 1,2,5,19,26 | Tank Hunter |
| Joey | TI | Eccentricity (4) | Tempestuous Technique (3) | 3 | 1,2,10,12,26 | - |
| Jugger | BG | Overclock (3) | System Crash (4) | 3 | 1,2,13,19,26 | Auto Tank |
| Julia | DM | Rallying Cry (5) | Media Mayhem (5) | 2 | 1,2,19,26 | Partisan |
| Kanbei | YC | Morale Boost (4) | Samurai Spirit (3) | 2 | 1,2,12,19,26 | Royal Guard |
| Kindle | BG | Urban Blight (3) | High Society (3) | 3 | 1,2,19,26 | Partisan |
| Koal | BG | Forced March (3) | Trail of Woe (2) | 3 | 1,2,5,19,26 | Hot Tank |
| Lash | BH | Terrain Tactics (4) | Prime Tactics (3) | 3 | 1,2,5,19,26,30 | Neospider Tank |
| Lin | AC | Preliminary Scout (4) | Night Vision (2) | 1 | 1,2,4,19,26 | Intel Truck |
| Mary | AC | Highway of Pain (3) | Bloody Mary (3) | 2 | 1,2,18,19,24,26 | At Cycle |
| Max | OS | Max Force (3) | Max Blast (3) | 3 | 1,2,3,5,19,26 | Tank Hunter |
| Meiyo | GS | Training Camp (3) | War Training (4) | 3 | 1,2,26 | - |
| Melanthe | DM | Synthetic Seedlings (3) | Agent Dreadroot (4) | 3 | 1,2,8,19,26 | Neospider Tank |
| Mina | PF | Portent of Adversity (3) | Dark Lightning (5) | 3 | 1,2,26 | - |
| Minamoto | GS | Wind Blade (3) | Storm Slash (3) | 3 | 1,2,5,19,26 | Neospider Tank |
| Nana | PF | Chain Reaction (5) | Explosive Tantrum (4) | 2 | 1,2,19,26 | Siege Cannon |
| Napoleon | MA | Blast Shield (4) | Last Stand (4) | 3 | 1,2,19,26 | Iron Shield Generator |
| Nell | OS | Lucky Star (3) | Lady Luck (3) | 3 | 1,2,13,26 | - |
| Olaf | BM | Blizzard (3) | Winter Fury (4) | 3 | 1,2,5,15,26 | - |
| Ozzy | AC | Hammer On (3) | Grand Finale (3) | 2 | 1,2,19,26 | Iron Shield Generator |
| Penny | DM | Stormfront (3) | Enigma (3) | 3 | 1,2,3,4,5,15,26 | - |
| Peter | GE | General Offensive (2) | Bombing Run (3) | 3 | 1,2,19,26 | Royal Guard |
| Rachel | OS | Lucky Lass (3) | Covering Fire (3) | 2 | 1,2,8,13,26 | - |
| Rattigan | YC | Raid (3) | The Sweep (4) | 3 | 1,2,5,19,26 | Auto Tank |
| Robo-Andy | MA | Cooldown (3) | Critical Mass (3) | 3 | 1,2,13,19,26 | At Cycle |
| Robo-Sturm | MA | Mechanized Storm (4) | Mechanized Destruction (5) | 2 | 1,2,5,19,26 | Tank Hunter |
| Sabaki | AC | Speed Salvage (3) | Rejuvenation Raid (3) | 3 | 1,2,19,26 | Crystal Tank |
| Sami | OS | Double Time (3) | Victory March (6) | 3 | 1,2,5,18,19,26 | Commando |
| Sanjuro | GS | Heart of Gold (2) | Buying Power (4) | 2 | 1,2,5,12,19,26 | Smuggler |
| Sasha | BM | Market Crash (2) | War Bonds (4) | 2 | 1,2,11,19,26 | Logic Truck |
| Sensei | YC | Copter Command (2) | Airborne Assault (4) | 1 | 1,2,5,19,26 | Commando |
| Smitan | GE | Scramble Tactics (3) | Flare Drive (5) | 3 | 1,2,3,19,26 | Siege Cannon |
| Sonja | YC | Enhanced Vision (3) | Counter Break (2) | 2 | 1,2,4,10,13,14,19,26 | Intel Truck |
| Sophie | AC | Sudden Strike (2) | Total War (4) | 2 | 1,2,19,26 | Commando |
| Sturm | BH | Meteor Slam (5) | Meteor Strike (5) | 3 | 1,2,5,26 | - |
| Tabitha | DM | Firestorm (4) | Apocalypse (4) | - | 1,2,26 | - |
| Tasha | BD | Sonic Boom (3) | Fox One (3) | 1 | 1,2,5,19,26 | Kirov |
| Varlot | DM | Acquisition (4) | Hostile Takeover (4) | 3 | 1,2,11,18,19,26 | Smuggler |
| Von Bolt | BG | Shockwave (6) | Deus Ex Machina (4) | 3 | 1,2,19,26 | Crystal Tank |
| Walter | TI | Memento (5) | Magnum Opus (4) | 2 | 1,2,14,19,26 | Intel Truck |
| Waylon | TI | Bad Company (3) | Wingman (3) | 2 | 1,2,19,26 | Kirov |
| Will | AC | Rally Cry (3) | New Era (3) | 2 | 1,2,5,19,26 | Hot Tank |
| Xavier | GS | Phasing Charge (3) | Reality Minus (4) | 3 | 1,2,26 | - |
| Yukio | DM | Dark Assault (8) | Total Invasion (7) | 4 | 1,2,19,26 | Logic Truck |
| Zandra | BD | Sand Shift (4) | Desert Gale (3) | 3 | 1,2,3,15,26 | - |

## 七、四代作品版本差异（mods/{aw,aw2,awds,awdc}_co 覆盖）

> CoW 把同一 CO 在 AW1/AW2/AWDS/AWDC 四个版本的数值差异做成覆盖文件（星数、Zone、hook 集合）；下表仅列与基础版有实际差异的 CO。搬运时按所选规则集加载对应覆盖即可。

| CO | 差异 |
|---|---|
| Adam | **aw**: 星数4/5→0/4；**awdc**: 星数4/5→3/3 |
| Adder | **aw**: 星数2/3→0/2；**awdc**: 星数2/3→3/3 |
| Aira | **awdc**: 星数4/4→3/3 |
| Alexander | **aw**: 星数3/4→0/3；**awdc**: 星数3/4→3/3 |
| Alexis | **aw**: 星数4/4→0/3；**awdc**: 星数4/4→3/3 |
| Amy | **aw**: 星数3/5→0/3；**awdc**: 星数3/5→3/3 |
| Brenner | **aw**: 星数4/2→0/4；**awdc**: 星数4/2→3/3 |
| Cairn | **aw**: 星数2/3→0/4；**awdc**: 星数2/3→3/3 |
| Cassidy | **aw**: 星数3/4→0/3；**awdc**: 星数3/4→3/3 |
| Caulder | **aw**: 星数7/5→0/7 |
| Colin | **aw**: 星数2/4→0/2；**awdc**: 星数2/4→3/3 |
| Conrad | **aw**: 星数3/4→0/3；**awdc**: 星数3/4→3/3 |
| Drake | **aw**: 星数4/3→0/4；**awdc**: 星数4/3→3/3 |
| Eagle | **aw**: 星数3/6→0/3；**awdc**: 星数3/6→3/3 |
| Epoch | **aw**: 星数1/4→0/1；**awdc**: 星数1/4→3/3 |
| Forsythe | **aw**: 星数5/1→0/4 |
| Gage | **aw**: 星数3/3→0/4 |
| Graves | **aw**: 星数4/3→0/4；**awdc**: 星数4/3→3/3 |
| Greyfield | **aw**: 星数3/3→0/4 |
| Grit | **aw**: 星数3/3→0/5 |
| Hachi | **aw**: 星数3/2→0/3；**awdc**: 星数3/2→3/3 |
| Hawke | **aw**: 星数5/4→0/5；**awdc**: 星数5/4→3/3 |
| IK-486-B7 | **aw**: 星数3/4→0/3；**awdc**: 星数3/4→3/3 |
| Isabella | **aw**: 星数3/3→0/4 |
| Joey | **aw**: 星数4/3→0/4；**awdc**: 星数4/3→3/3 |
| Jugger | **aw**: 星数3/4→0/3；**awdc**: 星数3/4→3/3 |
| Julia | **awdc**: 星数5/5→3/3 |
| Kanbei | **aw**: 星数4/3→0/4；**awdc**: 星数4/3→3/3 |
| Koal | **aw**: 星数3/2→0/3；**awdc**: 星数3/2→3/3 |
| Lash | **aw**: 星数4/3→0/4；**awdc**: 星数4/3→3/3 |
| Lin | **aw**: 星数4/2→0/4；**awdc**: 星数4/2→3/3 |
| Meiyo | **aw**: 星数3/4→0/3；**awdc**: 星数3/4→3/3 |
| Melanthe | **aw**: 星数3/4→0/3；**awdc**: 星数3/4→3/3 |
| Mina | **aw**: 星数3/5→0/3；**awdc**: 星数3/5→3/3 |
| Nana | **aw**: 星数5/4→0/5；**awdc**: 星数5/4→3/3 |
| Napoleon | **awdc**: 星数4/4→3/3 |
| Olaf | **aw**: 星数3/4→0/3；**awdc**: 星数3/4→3/3 |
| Peter | **awdc**: 星数2/3→3/3 |
| Rachel | **aw**: 星数3/3→0/6 |
| Rattigan | **aw**: 星数3/4→0/3；**awdc**: 星数3/4→3/3 |
| Robo-Sturm | **aw**: 星数4/5→0/4；**awdc**: 星数4/5→3/3 |
| Sami | **aw**: 星数3/6→0/2；**awdc**: 星数3/6→3/3 |
| Sanjuro | **aw**: 星数2/4→0/3；**awdc**: 星数2/4→3/3 |
| Sasha | **aw**: 星数2/4→0/2；**awdc**: 星数2/4→3/3 |
| Sensei | **aw**: 星数2/4→0/2；**awdc**: 星数2/4→3/3 |
| Smitan | **aw**: 星数3/5→0/3；**awdc**: 星数3/5→3/3 |
| Sonja | **aw**: 星数3/2→0/3；**awdc**: 星数3/2→3/3 |
| Sophie | **aw**: 星数2/4→0/2；**awdc**: 星数2/4→3/3 |
| Sturm | **aw2**: 星数5/5→0/10；**awdc**: 星数5/5→3/3 |
| Tabitha | **aw**: 星数4/4→0/5；**awdc**: 星数4/4→3/3 |
| Tasha | **aw**: 星数3/3→0/4 |
| Varlot | **awdc**: 星数4/4→3/3 |
| Von Bolt | **aw**: 星数6/4→0/5；**awdc**: 星数6/4→3/3；**awds**: 星数6/4→0/10 |
| Walter | **aw**: 星数5/4→0/5；**awdc**: 星数5/4→3/3 |
| Waylon | **aw**: 星数3/3→0/4 |
| Will | **aw**: 星数3/3→0/4 |
| Xavier | **awdc**: 星数3/4→3/3 |
| Yukio | **aw**: 星数8/7→0/8；**awdc**: 星数8/7→3/3 |
| Zandra | **aw**: 星数4/3→0/4；**awdc**: 星数4/3→3/3 |

## 八、落地实现路线（aw-1-3 / Godot C#）

1. **数据层**：每个 CO 一份 JSON（`co/<id>.json`）——基础信息三件套（头像路径/名称/介绍）+ 四槽位（None/COP/SCOP/Zone）+ 模块组合数组（每项含 模块号/三档或四档数值/时限参数/限定条件）+ 能量条来源与口径。76 个原作 CO 按第六节盘点表批量生成模板，再从 CoW 的 JS 逐个翻译数值。
2. **运行层**：`CoModuleSystem`（新 partial）——30 个模块各一个类，统一接口 `GetOffBonus(unit, context)`、`ApplyOnPower(co)` 等；战斗结算点（现有伤害计算处）改为遍历双方 CO 的模块链。
3. **能量条**：`PowerChargeSystem`——7 种来源事件打点（伤害/损失/生产/回合/资金），范围口径复用 GridManager 的格子查询（传送系统同款）。
4. **CO Zone**：复用 N 屏传送的格子高亮/范围查询基础设施；CO 单位（专属单位，19 号模块）携带 Zone。
5. **版本差异**：规则集选择 aw/aw2/awds/awdc 时按第七节覆盖表加载差异（星数/Zone/模块开关）。
6. **自定义 CO**：UI 面板 → 生成 JSON；头像上传存 `user_co/` 目录；脑洞通过 29 号限定模块 + 25 号状态模块组合覆盖。

> **✅ 落地进度（已完成的部分见下）：**
> - **数据层**：`spricts/COSystem/CoProfile.cs`（JSON 档案模型，`co/<id>.json` × 76 个已从 CoW 数值批量生成：经典系取 awds 规则集覆盖，DoR 系取 awdc 规则集以保留 Zone 值）
> - **运行层**：`spricts/COSystem/CoModuleSystem.cs`（势力→CO 状态、30 模块查询、能量条、大招发动/过期、Zone 判定）；势力绑定走 `co/bindings.json`
> - **已接线模块**：1 伤害（`CalculateFinalDamage`）、2 防御（同左+地形星）、3 射程（`GetFireRangeBonus` 待接 UI）、4 视野（`ActualVisionRange`）、5 移动（`ShowMoveRange`/移动判定/移动消耗）、8 回血（大招按钮内全军回复）、11 经济（`AddFundsForTeam`）、13 运气（`CalculateFinalDamage` 末段）、18 占领（`GetCapturePower`）、27 能量条（攻击/反击/损失/生产打点）
> - **UI**：`ActionMenu.CoPower.cs`（技能/大招按钮，能量攒满显示）
> - **待做**：7 生成单位、9 区域导弹、10 逆转联动（FirstStrike hook 查询已有）、14-17、20-26、28-29 的接线与 UI；CO 头像上传界面；CoW 专属兵种（ZCOUNIT_*）实装后替换近似映射

**第二轮落地（射程/专属单位/Tag Power）：**
- **模块3 攻击范围**：`IsTargetInAttackRange`（攻击判定）+ `ShowMinMaxAttackRange`（范围显示）均加射程/最小射程增减
- **模块12 生产价格**：生产菜单显示、实际扣费、AI 生产三处同步价格百分比增减
- **模块19 专属单位**：生产菜单并入 CO 专属单位（★标记、去重），出厂即 `isCoUnit=true`（Zone 载体+CO 死亡规则挂钩）；57 个 CO 的 CoW 专属兵种已映射到 aw-1-3 最接近单位（原始 ID 存 `coUnitsRaw`，后续实装自定义兵种时替换）
- **配合模块 Tag Power**：`bindings.json` 支持数组 `["主CO","搭档CO",...]`；合体星数=主 SCOP★+搭档 COP★；发动后主 CO+全部搭档的 SCOP 模块合并生效，且**全军再行动一次**（模块6 多次移动，`GrantTeamExtraTurn`，传送次数同步重置）；能量条上限自动抬到合体所需星数；UI 为"合体技"按钮
- **模块27 补充**：消耗资金打点（`OnFundsSpent`）接入生产扣费处

搬运优先级建议：先做 1/2/5/13/18/19 号模块（原作 CO 90% 的机制都靠它们），再做能量条与 COP/SCOP，最后做 Zone 与配合/多屏规则。

**第三轮落地（天气系统 + Tag 搭配加成 + 无上限合体）：**
- **模块15 天气/战争迷雾**：新建 `spricts/COSystem/WeatherSystem.cs`——天气类型（晴/雨/雪/沙暴/雾）、出现率权重（晴70%）、随机天数轮换（`AdvanceDay`）、雨/雾强制开雾+到期恢复、视野/移动/攻击/射程修正查询、雪免地形（道路/桥/轨道/建筑/空军）
  - 数值照搬 CoW `weather.js`：雨视野-1+强制雾、雪移动+1（免地形除外）、沙暴攻-15%+射程-1、雾视野-2+强制雾
  - 接线点：`ActualVisionRange`（视野）、`GetActualMoveCost`（移动消耗）、`CalculateFinalDamage`（沙暴攻减）、`IsTargetInAttackRange`+`ShowMinMaxAttackRange`（射程）
  - UI：`GameManager.WeatherUI.cs`（左上角天气标签+剩余天数+全屏色调，`WeatherSystem.OnWeatherChanged` 订阅）；新一天 `NextPhase` 调 `AdvanceDay`
- **发动时效果统一入口**：`ExecuteActivationEffects`——模块8 全军回血 + 模块15 改变天气，COP/SCOP/Tag 三条发动路径统一调用（替代原按钮内联回血）
- **CO 数据**：Olaf（COP/SCOP 降雪+雪免疫）、Drake（降雨+雨免疫）、Zandra（沙暴）已写入模块15
- **Tag Power 无上限**：`List<CoProfile> partners` 结构本身不限数量；合体星数=主 SCOP★+全部搭档 COP★；Tag 模式合并所有 CO 的 SCOP 模块
- **模块15 天气技能**：`setWeather`（0晴/1雨/2雪/3沙暴/4雾）+ `weatherTurns`（持续天数）+ `weatherImmune`（常态免疫）
- **Tag 搭配加成（CoW Tagpower.js 全量搬运）**：`co/tag_synergy.json` 收录 CoW 的 180 组 CO 搭配；`GetTagSynergyBonus` 对主+全部搭档**两两组合求和**（3 个 CO 吃 3 对、4 个吃 6 对，无上限）；攻击% 加进 `GetOffenseBonusPercent`、运气加成抬 `GetLuckRoll` 上限——**对比 CoW：原作每玩家固定 2 个 CO 槽位（`getCO(0)/getCO(1)`），aw-1-3 无上限**

**第四轮落地（剩余模块全清 + 编辑模式天气面板）：**
- **模块10（逆转/先制反击）**：`firstStrike`——防守方先手，反击先结算、攻击方按掉血后战力出手；攻击方被先手打死则本次攻击取消（能量打点同步）
- **模块16（升级）**：单位 `xp` 字段，击杀得 XP（默认100/`xpPerKill` 可配），Level 1-3（120/300 阈值），攻防每级+4%
- **模块17（CO四级）**：`CoTeamState.coLevel` 0-3，SCOP/Tag 发动升 1 级；模块条目 `minCoLevel/maxCoLevel` 区间门控
- **模块20（时限）**：`duration` 支持 `permanent`（999回合）/`instant`（瞬时，发动时效果已结算）
- **模块29（特殊限定）**：等级区间并入限定过滤（单位类型/地形/直间接/CO等级四维）
- **模块7（生成单位）**：`spawnUnitId`（1步兵/2机甲/3摩托/4侦察/5轻坦）+`spawnHp`+`spawnCount`，自己的城市刷兵，复用生产管线（Sensei 已写入示例）
- **模块9（区域导弹）**：`missileCount`/`missileDamage`/`blastRadius`，自动瞄准敌军最密集处、避开已打击中心
- **模块24（建筑状态）**：`facStatus`（1不可生产/2无收入/3无掩体/4无物资）+`facStatusTurns`，施加给全部敌方设施
- **模块25（状态系统）**：`burnHp`（燃烧每回合掉血，最低留1HP）/`stunTurns`（麻痹复用 ApplyStun）/`weakPercent`（虚弱降攻），回合开始结算（当前行动势力）
- **模块14（隐藏/反隐藏）**：`antiStealth` 反隐（看破+可直接攻击隐身，接入 IsVisibleTo/CanTargetStealth，Sonja 示例）；`stealthAll` 发动时全军隐身、大招到期自动解除
- **模块26（AI模块）**：`AIAutoPower`——AI 势力回合开始自动放大（优先 Tag/SCOP/COP）
- **模块21/22/23（脚本API）**：`TransferUnit`/`TransferCity`/`TransferFaction`（操作权/势力改变，移交后当场待机）/`GiftFunds`/`SetAlliance`
- **模块28（多屏 CO 死亡）**：`OnCoUnitDestroyed`——CO 单位阵亡时全图（含多屏）无残存 `isCoUnit` 则 CO 死亡（Unregister，模块效果全部失效）；任一屏还有载体则 CO 不死
- **编辑模式天气面板**：全局配置新增「天气系统」区——当前天气下拉立即生效、每日随机轮换开关、五种天气出现权重 0-100 可调（默认 70/10/10/10/0）、随机持续天数 min~max；地图导出/导入 txt 携带 `Weather/WeatherChances/WeatherRandom/WeatherDuration` 四行随图存取

至此 30 大模块全部落地（1-30），配合模块（多CO合体/一局多次移动/CO死亡）与能量条七来源齐备。
