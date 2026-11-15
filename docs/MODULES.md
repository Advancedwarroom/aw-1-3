# 格战原创模块图鉴（AI-KIMI-ENG 作品名录）


> 代码已按模块拆分为 partial class，`Infantry.cs` 的各模块现位于 `spricts/Infantry/Infantry.<模块>.cs`，见 [CODE_LAYOUT.md](CODE_LAYOUT.md)。

> 本文档供所有协作者（人类指挥官 + AI-2 参谋）阅读。记录 2026-10-28 至 10-30 由主程 AI 设计并落地的**原创机制**（非 AW 原作内容）。每个模块都可经检查器按单位配置，默认全关 = 零行为变化。

## 模块一：合流模块（Join）

**文件**：`Infantry.cs` / `GameManager.MergeUnits`
**字段**：
- `canMerge`（bool，默认 true）：是否允许参与合流
- `canActAfterMerge`（bool，默认 false）：合流后再行动一次
**规则**：开关看"被合流方"（目标单位）；默认关=AW 基准（合流即报废，else 分支强制待机）；开启则 OnTurnEnd() 全重置补一次行动。
**测试点**：两残血坦克合流，关=灰，开=可再动。

## 模块二：兵器攻击准入（双向白名单）

**文件**：`Infantry.cs`（CanTargetWeapon + excludedWeaponTargets）/ `Weapon.cs`（excludedAttackerTypes）
**字段**：
- `excludedWeaponTargets`（string[]，攻击方）：不能攻击哪些兵器，"Weapon"=全部禁
- `excludedAttackerTypes`（string[]，兵器方）：不接受哪些单位攻击
**三道闸门**：伤害函数归零 + 玩家点击拒绝 + 双向名单判定（支持基类名逐级匹配）
**起源**：战斗机 550 兜底值秒杀兵器事件 → 用户要求"成对准入，为未来能被打的新兵器留门"

## 模块三：隐身系统（76 字段机制层，核心作品）

**文件**：`Infantry.cs`（主体）/ `FogOfWarManager.cs` / `ActionMenu.cs` / `UnitManager.cs` / `GridManager.cs` / `Facility.cs`

### 字段分组
1. **基础**：canStealth / isStealthed / stealthToggleCostsAction（切换是否耗行动）
2. **消耗·八货币**（0=不扣）：燃油/血量/普通弹/自爆弹/照明弹/隐身弹/显形弹/迷彩弹，每日结算
3. **感知**：stealthVisibleWhenAdjacent+Min/MaxRange（X-Y 贴身）/ stealthVisibleOnTile（同格单位+脚下建筑）
4. **现形四触发**：攻击后 / 受伤（可设血量阈值对）/ 照明弹（永久 or 仅生效期间）/ 阈值矩阵
5. **阈值矩阵（32字段）**：八资源 × 双方向（ForceHide/ForceReveal）× 双阈值（High 用≥触发 / Low 用≤触发，中间不触发非区间带）+ stealthForceOneShot（一次性锁存/持续）+ stealthFormLocked（形态锁定：玩家不可切，阈值强制不受限）
6. **行为锁**：攻击/移动/搭载卸载/自爆等自定义/占点
7. **改写层**：stealthMoveTypeOverride（移动方式）/ stealthOverlapMode（0不改/1可重叠跳过合流/2不可重叠拒进格）/ stealthMatrixOverride（隐身攻防表）
8. **弹系统**：stealthAmmoMode 三选一（0免费/1双弹种/2迷彩弹，互斥不能通吃）+ 三弹仓 max/current + Facility 补给补满

### 判定函数（全部 public static，可被 AI/新模块复用）
- `IsVisibleTo(unit, viewerTeam, grids)`：视觉层
- `CanTargetStealth(attacker, target)`：攻击层（含照明弹分支）
- `CanTargetWeapon`：兵器版

### 关键设计
- 视觉闸门在 `ShouldShowUnit`（与迷雾开关解耦：雾关也生效）
- 攻击门在 `Attack()` 唯一咽喉；反击门=防守方打不了隐身攻击者则反击归零；攻击现形先于反击结算
- 照明弹联动：`IsFlareLit()` 实时查询亮格（`IsGridIlluminated`），离开亮格自动恢复——零标记位

## 模块四：单位生产流水线（七件套规范）

新单位 = 场景+脚本+动画资源（3件） + 注册表/生产数据库/伤害表/统计面板/编辑器模板+名单/视野表/翻译/AI优先级（9处集成）。**验收口诀：凡是引用 Fighter 的地方必有新单位，grep 即任务清单。**
素材换肤流水线：指挥官画 zip → 小写命名入库 → .tres 引用零改动。

