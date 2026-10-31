# 三代（AW1–3/DS）与四代（Days of Ruin）兵种对照

> 原则：哪作的单位数值用哪作；跨作组合（例如 DS 单位打 DoR 单位）按同定位兵种类比得出新的原创数值。

## 航母：三代 Carrier 与四代生产舰 ProducibleBoat

| 项目 | AWDS 航母 Carrier | DoR 航母 → 本作「生产舰 ProducibleBoat」 |
|---|---|---|
| 造价 | 30000 | 28000 |
| 武器 | 导弹 射程 3–8，弹 9，只能对空 | 防空炮 射程 1（直接攻击），只能对空 |
| 搭载 | 2 架空军 | 2 架空军，可补给维修舱内单位 |
| 特殊 | — | 可生产水上飞机（最多 4 架，生产当天不能移动）；舱内飞机可直接起飞并当回合行动 |

来源：[Wars Wiki: Carrier](https://warswiki.org/wiki/Carrier)

## 战列舰：三代 Battleship 与四代突击舰 KamikazeBoat

能移动后攻击的是**四代（DoR）**战列舰，不是三代。

| 项目 | AW1–3/DS 战列舰 Battleship | DoR 战列舰 → 本作「突击舰 KamikazeBoat」 |
|---|---|---|
| 造价 | 28000 | 25000 |
| 视野 | 2 | 3 |
| 弹药 | 9 | 6 |
| 射程 | 2–6 | 3–5 |
| 移动后攻击 | 不能 | **能** |
| 其他 | — | 可穿过桥下和浅滩（已实现：`terrainMoveCostOverrides`） |

来源：[Wars Wiki: Battleship (Advance Wars)](https://warswiki.org/wiki/Battleship_(Advance_Wars))

## 新增兵种原作数据

| 兵种 | 出处 | 造价 | 移动 | 视野 | 燃料 | 武器 | 特殊 | 来源 |
|---|---|---|---|---|---|---|---|---|
| 新型坦克 Neotank | AW2/DS | 22000 | 6 | 1 | 99 | 主炮 9 + 机枪 | — | [AWBW](https://awbw.amarriner.com/damage.php) |
| 巨型坦克 Megatank | DS | 28000 | 4 | 1 | 50 | 主炮 3 + 机枪 | — | [AWBW](https://awbw.amarriner.com/damage.php) |
| 弩级坦克 WarTank | DoR | 原作 16000 → 本作 22000 | 4 | 2 | 50 | 主炮 5 + 机枪 | 伤害按重型坦克与巨型坦克的均值，排在两者之间 | [Wars Wiki: War Tank](https://warswiki.org/wiki/War_Tank) |
| 工程车 Rig | DoR | 5000 | 6 | 1 | 99 | 无 | 载 1 步兵/机械兵；补给；搭建临时机场/港口 | [Wars Wiki: Rig](https://warswiki.org/wiki/Rig) |
| 螺旋桨机 Duster | DoR | 13000 | 8 | 4 | 99 | 机枪 9 | 对空对地 | [Wars Wiki: Duster](https://warswiki.org/wiki/Duster) |
| 水上飞机 Seaplane | DoR | 15000 | 7 | 4 | 40 | 导弹 3 | 只能由生产舰生产 | [Wars Wiki: Seaplane](https://warswiki.org/wiki/Seaplane) |

伤害表：DoR 单位之间用 [GameFAQs DoR 伤害表（DTaeKim）](https://gamefaqs.gamespot.com/ds/943675-advance-wars-days-of-ruin/faqs/51639)；Neotank/Megatank 用 [AWBW 伤害表](https://awbw.amarriner.com/damage.php)，再按本作重型坦克与 AWBW 重型坦克的差值整体平移。

## 弩级坦克造价为何是 22000

DoR 里 WarTank 是最强坦克，造价 16000；但和 DS 的巨型坦克（28000）同局时，若伤害和造价都照搬原作，巨型坦克会显得太亏。所以伤害取重型坦克与巨型坦克的均值，造价也落在两者之间（22000，与新型坦克同价）。

## 临时设施

| 设施 | 原作规则 | 本作 | 来源 |
|---|---|---|---|
| 临时机场 | 工程车搭建，只能建在平原；无收入、不能生产；只能维修补给停在里面的单位；防御 1 星 | 平原限定、收入 0、不生产；贴图为 DoR 原作临时机场（从 `docs/DS _ DSi - Advance Wars_ Days of Ruin - Map - Tileset (Normal).png` 截取，灯亮/灯灭两帧） | [Wars Wiki: Temporary Airport](https://warswiki.org/wiki/Temporary_Airport) |
| 临时港口 | 工程车搭建；无收入、不能生产；防御 1 星 | 海滩限定、收入 0、不生产；贴图为 DoR 原作临时港口（红/蓝用 `docs/TemporaryPort*.png`，中立从同一张 Tileset 截取） | [Wars Wiki: Temporary Port](https://warswiki.org/wiki/Temporary_Port) |


---

### 补遗：飞弹来援登记（2026-11-01）

| 游戏内名称 | 原作对应 | 出处 | 登记说明 |
|---|---|---|---|
| 飞弹 FlyBomb | Black Bomb 黑炸弹/黑弹 | AWDS | 开创人亲口登记：空军自爆系与黑炸弹机制同位（25000G 区域自爆），此前因中性化改名未登记亲缘，现正式入册 |
