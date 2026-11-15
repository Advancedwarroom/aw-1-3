// Infantry.ActionResource.cs — ✅ 开创人2026-11-15: 统一性基底·三界同构Export组（与另两界逐字段一致）
using Godot;
using System;

public partial class Infantry
{
    [ExportGroup("操作资源·准备/冷却/充能（统一基底）")]
    [Export] public bool actionResourceEnabled = false;                                  // 总开关（关=全部操作不受管，零干扰）
    [Export] public string[] actionTable = new string[] { "attack", "move", "capture", "stealth", "push", "produce" };  // 本实例受管操作（从ActionIds挑，可自定义任意名）
    // ── 每操作独立三池（A的任何消耗碰不到B）──
    [Export] public int[] actionFillPerTurn = new int[] { };                          // 各池每回合独立装填（抬枪attack=10）
    [Export] public int[] actionMax = new int[] { };                                    // 各池上限（0/缺省=无限蓄能——220连打11发/一辈子变身全合法）
    [Export] public int[] actionCosts = new int[] { };                                   // 消耗品法：自己的池扣多少（-1/缺省=不扣）
    [Export] public int[] actionUnlockAt = new int[] { };                              // 纪念法：攒到n永久解锁（-1=不用；20可攻/40变身）
    [Export] public int[] actionCooldownTurns = new int[] { };                          // 用后【该操作自己】冷却n回合——绝不拖累别的操作
    [Export] public int[] actionMaxPerTurn = new int[] { };                            // 每操作每回合次数上限（0/缺省=不限）
    [Export] public int[] actionClearOnMove = new int[] { };                          // 移动清哪些池/解锁（0否1是；P1.5接移动钩子）
    [Export] public bool actionLockAsTurret = false;                                    // 有存货=固定炮台（P1.5锁移动按钮）
    [Export] public bool actionMisfireFlag = false;                                     // 运行时：哑火旗（受击置位,回合开始清算）
    [Export] public Godot.Collections.Array<Godot.Collections.Dictionary> actionEvents = new(); // 触发事件：{action,when(always/current>=n),field,mode(set/add),value,temp(0永久)}

    // ── 哑火阈值（受击判定）──
    [Export] public int misfireHpBelow = -1;
    [Export] public int misfireHpAbove = -1;
    [Export] public int misfireDamageBelow = -1;
    [Export] public int misfireDamageAbove = -1;

    // ── 运行时（全走Export存档管线）──
    [Export] public int[] actionCurrent = new int[] { };                              // 各池现值
    [Export] public int[] actionUsedThisTurn = new int[] { };
    [Export] public int[] actionCooldownLeft = new int[] { };
    [Export] public int[] actionUnlocked = new int[] { };                            // 纪念法解锁态（0未解锁1已解锁）

    // ── 共享全局桶（可选通道：跨操作经济数据，v1.5接入门；枚举/三臂已备）──
    [Export] public int currentBucketAmmo = 0;
    [Export] public int maxBucketAmmo = 100;
    public bool ActionCanUse(string action) => ActionResourceRuntime.CanUse(this, action);
    public void ActionConsume(string action) => ActionResourceRuntime.Consume(this, action);
    public void ActionTurnStart() => ActionResourceRuntime.TurnStart(this);
    public void ActionFireEvents(string action) => ActionResourceRuntime.FireEvents(this, action);
    public void ActionMisfireCheck(int hpAfter, int damage) => ActionResourceRuntime.OnHitMisfire(this, hpAfter, damage);
    // ── v3：共享全局桶+冷却对表三规则 ──
    [Export] public bool useSharedBucket = false;                                     // 消耗从共享全局桶扣（跨操作经济）
    [Export] public int sharedBucketFillPerTurn = 0;                                  // 共享桶每回合装填
    [Export] public Godot.Collections.Array<Godot.Collections.Dictionary> actionCooldownRules = new(); // 冷却对表：{kind:1=窗口|2=全局, window, limit, cooldown, refresh}
    [Export] public int[] actionWindowUses = new int[] { };                          // 运行时：窗口计数
    [Export] public int[] actionWindowTurn = new int[] { };                          // 运行时：窗口锚定
    [Export] public int[] actionTotalUses = new int[] { };                           // 运行时：全局总次数
    [Export] public int actionTotalTurns = 0;                                          // 运行时：己方回合计数


    public void OnActionMoved() => ActionResourceRuntime.OnMoved(this);

    [ExportGroup("准备模块·移植层（开创人令 2026-11-15）")]
    [Export] public int[] actionBlockLeft = new int[] { };        // 运行时：各操作哑火禁操作剩余回合
    [Export] public int[] actionWeakCellReset = new int[] { };    // 弱点格装填手：序号格=1 → 被打中全池清零
    [Export] public bool actionManualOff = false;                  // 运行时：自主关闭效果（关闭效果按钮）
    [Export] public bool actionFillConsumesAction = true;          // 手动装填耗不耗行动
    [Export] public int misfireAmmoKind = -1;                    // 哑火·监视弹药（AmmoKind；-1=不看）
    [Export] public int misfireAmmoBelow = -1, misfireAmmoAbove = -1;          // 现值小于/大于 x
    [Export] public int misfireAmmoDeltaBelow = -1, misfireAmmoDeltaAbove = -1; // 一次性变化量小于/大于 x
    [Export] public string[] misfireBlockOps = new string[] { };  // 哑火触发时禁这些操作
    [Export] public int misfireBlockTurns = 0;                     // 禁操作持续回合（0=仅当回合）
}
