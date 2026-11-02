using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · Combat（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{
    [ExportGroup("补给")]
    [Export] public string[] excludedWeaponTargets = System.Array.Empty<string>();  // ⚔ 兵器攻击准入(攻击方)：不能攻击的兵器类型，"Weapon"=全部兵器，留空=均可攻击              // ✅ 合流模块：是否允许参与 Join 合流（false=该单位不可合流）




// 防御力（动态衰减：baseDefense × 血量% + 地形加成）
public int GetEffectiveDefense(Grids targetGrid)
{
    float healthPercent = (float)health / GetEffectiveMaxHealth();
    int dynamicDefense = Mathf.RoundToInt(baseDefense * healthPercent);
    // 群体狂热：固定防御力加成（百分比乘区；负=减成，下限0）
    if (packBoostDefense && PackFrenzyActive())
        dynamicDefense = dynamicDefense * Mathf.Max(0, 100 + packBonusPercent) / 100;
    float terrainBonus = (defenseBonusType == DefenseBonusType.CanDefenseBonus && targetGrid != null
            && (targetGrid.city == null || !targetGrid.city.HasStatus(FacilityStatusType.NoCover)))
        ? targetGrid.TerrainDefenseBonus * Mathf.Max(0, 100 + (targetGrid.city?.defenseSuppressPercent ?? 0)) / 100f 
        : 0f;
    int result = Mathf.RoundToInt(dynamicDefense * (1.0f + terrainBonus));
    if (FogActive()) result = Mathf.Max(0, result + fogFlatDefenseBonus);   // ✅ 雾战固定防御（可负）
    return result;
}

public virtual int CalculateFinalDamage(Infantry target, WeaponType weaponType, bool isCounter = false)
{
    int baseDamage = 0;
    
    if (weaponType == WeaponType.Primary)
        baseDamage = GetPrimaryDamageFromMatrix(target);
    else if (weaponType == WeaponType.Secondary)
        baseDamage = GetSecondaryDamageFromMatrix(target);
    
    // 群体狂热：真实攻击力加成（攻防表额外加成；负=减成，下限0）
    if (packBoostAttack && PackFrenzyActive())
        baseDamage = baseDamage * Mathf.Max(0, 100 + packBonusPercent) / 100;

    if (baseDamage <= 0) return 0;
    
    // 攻击方血量系数
    float attackerHealthPercent = (float)health / GetEffectiveMaxHealth();
    float actualAttack = baseDamage * attackerHealthPercent;

    // ✅ CO 模块系统·模块1（伤害加成）+ 天气系统·模块15（沙暴-15%）
    float coOffBonus = CoModuleSystem.GetOffenseBonusPercent(this) + WeatherSystem.GetOffenseModifierPercent(this);
    actualAttack *= Mathf.Max(0f, 100f + coOffBonus) / 100f;

    // 地形
    float terrainDefense = (target.defenseBonusType == DefenseBonusType.CanDefenseBonus && target.grid != null
            && (target.grid.city == null || !target.grid.city.HasStatus(FacilityStatusType.NoCover)))
        ? target.grid.TerrainDefenseBonus * Mathf.Max(0, 100 + (target.grid.city?.defenseSuppressPercent ?? 0)) / 100f
        : 0f;
    // ✅ CO 模块系统·模块2（防御加成）：防守方 CO 地形防御星加成
    terrainDefense = Mathf.Max(0f, terrainDefense + CoModuleSystem.GetTerrainDefenseBonus(target) / 100f);
    float terrainMultiplier = 1.0f - terrainDefense;
    float afterTerrain = actualAttack * terrainMultiplier;

    // ✅ CO 模块系统·模块2（防御加成）：防守方 CO 减伤百分比
    afterTerrain *= Mathf.Max(0.05f, 100f - CoModuleSystem.GetDefenseBonusPercent(target)) / 100f;

    // 防御阈值
    int effectiveDefense = target.GetEffectiveDefense(target.grid);
    float rawDamage = afterTerrain - effectiveDefense;

    if (rawDamage <= 0)
    {
        return 0;
    }

    // 反击乘系数
    if (isCounter)
        rawDamage *= counterMul;

    // ✅ CO 模块系统·模块13（运气加成）：最终伤害随机浮动（±luck 区间）
    int luckRoll = CoModuleSystem.GetLuckRoll(this);
    return Mathf.Max(1, Mathf.RoundToInt(rawDamage) + luckRoll);
}

// 反击
public virtual int CalculateCounterDamage(Infantry attacker)
{
    WeaponType weapon = SelectWeaponByMatrix(attacker);
    return CalculateFinalDamage(attacker, weapon, isCounter: true);
}
    [ExportGroup("主武器配置")]
    [Export] public bool useDefaultAmmoConfig = true;
    [Export] public bool hasPrimaryWeapon = false;           // 是否有主武器
    [Export] public bool primaryHasLimitedAmmo = true;       // 主武器是否有限弹药（false=无限）
    [Export] public int maxPrimaryAmmo = 0;                  // 最大弹药（0=无武器，>0=有限，>=99=无限）
    [Export] public int currentPrimaryAmmo = 0;              // 当前弹药

// ========== 副武器配置 ==========
    [ExportGroup("副武器配置")]
    [Export] public bool hasSecondaryWeapon = true;          // 是否有副武器
    [Export] public int secondaryAttack = 100;               // 副武器攻击力

// ========== 武器效果配置 ==========
    [ExportGroup("武器效果")]
    [Export] public bool primaryAntiArmor = false;           // 主武器对装甲有效
[Export] public bool primaryAntiInfantry = true;         // 主武器对步兵有效
[Export] public bool secondaryAntiArmor = false;         // 副武器对装甲有效
[Export] public bool secondaryAntiInfantry = true;       // 副武器对步兵有效
public virtual bool HasPrimaryWeapon => hasPrimaryWeapon;
public virtual bool HasSecondaryWeapon => hasSecondaryWeapon;
public virtual bool HasLimitedPrimaryAmmo => hasPrimaryWeapon && primaryHasLimitedAmmo && maxPrimaryAmmo < 99;
public virtual bool HasInfinitePrimaryAmmo => hasPrimaryWeapon && (!primaryHasLimitedAmmo || maxPrimaryAmmo >= 99);

public virtual string GetPrimaryAmmoString()
{
    if (!hasPrimaryWeapon) return TranslateGroup.T("无");
    if (HasInfinitePrimaryAmmo) return "∞";
    return $"{currentPrimaryAmmo}/{maxPrimaryAmmo}";
}
public virtual bool CanUsePrimaryWeapon()
{
    if (!hasPrimaryWeapon) return false;
    if (HasInfinitePrimaryAmmo) return true;
    return currentPrimaryAmmo > 0;
}
public virtual bool CanUseSecondaryWeapon()
{
    return hasSecondaryWeapon;
}
// 修改：泛化获取需要弹药的兵器
public virtual List<Weapon> GetWeaponsNeedingAmmo()
{
    var result = new List<Weapon>();
    var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
    if (gm?.weaponManager == null) return result;

    var supplyRange = CalculateSupplyRange();

    foreach (var weapon in gm.weaponManager.AllWeapons)
    {
        if (!IsInstanceValid(weapon)) continue;
        
        // ✅ 模块化：通过 Weapon 基类属性判断，零硬编码适配新武器
        if (weapon.useAmmoSystem && weapon.currentAmmo < weapon.maxAmmo)
        {
            if (weapon.grid != null && supplyRange.Contains(weapon.grid))
                result.Add(weapon);
        }
    }
    return result;
}

public virtual bool ResupplyWeapon(Weapon weapon)
{
    if (weapon == null || !IsInstanceValid(weapon)) return false;

    // ✅ 模块化：直接调用 Weapon 基类的虚方法，零硬编码适配新武器
    bool success = weapon.ResupplyAmmo();
    
    if (success)
    {
    }
    
    return success;
}

public virtual bool ConsumePrimaryAmmo()
{
    if (!hasPrimaryWeapon) return false;
    if (HasInfinitePrimaryAmmo) return true;
    return TryConsumeAmmo(AmmoKind.Primary);   // ✅ 统一弹药系统
}

    public virtual int attack
    {
        get 
        { 
            float healthPercent = (float)health / GetEffectiveMaxHealth();
            return Mathf.Max(0, Mathf.RoundToInt(baseAttack * healthPercent));
        }
    }

    public int defense
    {
        get 
        { 
            float healthPercent = (float)health / GetEffectiveMaxHealth();
            return Mathf.Max(0, Mathf.RoundToInt(baseDefense * healthPercent));
        }
    }

    /// <summary>
    /// 获取实际防御力（包含地形加成，如果允许）
    /// </summary>

    public virtual int secondaryAtk
    {
        get 
        { 
            float healthPercent = (float)health / GetEffectiveMaxHealth();
            return Mathf.Max(0, Mathf.RoundToInt(secondaryAttack * healthPercent));
        }
    }

    // ✅ 兵器攻击准入：双向名单判定（攻击方排除表 + 兵器拒绝表），空表=不限制
    public bool CanTargetWeapon(Weapon w)
    {
        if (w == null || w.isDestroyed) return false;
        string myName = UnitTypeId;
        if (w.excludedAttackerTypes != null && w.excludedAttackerTypes.Length > 0
            && System.Array.IndexOf(w.excludedAttackerTypes, myName) >= 0)
            return false;
        if (excludedWeaponTargets != null && excludedWeaponTargets.Length > 0)
        {
            Type t = w.GetType();
            while (t != null && t != typeof(object))
            {
                if (System.Array.IndexOf(excludedWeaponTargets, t.Name) >= 0) return false;
                t = t.BaseType;
            }
        }
        // ✅ 兵器类别限制：按攻击者空军/陆军/海军/防空分类过滤（可拼装模块）
        if (w.restrictAttackerCategories)
        {
            bool attackerIsAir = IsAirUnit;
            bool attackerIsNaval = moveType == MoveType.Naval;
            bool attackerIsAntiAir = isAntiAirUnit;
            bool attackerIsLand = !attackerIsAir && !attackerIsNaval;

            if (attackerIsAir && !w.allowAttackerAirUnits) return false;
            if (attackerIsNaval && !w.allowAttackerNavalUnits) return false;
            if (attackerIsAntiAir && !w.allowAttackerAntiAirUnits) return false;
            if (attackerIsLand && !w.allowAttackerLandUnits) return false;
        }
        return true;
    }

    public virtual int CalculateDamageAgainstWeapon(Weapon target, WeaponType weaponType)
{
    if (!CanTargetWeapon(target)) return 0;   // ✅ 兵器攻击准入闸门
    // 兵器没有类型名在攻防表中，使用基础攻击力的百分比
    float healthPercent = (float)health / GetEffectiveMaxHealth();
    
    int baseDamage = 0;
    if (weaponType == WeaponType.Primary && hasPrimaryWeapon)
    {
        // 使用主武器基础攻击力
        baseDamage = baseAttack;
    }
    else if (weaponType == WeaponType.Secondary && hasSecondaryWeapon)
    {
        // 使用副武器攻击力
        baseDamage = secondaryAttack;
    }
    
    // 应用血量系数
    int actualDamage = Mathf.Max(1, Mathf.RoundToInt(baseDamage * healthPercent));
    
    // 兵器没有防御力，直接扣血
    return actualDamage;
}

    public virtual void OnAttackSelected()
    {
        if (attackType == AttackType.NoAttack)
            return;

        // ✅ 修复：检查弹药和移动后攻击权限
        if (hasPrimaryWeapon && !CanUsePrimaryWeapon() && !hasSecondaryWeapon)
        {
            return;
        }
        if (!canAttackAfterMoving && state == UnitState.Moved)
        {
            return;
        }

        EnsureGridBound();
        ShowAnimState("attack");
        StopBreath();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.gridManager.ShowAttackRange(this);
    }

    public void AttackTarget(Node target)
    {
        if (target is Infantry infantry)
        {
            Attack(infantry);
        }
        else if (target is Weapon weapon)
        {
            // ✅ 修复1：使用统一的武器检查
            if (!CanUsePrimaryWeapon() && !CanUseSecondaryWeapon()) return;
            if (!canAttackAfterMoving && state == UnitState.Moved) return;
            if (state == UnitState.Acted) return;

            // ✅ 修复2：选择武器（虽然兵器没有类型名，但可以用默认伤害）
            WeaponType selectedWeapon = WeaponType.Primary;
            if (hasPrimaryWeapon && CanUsePrimaryWeapon())
            {
                selectedWeapon = WeaponType.Primary;
                ConsumePrimaryAmmo();
            }
            else if (hasSecondaryWeapon && CanUseSecondaryWeapon())
            {
                selectedWeapon = WeaponType.Secondary;
            }
            else
            {
                return; // 没有可用武器
            }

            isAttacked = true;
            isMoved = true;
            originalGrid = null;

            // ✅ 修复3：使用攻防表计算伤害（兵器没有对应条目，需要处理）
                        if (!CanTargetWeapon(weapon)) return;   // ✅ 准入拒绝
            int damage = CalculateDamageAgainstWeapon(weapon, selectedWeapon);
            weapon.TakeDamage(damage);

            state = UnitState.Acted;
            FreezeToIdleDark();

            // 清理UI
            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.gridManager?.HideAttackRange();
            gm?.ClearSelectedInfantry();
        }
    }

    public virtual bool IsArmoredTarget(Infantry target)
    {
        return false;
    }
public virtual (int damage, string info) CalculateDamagePreview(Infantry target)
{
    WeaponType weapon = SelectWeaponByMatrix(target);
    int damage = CalculateFinalDamage(target, weapon);
    int baseVal = weapon == WeaponType.Primary ? GetPrimaryDamageFromMatrix(target) : GetSecondaryDamageFromMatrix(target);
    
    string weaponName = weapon == WeaponType.Primary ? TranslateGroup.T("主") : TranslateGroup.T("副");
    string info = damage > 0 
        ? $"{weaponName}武:{damage}(基础{baseVal})" 
        : TranslateGroup.T("无法攻击");
    
    return (damage, info);
}

public virtual (int damage, string info) CalculateCounterPreview(Infantry attacker)
{
    if (attackType == AttackType.NoAttack || health <= 0)
        return (0, TranslateGroup.T("无法反击"));
    
    WeaponType counterWeapon = SelectWeaponByMatrix(attacker);
    int damage = CalculateFinalDamage(attacker, counterWeapon, isCounter: true);
    int baseVal = counterWeapon == WeaponType.Primary ? GetPrimaryDamageFromMatrix(attacker) : GetSecondaryDamageFromMatrix(attacker);
    
    return (damage, $"反击({counterWeapon}):{damage}");
}

public virtual void Attack(Infantry target, bool allowCounter = true)
{
        // ✅ 隐身攻击门：白名单外的单位打不了隐身目标（所有攻击路径的唯一闸门）
        if (!CanTargetStealth(this, target)) return;
        if (isStealthed && !canAttackWhileStealthed) return;   // ✅ 隐身行为锁：攻击
    if (attackType == AttackType.NoAttack) return;
    if (isAttacked) return;
    if (!canAttackAfterMoving && state == UnitState.Moved) return;
    if (state == UnitState.Acted) return;
    
    // ✅ 禁止隔空伤害：必须确认目标在攻击范围内
    if (!IsTargetInAttackRange(target))
    {
        return;
    }
    
    // 选择武器（新：攻防表系统）
    WeaponType weapon = SelectWeaponByMatrix(target);
    if (weapon == WeaponType.None) return;
    
    // 消耗弹药（仅限主武器）
    if (weapon == WeaponType.Primary)
        ConsumePrimaryAmmo();
    
    isAttacked = true;
    isMoved = true;
    originalGrid = null;

    // 计算伤害（新：攻防表公式）
    int attackDamage = CalculateFinalDamage(target, weapon);
    int counterDamage = 0;
    
    // 反击
    bool targetWillDie = (target.health - attackDamage) <= 0;
    bool canTargetCounter = !targetWillDie 
        && target.attackType != AttackType.NoAttack
        && target.IsTargetInCounterRange(this)
        && TeamHelper.CanCounterAttack(target.team);

    // ✅ CO 模块系统·模块10（逆转/先制反击）：防守方先手——反击先结算，攻击方按掉血后战力出手
    if (canTargetCounter && CoModuleSystem.GetFirstStrike(target))
    {
        WeaponType fsWeapon = target.SelectWeaponByMatrix(this);
        counterDamage = target.CalculateFinalDamage(this, fsWeapon, isCounter: true);
        if (!healthLocked) this.health -= counterDamage; // 力场锁血
        if (counterDamage > 0)
        {
            // ✅ 模块27：先制反击造成/受到伤害打点
            CoModuleSystem.OnDamageDealt(target.team, counterDamage);
            CoModuleSystem.OnDamageTaken(team, counterDamage);
            ApplyStealthRevealOnDamage(this);
        }
        CheckStealthThresholds();
        this.UpdateHpLabel();
        // 攻击方被先手打死 → 本次攻击取消（AW 先制规则：死人开不了枪）
        if (this.health <= 0 && !TryFlee())
        {
            CoModuleSystem.OnUnitLost(team);
            // ✅ 模块16：先制反击击杀也得 XP
            target.GainXp(CoModuleSystem.GetKillXp(target));
            this.OnDestroyed();
            if (this.grid != null)
            {
                this.grid.infantries.Remove(this);
                if (this.grid.infantry == this)
                    this.grid.infantry = this.grid.infantries.Count > 0 ? this.grid.infantries[0] : null;
                this.grid = null;
            }
            this.CallDeferred("queue_free");
            var gmFs = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            gmFs?.RemovePiece(this);
            return;
        }
        canTargetCounter = false;   // 先制反击已结算，后续不再反击
        counterDamage = 0;
        // ✅ 攻击伤害按攻击方掉血后的战力计算（CalculateFinalDamage 读当前 health）
        attackDamage = CalculateFinalDamage(target, weapon);
    }
    
    // 旧行为（可选）：按挨打前血量算反击
    if (canTargetCounter && target.counterUsePreDamageHp)
    {
        WeaponType counterWeapon = target.SelectWeaponByMatrix(this);
        counterDamage = target.CalculateFinalDamage(this, counterWeapon, isCounter: true);
    }
    
    // 应用伤害
    if (!target.healthLocked) target.health -= attackDamage; // 力场锁血
        // ✅ CO 模块系统·模块27（能量条积累）：造成/受到伤害打点
        CoModuleSystem.OnDamageDealt(team, attackDamage);
        CoModuleSystem.OnDamageTaken(target.team, attackDamage);
        ApplyStealthRevealOnDamage(target);   // ✅ 受伤现形（含阈值模式）
        target.CheckStealthThresholds();   // ✅ 受伤后阈值矩阵重判
        target.SyncDamageRevealTracker();     // ✅ 已按"单位攻击"处理过，广义监听不重复触发

    // ✅ AW 原作：反击按挨打后的血量计算（先手优势）
    if (canTargetCounter && !target.counterUsePreDamageHp)
    {
        WeaponType counterWeapon = target.SelectWeaponByMatrix(this);
        counterDamage = target.CalculateFinalDamage(this, counterWeapon, isCounter: true);
    }
        if (stealthRevealOnAttack) RevealStealth();   // ✅ 攻击后现形（先于反击结算：现形即暴露给反击）
        if (!allowCounter || !CanTargetStealth(target, this) || target.health <= 0) counterDamage = 0;   // ✅ 反击门+死亡消除(AW原教旨)：单向打击/打不了隐身者/被秒者→不还手
    if (!healthLocked) this.health -= counterDamage; // 力场锁血
        if (counterDamage > 0)
        {
            // ✅ 模块27：反击造成/受到伤害打点
            CoModuleSystem.OnDamageDealt(target.team, counterDamage);
            CoModuleSystem.OnDamageTaken(team, counterDamage);
            ApplyStealthRevealOnDamage(this);   // ✅ 反击也是单位攻击：攻击方受伤同样走受伤现形
        }
        CheckStealthThresholds();   // ✅ 反击掉血后阈值矩阵重判
        SyncDamageRevealTracker();
    
    target.UpdateHpLabel();
    this.UpdateHpLabel();
    
    // 日志
    string weaponName = weapon == WeaponType.Primary ? TranslateGroup.T("主") : TranslateGroup.T("副");
    int baseVal = weapon == WeaponType.Primary ? GetPrimaryDamageFromMatrix(target) : GetSecondaryDamageFromMatrix(target);

    // ========== 死亡处理 ==========
    if (target.health <= 0 && !target.TryFlee())
    {
        CoModuleSystem.OnUnitLost(target.team);   // ✅ 模块27：损失单位打点（先于死亡）
        // ✅ 模块16（升级）：击杀得 XP
        this.GainXp(CoModuleSystem.GetKillXp(this));
        target.OnDestroyed();
        if (target.grid != null)
        {
            target.grid.infantries.Remove(target);
            if (target.grid.infantry == target)
                target.grid.infantry = target.grid.infantries.Count > 0 ? target.grid.infantries[0] : null;
            target.grid = null;
        }
        target.CallDeferred("queue_free");
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.RemovePiece(target);
    }
    
    if (this.health <= 0 && !TryFlee())
    {
        CoModuleSystem.OnUnitLost(team);   // ✅ 模块27：损失单位打点
        // ✅ 模块16（升级）：反击击杀也得 XP
        target.GainXp(CoModuleSystem.GetKillXp(target));
        this.OnDestroyed();
        if (this.grid != null)
        {
            this.grid.infantries.Remove(this);
            if (this.grid.infantry == this)
                this.grid.infantry = this.grid.infantries.Count > 0 ? this.grid.infantries[0] : null;
            this.grid = null;
        }
        this.CallDeferred("queue_free");
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.RemovePiece(this);
        return;
    }
    
    state = UnitState.Acted;
    FreezeToIdleDark();
}


public virtual bool IsTargetInCounterRange(Infantry attacker)
{

    if (attacker.cannotCounterWhenAttacked)
        return false;
    
    if (!canCounterWhenDefending)
        return false;
    
    if (!canCounterAtRange)
    {
        if (this.grid == null || attacker.grid == null) return false;
        int distance = Mathf.Abs(this.grid.GridIndex.X - attacker.grid.GridIndex.X) 
                     + Mathf.Abs(this.grid.GridIndex.Y - attacker.grid.GridIndex.Y);
        return distance == 1;
    }
    

    return this.IsTargetInAttackRange(attacker);
}
    public virtual bool IsTargetInAttackRange(Infantry target)
    {
        if (target?.grid == null || this.grid == null) return false;

        int distance = Mathf.Abs(target.grid.GridIndex.X - this.grid.GridIndex.X) 
                     + Mathf.Abs(target.grid.GridIndex.Y - this.grid.GridIndex.Y);

        // ✅ CO 模块系统·模块3（攻击范围加成）+ 天气系统·模块15（沙暴-1）：射程/最小射程增减
        int coRangeBonus = CoModuleSystem.GetFireRangeBonus(this) + WeatherSystem.GetFireRangeModifier(this);
        int coMinBonus = CoModuleSystem.GetMinFireRangeBonus(this);
        if (useMinMaxAttackRange)
            return distance >= Mathf.Max(1, minAttackRange + coMinBonus) && distance <= maxAttackRange + coRangeBonus;
        else
            return distance <= attackRange + coRangeBonus;
    }
    




public virtual bool CanAttackAfterMove()
    {
        if (state == UnitState.Idle) return true;
        return canAttackAfterMoving;
    }




public virtual void ReloadAmmo()
{
    if (hasPrimaryWeapon && primaryHasLimitedAmmo && maxPrimaryAmmo < 99)
    {
        currentPrimaryAmmo = maxPrimaryAmmo;
    }
}
}