## 模块五：云打包产线

GitHub Actions 手动触发（workflow_dispatch），Mono+android-arm64 正路（bionic 是死胡同——Mono.linux-arm64 包不存在），Gradle 模板需装到 `android/build/` 子目录 + `.build_version` + `.gdignore`。详见 BUILD.md 事故档案。

## 协作协议

见 BUILD.md《双 AI 协作协议》。本图鉴由 AI-KIMI-ENG 维护，新模块落地后须补录此文。

## 模块六：雾战系统（10字段+攻防表覆盖）

**文件**：`Infantry.cs`（ExportGroup"雾战模块"）
**字段**：fogMaxHpBonus / fogMaxFuelBonus / fogFlatDefenseBonus / fogPrimaryAmmoMaxBonus / fogExplodeAmmoMaxBonus / fogFlareAmmoMaxBonus / fogStealthBulletMaxBonus / fogRevealBulletMaxBonus / fogCamoBulletMaxBonus / fogMatrixOverride（Dictionary 攻防表覆盖）
**规则**：全部可为负=减成；**不改实际数值，只改生效上限/判定**——雾里 HP/油料/弹药上限浮动 + 雾中攻防表独立一套
**配套**：迷雾三工单（探索记忆层/多阵营各自视角/森林礁岩隐蔽）+ 林地礁岩隐蔽（AW规则：无贴身敌人时藏于森林/礁岩不可见）

## 模块七：截断遭遇系统（11字段裁决层）

**文件**：`Infantry.cs`（ExportGroup"截断遭遇"）/ `GameManager.cs`
**字段**：开关 / 截断反击参数 / 反射弧过长阈值 / 双角色三模式（1主动出击/2两败俱伤/3反将被动）/ 伏击手权重（平权=被截断方先执行）/ 允许多次截断出击（开=双方各执行，关=只执行权重高者）等
**配套**：攻击单向反击参数独立调节 + 人间截断者遭遇确认菜单（回合中回合，全局冻结）+ 军团P0参与撮合
**测试点**：反将被动模式="对方打我我只能反击"；两败俱伤=双方同发互兑

## 模块八：手画路径移动

**文件**：`GridManager.cs` / `GameManager.cs`
**规则**：手指画航线 → 航点序列 + 路程计费（真实耗油）+ 绕圈合法 + 无效格截断红标 + 相机拖图修复（手势判定/防闪光升级到0帧窗口）
**定位**：电子棋自由度的实体棋回归——官方给格子，玩家画路线

## 模块九：群体狂热模块（16字段光环引擎）

**文件**：`Infantry.cs`（ExportGroup"群体狂热模块"）
**字段**：开关 / x-y检查范围 / 白名单（仅注册表单位种类）/ 特殊态仅同种类 / 敌我友三态（0全部1仅友2仅敌）/ 最大层数（看到7只也只享3层）/ 加减成百分比（负=减成）/ 破上限开关+新上限 / **五路效果对象**（弹药上限/燃油上限/血量上限/真实攻击力/固定防御力）/ 持续N回合（再符合=覆盖不叠加）
**原型**：Wargroove 狗群侧翼加成 → 泛化为任意单位可用的通用增益光环
**注**：机械狗非生物，默认不开启

## 模块十：逃跑模块（野化）

**文件**：`Infantry/Infantry.Flee.cs`（TryFlee）/ `GameManager.cs`（死亡结算5处挂钩）/ `Laser.cs`（三处击杀结算挂钩）
**字段**：canFleeOnDeath（致命伤触发）/ 逃跑目标格 x-y 环
**规则**：致命伤 → x-y环内随机空格逃跑并野化（**落地满血中立、永久存续、原主人可攻击**）；无处可逃才照常死亡。**致命伤被逃跑抵消，代价是脱离编制而非血量**——Wargroove"狗被打败会逃跑"的原教旨继承
**一次性规则（2026-11-01 人类指挥官裁决）**：野化为**终身一次性**——已野化的单位不再二次野化（"他都跑过一次了"），致命伤再临 = 正常死亡。
**实现**：`0c03b98` 已入库——TryFlee 成功后自动置 `canFleeOnDeath = false`；该字段是 Export 字段，随存档保存，读档后同样不会二次野化。

**联动**：死亡截断铁律（模块十三）为野化让路

## 模块十一：拆旗模块

