
using Godot;

public partial class MdTank : Infantry
{
    // === 场景节点引用 ===
    [Export] public AnimatedSprite2D MdSprite;

    public override void ApplyUnitSpecificDefaults()
    {
        if (!useDefaultConfig) return; // ✅ Inspector自定义模式：不执行硬编码默认值
        // === 核心定位 ===
        overlapType = UnitOverlapType.NonOverlapping;
        attackType = AttackType.CanAttack;
        moveType = MoveType.Treads;


        defaultMovePoints = 5;

        // === 防御 ===
        baseDefense = 0;          
        counterMul = 0.5f;

        // === 燃料 ===
        maxFuel = 50;
        consumeFuel = true;

        // === 武器配置 ===
        if (!hasPrimaryWeapon && !hasSecondaryWeapon)
        {
            // 主武器：中型加农炮，8发，对轻甲毁灭性
            hasPrimaryWeapon = true;
            primaryHasLimitedAmmo = true;
            maxPrimaryAmmo = 8;
            currentPrimaryAmmo = 8;
            primaryAntiArmor = true;
            primaryAntiInfantry = true;

            // 副武器：机枪，对步兵够用，对装甲无效
            hasSecondaryWeapon = true;
            secondaryAntiInfantry = true;
            secondaryAntiArmor = false;
        }

        // === 射程：近程 ===
        attackRange = 1;
        minAttackRange = 1;
        maxAttackRange = 1;
        canAttackAfterMoving = true;

        // === 不能占领/搭载 ===
        captureAbility = CaptureAbility.CannotCapture;
        canTransportUnits = false;
        maxTransportCapacity = 0;

        cost = 16000;  // MdTank造价
        canTransportUnits = false;
        maxTransportCapacity = 0;

    }

    // === 动画名映射 ===
    protected override string GetIdleAnimName() => team == "Player2" ? "Md2" : "Md1";

    // === 回合结束恢复 ===
    public void OnTurnEnd()
    {
        originalGrid = null;
        movePoints = defaultMovePoints;
        isMoved = false;
        isAttacked = false;
        state = UnitState.Idle;

        string animName = GetIdleAnimName();
        if (MdSprite != null && MdSprite.SpriteFrames?.HasAnimation(animName) == true)
        {
            MdSprite.Modulate = normal;
            MdSprite.Play(animName);
        }
        StartBreath();
    }

    public override void OnWaitSelected()
    {
        isMoved = true;
        isAttacked = true;
        state = UnitState.Acted;
        originalGrid = null;

        if (MdSprite != null)
        {
            MdSprite.Stop();
            MdSprite.Frame = 0;
            MdSprite.Modulate = dim;
        }

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.ClearSelectedInfantry();
    }

    // === 自动获取 MdSprite ===
    public override void _Ready()
    {
        base._Ready();

        if (MdSprite == null)
            MdSprite = GetNodeOrNull<AnimatedSprite2D>("MdSprite");
        if (MdSprite == null)
            MdSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");

        string animName = GetIdleAnimName();
        if (MdSprite != null && MdSprite.SpriteFrames?.HasAnimation(animName) == true)
            MdSprite.Play(animName);
    }
}
