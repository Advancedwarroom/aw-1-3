
using Godot;

public partial class AntiAir : Infantry
{
    [Export] public AnimatedSprite2D animSprite;
    [Export] public Sprite2D idleSprite;
    public override void _Ready()
    {
        base._Ready();

        if (animSprite == null)
            animSprite = GetNodeOrNull<AnimatedSprite2D>("animSprite");
        if (animSprite == null)
            animSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");

        if (idleSprite == null)
            idleSprite = GetNodeOrNull<Sprite2D>("Sprite2D");

        string animName = GetIdleAnimName();
        if (animSprite != null && animSprite.SpriteFrames?.HasAnimation(animName) == true)
            animSprite.Play(animName);
    }
    protected override string GetIdleAnimName() => team == "Player2" ? "antiair2" : "antiair1";


        public void OnTurnEnd()
    {
        originalGrid = null;
        movePoints = defaultMovePoints;
        isMoved = false;
        isAttacked = false;
        state = UnitState.Idle;

        string animName = GetIdleAnimName();
        if (animSprite != null && animSprite.SpriteFrames?.HasAnimation(animName) == true)
        {
            animSprite.Modulate = normal;
            animSprite.Play(animName);
        }
        StartBreath();
    }

    // === 自动获取 animSprite ===

    public override void OnWaitSelected()
    {
        isMoved = true;
        isAttacked = true;
        state = UnitState.Acted;
        originalGrid = null;

        if (animSprite != null)
        {
            animSprite.Stop();
            animSprite.Frame = 0;
            animSprite.Modulate = dim;
        }
        if (idleSprite != null)
            idleSprite.Hide();

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.ClearSelectedInfantry();
    }

    public override void ApplyUnitSpecificDefaults()
    {
        if (!useDefaultConfig) return; // ✅ Inspector自定义模式：不执行硬编码默认值
        
        overlapType = UnitOverlapType.NonOverlapping;
        attackType = AttackType.CanAttack;
        moveType = MoveType.Treads;      

        defaultMovePoints = 6;


        baseDefense = 0;                 
        counterMul = 0.5f;

        maxFuel = 60;
        consumeFuel = true;


        hasPrimaryWeapon = true;
        primaryHasLimitedAmmo = true;
        maxPrimaryAmmo = 9;
        currentPrimaryAmmo = 9;
        primaryAntiArmor = true;       
        primaryAntiInfantry = true;       


        hasSecondaryWeapon = false;

        attackRange = 1;
        minAttackRange = 1;
        maxAttackRange = 1;
        canAttackAfterMoving = true;     

        cost = 8000;  // AntiAir造价

    }


}