**文件**：`Infantry.cs` / `Building.cs`（双进度）
**字段**：canTearFlag / tearFlagPower（拆旗力；撕旗值=占领值20 → 5力四回合、步兵10两回合）
**规则**：与占领**并行的第二条进度**；占领按钮可见性扩展；两条进度各自独立（进度/冻结/遭遇清零）/ 满20转中立；程序化撕旗动画（帧序列非贴图）
**定位**：AW 只有"占"，格战有"占+拆"两手——攻守双方围绕同一块地打进度战

## 模块十二：设施压制（经济战光环）

**文件**：`Infantry.cs`（ExportGroup"设施压制"）/ `Facility.cs`
**字段**：开关 / x-y环（含自身格）/ **四路干扰**：资金（制止|百分比±）、掩体（制止|百分比）、生产（制止|百分比，作用于生产资金消耗）、补给（只制止）
**定位**：站在敌楼里压制周边经济——不消灭单位，消耗国力

## 模块十三：战斗法则补丁包

- **死亡截断铁律**：同发互兑原子化结算 + 跨发死亡取消后续拳（多段连击首杀=后段作废）；野化为该铁律的第一个例外
- **反击按挨打后血量**：原按挨打前，现修正
- **Oozium 覆盖去硬编码**：吞并判定导出化
- **修理通用化**：修理船行为泛化
- **不写代码造新兵种**（#14~#17）：注册表+模板流水线实战验收

---

## 原型归属勘误（2026-11-01）

- **机械狗**：原型 = Wargroove 的狗（快速廉价骚扰位）；嗅探模块 = 格战原创生态位（Wargroove 无战争迷雾，原作无可嗅之隐）
- **群体狂热模块**：原型 = Wargroove 狗群侧翼加成 → 泛化成模块九
- **逃跑/野化**：原型 = Wargroove"狗败逃不死亡" → 泛化成模块十
- **Wargroove 贡献三基因**：狗本体 / 侧翼加成 / 败逃机制；格战将其全部模块化为可插拔系统

*模块六~十三由 AI-KIMI-ENG 根据 10-31 提交记录补录；机制本体由 AI-2（主程）实现，人类指挥官裁决。*
---

## 模块十四：港口与海军线（2026-11-01）

**文件**：`Port.cs`（建筑，继承 City）/ `Battleship.cs` `Cruiser.cs` `Submarine.cs` `Lander.cs` `Carrier.cs` `Gunboat.cs` / `Infantry.Movement.cs`（GetMoveCostOnGrid）
**数值**：AW3/AWDS 原作值（战列舰 28000/2-6 间接、巡洋 18000/对空105反潜90、潜艇 20000/下潜油5、登陆艇 12000/载2、航母 30000/3-8 对空）；炮艇为 DoR 跨作智能适配原创值（5000/移动7/弹1/载1步兵系）
**规则**：
- 港口与原作一致：可占领、1000/回合收入、维修补给舰船、生产海军；海军单位可靠泊（`GetMoveCostOnGrid` 覆盖：海军/气垫进港口格=1，移动范围/路径/AI 寻路/逃离全走此口）
- 潜艇下潜/上浮 = 直接复用隐身模块，按钮文字可检查器改（`stealthHideButtonText`/`stealthShowButtonText`，默认隐身/显形，潜艇填下潜/上浮）；下潜贴身可见（相邻1格），只有巡洋舰和潜艇能打水下潜艇（stealthAttackableBy）
- 登陆艇装卸限制（原作）：只能浅滩（BEACH）和港口
- 贴图：原版 GBA 地图贴图（AWBW 提取，16×16，Base2D ×2）：`asscets/units/{Battleship,Cruiser,Submarine,Lander,Carrier}/`，红=Player1 / blue=Player2；炮艇为 DoR 原图按 GBA 橙星/蓝月调色板重上色（24×24，Base2D ×4/3）；港口 `art/Port/Port0~2.png`（中立/P1/P2，32×44，offset -4 底部与机场对齐）
**编辑器**：设施模板列表 + 紧凑格式缩写 Prt + 生成器搜索框（按显示名/描述/场景路径过滤，单位/兵器/设施通用）

## 模块十五：统一弹药系统（AmmoKind）

**文件**：`Infantry/Infantry.Ammo.cs`
**设计**：全部弹药种类（主武器/自爆/照明/隐身弹/显形弹/迷彩弹/生产弹/建造弹）走同一套接口：`GetAmmo/GetMaxAmmo/HasAmmo/TryConsumeAmmo/AddAmmo/RefillAmmo/RefillAllAmmo`。旧消耗点（武器开火、自爆、照明、隐身切换、AI 照明）全部改走 `TryConsumeAmmo` 单一咽喉；设施/补给单位补给走 `RefillAmmo`。
**新增弹种**：生产弹/建造弹（见模块十六/十七），设施补给表新增 `productionAmmoSupply`/`constructionAmmoSupply` + 独立兵种白名单。

