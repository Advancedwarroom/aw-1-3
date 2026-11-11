// Facility.Combat.cs - 独立设施血量、被击范围与伤害表；不读写任何占领/撕旗进度。
using Godot;
public partial class Facility
{
 [ExportGroup("设施血量与被击")]
 [Export] public bool canBeAttacked = false;
 [Export] public int maxHealth = 100;
 [Export] public int health = 100;
 [Export] public bool canBeAttackedOnSameGrid = true;
 [Export] public bool canBeAttackedFromAdjacent = true;
 [Export(PropertyHint.Range,"0,99,1")] public int attackableMinRange = 0;
 [Export(PropertyHint.Range,"0,99,1")] public int attackableMaxRange = 1;
 [ExportGroup("设施伤害表")]
 [Export] public Godot.Collections.Dictionary<string,int> unitDamageTable = new();
 [Export] public Godot.Collections.Dictionary<string,int> weaponDamageTable = new();
 public bool isDestroyed = false;
 public Label hpLabel;
 public bool CanBeAttackedFrom(Grids attackerGrid)
 {
  if (!canBeAttacked || isDestroyed || health<=0 || ParentGrid==null || attackerGrid==null) return false;
  int distance=Mathf.Abs(ParentGrid.GridIndex.X-attackerGrid.GridIndex.X)+Mathf.Abs(ParentGrid.GridIndex.Y-attackerGrid.GridIndex.Y);
  int min=Mathf.Max(0,attackableMinRange), max=Mathf.Max(min,attackableMaxRange);
  if (distance<min || distance>max) return false;
  if (distance==0) return canBeAttackedOnSameGrid;
  if (distance==1) return canBeAttackedFromAdjacent;
  return true;
 }
 public bool CanBeAttackedBy(Infantry attacker)
 { return attacker!=null && CanBeAttackedFrom(attacker.grid) && TeamHelper.IsEnemyForAttacker(attacker.team,facilityTeam,attacker.canAttackFriendlies); }
 private static int FindTableValue(Godot.Collections.Dictionary<string,int> table,params string[] keys)
 { foreach(string key in keys) if (!string.IsNullOrEmpty(key) && table.TryGetValue(key,out int damage)) return damage; return -1; }
 public int CalculateDamageFrom(Infantry attacker,WeaponType weaponType)
 {
  int configured=FindTableValue(unitDamageTable,attacker.UnitTypeId,attacker.GetType().Name,attacker.Name);
  if (configured>=0) return configured;
  int baseDamage=weaponType==WeaponType.Primary && attacker.hasPrimaryWeapon ? attacker.baseAttack : attacker.secondaryAttack;
  float percent=(float)attacker.health/Mathf.Max(1,attacker.GetEffectiveMaxHealth());
  return Mathf.Max(0,Mathf.RoundToInt(baseDamage*percent));
 }
 public int CalculateDamageFrom(Weapon attacker,float fallbackHealthPercent)
 {
  int configured=FindTableValue(weaponDamageTable,attacker.GetType().Name,attacker.Name);
  if (configured>=0) return configured;
  return Mathf.Max(1,Mathf.RoundToInt(maxHealth*Mathf.Abs(fallbackHealthPercent)));
 }
 public virtual void TakeDamage(int damage,Infantry attacker=null,Weapon weaponAttacker=null,bool canDestroy=true)
 {
  if (!canBeAttacked || isDestroyed || health<=0 || damage<=0) return;
  health=canDestroy ? Mathf.Max(0,health-damage) : Mathf.Max(1,health-damage);
  UpdateHpLabel();
  if (health<=0) { isDestroyed=true; OnDestroyed(); }
 }
 public void ApplyWeaponPercentDamage(Weapon attacker,float healthPercent,bool canDestroy,bool canOverMax=false)
 {
  if (!canBeAttacked || isDestroyed || health<=0) return;
  if (healthPercent<0)
  {
   int healing=Mathf.Max(1,Mathf.RoundToInt(maxHealth*Mathf.Abs(healthPercent)));
   health=canOverMax ? health+healing : Mathf.Min(maxHealth,health+healing); UpdateHpLabel(); return;
  }
  TakeDamage(CalculateDamageFrom(attacker,healthPercent),null,attacker,canDestroy);
 }
 public virtual void UpdateHpLabel()
 {
  if (!canBeAttacked || isDestroyed) { if (hpLabel!=null && IsInstanceValid(hpLabel)) hpLabel.Hide(); return; }
  if (hpLabel==null || !IsInstanceValid(hpLabel))
  {
   hpLabel=GetNodeOrNull<Label>("HpLabel");
   if (hpLabel==null)
   { hpLabel=new Label{Name="HpLabel",Position=new Vector2(12,-22),ZIndex=30}; hpLabel.AddThemeFontSizeOverride("font_size",14); AddChild(hpLabel); }
  }
  hpLabel.Text=Mathf.Max(0,health).ToString();
  hpLabel.Modulate=health*3<=maxHealth ? new Color(1f,0.45f,0.45f) : Colors.White; hpLabel.Show();
 }
}
