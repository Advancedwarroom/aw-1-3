// UnitRegistry.cs —— 兵种中央注册表
using Godot;
using System.Collections.Generic;

// ✅ 防御加成类型（避免编译顺序依赖）
public enum DefenseBonusType { NoDefenseBonus, CanDefenseBonus }

/// <summary>
/// 兵种完整数据配置。新增兵种只需要在这里加一行。
/// </summary>
public class UnitConfig
{
    // 基础
    public string UnitId;           // 唯一标识，如 "Infantry"
    public string DisplayName;      // 显示名，如 "步兵"
    public MoveType MoveType;       // 移动方式
    public int MovePoints;          // 移动力
    public int Cost;                // 造价

    // 视觉
    public string AnimPrefix;       // 动画前缀，如 "infantry" → 实际找 "infantry1"/"infantry2"

    // 武器 - 主武器
    public bool HasPrimaryWeapon;
    public int MaxPrimaryAmmo;
    public bool PrimaryHasLimitedAmmo = true;
    public bool PrimaryAntiArmor;
    public bool PrimaryAntiInfantry;
    public int BaseAttack = 0;

    // 武器 - 副武器
    public bool HasSecondaryWeapon;
    public int SecondaryAttack;
    public bool SecondaryAntiArmor;
    public bool SecondaryAntiInfantry;

    // 射程
    public int MinAttackRange = 1;
    public int MaxAttackRange = 1;
    public bool UseMinMaxAttackRange = false;
    public bool CanAttackAfterMoving = true;
    public bool CannotCounterWhenAttacked = false;
    public bool CanCounterWhenDefending = true;
    public bool CanCounterAtRange = false;

    // 燃料
    public int MaxFuel = 99;
    public bool ConsumeFuel = false;
        public bool CanMerge { get; set; } = true;
        public bool CanOperateLoadedUnits { get; set; } = false;
        public int MaxLoadedOperationsPerTurn { get; set; } = 1;
        public float LoadedOperationFactor { get; set; } = 0.5f;      // 合流模块：默认允许合流
    public int DailyFuelConsumption = 0;
    public bool DestroyOnOutOfFuel = false;
    public int LowFuelThreshold = 15;
    public int LowAmmoThreshold = -1;   // ✅ 缺弹警告阈值：-1=自动(弹药上限/3,最少1)

    // 防御
    public int BaseDefense = 0;
    public float CounterMul = 0.5f;

    // 占领
    public CaptureAbility CaptureAbility = CaptureAbility.CannotCapture;
    public int CapturePower = 10;
    public bool AllowCaptureAfterMove = true;

    // 搭载
    public bool CanTransportUnits = false;
    public bool CanSupplyUnits = false;
    public int MaxTransportCapacity = 0;
    public string[] TransportUnitTypes = System.Array.Empty<string>();

    // 特殊能力
    public bool CanDevour = false;
    public bool CanExplode = false;
    public bool CanIlluminate = false;

    // 重叠规则
    public UnitOverlapType OverlapType = UnitOverlapType.NonOverlapping;
    public AttackType AttackType = AttackType.CanAttack;
    public bool IsArmoredUnit = false;
    public bool IsAirUnit = false;
    public DefenseBonusType DefenseBonusType = DefenseBonusType.CanDefenseBonus;

    // 视野
    public int VisionRange = -1;  // -1 = 用配置表默认值
}

