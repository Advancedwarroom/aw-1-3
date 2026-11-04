using Godot;

// 力场支援·锁标志（Infantry）：由 ForceSatellite.Support 模块写入，各消费/伤害/维修入口检查
public partial class Infantry
{
    [Export] public bool healthLocked = false;        // 锁血：锁死当前值（伤害与维修均无效）
    [Export] public bool fuelLocked = false;          // 锁燃油
    [Export] public bool primaryAmmoLocked = false;   // 锁主弹药

    // 力场支援模块每回合入口（GameManager 新回合循环调用；默认空实现，ForceSatellite 覆写）
    public virtual void ProcessTurnSupport() { TickForceFieldTurns(); }   // ✅ 通用：力场计数+永久化推进
}
