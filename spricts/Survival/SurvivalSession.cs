// SurvivalSession.cs v2 - 会话运行时+全量换图装载+无尽池 (开创人2026-11-14)
// autoload（组名 survival_session）。预算数学/回合与时间扣减/关卡推进/HUD/结算信号。
using Godot;

public partial class SurvivalSession : Node
{
    public static SurvivalSession Instance { get; private set; }

    [Signal] public delegate void BudgetChangedEventHandler(double remaining, int limitType);
    [Signal] public delegate void StageChangedEventHandler(int stageIndex, int totalStages);
    [Signal] public delegate void SurvivalOverEventHandler(bool victory, int stagesCleared, double remainingBudget);

    public SurvivalCampaign Campaign { get; private set; }
    public int StageIndex { get; private set; }
    public double RemainingBudget { get; private set; }
    public bool Active { get; private set; }
    public int StagesCleared => StageIndex;

    private RichTextLabel _hud;
    private double _timeAccum;

    public override void _Ready()
    {
        Instance = this;
        AddToGroup("survival_session");
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        if (!Active || Campaign == null || Campaign.limitType != 2) return;
        _timeAccum += delta;
        if (_timeAccum >= 1.0)
        {
            RemainingBudget -= (int)_timeAccum;
            _timeAccum = 0;
            EmitSignal(SignalName.BudgetChanged, RemainingBudget, 2);
            UpdateHud();
            if (RemainingBudget <= 0) EndRun(false, "时间耗尽");
        }
    }

    // ✅ 开局（按钮/调试控制台调）
    public void BeginCampaign(SurvivalCampaign campaign)
    {
        Campaign = campaign;
        StageIndex = 0;
        RemainingBudget = campaign?.budget ?? 0;
        Active = campaign != null && campaign.stageMapPaths.Length > 0;
        _timeAccum = 0;
        EnsureHud();
        EmitSignal(SignalName.StageChanged, StageIndex, Campaign?.stageMapPaths.Length ?? 0);
        UpdateHud();
        if (Active) GD.Print($"[Survival] 开局:{campaign.campaignName} 关卡×{campaign.stageMapPaths.Length} 预算[{campaign.limitType}]={campaign.budget}");
    }

    // ✅ 钩子①：GameManager.Turn.cs isNewTurn 每大回合调用（回合限制）
    public void TickRound()
    {
        if (!Active || Campaign == null || Campaign.limitType != 1) return;
        RemainingBudget--;
        EmitSignal(SignalName.BudgetChanged, RemainingBudget, 1);
        UpdateHud();
        if (RemainingBudget <= 0) EndRun(false, "回合耗尽");
    }

    // ✅ 钩子②：生产扣费闸门（资金限制）。返回false=拒绝生产。调用点待接生产线。
    public bool TrySpendProduction(int cost)
    {
        if (!Active || Campaign == null || Campaign.limitType != 0) return true;
        if (cost > RemainingBudget) { GD.Print($"[Survival] 生产被拒:{cost}G > 余量{RemainingBudget}G"); return false; }
        RemainingBudget -= cost;
        EmitSignal(SignalName.BudgetChanged, RemainingBudget, 0);
        UpdateHud();
        if (RemainingBudget <= 0) GD.Print("[Survival] 资金见底——仍可战，但造不出新单位了");
        return true;
    }

    // ✅ 钩子③：GameManager.Victory.cs ShowVictory 调用——玩家胜→推进下一关
    public void OnMapVictorious(string winningTeam)
    {
        if (!Active || Campaign == null) return;
        if (winningTeam != Campaign.playerTeam) { EndRun(false, "战役失利"); return; }
        StageIndex++;
        int total = Campaign.endlessMode
            ? (Campaign.randomDraw ? -1 : Campaign.stageSlots.Length + Campaign.endlessPoolSlots.Length)
            : Campaign.stageSlots.Length;
        if (!Campaign.endlessMode && StageIndex >= Campaign.stageSlots.Length) { EndRun(true, "全部关卡通过"); return; }
        if (Campaign.endlessMode && Campaign.endlessPoolSlots.Length == 0) { EndRun(true, "无尽池为空,战役完结"); return; }
        // 无尽模式：深入一层，预算缩减
        if (Campaign.endlessMode && StageIndex >= Campaign.stageSlots.Length && Campaign.budgetShrinkPercentPerStage > 0)
        {
            double before = RemainingBudget;
            RemainingBudget *= (100.0 - Campaign.budgetShrinkPercentPerStage) / 100.0;
            GD.Print($"[Survival] 无尽深入:预算 {before} → {RemainingBudget}(-{Campaign.budgetShrinkPercentPerStage}%)");
        }
        EmitSignal(SignalName.StageChanged, StageIndex, total);
        UpdateHud();
        CallDeferred(nameof(DeferredStageLoad));
    }

