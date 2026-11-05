// Infantry.Trap.cs - 卡死规则挂点（issue#1：多格单位内部卡敌→不能移动）
// GrandBolt.holesTrapEnemies 扫描足印内洞，给合围中的敌方单位挂 trappedByWeapon 标记。
// 本文件只加字段与查询助手；拦截点在 Infantry.CanUnitMove 加一行（见 README 补丁段）。
using Godot;

public partial class Infantry
{
    /// <summary>卡死规则：困住本单位的兵器（null=自由）。被合围时由 GrandBolt 回合开始扫描挂/摘。</summary>
    public Weapon trappedByWeapon = null;

    /// <summary>是否被多格兵器困住（不能移动/不能下载卸载以外的任何位移）</summary>
    public bool IsTrappedByWeapon()
    {
        var w = trappedByWeapon;
        if (w == null) return false;
        if (!IsInstanceValid(w) || w.isDestroyed || w.IsQueuedForDeletion()) { trappedByWeapon = null; return false; }
        return true;
    }
}
