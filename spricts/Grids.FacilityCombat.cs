// Grids.FacilityCombat.cs - 单位攻击目标查询；设施血量与占领进度完全分离。
public partial class Grids
{
 public bool HasAttackableFacility(Infantry attacker)
 { return city!=null && IsInstanceValid(city) && city.CanBeAttackedBy(attacker); }
}
