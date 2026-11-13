// Facility.DeathTransform.cs - 设施通用销毁与尸化/亡语；占领不会进入此入口。
using Godot;

public partial class Facility
{
    [ExportGroup("尸化 / 亡语变形")]
    [Export] public bool deathTransformEnabled = false;
    [Export(PropertyHint.Enum, "单位,兵器,设施")] public int deathTransformTargetType = 0;
    [Export] public string deathTransformUnitName = "";
    [Export(PropertyHint.File, "*.tscn")] public string deathTransformScenePath = "";
    [Export(PropertyHint.Enum, "沿用旧队伍,中立,随机其他势力,指定队伍")] public int deathTransformTeamPolicy = 0;
    [Export] public string deathTransformExplicitTeam = "Player1";
    [Export] public Godot.Collections.Dictionary<string, string> deathTransformExportOverrides = new();
    [Export] public Godot.Collections.Dictionary<string, float> deathTransformExportDeltas = new();

    private bool deathTransformAttempted = false;

    public bool TryDeathTransform()
    {
        if (!deathTransformEnabled || deathTransformAttempted || ParentGrid == null) return false;
        deathTransformAttempted = true;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return false;
        string newTeam = UniversalTransformRuntime.ResolveDeathTransformTeam(this, gm, deathTransformTeamPolicy, deathTransformExplicitTeam);
        Node replacement = UniversalTransformRuntime.Replace(this, deathTransformTargetType, deathTransformUnitName,
            deathTransformScenePath, newTeam, deathTransformExportOverrides, deathTransformExportDeltas, true);
        if (replacement == null) return false;
        GD.Print($"[尸化] {Name} 销毁后生成 {replacement.GetType().Name}");
        return true;
    }

    [Export] public double campaignKillScoreValue = 0;      // ✅ 开创人2026-11-14 接缝③:击杀积分价值(0=不记账)
    [Export] public double campaignKillCrystalValue = 0;    // ✅ 宝钞价值
    public virtual void OnDestroyed()
    {
        if (TryDeathTransform()) return;
        {   // ✅ 开创人2026-11-14 接缝③:死亡入口→战役结算台账
            var __st = GetTree()?.Root?.FindChild("CampaignSettlement", true, false) as CampaignSettlement;
            __st?.RecordKill(campaignKillScoreValue, campaignKillCrystalValue, lastAttackerTeam);
        }
        if (ParentGrid != null && ParentGrid.city == this) ParentGrid.city = null;
        QueueFree();
    }
}
