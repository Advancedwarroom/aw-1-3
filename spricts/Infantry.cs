using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

public partial class Infantry : Node2D
{

    [ExportGroup("究极自由：单位类型特性")]
    [Export] public bool cannotCounterWhenAttacked = false; // 攻击时不被反击（如Artillery/Rocket/AntiTank等间接攻击单位）
    [Export] public bool canCounterWhenDefending = true;   // 被攻击时能否反击（Artillery/Rocket=false，默认true）
    [Export] public bool canCounterAtRange = false;        // 能否远程反击（false=仅近战距离1可反击，true=在攻击范围内可反击）
    // ✅ 开创人2026-11-14 对类别乘区(王牌空战式)
    [Export] public bool attackVsCategoryEnabled = false;
    [Export] public UnitCategory attackVsCategory = default;
    [Export] public float attackVsCategoryMultiplier = 1.0f;
    [Export] public bool canMerge = true;

    [ExportGroup("战役记账")]
    [Export] public double campaignScoreValue = 0;      // 摧毁得分
    [Export] public double campaignCrystalValue = 0;    // 摧毁宝钞
    public string lastAttackerTeam = "";                // 凶手追踪(运行时)
    [Export] public bool canActAfterMerge = false;

    // ✅ 开创人2026-11-10：存活胜负倒计时（全部单位通用，默认关闭=0；与兵器/设施同机制同字段名，GameManager.SurvivalCountdowns 统一驱动）
    [ExportGroup("存活胜负倒计时")]
    [Export] public int surviveVictoryTurns = 0;   // 本单位存活满 N 大回合 → 所属势力直接胜利（0=关闭）
    [Export] public int surviveDefeatTurns = 0;    // 本单位存活满 N 大回合 → 所属势力失败出局（0=关闭）
    [Export] public int survivedTurns = 0;           // 已存活大回合数（计时器；[Export] 随存档/导出/导入自动带走）

// ✅ AI 档案（提案 docs/proposals/ai-brain.md）：全部默认=自动，AI 按单位实际能力字段推断行为；填写后覆盖推断
[ExportGroup("AI 档案")]
[Export(PropertyHint.Enum, "自动,突击,占点,守备,刺客")] public int aiRole = 0;   // 自动=按能力推断；突击=只找仗打；占点=优先占领；守备=守己方据点；刺客=打完就隐身/撤离
[Export] public Godot.Collections.Dictionary<string, float> aiTargetWeights = new(); // 目标兵种名→价值倍率（"*"=其余全部）；空=纯伤害预估。例：战斗机 {"*":0}+空军各1
[Export] public int aiRetreatHpThreshold = -1;     // 血≤N 撤退修整；-1=自动(最大血量30%)；0=死战不退
[Export] public bool aiPreferKill = true;           // 能击杀的目标额外加分
[Export] public bool aiFocusFire = true;           // 集火：残血目标额外加分
[Export] public float aiRiskMultiplier = 1f;       // 威胁规避倍率：0=无畏，1=默认，>1=更怕死
public virtual void OnTurnEnd()
{
    originalGrid = null;
    state = UnitState.Idle;
    isMoved = false;
    isAttacked = false;
    movedThisTurnForAutoRepair = false; autoRepairAfterMoveDone = false;
    hasActed = false;   // ✅ 修复：此前从不重置，生产/合流/补给/修理过的单位永久失去补给/修理/自爆等按钮
    hasTeleportedThisTurn = false;  // ✅ 传送每回合限一次：回合结束重置（防止传送后直接传回/二次传送）
    movePoints = defaultMovePoints;
    SetWaitVisual(false);
    // ✅ 恢复 idle 动画后再启动呼吸（animSprite 单位需要）
    string animName = GetIdleAnimName();
    ShowAnimState(animName);
    StartBreath();
}

// 添加到 Infantry.cs 的字段区域（大约在 line 30-40 附近，[ExportGroup("究极自由：单位类型特性")] 之后）

[ExportGroup("单位特性判定")]
[Export] public bool isArmoredUnit = false;           // 是否是装甲单位（影响武器选择）
[Export] public bool IsAirUnit = false;           // 是否为空军单位（影响地形加成判定，不影响移动方式）
    [Export] public bool isAntiAirUnit = false;         // 是否是防空/反空中单位（能对空中目标造成有效伤害）

public enum DefenseBonusType { NoDefenseBonus, CanDefenseBonus }
[Export] public DefenseBonusType defenseBonusType = DefenseBonusType.CanDefenseBonus;  // 能否享有地形防御加成
public bool hasActed = false;
    public ActionMenu actionMenu;
    public Grids grid;
    public Action<Infantry> OnClickPiece;

