// MapGenProfile.cs - 程序地图生成器·配置面 (开创人2026-11-14)
// 缺口B首片:设计合同。生成核心下轮从 SavedMapData 数据结构反打。
using Godot;

[GlobalClass]
public partial class MapGenProfile : Resource
{
    [ExportGroup("尺寸与分屏")]
    [Export] public int width = 20;                    // 宽(格)
    [Export] public int height = 15;                   // 高(格)
    [Export] public int screenCount = 1;               // 自动竖切分屏数(1=不分)

    [ExportGroup("地形权重(自动归一)")]
    [Export] public double plainWeight = 50;           // 平原
    [Export] public double forestWeight = 15;          // 森林
    [Export] public double mountainWeight = 10;        // 山
    [Export] public double seaWeight = 12;             // 海
    [Export] public double riverWeight = 5;            // 河(线状生成,权重=条数倾向)
    [Export] public double roadWeight = 8;             // 路(HQ互连路网密度)

    [ExportGroup("结构")]
    [Export] public int symmetryMode = 0;              // 0=不对称 1=竖轴镜像 2=横轴镜像 3=点对称
    [Export] public double chokeDensity = 0.3;         // 咽喉密度0~1:山/海挤走廊的力度
    [Export] public int chokeLineCount = 3;            // 咽喉线数量(随机折线山脉/海峡)

    [ExportGroup("势力与资源")]
    [Export] public int factionCount = 2;              // 势力数=HQ数(2~4)
    [Export] public int minStartDistance = 8;          // HQ间最小曼哈顿距离
    [Export] public int cityCountPerFaction = 4;       // 每势力城区数(资源平衡=镜像布置)
    [Export] public int factoryCountPerFaction = 1;
    [Export] public int airportCountPerFaction = 0;
    [Export] public int portCountPerFaction = 0;
    [Export] public bool balanceResources = true;      // 各势力资源量镜像相等

    [ExportGroup("校验")]
    [Export] public bool validateReachability = true;  // BFS校验:HQ不可孤岛,否则自动开路
    [Export] public int maxRegenAttempts = 10;         // 校验失败重生成上限

    [ExportGroup("随机")]
    [Export] public int seed = 0;                      // 0=每局真随机;其它=固定种子(每日挑战/复现)

    // 派生:归一化权重表
    public double TotalWeight => plainWeight + forestWeight + mountainWeight + seaWeight;
}
