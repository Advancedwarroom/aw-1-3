using Godot;

// Infantry · PrepHook（准备/冷却模块接入：弹药表第15种冷却水桶）
// 引擎与判定全在 PrepModule/PrepModule.Engine.cs；本文件只提供桶字段与通用查询，老单位零挂接零变化。
public partial class Infantry
{
    [ExportGroup("冷却水桶（弹药表第15种）")]
    [Export] public int maxPrepBucket = 0;        // 桶上限（0=桶关闭；不配桶也能用纯冷却/哑火/事件规则）
    [Export] public int currentPrepBucket = 0;    // 现有库存（走存档/补给/自费补给全套弹药管线）

    /// <summary>本单位的准备模块（没挂=返回null=全场放行）</summary>
    public PrepModule GetPrepModule() => PrepEngine.Get(this);

    /// <summary>固定炮台锁：有存货且无移动行许可时锁移动（ShowMenu/TryQuickMove 用）</summary>
    public bool PrepMovementLocked() => PrepEngine.MovementLocked(this);
}
