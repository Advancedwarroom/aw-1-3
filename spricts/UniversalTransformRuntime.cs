// UniversalTransformRuntime.cs - 单位/兵器/设施通用原地变形。
// 先解除旧对象占位，生成失败完整恢复；只有成功后才销毁旧对象并映射新实例 Export。
using Godot;
using System.Collections.Generic;
using System.Linq;

public static class UniversalTransformRuntime
{
    private sealed class DetachedSource
    {
        public Node Source;
        public GameManager Game;
        public Grids Grid;
        public List<Grids> WeaponGrids = new();
        public bool HadUnitCategory;
        public UnitCategory Category;
    }

    public static Node Replace(Node source, int targetType, string unitName, string scenePath, string newTeam,
        Godot.Collections.Dictionary<string, string> overrides,
        Godot.Collections.Dictionary<string, float> deltas,
        bool triggeredByDeath)
    {
        if (source == null || !GodotObject.IsInstanceValid(source)) return null;
        var gm = source.GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        Grids grid = GetGrid(source, gm);
        if (gm?.unitManager == null || gm.weaponManager == null || grid == null) return null;

        PackedScene scene = ResolveScene(targetType, unitName, scenePath);
        if (scene == null || !SceneRootMatches(scene, targetType)) return null;

        var detached = Detach(source, gm, grid);
        if (detached == null) return null;

        Node replacement = Spawn(gm, grid, scene, targetType, newTeam);
        if (replacement == null)
        {
            Restore(detached);
            RefreshAfterChange(gm);
            return null;
        }

        ExportFieldMapper.ApplyOverrides(replacement, overrides);
        ExportFieldMapper.ApplyNumericDeltas(replacement, deltas);
        FinalizeOldSource(detached, triggeredByDeath);

        if (replacement is Infantry unit)
            gm.RegisterUnit(unit, gm.InferCategory(unit));
        RefreshVisual(replacement);
        RefreshAfterChange(gm);
        return replacement;
    }

    public static string ResolveDeathTransformTeam(Node source, GameManager gm, int policy, string explicitTeam)
    {
        string oldTeam = GetTeam(source);
        switch (policy)
        {
            case 1:
                return TeamHelper.Player0;
            case 2:
                var teams = gm.unitManager.AllUnits
                    .Where(u => u != null && u != source && u.team != oldTeam && u.team != TeamHelper.Player0)
                    .Select(u => u.team).Distinct().ToList();
                if (gm.weaponManager != null)
                    teams.AddRange(gm.weaponManager.AllWeapons
                        .Where(w => w != null && w != source && w.team != oldTeam && w.team != TeamHelper.Player0)
                        .Select(w => w.team));
                teams = teams.Distinct().ToList();
                return teams.Count > 0 ? teams[GD.RandRange(0, teams.Count - 1)] : TeamHelper.Player0;
            case 3:
                return string.IsNullOrWhiteSpace(explicitTeam) ? oldTeam : explicitTeam;
            default:
                return oldTeam;
        }
    }

    public static string GetTeam(Node source)
    {
        if (source is Infantry unit) return unit.team;
        if (source is Weapon weapon) return weapon.team;
        if (source is Facility facility) return facility.facilityTeam;
        return TeamHelper.Player0;
    }

    private static Grids GetGrid(Node source, GameManager gm)
    {
        if (source is Infantry unit) return unit.grid;
        if (source is Weapon weapon) return weapon.grid;
        if (source is Facility facility)
            return facility.ParentGrid ?? gm?.gridManager?.grids.FirstOrDefault(g => g?.city == facility);
        return null;
    }

    private static PackedScene ResolveScene(int targetType, string unitName, string scenePath)
    {
        if (targetType == 0)
        {
            if (string.IsNullOrWhiteSpace(unitName) || !UnitProductionDatabase.HasUnit(unitName)) return null;
            return GD.Load<PackedScene>(UnitProductionDatabase.GetInfo(unitName).ScenePath);
        }
        if (string.IsNullOrWhiteSpace(scenePath)) return null;
        return GD.Load<PackedScene>(scenePath);
    }

    private static bool SceneRootMatches(PackedScene scene, int targetType)
    {
        Node probe = scene.Instantiate();
        if (probe == null) return false;
        bool valid = targetType == 0 ? probe is Infantry
            : targetType == 1 ? probe is Weapon
            : targetType == 2 && probe is Facility;
        probe.Free();
        return valid;
    }

