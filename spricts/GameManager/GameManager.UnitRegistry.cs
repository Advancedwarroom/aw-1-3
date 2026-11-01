// GameManager.cs - 添加完整的单位统计系统
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// GameManager · UnitRegistry（由 GameManager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class GameManager
{

    // ✅ 新增：刷新所有专用单位列表
    public void RefreshSpecializedUnitLists()
    {
        // 清空所有列表
        infantryUnits.Clear();
        mechUnits.Clear();
        ooziumUnits.Clear();
        tankUnits.Clear();
        mdTankUnits.Clear();
        vehicleUnits.Clear();
        artilleryUnits.Clear();
        antiairUnits.Clear();
        antiTankUnits.Clear();
        apcUnits.Clear();
        reconUnits.Clear();
        flyBombUnits.Clear();
        flareUnits.Clear();
        bikeUnits.Clear();
        repairBoatUnits.Clear();
        PipeRunnerUnits.Clear();

        foreach (var unit in unitManager.AllUnits)
        {
            // 通过字典查询分类
            if (unitCategories.TryGetValue(unit, out var category))
            {
                switch (category)
                {
                    case UnitCategory.Infantry:
                        infantryUnits.Add(unit);
                        break;
                    case UnitCategory.Mech:
                        mechUnits.Add(unit);
                        break;
                    case UnitCategory.Oozium:
                        ooziumUnits.Add(unit);
                        break;
                    case UnitCategory.Tank:
                        tankUnits.Add(unit);
                        break;
                    case UnitCategory.MdTank:
                        mdTankUnits.Add(unit);
                        break;
                    case UnitCategory.Vehicle:
                        vehicleUnits.Add(unit);
                        break;
                    case UnitCategory.APC:  // ✅ APC分类
                        apcUnits.Add(unit);
                        break;
                    case UnitCategory.Recon:
                        reconUnits.Add(unit);
                        break;
                    case UnitCategory.AntiAir:
                        antiairUnits.Add(unit);
                        break;
                    case UnitCategory.AntiTank:
                        antiTankUnits.Add(unit);
                        break;
                    case UnitCategory.Artillery:
                        artilleryUnits.Add(unit);
                        break;
                    case UnitCategory.Rocket:
                        artilleryUnits.Add(unit);
                        break;
                    case UnitCategory.FlyBomb:
                        flyBombUnits.Add(unit);
                        break;
                    case UnitCategory.Flare:
                        flareUnits.Add(unit);
                        break;
                    case UnitCategory.Bike:
                        bikeUnits.Add(unit);
                        break;
                    case UnitCategory.RepairBoat:
                        repairBoatUnits.Add(unit);
                        break;
                    case UnitCategory.PipeRunner:
                        PipeRunnerUnits.Add(unit);
                        break;
                }
            }
            else
            {
                // ✅ 自动识别未分类单位
                AutoCategorizeUnit(unit);
            }
        }

    }

    // ✅ 新增：自动识别单位类型
// GameManager.cs - 修复后的 AutoCategorizeUnit 方法
// GameManager.cs - AutoCategorizeUnit 方法
private void AutoCategorizeUnit(Infantry unit)
{
    UnitCategory category;

    switch (unit)
    {
        // ✅ 新增 8 个原版兵种（Rig 必须在 APC 之前；全部在 Infantry 之前）
        case Neotank _:
            category = UnitCategory.Neotank;
            break;
        case Megatank _:
            category = UnitCategory.Megatank;
            break;
        case WarTank _:
            category = UnitCategory.WarTank;
            break;
        case Rig _:
            category = UnitCategory.Rig;
            break;
        case Duster _:
            category = UnitCategory.Duster;
            break;
        case Seaplane _:
            category = UnitCategory.Seaplane;
            break;
        case ProducibleBoat _:
            category = UnitCategory.ProducibleBoat;
            break;
        case KamikazeBoat _:
            category = UnitCategory.KamikazeBoat;
            break;
        case APC _:         // ✅ 添加 APC 检查（必须在 Infantry 之前）
            category = UnitCategory.APC;
            apcUnits.Add(unit);
            break;

        case Recon _:
            category = UnitCategory.Recon;
            reconUnits.Add(unit);
            break;


        case Mech _:
            category = UnitCategory.Mech;
            mechUnits.Add(unit);
            break;

        case Oozium _:
            category = UnitCategory.Oozium;
            ooziumUnits.Add(unit);
            break;

        case MdTank _:
            category = UnitCategory.MdTank;
            mdTankUnits.Add(unit);
            break;

        case LightTank _:
            category = UnitCategory.Tank;
            tankUnits.Add(unit);
            break;

        case Rocket _:
            category = UnitCategory.Rocket;  // 归入火炮类，或新建 Rocket 分类
            artilleryUnits.Add(unit);
            break;
        case FlyBomb _:
            category = UnitCategory.FlyBomb;
            flyBombUnits.Add(unit);
            break;
        case Flare _:
            category = UnitCategory.Vehicle;
            flareUnits.Add(unit);
            break;
        case Bike _:
            category = UnitCategory.Infantry;
            bikeUnits.Add(unit);
            break;
        case PipeRunner _:
            category = UnitCategory.PipeRunner;
            PipeRunnerUnits.Add(unit);
            break;
        case RepairBoat _:
            category = UnitCategory.RepairBoat;
            repairBoatUnits.Add(unit);
            break;
        case Artillery _:
            category = UnitCategory.Artillery;
            artilleryUnits.Add(unit);
            break;
        case AntiAir _:         // ✅ 添加 APC 检查（必须在 Infantry 之前）
            category = UnitCategory.AntiAir;
            antiairUnits.Add(unit);
            break;

        case AntiTank _:
            category = UnitCategory.AntiTank;
            antiTankUnits.Add(unit);
            break;

        case Infantry _:
            category = UnitCategory.Infantry;
            infantryUnits.Add(unit);
            break;

        default:
            category = UnitCategory.Other;
            break;
    }

    unitCategories[unit] = category;
}


    // ✅ 动态添加单位 - 传入分类参数
    public void AddNewUnit(PackedScene unitScene, Vector2I gridPos, string team, UnitCategory? category = null)
    {
        var newUnit = unitManager?.SpawnUnit(unitScene, gridPos, team);
        if (newUnit != null)
        {
            newUnit.OnClickPiece = OnSelectPiece;
            newUnit.isMoved = false;
            newUnit.isAttacked = false;

            // 自动推断分类（如果未指定）
            var actualCategory = category ?? InferCategory(newUnit);
            RegisterUnit(newUnit, actualCategory);

            // ✅ 更新统计
            UpdateUnitLists();
        }
    }

    public UnitCategory InferCategory(Infantry unit)
    {
        return unit switch
        {
            Neotank _ => UnitCategory.Neotank,
            Megatank _ => UnitCategory.Megatank,
            WarTank _ => UnitCategory.WarTank,
            Rig _ => UnitCategory.Rig,
            Duster _ => UnitCategory.Duster,
            Seaplane _ => UnitCategory.Seaplane,
            ProducibleBoat _ => UnitCategory.ProducibleBoat,
            KamikazeBoat _ => UnitCategory.KamikazeBoat,
            APC _ => UnitCategory.APC,
            Recon _ => UnitCategory.Recon,
            Mech _ => UnitCategory.Mech,
            Oozium _ => UnitCategory.Oozium,
            MdTank _ => UnitCategory.MdTank,
            LightTank _ => UnitCategory.Tank,
            Rocket _ => UnitCategory.Rocket,
            FlyBomb _ => UnitCategory.FlyBomb,
            Flare _ => UnitCategory.Vehicle,
            Bike _ => UnitCategory.Infantry,
            Artillery _ => UnitCategory.Artillery,
            AntiAir _ => UnitCategory.AntiAir,
            AntiTank _ => UnitCategory.AntiTank,
            PipeRunner _ => UnitCategory.PipeRunner,
            RepairBoat _ => UnitCategory.RepairBoat,
            Battleship _ => UnitCategory.Battleship,
            Cruiser _ => UnitCategory.Cruiser,
            Submarine _ => UnitCategory.Submarine,
            Lander _ => UnitCategory.Lander,
            Carrier _ => UnitCategory.Carrier,
            Gunboat _ => UnitCategory.Gunboat,
            Infantry _ => UnitCategory.Infantry,
            _ => UnitCategory.Other
        };
    }

    public void RemoveUnit(Infantry unit)
    {
        // ✅ 从所有专用列表中移除
        infantryUnits.Remove(unit);
        mechUnits.Remove(unit);
        ooziumUnits.Remove(unit);
        tankUnits.Remove(unit);
        lightTankUnits.Remove(unit);
        rocketUnits.Remove(unit);
        antiAirUnits.Remove(unit);
        mdTankUnits.Remove(unit);
        antiTankUnits.Remove(unit);
        flareUnits.Remove(unit);
        bikeUnits.Remove(unit);
        vehicleUnits.Remove(unit);
        artilleryUnits.Remove(unit);
        antiairUnits.Remove(unit);
        apcUnits.Remove(unit);
        reconUnits.Remove(unit);
        flyBombUnits.Remove(unit);
        PipeRunnerUnits.Remove(unit);
        repairBoatUnits.Remove(unit);

        unitCategories.Remove(unit);
        unitManager?.RemoveUnit(unit);

        if (selectedInfantry == unit)
        {
            selectedInfantry = null;
        }
        CheckVictoryCondition();
        // ✅ 更新统计
        UpdateUnitLists();
    }

    // 从列表移除（供 Infantry 调用）
    public void RemovePiece(Infantry piece)
    {
        RemoveUnit(piece);
    }

public void RegisterUnit(Infantry unit, UnitCategory category)
{
    // 如果已存在，先从旧列表移除
    if (unitCategories.ContainsKey(unit))
    {
        var oldCategory = unitCategories[unit];
        RemoveFromCategoryList(unit, oldCategory);
    }

    unitCategories[unit] = category;
    AddToCategoryList(unit, category);


    // 立即更新统计面板
    UpdateUnitLists();
}

    private void AddToCategoryList(Infantry unit, UnitCategory category)
    {
        switch (category)
        {
            case UnitCategory.Infantry:
                if (!infantryUnits.Contains(unit)) infantryUnits.Add(unit);
                break;
            case UnitCategory.Mech:
                if (!mechUnits.Contains(unit)) mechUnits.Add(unit);
                break;
            case UnitCategory.Oozium:
                if (!ooziumUnits.Contains(unit)) ooziumUnits.Add(unit);
                break;
            case UnitCategory.Tank:
                if (!tankUnits.Contains(unit)) tankUnits.Add(unit);
                break;
            case UnitCategory.MdTank:
                if (!mdTankUnits.Contains(unit)) mdTankUnits.Add(unit);
                break;
            case UnitCategory.Vehicle:
                if (!vehicleUnits.Contains(unit)) vehicleUnits.Add(unit);
                break;
            case UnitCategory.APC:  // ✅ 确保有这行！
                if (!apcUnits.Contains(unit)) apcUnits.Add(unit);
                break;
            case UnitCategory.AntiAir:
                if (!antiairUnits.Contains(unit)) antiairUnits.Add(unit);
                break;
            case UnitCategory.AntiTank:
                if (!antiTankUnits.Contains(unit)) antiTankUnits.Add(unit);
                break;
            case UnitCategory.Flare:
                if (!flareUnits.Contains(unit)) flareUnits.Add(unit);
                break;
            case UnitCategory.Bike:
                if (!bikeUnits.Contains(unit)) bikeUnits.Add(unit);
                break;
            case UnitCategory.PipeRunner:
                if (!PipeRunnerUnits.Contains(unit)) PipeRunnerUnits.Add(unit);
                break;
            case UnitCategory.Artillery:
                if (!artilleryUnits.Contains(unit)) artilleryUnits.Add(unit);
                break;
            case UnitCategory.Rocket:
                if (!artilleryUnits.Contains(unit)) artilleryUnits.Add(unit);
                break;
            case UnitCategory.FlyBomb:
                if (!flyBombUnits.Contains(unit)) flyBombUnits.Add(unit);
                break;
            case UnitCategory.Recon:
                if (!reconUnits.Contains(unit)) reconUnits.Add(unit);
                break;
        }
    }

    private void RemoveFromCategoryList(Infantry unit, UnitCategory category)
    {
        switch (category)
        {
            case UnitCategory.Infantry:
                infantryUnits.Remove(unit);
                break;
            case UnitCategory.Mech:
                mechUnits.Remove(unit);
                break;
            case UnitCategory.Oozium:
                ooziumUnits.Remove(unit);
                break;
            case UnitCategory.Tank:
                tankUnits.Remove(unit);
        lightTankUnits.Remove(unit);
        rocketUnits.Remove(unit);
        antiAirUnits.Remove(unit);
                break;
            case UnitCategory.MdTank:
                mdTankUnits.Remove(unit);
                break;
            case UnitCategory.Vehicle:
                vehicleUnits.Remove(unit);
                break;
            case UnitCategory.APC:  
                apcUnits.Remove(unit);
                break;
            case UnitCategory.AntiAir:  
                antiairUnits.Remove(unit);
                break;
            case UnitCategory.Artillery:
                artilleryUnits.Remove(unit);
                break;
            case UnitCategory.Rocket:
                artilleryUnits.Remove(unit);
                break;
            case UnitCategory.FlyBomb:
                flyBombUnits.Remove(unit);
                break;
            case UnitCategory.Recon:
                reconUnits.Remove(unit);
                break;
        }
    }

    public List<Infantry> GetAllArmoredUnits()
    {
        var armored = new List<Infantry>();
        foreach (var node in tankUnits)
        {
            if (IsInstanceValid(node)) armored.Add(node);
        }
        foreach (var node in vehicleUnits)
        {
            if (IsInstanceValid(node)) armored.Add(node);
        }
        return armored;
    }
}
