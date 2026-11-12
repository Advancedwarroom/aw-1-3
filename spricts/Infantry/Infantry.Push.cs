using Godot;
using System;

public partial class Infantry
{
    public enum PushTriggerMode
    {
        Disabled,
        Attack,
        SeparateAction
    }

    public enum EqualWeightPushMode
    {
        Stop,
        AllowSimultaneous
    }

    public enum PushDirectionMode
    {
        AwayFromPusher,
        Fixed,
        OppositeSides
    }

    public enum PushTargetClass
    {
        InfantryOnly,
        AnyPushableEntity
    }

    public enum PushTargetTeamMode
    {
        EnemiesOnly,
        FriendliesOnly,
        AlliesOnly,
        Everyone,
        Custom
    }

    public enum PushResourceType
    {
        None,
        Funds,
        Health,
        Fuel,
        PrimaryAmmo
    }

    [ExportGroup("推动模块")]
    [Export] public bool pushEnabled = false;
    [Export] public PushTriggerMode pushTriggerMode = PushTriggerMode.Disabled;
    [Export] public float weight = 1.0f;
    [Export] public bool weightScalesWithHealth = false;
    [Export] public EqualWeightPushMode equalWeightPushMode = EqualWeightPushMode.Stop;

    [ExportSubgroup("独立推动操作")]
    [Export] public bool pushActionCountsAsAction = true;
    [Export] public bool pushCanBeUsedAfterMoving = true;
    [Export] public bool pushOncePerTurn = true;

    [ExportSubgroup("基础推动参数")]
    [Export] public int pushDistance = 1;
    [Export] public int pushMinTargetRange = 1;
    [Export] public int pushRange = 1;
    [Export] public bool pushCanTargetEnemies = true;
    [Export] public bool pushCanTargetFriendlies = false;
    [Export] public bool pushCanTargetAllies = false;
    [Export] public PushTargetTeamMode pushTargetPolicy = PushTargetTeamMode.Custom;
    [Export] public string[] pushTargetWhitelist = System.Array.Empty<string>();
    [Export] public string[] pushTargetBlacklist = System.Array.Empty<string>();

    [ExportSubgroup("方向与目标")]
    [Export] public bool pushChainEnabled = true;
    [Export] public bool pushUsesWeight = true;
    [Export] public int pushRamDamage = 0;
    [Export] public PushDirectionMode pushDirectionMode = PushDirectionMode.AwayFromPusher;
    [Export] public Vector2I fixedPushDirection = Vector2I.Right;
    [Export] public PushTargetClass pushTargetClass = PushTargetClass.InfantryOnly;
    [Export] public int pushMaxChainStacks = 0; // 0 = unlimited

    [ExportSubgroup("推力与资源")]
    [Export] public bool pushForceScalesWithDistance = false;
    [Export] public float pushForcePerTile = 1.0f;
    [Export] public PushResourceType pushCostResource = PushResourceType.None;
    [Export] public int pushCostAmount = 0;
    [Export] public int pushCooldownTurns = 0;

    [ExportSubgroup("目标状态效果")]
    [Export] public int pushTargetDamage = 0;
    [Export] public bool pushDamageCanDestroy = false;
    [Export] public bool pushLockTargetToOneHp = false;
    [Export] public int pushTargetFuelDelta = 0;
    [Export] public int pushTargetAmmoDelta = 0;
    [Export] public int pushTargetHealAmount = 0;
    [Export] public bool pushTargetCanOverMaxHealth = false;
    [Export] public int pushTargetMaxHealthDelta = 0;

    [ExportSubgroup("反弹模块")]
    [Export] public bool pushReboundEnabled = false;
    [Export] public bool pushReboundToOrigin = true;
    [Export] public int pushReboundAmmoCost = 0;
    [Export] public int pushReboundFuelCost = 0;
    [Export] public int pushReboundFundsCost = 0;

    [ExportSubgroup("缓冲模块")]
    [Export] public bool pushMitigationEnabled = false;
    [Export] public int pushMitigationTiles = 0;
    [Export(PropertyHint.Range, "0,100,1")] public float pushMitigationPercent = 0f;

    [ExportSubgroup("反重力转移模块")]
    [Export] public bool pushTransferEnabled = false;
    [Export] public Vector2I pushTransferDirection = Vector2I.Zero;
    [Export] public int pushTransferAmmoCost = 0;
    [Export] public int pushTransferFuelCost = 0;
    [Export] public int pushTransferHealthCost = 0;
    [Export] public int pushTransferFundsCost = 0;