## 模块十六：内部服务（舱内单位：内部补给 + 内部回血）

**文件**：`Infantry/Infantry.InternalService.cs`
**字段**（ExportGroup「内部服务（舱内单位）」）：
- `internalSupplyEnabled` + `internalSupplyWhitelist`（独立白名单，空=全部舱内单位）
- `internalHealEnabled` + `internalHealAmount`（每回合回 x HP，**可为负=扣血，扣到 0 以下=舱内单位损毁**）+ `internalHealWhitelist`（与补给/搭载白名单各自独立）
**规则**：每个大回合开始结算（挂在回合燃料结算同处）；补给=燃料+全弹药补满（走统一弹药系统）。航母（给舰载机）/巡洋舰（AW3 可载2直升机）即内置用户。

## 模块十七：生产模块（单位级，11 字段规范）

**文件**：`Infantry/Infantry.Production.cs` / `ActionMenu/ActionMenu.Production.cs`（按钮与目标选择）
**字段**（ExportGroup「生产模块」）：①canProduceUnits ②位置模式（关闭/内部/外部/可选内外）③外部范围 x-y ④自动随机放置 vs 手选目标格 ⑤生产白名单 ⑥生产弹药（决定按钮显示，开工耗1发）⑦总进度 N ⑧每回合进度 Z（如 20/10=两回合）⑨内部生产时可否移动（**=移动会不会打断进度**，关=移动即中断；外部一律移动即中断）⑩自动化（开=点一次后每回合自动推进，只显示中断按钮，生产不消耗行动——照常攻击/操作/待机；关=生产占用回合，单位待机）⑪冷却回合数
**内部生产依赖**：点「内部生产」自动开启搭载模块（仅需搭载，白名单可空——产出单位直接进舱可待着，出舱后不在搭载白名单就装不回去）。
**完工**：内部→直接进舱；外部→放到目标格（手选时高亮有效格）。

## 模块十八：建造模块（与生产对称）

**文件**：`Infantry/Infantry.Construction.cs` / `FacilityDatabase.cs`
**字段**：与生产模块一一对应（canBuildFacilities/位置模式/范围/自动放置/建造白名单（建筑类型名）/建造弹药/进度 N/Z/内部建造时可否移动/自动化/冷却）。
**规则**：内部建造与搭载模块**无绑定**——建好的建筑存舱内数据，用「📦 部署建筑」按钮放到相邻空格；外部建造直接放目标格。可建建筑登记在 `FacilityDatabase`（City/Base/AirPort/Port）。

## 模块十九：检查器优先审计（2026-11-01 顺手修）

- `City`/`AirPort`/`Base`/`Port`：补给兵种表、生产名单改为「仅当未配置时补默认」（默认列表抽成 `DefaultSupplyUnitTypes()` 虚方法，子类重写），改检查器不再被 _Ready 覆盖
- `RepairBoat`：删掉 _Ready 里整套硬编码强制覆盖（移动/燃料/修理字段），改为「0=未配置才补默认」
- 舰船六类 + 港口场景值全部写进 .tscn 检查器，代码零硬编码

## 模块二十：舰载起飞 / 即时生产建造 / 临时设施（2026-11-01）

- **舰载起飞** `canLaunchCargo`（搭载模块分组）：回合开始时已在舱内的单位，在载具本回合**未移动**时可「起飞」到相邻格并保留完整行动（移动/攻击照常）；起飞后载具本回合不能再移动。本回合才进舱/刚生产的单位不能起飞。起飞范围显示青色，普通卸载显示绿色。生产舰 ProducibleBoat 默认开启（DoR 原作航母规则）。
- **卸载地形判定**：卸载格必须是乘员能站上的地形（`CanUnloadUnitToGrid`）。
- **即时生产/建造**：生产总进度 N ≤ 0 或建造总进度 N ≤ 0 时开工当回合立即完成（生产舰造水上飞机、工程车搭临时设施都按此配置）。
- **临时机场 / 临时港口**：工程车 Rig 外部建造（范围 0 = 自身所在格），建造弹药 1 发。临时机场只能建在平原、临时港口只能建在海滩——由建造模块⑫「可建造地形白名单」`constructionAllowedTerrains` 配置（写 `"BEACH"`=对所有建筑生效，写 `"TempPort:BEACH"`=只对该建筑生效；留空才用 `FacilityDatabase` 默认规则）。外部建造与「部署建筑」都受此限制。可维修补给、可占领，不能生产、无收入。
- **搭载判定重写**：显式白名单优先（匹配 unitTypeId/模板 ID 即可装，不再被「排除空军」等属性筛选误拦）；白名单在 .tscn 里统一用 `Array[String]` 格式。
- **截断只对敌军生效**：友军不再截断我方移动，路径可穿过友军；终点是友军且不能装载/合流时直接拒绝移动。
- **攻击按钮**：与攻击范围高亮完全同一套判定（雾中不可见、被搭载、已阵亡、同队的单位都不算），没有可攻击目标就不显示。

