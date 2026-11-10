using Godot;
using System;

// ============================================================
// DevourModules.cs —— 吞噬回血 / 吞噬消耗（开创人 2026-11-10 定案）
// 适用行为：Oozium 覆盖吞噬（GridManager.DevourUnit）、GrandBolt 蠕动压毁（Wriggle）
// 十资源：血量 / 燃料 / 普通弹 / 自爆弹 / 照明弹 / 隐身弹 / 显形弹 / 迷彩弹 / 生产弹 / 建造弹
// 取值方式（回血与消耗各自独立，各三种）：
//   0=资金价值换算：被吞物当前价值资金 = 被吞物造价 × 当前血/满血；再按本方"该资源满额=造价"线性换算
//   1=百分比（回血=被吞物当前血量%；消耗=本方该资源当前量%）
//   2=固定值
// 正负开关：true=方向取反（回血变自伤 / 消耗变返还）
// 共享 devourOverflowAllowed：效果可否超出上限（回血可超满 / 消耗可扣穿 0）。
//   血量下限仍保 1（不自杀，与隐身日耗 HP 的 Math.Max(1,...) 同一约定）；弹药/燃料开启后可到负数。
// ============================================================

/// <summary>吞噬效果的静态数学工具（两类共用，避免重复实现）</summary>
public static class DevourCalc
{
    // 模式0前置：被吞物当前价值（资金）。满血=全价，残血=折价
    public static long DevouredMoney(Infantry devoured)
    {
        if (devoured == null) return 0;
        int maxHp = Math.Max(1, devoured.maxHealth);
        return (long)Math.Max(0, devoured.cost) * Math.Max(0, devoured.health) / maxHp;
    }

    // 模式0：资金 → 本方某资源量（"该资源满额=造价"的线性换算，四舍五入）
    public static int MoneyToResource(long money, int selfCost, int selfMax)
    {
        if (money <= 0 || selfCost <= 0 || selfMax <= 0) return 0;
        return (int)((money * selfMax + selfCost / 2) / selfCost);
    }

    // 模式1=百分比（base×value%），模式2=固定值
    public static int PercentOrFixed(int mode, int value, int baseAmount)
        => mode == 1 ? baseAmount * Math.Max(0, value) / 100 : Math.Max(0, value);
}

public partial class Infantry
{
    [ExportGroup("吞噬回血模块")]
    [Export] public bool devourHealEnabled = false;                 // ① 是否开启（默认关=原作纯吞噬）
    [Export(PropertyHint.Enum, "资金价值换算,目标血量百分比,固定值")]
    public int devourHealMode = 2;                                  // ② 回血方式
    [Export] public int devourHealValue = 0;                        // ③ 数值：模式1=被吞物血量百分比(0-100)；模式2=固定资源量；模式0忽略
    [Export] public bool devourHealNegative = false;                // ④ 是正是负：true=反扣（吞噬自伤）
    [ExportGroup("吞噬回血·作用资源（勾选生效）")]
    [Export] public bool devourHealHp = true;
    [Export] public bool devourHealFuel = false;
    [Export] public bool devourHealAmmoPrimary = false;
    [Export] public bool devourHealAmmoExplode = false;
    [Export] public bool devourHealAmmoFlare = false;
    [Export] public bool devourHealAmmoStealth = false;
    [Export] public bool devourHealAmmoReveal = false;
    [Export] public bool devourHealAmmoCamo = false;
    [Export] public bool devourHealAmmoProduction = false;
    [Export] public bool devourHealAmmoConstruction = false;
    [ExportGroup("吞噬消耗模块")]
    [Export] public bool devourCostEnabled = false;                   // ⑤ 是否开启（吞一次付代价）
    [Export(PropertyHint.Enum, "资金价值换算,自身当前量百分比,固定值")]
    public int devourCostMode = 2;                                  // ⑥ 消耗方式（同上三种）
    [Export] public int devourCostValue = 0;                        // ⑦ 数值：模式1=本方该资源当前量百分比；模式2=固定量；模式0忽略
    [Export] public bool devourCostNegative = false;                // ⑧ 是正是负：true=负消耗（吞噬返还）
    [ExportGroup("吞噬消耗·作用资源（勾选生效）")]
    [Export] public bool devourCostHp = false;
    [Export] public bool devourCostFuel = false;
    [Export] public bool devourCostAmmoPrimary = false;
    [Export] public bool devourCostAmmoExplode = false;
    [Export] public bool devourCostAmmoFlare = false;
    [Export] public bool devourCostAmmoStealth = false;
    [Export] public bool devourCostAmmoReveal = false;
    [Export] public bool devourCostAmmoCamo = false;
    [Export] public bool devourCostAmmoProduction = false;
    [Export] public bool devourCostAmmoConstruction = false;
    [ExportGroup("吞噬效果·共享")]
    [Export] public bool devourOverflowAllowed = false;             // ⑨ 回血④与消耗⑨合并：可否超出上限（回血超满 / 消耗扣穿0）
    [Export] public bool devourHpFloorLocked = true;              // ⑩ 血量下限是否锁死：true=保1不自杀（与隐身日耗同约定）；false=解锁，吞噬代价/反扣血可致死（走尸化/野化→标准死亡管线）

