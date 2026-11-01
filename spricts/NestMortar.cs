// NestMortar.cs - 巢穴·迫击炮
using Godot;

public partial class NestMortar : Weapon
{
    [ExportGroup("巢穴·迫击炮模块")]
    [Export] public bool moduleEnabled = true;
    [Export] public int partMaxHealth = 999999;        // 血量（千分制）

    public override void _Ready()
    {
        maxHealth = partMaxHealth; health = partMaxHealth;
        canRotate = false;
        contributesToVictory = false;
        // 原作：迫击炮不可摧毁；弹道=导弹发射井式（左右各3格+整列直轰到底），5HP，砸最贵单位集群；
        // 双炮每日交替+代打规则（Export 配置射程/弹药/目标模式）
        base._Ready();
    }
}
