// GrandBoltPart.cs - 拆分版大雷弹（AWDS 大雷弹的弱点拆成独立防御节点）
// 用法：放置3个节点，全毁=胜利（victoryCountRequired=3 对齐节点数）
using Godot;

public partial class GrandBoltPart : Weapon
{
    [ExportGroup("拆分大雷弹模块")]
    [Export] public bool partEnabled = true;
    [Export] public int partMaxHealth = 999;        // 节点血量（千分制）

    public override void _Ready()
    {
        maxHealth = partMaxHealth; health = partMaxHealth;
        contributesToVictory = true; victoryCountRequired = 3;   // 3节点全毁=胜利
        base._Ready();
    }
}
