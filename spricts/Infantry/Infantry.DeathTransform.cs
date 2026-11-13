// Infantry.DeathTransform.cs - 尸化/亡语：旧单位死亡，原地生成单位、兵器或设施。
using Godot;
using System;

public partial class Infantry
{
    [ExportGroup("尸化 / 亡语变形")]
    [Export] public bool deathTransformEnabled = false;
    [Export(PropertyHint.Enum, "单位,兵器,设施")] public int deathTransformTargetType = 0;
    [Export] public string deathTransformUnitName = ""; // 目标为单位时使用，兼容旧配置。
    [Export(PropertyHint.File, "*.tscn")] public string deathTransformScenePath = ""; // 目标为兵器/设施时使用。
    [Export(PropertyHint.Enum, "沿用旧队伍,中立,随机其他势力,指定队伍")] public int deathTransformTeamPolicy = 0;
    [Export] public string deathTransformExplicitTeam = "Player1";
    [Export] public Godot.Collections.Dictionary<string, string> deathTransformExportOverrides = new();
    [Export] public Godot.Collections.Dictionary<string, float> deathTransformExportDeltas = new();
    [Export] public bool deathTransformRequireLegalTile = false;   // ✅ 开创人2026-11-14 跳伞逃生式:目标格不可停留则取消变形

    public bool TryDeathTransform()
    {
        if (!deathTransformEnabled || grid == null) return false;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return false;
        string newTeam = UniversalTransformRuntime.ResolveDeathTransformTeam(this, gm, deathTransformTeamPolicy, deathTransformExplicitTeam);
        Node replacement = UniversalTransformRuntime.Replace(this, deathTransformTargetType, deathTransformUnitName,
            deathTransformScenePath, newTeam, deathTransformExportOverrides, deathTransformExportDeltas, true, deathTransformRequireLegalTile ? (Func<Node, bool>)(n => n is Infantry ni && ni.GetMoveCostOnGrid(grid) < 999) : null);
        if (replacement == null) return false;
        GD.Print($"[尸化] {Name} 死亡后生成 {replacement.GetType().Name}");
        return true;
    }
}
