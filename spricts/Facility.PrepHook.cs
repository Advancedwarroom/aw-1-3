using Godot;

// Facility · PrepHook（准备/冷却模块接入：设施自带水桶 + 资格查询）
public partial class Facility
{
    /// <summary>本设施的准备模块（没挂=返回null=全场放行）</summary>
    public PrepModule GetPrepModule() => PrepEngine.Get(this);

    /// <summary>设施操作资格（占领/修理/生产行的装填/冷却/哑火判定）</summary>
    public bool PrepCan(string op) => PrepEngine.CanPerform(this, op);
}
