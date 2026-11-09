// PipeSeam.cs - 管道接缝（Black Hole 管道动脉唯一可攻击部位）
// 原作设定：防御力≈中型坦克（受击伤害×0.45）；99 HP 惯例；被毁后地形变 BROKENPIPE（瓦砾平原）。
// 联动：管道接缝被毁时通知所有联动方（超级兵工厂供能线断裂→中立废铁）。
using Godot;

public partial class PipeSeam : Weapon
{
    // ---------- 受击防御：≈中型坦克 ----------
    public override void TakeDamage(int damage, Infantry attacker = null)
    {
        // 原作：管道缝防御力≈中型坦克 → 实受伤害×0.45（向下取整保1）
        int actual = Mathf.Max(1, Mathf.RoundToInt(damage * 0.45f));
        base.TakeDamage(actual);
    }

    public override string GetIdleAnimName() => "PipeSeam";
    public override string GetBrokenAnimName() => "PipeSeamBroken";
    public override bool CanAttack() => false;   // 接缝本身不攻击

    public override void _Ready()
    {
        base._Ready();
    }

    /// <summary>管道构造者建造的接缝兵器：无队伍、不变色（保持原色调）</summary>
    protected override void ApplyPaletteMaterial()
    {
        // 不应用任何势力调色 → 原色调
    }

    // 被毁时通知联动方（供能线断裂）
    public override void OnDestroyed()
    {
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        // 先通知联动系统（超级兵工厂会变中立），再走通用销毁
        gm?.NotifyLinkageSourcesDestroyed(this);
        base.OnDestroyed();
    }
}
