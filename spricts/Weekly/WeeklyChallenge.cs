// WeeklyChallenge.cs - 每周挑战壳 (开创人2026-11-14)
// 审查四节:统一种子喂地图+天赋+敌军=全服同题同解可复现。日期即题号,周一自动换题。
using Godot;

public partial class WeeklyChallenge : Node
{
    public static WeeklyChallenge Instance { get; private set; }

    [Signal] public delegate void ChallengeReadyEventHandler(int seed, string challengeId);

    public string ChallengeId { get; private set; } = "";
    public int ChallengeSeed { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        AddToGroup("weekly_challenge");
    }

    // 本周题号:日期哈希为种子——同日期任何机器同一题
    public void GenerateThisWeek()
    {
        string today = Time.GetDatetimeStringFromSystem(false).Substring(0, 10);
        ChallengeId = $"W-{today}";
        ChallengeSeed = Mathf.Abs(ChallengeId.GetHashCode());
        GD.Print($"[Weekly] 本周挑战 {ChallengeId} seed={ChallengeSeed}");
        EmitSignal(SignalName.ChallengeReady, ChallengeSeed, ChallengeId);
    }

    // 一键开赛:固定种子生成图进无尽池+固定生存规则
    // TODO(接缝): TalentRuntime.DraftThree 的种子化(接同一 ChallengeSeed)=天赋也固定; 敌军CO固定阵容=战役配置coIds
    public void StartThisWeekChallenge()
    {
        GenerateThisWeek();
        var profile = new MapGenProfile
        {
            seed = ChallengeSeed,
            width = 22, height = 16,
            symmetryMode = 1,
            factionCount = 2,
            chokeDensity = 0.35,
        };
        var c = new SurvivalCampaign
        {
            campaignName = $"每周挑战·{ChallengeId}",
            limitType = 1,
            budget = 25,
            playerTeam = "Player1",
            endlessMode = true,
            endlessGenPool = new MapGenProfile[] { profile },
            budgetShrinkPercentPerStage = 8,
        };
        SurvivalSession.Instance?.BeginCampaign(c);
        GD.Print("[Weekly] 已开赛——同种子同地图,层数即成绩");
    }
}
