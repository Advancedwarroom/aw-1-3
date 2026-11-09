// Weapon.Shelter.cs - 兵器庇护：单位可进入兵器；内部服务与单位载具共用映射层。
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class Infantry
{
    public Weapon shelteredByWeapon = null;
}

public partial class Weapon
{
    [ExportGroup("庇护 / 兵器进驻")]
    [Export] public bool canShelterUnits = false;
    [Export] public int shelterCapacity = 1;
    [Export] public Godot.Collections.Array<string> shelterUnitTypes = new();
    [Export] public Godot.Collections.Array<UnitCategory> shelterUnitCategories = new();
    public List<Infantry> shelteredUnits = new();

    [ExportGroup("庇护：内部服务Export映射")]
    [Export] public bool internalExportServiceEnabled = false;
    [Export] public Godot.Collections.Array<string> internalExportServiceWhitelist = new();
    [Export] public Godot.Collections.Dictionary<string, string> internalExportServiceOverrides = new();
    [Export] public Godot.Collections.Dictionary<string, float> internalExportServiceDeltas = new();

    public virtual bool CanShelterUnit(Infantry unit)
    {
        if (!canShelterUnits || shelterCapacity <= 0 || unit == null || !IsInstanceValid(unit)) return false;
        if (unit.isTransported || unit.shelteredByWeapon != null || unit.isMultiTileUnit) return false;
        if (shelteredUnits.Count >= shelterCapacity) return false;
        if (unit.team != team && team != TeamHelper.Player) return false;
        if (unit.canTransportUnits && unit.transportedUnits.Count > 0) return false;
        if (shelterUnitTypes.Count > 0)
            return shelterUnitTypes.Contains(unit.UnitTypeId) || shelterUnitTypes.Contains(unit.TemplateId) || shelterUnitTypes.Contains(unit.GetType().Name);
        if (shelterUnitCategories.Count > 0)
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm != null && gm.unitCategories.TryGetValue(unit, out var category) && !shelterUnitCategories.Contains(category)) return false;
        }
        return true;
    }

    public static Weapon FindShelterFor(IEnumerable<Weapon> weapons, Infantry rider)
    {
        if (weapons == null || rider == null) return null;
        Weapon publicShelter = null;
        foreach (var weapon in weapons)
        {
            if (weapon == null || !IsInstanceValid(weapon) || !weapon.CanShelterUnit(rider)) continue;
            if (weapon.team == rider.team) return weapon;
            if (publicShelter == null && weapon.team == TeamHelper.Player) publicShelter = weapon;
        }
        return publicShelter;
    }

    public virtual bool LoadShelterUnit(Infantry unit)
    {
        if (!CanShelterUnit(unit)) return false;
        shelteredUnits.Add(unit);
        unit.isTransported = true;
        unit.shelteredByWeapon = this;
        unit.Hide();
        unit.ProcessMode = ProcessModeEnum.Disabled;
        if (unit.grid != null)
        {
            unit.grid.infantries.Remove(unit);
            if (unit.grid.infantry == unit) unit.grid.infantry = unit.grid.infantries.FirstOrDefault();
            unit.grid = null;
        }
        unit.ClearFootprint();
        ApplyShelterExportService(unit);
        (GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager)?.fogOfWarManager?.OnUnitMoved();
        return true;
    }

    public bool CanUnloadShelterUnitToGrid(Infantry unit, Grids targetGrid)
    {
        if (unit == null || targetGrid == null || !shelteredUnits.Contains(unit)) return false;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (targetGrid.PreventsUnitStop(gm?.gridManager)) return false;
        if (unit.GetMoveCostOnGrid(targetGrid) >= 999) return false;
        return !targetGrid.infantries.Any(u => u != null && IsInstanceValid(u) && u.overlapType == UnitOverlapType.NonOverlapping);
    }

    public virtual bool UnloadShelterUnit(Infantry unit, Grids targetGrid)
    {
        if (!CanUnloadShelterUnitToGrid(unit, targetGrid)) return false;
        RemoveShelterExportService(unit);
        shelteredUnits.Remove(unit);
        unit.isTransported = false;
        unit.shelteredByWeapon = null;
        unit.Show();
        unit.ProcessMode = ProcessModeEnum.Inherit;
        unit.Position = targetGrid.Position;
        unit.grid = targetGrid;
        if (!targetGrid.infantries.Contains(unit)) targetGrid.infantries.Add(unit);
        if (targetGrid.infantry == null) targetGrid.infantry = unit;
        unit.isMoved = true;
        unit.isAttacked = true;
        unit.hasActed = true;
        unit.state = UnitState.Acted;
        unit.SetWaitVisual(true);
        unit.StartBreath();
        gm?.unitManager?.BindUnitToGrid(unit, true);
        gm?.fogOfWarManager?.OnUnitMoved();
        return true;
    }

    public void ApplyShelterExportService(Infantry unit)
        => InternalServiceMapper.Apply(this, unit, internalExportServiceEnabled, internalExportServiceWhitelist, internalExportServiceOverrides, internalExportServiceDeltas);

    public void RemoveShelterExportService(Infantry unit) => InternalServiceMapper.Remove(this, unit);

    public void ProcessShelterServices()
    {
        foreach (var unit in shelteredUnits.ToList())
            if (unit != null && IsInstanceValid(unit)) ApplyShelterExportService(unit);
    }

    public void ClearShelterOnDestroyed()
    {
        InternalServiceMapper.RemoveAll(this);
        foreach (var unit in shelteredUnits.ToList())
        {
            if (unit == null || !IsInstanceValid(unit)) continue;
            unit.shelteredByWeapon = null;
            unit.isTransported = false;
            unit.QueueFree();
        }
        shelteredUnits.Clear();
    }
}
