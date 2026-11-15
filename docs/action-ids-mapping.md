# ActionIds 操作命名总表（A11 对齐基准，开创人2026-11-15）

> 本表是全引擎操作命名的唯一基准。`ActionIds.cs` 头注已立约：另一参谋 AI 的操作命名对齐此表（大小写不敏感）。
> PrepEngine(Ops) 侧尚未入库——其命名暴露后，在末表逐行核对，差异标红提 issue。

## 一、ActionIds 注册表（spricts/ActionIds.cs，31 常量）

| 常量 | 值 | 语义 | 挂门/消费位置 |
|---|---|---|---|
| Attack | attack | 攻击 | 行动菜单门禁；AI DoAction(Attack/AttackWeapon) |
| Move | move | 移动 | MovementLocked 锁（锁非消耗）；actionTable 默认位1 |
| Capture | capture | 占领 | PerformCapture 启动点；AI DoAction |
| Stealth | stealth | 隐身 | 行动菜单门禁 |
| Scent | scent | 气息 | 待挂 |
| TearFlag | tearflag | 拆旗 | 待挂 |
| Load | load | 装载 | 待挂（AI Act.Load 同） |
| Unload | unload | 卸载 | 待挂（AI Act.Unload 同） |
| Push | push | 推动 | CanUseSeparatePushAction / TryMarkPushActionUsed |
| Empower | empower | 赋能 | 待挂 |
| Rotate | rotate | 旋转 | 待挂 |
| Build | build | 建造 | CanStartConstruction / StartConstruction |
| Produce | produce | 生产 | CanStartProduction / StartProduction |
| Wait | wait | 待机 | 待挂 |
| Repair | repair | 修理 | 待挂 |
| Illuminate | illuminate | 照明 | 待挂 |
| Supply | supply | 补给 | 待挂 |
| Teleport | teleport | 传送 | PerformTeleport（落点校验后结算） |
| Counter | counter | 反击 | 待挂 |
| Rebound | rebound | 反弹 | 待挂 |
| Mitigation | mitigation | 减伤 | 待挂 |
| Transfer | transfer | 转移 | 待挂 |
| SpecialFire | specialfire | 兵器开火 | DeathRay / 黑水晶 / 黑炮 PerformAttack |
| Shelter | shelter | 庇护 | 待挂 |
| Devour | devour | 吞噬 | 待挂（吞噬模块 Consume 处） |
| Custom1/2/3 | custom1..3 | 作者自定义 | 预留 |

## 二、AI Act 枚举 ↔ ActionIds（库内实映射，AI_Manager.cs）

| AI Act | ActionIds | 挂门状态 |
|---|---|---|
| Wait | wait | 未挂 |
| Attack | attack | ✅ AI DoAction |
| AttackWeapon | attack | ✅ 同（兵器攻击归 attack 池） |
| Capture | capture | ✅ 同 |
| Merge | —（注册表无对应常量） | 差半口：补 Merge 常量或走自定义 |
| Load | load | 未挂 |
| Unload | unload | 未挂 |
| Supply | supply | 未挂 |
| Repair | repair | 未挂 |
| Illuminate | illuminate | 未挂 |
| Explode | —（自爆模块自身开关） | 差半口 |
| Hide | stealth | 语义重叠（Hide/Reveal 与 stealth 的覆盖关系待裁定） |
| Reveal | —（与 Hide 配对） | 差半口 |

## 三、PrepEngine(Ops) ↔ ActionIds（对端缺席，预留）

| PrepEngine Ops | ActionIds | 状态 |
|---|---|---|
| Ops.Move | move | 参谋门禁已引用（据其说明） |
| （其余待其入库暴露全命名） | … | 逐行对齐时填实 |

> 对齐协议：Ops 全命名落地当轮，按本表一、三逐项核对；命名不一致以 ActionIds 为准改 Ops 侧（头注立约已给定方向）。
