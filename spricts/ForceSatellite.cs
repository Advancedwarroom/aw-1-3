// ForceSatellite.cs - 力场卫星（Black Onyx 黑玛瑙，AW3 Crystal Calamity）
// 英文名 Black Onyx 保留，中文名"力场卫星"。宇宙战舰移动方式（SpaceShiper，全地形=1）
// 无武器；只能被攻击卫星击伤（55%）。HP = 导弹井固定伤害×9（原作九次击落：9×30=270）
using Godot;

public partial class ForceSatellite : Infantry
{
    public override string GetIdleAnimName() => team == "Player2" ? "ForceSatellite2" : "ForceSatellite1";

    public override void _Ready()
    {
        base._Ready();
        // 力场卫星：默认开启力场支援（锁血），仅友军，只选兵器，覆盖弱点格
        if (!forceSupportEnabled)
        {
            forceSupportEnabled = true;
            forceSupportToUnits = false;
            forceSupportToWeapons = true;
            forceSupportLockHP = true;
        }
    }
}
