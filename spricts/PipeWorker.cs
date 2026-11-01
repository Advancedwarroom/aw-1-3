// PipeWorker.cs - 管道构造者（黑洞军稀有单位）
// 原作设定：移动方式与新型坦克一致（履带 6 格）；无攻击力；装甲堪比新型坦克（承伤表同 Neotank）。
// 能力：复用建造模块——建造「管道接缝 PIPESEAM」且必须临格（修复/续接管道供能线）。
// 稀有单位：地图编辑器/自定义对战可让玩家永久操控；被缴获后触发「缴获红利」联动（见 FactoryLinkage）。
using Godot;

public partial class PipeWorker : Infantry
{
    public override string GetIdleAnimName() => "PipeWorker1";

    public override void _Ready()
    {
        base._Ready();
    }
}