    // ============ 入口：一次吞噬的总结算（GridManager.DevourUnit / GrandBolt.Wriggle / GrandBolt.ProduceOozium 调用） ============
    public void ApplyDevourEffects(Infantry devoured)
    {
        if (devoured == null || !IsInstanceValid(devoured)) return;
        if (devourHealEnabled) ApplyDevourHeal(devoured);
        if (devourCostEnabled) ApplyDevourCost(devoured);
        if (devourHealEnabled && devourHealHp) UpdateHpLabel();
        // ✅ 血量下限解锁：吞噬自伤致死——走标准死亡管线（TryFlee 内尸化/野化拦截生效）
        if (!devourHpFloorLocked && health <= 0)
        {
            health = 0;
            UpdateHpLabel();
            if (!TryFlee())
            {
                gameManager?.RemovePiece(this);
                CallDeferred(Node.MethodName.QueueFree);
            }
        }
    }

    private void ApplyDevourHeal(Infantry devoured)
    {
        long money = DevourCalc.DevouredMoney(devoured);
        if (devourHealHp)
            DevourHealRes(devourHealMode, devourHealValue, money, devoured.health, health, maxHealth, cost, isHp: true, v => health = v);
        if (devourHealFuel)
            DevourHealRes(devourHealMode, devourHealValue, money, devoured.health, fuel, maxFuel, cost, isHp: false, v => fuel = v);
        if (devourHealAmmoPrimary)
            DevourHealRes(devourHealMode, devourHealValue, money, devoured.health, currentPrimaryAmmo, maxPrimaryAmmo, cost, isHp: false, v => currentPrimaryAmmo = v);
        if (devourHealAmmoExplode)
            DevourHealRes(devourHealMode, devourHealValue, money, devoured.health, currentExplodeAmmo, maxExplodeAmmo, cost, isHp: false, v => currentExplodeAmmo = v);
        if (devourHealAmmoFlare)
            DevourHealRes(devourHealMode, devourHealValue, money, devoured.health, currentFlareAmmo, maxFlareAmmo, cost, isHp: false, v => currentFlareAmmo = v);
        if (devourHealAmmoStealth)
            DevourHealRes(devourHealMode, devourHealValue, money, devoured.health, currentStealthBullets, maxStealthBullets, cost, isHp: false, v => currentStealthBullets = v);
        if (devourHealAmmoReveal)
            DevourHealRes(devourHealMode, devourHealValue, money, devoured.health, currentRevealBullets, maxRevealBullets, cost, isHp: false, v => currentRevealBullets = v);
        if (devourHealAmmoCamo)
            DevourHealRes(devourHealMode, devourHealValue, money, devoured.health, currentCamoBullets, maxCamoBullets, cost, isHp: false, v => currentCamoBullets = v);
        if (devourHealAmmoProduction)
            DevourHealRes(devourHealMode, devourHealValue, money, devoured.health, currentProductionAmmo, maxProductionAmmo, cost, isHp: false, v => currentProductionAmmo = v);
        if (devourHealAmmoConstruction)
            DevourHealRes(devourHealMode, devourHealValue, money, devoured.health, currentConstructionAmmo, maxConstructionAmmo, cost, isHp: false, v => currentConstructionAmmo = v);
    }