    public Grids originalGrid;
    public bool isMoved;
    public bool isAttacked;
    public bool hasTeleportedThisTurn;  // ✅ 本回合是否已传送过：传送每回合限一次（ActionMenu.Teleport 使用，回合结束重置）
    [Export] public bool isCoUnit = false;  // ✅ CO 单位（模块19/30：携带 CO Zone 的载体，由 CO 专属单位系统设置）
    // ✅ 模块16（升级）：单位经验——击杀得 XP，等级攻/防每级+4%（CoModuleSystem 查询应用）
    public int xp = 0;
    public int Level => xp >= 300 ? 3 : xp >= 120 ? 2 : 1;
    public void GainXp(int amount) => xp += Mathf.Max(0, amount);
    public Label hpLabel;
    public Sprite2D sprite;

public override void _Ready()
{
    ZIndex = 10;
    AddToGroup("infantry");

    // 视觉组件获取
    spriteRed = GetNodeOrNull<Sprite2D>("Osinfantry");
    spriteBlue = GetNodeOrNull<Sprite2D>("Bminfantry");
    UpdateActiveSprite();
    // ✅ 有 animSprite 时不再兜底找 sprite（避免 FindChild 错误匹配空 Sprite2D 如 BaseSprite）
    if (sprite == null && animSprite == null)
    {
        sprite = GetNodeOrNull<Sprite2D>("Osinfantry");
        if (sprite == null)
            sprite = FindChild("*", true, false) as Sprite2D;
        if (sprite == null)
            sprite = GetChildOrNull<Sprite2D>(0);
    }

    // ✅ 动画组件自动获取（子类不再需要重复写）
    if (animSprite == null)
        animSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
    if (animSprite == null)
        animSprite = GetNodeOrNull<AnimatedSprite2D>("Base2D");
    if (idleSprite == null)
        idleSprite = GetNodeOrNull<Sprite2D>("Sprite2D");
    if (idleSprite == null)
        idleSprite = GetNodeOrNull<Sprite2D>("BaseSprite");
    
    // ✅ 双图层组件自动获取（Sprite2D 版）
    if (baseSprite == null)
        baseSprite = GetNodeOrNull<Sprite2D>("BaseSprite");
    if (normalSprite == null)
        normalSprite = GetNodeOrNull<Sprite2D>("NormalSprite");

    // 修复：无 animSprite 的双图层单位可能被 FindChild("*") 误识别为旧版 sprite。
    // 同时 idleSprite 的 BaseSprite 兜底也不能指向双图层底图，否则旧动画逻辑会隐藏/改写它。
    if (baseSprite != null && sprite == baseSprite)
        sprite = null;
    if (baseSprite != null && idleSprite == baseSprite)
        idleSprite = null;
    
    // ✅ 双图层组件自动获取（AnimatedSprite2D 版）
    if (baseAnimSprite == null)
        baseAnimSprite = GetNodeOrNull<AnimatedSprite2D>("BaseAnimSprite");
    if (baseAnimSprite == null)
        baseAnimSprite = GetNodeOrNull<AnimatedSprite2D>("BaseAnimatedSprite");
    if (normalAnimSprite == null)
        normalAnimSprite = GetNodeOrNull<AnimatedSprite2D>("NormalAnimSprite");
    if (normalAnimSprite == null)
        normalAnimSprite = GetNodeOrNull<AnimatedSprite2D>("NormalAnimatedSprite");
    
    // ✅ 双图层 Sprite2D：把 Normal 层挂到 Base 层下，继承所有变换
    if (baseSprite != null && normalSprite != null && normalSprite.GetParent() != baseSprite)
    {
        normalSprite.Reparent(baseSprite, true);
    }
    // ✅ 双图层 AnimatedSprite2D：同理
    if (baseAnimSprite != null && normalAnimSprite != null && normalAnimSprite.GetParent() != baseAnimSprite)
    {
        normalAnimSprite.Reparent(baseAnimSprite, true);
    }

    actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
    hpLabel = GetNodeOrNull<Label>("HpLabel");
    if (noAmmoIcon == null)
        noAmmoIcon = GetNodeOrNull<AnimatedSprite2D>("NoAmmoIcon");

    // ✅ 只有勾选 useDefaultConfig 时才应用代码默认值和 UnitRegistry
    if (useDefaultConfig)
    {
        // ========== 全局统一默认值 ==========
        // 血量：所有单位100，Oozium在子类覆盖为200
        if (maxHealth == 0) maxHealth = 100;
        if (health == 0) health = maxHealth;

        // 防御力：所有单位0，Oozium在子类覆盖为10
        if (baseDefense == 0) baseDefense = 0;

        // 移动力
        if (defaultMovePoints == 0 && movePoints == 0) 
        {
            defaultMovePoints = 3;
            movePoints = defaultMovePoints;
        }
        if (movePoints == 0) movePoints = defaultMovePoints;

        // 射程
        if (maxAttackRange == 0) 
        {
            if (attackRange > 0) maxAttackRange = attackRange;
            else maxAttackRange = 1;
        }
        if (minAttackRange == 0) minAttackRange = 1;
        if (attackRange == 0 && maxAttackRange > 0) attackRange = maxAttackRange;

        // 武器系统（保留，但伤害不走这里）
        if (!hasPrimaryWeapon && !hasSecondaryWeapon)
        {
            if (hasSecondaryWeapon == false) hasSecondaryWeapon = true;
            if (secondaryAttack == 0) secondaryAttack = 100;
            secondaryAntiArmor = false;
            secondaryAntiInfantry = true;
        }
        // 修复：主武器单位也必须有副武器兜底，防止主武器弹药耗尽后无法攻击
        if (hasPrimaryWeapon && !hasSecondaryWeapon)
        {
            hasSecondaryWeapon = true;
            if (secondaryAttack == 0) secondaryAttack = 100;
            secondaryAntiArmor = false;
            secondaryAntiInfantry = true;
        }
        if (hasPrimaryWeapon)
        {
            if (maxPrimaryAmmo == 0 && primaryHasLimitedAmmo) maxPrimaryAmmo = 99;
            if (currentPrimaryAmmo <= 0 && maxPrimaryAmmo > 0) currentPrimaryAmmo = maxPrimaryAmmo;
        }

        // 占领
        if (captureAbility == default) captureAbility = CaptureAbility.CanCapture;
        if (capturePower == 0) capturePower = 10;

        // 燃料
        if (maxFuel == 0) maxFuel = 99;
        if (fuel == 0) fuel = maxFuel;

        // 反击系数
        if (counterMul == 0) counterMul = 0.5f;

        // === 从 UnitRegistry 读取配置（优先）===
        ApplyUnitRegistryConfig();
        // === 子类差异化配置入口（覆盖 UnitRegistry）===
        ApplyUnitSpecificDefaults();
        // 修复：子类可能修改了 defaultMovePoints，需要同步到 movePoints
        if (movePoints == 0) movePoints = defaultMovePoints;
    }

    // 初始化攻防表（无论 useDefaultConfig 如何都需要，用于查询）
    InitializeDefaultMatrix();

    // ✅ 记录视觉组件原始 scale（必须在 StartBreath 之前！）
    if (baseAnimSprite != null && IsInstanceValid(baseAnimSprite))
        _originalVisualScale = baseAnimSprite.Scale;
    else if (baseSprite != null && IsInstanceValid(baseSprite))
        _originalVisualScale = baseSprite.Scale;
    else if (animSprite != null && IsInstanceValid(animSprite))
        _originalVisualScale = animSprite.Scale;
    else if (sprite != null && IsInstanceValid(sprite))
        _originalVisualScale = sprite.Scale;

    UpdateTeamVisual();
    UpdateHpLabel();
    // ✅ ShowAnimState / StartBreath 已统一在 UpdateTeamVisual() 中处理
    //    避免 team 改变后重复调用或状态不同步

        // ========== ✅ 战争迷雾视野初始化 ==========
        if (visionRange < 0 && useConfigVision)
        {
            visionRange = VisionConfig.GetUnitVisionRange(UnitTypeId, TemplateId);
        }
        else if (visionRange >= 0)
        {
        }

        // ✅ 初始化单位专属地形加成矩阵（如果为空则填充默认值）
        if (overrideGlobalTerrainBonus && (unitTerrainVisionBonus == null || unitTerrainVisionBonus.Count == 0))
        {
            var defaultTable = VisionConfig.GetUnitTerrainBonusTable(TemplateId);
            unitTerrainVisionBonus = new Godot.Collections.Dictionary<GridType, int>();
            foreach (var kvp in defaultTable)
                unitTerrainVisionBonus[kvp.Key] = kvp.Value;
        }

        // ✅ P-1 单位默认开启视野共享（如果team是Player且未手动设置）
        if (team == TeamHelper.Player && !isPublicVisionUnit)
        {
            isPublicVisionUnit = true;
        }

}

// ========== 子类覆盖：单位特有默认值 ==========
public virtual void ApplyUnitSpecificDefaults() { }

// ✅ 从 UnitRegistry 读取兵种配置
protected virtual void ApplyUnitRegistryConfig()
{
    string unitId = UnitTypeId;
    var config = UnitRegistry.Get(unitId) ?? UnitRegistry.Get(TemplateId);   // 自定义兵种无专属配置时用参照兵种
    if (config == null) return;

    // 基础属性
    if (config.MoveType != default) moveType = config.MoveType;
    if (config.MovePoints > 0) defaultMovePoints = config.MovePoints;
    // cost 不再由 UnitRegistry 覆盖：造价一律以检查器为准（勾选 useDefaultConfig 也一样）
    if (config.CanMerge) canMerge = true;
    if (!string.IsNullOrEmpty(config.DisplayName) && string.IsNullOrEmpty(Name))
        Name = config.DisplayName;

    // 武器 - 主
    hasPrimaryWeapon = config.HasPrimaryWeapon;
    if (config.HasPrimaryWeapon)
    {
        maxPrimaryAmmo = config.MaxPrimaryAmmo;
        primaryHasLimitedAmmo = config.PrimaryHasLimitedAmmo;
        if (currentPrimaryAmmo <= 0) currentPrimaryAmmo = maxPrimaryAmmo;
        primaryAntiArmor = config.PrimaryAntiArmor;
        primaryAntiInfantry = config.PrimaryAntiInfantry;
    }
    if (config.BaseAttack > 0) baseAttack = config.BaseAttack;

    // 武器 - 副
    hasSecondaryWeapon = config.HasSecondaryWeapon;
    if (config.HasSecondaryWeapon && config.SecondaryAttack > 0)
        secondaryAttack = config.SecondaryAttack;
    secondaryAntiArmor = config.SecondaryAntiArmor;
    secondaryAntiInfantry = config.SecondaryAntiInfantry;

    // 射程
    if (config.UseMinMaxAttackRange)
    {
        useMinMaxAttackRange = true;
        minAttackRange = config.MinAttackRange;
        maxAttackRange = config.MaxAttackRange;
        attackRange = maxAttackRange;
    }
    canAttackAfterMoving = config.CanAttackAfterMoving;
    cannotCounterWhenAttacked = config.CannotCounterWhenAttacked;
    canCounterWhenDefending = config.CanCounterWhenDefending;
    canCounterAtRange = config.CanCounterAtRange;

    // 燃料
    if (config.MaxFuel != 99) maxFuel = config.MaxFuel;
    consumeFuel = config.ConsumeFuel;
    if (config.DailyFuelConsumption > 0) dailyFuelConsumption = config.DailyFuelConsumption;
    destroyOnOutOfFuel = config.DestroyOnOutOfFuel;
    if (config.LowFuelThreshold != 15) lowFuelThreshold = config.LowFuelThreshold;
    if (config.LowAmmoThreshold >= 0) lowAmmoThreshold = config.LowAmmoThreshold;

    // 防御
    if (config.BaseDefense > 0) baseDefense = config.BaseDefense;
    if (config.CounterMul != 0.5f) counterMul = config.CounterMul;

    // 占领
    captureAbility = config.CaptureAbility;
    if (config.CapturePower > 0) capturePower = config.CapturePower;

    // 特殊能力
    canDevour = config.CanDevour;
    if (config.CanIlluminate) canIlluminate = true;

    // 重叠规则
    overlapType = config.OverlapType;
    attackType = config.AttackType;
    isArmoredUnit = config.IsArmoredUnit;
    IsAirUnit = config.IsAirUnit;

    // 搭载
    if (config.CanTransportUnits)
    {
        canTransportUnits = true;
        canSupplyUnits = config.CanSupplyUnits;
        maxTransportCapacity = config.MaxTransportCapacity;
        if (config.TransportUnitTypes != null && config.TransportUnitTypes.Length > 0)
            canTransportUnitTypes = new Godot.Collections.Array<string>(config.TransportUnitTypes);
    }

    // 视野
    if (config.VisionRange >= 0) visionRange = config.VisionRange;
}

public override void _Process(double delta)
{
    // ✅ 力场支援模块：持续锁值
    if (forceSupportEnabled) TickForceSupport(delta);

    // ✅ 广义受伤现形
    if (damageRevealLastHp < 0) damageRevealLastHp = health;
    else if (health < damageRevealLastHp)
    {
        if (stealthRevealDamageScope == 1 && isStealthed && health > 0)
        {
            ApplyStealthRevealOnDamage(this);
            CheckStealthThresholds();
        }
        damageRevealLastHp = health;
    }
    else if (health > damageRevealLastHp) damageRevealLastHp = health;

    // ✅ 移动后自动修理：移动过且本回合行动已结束时触发一次（不占操作；撤销移动不会刷血）
    if (autoRepairAfterMove && movedThisTurnForAutoRepair && !autoRepairAfterMoveDone && state == UnitState.Acted && CanPerformRepair)
    {
        autoRepairAfterMoveDone = true;
        PerformAutoRepair();
    }

    // ✅ 隐身半透明与状态同步（修复：读档/编辑器载入 isStealthed=true 时不会重设半透明）
    if (isStealthed != stealthVisualApplied)
    {
        stealthVisualApplied = isStealthed;
        Modulate = isStealthed ? new Color(1, 1, 1, 0.45f) : new Color(1, 1, 1,1);   // ✅ 我方视角=半透明幽灵；敌方视角=完全不渲染（ShouldShowUnit）
    }
    // ✅ noFuelIcon 闪烁（从子类抽到基类统一处理）
    if (noFuelIcon != null)
    {
        if (!consumeFuel)
            noFuelIcon.Visible = false;
        else if (fuel <= 0)
            noFuelIcon.Visible = true;
        else if (fuel <= lowFuelThreshold)
            noFuelIcon.Visible = (Time.GetTicksMsec() / 500) % 2 == 0;
        else
            noFuelIcon.Visible = false;
    }


    // noAmmoIcon 闪烁
    if (noAmmoIcon != null)
    {
        // ✅ 修复：无限弹药的单位（弹药上限99或无弹药限制）永远不显示缺弹药图标
        if (!hasPrimaryWeapon || HasInfinitePrimaryAmmo)
            noAmmoIcon.Visible = false;
        else if (!CanUsePrimaryWeapon())
            noAmmoIcon.Visible = true;
        else if (currentPrimaryAmmo <= (lowAmmoThreshold >= 0 ? lowAmmoThreshold : Math.Max(1, maxPrimaryAmmo / 3)))
            noAmmoIcon.Visible = (Time.GetTicksMsec() / 500) % 2 == 0;
        else
            noAmmoIcon.Visible = false;
    }
}
    [Export] public double campaignKillScoreValue = 0;      // ✅ 开创人2026-11-14 接缝③:击杀积分价值(0=不记账)
    [Export] public double campaignKillCrystalValue = 0;    // ✅ 宝钞价值
    public virtual void OnDestroyed()
    {
        {   // ✅ 开创人2026-11-14 接缝③:死亡入口→战役结算台账
            var __st = GetTree()?.Root?.FindChild("CampaignSettlement", true, false) as CampaignSettlement;
            __st?.RecordKill(campaignKillScoreValue, campaignKillCrystalValue, lastAttackerTeam);
        }
        // ✅ CO 模块28（多屏 CO 死亡）：CO 单位阵亡——全图（含多屏）无残存 isCoUnit 则 CO 死亡
        if (isCoUnit)
            CoModuleSystem.OnCoUnitDestroyed(this);

        // 如果正在占领中，清理占领状态
        if (currentCaptureProgress > 0 && capturingGrid != null)
        {
            // 通知格子占领中断
            capturingGrid.city?.OnCaptureInterrupted(this);
            ShowCaptureInterruptedEffect();
            // 清理自己的占领状态
            currentCaptureProgress = 0;
            capturingGrid = null;
        }
        
        // 从格子中移除
        if (grid != null)
        {
            grid.infantries.Remove(this);
            if (grid.infantry == this)
            {
                grid.infantry = grid.infantries.Count > 0 ? grid.infantries[0] : null;
            }
            grid = null;
        }
    }

