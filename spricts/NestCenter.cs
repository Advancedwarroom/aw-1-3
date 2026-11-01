// NestCenter.cs - 巢穴·中枢
using Godot;

public partial class NestCenter : Weapon
{
    [ExportGroup("巢穴·中枢模块")]
    [Export] public bool moduleEnabled = true;
    [Export] public int partMaxHealth = 999999;        // 血量（千分制）

    public override void _Ready()
    {
        maxHealth = partMaxHealth; health = partMaxHealth;
        canRotate = false;
        contributesToVictory = false;
        base._Ready();
    }
}
