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

    // Runtime-only turn limiter. It is reset by GameManager when this unit's turn refreshes.
    private bool hasUsedPushThisTurn = false;

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
    }
}
