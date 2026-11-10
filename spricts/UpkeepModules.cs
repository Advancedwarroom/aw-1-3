// UpkeepModules.cs - 设施/兵器/单位通用回合维持资金与失败事件。
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class Facility
{
    [ExportGroup("回合维持资金")]
    [Export] public int upkeepCostPerTurn = 0;
    [Export(PropertyHint.Enum, "无,势力失败,势力胜利,随机换队,中立化,自毁,变形并映射,仅执行Export事件")]
    public int upkeepFailureAction = 0;
    [Export(PropertyHint.Enum, "单位,兵器,设施")] public int upkeepFailureTransformTargetType = 0;
    [Export] public string upkeepFailureTransformUnitName = ""; // 目标为单位时使用，兼容旧配置。
    [Export(PropertyHint.File, "*.tscn")] public string upkeepFailureTransformScenePath = ""; // 目标为兵器/设施时使用。
    [Export] public Godot.Collections.Dictionary<string, string> upkeepFailureExportOverrides = new();
    [Export] public Godot.Collections.Dictionary<string, float> upkeepFailureExportDeltas = new();
}

public partial class Weapon
{
    [ExportGroup("回合维持资金")]
    [Export] public int upkeepCostPerTurn = 0;
    [Export(PropertyHint.Enum, "无,势力失败,势力胜利,随机换队,中立化,自毁,变形并映射,仅执行Export事件")]
    public int upkeepFailureAction = 0;
    [Export(PropertyHint.Enum, "单位,兵器,设施")] public int upkeepFailureTransformTargetType = 0;
    [Export] public string upkeepFailureTransformUnitName = "";
    [Export(PropertyHint.File, "*.tscn")] public string upkeepFailureTransformScenePath = "";
    [Export] public Godot.Collections.Dictionary<string, string> upkeepFailureExportOverrides = new();
    [Export] public Godot.Collections.Dictionary<string, float> upkeepFailureExportDeltas = new();
}

public partial class Infantry
{
    [ExportGroup("回合维持资金")]
    [Export] public int upkeepCostPerTurn = 0;
    [Export(PropertyHint.Enum, "无,势力失败,势力胜利,随机换队,中立化,自毁,变形并映射,仅执行Export事件")]
    public int upkeepFailureAction = 0;
    [Export(PropertyHint.Enum, "单位,兵器,设施")] public int upkeepFailureTransformTargetType = 0;
    [Export] public string upkeepFailureTransformUnitName = "";
    [Export(PropertyHint.File, "*.tscn")] public string upkeepFailureTransformScenePath = "";
    [Export] public Godot.Collections.Dictionary<string, string> upkeepFailureExportOverrides = new();
    [Export] public Godot.Collections.Dictionary<string, float> upkeepFailureExportDeltas = new();
}

public partial class GameManager
{
    [ExportGroup("通用维持资金")]
    [Export] public Godot.Collections.Array<string> upkeepFreeTeams = new();

    public void ProcessUpkeepForTeam(string team)
    {
        if (string.IsNullOrEmpty(team) || team == TeamHelper.Player0 || upkeepFreeTeams.Contains(team)) return;

        foreach (var facility in gridManager.grids.Where(g => g?.city != null).Select(g => g.city).ToList())
        {
            if (!IsInstanceValid(facility) || facility.facilityTeam != team || facility.upkeepCostPerTurn <= 0) continue;
            if (!TryPayUpkeep(team, facility.upkeepCostPerTurn))
                ResolveUpkeepFailure(facility, team, facility.upkeepFailureAction, facility.upkeepFailureTransformTargetType,
                    facility.upkeepFailureTransformUnitName, facility.upkeepFailureTransformScenePath,
                    facility.upkeepFailureExportOverrides, facility.upkeepFailureExportDeltas);
            if (gameEnded) return;
        }

        foreach (var weapon in (weaponManager?.AllWeapons ?? new List<Weapon>()).ToList())
        {
            if (!IsInstanceValid(weapon) || weapon.team != team || weapon.upkeepCostPerTurn <= 0) continue;
            if (!TryPayUpkeep(team, weapon.upkeepCostPerTurn))
                ResolveUpkeepFailure(weapon, team, weapon.upkeepFailureAction, weapon.upkeepFailureTransformTargetType,
                    weapon.upkeepFailureTransformUnitName, weapon.upkeepFailureTransformScenePath,
                    weapon.upkeepFailureExportOverrides, weapon.upkeepFailureExportDeltas);
            if (gameEnded) return;
        }

        foreach (var unit in (unitManager?.AllUnits ?? new List<Infantry>()).ToList())
        {
            if (!IsInstanceValid(unit) || unit.team != team || unit.upkeepCostPerTurn <= 0) continue;
            if (!TryPayUpkeep(team, unit.upkeepCostPerTurn))
                ResolveUpkeepFailure(unit, team, unit.upkeepFailureAction, unit.upkeepFailureTransformTargetType,
                    unit.upkeepFailureTransformUnitName, unit.upkeepFailureTransformScenePath,
                    unit.upkeepFailureExportOverrides, unit.upkeepFailureExportDeltas);
            if (gameEnded) return;
        }
    }

