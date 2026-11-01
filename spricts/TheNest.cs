// TheNest.cs - 巢穴（整体版，DoR 原作 Owl's Nest）
// 复用 GrandBolt 弱点多格框架：5个可毁炮位=弱点（3固定+2旋转），全毁=巢穴陷落
// 拆分版见 NestCenter/NestCannonFixed/NestCannonRotating/NestMortar
using Godot;

public partial class TheNest : GrandBolt
{
    [ExportGroup("巢穴模块（整体版）")]
    [Export] public bool nestEnabled = true;

    public override void _Ready()
    {
        grandBoltSize = new Vector2I(9, 6);
        footprintMask = new string[] {
            "..#####..",
            ".#######.",
            "#########",
            "#########",
            ".#######.",
            "..#####..",
        };
        weakPointOffsets = new Godot.Collections.Array<Vector2I> {
            new Vector2I(2, 1),    // 固定炮·左
            new Vector2I(4, 0),    // 固定炮·中
            new Vector2I(6, 1),    // 固定炮·右
            new Vector2I(1, 3),    // 旋转炮·左
            new Vector2I(7, 3),    // 旋转炮·右
        };
        weakPointMaxHealth = 800;                 // 原作：炮位8HP（千分制800）
        destroyOnAllWeakPointsDestroyed = true;   // 5弱点全毁=陷落
        enterDesperateOnAllDestroyed = false;     // 巢穴无困兽犹斗
        grandBoltCooldown = 1;
        factionRecolor = true;
        base._Ready();
    }
}
