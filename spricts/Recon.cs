using Godot;

public partial class Recon : Infantry
{
    [Export] public AnimatedSprite2D animSprite;
    [Export] public Sprite2D idleSprite;

    private bool _wasActed = false;

    public override void _Ready()
    {
        base._Ready();

        if (lowFuelThreshold == 15) lowFuelThreshold = 30; // ✅ 低油闪烁阈值

        if (animSprite == null)
            animSprite = GetNodeOrNull<AnimatedSprite2D>("Base2D");
        if (animSprite == null)
            animSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");

        if (idleSprite == null)
            idleSprite = GetNodeOrNull<Sprite2D>("BaseSprite");

        string animName = GetIdleAnimName();
        if (animSprite != null && animSprite.SpriteFrames?.HasAnimation(animName) == true)
            animSprite.Play(animName);
    }

    public override void _Process(double delta)
    {
        bool nowActed = isAttacked || state == UnitState.Acted;
        if (nowActed && !_wasActed)
            FreezeToIdleDark();
        else if (!nowActed && _wasActed)
            RestoreAnimAndBreath();
        _wasActed = nowActed;

        // 燃料图标
        var icon = GetNodeOrNull<AnimatedSprite2D>("NoFuelIcno");
        if (icon != null)
        {
            if (!consumeFuel)
                icon.Visible = false;
            else if (fuel <= 0)
                icon.Visible = true;
            else if (fuel <= lowFuelThreshold)
                icon.Visible = (Time.GetTicksMsec() / 500) % 2 == 0;
            else
                icon.Visible = false;
        }

        // 弹药图标
        if (noAmmoIcon != null)
        {
            if (!hasPrimaryWeapon)
                noAmmoIcon.Visible = false;
            else if (!CanUsePrimaryWeapon())
                noAmmoIcon.Visible = true;
            else if (currentPrimaryAmmo <= 3 && currentPrimaryAmmo > 0)
                noAmmoIcon.Visible = (Time.GetTicksMsec() / 500) % 2 == 0;
            else
                noAmmoIcon.Visible = false;
        }
    }

    protected override string GetIdleAnimName() => team == "Player2" ? "Recon2" : "Recon1";

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

    public override void OnWaitSelected()
    {
        isMoved = true;
        isAttacked = true;
        state = UnitState.Acted;
        originalGrid = null;

        FreezeToIdleDark();

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.ClearSelectedInfantry();
    }

    public override int CalculateDamageAgainstWeapon(Weapon target, WeaponType weaponType)
    {
        float healthPercent = (float)health / maxHealth;
        return Mathf.Max(1, Mathf.RoundToInt(35 * healthPercent));
    }

    private void FreezeToIdleDark()
    {
        breathTween?.Kill();
        if (animSprite != null)
        {
            animSprite.Stop();
            animSprite.Frame = 0;
            animSprite.Modulate = dim;
            animSprite.Show();
        }
        if (idleSprite != null && idleSprite.Texture != null)
        {
            idleSprite.Show();
            idleSprite.Modulate = dim;
            if (animSprite != null)
                animSprite.Hide();
        }
    }

    private void RestoreAnimAndBreath()
    {
        string animName = GetIdleAnimName();
        if (animSprite != null && animSprite.SpriteFrames?.HasAnimation(animName) == true)
        {
            animSprite.Modulate = normal;
            animSprite.Show();
            animSprite.Play(animName);
        }
        if (idleSprite != null)
            idleSprite.Hide();
        StartBreath();
    }

    public override void ApplyUnitSpecificDefaults()
    {
        if (!useDefaultConfig) return; // ✅ Inspector自定义模式：不执行硬编码默认值
        overlapType = UnitOverlapType.NonOverlapping;
        attackType = AttackType.CanAttack;
        moveType = MoveType.Tire;
        defaultMovePoints = 8;
        movePoints = 8;
        baseDefense = 0;
        counterMul = 0.5f;
        maxFuel = 80;
        consumeFuel = true;
        hasPrimaryWeapon = false;
        hasSecondaryWeapon = true;
        secondaryAttack = 100;
        attackRange = 1;
        minAttackRange = 1;
        maxAttackRange = 1;
        canAttackAfterMoving = true;
        captureAbility = CaptureAbility.CannotCapture;

        cost = 4000;  // Recon造价
    }
}