public static class UnitRegistry
{
    /// <summary>
    /// 所有兵种配置集中在这里。新增兵种 = 往字典里加一行。
    /// </summary>
    public static readonly Dictionary<string, UnitConfig> Configs = new()
    {
        // ==================== 步兵类 ====================
        ["Infantry"] = new UnitConfig
        {
            UnitId = "Infantry",
            DisplayName = TranslateGroup.T("步兵"),
            MoveType = MoveType.Infantry,
            MovePoints = 3,
            Cost = 1000,
            AnimPrefix = "infantry",
            HasPrimaryWeapon = false,
            HasSecondaryWeapon = true,
            SecondaryAttack = 100,
            SecondaryAntiInfantry = true,
            SecondaryAntiArmor = false,
            CaptureAbility = CaptureAbility.CanCapture,
            CapturePower = 10,
            OverlapType = UnitOverlapType.NonOverlapping,
        },

        ["Mech"] = new UnitConfig
        {
            UnitId = "Mech",
            DisplayName = TranslateGroup.T("机甲"),
            MoveType = MoveType.Mech,
            MovePoints = 2,
            Cost = 3000,
            AnimPrefix = "mech",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 3,
            PrimaryAntiArmor = true,
            PrimaryAntiInfantry = false,
            HasSecondaryWeapon = true,
            SecondaryAttack = 80,
            SecondaryAntiArmor = true,
            SecondaryAntiInfantry = true,
            CaptureAbility = CaptureAbility.CanCapture,
            CapturePower = 10,
            MaxFuel = 70,
            ConsumeFuel = true,
            LowFuelThreshold = 30,
            CounterMul = 0.4f,
            CanTransportUnits = true,
            MaxTransportCapacity = 1,
            TransportUnitTypes = new[] { "Infantry" },
        },

        ["Bike"] = new UnitConfig
        {
            UnitId = "Bike",
            DisplayName = TranslateGroup.T("摩托兵"),
            MoveType = MoveType.Tire,
            MovePoints = 5,
            Cost = 2500,
            AnimPrefix = "bike",
            HasPrimaryWeapon = false,
            HasSecondaryWeapon = true,
            SecondaryAttack = 100,
            SecondaryAntiArmor = true,
            SecondaryAntiInfantry = true,
            CaptureAbility = CaptureAbility.CanCapture,
            CapturePower = 10,
            MaxFuel = 70,
            ConsumeFuel = true,
            LowFuelThreshold = 30,
            BaseAttack = 10,
        },

        // ==================== 坦克类 ====================
        ["LightTank"] = new UnitConfig
        {
            UnitId = "LightTank",
            DisplayName = TranslateGroup.T("轻型坦克"),
            MoveType = MoveType.Treads,
            MovePoints = 6,
            Cost = 7000,
            AnimPrefix = "lighttank",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 9,
            PrimaryAntiArmor = true,
            PrimaryAntiInfantry = true,
            HasSecondaryWeapon = true,
            SecondaryAttack = 100,
            SecondaryAntiArmor = true,
            SecondaryAntiInfantry = true,
            MaxFuel = 70,
            ConsumeFuel = true,
            LowFuelThreshold = 30,
            IsArmoredUnit = true,
            CanTransportUnits = true,
            MaxTransportCapacity = 2,
            TransportUnitTypes = new[] { "Infantry", "Mech" },
        },

        ["Rhino"] = new UnitConfig
        {
            UnitId = "Rhino",
            DisplayName = TranslateGroup.T("犀牛"),
            MoveType = MoveType.Treads,
            MovePoints = 5,
            Cost = 16000,
            AnimPrefix = "rhino",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 8,
            PrimaryAntiArmor = true,
            PrimaryAntiInfantry = true,
            HasSecondaryWeapon = true,
            SecondaryAttack = 100,
            SecondaryAntiInfantry = true,
            SecondaryAntiArmor = false,
            MaxFuel = 80,
            ConsumeFuel = true,
            IsArmoredUnit = true,
            VisionRange = 4,
        },

        ["MdTank"] = new UnitConfig
        {
            UnitId = "MdTank",
            DisplayName = TranslateGroup.T("重型坦克"),
            MoveType = MoveType.Treads,
            MovePoints = 5,
            Cost = 16000,
            AnimPrefix = "mdtank",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 8,
            PrimaryAntiArmor = true,
            PrimaryAntiInfantry = true,
            HasSecondaryWeapon = true,
            SecondaryAttack = 100,
            SecondaryAntiInfantry = true,
            SecondaryAntiArmor = false,
            MaxFuel = 50,
            ConsumeFuel = true,
            IsArmoredUnit = true,
        },

        // ==================== 支援/间接 ====================
        ["Recon"] = new UnitConfig
        {
            UnitId = "Recon",
            DisplayName = TranslateGroup.T("侦察车"),
            MoveType = MoveType.Tire,
            MovePoints = 8,
            Cost = 4000,
            AnimPrefix = "recon",
            HasPrimaryWeapon = false,
            HasSecondaryWeapon = true,
            SecondaryAttack = 100,
            MaxFuel = 80,
            ConsumeFuel = true,
            LowFuelThreshold = 30,
        },

        ["APC"] = new UnitConfig
        {
            UnitId = "APC",
            DisplayName = TranslateGroup.T("装甲运兵车"),
            MoveType = MoveType.Treads,
            MovePoints = 6,
            Cost = 5000,
            AnimPrefix = "apc",
            AttackType = AttackType.NoAttack,
            HasPrimaryWeapon = false,
            HasSecondaryWeapon = false,
            MaxFuel = 70,
            ConsumeFuel = true,
            CanTransportUnits = true,
            CanSupplyUnits = true,
            MaxTransportCapacity = 1,
            TransportUnitTypes = new[] { "Infantry", "Mech" },
            CounterMul = 0f,
        },

        ["Artillery"] = new UnitConfig
        {
            UnitId = "Artillery",
            DisplayName = TranslateGroup.T("自行火炮"),
            MoveType = MoveType.Treads,
            MovePoints = 5,
            Cost = 6000,
            AnimPrefix = "artillery",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 9,
            PrimaryAntiArmor = true,
            PrimaryAntiInfantry = true,
            HasSecondaryWeapon = false,
            MinAttackRange = 2,
            MaxAttackRange = 3,
            UseMinMaxAttackRange = true,
            CanAttackAfterMoving = false,
            CannotCounterWhenAttacked = true,
            CanCounterWhenDefending = false,
            MaxFuel = 50,
            ConsumeFuel = true,
            CounterMul = 0.3f,
        },

        ["Rocket"] = new UnitConfig
        {
            UnitId = "Rocket",
            DisplayName = TranslateGroup.T("火箭炮"),
            MoveType = MoveType.Tire,
            MovePoints = 4,
            Cost = 15000,
            AnimPrefix = "rocket",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 6,
            PrimaryAntiArmor = true,
            PrimaryAntiInfantry = true,
            HasSecondaryWeapon = false,
            MinAttackRange = 3,
            MaxAttackRange = 5,
            UseMinMaxAttackRange = true,
            CanAttackAfterMoving = false,
            CannotCounterWhenAttacked = true,
            CanCounterWhenDefending = false,
            MaxFuel = 50,
            ConsumeFuel = true,
            CounterMul = 0.3f,
        },

        ["AntiAir"] = new UnitConfig
        {
            UnitId = "AntiAir",
            DisplayName = TranslateGroup.T("防空高炮"),
            MoveType = MoveType.Treads,
            MovePoints = 6,
            Cost = 8000,
            AnimPrefix = "antiair",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 9,
            PrimaryAntiArmor = true,
            PrimaryAntiInfantry = true,
            HasSecondaryWeapon = false,
            MaxFuel = 60,
            ConsumeFuel = true,
        },

        ["AntiTank"] = new UnitConfig
        {
            UnitId = "AntiTank",
            DisplayName = TranslateGroup.T("反坦克炮"),
            MoveType = MoveType.Tire,
            MovePoints = 4,
            Cost = 11000,
            AnimPrefix = "antitank",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 6,
            PrimaryAntiArmor = true,
            PrimaryAntiInfantry = true,
            HasSecondaryWeapon = false,
            MinAttackRange = 1,
            MaxAttackRange = 3,
            UseMinMaxAttackRange = true,
            CanAttackAfterMoving = false,
            CannotCounterWhenAttacked = true,
            CanCounterWhenDefending = true,
            CanCounterAtRange = false,
            MaxFuel = 50,
            ConsumeFuel = true,
            CounterMul = 0.3f,
            BaseAttack = 550,
        },

        ["Flare"] = new UnitConfig
        {
            UnitId = "Flare",
            DisplayName = TranslateGroup.T("照明车"),
            MoveType = MoveType.Treads,
            MovePoints = 5,
            Cost = 5000,
            AnimPrefix = "flare",
            HasPrimaryWeapon = false,
            HasSecondaryWeapon = true,
            SecondaryAttack = 100,
            SecondaryAntiArmor = true,
            SecondaryAntiInfantry = true,
            MaxFuel = 60,
            ConsumeFuel = true,
            LowFuelThreshold = 30,
            CanIlluminate = true,
        },

        ["PipeRunner"] = new UnitConfig
        {
            UnitId = "PipeRunner",
            DisplayName = TranslateGroup.T("管道炮"),
            MoveType = MoveType.PipeRunner,
            MovePoints = 5,
            Cost = 20000,
            AnimPrefix = "piperunner",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 99,
            PrimaryHasLimitedAmmo = false,
            PrimaryAntiArmor = true,
            PrimaryAntiInfantry = true,
            HasSecondaryWeapon = false,
            MinAttackRange = 2,
            MaxAttackRange = 5,
            UseMinMaxAttackRange = true,
            CanAttackAfterMoving = false,
            CannotCounterWhenAttacked = true,
            CanCounterWhenDefending = false,
            IsArmoredUnit = true,
        },

        // ==================== 海军 ====================
        ["RepairBoat"] = new UnitConfig
        {
            UnitId = "RepairBoat",
            DisplayName = TranslateGroup.T("维修艇"),
            MoveType = MoveType.Hover,
            MovePoints = 7,
            Cost = 7500,
            AnimPrefix = "repairboat",
            AttackType = AttackType.NoAttack,
            HasPrimaryWeapon = false,
            HasSecondaryWeapon = false,
            MaxFuel = 50,
            ConsumeFuel = true,
            DailyFuelConsumption = 1,
            DestroyOnOutOfFuel = true,
            LowFuelThreshold = 10,
            BaseDefense = 0,
            CounterMul = 0f,
            CanTransportUnits = true,
            CanSupplyUnits = false,
            MaxTransportCapacity = 1,
            TransportUnitTypes = new[] { "Infantry", "Mech", "Bike" },
            VisionRange = 1,
        },

        // ==================== 特殊 ====================
        ["Oozium"] = new UnitConfig
        {
            UnitId = "Oozium",
            DisplayName = TranslateGroup.T("史莱姆"),
            MoveType = MoveType.Oozium,
            MovePoints = 1,
            Cost = 20000,
            AnimPrefix = "oozium",
            HasPrimaryWeapon = false,
            HasSecondaryWeapon = false,
            CanDevour = true,
            OverlapType = UnitOverlapType.Oozium,
        },

        // ==================== 天军（原地图事件单位，2026-11-05 入册走可生产通道）====================
        ["ForceSatellite"] = new UnitConfig
        {
            UnitId = "ForceSatellite",
            DisplayName = TranslateGroup.T("力场卫星"),
            MoveType = MoveType.SpaceShiper,
            MovePoints = 6,
            Cost = 28000,                       // 造价暂定（超级兵器档），开创人可改
            AnimPrefix = "ForceSatellite",
            HasPrimaryWeapon = false,
            HasSecondaryWeapon = false,
            AttackType = AttackType.NoAttack,   // 无武器：仅力场支援
            VisionRange = 3,
        },
        ["AttackSatellite"] = new UnitConfig
        {
            UnitId = "AttackSatellite",
            DisplayName = TranslateGroup.T("黑琥珀"),
            MoveType = MoveType.SpaceShiper,
            MovePoints = 6,
            Cost = 32000,                       // 造价暂定（含天基打击溢价），开创人可改
            AnimPrefix = "AttackSatellite",
            HasPrimaryWeapon = true,
            VisionRange = 3,
        },

        ["FlyBomb"] = new UnitConfig
        {
            UnitId = "FlyBomb",
            DisplayName = TranslateGroup.T("飞弹"),
            MoveType = MoveType.Missile,
            MovePoints = 9,
            Cost = 25000,
            AnimPrefix = "flybomb",
            HasPrimaryWeapon = false,
            HasSecondaryWeapon = false,
            AttackType = AttackType.NoAttack,
            CanExplode = true,
            IsAirUnit = true,
            MaxFuel = 45,
            ConsumeFuel = true,
            DailyFuelConsumption = 5,
            DestroyOnOutOfFuel = true,
        },

        // ==================== 战斗机（2026-10-28 正式启用）====================
        ["Fighter"] = new UnitConfig
        {
            UnitId = "Fighter",
            DisplayName = TranslateGroup.T("战斗机"),
            MoveType = MoveType.AirPlane,
            MovePoints = 9,
            Cost = 20000,
            AnimPrefix = "fighter",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 9,
            PrimaryHasLimitedAmmo = true,
            IsAirUnit = true,
            VisionRange = 2,
            MaxFuel = 99,
            ConsumeFuel = true,
            DailyFuelConsumption = 5,
            DestroyOnOutOfFuel = true,
        },
        // ==================== 轰炸机（2026-10-28 紧随战斗机服役）====================
        ["Bomber"] = new UnitConfig
        {
            UnitId = "Bomber",
            DisplayName = TranslateGroup.T("轰炸机"),
            MoveType = MoveType.AirPlane,
            MovePoints = 7,
            Cost = 22000,
            AnimPrefix = "bomber",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 9,
            PrimaryHasLimitedAmmo = true,
            IsAirUnit = true,
            VisionRange = 2,
            MaxFuel = 99,
            ConsumeFuel = true,
            DailyFuelConsumption = 5,
            DestroyOnOutOfFuel = true,
        },
        // ==================== 武装直升机（2026-10-28 空军补齐）====================
        ["BCopter"] = new UnitConfig
        {
            UnitId = "BCopter",
            DisplayName = TranslateGroup.T("武装直升机"),
            MoveType = MoveType.HeliCopter,
            MovePoints = 6,
            Cost = 9000,
            AnimPrefix = "bcopter",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 6,
            PrimaryHasLimitedAmmo = true,
            IsAirUnit = true,
            VisionRange = 3,
            MaxFuel = 99,
            ConsumeFuel = true,
            DailyFuelConsumption = 2,
            DestroyOnOutOfFuel = true,
        },
        // ==================== 运输直升机（2026-10-28 空军补齐）====================
        ["TCopter"] = new UnitConfig
        {
            UnitId = "TCopter",
            DisplayName = TranslateGroup.T("运输直升机"),
            MoveType = MoveType.HeliCopter,
            MovePoints = 6,
            Cost = 5000,
            AnimPrefix = "tcopter",
            HasPrimaryWeapon = false,
            IsAirUnit = true,
            VisionRange = 2,
            MaxFuel = 99,
            ConsumeFuel = true,
            DailyFuelConsumption = 2,
            DestroyOnOutOfFuel = true,
        },
        // ==================== 防空导弹车（2026-10-29 空军克星）====================
        ["AntiAirMissile"] = new UnitConfig
        {
            UnitId = "AntiAirMissile",
            DisplayName = TranslateGroup.T("防空导弹"),
            MoveType = MoveType.Tire,
            MovePoints = 4,
            Cost = 12000,
            AnimPrefix = "aamissile",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 6,
            PrimaryHasLimitedAmmo = true,
            IsAirUnit = false,
            VisionRange = 5,
            MaxFuel = 50,
            ConsumeFuel = false,
        },
        // ==================== 隐身战机（2026-10-29 隐身模块首任用户，AW3官方值）====================
        ["Stealth"] = new UnitConfig
        {
            UnitId = "Stealth",
            DisplayName = TranslateGroup.T("隐身战机"),
            MoveType = MoveType.AirPlane,
            MovePoints = 6,
            Cost = 24000,
            AnimPrefix = "stealth",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 6,
            PrimaryHasLimitedAmmo = true,
            IsAirUnit = true,
            VisionRange = 4,
            MaxFuel = 60,
            ConsumeFuel = true,
            DailyFuelConsumption = 5,
            DestroyOnOutOfFuel = true,
        },
        // ==================== 机械狗（电子嗅探者）====================
        ["Dog"] = new UnitConfig
        {
            UnitId = "Dog",
            DisplayName = TranslateGroup.T("机械狗"),
            MoveType = MoveType.Mech,
            MovePoints = 6,
            Cost = 1500,
            AnimPrefix = "dog",
            HasPrimaryWeapon = true,
            PrimaryHasLimitedAmmo = false,
            IsAirUnit = false,
            VisionRange = 5,
            MaxFuel = 60,
            ConsumeFuel = true,
            DailyFuelConsumption = 2,
            DestroyOnOutOfFuel = true,
        },

            // ==================== 海军线（AW3 原作值；炮艇 DoR 跨作适配） ====================
        ["Battleship"] = new UnitConfig
        {
            UnitId = "Battleship",
            DisplayName = TranslateGroup.T("战列舰"),
            MoveType = MoveType.Naval,
            MovePoints = 5,
            Cost = 28000,
            AnimPrefix = "battleship",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 9,
            PrimaryHasLimitedAmmo = true,
            MinAttackRange = 2,
            MaxAttackRange = 6,
            IsAirUnit = false,
            VisionRange = 2,
            MaxFuel = 99,
            ConsumeFuel = true,
            DailyFuelConsumption = 1,
        },
        ["Cruiser"] = new UnitConfig
        {
            UnitId = "Cruiser",
            DisplayName = TranslateGroup.T("巡洋舰"),
            MoveType = MoveType.Naval,
            MovePoints = 6,
            Cost = 18000,
            AnimPrefix = "cruiser",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 9,
            PrimaryHasLimitedAmmo = true,
            MinAttackRange = 1,
            MaxAttackRange = 1,
            IsAirUnit = false,
            VisionRange = 3,
            MaxFuel = 99,
            ConsumeFuel = true,
            DailyFuelConsumption = 1,
        },
        ["Submarine"] = new UnitConfig
        {
            UnitId = "Submarine",
            DisplayName = TranslateGroup.T("潜艇"),
            MoveType = MoveType.Naval,
            MovePoints = 5,
            Cost = 20000,
            AnimPrefix = "submarine",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 6,
            PrimaryHasLimitedAmmo = true,
            MinAttackRange = 1,
            MaxAttackRange = 1,
            IsAirUnit = false,
            VisionRange = 5,
            MaxFuel = 60,
            ConsumeFuel = true,
            DailyFuelConsumption = 1,
        },
        ["Lander"] = new UnitConfig
        {
            UnitId = "Lander",
            DisplayName = TranslateGroup.T("登陆艇"),
            MoveType = MoveType.Naval,
            MovePoints = 6,
            Cost = 12000,
            AnimPrefix = "lander",
            HasPrimaryWeapon = false,
            IsAirUnit = false,
            VisionRange = 1,
            MaxFuel = 99,
            ConsumeFuel = true,
            DailyFuelConsumption = 1,
        },
        ["Carrier"] = new UnitConfig
        {
            UnitId = "Carrier",
            DisplayName = TranslateGroup.T("航母"),
            MoveType = MoveType.Naval,
            MovePoints = 5,
            Cost = 30000,
            AnimPrefix = "carrier",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 9,
            PrimaryHasLimitedAmmo = true,
            MinAttackRange = 3,
            MaxAttackRange = 8,
            IsAirUnit = false,
            VisionRange = 4,
            MaxFuel = 99,
            ConsumeFuel = true,
            DailyFuelConsumption = 1,
        },
        ["Gunboat"] = new UnitConfig
        {
            UnitId = "Gunboat",
            DisplayName = TranslateGroup.T("炮艇"),
            MoveType = MoveType.Naval,
            MovePoints = 7,
            Cost = 6000,
            AnimPrefix = "gunboat",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 1,
            PrimaryHasLimitedAmmo = true,
            MinAttackRange = 1,
            MaxAttackRange = 1,
            IsAirUnit = false,
            VisionRange = 1,
            MaxFuel = 99,
            ConsumeFuel = true,
            DailyFuelConsumption = 1,
        },
        // ==================== 新增原版兵种（DS / DoR） ====================
        ["Neotank"] = new UnitConfig
        {
            UnitId = "Neotank",
            DisplayName = TranslateGroup.T("新型坦克"),
            MoveType = MoveType.Treads,
            MovePoints = 6,
            Cost = 22000,
            AnimPrefix = "neotank",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 9,
            PrimaryHasLimitedAmmo = true,
            HasSecondaryWeapon = true,
            MinAttackRange = 1,
            MaxAttackRange = 1,
            CanAttackAfterMoving = true,
            IsAirUnit = false,
            VisionRange = 1,
            MaxFuel = 99,
            ConsumeFuel = true,
            DailyFuelConsumption = 0,
        },
        ["Megatank"] = new UnitConfig
        {
            UnitId = "Megatank",
            DisplayName = TranslateGroup.T("巨型坦克"),
            MoveType = MoveType.Treads,
            MovePoints = 4,
            Cost = 28000,
            AnimPrefix = "megatank",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 3,
            PrimaryHasLimitedAmmo = true,
            HasSecondaryWeapon = true,
            MinAttackRange = 1,
            MaxAttackRange = 1,
            CanAttackAfterMoving = true,
            IsAirUnit = false,
            VisionRange = 1,
            MaxFuel = 50,
            ConsumeFuel = true,
            DailyFuelConsumption = 0,
        },
        ["WarTank"] = new UnitConfig
        {
            UnitId = "WarTank",
            DisplayName = TranslateGroup.T("弩级坦克"),
            MoveType = MoveType.Treads,
            MovePoints = 4,
            Cost = 22000,
            AnimPrefix = "wartank",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 5,
            PrimaryHasLimitedAmmo = true,
            HasSecondaryWeapon = true,
            MinAttackRange = 1,
            MaxAttackRange = 1,
            CanAttackAfterMoving = true,
            IsAirUnit = false,
            VisionRange = 2,
            MaxFuel = 50,
            ConsumeFuel = true,
            DailyFuelConsumption = 0,
        },
        ["Rig"] = new UnitConfig
        {
            UnitId = "Rig",
            DisplayName = TranslateGroup.T("工程车"),
            MoveType = MoveType.Treads,
            MovePoints = 6,
            Cost = 5000,
            AnimPrefix = "rig",
            HasPrimaryWeapon = false,
            MaxPrimaryAmmo = 0,
            PrimaryHasLimitedAmmo = true,
            HasSecondaryWeapon = false,
            MinAttackRange = 1,
            MaxAttackRange = 1,
            CanAttackAfterMoving = true,
            IsAirUnit = false,
            VisionRange = 1,
            MaxFuel = 99,
            ConsumeFuel = true,
            DailyFuelConsumption = 0,
        },
        ["PipeWorker"] = new UnitConfig
        {
            UnitId = "PipeWorker",
            DisplayName = TranslateGroup.T("管道构造者"),
            MoveType = MoveType.Treads,          // 移动方式与新型坦克一致
            MovePoints = 6,
            Cost = 7500,
            AnimPrefix = "pipeworker",
            HasPrimaryWeapon = false,             // 无攻击力
            MaxPrimaryAmmo = 0,
            PrimaryHasLimitedAmmo = true,
            HasSecondaryWeapon = false,
            MinAttackRange = 1,
            MaxAttackRange = 1,
            CanAttackAfterMoving = false,
            IsAirUnit = false,
            VisionRange = 1,
            MaxFuel = 99,
            ConsumeFuel = true,
            DailyFuelConsumption = 0,
        },
        ["Duster"] = new UnitConfig
        {
            UnitId = "Duster",
            DisplayName = TranslateGroup.T("螺旋桨机"),
            MoveType = MoveType.AirPlane,
            MovePoints = 8,
            Cost = 13000,
            AnimPrefix = "duster",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 9,
            PrimaryHasLimitedAmmo = true,
            HasSecondaryWeapon = false,
            MinAttackRange = 1,
            MaxAttackRange = 1,
            CanAttackAfterMoving = true,
            IsAirUnit = true,
            VisionRange = 4,
            MaxFuel = 99,
            ConsumeFuel = true,
            DailyFuelConsumption = 5,
        },
        ["Seaplane"] = new UnitConfig
        {
            UnitId = "Seaplane",
            DisplayName = TranslateGroup.T("水上飞机"),
            MoveType = MoveType.AirPlane,
            MovePoints = 7,
            Cost = 15000,
            AnimPrefix = "seaplane",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 3,
            PrimaryHasLimitedAmmo = true,
            HasSecondaryWeapon = false,
            MinAttackRange = 1,
            MaxAttackRange = 1,
            CanAttackAfterMoving = true,
            IsAirUnit = true,
            VisionRange = 4,
            MaxFuel = 40,
            ConsumeFuel = true,
            DailyFuelConsumption = 5,
        },
        ["ProducibleBoat"] = new UnitConfig
        {
            UnitId = "ProducibleBoat",
            DisplayName = TranslateGroup.T("生产舰"),
            MoveType = MoveType.Naval,
            MovePoints = 5,
            Cost = 28000,
            AnimPrefix = "producibleboat",
            HasPrimaryWeapon = false,
            MaxPrimaryAmmo = 0,
            PrimaryHasLimitedAmmo = true,
            HasSecondaryWeapon = true,
            MinAttackRange = 1,
            MaxAttackRange = 1,
            CanAttackAfterMoving = true,
            IsAirUnit = false,
            VisionRange = 4,
            MaxFuel = 99,
            ConsumeFuel = true,
            DailyFuelConsumption = 1,
        },
        ["KamikazeBoat"] = new UnitConfig
        {
            UnitId = "KamikazeBoat",
            DisplayName = TranslateGroup.T("突击舰"),
            MoveType = MoveType.Naval,
            MovePoints = 5,
            Cost = 25000,
            AnimPrefix = "kamikazeboat",
            HasPrimaryWeapon = true,
            MaxPrimaryAmmo = 6,
            PrimaryHasLimitedAmmo = true,
            HasSecondaryWeapon = false,
            MinAttackRange = 3,
            MaxAttackRange = 5,
            CanAttackAfterMoving = true,
            IsAirUnit = false,
            VisionRange = 3,
            MaxFuel = 99,
            ConsumeFuel = true,
            DailyFuelConsumption = 1,
        },
};

    /// <summary>
    /// 获取兵种配置，找不到返回null
    /// </summary>
    public static UnitConfig Get(string unitId)
    {
        if (Configs.TryGetValue(unitId, out var config))
            return config;
        return null;
    }

    /// <summary>
    /// 获取所有兵种ID列表
    /// </summary>
    public static List<string> GetAllUnitIds() => new List<string>(Configs.Keys);

    /// <summary>
    /// 判断是否存在该兵种
    /// </summary>
    public static bool Has(string unitId) => Configs.ContainsKey(unitId);
}
