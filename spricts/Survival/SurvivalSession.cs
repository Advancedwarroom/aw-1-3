// SurvivalSession.cs - AWDS正统生存模式·会话运行时 (开创人2026-11-14)
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
        if (StageIndex >= Campaign.stageMapPaths.Length) { EndRun(true, "全部关卡通过"); return; }
        GD.Print($"[Survival] 进入第 {StageIndex + 1}/{Campaign.stageMapPaths.Length} 关:{Campaign.stageMapPaths[StageIndex]}");
        EmitSignal(SignalName.StageChanged, StageIndex, Campaign.stageMapPaths.Length);
        UpdateHud();
        RequestStageLoad(Campaign.stageMapPaths[StageIndex]);
    }

    // 地图装载交接点：对接地图加载管线（TerrainEditor/存档槽），下一轮钉死具体调用
    private void RequestStageLoad(string mapPath)
    {
        GD.Print($"[Survival] ⚠ 待接管线:装载关卡地图 {mapPath}（编辑/存档加载入口）");
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
        _hud.Text = $"[Survival] {Campaign.campaignName} 第{StageIndex + 1}/{Campaign.stageMapPaths.Length}关  预算:{RemainingBudget}{unit}";
    }
}
