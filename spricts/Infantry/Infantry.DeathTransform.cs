// Infantry.DeathTransform.cs - 尸化/亡语：旧单位死亡，在原格生成真正的新兵种实例。
using Godot;
using System.Linq;

public partial class Infantry
{
    [ExportGroup("尸化 / 亡语变形")]
    [Export] public bool deathTransformEnabled = false;
    [Export] public string deathTransformUnitName = "";
    [Export] public int deathTransformTeamPolicy = 0; // 0=沿用旧队伍 1=中立 2=随机其他势力 3=指定队伍
    [Export] public string deathTransformExplicitTeam = "Player1";
    [Export] public Godot.Collections.Dictionary<string, string> deathTransformExportOverrides = new();
    [Export] public Godot.Collections.Dictionary<string, float> deathTransformExportDeltas = new();

    public bool TryDeathTransform()
    {
        if (!deathTransformEnabled || string.IsNullOrWhiteSpace(deathTransformUnitName)) return false;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var um = gm?.unitManager;
        Grids oldGrid = grid;
        if (um?.units == null || oldGrid == null || !UnitProductionDatabase.HasUnit(deathTransformUnitName)) return false;

        var info = UnitProductionDatabase.GetInfo(deathTransformUnitName);
        var scene = GD.Load<PackedScene>(info.ScenePath);
        if (scene == null) return false;

        string newTeam = ResolveDeathTransformTeam(gm);

        // 先解除旧单位占位，再生成新实例；失败则完整恢复旧单位占位，让原死亡流程继续。
        ClearFootprint();
        oldGrid.infantries.Remove(this);
        if (oldGrid.infantry == this) oldGrid.infantry = oldGrid.infantries.FirstOrDefault();
        um.AllUnits.Remove(this);
        grid = null;

        Infantry replacement = um.SpawnUnit(scene, oldGrid.GridIndex, newTeam);
        if (replacement == null)
        {
            grid = oldGrid;
            if (!oldGrid.infantries.Contains(this)) oldGrid.infantries.Add(this);
            if (oldGrid.infantry == null) oldGrid.infantry = this;
            if (!um.AllUnits.Contains(this)) um.AllUnits.Add(this);
            OccupyFootprint(gm);
            return false;
        }

        ExportFieldMapper.ApplyOverrides(replacement, deathTransformExportOverrides);
        ExportFieldMapper.ApplyNumericDeltas(replacement, deathTransformExportDeltas);
        replacement.UpdateTeamVisual();
        replacement.UpdateHpLabel();

        OnDestroyed();
        QueueFree();
        GD.Print($"[尸化] {Name} 死亡后在 {oldGrid.GridIndex} 生成 {replacement.UnitTypeId}");
        return true;
    }

    private string ResolveDeathTransformTeam(GameManager gm)
    {
        switch (deathTransformTeamPolicy)
        {
            case 1: return TeamHelper.Player0;
            case 2:
                var teams = gm.unitManager.AllUnits.Where(u => u != null && u != this && u.team != team && u.team != TeamHelper.Player0)
                    .Select(u => u.team).Distinct().ToList();
                if (gm.weaponManager != null)
                    teams.AddRange(gm.weaponManager.AllWeapons.Where(w => w != null && w.team != team && w.team != TeamHelper.Player0).Select(w => w.team));
                teams = teams.Distinct().ToList();
                return teams.Count > 0 ? teams[GD.RandRange(0, teams.Count - 1)] : TeamHelper.Player0;
            case 3: return string.IsNullOrWhiteSpace(deathTransformExplicitTeam) ? team : deathTransformExplicitTeam;
            default: return team;
        }
    }
}
