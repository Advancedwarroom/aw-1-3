// ExtraSave.RuntimeRestore.cs - 读档时恢复运输/兵器庇护关系。
using Godot;
using System.Linq;

public partial class ExtraSave
{
    public void RestoreCarriedUnitAfterLoad(SavedUnitData data, GameManager gm)
    {
        if (data?.fieldValues == null || gm?.unitManager == null) return;
        if (!data.fieldValues.TryGetValue("transportHostKind", out string kind)) return;
        data.fieldValues.TryGetValue("transportHostName", out string hostName);
        var unit = gm.unitManager.AllUnits.LastOrDefault(u => u != null && u.Name == data.displayName);
        if (unit == null) return;
        if (kind == "Unit")
        {
            var host = gm.unitManager.AllUnits.FirstOrDefault(u => u != null && u.Name == hostName);
            host?.LoadUnit(unit);
        }
        else if (kind == "Weapon")
        {
            var host = gm.weaponManager?.AllWeapons.FirstOrDefault(w => w != null && w.Name == hostName);
            host?.LoadShelterUnit(unit);
        }
    }
}
