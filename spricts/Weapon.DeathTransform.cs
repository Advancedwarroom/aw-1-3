// Weapon.DeathTransform.cs - 兵器尸化/亡语：死亡后原地生成单位、兵器或设施。
using Godot;

public partial class Weapon
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
        // 同一死亡事件只尝试一次；生成失败后走原有销毁，不在延迟回调里二次尸化。
        if (!deathTransformEnabled || deathTransformAttempted || grid == null) return false;
        deathTransformAttempted = true;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return false;
        string newTeam = UniversalTransformRuntime.ResolveDeathTransformTeam(this, gm, deathTransformTeamPolicy, deathTransformExplicitTeam);
        Node replacement = UniversalTransformRuntime.Replace(this, deathTransformTargetType, deathTransformUnitName,
            deathTransformScenePath, newTeam, deathTransformExportOverrides, deathTransformExportDeltas, true);
        if (replacement == null) return false;
        GD.Print($"[尸化] {Name} 死亡后生成 {replacement.GetType().Name}");
        return true;
    }
}