    // ✅ 装载下一关：走 ExtraSave.LoadMapToGame 全量换图（清理实体→重建网格→地形单位CO全落位）
    private void DeferredStageLoad()
    {
        int slot = ResolveCurrentSlot();
        var root = GetTree()?.Root;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var gridMgr = root?.FindChild("GridManager", recursive: true, owned: false) as GridManager;
        var terrainEditor = root?.FindChild("TerrainEditor", recursive: true, owned: false) as TerrainEditor;
        var extraSave = root?.GetNodeOrNull("ExtraSave") as ExtraSave;
        if (gm == null || gridMgr == null || terrainEditor == null || extraSave == null)
        {
            GD.PushWarning($"[Survival] 装载失败:依赖缺失 gm={gm != null} grid={gridMgr != null} editor={terrainEditor != null} save={extraSave != null}");
            return;
        }
        gm.gameEnded = false;   // ✅ 结算后复位，新关开局
        gm.turnPhase = 1;
        bool ok = extraSave.LoadMapToGame(slot, gm, gridMgr, terrainEditor);
        GD.Print($"[Survival] 第{StageIndex + 1}关装载 slot={slot} → {(ok ? "成功" : "失败")}");
        if (ok && Campaign.coEnergyResetPerStage) CoModuleSystem.ResetAllCharges();   // ✅ 开创人2026-11-14 CO能量跨关重置
        EmitSignal(SignalName.BudgetChanged, RemainingBudget, Campaign.limitType);
        UpdateHud();
    }

    private int ResolveCurrentSlot()
    {
        if (!Campaign.endlessMode || StageIndex < Campaign.stageSlots.Length)
            return Campaign.stageSlots[Mathf.Min(StageIndex, Campaign.stageSlots.Length - 1)];
        int depth = StageIndex - Campaign.stageSlots.Length;
        if (!Campaign.randomDraw) return Campaign.endlessPoolSlots[depth % Campaign.endlessPoolSlots.Length];
        return Campaign.endlessPoolSlots[GD.RandRange(0, Campaign.endlessPoolSlots.Length - 1)];
    }

    private void EndRun(bool victory, string reason)
    {
        Active = false;
        GD.Print($"[Survival] 战役结束 胜利={victory} 通过={StagesCleared}关 余量={RemainingBudget} ({reason})");
        EmitSignal(SignalName.SurvivalOver, victory, StagesCleared, RemainingBudget);
        UpdateHud();
    }

    private void EnsureHud()
    {
        if (_hud != null && IsInstanceValid(_hud)) return;
        var vp = GetViewport(); if (vp == null) return;
        _hud = new RichTextLabel { Position = new Vector2(8, 8), Size = new Vector2(360, 46), BbcodeEnabled = true };
        vp.AddChild(_hud);
    }

    private void UpdateHud()
    {
        if (_hud == null || !IsInstanceValid(_hud)) return;
        if (!Active) { if (Campaign != null) _hud.Text = $"[Survival] 结束：通过{StagesCleared}关 余量{RemainingBudget}"; return; }
        string unit = Campaign.limitType == 0 ? "G" : Campaign.limitType == 1 ? "回合" : "秒";
        int total = Campaign.endlessMode ? -1 : Campaign.stageSlots.Length;
        string prog = total > 0 ? $"{StageIndex + 1}/{total}" : $"第{StageIndex + 1}层";
        _hud.Text = $"[Survival] {Campaign.campaignName} {prog}  预算:{RemainingBudget}{unit}";
    }
}