    private void DevourHealRes(int mode, int value, long money, int devouredHp, int cur, int max, int selfCost, bool isHp, Action<int> set)
    {
        int amount = mode == 0 ? DevourCalc.MoneyToResource(money, selfCost, max)
                               : DevourCalc.PercentOrFixed(mode, value, devouredHp);
        if (devourHealNegative) amount = -amount;
        int next = cur + amount;
        if (!devourOverflowAllowed && next > max) next = max;
        int floor = isHp ? (devourHpFloorLocked ? 1 : int.MinValue + 1)   // HP：锁死保1；解锁可击穿（致死由入口统一处理）
                         : (devourOverflowAllowed ? int.MinValue + 1 : 0);   // 弹药/燃料：未开溢出钳0，开了允许负数
        if (next < floor) next = floor;
        set(next);
    }

    private void ApplyDevourCost(Infantry devoured)
    {
        long money = DevourCalc.DevouredMoney(devoured);
        if (devourCostHp)
            DevourCostRes(devourCostMode, devourCostValue, money, health, maxHealth, cost, isHp: true, v => health = v);
        if (devourCostFuel)
            DevourCostRes(devourCostMode, devourCostValue, money, fuel, maxFuel, cost, isHp: false, v => fuel = v);
        if (devourCostAmmoPrimary)
            DevourCostRes(devourCostMode, devourCostValue, money, currentPrimaryAmmo, maxPrimaryAmmo, cost, isHp: false, v => currentPrimaryAmmo = v);
        if (devourCostAmmoExplode)
            DevourCostRes(devourCostMode, devourCostValue, money, currentExplodeAmmo, maxExplodeAmmo, cost, isHp: false, v => currentExplodeAmmo = v);
        if (devourCostAmmoFlare)
            DevourCostRes(devourCostMode, devourCostValue, money, currentFlareAmmo, maxFlareAmmo, cost, isHp: false, v => currentFlareAmmo = v);
        if (devourCostAmmoStealth)
            DevourCostRes(devourCostMode, devourCostValue, money, currentStealthBullets, maxStealthBullets, cost, isHp: false, v => currentStealthBullets = v);
        if (devourCostAmmoReveal)
            DevourCostRes(devourCostMode, devourCostValue, money, currentRevealBullets, maxRevealBullets, cost, isHp: false, v => currentRevealBullets = v);
        if (devourCostAmmoCamo)
            DevourCostRes(devourCostMode, devourCostValue, money, currentCamoBullets, maxCamoBullets, cost, isHp: false, v => currentCamoBullets = v);
        if (devourCostAmmoProduction)
            DevourCostRes(devourCostMode, devourCostValue, money, currentProductionAmmo, maxProductionAmmo, cost, isHp: false, v => currentProductionAmmo = v);
        if (devourCostAmmoConstruction)
            DevourCostRes(devourCostMode, devourCostValue, money, currentConstructionAmmo, maxConstructionAmmo, cost, isHp: false, v => currentConstructionAmmo = v);
    }

    private void DevourCostRes(int mode, int value, long money, int cur, int max, int selfCost, bool isHp, Action<int> set)
    {
        int amount = mode == 0 ? DevourCalc.MoneyToResource(money, selfCost, max)
                               : DevourCalc.PercentOrFixed(mode, value, cur);
        if (devourCostNegative) amount = -amount;
        int next = cur - amount;
        if (!devourOverflowAllowed && next > max) next = max;
        int floor = isHp ? (devourHpFloorLocked ? 1 : int.MinValue + 1)
                         : (devourOverflowAllowed ? int.MinValue + 1 : 0);
        if (next < floor) next = floor;
        set(next);
    }
}

