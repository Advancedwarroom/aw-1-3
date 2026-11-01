// TheNest.cs - 巢穴（整体版，DoR 最终兵器 Owl's Nest，Sunrise 关）
// 原作权威机制（Wars Wiki）：巢穴无独立本体血条，5门可毁炮即巢穴全部；
// 五炮全灭=系统过载=巢穴爆炸=胜利。左右出兵口：左恒产陆军/右恒产空军，被堵跳过。
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
