using Godot;
using System;
using System.Collections.Generic;

public partial class Infantry
{
    public enum PushTriggerMode
    {
        Disabled,
        Attack,
        SeparateAction,
        Movement
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
        OppositeSides,
        FreeAngle
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
        PrimaryAmmo,
        Ammo
    }

    public enum PushChainWeightMode
    {
        Constant,
        Accumulate,
        Decrease
    }

    public enum PushFragileDamageMode
    {
        Fixed,
        AttackCalculation,
        HealthAndFunds
    }

    [ExportGroup("推动模块")]
    [Export] public bool pushEnabled = false;
    [Export] public PushTriggerMode pushTriggerMode = PushTriggerMode.Disabled;
    [Export] public bool pushRequiresPrimaryWeapon = false;
    [Export(PropertyHint.Range, "0,9999,0.01")] public float weight = 1.0f;
    [Export] public bool weightScalesWithHealth = false;
    [Export] public EqualWeightPushMode equalWeightPushMode = EqualWeightPushMode.Stop;

    [ExportSubgroup("独立推动操作")]
    [Export] public bool pushActionCountsAsAction = true;
    [Export] public bool pushCanBeUsedAfterMoving = true;
    [Export] public bool pushOncePerTurn = true;

    [ExportSubgroup("基础推动参数")]
    [Export] public int pushDistance = 1; // maximum displacement distance
    [Export] public int pushMinDistance = 1;
    [Export] public bool pushAutoDistance = true;
    [Export] public int pushMinTargetRange = 1;
    [Export] public int pushRange = 1;
    [Export] public bool pushCanTargetEnemies = true;
    [Export] public bool pushCanTargetFriendlies = false;
    [Export] public bool pushCanTargetAllies = false;
    [Export] public PushTargetTeamMode pushTargetPolicy = PushTargetTeamMode.Custom;
    [Export] public string[] pushTargetWhitelist = System.Array.Empty<string>();
    [Export] public string[] pushTargetBlacklist = System.Array.Empty<string>();
    [Export] public Godot.Collections.Array<PushChainRule> pushChainRules = new();

    [ExportSubgroup("方向与目标")]
    [Export] public bool pushChainEnabled = true;
    [Export] public bool pushUsesWeight = true;
    [Export] public int pushRamDamage = 0;
    [Export] public PushDirectionMode pushDirectionMode = PushDirectionMode.AwayFromPusher;
    [Export] public Vector2I fixedPushDirection = Vector2I.Right;
    [Export(PropertyHint.Range, "0,360,1")] public float pushFreeAngleDegrees = 0.0f;
    [Export] public bool pushCanTargetInfantry = true;
    [Export] public bool pushCanTargetWeapons = false;
    [Export] public bool pushCanTargetFacilities = false;
    [Export] public int pushMaxChainStacks = 0; // 0 = unlimited
    [Export] public int pushMaxChainUnits = 0; // 0 = unlimited; co-located stacks are never split
    [Export] public PushChainWeightMode pushChainWeightMode = PushChainWeightMode.Constant;
    [Export] public float pushChainMaxAccumulatedWeight = 0.0f; // 0 = unlimited

    [ExportSubgroup("推力与资源")]
    [Export] public bool pushForceScalesWithDistance = false;
    [Export] public float pushForcePerTile = 1.0f;
    [Export] public PushResourceType pushCostResource = PushResourceType.None;
    [Export] public int pushCostAmount = 0;
    [Export] public AmmoKind pushCostAmmoKind = AmmoKind.Primary;
    [Export] public int pushCooldownTurns = 0;

    [ExportSubgroup("目标状态效果")]
    [Export] public int pushTargetDamage = 0;
    [Export] public int pushTargetFundsDelta = 0;
    [Export] public bool pushTargetFundsCanOverMax = false;
    [Export] public bool pushDamageCanDestroy = false;
    [Export] public bool pushLockTargetToOneHp = false;
    [Export] public int pushTargetFuelDelta = 0;
    [Export] public bool pushTargetFuelCanOverMax = false;
    [Export] public int pushTargetAmmoDelta = 0;
    [Export] public AmmoKind pushTargetAmmoKind = AmmoKind.Primary;
    [Export] public bool pushTargetAmmoCanOverMax = false;
    [Export] public int pushTargetHealAmount = 0;
    [Export] public bool pushTargetCanOverMaxHealth = false;
    [Export] public int pushTargetMaxHealthDelta = 0;

    [ExportSubgroup("脆弱模块")]
    [Export] public bool pushFragileEnabled = false;
    [Export] public PushFragileDamageMode pushFragileDamageMode = PushFragileDamageMode.Fixed;
    [Export] public int pushFragileDamage = 0;
    [Export] public float pushFragileHealthScale = 0.1f;
    [Export] public int pushFragileFundsPerDamage = 1000;
    [Export] public bool pushFragileCanDestroy = false;
    [Export] public bool pushFragileLockOneHp = false;
    [Export] public bool pushFragileCanOverMaxHealth = false;
    [Export] public int pushFragileFuelDelta = 0;
    [Export] public bool pushFragileFuelCanOverMax = false;
    [Export] public int pushFragileAmmoDelta = 0;
    [Export] public bool pushFragileAmmoCanOverMax = false;
    [Export] public AmmoKind pushFragileAmmoKind = AmmoKind.Primary;