public partial class Weapon
{
    [ExportGroup("吞噬回血模块")]
    [Export] public bool devourHealEnabled = false;
    [Export(PropertyHint.Enum, "资金价值换算,目标血量百分比,固定值")]
    public int devourHealMode = 2;
    [Export] public int devourHealValue = 0;
    [Export] public bool devourHealNegative = false;
    [ExportGroup("吞噬回血·作用资源（勾选生效）")]
    [Export] public bool devourHealHp = true;
    [Export] public bool devourHealFuel = false;                      // 兵器无燃料字段→勾选无效（仅占位统一面板）
    [Export] public bool devourHealAmmoPrimary = false;
    [Export] public bool devourHealAmmoExplode = false;
    [Export] public bool devourHealAmmoFlare = false;
    [Export] public bool devourHealAmmoStealth = false;
    [Export] public bool devourHealAmmoReveal = false;
    [Export] public bool devourHealAmmoCamo = false;
    [Export] public bool devourHealAmmoProduction = false;            // 兵器无生产弹字段→勾选无效
    [Export] public bool devourHealAmmoConstruction = false;          // 兵器无建造弹字段→勾选无效
    [ExportGroup("吞噬消耗模块")]
    [Export] public bool devourCostEnabled = false;
    [Export(PropertyHint.Enum, "资金价值换算,自身当前量百分比,固定值")]
    public int devourCostMode = 2;
    [Export] public int devourCostValue = 0;
    [Export] public bool devourCostNegative = false;
    [ExportGroup("吞噬消耗·作用资源（勾选生效）")]
    [Export] public bool devourCostHp = false;
    [Export] public bool devourCostFuel = false;
    [Export] public bool devourCostAmmoPrimary = false;
    [Export] public bool devourCostAmmoExplode = false;
    [Export] public bool devourCostAmmoFlare = false;
    [Export] public bool devourCostAmmoStealth = false;
    [Export] public bool devourCostAmmoReveal = false;
    [Export] public bool devourCostAmmoCamo = false;
    [Export] public bool devourCostAmmoProduction = false;
    [Export] public bool devourCostAmmoConstruction = false;
    [ExportGroup("吞噬效果·共享")]
    [Export] public bool devourOverflowAllowed = false;
    [Export] public bool devourHpFloorLocked = true;              // 血量下限是否锁死：false=解锁后吞噬自伤致死（走 TakeDamage 销毁管线，护盾/锁血拦截天然生效）

    // ============ 入口：蠕动压毁/生产覆盖=吞噬（GrandBolt.Wriggle / ProduceOozium 调用） ============
    public void ApplyDevourEffects(Infantry devoured)
    {
        if (devoured == null || !IsInstanceValid(devoured)) return;
        if (devourHealEnabled) ApplyDevourHeal(devoured);
        if (devourCostEnabled) ApplyDevourCost(devoured);
        // ✅ 血量下限解锁：自伤致死交给兵器标准受伤管线（isDestroyed/护盾/锁血一致处理）
        if (!devourHpFloorLocked && health <= 0) { health = 0; TakeDamage(1); }
    }

