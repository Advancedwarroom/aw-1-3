// SurvivalCampaign.cs v2 - AWDS正统生存模式·战役配置 + 无尽/肉鸽池 (开创人2026-11-14)
using Godot;

[GlobalClass]
public partial class SurvivalCampaign : Resource
{
    [ExportGroup("生存战役")]
    [Export] public string campaignName = "生存战役";
    [Export] public int[] stageSlots = new int[0];            // 关卡序列：存档槽索引（ExtraSave槽）
    [Export] public string[] stageMapPaths = new string[0]; // ✅ 缺口A(开创人2026-11-14):路径轨——res://Maps/xx.txt,槽位越界时兜底,战役可分发
    [Export] public int limitType = 0;                        // 0=总资金 1=总回合数 2=总时间(秒)
    [Export] public double budget = 0;                        // 总预算：资金(G)/大回合数/秒数；0=不限
    [Export] public bool fundsCarry = true;
    [Export] public bool coEnergyResetPerStage = true;        // 待接：每关CO能量重置
    [Export] public string playerTeam = "Player1";
    [Export] public string[] coIds = new string[0];           // 战役锁定CO：coIds[0]=主CO，其余=搭档（空=沿用各关地图默认）

    [ExportGroup("无尽/肉鸽")]
    [Export] public bool endlessMode = false;                 // 主序列打完后从池中无限抽关
    [Export] public int[] endlessPoolSlots = new int[0];      // 无尽池：存档槽索引
    [Export] public string[] endlessPoolPaths = new string[0]; // ✅ 无尽池路径轨
    [Export] public MapGenProfile[] endlessGenPool = new MapGenProfile[0]; // ✅ 缺口B:无尽生成池——抽中即现场生成一张新图(真无限)
    [Export] public bool randomDraw = true;                   // true=随机抽；false=顺序循环
    [Export] public double budgetShrinkPercentPerStage = 0;   // 每深入一层预算缩减%(0-100)，压迫感来源
}
