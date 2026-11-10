// HQ.cs - 总部（Headquarters）
// 原作AW：占领敌方HQ即获胜。本作规格：加一行Export的事——改变team则Lose（输）。
// 被占领时，原归属队伍直接判负（由 GameManager.Victory 处理）。
using Godot;

public partial class HQ : Facility
{
    // HQ base frames use a near-red source palette.
    protected override float GetBaseArtHue() => 1.7f / 360.0f;

    [ExportGroup("总部 - 胜负规则")]
    /// <summary>是否在team改变（被占领）时判负。默认开启（原作行为）。</summary>
    [Export] public bool loseOnTeamChange = true;

    protected override (string neutral, string red, string blue)? GetTeamAnimNames()
        => ("P0HQ", "hqteam1", "hqteam2");

    public override void OnCaptureCompleted(Infantry unit)
    {
        string oldTeam = facilityTeam;
        base.OnCaptureCompleted(unit);

        // ✅ 核心：原归属队伍判负
        if (loseOnTeamChange && !string.IsNullOrEmpty(oldTeam) && oldTeam != facilityTeam)
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.TriggerTeamLose(oldTeam, $"HQ被占领（{unit?.team}）");
        }
    }
}