## 模块二十一：单位级地形移动覆盖 / 环形射程补漏（2026-11-01）

- **`terrainMoveCostOverrides`**（移动方式旁）：`"地形=开销"`，优先于移动方式表与自定义移动方式。突击舰 KamikazeBoat 配 `BRIDGE=1`、`BEACH=1`（DoR 原作：可从桥下穿过、可进浅滩）。
- **移动范围预览改用 `GetMoveCostOnGrid`**：与实际移动一致（舰船可进港口格的高亮之前缺失）。
- **环形射程**：战列舰（2–6）、航母（3–8）、突击舰（3–5）补上 `useMinMaxAttackRange = true`（之前漏开，只能打相邻格）。
- **视野表**：海军线与 8 个新兵种登记进 `VisionConfig`；配置视野先查兵种自身 ID，再查参照兵种。

## 模块二十二：翻译引擎全游戏化（2026-11-01）
- **接入原生 TranslationServer**：`GameTranslation : Translation` 每语言一个实例（en/ja/ko/de/ru/zh_CN/zh_TW），重写 `_GetMessage`。所有 Control（Label/Button/提示/占位符/OptionButton/PopupMenu/Tab/Window 标题）自动翻译，切换语言时引擎通知整棵场景树即时重译，包括代码后设的文本。
- **匹配顺序**（`TranslateGroup.Engine.cs`，按语言缓存）：精确键 → 反向索引（任意语言译文→原键，T() 已翻过的文字切换语言仍可重译）→ `{0}` 模式键 → 多行逐行 → 剥离首尾 emoji/数字 → 最长优先分词（西文/韩文补空格，全角标点转半角）。
- **词库**：手写 `TranslateGroup.Dictionary.cs`（优先）+ `tools/i18n/terms.tsv`（584 条 UI 词条，5 语）经 `tools/i18n/gen_terms.py` 生成 `TranslateGroup.Terms.cs`；繁中由 OpenCC s2twp 自动生成，并附简→繁单字兜底表。
- **持久化**：语言选择存 `user://settings.cfg`，优先于系统检测。
- **外部覆盖**：`user://translations.csv`（key,en,ja,ko,de,ru,zh_CN,zh_TW），空单元格不覆盖。
- **缺词报告**：非中文语言下未命中的中文片段写入 `user://missing_translations.csv`（同格式，可直接填好后改名为 translations.csv 生效）。
- 旧 `RefreshAllUI` 的 tr_key 回写（会覆盖代码动态文本）已废弃，改为广播 `NotificationTranslationChanged`。语言面板本身 `AutoTranslateMode = Disabled`。
- 局限：由 T() 片段拼接出的英文长句（如 "HP: 80"）切换到第三种语言时，需界面刷新一次才更新。

## 模块二十三：势力配色（参照 Commander Wars color table，2026-11-01）
- **贴图选择统一**：`TeamHelper.PickTeamArt(team, 中立, 红, 蓝)` / `ArtSuffix(team)`。P0 → 中立贴图，P2 → 蓝色贴图，其余（P1、P-1、P3…P100）→ 红色贴图作为模板。
- **Shader 模式 2（模板色表）**（`palette_lut.gdshader`）：模板色相带内像素（红 330°–40° / 蓝 180°–265°，饱和度 ≥0.35）按亮度阶梯映射为「主色×0.4 → 主色 → 主色混白 85%」。红蓝贴图共有的肤色（实测 6 种）排除。P1/P2 主色未改时直出原图。
- **修复**：
  - 城市/工厂从不切换势力动画，占领后仍是中立图。现由 `Facility.GetTeamAnimNames()` 统一处理。
  - 港口/机场（含临时港口、临时机场）新增势力回落中立灰图且不染色。
  - P2 的蓝色贴图原先映射到副色，改主色无效。
  - 激光束颜色写死红/蓝，现跟随势力主色。
  - 黑水晶 Modulate 与 Shader 重复染色，新增势力显示无色。
  - 卸载菜单图标不带势力色。
  - Shader 忽略 Modulate 透明度，导致潜行半透明失效。
