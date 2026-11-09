// FixedDefense.cs - 固定防御阈值：攻击不大于固定防御时不造成伤害。
using Godot;

public partial class Infantry
{
    [ExportGroup("固定防御")]
    [Export] public int fixedDefense = 0;
}

public partial class Weapon
{
    [ExportGroup("固定防御")]
    [Export] public int fixedDefense = 0;
}