    [ExportSubgroup("反弹模块")]
    [Export] public bool pushReboundEnabled = false;
    [Export] public bool pushReboundCanTargetEnemies = true;
    [Export] public bool pushReboundCanTargetFriendlies = false;
    [Export] public bool pushReboundCanTargetAllies = false;
    [Export] public int pushReboundMinRange = 1;
    [Export] public int pushReboundRange = 99;
    [Export] public bool pushReboundToOrigin = true;
    [Export] public int pushReboundAmmoCost = 0;
    [Export] public AmmoKind pushReboundAmmoKind = AmmoKind.PushRebound;
    [Export] public int pushReboundHealthCost = 0;
    [Export] public int pushReboundFuelCost = 0;
    [Export] public int pushReboundFundsCost = 0;
    [Export] public int pushReboundTargetDamage = 0;
    [Export] public bool pushReboundTargetCanDestroy = false;
    [Export] public bool pushReboundTargetLockOneHp = false;
    [Export] public int pushReboundTargetFuelDelta = 0;
    [Export] public int pushReboundTargetAmmoDelta = 0;
    [Export] public AmmoKind pushReboundTargetAmmoKind = AmmoKind.Primary;

    [ExportSubgroup("缓冲模块")]
    [Export] public bool pushMitigationEnabled = false;
    [Export] public bool pushMitigationProtectHealth = false;
    [Export] public bool pushMitigationProtectFunds = false;
    [Export] public bool pushMitigationProtectFuel = false;
    [Export] public bool pushMitigationProtectAmmo = false;
    [Export] public AmmoKind pushMitigationProtectAmmoKind = AmmoKind.Primary;
    [Export] public int pushMitigationResourceFixedReduction = 0;
    [Export(PropertyHint.Range, "0,100,0.1")] public float pushMitigationResourcePercentReduction = 0f;
    [Export] public AmmoKind pushMitigationAmmoKind = AmmoKind.PushMitigation;
    [Export] public int pushMitigationAmmoCost = 0;
    [Export] public int pushMitigationTiles = 0;
    [Export(PropertyHint.Range, "0,100,1")] public float pushMitigationPercent = 0f;

    [ExportSubgroup("反重力转移模块")]
    [Export] public bool pushTransferEnabled = false;
    [Export] public Vector2I pushTransferDirection = Vector2I.Zero;
    [Export] public int pushTransferAmmoCost = 0;
    [Export] public AmmoKind pushTransferAmmoKind = AmmoKind.AntiGravityTransfer;
    [Export] public int pushTransferFuelCost = 0;
    [Export] public int pushTransferHealthCost = 0;
    [Export] public int pushTransferFundsCost = 0;

    [ExportSubgroup("推动者行为")]
    [Export] public bool pushPusherFollowsChain = false;
    [Export] public bool pushSelfDestructOnInvalidIntermediateTile = true;
    [Export] public bool pushDestroyOnProperty = false; // Destroy displaced units on property tiles (Rhino-style).

    // Persisted turn limiter: ExtraSave serializes public Export fields, so a save/load
    // cannot accidentally restore a push that was already spent this turn.
    [ExportSubgroup("运行时状态（自动保存）")]
    [Export] public bool pushRuntimeHasUsedThisTurn = false;
    [Export] public int pushRuntimeCooldownTurnsRemaining = 0;
    private readonly HashSet<Infantry> mitigationAmmoPaidThisAction = new();

    public float GetEffectiveWeight()
    {
        float baseWeight = Mathf.Clamp(weight, 0.0f, 9999.0f);
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
        if (pushRuntimeCooldownTurnsRemaining > 0) return false;
        if (pushOncePerTurn && pushRuntimeHasUsedThisTurn)
            return false;
        if (!pushCanBeUsedAfterMoving && isMoved)
            return false;
        if (pushActionCountsAsAction && (hasActed || state == UnitState.Acted))
            return false;
        return true;
    }

    public bool CanUseMovementPush()
    {
        if (!pushEnabled || pushTriggerMode != PushTriggerMode.Movement)
            return false;
        if (pushRuntimeCooldownTurnsRemaining > 0) return false;
        if (pushOncePerTurn && pushRuntimeHasUsedThisTurn) return false;
        return true;
    }

    public bool TryMarkMovementPushUsed()
    {
        if (!CanUseMovementPush()) return false;
        MarkPushUsed();
        return true;
    }

    public bool CanUseAttackPush()
    {
        if (!pushEnabled || pushTriggerMode != PushTriggerMode.Attack)
            return false;
        if (pushRuntimeCooldownTurnsRemaining > 0) return false;
        if (pushOncePerTurn && pushRuntimeHasUsedThisTurn) return false;
        return true;
    }

    private void MarkPushUsed()
    {
        pushRuntimeHasUsedThisTurn = true;
        pushRuntimeCooldownTurnsRemaining = Mathf.Max(pushRuntimeCooldownTurnsRemaining, pushCooldownTurns);
    }

    public bool TryMarkPushActionUsed()
    {
        if (!CanUseSeparatePushAction())
            return false;
        MarkPushUsed();
        if (pushActionCountsAsAction)
        {
            hasActed = true;
            state = UnitState.Acted;
            SetWaitVisual(true);
        }
        return true;
    }

    public bool TryMarkAttackPushUsed()
    {
        if (!CanUseAttackPush())
            return false;
        MarkPushUsed();
        return true;
    }

    public void ResetPushUseForTurn()
    {
        pushRuntimeHasUsedThisTurn = false;
        if (pushRuntimeCooldownTurnsRemaining > 0) pushRuntimeCooldownTurnsRemaining--;
    }
}