- **即时刷新**：势力面板保存或恢复默认后，`FactionPaletteManager.RefreshAllVisuals` 刷新全场景单位、设施和兵器，无需重启场景。
- 巨型炮、死光炮原作只有中立贴图，不染色。

---

## 模块二十四：天气系统（全球+区域双层，2026-11-15 补录）

**文件**：`spricts/WeatherManager.cs`
**机制**：全球天气（晴/雨/雪/沙暴）+ 区域天气栈（每格独立，异种最多叠四层）＋攻防乘区＋CO天气免疫只管全球层。雨强制开雾、雪增油耗、沙暴压视野与间射射程。
**三界适配**：✅ 全吃（作用于格子，单位/兵器/设施共享同一物理层）。

## 模块二十五：多屏系统（自由拓扑，2026-11-15 补录）

**文件**：`spricts/GridManager/GridManager.FreeformScreens.cs`
**机制**：屏=四向连通块；可并屏/拆屏/扣空屏；跨屏须传送或专门连接；CO可绑屏；寻路按 screenId 硬隔离。
**三界适配**：✅ 空间层，三界共乘。

## 模块二十六：CO模块系统（2026-11-15 补录）

**文件**：`spricts/COSystem/`（CoModuleSystem / CoProfile / CoEditorUI / 30机制模块 / 76档案 / 180搭配）
**机制**：CO=模块拼装JSON；多搭档合体求和；能量=主SCOP星+全搭档COP星；maxStacks叠加上限；切将=全军再动（行动/兵器/生产/地形）；绑屏CO看本屏存亡。
**三界适配**：✅ 全域（CO影响单位生产/作战与设施经济）。

## 模块二十七：推动系统·犀牛四件套（2026-11-15 补录）

**文件**：`spricts/Infantry/Infantry.Push.cs` / `PushPlan.cs` / `PushResponse.cs` / `PushRuntime.cs`
**机制**：推动/反弹/卸力/转移四件套＋重量（0~9999，随血衰减）＋链式多米诺＋重叠冲突裁决＋脆弱冲击。灵感=Warside犀牛推土车（反僵局神器）。
**三界适配**：⚠️ 仅单位侧——**工单：推动模块适配兵器/设施**。

## 模块二十八：多格单位（2026-11-15 补录）

**文件**：`spricts/Infantry/Infantry.MultiTile.cs`（二期：旋转/攻击锚点/寻路足印/运输解禁）
**机制**：isMultiTileUnit＋足印尺寸/掩码＋朝向四态＋占位裁定（友堵/敌碾）＋非法地形严格模式＋最低安全格阈值。
**三界适配**：⚠️ 仅单位侧（兵器多格走 Weapon 足印体系，设施走占地体系——三套并存）。

## 模块二十九：受击弱点格·精度崩（2026-11-15 补录）

**文件**：`spricts/Infantry/Infantry.UnitWeakPoint.cs`＋`Combat.cs`＋`Grids.cs`＋`AI_Manager.Planning.cs`
**机制**：弱点格可选目标（关闭=全格可打）＋每格受击倍率＋精度崩（被打格→下次攻击乘数，一次性消耗，走存档）。大型兵器 IsWeakPointGrid 机制完整移植。
**三界适配**：⚠️ 仅单位侧——**工单：弱点格适配兵器**（兵器已有IsWeakPointGrid但无逐格差异表）。

## 模块三十：统一弹药·扩展弹种（2026-11-15 补录）

**文件**：`spricts/Infantry/Infantry.Ammo.cs` / `RotateAmmo.cs`
**机制**：全部弹种单一咽喉 TryConsumeAmmo；含旋转弹/推动弹等新弹种；补给/补弹/八类增减全适配。
**三界适配**：✅ 单位全量；兵器走 Weapon 弹药组（对称但独立管线）。

## 模块三十一：庇护系统（兵器容器，2026-11-15 补录）

**文件**：`spricts/Weapon/Weapon.Shelter.cs`
**机制**：canShelterUnits＋兵种白名单＋进驻/卸载＋内部服务映射＋销毁清理。
**三界适配**：⚠️ 仅兵器当容器侧。

## 模块三十二：维护费系统（2026-11-15 补录）

**文件**：`spricts/UpkeepModules.cs`
**机制**：upkeepCostPerTurn＋失败七选（势力败/胜/换队/中立/自毁/变形/字典事件）＋白名单势力免扣。
**三界适配**：✅ 三界全通吃（单位/兵器/设施各自Export）。