    // ==================== 雾战模块助手 ====================
    private GameManager _fogGmCache;
    /// <summary>GameManager 缓存访问器（逃跑/拆旗/设施压制/群体狂热等模块共用）</summary>
    private GameManager gameManager
    {
        get
        {
            if (_fogGmCache == null || !GodotObject.IsInstanceValid(_fogGmCache))
                _fogGmCache = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            return _fogGmCache;
        }
    }

    public virtual void EnsureGridBound()
    {
        if (grid != null && IsInstanceValid(grid))
        {
            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm?.unitManager != null)
            {
                var expectedPos = gm.unitManager.GridToWorld(grid.GridIndex);
                if (Position.DistanceTo(expectedPos) > 1) Position = expectedPos;
            }
            return;
        }
        var gameManager = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gameManager?.unitManager != null)
        {
            bool bound = gameManager.unitManager.BindUnitToGrid(this, true);
            if (!bound || grid == null)
            {
                var gridPos = gameManager.unitManager.WorldToGrid(Position);
                if (gameManager.unitManager.IsValidGrid(gridPos))
                {
                    var targetGrid = gameManager.gridManager.map[gridPos.X, gridPos.Y];
                    if (targetGrid != null)
                    {
                        grid = targetGrid;
                        if (!targetGrid.infantries.Contains(this)) targetGrid.infantries.Add(this);
                        if (targetGrid.infantry == null) targetGrid.infantry = this;
                        Position = targetGrid.Position;
                    }
                }
            }
        }
    }


}
    
