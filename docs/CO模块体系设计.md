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