## 模块三十三：通用变形运行时（2026-11-15 补录）

**文件**：`spricts/UniversalTransformRuntime.cs`＋三界 DeathTransform partial
**机制**：单位/兵器/设施互变＋映射表（覆盖/增减）＋保留血量选项＋失败恢复＋多格清理。
**三界适配**：✅ 本就是为三界而生。

## 模块三十四：付费补弹（2026-11-15 补录）

**文件**：`spricts/PaidResupplyModules.cs`
**机制**：弹药资金化——花钱补弹（停车场#9的实现体）。
**三界适配**：✅ 弹药体系在谁身上谁就能付费。

## 模块三十五：设施CoW色板（2026-11-15 补录）

**文件**：`TeamHelper.cs`（FactionPaletteManager）＋`asscets/shaders/cow_palette.gdshader`
**机制**：(R,G)二维查表——palette[tex.r, tex.g]；势力→色板映射（P-1=purple正典）；材质缓存+ReleaseCowPaletteCaches关闭释放。
**三界适配**：✅ 设施/兵器共用（兵器Crystal系同管线）。

## 模块三十六：传送链（2026-11-15 补录）

**文件**：`spricts/TeleportLink.cs`
**机制**：N:M显式链＋单向/互传＋传送按钮＋落点校验（非法地形拒绝）。
**三界适配**：✅ 单位走链；多格单位传送=全足印判定（二期补）。

## 模块三十七：Export字典映射与存档编解码（2026-11-15 补录）

**文件**：`spricts/ExportFieldMapper.cs` / `ExportPersistenceCodec.cs`
**机制**：全引擎Export的字符串↔值双向映射＋存档三格式＋紧凑地图diff。所有新模块的隐形地基。
**三界适配**：✅ 基建，三界通用。

## 模块三十八：生产双经济定价（2026-11-15 补录）

**文件**：`spricts/ProductionPricing.cs`
**机制**：即时1.2倍/待机0.8倍＋逐兵种降价字典＋AI同价评估。
**三界适配**：✅ 设施生产＋SuperFactory兵器生产。

## 模块三十九：地形皮肤管线（2026-11-15 补录）

**文件**：`spricts/TerrainRegistry.cs` / `TerrainStyleManager.cs`
**机制**：地形注册表＋样式（aw1/2/ds/dor/wg）＋调色板＋编辑器即换肤。
**三界适配**：✅ 地图层。

## 模块四十：报错上屏（2026-11-15 补录）

**文件**：`spricts/ScreenErrorLogger.cs`
**机制**：PushError/警告全上屏＋复制按钮＋诊断插桩。CI失败自动开issue带日志。
**三界适配**：✅ UI基建。

## 模块四十一：双卫星系统（2026-11-15 补录）

**文件**：`spricts/Infantry/Infantry.SatelliteModules.cs`
**机制**：力量卫星（Black Onyx力场可玩化）＋攻击卫星（盟军回应伏笔+天基打击）。
**三界适配**：⚠️ 单位层（战略兵器身份）。

## 模块四十二：吞并/赋能（2026-11-15 补录）

**文件**：`spricts/DevourModules.cs` / `EmpowermentModules.cs` / `EmpowermentRuntime.cs`
**机制**：范围Buff/Debuff任意Export映射＋解能快照恢复＋N:M叠层＋权重裁决＋手动/自动＋目标筛选（敌友盟/三对象/黑名单）。
**三界适配**：✅ 三界全通吃。

## 模块四十三：ActionResource 操作资源体系（2026-11-15 补录·正式立档）

> 原"提案中：准备/冷却·哑火"于今夜落地，升级为正式模块。抬枪兵架枪为首个实装单位。

### 总开关
`actionResourceEnabled=false`（默认）= 全部操作不受管，零干扰；只在显式开启的单位上接管。与 PrepEngine 行动门串联不并联。

### 每操作独立三池（A 的消耗碰不到 B）
- `actionTable`：受管操作名表（默认 attack/move/capture/stealth/push/produce，可自定义任意名）
- `actionFillPerTurn`：各池每回合独立装填（抬枪 attack=10）
- `actionMax`：池上限（0/缺省=无限蓄能——220 连打 11 发/一辈子变身全合法）
- `actionCosts`：消耗品法：自己的池扣多少（-1/缺省=不扣）
- `actionUnlockAt`：纪念法：攒到 n 永久解锁（-1=不用）
- `actionCooldownTurns`：用后该操作自己冷却 n 回合——绝不拖累别的操作
- `actionMaxPerTurn`：每操作每回合次数上限

