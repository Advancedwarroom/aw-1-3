using Godot;

// PrepRows.cs - 准备/冷却模块四张表的行 Resource。
// sentinel 约定（开创人定）：int=-1 / PrepTriState=Global / 字符串空 = 用模块全局默认表；
// 行内填了=该操作独立判定（★A 操作的冷却/扣款绝不污染 B——状态跟行走）。

public enum PrepTriState { Global, Yes, No }
public enum PrepConsumeMode { Consume, Threshold }       // 消耗品法=扣trigger / 纪念法=到值即允许、超了都允许不扣
public enum PrepModifyMode { Add, Subtract, SetFixed }   // 加成 / 减成 / 修改为固定值
public enum PrepCapScope { DistinctKinds, AllStacks, ExceptSelf } // 上限口径三选一：总体只看不同种类 / 包括所有叠加 / 除自身叠加以外的所有叠加
public enum PrepCooldownScope { PerOperation, TotalCount }
public enum PrepCooldownWindow { ThisTurn, LastNTurns, GlobalTotal }

// ★ 操作表行：每个操作一张独立判定表
public partial class PrepOpConfig : Resource
{
    [Export] public string operation = "";              // 操作名（Ops 名册；不识别=不调用）
    [Export] public int prepPerTurn = -1;               // 装填n每回合（前装=10；-1=全局；0=不自动）
    [Export] public int prepMax = -1;                   // 该操作装填上限（-1=全局/不限=桶上限）
    [Export] public int prepTrigger = -1;             // 装填到n才能成功进行此操作（-1=全局；0=不需要）
    [Export] public PrepConsumeMode consumeMode = PrepConsumeMode.Consume;
    [Export] public PrepTriState oncePerTurn = PrepTriState.Global;    // 一回合内同一操作只触发一次扣款
    [Export] public int maxUsesPerTurn = -1;           // 一回合最大扣款/调用次数（<=0=不限）
    [Export] public int cooldownTurns = -1;             // 操作完后该操作歇N回合才能再次装填/操作（-1=全局；0=不冷却=可连发11次）
    [Export] public int cooldownRefreshesTo = -1;       // 冷却结束刷新到n次机会（-1/0=全刷新；<已用=留差额→次数本身成为冷却实现品）
    [Export] public PrepTriState storeAcrossTurns = PrepTriState.Global; // 装填后切换回合是否存
    [Export] public PrepTriState locksMovementWhenStocked = PrepTriState.Global; // 有存货=固定炮台（锁移动；触发操作后进度清零解锁）
    [Export] public string[] resetOnOperations = null;  // 进行这些操作时清进度（null=用全局）
    [Export] public PrepTriState manualFillAllowed = PrepTriState.Global; // 手动装填按钮
    [Export] public PrepTriState refillAfterUse = PrepTriState.Global;   // 操作后自动重新装填（开火后回合不等待）
    [Export] public string prepActionNameOverride = "";// 按钮名覆盖（空=全局 prepActionName）

    // ── 运行时（每操作独立！A 不影响 B）──
    [Export] public int _cooldownLeft = 0;              // 该操作剩余冷却（走存档）
    [Export] public int _refreshedUsesLeft = -1;        // 该操作冷却额度模式剩余次数（-1=未进入；-1走存档）
    public int _usesThisTurn = 0;                       // 该操作本回合已扣款次数（不存档）
}

// ★ 事件表行：Trigger 满足时触发"?"事件
public partial class PrepEventConfig : Resource
{
    [Export] public string operation = "";            // 哪个操作的trigger满足时触发；空=任何操作
    [Export] public int triggerAt = 0;               // 桶≥此值触发（纪念法阈值：到20可攻击/到30可嗅探/到40攻击+30%…）
    [Export] public bool attachToModule = false;     // 是否勾选附加某模块
    [Export] public string targetModule = "";        // 附加模块名（空/不存在=默认不调用）
    [Export] public string fieldName = "";             // 要修改的 Export 字段名（FieldMods 反射：兵种类/队伍/攻击力/最小攻击距离/视野/弹药上限…；空=纯通知）
    [Export] public PrepModifyMode modifyMode = PrepModifyMode.Add;
    [Export] public float value = 0;                 // 数值（0~+∞）
    [Export] public string stringValue = "";          // 字符串值（枚举/字符串字段用）
    [Export] public bool permanent = true;            // 临时Or永久
    [Export] public int durationTurns = 1;           // 临时：多少回合后销毁还原
    [Export] public int maxStacks = 0;               // 本事件调用层数上限（0=不限）
    [Export] public int perTurnInvokeCap = 0;        // 每单位每回合调用次数上限（0=不限）
    [Export] public PrepCapScope capScope = PrepCapScope.ExceptSelf;
    // ── 清理判定（各自独立勾选）──
    [Export] public bool cleanupOnMove = false;
    [Export] public string[] cleanupOnOperations = { };
    [Export] public bool manualOff = false;           // 自主可关闭（生成关闭效果按钮）
    [Export] public bool cleanupOnHit = false;
    [Export] public bool cleanupOnHpChange = false;
    [Export] public bool keepHealthOnTransform = true; // 改变兵种种类时：血量留着其余重置

    public int _invokesThisTurn = 0;                  // 运行时：本回合已触发次数
}

// ★ 冷却表行：三条规则逐行配置
public partial class PrepCooldownConfig : Resource
{
    [Export] public PrepCooldownScope scope = PrepCooldownScope.PerOperation;
    [Export] public string operation = "";            // scope=PerOperation 时的操作名（空=不调用）
    [Export] public PrepCooldownWindow window = PrepCooldownWindow.ThisTurn;
    [Export] public int windowTurns = 1;             // window=LastNTurns 的 n
    [Export] public int threshold = 1;               // 次数大于 m
    [Export] public int cooldownTurns = 1;         // 冷却 h 回合
    [Export] public int refreshTo = 0;              // 结束刷新掉n次机会（0=一致=全刷新；小于已用=留差额，操作次数本身成为冷却实现品）
    [Export] public bool keepWhenIdle = false;       // 冷却结束后不操作是否保留（不操作不触发冷却，就这三条）
}

// ★ 哑火表行：血量/一次性伤害/弹药燃油变化 → 清进度或禁操作
public partial class PrepMisfireConfig : Resource
{
    [Export] public bool clearProgress = true;      // 触发时清装填进度（桶清零）
    [Export] public string[] blockOperations = { };  // 触发时不能进行的操作（空=不额外禁）
    [Export] public int blockTurns = 0;             // 禁操作持续回合（0=仅当回合）
    [Export] public float hpBelow = -1, hpAbove = -1;           // 受击后血量(%)小于/大于 x（-1=不看）
    [Export] public float damageBelow = -1, damageAbove = -1;   // 一次性伤害小于/大于 x
    [Export] public int watchAmmoKind = -1;         // 监视弹药种类（AmmoKind 值；-1=不看）
    [Export] public float ammoBelow = -1, ammoAbove = -1;
    [Export] public float ammoDeltaBelow = -1, ammoDeltaAbove = -1;
    [Export] public bool watchFuel = false;          // 监视燃油
    [Export] public float fuelBelow = -1, fuelAbove = -1;
    [Export] public float fuelDeltaBelow = -1, fuelDeltaAbove = -1;

    [Export] public int _blockedUntilTurn = -1;     // 运行时：禁操作直到第N回合（走存档）
}
