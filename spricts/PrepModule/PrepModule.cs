using Godot;
using System.Collections.Generic;

// PrepModule.cs - 准备/冷却模块：横跨单位/兵器/设施三界的第一个通用底层架构级模块（开创人令 2026-11-15）。
// 用法：把本节点挂在 Infantry / Weapon / Facility 下（命名建议 "PrepModule"）。
// 冷却水桶 = 弹药表第15种 PrepBucket（挂在 Infantry 上走 AmmoKind 全套管线：max/现有/补给/补给车/自费补给；
//           挂在兵器/设施上走本节点自带 moduleBucket/moduleBucketMax）。
// 核心语义：登记进操作表的操作=行内独立判定（A 的冷却不污染 B）；没登记=走全局默认表（默认统一）。
public partial class PrepModule : Node2D
{
    [ExportGroup("总控")]
    [Export] public bool prepEnabled = false;             // 总开关
    [Export] public bool manualToggleButton = false;      // 自主是否可关闭（true=菜单出「关闭效果」按钮）

    [ExportGroup("全局默认表（未登记操作的统一表）")]
    [Export] public int prepPerTurn = 0;                  // 装填n每回合（前装=10；0=不自动装填）
    [Export] public int prepMax = 0;                      // 装填上限（0=不限=桶上限；每种操作可在行内另填）
    [Export] public int prepTrigger = 0;                // 装填到n才能成功进行（0=不需要）
    [Export] public PrepConsumeMode consumeMode = PrepConsumeMode.Consume; // 消耗品法/纪念法
    [Export] public bool oncePerTurn = false;           // 一回合内同一操作只触发一次扣款，后续不扣款
    [Export] public int maxUsesPerTurn = 0;             // 一回合最大扣款/调用次数（0=不限→220堆出来就连着11次）
    [Export] public int cooldownTurns = 0;              // 操作完后该操作歇N回合才能再次装填/操作（0=不冷却=可连发）
    [Export] public int cooldownRefreshesTo = 0;        // 冷却结束刷新到n次机会（0=全刷新；<已用=留差额，次数本身成为冷却实现品）
    [Export] public bool storeAcrossTurns = true;         // 装填后切换回合是否存（能存则回合切换也能用）
    [Export] public bool locksMovementWhenStocked = false; // 有存货时变固定炮台（锁死移动按钮；触发操作后进度清零解锁）
    [Export] public string prepActionName = "装填";       // 手动装填按钮名（抬枪就叫"装填"）
    [Export] public bool prepAutoStart = false;          // false=手动按钮；true=回合自动开始装填
    [Export] public bool prepConsumesAction = true;     // 手动装填耗不耗行动
    [Export] public bool prepLocksMovement = true;      // 架着枪移动=白装（枪架拆了）
    [Export] public bool prepResetOnHit = true;         // ★哑火·通用：受击走哑火表判定
    [Export] public bool fireResetsPrep = true;         // 开火后自动重新装填（回合不等待）
    [Export] public int refillAfterUse = 0;             // 操作后自动加n（0=不加）
    [Export] public string[] resetOnOperations = { };  // 对该单位进行这些操作时清进度
    [Export] public bool resetOnMove = false;           // 移动清进度
    [Export] public bool resetOnAnyAction = false;     // 任何操作清进度

    [ExportGroup("操作表（每行=一个操作独立判定）")]
    [Export] public Godot.Collections.Array<PrepOpConfig> prepOps = new();

    [ExportGroup("事件表（Trigger 满足触发 ? 事件）")]
    [Export] public Godot.Collections.Array<PrepEventConfig> prepEvents = new();

    [ExportGroup("冷却表（次数→冷却三条规则）")]
    [Export] public Godot.Collections.Array<PrepCooldownConfig> prepCooldowns = new();

    [ExportGroup("哑火表（血量/伤害/弹药燃油→清进度或禁操作）")]
    [Export] public Godot.Collections.Array<PrepMisfireConfig> prepMisfires = new();

    [ExportGroup("弱点格联动（多格单位·装填手）")]
    [Export] public int[] weakCellResetPrep = new int[] { };    // ★哑火·弱点格：打中此格(序号)走清进度判定（装填手！1=生效 0=不生效；Godot C# 不支持 bool[] Export）

    [ExportGroup("兵器/设施自带水桶（挂 Infantry 时不用填）")]
    [Export] public int moduleBucketMax = 0;          // 桶上限（0=桶关闭）
    [Export] public int moduleBucket = 0;             // 现有库存（走存档）

    // ── 运行时：未登记操作的全局槽（已登记操作的独立状态挂在各自行上）──
    [Export] public int prepCooldownLeftGlobal = 0;     // 全局槽剩余冷却（走存档）
    [Export] public int prepGlobalUsesLeft = -1;        // 全局槽冷却额度（-1=未进入；走存档）
    public int _globalUsesThisTurn = 0;                 // 全局槽本回合已扣款次数
    public bool moduleOff = false;                        // 手动关闭效果
    public readonly List<TempFieldEffect> activeTempEffects = new(); // 临时效果还原栈（FieldMods 驱动）
    public readonly List<(string op, int turn)> history = new();      // 操作历史（冷却表窗口判定）
    public int _lastTickedTurn = -1;                      // 防同回合重复 tick（双跑保护）
}