    [ExportSubgroup("推动者行为")]
    [Export] public bool pushPusherFollowsChain = false;
    [Export] public bool pushSelfDestructOnInvalidIntermediateTile = true;

    // Runtime-only turn limiter. It is reset by GameManager when this unit's turn refreshes.
    private bool hasUsedPushThisTurn = false;
    private int pushCooldownTurnsRemaining = 0;

    public float GetEffectiveWeight()
    {
        float baseWeight = Mathf.Max(0.0f, weight);
        if (!weightScalesWithHealth)
            return baseWeight;

        int maxHealth = Mathf.Max(1, GetEffectiveMaxHealth());
        float healthRatio = Mathf.Clamp((float)health / maxHealth, 0.0f, 1.0f);
        return baseWeight * healthRatio;
    }

    public bool CanUseSeparatePushAction()
    {
        if (!pushEnabled || pushTriggerMode != PushTriggerMode.SeparateAction)
            return false;
        if (pushCooldownTurnsRemaining > 0) return false;
        if (pushOncePerTurn && hasUsedPushThisTurn)
            return false;
        if (!pushCanBeUsedAfterMoving && isMoved)
            return false;
        if (pushActionCountsAsAction && (hasActed || state == UnitState.Acted))
            return false;
        return true;
    }

    public bool TryMarkPushActionUsed()
    {
        if (!CanUseSeparatePushAction())
            return false;
        hasUsedPushThisTurn = true;
        pushCooldownTurnsRemaining = Mathf.Max(pushCooldownTurnsRemaining, pushCooldownTurns);
        if (pushActionCountsAsAction)
        {
            hasActed = true;
            state = UnitState.Acted;
            SetWaitVisual(true);
        }
        return true;
    }

    public void ResetPushUseForTurn()
    {
        hasUsedPushThisTurn = false;
        if (pushCooldownTurnsRemaining > 0) pushCooldownTurnsRemaining--;
    }
    // ===== 原文补全（2026-11-13）：移动时推动方式① =====
    [Export] public bool pushMoveScalesWithDistance = false;   // 消耗/增减度随移动距离逐步增减
    // ===== 原文补全：地图边缘循环 =====
    [Export] public bool pushWrapAroundEdges = false;          // 撞边从地图另一侧回来
    // ===== 原文补全：重叠冲突仪式 =====
    [Export] public bool pushOverlapExecutesConflict = false;  // 被平移覆盖到身上时执行冲突仪式（false=直接共存）
    [Export] public bool fragile = false;                      // 脆弱：冲突存活后仍受冲击
    [Export] public FragileDamageMode fragileDamageMode = FragileDamageMode.Fixed;
    [Export] public int fragileDamageAmount = 0;               // 固定值模式：扣血（负=增血）
    [Export] public int fragileAmmoDelta = 0;
    [Export] public int fragileFuelDelta = 0;
    // ===== 原文补全：反弹扩展 =====
    [Export] public bool reboundAuto = true;                   // false=弹出菜单选择
    [Export] public int reboundRangeMin = 0;                   // 检查范围n-m；0=自身原位
    [Export] public int reboundRangeMax = 1;
    [Export] public int reboundAttackerDamage = 0;             // 反弹致发动者扣血（负=增）
    [Export] public int reboundAttackerFuelDelta = 0;
    [Export] public int reboundAttackerAmmoDelta = 0;
    [Export] public bool reboundCounterPush = false;           // 蹬鼻子上脸：按重量反推回去
    [Export] public bool reboundCanTargetEnemies = true;
    [Export] public bool reboundCanTargetFriendlies = true;
    [Export] public bool reboundCanTargetAllies = true;
    // ===== 原文补全：转移扩展 =====
    [Export] public Godot.Collections.Array<Vector2I> transferDirections = new(); // Laser式m选1；空=用pushTransferDirection
    [Export] public bool transferAutoDirection = true;         // false=弹出菜单选方向
    // ===== 原文补全：卸力仪式 =====
    [Export] public bool pushMitigationCeremony = false;       // 总开关（false=纯距离卸力旧行为）
    [Export] public int pushMitigationAmmoCost = 0;            // 卸力弹消耗（每次卸力）
    [Export] public bool mitigationProtectsHealth = true;      // 保住哪些（可多选）
    [Export] public bool mitigationProtectsFunds = false;
    [Export] public bool mitigationProtectsFuel = false;
    [Export] public bool mitigationProtectsAmmo = false;
    [Export] public MitigationSideEffectTarget mitigationSideEffectTarget = MitigationSideEffectTarget.None;
    [Export] public int mitigationSideEffectScale = 100;       // 差值作用比例%
    [Export] public Godot.Collections.Array<Vector2I> mitigationSpreadDirections = new(); // 均分方向（空=不扩散）
    [Export] public bool mitigationAuto = true;                // false=弹出菜单（推动前预选择）
    [Export] public bool mitigationWeakensWeight = false;      // 削弱目标重量实际作用
    [Export] public MitigationValueMode mitigationWeightWeakenMode = MitigationValueMode.Fixed;
    [Export] public float mitigationWeightWeakenValue = 0;
    [Export] public bool mitigationWeightDiffAffects = false;  // 重量差影响卸力效果
    [Export] public MitigationValueMode mitigationWeightDiffMode = MitigationValueMode.Percent;
    [Export] public float mitigationWeightDiffValue = 0;
    [Export] public bool mitigationWhitelistEnabled = false;   // 卸力只对白名单攻击方生效
    [Export] public Godot.Collections.Array<string> mitigationWhitelist = new(); // 攻击方UnitTypeId
    // ===== 原文补全：单位间攻击固定值模式（"我有Export不代表要用"） =====
    [Export] public bool attackUsesFixedDamage = false;        // 单位间攻击用固定值模式（跳过伤害矩阵）
    [Export] public int attackFixedDamage = 0;                 // 固定值：正=给目标加血，负=扣血（GetAttackDamage 内取反）
    [Export] public int attackFixedAmmoDelta = 0;
    [Export] public int attackFixedFuelDelta = 0;
    // ===== 四种新弹药字段（推动模块弹药） =====
    [ExportGroup("推送弹")]
    [Export] public int currentPushAmmo = 0;
    [Export] public int maxPushAmmo = 0;
    [ExportGroup("反弹推进弹")]
    [Export] public int currentReboundAmmo = 0;
    [Export] public int maxReboundAmmo = 0;
    [ExportGroup("卸力弹")]
    [Export] public int currentMitigationAmmo = 0;
    [Export] public int maxMitigationAmmo = 0;
    [ExportGroup("反重力场弹")]
    [Export] public int currentAntiGravityAmmo = 0;
    [Export] public int maxAntiGravityAmmo = 0;

