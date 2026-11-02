using Godot;

// 力场支援·锁标志（Infantry）：由 ForceSatellite.Support 模块写入，各消费/伤害/维修入口检查
public partial class Infantry
{
    [Export] public bool healthLocked = false;        // 锁血：锁死当前值（伤害与维修均无效）
    [Export] public bool fuelLocked = false;          // 锁燃油
    [Export] public bool primaryAmmoLocked = false;   // 锁主弹药
}
