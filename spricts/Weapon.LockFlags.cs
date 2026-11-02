using Godot;

// 力场支援·锁标志（Weapon 8弹药系统）：每种弹药独立锁
public partial class Weapon
{
    [Export] public bool healthLocked = false;             // 锁血（兵器）
    [Export] public Godot.Collections.Array<bool> ammoLocked = new();      // 按弹药类型0-7独立锁

    public bool AmmoTypeLocked(int t) => ammoLocked != null && t >= 0 && t < ammoLocked.Count && ammoLocked[t];
}
