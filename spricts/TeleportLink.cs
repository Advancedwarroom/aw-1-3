// TeleportLink.cs - N屏地图传送链路定义
// N:M：同一个源格可以有多条出链（一按多应），一个目标格也可被多个源格指向
// oneWay=true 单向传送；oneWay=false 互传（保存时自动生成反向链）
using Godot;

public class TeleportLink
{
    public Vector2I from = Vector2I.Zero;   // 源格
    public Vector2I to = Vector2I.Zero;     // 目标格
    public bool oneWay = true;              // 单向；互传=保存时补一条反向链

    public TeleportLink() { }

    public TeleportLink(Vector2I f, Vector2I t, bool one)
    {
        from = f;
        to = t;
        oneWay = one;
    }
}
