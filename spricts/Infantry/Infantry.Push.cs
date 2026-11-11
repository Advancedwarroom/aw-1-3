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
        Everyone
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
    [Export] public int pushMaxTargetRange = 1;
    [Export] public bool pushCanTargetEnemies = true;
    [Export] public bool pushCanTargetFriendlies = false;
    [Export] public bool pushCanTargetAllies = false;

    [ExportSubgroup("方向与目标")]
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
}