    private static DetachedSource Detach(Node source, GameManager gm, Grids grid)
    {
        var state = new DetachedSource { Source = source, Game = gm, Grid = grid };
        if (source is Infantry unit)
        {
            state.HadUnitCategory = gm.unitCategories.TryGetValue(unit, out state.Category);
            unit.ClearFootprint();
            grid.infantries.Remove(unit);
            if (grid.infantry == unit) grid.infantry = grid.infantries.FirstOrDefault();
            gm.unitManager.AllUnits.Remove(unit);
            gm.unitCategories.Remove(unit);
            unit.grid = null;
            return state;
        }
        if (source is Weapon weapon)
        {
            state.WeaponGrids = weapon.isMultiTile ? weapon.occupiedGrids.ToList() : new List<Grids>();
            if (!state.WeaponGrids.Contains(grid)) state.WeaponGrids.Add(grid);
            foreach (var occupied in state.WeaponGrids)
            {
                if (occupied == null) continue;
                occupied.weapons.Remove(weapon);
                if (occupied.weapon == weapon) occupied.weapon = occupied.weapons.FirstOrDefault();
            }
            weapon.occupiedGrids.Clear();
            gm.weaponManager.AllWeapons.Remove(weapon);
            gm.weapons.Remove(weapon);
            return state;
        }
        if (source is Facility facility)
        {
            if (grid.city != facility) return null;
            grid.city = null;
            return state;
        }
        return null;
    }

    private static Node Spawn(GameManager gm, Grids grid, PackedScene scene, int targetType, string team)
    {
        if (targetType == 0) return gm.unitManager.SpawnUnit(scene, grid.GridIndex, team);
        if (targetType == 1) return gm.weaponManager.SpawnWeapon(scene, grid.GridIndex, team);
        if (targetType != 2 || grid.city != null) return null;

        var facility = scene.Instantiate<Facility>();
        if (facility == null) return null;
        facility.Name = $"{team}_{facility.GetType().Name}_Transformed";
        facility.Position = Vector2.Zero;
        facility.facilityTeam = team;
        grid.AddChild(facility);
        grid.city = facility;
        return facility;
    }

    private static void Restore(DetachedSource state)
    {
        if (state.Source is Infantry unit)
        {
            unit.grid = state.Grid;
            if (!state.Grid.infantries.Contains(unit)) state.Grid.infantries.Add(unit);
            if (state.Grid.infantry == null) state.Grid.infantry = unit;
            if (!state.Game.unitManager.AllUnits.Contains(unit)) state.Game.unitManager.AllUnits.Add(unit);
            if (state.HadUnitCategory) state.Game.unitCategories[unit] = state.Category;
            unit.OccupyFootprint(state.Game);
        }
        else if (state.Source is Weapon weapon)
        {
            foreach (var occupied in state.WeaponGrids)
            {
                if (occupied == null) continue;
                if (!occupied.weapons.Contains(weapon)) occupied.weapons.Add(weapon);
                if (occupied.weapon == null) occupied.weapon = weapon;
            }
            weapon.occupiedGrids.Clear();
            if (weapon.isMultiTile) weapon.occupiedGrids.AddRange(state.WeaponGrids);
            if (!state.Game.weaponManager.AllWeapons.Contains(weapon)) state.Game.weaponManager.AllWeapons.Add(weapon);
            if (!state.Game.weapons.Contains(weapon)) state.Game.weapons.Add(weapon);
        }
        else if (state.Source is Facility facility)
        {
            state.Grid.city = facility;
        }
    }

    private static void FinalizeOldSource(DetachedSource state, bool triggeredByDeath)
    {
        if (state.Source is Infantry unit)
        {
            if (state.Game.selectedInfantry == unit) state.Game.selectedInfantry = null;
            unit.OnDestroyed();
            unit.QueueFree();
        }
        else if (state.Source is Weapon weapon)
        {
            if (state.Game.selectedWeapon == weapon) state.Game.selectedWeapon = null;
            weapon.ClearShelterOnDestroyed(); // 先恢复内部服务快照，再按原庇护规则清理乘员。
            if (triggeredByDeath) state.Game.CheckVictoryCondition();
            weapon.QueueFree();
        }
        else if (state.Source is Facility facility)
        {
            facility.QueueFree();
        }
    }

    private static void RefreshVisual(Node target)
    {
        if (target is Infantry unit)
        {
            unit.UpdateTeamVisual();
            unit.UpdateHpLabel();
        }
        else if (target is Weapon weapon)
        {
            weapon.RefreshTeamVisual();
            weapon.UpdateHpLabel();
        }
        else if (target is Facility facility)
        {
            facility.UpdateCityVisual();
        }
    }

    private static void RefreshAfterChange(GameManager gm)
    {
        gm.RefreshSpecializedUnitLists();
        gm.UpdateUnitLists();
        gm.fogOfWarManager?.OnUnitMoved();
        EmpowermentRuntime.RefreshAll(gm);
    }
}