    // ===== 菜单预选择（推动前由 ActionMenu.PushChoice 写入，同步推演一次完成）=====
    public Vector2I? pushChosenTransferDirection = null;   // 转移 m 选 1 的选定方向（null=自动取首）
    public int? pushChosenReboundRange = null;             // 反弹选定距离（null=自动取最远）
    public Godot.Collections.Array<Vector2I> pushChosenSpreadDirections = new(); // 卸力均分方向 n 选 m（空=全触发）
    public bool pushChoicePending = false;                 // 等待玩家选择，ResolvePushChain 入口检查


    public enum FragileDamageMode { Fixed, AttackCalculation, HealthFundsRatio, LockOneHp }
    public enum MitigationSideEffectTarget { None, Attacker, Other }
    public enum MitigationValueMode { Fixed, Percent }

    // ===== 链深度规则 + 联动 + 弹药（一致性补齐 2026-11-13）=====
    [Export] public bool pushChainAccumulatesWeight = true;    // 原文：开=每环加上目标重量推下一环；关=从始至终用初始推力
    [Export] public float pushChainAccumulatedWeightCap = 0f;  // 原文"累计上限=多大数值"：累计重量封顶（0=不限，单位数上限用pushMaxChainStacks）
    [Export] public Godot.Collections.Array<PushTargetTeamMode> pushChainDepthTeamModes = new();   // 第n环势力规则（1-based，空=全局）
    [Export] public Godot.Collections.Array<PushTargetClass> pushChainDepthTargetClasses = new();  // 第n环可推种类
    [Export] public Godot.Collections.Array<string> pushChainDepthBlacklists = new();              // 第n环黑名单，逗号分隔UnitTypeId
    [Export] public bool pushAutoPickTargetAtMaxRange = false; // 原文：pushRange=1时自动沿指向取最远合规目标
    [Export] public bool pushTargetEffectsScaleWithDistance = false; // 目标增减度按被推格数放大
}
