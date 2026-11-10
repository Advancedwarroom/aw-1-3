// Infantry.cs - 统一弹药系统
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// Infantry · Ammo（统一弹药系统）
// 由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注
// ✅ 设计：所有弹药（主武器/自爆/照明/隐身弹/显形弹/迷彩弹/生产弹/建造弹）
//    统一走本文件的读写接口，任何模块新增弹药种类只需在 AmmoKind 增加枚举 + 两个 switch 补齐。
public partial class Infantry
{
    /// <summary>弹药种类（统一弹药系统）</summary>
    public enum AmmoKind
    {
        Primary,        // 主武器弹药
        Explode,        // 自爆弹
        Flare,          // 照明弹
        StealthBullet,  // 隐身弹（潜艇=下潜弹）
        RevealBullet,   // 显形弹（潜艇=上浮弹）
        CamoBullet,     // 迷彩弹
        Production,     // 生产弹药（生产模块）
        Construction,   // 建造弹药（建造模块）
        Special         // 特殊弹（第11资源，自费补给模块货币槽；useSpecialAmmo=false或上限0=无限）
    }

    public static readonly AmmoKind[] AllAmmoKinds =
        (AmmoKind[])Enum.GetValues(typeof(AmmoKind));

    // ========== 读取 ==========

    public int GetAmmo(AmmoKind kind) => kind switch
    {
        AmmoKind.Primary => currentPrimaryAmmo,
        AmmoKind.Explode => currentExplodeAmmo,
        AmmoKind.Flare => currentFlareAmmo,
        AmmoKind.StealthBullet => currentStealthBullets,
        AmmoKind.RevealBullet => currentRevealBullets,
        AmmoKind.CamoBullet => currentCamoBullets,
        AmmoKind.Production => currentProductionAmmo,
        AmmoKind.Construction => currentConstructionAmmo,
        AmmoKind.Special => currentSpecialAmmo,
        _ => 0
    };

    public int GetMaxAmmo(AmmoKind kind) => kind switch
    {
        AmmoKind.Primary => GetEffectiveMaxPrimaryAmmo(),
        AmmoKind.Explode => maxExplodeAmmo,
        AmmoKind.Flare => maxFlareAmmo,
        AmmoKind.StealthBullet => GetEffectiveMaxStealthBullets(),
        AmmoKind.RevealBullet => GetEffectiveMaxRevealBullets(),
        AmmoKind.CamoBullet => GetEffectiveMaxCamoBullets(),
        AmmoKind.Production => maxProductionAmmo,
        AmmoKind.Construction => maxConstructionAmmo,
        AmmoKind.Special => maxSpecialAmmo,
        _ => 0
    };

    // ========== 写入（统一入口，自动夹取 0..上限） ==========

    public void AddAmmo(AmmoKind kind, int amount)
    {
        if (amount == 0) return;
        int max = GetMaxAmmo(kind);
        int value = Mathf.Clamp(GetAmmo(kind) + amount, 0, Math.Max(0, max));
        SetAmmoRaw(kind, value);
    }

    /// <summary>是否持有至少 need 发弹药</summary>
    public bool HasAmmo(AmmoKind kind, int need = 1) => GetAmmo(kind) >= need;

    /// <summary>尝试消耗 amount 发弹药：不足则拒绝并返回 false（不扣）</summary>
    public bool TryConsumeAmmo(AmmoKind kind, int amount = 1)
    {
        if (amount <= 0) return true;
        if (GetAmmo(kind) < amount) return false;
        AddAmmo(kind, -amount);
        return true;
    }

    /// <summary>补满单种弹药</summary>
    public void RefillAmmo(AmmoKind kind)
    {
        int max = GetMaxAmmo(kind);
        if (max > 0) SetAmmoRaw(kind, max);
    }

    /// <summary>补满所有种类弹药（补给单位/设施通用）</summary>
    public void RefillAllAmmo()
    {
        foreach (var kind in AllAmmoKinds) RefillAmmo(kind);
    }

    /// <summary>✦优化：读取单种弹药上限基础值（不含雾战等加成；地形最大值变化模块用）</summary>
    public int GetBaseMaxAmmo(AmmoKind kind) => kind switch
    {
        AmmoKind.Primary => maxPrimaryAmmo,
        AmmoKind.Explode => maxExplodeAmmo,
        AmmoKind.Flare => maxFlareAmmo,
        AmmoKind.StealthBullet => maxStealthBullets,
        AmmoKind.RevealBullet => maxRevealBullets,
        AmmoKind.CamoBullet => maxCamoBullets,
        AmmoKind.Production => maxProductionAmmo,
        AmmoKind.Construction => maxConstructionAmmo,
        AmmoKind.Special => maxSpecialAmmo,
        _ => 0,
    };

    /// <summary>✦优化：写入单种弹药上限基础值（夹取>=0；地形最大值变化模块用）</summary>
    public void SetMaxAmmoRaw(AmmoKind kind, int value)
    {
        value = Mathf.Max(0, value);
        switch (kind)
        {
            case AmmoKind.Primary: maxPrimaryAmmo = value; break;
            case AmmoKind.Explode: maxExplodeAmmo = value; break;
            case AmmoKind.Flare: maxFlareAmmo = value; break;
            case AmmoKind.StealthBullet: maxStealthBullets = value; break;
            case AmmoKind.RevealBullet: maxRevealBullets = value; break;
            case AmmoKind.CamoBullet: maxCamoBullets = value; break;
            case AmmoKind.Production: maxProductionAmmo = value; break;
            case AmmoKind.Construction: maxConstructionAmmo = value; break;
            case AmmoKind.Special: maxSpecialAmmo = value; break;
        }
    }

    public void SetAmmoRaw(AmmoKind kind, int value) // ✦优化：改为public供地形效果模块使用
    {
        switch (kind)
        {
            case AmmoKind.Primary: currentPrimaryAmmo = value; break;
            case AmmoKind.Explode: currentExplodeAmmo = value; break;
            case AmmoKind.Flare: currentFlareAmmo = value; break;
            case AmmoKind.StealthBullet: currentStealthBullets = value; break;
            case AmmoKind.RevealBullet: currentRevealBullets = value; break;
            case AmmoKind.CamoBullet: currentCamoBullets = value; break;
            case AmmoKind.Production: currentProductionAmmo = value; break;
            case AmmoKind.Construction: currentConstructionAmmo = value; break;
            case AmmoKind.Special: currentSpecialAmmo = value; break;
        }
    }
}
