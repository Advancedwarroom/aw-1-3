// PipeWorker.cs - 管道构造者（黑洞军稀有单位）
// 原作设定：移动方式与新型坦克一致（履带 6 格）；无攻击力；装甲堪比新型坦克（承伤表同 Neotank）。
// 能力：复用建造模块——建造「管道接缝 PIPESEAM」且必须临格（修复/续接管道供能线）。
// 稀有单位特权（地图编辑器/自定义对战可配）：永久免费生产 3 个即刻行动单位（每回合重置）。
// 被缴获后触发「缴获红利」联动（见 FactoryLinkage / SuperFactory）。
using Godot;

public partial class PipeWorker : Infantry
{
    // ========== 稀有单位特权：永久免费生产 ==========
    [ExportGroup("管道构造者 - 永久免费生产（稀有单位特权）")]
    /// <summary>是否永久免费生产（地图编辑器/自定义对战地图配置项）</summary>
    [Export] public bool permanentFreeProduction = false;
    /// <summary>每回合免费即刻行动单位数（原作：3个）</summary>
    [Export] public int freeInstantUnitsPerTurn = 3;

    /// <summary>本回合已产出的免费即刻单位数（运行时，每回合重置）</summary>
    public int freeUnitsProducedThisTurn = 0;

    public override string GetIdleAnimName() => team == "Player2" ? "PipeWorker2" : "PipeWorker1";

    public override void _Ready()
    {
        base._Ready();

        // ✅ 永久免费生产：开启生产模块（免费+即时=总进度0当回合完成+外部放置）
        if (permanentFreeProduction)
        {
            canProduceUnits = true;
            productionFree = true;            // ⑫ 无消耗
            productionLocationMode = 2;       // 外部
            productionTotalProgress = 0;      // 即时生产（当回合完成）
            productionProgressPerTurn = 0;
            productionAutomation = false;      // 生产消耗行动（原作行为）
        }
    }

    /// <summary>每回合重置免费生产计数（由 GameManager.Turn 调用）</summary>
    public override void ProcessTurnProduction()
    {
        freeUnitsProducedThisTurn = 0;
        base.ProcessTurnProduction();
    }

    /// <summary>管道构造者特权：本回合还能否免费生产即刻单位</summary>
    public bool CanFreeProduceThisTurn()
    {
        return permanentFreeProduction && freeUnitsProducedThisTurn < freeInstantUnitsPerTurn;
    }

    /// <summary>开始一次免费生产（计入本回合配额）</summary>
    public bool StartFreeProduction(string unitId, Grids externalTargetGrid = null)
    {
        if (!CanFreeProduceThisTurn()) return false;
        if (!StartProduction(unitId, false, externalTargetGrid)) return false;
        freeUnitsProducedThisTurn++;
        return true;
    }
}