### 共享全局桶（可选通道）
`useSharedBucket`：消耗从共享桶扣（跨操作经济）；`sharedBucketFillPerTurn` 回充；`maxBucketAmmo/currentBucketAmmo`。

### 冷却对表三规则（v3）
`actionCooldownRules`：{kind:1=窗口|2=全局, window, limit, cooldown, refresh}，运行时 `actionWindowUses/actionWindowTurn/actionTotalUses/actionTotalTurns`。

### 哑火全家
- 阈值：`misfireHpBelow/Above`（血量）、`misfireDamageBelow/Above`（伤害量）、`misfireAmmoKind+Below/Above+DeltaBelow/DeltaAbove`（弹药现值/一次性变化量）
- 处置：`misfireBlockOps`（禁哪些操作）+ `misfireBlockTurns`（0=仅当回合），运行时 `actionBlockLeft`
- 弱点格：`actionWeakCellReset`（序号格=1 → 被打中全池清零）
- 手动：`actionManualOff`（自主关效果）、`actionFillConsumesAction`（装填耗不耗行动）

### 触发事件 v1
`actionEvents`：{action, when(always/current>=n), field, mode(set/add), value, temp(0永久)}。

### 炮台锁
`actionLockAsTurret`：有存货=固定炮台。`ActionResourceRuntime.MovementLocked` 判分池或共享桶存货，锁行动菜单"移动"按钮与快捷移动（TryQuickMove）双门禁。

### 七道门（执行入口挂门清单）
| 操作 | 位置 | 资源ID |
|---|---|---|
| 攻击/隐身 | 行动菜单门禁 | attack/stealth |
| 移动 | MovementLocked 双门禁 | （锁，非消耗） |
| 推动 | CanUseSeparatePushAction/TryMarkPushActionUsed | push |
| 占领 | PerformCapture 启动点 | capture |
| 建造 | CanStartConstruction/StartConstruction | build |
| 生产 | CanStartProduction/StartProduction | produce |
| 传送 | PerformTeleport（落点校验后结算） | teleport |
| 兵器开火 | DeathRay/黑水晶/黑炮 PerformAttack 首行 | specialfire |
| AI 侧 | AI_Manager DoAction 顶挂，未装填跳过计划 | attack/capture |

### ActionIds ↔ PrepEngine(Ops) 对照表（A11）
| ActionIds | PrepEngine Ops | 状态 |
|---|---|---|
| attack | Ops.Attack | 待对齐 |
| move | Ops.Move | 参谋门禁已引用 |
| capture | Ops.Capture | 待对齐 |
| stealth | Ops.Stealth | 待对齐 |
| push | Ops.Push | 待对齐 |
| produce | Ops.Produce | 待对齐 |
| build | Ops.Build | 待对齐 |
| teleport | Ops.Teleport | 待对齐 |
| specialfire | Ops.SpecialFire | 待对齐 |

> PrepEngine 本体尚未入库，Ops 全命名暴露后逐格核对本表。

### 文件
- `spricts/ActionIds.cs`（操作名常量）
- `spricts/ActionResourceRuntime.cs` + `ActionResourceRuntime.Port.cs`（运行时/桥）
- `spricts/Infantry/Infantry.ActionResource.cs`、`spricts/Facility/Facility.ActionResource.cs`、`spricts/Weapon/Weapon.ActionResource.cs`（三界同构 Export 组，逐字段一致）

---

## ~~提案中：准备/冷却·哑火（抬枪兵架枪）~~ → 已落地为模块四十三

两阶段Export骨架已设计（冷却水桶弹药＋消耗品/纪念法双方案＋触发事件引擎＋冷却对表三规则），代码未落，接入点侦察完毕待命。

---

## 附录：三界适配总表（可操作模块 × 单位/兵器/设施）

| 模块 | 单位 | 兵器 | 设施 | 适配工单 |
|---|---|---|---|---|
| 攻击/反击/矩阵 | ✅ | ✅ | ✅(设施战斗) | — |
| 推动四件套 | ✅ | ⚠️工单 | ⚠️工单 | 二十七 |
| 弱点格/精度崩 | ✅ | ⚠️工单 | ⚠️工单 | 二十九 |
| 多格/足印 | ✅ | ✅ | ✅(三套并存) | 统一足印层? |
| 维护费/变形/赋能/色板 | ✅ | ✅ | ✅ | — |
| 准备/冷却 | ✅ | ✅ | ✅ | 模块四十三 |

> 图鉴维护规矩：新模块落地须补录本表＋更新三界适配列。
