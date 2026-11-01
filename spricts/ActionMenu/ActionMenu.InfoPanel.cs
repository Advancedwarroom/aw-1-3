// ActionMenu.cs - 完整修复版，移除所有unloadButton直接引用
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;

// ActionMenu · InfoPanel（由 ActionMenu.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class ActionMenu
{

    public void OnInfoPressed()
    {
        GetViewport().SetInputAsHandled();
        if (currentWeapon != null && currentUnit == null)
        {
            ShowWeaponInfo(currentWeapon);
            return;
        }

        if (currentUnit != null && currentWeapon == null)
        {
            ShowUnitInfo();
            return;
        }

        TranslateGroup.SetText(unitInfoLabel, "未选择单位");
        unitInfoLabel.Visible = true;
        closeInfoButton.Visible = true;
    }

    private void OnLanguageChanged(int langCode)
    {
        // ✅ 语言切换时，如果信息面板开着，重新刷新
        if (unitInfoLabel != null && unitInfoLabel.Visible)
        {
            if (currentUnit != null && currentWeapon == null)
            {
                ShowUnitInfo();
            }
            else if (currentWeapon != null && currentUnit == null)
            {
                ShowWeaponInfo(currentWeapon);
            }
        }
    }

    private void ShowUnitInfo()
    {
        ZIndex = 200;
        if (currentUnit == null) return;

        string infoText = $"{TranslateGroup.T("单位名称")}: {currentUnit.Name}\n";
        infoText += $"{TranslateGroup.T("团队")}: {currentUnit.team}\n";
        infoText += $"{TranslateGroup.T("最大生命值")}: {currentUnit.maxHealth}\n";
        infoText += $"{TranslateGroup.T("生命值")}: {currentUnit.health}\n";
        infoText += $"{TranslateGroup.T("攻击力")}: {currentUnit.baseAttack}\n";
        infoText += $"{TranslateGroup.T("实时主武器攻击力")}: {currentUnit.attack}\n";
        infoText += $"{TranslateGroup.T("实时副武器攻击力")}: {currentUnit.secondaryAtk}\n";
        infoText += $"{TranslateGroup.T("防御力")}: {currentUnit.defense}\n";
        string defenseBonusStr = (int)currentUnit.defenseBonusType switch
        {
            1 => TranslateGroup.T("受地形加成"),
            0 => TranslateGroup.T("不受地形加成"),
            _ => TranslateGroup.T("未知")
        };
        infoText += $"{TranslateGroup.T("防御类型")}: {defenseBonusStr}\n";
        infoText += $"{TranslateGroup.T("移动力")}: {currentUnit.movePoints}\n";

        // ✅ 从 UnitRegistry 读取单位显示名，零硬编码适配新单位
        string unitType = currentUnit.DisplayLabel;

        infoText += $"{TranslateGroup.T("单位类型")}: {unitType}\n";

if (currentUnit.canTransportUnits && currentUnit.maxTransportCapacity > 0)
{
    infoText += $"\n=== {TranslateGroup.T("可运输单位配置信息")} ===\n";

    // 1. 可搭载单位种类
    string loadableTypes = currentUnit.canTransportUnitTypes != null && currentUnit.canTransportUnitTypes.Count > 0
        ? string.Join(", ", currentUnit.canTransportUnitTypes)
        : TranslateGroup.T("无");
    infoText += $"{TranslateGroup.T("可搭载单位")}: {loadableTypes}\n";

    // 2. 远程搭载
    infoText += $"{TranslateGroup.T("远程搭载")}: {(currentUnit.CanFarMounting ? TranslateGroup.T("✓ 已开启") : TranslateGroup.T("✗ 未开启"))}\n";
    if (currentUnit.CanFarMounting)
    {
        infoText += $"{TranslateGroup.T("远程搭载距离")}: {currentUnit.minFarMountingDistance}-{currentUnit.maxFarMountingDistance}格\n";
    }

    // 3. 已搭载单位列表 - 用 maxTransportCapacity
    infoText += $"\n[{TranslateGroup.T("已搭载单位")}] {currentUnit.transportedUnits.Count}/{currentUnit.maxTransportCapacity}\n";
    if (currentUnit.transportedUnits.Count > 0)
    {
        foreach (var transportedUnit in currentUnit.transportedUnits)
        {
            if (transportedUnit != null && IsInstanceValid(transportedUnit))
            {
                // ✅ 从 UnitRegistry 读取装载单位显示名
                string loadedType = transportedUnit.DisplayLabel;
                int hpBars = Mathf.CeilToInt(transportedUnit.health / 10f);
                infoText += $"  • {transportedUnit.Name} ({loadedType}) HP:{hpBars}\n";
            }
        }
    }
    else
    {
        infoText += TranslateGroup.T("  (空)\n");
    }

    infoText += $"\n";
}

        string overlapType = currentUnit.overlapType switch
        {
            UnitOverlapType.NonOverlapping => "NonOverlapping",
            UnitOverlapType.Overlapping => "Overlapping",
            UnitOverlapType.Oozium => "Oozium",
            _ => "Unknown"
        };
        infoText += $"{TranslateGroup.T("范围类型")}: {overlapType}\n";

        string primaryWeaponStr;
        if (!currentUnit.hasPrimaryWeapon)
            primaryWeaponStr = TranslateGroup.T("无");
        else if (currentUnit.HasInfinitePrimaryAmmo)
            primaryWeaponStr = $"{TranslateGroup.T("有")} (∞{TranslateGroup.T("无限")})";
        else
            primaryWeaponStr = $"{TranslateGroup.T("有")} ({TranslateGroup.T("弹药")}: {currentUnit.currentPrimaryAmmo}/{currentUnit.maxPrimaryAmmo})";

        string secondaryWeaponStr = currentUnit.hasSecondaryWeapon ? TranslateGroup.T("有") : TranslateGroup.T("无");

        string primaryEffect = "";
        if (currentUnit.hasPrimaryWeapon)
        {
            var effects = new System.Collections.Generic.List<string>();
            if (currentUnit.primaryAntiArmor) effects.Add(TranslateGroup.T("反装甲"));
            if (currentUnit.primaryAntiInfantry) effects.Add(TranslateGroup.T("反步兵"));
            primaryEffect = $"[{string.Join("/", effects)}]";
        }

        string secondaryEffect = "";
        if (currentUnit.hasSecondaryWeapon)
        {
            var effects = new System.Collections.Generic.List<string>();
            if (currentUnit.secondaryAntiArmor) effects.Add(TranslateGroup.T("反装甲"));
            if (currentUnit.secondaryAntiInfantry) effects.Add(TranslateGroup.T("反步兵"));
            secondaryEffect = $"[{string.Join("/", effects)}]";
        }

        infoText += $"{TranslateGroup.T("主武器")}: {primaryWeaponStr} {primaryEffect}\n";
        infoText += $"{TranslateGroup.T("副武器")}: {secondaryWeaponStr} {secondaryEffect}\n";

        string attackRangeStr = currentUnit.useMinMaxAttackRange 
            ? $"{currentUnit.minAttackRange}-{currentUnit.maxAttackRange}" 
            : currentUnit.attackRange.ToString();
        infoText += $"{TranslateGroup.T("攻击范围")}: {attackRangeStr}\n";

        string state = currentUnit.state switch
        {
            UnitState.Idle => "Idle",
            UnitState.Moved => "Moved",
            UnitState.Acted => "Acted",
            _ => "Unknown"
        };
        infoText += $"{TranslateGroup.T("状态")}: {state}\n";

        string posText = currentUnit.grid != null 
            ? $"(X:{currentUnit.grid.GridIndex.X},Y:{currentUnit.grid.GridIndex.Y})" 
            : TranslateGroup.T("未绑定格子");
        string moveTypeStr = currentUnit.moveType switch
        {
            MoveType.Infantry => TranslateGroup.T("步兵移动"),
            MoveType.Mech => TranslateGroup.T("机甲移动"),
            MoveType.Oozium => TranslateGroup.T("Oozium移动"),
            MoveType.Treads => TranslateGroup.T("履带移动"), 
            MoveType.Tire => TranslateGroup.T("轮胎移动"),
            MoveType.AeroSpacer => TranslateGroup.T("空天移动"),
            MoveType.AirPlane => TranslateGroup.T("飞机移动"),
            MoveType.AirShip => TranslateGroup.T("飞艇移动"),
            MoveType.Drone => TranslateGroup.T("无人机移动"),
            MoveType.HeliCopter => TranslateGroup.T("直升机移动"),
            MoveType.Hover => TranslateGroup.T("登陆艇移动"),
            MoveType.Naval => TranslateGroup.T("海军移动"),
            MoveType.LAVAHOVER => TranslateGroup.T("岩浆登陆者移动"),
            MoveType.LAVARUNNER => TranslateGroup.T("岩浆行者移动"),
            MoveType.SpaceShiper => TranslateGroup.T("星舰移动"),
            MoveType.PipeRunner => TranslateGroup.T("管道行者移动"),
            MoveType.Train => TranslateGroup.T("普通火车移动"),
            MoveType.GasTrain => TranslateGroup.T("蒸汽机移动"),
            MoveType.FASTER => TranslateGroup.T("高铁移动"),
            MoveType.Missile => TranslateGroup.T("导弹飞行"),
            _ => "Unknown"
        };
        infoText += $"{TranslateGroup.T("移动方式")}: {moveTypeStr}\n";
        string fuelText = currentUnit.consumeFuel 
            ? $"{currentUnit.fuel}/{currentUnit.maxFuel} ({TranslateGroup.T("阈值")}:{currentUnit.lowFuelThreshold}, {TranslateGroup.T("日耗")}:{currentUnit.dailyFuelConsumption})" 
            : TranslateGroup.T("无限");
        infoText += $"{TranslateGroup.T("燃料")}: {fuelText}\n";

        // ✅ 自爆系统信息
        if (currentUnit.canExplode)
        {
            infoText += $"\n=== {TranslateGroup.T("自爆系统")} ===\n";
            infoText += $"{TranslateGroup.T("爆炸范围")}: {currentUnit.explosionMinRange}-{currentUnit.explosionMaxRange}格\n";
            string damageModeStr = currentUnit.explosionDamageMode switch
            {
                0 => $"{TranslateGroup.T("固定值")} {currentUnit.explosionFixedValue}",
                1 => $"{currentUnit.explosionPercentValue * 100}%{TranslateGroup.T("最大")}HP",
                2 => TranslateGroup.T("攻击公式"),
                _ => TranslateGroup.T("未知")
            };
            infoText += $"{TranslateGroup.T("伤害模式")}: {damageModeStr}\n";
            string targetModeStr = currentUnit.explosionTargetMode switch
            {
                0 => TranslateGroup.T("所有单位"),
                1 => TranslateGroup.T("仅敌方"),
                2 => TranslateGroup.T("仅友方"),
                _ => TranslateGroup.T("未知")
            };
            infoText += $"{TranslateGroup.T("目标筛选")}: {targetModeStr}\n";
            infoText += $"{TranslateGroup.T("摧毁自身")}: {(currentUnit.explosionDestroysSelf ? "✓" : "✗")}\n";
            infoText += $"{TranslateGroup.T("可击杀")}: {(currentUnit.explosionCanKill ? "✓" : TranslateGroup.T("✗ (保留1HP)"))}\n";
            infoText += $"{TranslateGroup.T("影响兵器")}: {(currentUnit.explosionAffectsWeapons ? "✓" : "✗")}\n";
            infoText += $"{TranslateGroup.T("回血可超上限")}: {(currentUnit.explosionCanExceedMaxHealth ? "✓" : "✗")}\n";
        }

        // ✅ 照明模块信息
        if (currentUnit.canIlluminate)
        {
            infoText += $"\n=== {TranslateGroup.T("照明模块")} ===\n";
            infoText += $"{TranslateGroup.T("照明弹")}: {currentUnit.currentFlareAmmo}/{currentUnit.maxFlareAmmo}\n";
            infoText += $"{TranslateGroup.T("投射射程")}: {currentUnit.minLaunchRange}-{currentUnit.maxLaunchRange}格\n";
            infoText += $"{TranslateGroup.T("照明覆盖")}: {currentUnit.minIlluminationRange}-{currentUnit.maxIlluminationRange}格\n";
            infoText += $"{TranslateGroup.T("持续回合")}: {currentUnit.flareDurationTurns}大回合\n";
            infoText += $"{TranslateGroup.T("移动后可照明")}: {(currentUnit.canIlluminateAfterMove ? "✓" : "✗")}\n";
        }

        if (currentUnit.grid != null)
        {
            infoText += $"\n=== {TranslateGroup.T("自爆系统")} ===\n";
            infoText += $"{TranslateGroup.T("爆炸范围")}: {currentUnit.explosionMinRange}-{currentUnit.explosionMaxRange}格\n";
            string damageModeStr = currentUnit.explosionDamageMode switch
            {
                0 => $"{TranslateGroup.T("固定值")} {currentUnit.explosionFixedValue}",
                1 => $"{currentUnit.explosionPercentValue * 100}%{TranslateGroup.T("最大")}HP",
                2 => TranslateGroup.T("攻击公式"),
                _ => TranslateGroup.T("未知")
            };
            infoText += $"{TranslateGroup.T("伤害模式")}: {damageModeStr}\n";
            string targetModeStr = currentUnit.explosionTargetMode switch
            {
                0 => TranslateGroup.T("所有单位"),
                1 => TranslateGroup.T("仅敌方"),
                2 => TranslateGroup.T("仅友方"),
                _ => TranslateGroup.T("未知")
            };
            infoText += $"{TranslateGroup.T("目标筛选")}: {targetModeStr}\n";
            infoText += $"{TranslateGroup.T("摧毁自身")}: {(currentUnit.explosionDestroysSelf ? "✓" : "✗")}\n";
            infoText += $"{TranslateGroup.T("可击杀")}: {(currentUnit.explosionCanKill ? "✓" : TranslateGroup.T("✗ (保留1HP)"))}\n";
            infoText += $"{TranslateGroup.T("影响兵器")}: {(currentUnit.explosionAffectsWeapons ? "✓" : "✗")}\n";
            infoText += $"{TranslateGroup.T("回血可超上限")}: {(currentUnit.explosionCanExceedMaxHealth ? "✓" : "✗")}\n";
        }

        if (currentUnit.grid != null)
        {
            infoText += $"\n=== {TranslateGroup.T("地形信息")} ===\n";
            infoText += $"{TranslateGroup.T("地形类型")}: {currentUnit.grid.gridType}\n";
            string terrainDefenseText = currentUnit.grid.GetTerrainDefenseDescription();
            if ((int)currentUnit.defenseBonusType == 0)
                infoText += $"{TranslateGroup.T("地形防御")}: {terrainDefenseText} (单位不受加成)\n";
            else
                infoText += $"{TranslateGroup.T("地形防御")}: {terrainDefenseText}\n";

            // ✅ 新增：显示格子自定义伤害系统信息
            infoText += $"\n=== {TranslateGroup.T("格子自定义效果")} ===\n";

            // 血量相关
            infoText += $"[{TranslateGroup.T("血量系统")}]\n";
            infoText += $"  {TranslateGroup.T("可摧毁单位")}: {(currentUnit.grid.canDestroyUnit ? "✓" : TranslateGroup.T("✗ (锁血1点)"))}\n";
            if (currentUnit.grid.fixedDamagePerTurn != 0)
                infoText += $"  {TranslateGroup.T("固定伤害")}/回合: {currentUnit.grid.fixedDamagePerTurn} ({(currentUnit.grid.fixedDamagePerTurn > 0 ? TranslateGroup.T("扣血") : TranslateGroup.T("回血"))})\n";
            if (currentUnit.grid.fixedAttackPerTurn != 0)
                infoText += $"  {TranslateGroup.T("固定攻击")}/回合: {currentUnit.grid.fixedAttackPerTurn} ({(currentUnit.grid.fixedAttackPerTurn > 0 ? TranslateGroup.T("扣血(经防御)") : TranslateGroup.T("回血"))})\n";
            infoText += $"  {TranslateGroup.T("可超血量上限")}: {(currentUnit.grid.canOverMaxHealth ? "✓" : "✗")}\n";
            if (currentUnit.grid.fixedMaxHealthChangePerTurn != 0) // ✦优化：最大血量增减
                infoText += $"  {TranslateGroup.T("最大血量增减")}/回合: {(currentUnit.grid.fixedMaxHealthChangePerTurn > 0 ? "+" : "")}{currentUnit.grid.fixedMaxHealthChangePerTurn}\n";

            // 弹药相关
            infoText += $"\n[{TranslateGroup.T("弹药系统")}]\n";
            if (currentUnit.grid.fixedAmmoChangePerTurn != 0)
            {
                string ammoChangeText = currentUnit.grid.fixedAmmoChangePerTurn > 0 ? $"+{currentUnit.grid.fixedAmmoChangePerTurn} ({TranslateGroup.T("补充")})" : $"{currentUnit.grid.fixedAmmoChangePerTurn} ({TranslateGroup.T("消耗")})";
                infoText += $"  {TranslateGroup.T("弹药变化")}/回合: {ammoChangeText}\n";
            }
            else
            {
                infoText += $"  {TranslateGroup.T("弹药变化")}/回合: 无\n";
            }
            infoText += $"  {TranslateGroup.T("可超弹药上限")}: {(currentUnit.grid.ammoCanOverMax ? "✓" : "✗")}\n";
            infoText += $"  {TranslateGroup.T("可归零")}: {(currentUnit.grid.ammoCanReachZero ? "✓" : TranslateGroup.T("✗ (强制剩1)"))}\n";
            // ✦优化：全种类弹药增减显示
            foreach (Infantry.AmmoKind k in Infantry.AllAmmoKinds)
            {
                int kc = currentUnit.grid.GetAmmoKindChange(k);
                int km = currentUnit.grid.GetAmmoKindMaxChange(k);
                if (kc != 0 || km != 0)
                    infoText += $"  {Grids.GetAmmoKindName(k)}: {(kc > 0 ? "+" : "")}{kc}/回合{(km != 0 ? $" (MAX{(km > 0 ? "+" : "")}{km})" : "")}\n";
            }

            // 燃料相关
            infoText += $"\n[{TranslateGroup.T("燃料系统")}]\n";
            if (currentUnit.grid.fixedFuelChangePerTurn != 0)
            {
                string fuelChangeText = currentUnit.grid.fixedFuelChangePerTurn > 0 ? $"+{currentUnit.grid.fixedFuelChangePerTurn} ({TranslateGroup.T("补充")})" : $"{currentUnit.grid.fixedFuelChangePerTurn} ({TranslateGroup.T("消耗")})";
                infoText += $"  {TranslateGroup.T("燃料变化")}/回合: {fuelChangeText}\n";
            }
            else
            {
                infoText += $"  {TranslateGroup.T("燃料变化")}/回合: 无\n";
            }
            infoText += $"  {TranslateGroup.T("可超燃料上限")}: {(currentUnit.grid.fuelCanOverMax ? "✓" : "✗")}\n";
            if (currentUnit.grid.fixedMaxFuelChangePerTurn != 0) // ✦优化：燃油上限增减
                infoText += $"  {TranslateGroup.T("燃油上限增减")}/回合: {(currentUnit.grid.fixedMaxFuelChangePerTurn > 0 ? "+" : "")}{currentUnit.grid.fixedMaxFuelChangePerTurn}\n";
            infoText += $"  {TranslateGroup.T("可归零")}: {(currentUnit.grid.fuelCanReachZero ? "✓" : TranslateGroup.T("✗ (强制剩1)"))}\n";
        }

        if (currentUnit.grid?.city != null)
        {
            string ownerText = currentUnit.grid.city.facilityTeam switch
            {
                "Player0" => TranslateGroup.T("中立（双方共用）"),
                "Player1" => TranslateGroup.T("🔴 Player1 占领"),
                "Player2" => TranslateGroup.T("🔵 Player2 占领"),
                _ => TranslateGroup.T("无归属（无法补给）")
            };
            infoText += $"{TranslateGroup.T("CITY归属")}: {ownerText}\n";
        }

        string captureAbility = currentUnit.captureAbility == CaptureAbility.CanCapture ? TranslateGroup.T("可以占领") : TranslateGroup.T("不能占领");
        infoText += $"{TranslateGroup.T("占领能力")}: {captureAbility}\n";
        if (currentUnit.captureAbility == CaptureAbility.CanCapture && currentUnit.grid?.city != null)
        {
            infoText += $"{TranslateGroup.T("占领力")}: {currentUnit.GetCapturePower()}\n";
            infoText += $"{TranslateGroup.T("占领消耗")}: {currentUnit.grid.city?.capturePointsRequired ?? 20}\n";
            if (currentUnit.currentCaptureProgress > 0)
            {
                infoText += $"{TranslateGroup.T("当前占领进度")}: {currentUnit.currentCaptureProgress}/{currentUnit.grid.city?.capturePointsRequired ?? 20}\n";
                if (currentUnit.capturingGrid != null)
                {
                    infoText += $"{TranslateGroup.T("占领目标")}: ({currentUnit.capturingGrid.GridIndex.X},{currentUnit.capturingGrid.GridIndex.Y})\n";
                }
            }
        }
        
        // ========== ✅ 新增：战争迷雾视野信息 ==========
        infoText += $"{TranslateGroup.T("视野能力")} ";

        // 基础视野
        int actualVision = currentUnit.ActualVisionRange;
        infoText += $"{TranslateGroup.T("基础视野")}: {actualVision}{TranslateGroup.T("格")}";
        if (!currentUnit.useConfigVision && currentUnit.visionRange >= 0)
            infoText += $" ({TranslateGroup.T("自定义")}:{currentUnit.visionRange})";
        else
            infoText += $" ({TranslateGroup.T("配置表")})";
        infoText += "\n";

        // 地形视野加成状态
        if (currentUnit.overrideGlobalTerrainBonus)
        {
            infoText += $"{TranslateGroup.T("地形加成")}: {TranslateGroup.T("使用专属矩阵")} ({currentUnit.unitTerrainVisionBonus?.Count ?? 0}{TranslateGroup.T("项")})\n";
        }
        else
        {
            infoText += $"{TranslateGroup.T("地形加成")}: {TranslateGroup.T("使用全局默认")}\n";
        }

        // 如果当前在格子上，显示当前地形的实际加成
        if (currentUnit.grid != null)
        {
            int terrainBonus = currentUnit.GetTerrainVisionBonus(currentUnit.grid.gridType);
            string sign = terrainBonus > 0 ? "+" : "";
            infoText += $"{TranslateGroup.T("当前地形")}({currentUnit.grid.gridType}): {sign}{terrainBonus}{TranslateGroup.T("格")}\n";
            infoText += $"{TranslateGroup.T("实际视野")}: {actualVision + terrainBonus}{TranslateGroup.T("格")}\n";
        }

infoText += $"{TranslateGroup.T("所在位置")}: {posText}\n";

        unitInfoLabel.Text = infoText;
        unitInfoLabel.Visible = true;
        closeInfoButton.Visible = true;
    }

    public void OnCloseInfoPressed()
    {
        GetViewport().SetInputAsHandled();
        unitInfoLabel.Visible = false;
        closeInfoButton.Visible = false;
    }

private void ShowWeaponInfo(Weapon weapon)
    {
        ZIndex = 200;
        string info = "";

        info += $"[{TranslateGroup.T("兵器")}] {weapon.Name}\n";
        info += $"{TranslateGroup.T("团队")}: {weapon.team}\n";

        // ✅ 修复：兵器血量显示格式（1-99格，不是百分比）
        int hpBars = Mathf.CeilToInt((float)weapon.health / 10f);
        hpBars = Mathf.Clamp(hpBars, 1, 99);
        info += $"{TranslateGroup.T("血量")}: {weapon.health}/{weapon.maxHealth} (显示{hpBars}格)\n";

        info += $"{TranslateGroup.T("状态")}: {(weapon.hasActed ? TranslateGroup.T("已行动") : TranslateGroup.T("待机"))}\n";
        info += $"{TranslateGroup.T("每回合攻击次数")}: {weapon.remainingAttacks}/{weapon.maxAttacksPerTurn}\n";
        info += $"{TranslateGroup.T("可旋转")}: {(weapon.canRotate ? TranslateGroup.T("是") : TranslateGroup.T("否"))}\n";
        info += $"{TranslateGroup.T("可攻击兵器")}: {(weapon.CanAttackWeapon ? TranslateGroup.T("是") : TranslateGroup.T("否"))}\n";

        if (weapon is BlackCannon cannon)
        {
            info += $"{TranslateGroup.T("黑炮专用配置")}";
            info += $"{TranslateGroup.T("射程深度")}: {cannon.maxAttackDepth}格\n";
            info += $"{TranslateGroup.T("伤害模式")}: {(cannon.useModifiedDamage ? TranslateGroup.T("改良模式") : TranslateGroup.T("传统模式"))}\n";

            // ✅ 新增：弹药系统完整信息
            info += $"[{TranslateGroup.T("弹药系统")}]";
            if (cannon.useAmmoSystem)
            {
                info += $"{TranslateGroup.T("弹药")}: {cannon.currentAmmo}/{cannon.maxAmmo}\n";
                info += $"{TranslateGroup.T("补给状态")}: {(cannon.currentAmmo >= cannon.maxAmmo ? TranslateGroup.T("已满") : TranslateGroup.T("可补给"))}\n";
            }
            else
            {
                info += $"{TranslateGroup.T("弹药")}: 无限 (∞)\n";
            }

            // ✅ 新增：冷却系统完整信息
            info += $"[{TranslateGroup.T("冷却系统")}]";
            if (cannon.useCooldownSystem)
            {
                info += $"{TranslateGroup.T("冷却周期")}: {cannon.cooldownTurns}回合\n";
                info += $"{TranslateGroup.T("周期攻击次数")}: {cannon.attacksPerCooldown}次\n";
                info += $"{TranslateGroup.T("存次数")}: {(cannon.storeAttacks ? TranslateGroup.T("是") : TranslateGroup.T("否"))}\n";
                info += $"{TranslateGroup.T("当前周期进度")}: {cannon.turnsSinceLastAttack}/{cannon.cooldownTurns}回合\n";
                info += $"{TranslateGroup.T("周期剩余次数")}: {cannon.attacksRemainingInCycle}次\n";
                info += $"{TranslateGroup.T("冷却就绪")}: {(cannon.cooldownReady ? TranslateGroup.T("✓ 是") : TranslateGroup.T("✗ 否"))}\n";

                if (!cannon.cooldownReady)
                {
                    info += $"{TranslateGroup.T("还需等待")}: {cannon.cooldownTurns - cannon.turnsSinceLastAttack}回合\n";
                }
            }
            else
            {
                info += $"{TranslateGroup.T("冷却")}: {TranslateGroup.T("无（每回合重置）")}";
                info += $"{TranslateGroup.T("每回合次数")}: {weapon.maxAttacksPerTurn}{TranslateGroup.T("次")}";
            }

            // ✅ 新增：综合攻击状态
            info += $"[{TranslateGroup.T("攻击状态")}]";
            info += $"{TranslateGroup.T("可攻击")}: {(cannon.CanAttack() ? TranslateGroup.T("✓ 是") : TranslateGroup.T("✗ 否"))}\n";
            if (!cannon.CanAttack())
            {
                if (cannon.useAmmoSystem && cannon.currentAmmo <= 0)
                    info += $"{TranslateGroup.T("原因")}: 弹药耗尽\n";
                if (cannon.useCooldownSystem && !cannon.cooldownReady)
                    info += $"{TranslateGroup.T("原因")}: 冷却中 ({cannon.turnsSinceLastAttack}/{cannon.cooldownTurns}回合)\n";
                if (cannon.useCooldownSystem && cannon.attacksRemainingInCycle <= 0)
                    info += $"{TranslateGroup.T("原因")}: 周期次数已用完\n";
            }

            // 伤害配置
            if (!cannon.useModifiedDamage)
            {
                info += $"[{TranslateGroup.T("传统伤害")}]";
                info += $"{TranslateGroup.T("固定伤害")}: {cannon.fixedDamagePercent * 100:F0}%\n";
                info += $"{TranslateGroup.T("可摧毁")}: {(cannon.CanDestroy ? TranslateGroup.T("是") : TranslateGroup.T("否"))}\n";
            }
            else
            {
                info += $"[{TranslateGroup.T("改良伤害")}]";
                info += $"{TranslateGroup.T("血量变化")}: {cannon.modifiedHealthPercent * 100:F0}% ({(cannon.modifiedHealthPercent > 0 ? TranslateGroup.T("扣血") : cannon.modifiedHealthPercent < 0 ? TranslateGroup.T("回血") : TranslateGroup.T("无变化"))})\n";
                info += $"{TranslateGroup.T("弹药变化")}: {cannon.modifiedAmmoPercent * 100:F0}% ({(cannon.modifiedAmmoPercent > 0 ? TranslateGroup.T("消耗") : cannon.modifiedAmmoPercent < 0 ? TranslateGroup.T("补充") : TranslateGroup.T("无变化"))})\n";
                info += $"{TranslateGroup.T("燃料变化")}: {cannon.modifiedFuelPercent * 100:F0}% ({(cannon.modifiedFuelPercent > 0 ? TranslateGroup.T("消耗") : cannon.modifiedFuelPercent < 0 ? TranslateGroup.T("补充") : TranslateGroup.T("无变化"))})\n";
                info += $"{TranslateGroup.T("可超血量上限")}: {(cannon.canOverMaxHp ? TranslateGroup.T("是") : TranslateGroup.T("否"))}\n";
                info += $"{TranslateGroup.T("可摧毁")}: {(cannon.CanDestroy ? TranslateGroup.T("是") : TranslateGroup.T("否"))}\n";
            }

            info += $"[{TranslateGroup.T("其他配置")}]";
            info += $"{TranslateGroup.T("朝向")}: {cannon.direction}\n";
            info += $"{TranslateGroup.T("可攻击兵器")}: {(cannon.CanAttackWeapon ? TranslateGroup.T("是") : TranslateGroup.T("否"))}\n";
            info += $"{TranslateGroup.T("目标选择")}: {cannon.targetSelectionMode switch 
            { 
                BlackCannon.TargetSelectionMode.AllSelect => TranslateGroup.T("所有单位"),
                BlackCannon.TargetSelectionMode.OnlyEnemyUnits => TranslateGroup.T("仅敌方单位"),
                BlackCannon.TargetSelectionMode.OnlyUserUnits => TranslateGroup.T("仅己方单位"),
                _ => TranslateGroup.T("未知")
            }}\n";

            info += $"[{TranslateGroup.T("操作说明")}]";
            info += $"• {TranslateGroup.T("左键点击")}: {TranslateGroup.T("选择/显示攻击范围")}";
            info += $"• {TranslateGroup.T("R键")}: {TranslateGroup.T("旋转方向")}";
            info += $"• {TranslateGroup.T("攻击范围")}: {TranslateGroup.T("三角形区域")}，{TranslateGroup.T("深度")}{cannon.maxAttackDepth}{TranslateGroup.T("格")}";

            if (cannon.useAmmoSystem)
            {
                info += $"• {TranslateGroup.T("需要APC在范围内补给弹药")}";
            }
        }

else if (weapon is Laser laser)
        {
            info = laser.GetLaserFullInfo();
        }
        unitInfoLabel.Text = info;
        unitInfoLabel.Visible = true;
        closeInfoButton.Visible = true;
    }    }
