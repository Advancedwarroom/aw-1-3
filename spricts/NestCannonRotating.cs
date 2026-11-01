// NestCannonRotating.cs - 巢穴·旋转炮
using Godot;

public partial class NestCannonRotating : Weapon
{
    [ExportGroup("巢穴·旋转炮模块")]
    [Export] public bool moduleEnabled = true;
    [Export] public int partMaxHealth = 800;        // 血量（千分制）

    public override void _Ready()
    {
        maxHealth = partMaxHealth; health = partMaxHealth;
        canRotate = true;
        contributesToVictory = true; victoryCountRequired = 5;
        base._Ready();
    }
}
