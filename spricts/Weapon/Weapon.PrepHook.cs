using Godot;

// Weapon · PrepHook（准备/冷却模块接入：兵器自带水桶 + 资格查询）
// 兵器不是 Infantry，弹药表管线走不通——桶用 PrepModule.moduleBucket/moduleBucketMax。
public partial class Weapon
{
    /// <summary>本兵器的准备模块（没挂=返回null=全场放行）</summary>
    public PrepModule GetPrepModule() => PrepEngine.Get(this);

    /// <summary>开火资格（武器攻击行的装填/冷却/哑火判定）</summary>
    public bool PrepCanFire() => PrepEngine.CanPerform(this, Ops.WeaponAttack);
}
