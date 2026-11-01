// NestCannonFixed.cs - 巢穴·固定炮
using Godot;

public partial class NestCannonFixed : Weapon
{
    [ExportGroup("巢穴·固定炮模块")]
    [Export] public bool moduleEnabled = true;
    [Export] public int partMaxHealth = 800;        // 血量（千分制）

    public override void _Ready()
    {
        maxHealth = partMaxHealth; health = partMaxHealth;
        canRotate = false;
        contributesToVictory = true; victoryCountRequired = 5;
        base._Ready();
    }
}
