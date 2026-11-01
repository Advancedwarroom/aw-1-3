// AttackSatellite.cs - 攻击卫星（AW3 Crystal Calamity 反卫星攻击卫星）
// 宇宙战舰移动方式（SpaceShiper）。两个弹药：普通弹药专门打同类65%和力场卫星55%
// 天基打击固定50%。与力场卫星最大血量相同（270=导弹井伤害30×9）
using Godot;

public partial class AttackSatellite : Infantry
{
    public override string GetIdleAnimName() => team == "Player2" ? "AttackSatellite2" : "AttackSatellite1";

    public override void _Ready()
    {
        base._Ready();
        // 攻击卫星：默认开启天基打击模块（固定50%）
        if (!spaceStrikeEnabled)
        {
            spaceStrikeEnabled = true;
            spaceStrikeDamagePercent = 0.5f;
            spaceStrikeTargetPolicy = 1; // 仅敌军
        }
    }
}