    private void ApplyDevourHeal(Infantry devoured)
    {
        long money = DevourCalc.DevouredMoney(devoured);
        if (devourHealHp)
            DevourHealRes(devourHealMode, devourHealValue, money, devoured.health, health, maxHealth, cost, isHp: true, v => health = v);
        if (devourHealAmmoPrimary)
            DevourHealRes(devourHealMode, devourHealValue, money, devoured.health, currentAmmo, maxAmmo, cost, isHp: false, v => currentAmmo = v);
        if (devourHealAmmoExplode)
            DevourHealRes(devourHealMode, devourHealValue, money, devoured.health, wExplodeCurrent, wExplodeMax, cost, isHp: false, v => wExplodeCurrent = v);
        if (devourHealAmmoFlare)
            DevourHealRes(devourHealMode, devourHealValue, money, devoured.health, wFlareCurrent, wFlareMax, cost, isHp: false, v => wFlareCurrent = v);
        if (devourHealAmmoStealth)
            DevourHealRes(devourHealMode, devourHealValue, money, devoured.health, wStealthBulletCurrent, wStealthBulletMax, cost, isHp: false, v => wStealthBulletCurrent = v);
        if (devourHealAmmoReveal)
            DevourHealRes(devourHealMode, devourHealValue, money, devoured.health, wRevealBulletCurrent, wRevealBulletMax, cost, isHp: false, v => wRevealBulletCurrent = v);
        if (devourHealAmmoCamo)
            DevourHealRes(devourHealMode, devourHealValue, money, devoured.health, wCamoBulletCurrent, wCamoBulletMax, cost, isHp: false, v => wCamoBulletCurrent = v);
        // Fuel / Production / Construction：兵器无对应字段，勾选静默无效
    }

    private void DevourHealRes(int mode, int value, long money, int devouredHp, int cur, int max, int selfCost, bool isHp, Action<int> set)
    {
        int amount = mode == 0 ? DevourCalc.MoneyToResource(money, selfCost, max)
                               : DevourCalc.PercentOrFixed(mode, value, devouredHp);
        if (devourHealNegative) amount = -amount;
        int next = cur + amount;
        if (!devourOverflowAllowed && next > max) next = max;
        int floor = isHp ? (devourHpFloorLocked ? 1 : int.MinValue + 1)
                         : (devourOverflowAllowed ? int.MinValue + 1 : 0);
        if (next < floor) next = floor;
        set(next);
    }

    private void ApplyDevourCost(Infantry devoured)
    {
        long money = DevourCalc.DevouredMoney(devoured);
        if (devourCostHp)
            DevourCostRes(devourCostMode, devourCostValue, money, health, maxHealth, cost, isHp: true, v => health = v);
        if (devourCostAmmoPrimary)
            DevourCostRes(devourCostMode, devourCostValue, money, currentAmmo, maxAmmo, cost, isHp: false, v => currentAmmo = v);
        if (devourCostAmmoExplode)
            DevourCostRes(devourCostMode, devourCostValue, money, wExplodeCurrent, wExplodeMax, cost, isHp: false, v => wExplodeCurrent = v);
        if (devourCostAmmoFlare)
            DevourCostRes(devourCostMode, devourCostValue, money, wFlareCurrent, wFlareMax, cost, isHp: false, v => wFlareCurrent = v);
        if (devourCostAmmoStealth)
            DevourCostRes(devourCostMode, devourCostValue, money, wStealthBulletCurrent, wStealthBulletMax, cost, isHp: false, v => wStealthBulletCurrent = v);
        if (devourCostAmmoReveal)
            DevourCostRes(devourCostMode, devourCostValue, money, wRevealBulletCurrent, wRevealBulletMax, cost, isHp: false, v => wRevealBulletCurrent = v);
        if (devourCostAmmoCamo)
            DevourCostRes(devourCostMode, devourCostValue, money, wCamoBulletCurrent, wCamoBulletMax, cost, isHp: false, v => wCamoBulletCurrent = v);
    }

    private void DevourCostRes(int mode, int value, long money, int cur, int max, int selfCost, bool isHp, Action<int> set)
    {
        int amount = mode == 0 ? DevourCalc.MoneyToResource(money, selfCost, max)
                               : DevourCalc.PercentOrFixed(mode, value, cur);
        if (devourCostNegative) amount = -amount;
        int next = cur - amount;
        if (!devourOverflowAllowed && next > max) next = max;
        int floor = isHp ? (devourHpFloorLocked ? 1 : int.MinValue + 1)
                         : (devourOverflowAllowed ? int.MinValue + 1 : 0);
        if (next < floor) next = floor;
        set(next);
    }
}
