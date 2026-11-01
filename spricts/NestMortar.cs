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
        // 原作：迫击炮不可摧毁、集群落点（Export 里配置射程/弹药/伤害后生效）
        base._Ready();
    }
}