    private bool TryPayUpkeep(string team, int cost)
    {
        int funds = GetTeamFunds(team);
        if (funds < cost)
        {
            SetTeamFunds(team, 0);
            return false;
        }
        SetTeamFunds(team, funds - cost);
        return true;
    }

    private void ResolveUpkeepFailure(Node target, string team, int action, int transformType, string transformUnit,
        string transformScene, Godot.Collections.Dictionary<string, string> overrides,
        Godot.Collections.Dictionary<string, float> deltas)
    {
        if (target == null || !IsInstanceValid(target)) return;
        switch (action)
        {
            case 1:
                defeatedTeams.Add(team);
                CheckFactionDefeatCondition();
                break;
            case 2:
                ShowVictory(team, "Upkeep failure victory");
                break;
            case 3:
                SetUpkeepTeam(target, FindRandomOtherUpkeepTeam(team));
                break;
            case 4:
                SetUpkeepTeam(target, TeamHelper.Player0);
                break;
            case 5:
                DestroyUpkeepTarget(target);
                break;
            case 6:
                TransformUpkeepTarget(target, transformType, transformUnit, transformScene, overrides, deltas);
                break;
            case 7:
                ExportFieldMapper.ApplyOverrides(target, overrides);
                ExportFieldMapper.ApplyNumericDeltas(target, deltas);
                RefreshUpkeepVisual(target);
                break;
        }
    }

    private string FindRandomOtherUpkeepTeam(string oldTeam)
    {
        var teams = TeamHelper.GetActiveFactions().Select(f => f.Id)
            .Where(id => id != oldTeam && id != TeamHelper.Player0 && id != TeamHelper.Player).Distinct().ToList();
        return teams.Count > 0 ? teams[GD.RandRange(0, teams.Count - 1)] : TeamHelper.Player0;
    }

    private void SetUpkeepTeam(Node target, string newTeam)
    {
        if (target is Infantry unit) unit.team = newTeam;
        else if (target is Weapon weapon) weapon.team = newTeam;
        else if (target is Facility facility) facility.facilityTeam = newTeam;
        RefreshUpkeepVisual(target);
    }

    private void DestroyUpkeepTarget(Node target)
    {
        if (target is Infantry unit) unitManager.RemoveUnit(unit);
        else if (target is Weapon weapon)
        {
            weapon.ClearShelterOnDestroyed();
            weaponManager.RemoveWeapon(weapon); // 清除多格兵器全部占据格，而非只清锚点。
        }
        else if (target is Facility facility)
        {
            var grid = gridManager.grids.FirstOrDefault(g => g?.city == facility);
            if (grid != null) grid.city = null;
            facility.QueueFree();
        }
    }

    private void TransformUpkeepTarget(Node target, int targetType, string unitName, string scenePath,
        Godot.Collections.Dictionary<string, string> overrides, Godot.Collections.Dictionary<string, float> deltas)
    {
        string newTeam = UniversalTransformRuntime.GetTeam(target);
        UniversalTransformRuntime.Replace(target, targetType, unitName, scenePath, newTeam, overrides, deltas, false);
    }

    private void RefreshUpkeepVisual(Node target)
    {
        if (target is Infantry unit) { unit.UpdateTeamVisual(); unit.UpdateHpLabel(); }
        else if (target is Weapon weapon)
        {
            weapon.GetType().GetMethods().FirstOrDefault(m => m.Name == "UpdateTeamVisual" && m.GetParameters().Length == 0)?.Invoke(weapon, null);
            weapon.UpdateHpLabel();
        }
        else if (target is Facility facility)
        {
            // Facility 的真实视觉入口是 UpdateCityVisual；旧反射查找不会命中它。
            facility.UpdateCityVisual();
        }
    }
}
