// GameManager.FactoryLinkage.cs - 兵器/单位/建筑联动系统（超级兵工厂↔管道接缝↔中转口）
// 机制：
// 1. 管道接缝被毁 → 所有联动它的超级兵工厂失去供能变中立废铁（不可生产）
// 2. 管道构造者修复接缝 → 恢复供能判定
// 3. 中转口（Rig建造）完成 → 兵工厂归属改为建造方
using Godot;
using System.Linq;

public partial class GameManager
{
    /// <summary>管道接缝被毁时通知：刷新所有兵工厂的供能状态</summary>
    public void NotifyLinkageSourcesDestroyed(Weapon destroyedSource)
    {
        if (weaponManager == null) return;
        foreach (var w in weaponManager.AllWeapons.ToList())
        {
            if (w is SuperFactory f && IsInstanceValid(w) && !w.IsQueuedForDeletion())
            {
                f.CheckPowerLinkage();
            }
        }
    }

    /// <summary>任意兵器/单位状态变化后的联动刷新（回合流转/修复后调用）</summary>
    public void RefreshFactoryLinkages()
    {
        if (weaponManager == null) return;
        foreach (var w in weaponManager.AllWeapons.ToList())
        {
            if (w is SuperFactory f && IsInstanceValid(w) && !w.IsQueuedForDeletion())
            {
                f.CheckPowerLinkage();
            }
        }
    }

    /// <summary>任意兵器被毁后：刷新黑弧护卫联动（护卫全毁→黑弧坠落）</summary>
    public void NotifyBlackArcEscortCheck()
    {
        if (weaponManager == null) return;
        foreach (var w in weaponManager.AllWeapons.ToList())
        {
            if (w is BlackArc arc && IsInstanceValid(w) && !w.IsQueuedForDeletion())
                arc.CheckEscortLinkage();
        }
    }
}
