// SurvivalCampaign.cs - AWDS正统生存模式·战役配置 (开创人2026-11-14)
// 原作：Dual Strike Survival——连续打一串地图，三选一全局紧箍咒：总资金/总回合数/总时间
using Godot;

[GlobalClass]
public partial class SurvivalCampaign : Resource
{
    [ExportGroup("生存战役")]
    [Export] public string campaignName = "生存战役";
    [Export] public string[] stageMapPaths = new string[0];   // 关卡序列（地图文件路径，顺序即流程）
    [Export] public int limitType = 0;                        // 0=总资金 1=总回合数 2=总时间(秒)
    [Export] public double budget = 0;                        // 总预算：资金(G)/大回合数/秒数；0=不限（仅推进度）
    [Export] public bool fundsCarry = true;                   // 资金模式：每关结余是否带入下一关
    [Export] public bool coEnergyResetPerStage = true;        // 每关开始时CO能量重置
    [Export] public string playerTeam = "Player1";
    [Export] public string[] coIds = new string[0];           // 锁定CO列表（空=沿用各关地图默认）
}
