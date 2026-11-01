using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · Movement（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{

// 卸下后不能移动的单位记录（静态，所有运输单位共享）
public static Dictionary<Infantry, int> unitsCannotMove = new Dictionary<Infantry, int>();

    // ========== 电磁爆麻痹状态（Von-Bolt 电磁爆：不能操作，持续N回合）==========
    public int stunTurnsRemaining = 0;            // 剩余麻痹回合（运行时；由施加方Export决定时长）
    public bool IsStunned => stunTurnsRemaining > 0;

    // ========== ✅ CO 模块25（状态系统）：燃烧/虚弱（麻痹复用上面的 stun）==========
    public int burnTurnsRemaining = 0;   // 剩余燃烧回合
    public int burnDamage = 0;          // 每回合掉血（HP 1-10，千分制×10结算）
    public int weakTurnsRemaining = 0;   // 剩余虚弱回合
    public int weakPercent = 0;         // 攻击降低百分比
    public bool IsBurning => burnTurnsRemaining > 0;
    public bool IsWeakened => weakTurnsRemaining > 0;

    /// <summary>施加麻痹：持续 turns 回合（本方回合开始递减；麻痹中不能移动/攻击/行动）</summary>
    public void ApplyStun(int turns)
    {
        if (turns <= 0) return;
        stunTurnsRemaining = Mathf.Max(stunTurnsRemaining, turns);
        hasActed = true;
        state = UnitState.Acted;
        SetWaitVisual(true);
    }

    public virtual bool CanMove()
    {
        if (IsStunned) return false;   // ✅ 麻痹中不能移动
        if (!consumeFuel) return true;
        return fuel > 0;
    }

    /// <summary>
    /// 移动中断判定（每次移动完成时调用）：
    /// ✅ 生产/建造「可否移动」开关 = 移动会不会打断进度——
    ///    内部模式看各自开关（关=移动即中断）；外部模式一律移动即中断。
    /// </summary>
    public void OnMovedInterruptCheck()
    {
        if (isProducing && (!productionInternal || !canMoveWhileProducing)) CancelProduction();
        if (isConstructing && (!constructionInternal || !canMoveWhileBuilding)) CancelConstruction();
    }

    /// <summary>
    /// 按格子取移动开销（含设施覆盖）。
    /// ✅ 港口：海军/气垫单位可进入港口格（与原作一致——舰船可靠泊、在港口生产/补给/维修）。
    /// 其余格子仍按 gridType 的移动开销表。
    /// </summary>
    public virtual int GetMoveCostOnGrid(Grids targetGrid)
    {
        if (targetGrid != null && targetGrid.city is Port
            && (moveType == MoveType.Naval || moveType == MoveType.Hover))
            return 1;
        return GetMoveCost(targetGrid != null ? targetGrid.gridType : GridType.GROUND);
    }

    /// <summary>✅ 解析检查器里的 terrainMoveCostOverrides（"BRIDGE=1"）</summary>
    public bool TryGetTerrainMoveCostOverride(GridType gridType, out int cost)
    {
        cost = 0;
        if (terrainMoveCostOverrides == null || terrainMoveCostOverrides.Count == 0) return false;
        foreach (var raw in terrainMoveCostOverrides)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            int sep = raw.IndexOf('=');
            if (sep <= 0) continue;
            if (!System.Enum.TryParse<GridType>(raw[..sep].Trim(), true, out var g) || g != gridType) continue;
            if (int.TryParse(raw[(sep + 1)..].Trim(), out cost)) return true;
        }
        return false;
    }

    public virtual void ConsumeFuel(int amount)
    {
        if (!consumeFuel) return;
        fuel -= amount;
    }

    // ✅ 每日回合开始时自动消耗燃料
    public virtual void ConsumeDailyFuel()
    {
        bool hasStealthCost = isStealthed && HasAnyStealthCost();
        if ((!consumeFuel || dailyFuelConsumption <= 0) && !hasStealthCost) return;
        if (consumeFuel && dailyFuelConsumption > 0) fuel -= dailyFuelConsumption;

        stealthFlareRevealed = false;   // 照明临时显形每回合刷新
        ApplyStealthDailyCost();   // ✅ 隐身日耗（独立于燃料开关）
        MaintainScentTracking();
        PerformScentScan();

        // ✅ 燃料耗尽坠毁：统一在日耗全部结算后判定一次（修复：此前与 ApplyStealthDailyCost 各判一次，隐身时重复销毁）
        if (fuel <= 0 && destroyOnOutOfFuel && !pendingFuelDestroy)
        {
            pendingFuelDestroy = true;
            // 延迟一帧销毁，避免在迭代中修改集合
            CallDeferred(nameof(DestroySelf));
        }
    }
    private bool pendingFuelDestroy = false;


public virtual int GetMoveCost(GridType gridType)
{
    if (TryGetTerrainMoveCostOverride(gridType, out int overrideCost)) return overrideCost;   // ✅ 单位级地形覆盖（如突击舰过桥下/浅滩）
    MoveType moveType = (isStealthed && stealthMoveTypeOverride >= 0) ? (MoveType)stealthMoveTypeOverride : this.moveType;   // ✅ 隐身移动方式改写（局部遮蔽）
        var extraSave = ExtraSave.Instance;
    if (extraSave != null)
    {
        string terrainName = gridType.ToString();
        string moveTypeName = moveType.ToString();
        int customCost = extraSave.GetMoveCost(moveTypeName, terrainName);
        if (customCost >= 0) return customCost; // -1 表示走内置逻辑，继续原有代码
    }
    return moveType switch
    {
        // ========== Infantry（步兵）==========
        MoveType.Infantry => gridType switch
        {
            GridType.GROUND => 1,
            GridType.FOREST => 1,

            GridType.ROAD => 1,
            GridType.RIVER => 2,
            GridType.HILL => 2,
            GridType.BEACH => 1,
            GridType.TP => 0,
            GridType.REEF => 999,
            GridType.WHIRLPOOL => 999,
            GridType.LAVASIDE => 1,
            GridType.SEAFOG => 999,
            GridType.LANDFOG => 3,
            GridType.WATERFALL => 999,
            GridType.CLIFF => 999,
            GridType.SLOPE => 2,
            GridType.CAVE => 1,
            GridType.HOLE => 1,
            GridType.SEA => 999,
            GridType.METEORITE => 999,
            GridType.PIPE => 999,
            GridType.LAVA => 999,
            GridType.PIPESEAM => 999,  
            GridType.PLASMA => 999,   // 等离子：完全不可通行（含空军，能量墙）
            GridType.CREEPER => 999,  // 蔓生藤蔓：不可通行
            GridType.GATE => 1,       // 门地形可通行（通行由Gate兵器白名单控制）
            // ✅ CW墙系+三气候皮肤组（消耗镜像基础地形）
            GridType.WALL => 999,
            GridType.WEAK_WALL => 999,
            GridType.WASTELAND => 1,
            GridType.RUINS2 => 2,
            GridType.RUINS3 => 2,
            GridType.DESERT_GROUND => 1,
            GridType.SNOW_GROUND => 1,
            GridType.WASTE_GROUND => 1,
            GridType.DESERT_ROAD => 1,
            GridType.SNOW_ROAD => 1,
            GridType.WASTE_ROAD => 1,
            GridType.DESERT_FOREST => 1,
            GridType.SNOW_FOREST => 1,
            GridType.WASTE_FOREST => 1,
            GridType.DESERT_HILL => 2,
            GridType.SNOW_HILL => 2,
            GridType.WASTE_HILL => 2,
            GridType.DESERT_RUINS => 2,
            GridType.SNOW_RUINS => 2,
            GridType.WASTE_RUINS => 2,
            GridType.DESERT_WASTELAND => 1,
            GridType.SNOW_WASTELAND => 1,
            GridType.WASTE_WASTELAND => 1,
            GridType.DESERT_PIPE => 999,
            GridType.SNOW_PIPE => 999,
            GridType.WASTE_PIPE => 999,
            GridType.DESERT_PIPESEAM => 999,
            GridType.SNOW_PIPESEAM => 999,
            GridType.WASTE_PIPESEAM => 999,
            GridType.DESERT_BROKENPIPE => 3,
            GridType.SNOW_BROKENPIPE => 3,
            GridType.WASTE_BROKENPIPE => 3,
            // 新增地形
            GridType.TRACK => 2,
            GridType.STATION => 1,
            GridType.BRIDGE => 1,
            GridType.LAVABRIDGE => 1,
            GridType.PASSABLEPIPE => 1,
            GridType.SHIPGATE => 1,
            GridType.OVERPASS => 1,
            GridType.BROKENPIPE => 3,
            GridType.RUINS => 2,
            GridType.BROKENTRACK => 2,
            GridType.LAVAFOG => 999, 
            _ => 999
        },

        // ========== Mech（机甲）==========
        MoveType.Mech => gridType switch
        {
            GridType.GROUND => 1,
            GridType.FOREST => 1,

            GridType.ROAD => 1,
            GridType.RIVER => 1,
            GridType.HILL => 1,
            GridType.BEACH => 1,
            GridType.TP => 0,
            GridType.REEF => 999,
            GridType.WHIRLPOOL => 999,
            GridType.LAVASIDE => 1,
            GridType.SEAFOG => 999,
            GridType.LANDFOG => 3,
            GridType.WATERFALL => 999,
            GridType.CLIFF => 999,
            GridType.SLOPE => 2,
            GridType.CAVE => 1,
            GridType.HOLE => 1,
            GridType.SEA => 999,
            GridType.METEORITE => 999,
            GridType.PIPE => 999,
            GridType.LAVA => 999,
            GridType.PIPESEAM => 999, 
            GridType.PLASMA => 999,   // 等离子：完全不可通行（含空军，能量墙）
            GridType.CREEPER => 999,  // 蔓生藤蔓：不可通行
            GridType.GATE => 1,       // 门地形可通行（通行由Gate兵器白名单控制）
            // ✅ CW墙系+三气候皮肤组（消耗镜像基础地形）
            GridType.WALL => 999,
            GridType.WEAK_WALL => 999,
            GridType.WASTELAND => 1,
            GridType.RUINS2 => 1,
            GridType.RUINS3 => 1,
            GridType.DESERT_GROUND => 1,
            GridType.SNOW_GROUND => 1,
            GridType.WASTE_GROUND => 1,
            GridType.DESERT_ROAD => 1,
            GridType.SNOW_ROAD => 1,
            GridType.WASTE_ROAD => 1,
            GridType.DESERT_FOREST => 1,
            GridType.SNOW_FOREST => 1,
            GridType.WASTE_FOREST => 1,
            GridType.DESERT_HILL => 1,
            GridType.SNOW_HILL => 1,
            GridType.WASTE_HILL => 1,
            GridType.DESERT_RUINS => 1,
            GridType.SNOW_RUINS => 1,
            GridType.WASTE_RUINS => 1,
            GridType.DESERT_WASTELAND => 1,
            GridType.SNOW_WASTELAND => 1,
            GridType.WASTE_WASTELAND => 1,
            GridType.DESERT_PIPE => 999,
            GridType.SNOW_PIPE => 999,
            GridType.WASTE_PIPE => 999,
            GridType.DESERT_PIPESEAM => 999,
            GridType.SNOW_PIPESEAM => 999,
            GridType.WASTE_PIPESEAM => 999,
            GridType.DESERT_BROKENPIPE => 2,
            GridType.SNOW_BROKENPIPE => 2,
            GridType.WASTE_BROKENPIPE => 2,
            // 新增地形
            GridType.TRACK => 1,
            GridType.STATION => 1,
            GridType.BRIDGE => 1,
            GridType.LAVABRIDGE => 1,
            GridType.PASSABLEPIPE => 1,
            GridType.SHIPGATE => 1,
            GridType.OVERPASS => 1,
            GridType.BROKENPIPE => 2,
            GridType.RUINS => 1,
            GridType.BROKENTRACK => 1,
            GridType.LAVAFOG => 999, 
            _ => 999
        },

        // ========== Oozium（史莱姆）==========
        MoveType.Oozium => gridType switch
        {
            GridType.GROUND => 1,
            GridType.FOREST => 1,

            GridType.ROAD => 1,
            GridType.SEA => 1,
            GridType.RIVER => 1,
            GridType.HILL => 1,
            GridType.LAVA => 1,
            GridType.BEACH => 1,
            GridType.TP => 0,
            GridType.REEF => 1,
            GridType.WHIRLPOOL => 2,
            GridType.LAVASIDE => 1,
            GridType.SEAFOG => 2,
            GridType.LANDFOG => 2,
            GridType.WATERFALL => 999,
            GridType.CLIFF => 1,
            GridType.SLOPE => 1,
            GridType.CAVE => 1,
            GridType.HOLE => 1,
            GridType.METEORITE => 999,
            GridType.PIPE => 999,
            GridType.PIPESEAM => 999,  // Oozium不能通过管道接缝
            GridType.PLASMA => 999,   // 等离子：完全不可通行（含空军，能量墙）
            GridType.CREEPER => 999,  // 蔓生藤蔓：不可通行
            GridType.GATE => 1,       // 门地形可通行（通行由Gate兵器白名单控制）
            // ✅ CW墙系+三气候皮肤组（消耗镜像基础地形）
            GridType.WALL => 999,
            GridType.WEAK_WALL => 999,
            GridType.WASTELAND => 1,
            GridType.RUINS2 => 1,
            GridType.RUINS3 => 1,
            GridType.DESERT_GROUND => 1,
            GridType.SNOW_GROUND => 1,
            GridType.WASTE_GROUND => 1,
            GridType.DESERT_ROAD => 1,
            GridType.SNOW_ROAD => 1,
            GridType.WASTE_ROAD => 1,
            GridType.DESERT_FOREST => 1,
            GridType.SNOW_FOREST => 1,
            GridType.WASTE_FOREST => 1,
            GridType.DESERT_HILL => 1,
            GridType.SNOW_HILL => 1,
            GridType.WASTE_HILL => 1,
            GridType.DESERT_RUINS => 1,
            GridType.SNOW_RUINS => 1,
            GridType.WASTE_RUINS => 1,
            GridType.DESERT_WASTELAND => 1,
            GridType.SNOW_WASTELAND => 1,
            GridType.WASTE_WASTELAND => 1,
            GridType.DESERT_PIPE => 999,
            GridType.SNOW_PIPE => 999,
            GridType.WASTE_PIPE => 999,
            GridType.DESERT_PIPESEAM => 999,
            GridType.SNOW_PIPESEAM => 999,
            GridType.WASTE_PIPESEAM => 999,
            GridType.DESERT_BROKENPIPE => 1,
            GridType.SNOW_BROKENPIPE => 1,
            GridType.WASTE_BROKENPIPE => 1,
            // 新增地形
            GridType.TRACK => 1,
            GridType.STATION => 1,
            GridType.BRIDGE => 1,
            GridType.LAVABRIDGE => 1,
            GridType.PASSABLEPIPE => 1,
            GridType.SHIPGATE => 1,
            GridType.OVERPASS => 1,
            GridType.BROKENPIPE => 1,
            GridType.RUINS => 1,
            GridType.BROKENTRACK => 1,
            GridType.LAVAFOG => 2,  
            _ => 999
        },

        // ========== Treads（履带）==========
        MoveType.Treads => gridType switch
        {
            GridType.GROUND => 1,

            GridType.ROAD => 1,
            GridType.FOREST => 2,
            GridType.BEACH => 1,
            GridType.TP => 0,
            GridType.REEF => 999,
            GridType.WHIRLPOOL => 999,
            GridType.LAVASIDE => 1,
            GridType.SEAFOG => 999,
            GridType.LANDFOG => 3,
            GridType.WATERFALL => 999,
            GridType.CLIFF => 999,
            GridType.SLOPE => 2,
            GridType.CAVE => 1,
            GridType.HOLE => 1,
            GridType.SEA => 999,
            GridType.RIVER => 999,
            GridType.HILL => 999,
            GridType.METEORITE => 999,
            GridType.PIPE => 999,
            GridType.LAVA => 999,
            GridType.PIPESEAM => 999,  // 履带不能通过管道接缝
            GridType.PLASMA => 999,   // 等离子：完全不可通行（含空军，能量墙）
            GridType.CREEPER => 999,  // 蔓生藤蔓：不可通行
            GridType.GATE => 1,       // 门地形可通行（通行由Gate兵器白名单控制）
            // ✅ CW墙系+三气候皮肤组（消耗镜像基础地形）
            GridType.WALL => 999,
            GridType.WEAK_WALL => 999,
            GridType.WASTELAND => 2,
            GridType.RUINS2 => 3,
            GridType.RUINS3 => 3,
            GridType.DESERT_GROUND => 1,
            GridType.SNOW_GROUND => 1,
            GridType.WASTE_GROUND => 1,
            GridType.DESERT_ROAD => 1,
            GridType.SNOW_ROAD => 1,
            GridType.WASTE_ROAD => 1,
            GridType.DESERT_FOREST => 2,
            GridType.SNOW_FOREST => 2,
            GridType.WASTE_FOREST => 2,
            GridType.DESERT_HILL => 999,
            GridType.SNOW_HILL => 999,
            GridType.WASTE_HILL => 999,
            GridType.DESERT_RUINS => 3,
            GridType.SNOW_RUINS => 3,
            GridType.WASTE_RUINS => 3,
            GridType.DESERT_WASTELAND => 2,
            GridType.SNOW_WASTELAND => 2,
            GridType.WASTE_WASTELAND => 2,
            GridType.DESERT_PIPE => 999,
            GridType.SNOW_PIPE => 999,
            GridType.WASTE_PIPE => 999,
            GridType.DESERT_PIPESEAM => 999,
            GridType.SNOW_PIPESEAM => 999,
            GridType.WASTE_PIPESEAM => 999,
            GridType.DESERT_BROKENPIPE => 3,
            GridType.SNOW_BROKENPIPE => 3,
            GridType.WASTE_BROKENPIPE => 3,
            // 新增地形
            GridType.TRACK => 2,
            GridType.STATION => 1,
            GridType.BRIDGE => 1,
            GridType.LAVABRIDGE => 1,
            GridType.PASSABLEPIPE => 1,
            GridType.SHIPGATE => 1,
            GridType.OVERPASS => 1,
            GridType.BROKENPIPE => 3,
            GridType.RUINS => 3,
            GridType.BROKENTRACK => 3,
            GridType.LAVAFOG => 999,  
            _ => 999
        },

        // ========== Tire（轮胎）==========
        MoveType.Tire => gridType switch
        {

            GridType.ROAD => 1,
            GridType.GROUND => 2,
            GridType.FOREST => 3,
            GridType.BEACH => 1,
            GridType.TP => 0,
            GridType.REEF => 999,
            GridType.WHIRLPOOL => 999,
            GridType.LAVASIDE => 1,
            GridType.SEAFOG => 999,
            GridType.LANDFOG => 3,
            GridType.WATERFALL => 999,
            GridType.CLIFF => 999,
            GridType.SLOPE => 3,
            GridType.CAVE => 1,
            GridType.HOLE => 1,
            GridType.SEA => 999,
            GridType.RIVER => 999,
            GridType.HILL => 999,
            GridType.METEORITE => 999,
            GridType.PIPE => 999,
            GridType.LAVA => 999,
            GridType.PIPESEAM => 999, 
            GridType.PLASMA => 999,   // 等离子：完全不可通行（含空军，能量墙）
            GridType.CREEPER => 999,  // 蔓生藤蔓：不可通行
            GridType.GATE => 1,       // 门地形可通行（通行由Gate兵器白名单控制）
            // ✅ CW墙系+三气候皮肤组（消耗镜像基础地形）
            GridType.WALL => 999,
            GridType.WEAK_WALL => 999,
            GridType.WASTELAND => 3,
            GridType.RUINS2 => 3,
            GridType.RUINS3 => 3,
            GridType.DESERT_GROUND => 2,
            GridType.SNOW_GROUND => 2,
            GridType.WASTE_GROUND => 2,
            GridType.DESERT_ROAD => 1,
            GridType.SNOW_ROAD => 1,
            GridType.WASTE_ROAD => 1,
            GridType.DESERT_FOREST => 3,
            GridType.SNOW_FOREST => 3,
            GridType.WASTE_FOREST => 3,
            GridType.DESERT_HILL => 999,
            GridType.SNOW_HILL => 999,
            GridType.WASTE_HILL => 999,
            GridType.DESERT_RUINS => 3,
            GridType.SNOW_RUINS => 3,
            GridType.WASTE_RUINS => 3,
            GridType.DESERT_WASTELAND => 3,
            GridType.SNOW_WASTELAND => 3,
            GridType.WASTE_WASTELAND => 3,
            GridType.DESERT_PIPE => 999,
            GridType.SNOW_PIPE => 999,
            GridType.WASTE_PIPE => 999,
            GridType.DESERT_PIPESEAM => 999,
            GridType.SNOW_PIPESEAM => 999,
            GridType.WASTE_PIPESEAM => 999,
            GridType.DESERT_BROKENPIPE => 4,
            GridType.SNOW_BROKENPIPE => 4,
            GridType.WASTE_BROKENPIPE => 4,
            // 新增地形
            GridType.TRACK => 3,
            GridType.STATION => 2,
            GridType.BRIDGE => 2,
            GridType.LAVABRIDGE => 2,
            GridType.PASSABLEPIPE => 2,
            GridType.SHIPGATE => 2,
            GridType.OVERPASS => 2,
            GridType.BROKENPIPE => 4,
            GridType.RUINS => 3,
            GridType.BROKENTRACK => 3,
            GridType.LAVAFOG => 999, 
            _ => 999
        },

        // ========== Naval（海军）==========
        MoveType.Naval => gridType switch
        {
            GridType.SEA => 1,
            GridType.TP => 0,
            GridType.REEF => 2,
            GridType.WHIRLPOOL => 3,
            GridType.SEAFOG => 3,
            GridType.CAVE => 1,
            GridType.HOLE => 1,
            GridType.PIPESEAM => 999, 
            GridType.PLASMA => 999,   // 等离子：完全不可通行（含空军，能量墙）
            GridType.CREEPER => 999,  // 蔓生藤蔓：不可通行
            GridType.GATE => 1,       // 门地形可通行（通行由Gate兵器白名单控制）
            // ✅ CW墙系+三气候皮肤组（消耗镜像基础地形）
            GridType.WALL => 999,
            GridType.WEAK_WALL => 999,
            GridType.WASTELAND => 999,
            GridType.RUINS2 => 999,
            GridType.RUINS3 => 999,
            GridType.DESERT_GROUND => 999,
            GridType.SNOW_GROUND => 999,
            GridType.WASTE_GROUND => 999,
            GridType.DESERT_ROAD => 999,
            GridType.SNOW_ROAD => 999,
            GridType.WASTE_ROAD => 999,
            GridType.DESERT_FOREST => 999,
            GridType.SNOW_FOREST => 999,
            GridType.WASTE_FOREST => 999,
            GridType.DESERT_HILL => 999,
            GridType.SNOW_HILL => 999,
            GridType.WASTE_HILL => 999,
            GridType.DESERT_RUINS => 999,
            GridType.SNOW_RUINS => 999,
            GridType.WASTE_RUINS => 999,
            GridType.DESERT_WASTELAND => 999,
            GridType.SNOW_WASTELAND => 999,
            GridType.WASTE_WASTELAND => 999,
            GridType.DESERT_PIPE => 999,
            GridType.SNOW_PIPE => 999,
            GridType.WASTE_PIPE => 999,
            GridType.DESERT_PIPESEAM => 999,
            GridType.SNOW_PIPESEAM => 999,
            GridType.WASTE_PIPESEAM => 999,
            GridType.DESERT_BROKENPIPE => 999,
            GridType.SNOW_BROKENPIPE => 999,
            GridType.WASTE_BROKENPIPE => 999,
            GridType.GROUND => 999,
            GridType.FOREST => 999,

            GridType.ROAD => 999,
            GridType.RIVER => 999,
            GridType.HILL => 999,
            GridType.METEORITE => 999,
            GridType.PIPE => 999,
            GridType.LAVA => 999,
            GridType.BEACH => 999,
            GridType.LAVASIDE => 999,
            GridType.LANDFOG => 999,
            GridType.WATERFALL => 999,
            GridType.CLIFF => 999,
            GridType.SLOPE => 999,
            // 新增地形
            GridType.TRACK => 999,
            GridType.STATION => 999,
            GridType.BRIDGE => 2,
            GridType.LAVABRIDGE => 999,
            GridType.PASSABLEPIPE => 999,
            GridType.SHIPGATE => 2,
            GridType.OVERPASS => 2,
            GridType.BROKENPIPE => 999,
            GridType.RUINS => 999,
            GridType.BROKENTRACK => 999,
            GridType.LAVAFOG => 999, 
            _ => 999
        },

        // ========== AirPlane（战机）==========
        MoveType.AirPlane => gridType switch
        {
            GridType.GROUND => 1,
            GridType.FOREST => 1,

            GridType.ROAD => 1,
            GridType.SEA => 1,
            GridType.RIVER => 1,
            GridType.HILL => 1,
            GridType.LAVA => 1,
            GridType.BEACH => 1,
            GridType.TP => 0,
            GridType.REEF => 1,
            GridType.WHIRLPOOL => 1,
            GridType.LAVASIDE => 1,
            GridType.SEAFOG => 2,
            GridType.LANDFOG => 2,
            GridType.WATERFALL => 1,
            GridType.CLIFF => 1,
            GridType.SLOPE => 1,
            GridType.CAVE => 1,
            GridType.HOLE => 1,
            GridType.METEORITE => 999,
            GridType.PIPE => 999,
            GridType.PIPESEAM => 999,  // 战机不能通过管道接缝
            GridType.PLASMA => 999,   // 等离子：完全不可通行（含空军，能量墙）
            GridType.CREEPER => 999,  // 蔓生藤蔓：不可通行
            GridType.GATE => 1,       // 门地形可通行（通行由Gate兵器白名单控制）
            // ✅ CW墙系+三气候皮肤组（消耗镜像基础地形）
            GridType.WALL => 999,
            GridType.WEAK_WALL => 999,
            GridType.WASTELAND => 1,
            GridType.RUINS2 => 1,
            GridType.RUINS3 => 1,
            GridType.DESERT_GROUND => 1,
            GridType.SNOW_GROUND => 1,
            GridType.WASTE_GROUND => 1,
            GridType.DESERT_ROAD => 1,
            GridType.SNOW_ROAD => 1,
            GridType.WASTE_ROAD => 1,
            GridType.DESERT_FOREST => 1,
            GridType.SNOW_FOREST => 1,
            GridType.WASTE_FOREST => 1,
            GridType.DESERT_HILL => 1,
            GridType.SNOW_HILL => 1,
            GridType.WASTE_HILL => 1,
            GridType.DESERT_RUINS => 1,
            GridType.SNOW_RUINS => 1,
            GridType.WASTE_RUINS => 1,
            GridType.DESERT_WASTELAND => 1,
            GridType.SNOW_WASTELAND => 1,
            GridType.WASTE_WASTELAND => 1,
            GridType.DESERT_PIPE => 999,
            GridType.SNOW_PIPE => 999,
            GridType.WASTE_PIPE => 999,
            GridType.DESERT_PIPESEAM => 999,
            GridType.SNOW_PIPESEAM => 999,
            GridType.WASTE_PIPESEAM => 999,
            GridType.DESERT_BROKENPIPE => 1,
            GridType.SNOW_BROKENPIPE => 1,
            GridType.WASTE_BROKENPIPE => 1,
            // 新增地形
            GridType.TRACK => 1,
            GridType.STATION => 1,
            GridType.BRIDGE => 1,
            GridType.LAVABRIDGE => 1,
            GridType.PASSABLEPIPE => 1,
            GridType.SHIPGATE => 1,
            GridType.OVERPASS => 1,
            GridType.BROKENPIPE => 1,
            GridType.RUINS => 1,
            GridType.BROKENTRACK => 1,
            GridType.LAVAFOG => 2,
            _ => 999
        },

        // ========== AirShip（飞艇）==========
        MoveType.AirShip => gridType switch
        {
            GridType.GROUND => 1,
            GridType.FOREST => 1,

            GridType.ROAD => 1,
            GridType.SEA => 1,
            GridType.RIVER => 1,
            GridType.HILL => 1,
            GridType.LAVA => 1,
            GridType.BEACH => 1,
            GridType.TP => 0,
            GridType.REEF => 1,
            GridType.WHIRLPOOL => 1,
            GridType.LAVASIDE => 1,
            GridType.SEAFOG => 2,
            GridType.LANDFOG => 2,
            GridType.WATERFALL => 1,
            GridType.CLIFF => 1,
            GridType.SLOPE => 1,
            GridType.CAVE => 1,
            GridType.HOLE => 1,
            GridType.METEORITE => 999,
            GridType.PIPE => 999,
            GridType.PIPESEAM => 999,  // 飞艇不能通过管道接缝
            GridType.PLASMA => 999,   // 等离子：完全不可通行（含空军，能量墙）
            GridType.CREEPER => 999,  // 蔓生藤蔓：不可通行
            GridType.GATE => 1,       // 门地形可通行（通行由Gate兵器白名单控制）
            // ✅ CW墙系+三气候皮肤组（消耗镜像基础地形）
            GridType.WALL => 999,
            GridType.WEAK_WALL => 999,
            GridType.WASTELAND => 1,
            GridType.RUINS2 => 1,
            GridType.RUINS3 => 1,
            GridType.DESERT_GROUND => 1,
            GridType.SNOW_GROUND => 1,
            GridType.WASTE_GROUND => 1,
            GridType.DESERT_ROAD => 1,
            GridType.SNOW_ROAD => 1,
            GridType.WASTE_ROAD => 1,
            GridType.DESERT_FOREST => 1,
            GridType.SNOW_FOREST => 1,
            GridType.WASTE_FOREST => 1,
            GridType.DESERT_HILL => 1,
            GridType.SNOW_HILL => 1,
            GridType.WASTE_HILL => 1,
            GridType.DESERT_RUINS => 1,
            GridType.SNOW_RUINS => 1,
            GridType.WASTE_RUINS => 1,
            GridType.DESERT_WASTELAND => 1,
            GridType.SNOW_WASTELAND => 1,
            GridType.WASTE_WASTELAND => 1,
            GridType.DESERT_PIPE => 999,
            GridType.SNOW_PIPE => 999,
            GridType.WASTE_PIPE => 999,
            GridType.DESERT_PIPESEAM => 999,
            GridType.SNOW_PIPESEAM => 999,
            GridType.WASTE_PIPESEAM => 999,
            GridType.DESERT_BROKENPIPE => 1,
            GridType.SNOW_BROKENPIPE => 1,
            GridType.WASTE_BROKENPIPE => 1,
            // 新增地形
            GridType.TRACK => 1,
            GridType.STATION => 1,
            GridType.BRIDGE => 1,
            GridType.LAVABRIDGE => 1,
            GridType.PASSABLEPIPE => 1,
            GridType.SHIPGATE => 1,
            GridType.OVERPASS => 1,
            GridType.BROKENPIPE => 1,
            GridType.RUINS => 1,
            GridType.BROKENTRACK => 1,
            GridType.LAVAFOG => 2,
            _ => 999
        },

        // ========== Drone（无人机）==========
        MoveType.Drone => gridType switch
        {
            GridType.GROUND => 1,
            GridType.FOREST => 1,

            GridType.ROAD => 1,
            GridType.RIVER => 1,
            GridType.HILL => 1,
            GridType.BEACH => 1,
            GridType.TP => 0,
            GridType.REEF => 1,
            GridType.WHIRLPOOL => 1,
            GridType.LAVASIDE => 1,
            GridType.SEAFOG => 2,
            GridType.LANDFOG => 2,
            GridType.WATERFALL => 1,
            GridType.CLIFF => 1,
            GridType.SLOPE => 1,
            GridType.CAVE => 1,
            GridType.HOLE => 1,
            GridType.SEA => 999,
            GridType.LAVA => 999,
            GridType.METEORITE => 999,
            GridType.PIPE => 999,
            GridType.PIPESEAM => 999,  // 无人机不能通过管道接缝
            GridType.PLASMA => 999,   // 等离子：完全不可通行（含空军，能量墙）
            GridType.CREEPER => 999,  // 蔓生藤蔓：不可通行
            GridType.GATE => 1,       // 门地形可通行（通行由Gate兵器白名单控制）
            // ✅ CW墙系+三气候皮肤组（消耗镜像基础地形）
            GridType.WALL => 999,
            GridType.WEAK_WALL => 999,
            GridType.WASTELAND => 1,
            GridType.RUINS2 => 2,
            GridType.RUINS3 => 2,
            GridType.DESERT_GROUND => 1,
            GridType.SNOW_GROUND => 1,
            GridType.WASTE_GROUND => 1,
            GridType.DESERT_ROAD => 1,
            GridType.SNOW_ROAD => 1,
            GridType.WASTE_ROAD => 1,
            GridType.DESERT_FOREST => 1,
            GridType.SNOW_FOREST => 1,
            GridType.WASTE_FOREST => 1,
            GridType.DESERT_HILL => 1,
            GridType.SNOW_HILL => 1,
            GridType.WASTE_HILL => 1,
            GridType.DESERT_RUINS => 2,
            GridType.SNOW_RUINS => 2,
            GridType.WASTE_RUINS => 2,
            GridType.DESERT_WASTELAND => 1,
            GridType.SNOW_WASTELAND => 1,
            GridType.WASTE_WASTELAND => 1,
            GridType.DESERT_PIPE => 999,
            GridType.SNOW_PIPE => 999,
            GridType.WASTE_PIPE => 999,
            GridType.DESERT_PIPESEAM => 999,
            GridType.SNOW_PIPESEAM => 999,
            GridType.WASTE_PIPESEAM => 999,
            GridType.DESERT_BROKENPIPE => 3,
            GridType.SNOW_BROKENPIPE => 3,
            GridType.WASTE_BROKENPIPE => 3,
            // 新增地形
            GridType.TRACK => 1,
            GridType.STATION => 1,
            GridType.BRIDGE => 1,
            GridType.LAVABRIDGE => 1,
            GridType.PASSABLEPIPE => 1,
            GridType.SHIPGATE => 1,
            GridType.OVERPASS => 1,
            GridType.BROKENPIPE => 3,
            GridType.RUINS => 2,
            GridType.BROKENTRACK => 2,
            GridType.LAVAFOG => 999,  
            _ => 999
        },

        // ========== AeroSpacer（空天战机）==========
        MoveType.AeroSpacer => gridType switch
        {
            GridType.GROUND => 1,
            GridType.FOREST => 1,

            GridType.ROAD => 1,
            GridType.SEA => 1,
            GridType.RIVER => 1,
            GridType.HILL => 1,
            GridType.LAVA => 1,
            GridType.BEACH => 1,
            GridType.TP => 0,
            GridType.REEF => 1,
            GridType.WHIRLPOOL => 1,
            GridType.LAVASIDE => 1,
            GridType.SEAFOG => 1,
            GridType.LANDFOG => 1,
            GridType.WATERFALL => 1,
            GridType.CLIFF => 1,
            GridType.SLOPE => 1,
            GridType.CAVE => 1,
            GridType.HOLE => 1,
            GridType.METEORITE => 999,
            GridType.PIPE => 999,
            GridType.PIPESEAM => 999,  
            GridType.PLASMA => 999,   // 等离子：完全不可通行（含空军，能量墙）
            GridType.CREEPER => 999,  // 蔓生藤蔓：不可通行
            GridType.GATE => 1,       // 门地形可通行（通行由Gate兵器白名单控制）
            // ✅ CW墙系+三气候皮肤组（消耗镜像基础地形）
            GridType.WALL => 999,
            GridType.WEAK_WALL => 999,
            GridType.WASTELAND => 1,
            GridType.RUINS2 => 1,
            GridType.RUINS3 => 1,
            GridType.DESERT_GROUND => 1,
            GridType.SNOW_GROUND => 1,
            GridType.WASTE_GROUND => 1,
            GridType.DESERT_ROAD => 1,
            GridType.SNOW_ROAD => 1,
            GridType.WASTE_ROAD => 1,
            GridType.DESERT_FOREST => 1,
            GridType.SNOW_FOREST => 1,
            GridType.WASTE_FOREST => 1,
            GridType.DESERT_HILL => 1,
            GridType.SNOW_HILL => 1,
            GridType.WASTE_HILL => 1,
            GridType.DESERT_RUINS => 1,
            GridType.SNOW_RUINS => 1,
            GridType.WASTE_RUINS => 1,
            GridType.DESERT_WASTELAND => 1,
            GridType.SNOW_WASTELAND => 1,
            GridType.WASTE_WASTELAND => 1,
            GridType.DESERT_PIPE => 999,
            GridType.SNOW_PIPE => 999,
            GridType.WASTE_PIPE => 999,
            GridType.DESERT_PIPESEAM => 999,
            GridType.SNOW_PIPESEAM => 999,
            GridType.WASTE_PIPESEAM => 999,
            GridType.DESERT_BROKENPIPE => 1,
            GridType.SNOW_BROKENPIPE => 1,
            GridType.WASTE_BROKENPIPE => 1,
            GridType.TRACK => 1,
            GridType.STATION => 1,
            GridType.BRIDGE => 1,
            GridType.LAVABRIDGE => 1,
            GridType.PASSABLEPIPE => 1,
            GridType.SHIPGATE => 1,
            GridType.OVERPASS => 1,
            GridType.BROKENPIPE => 1,
            GridType.RUINS => 1,
            GridType.BROKENTRACK => 1,
            GridType.LAVAFOG => 1,
            _ => 999
        },

        // ========== HeliCopter（直升机）==========
        MoveType.HeliCopter => gridType switch
        {
            GridType.GROUND => 1,
            GridType.FOREST => 1,

            GridType.ROAD => 1,
            GridType.SEA => 1,
            GridType.RIVER => 1,
            GridType.HILL => 1,
            GridType.BEACH => 1,
            GridType.TP => 0,
            GridType.REEF => 1,
            GridType.WHIRLPOOL => 1,
            GridType.LAVASIDE => 1,
            GridType.SEAFOG => 2,
            GridType.LANDFOG => 2,
            GridType.WATERFALL => 1,
            GridType.CLIFF => 1,
            GridType.SLOPE => 1,
            GridType.CAVE => 1,
            GridType.HOLE => 1,
            GridType.LAVA => 999,
            GridType.METEORITE => 999,
            GridType.PIPE => 999,
            GridType.PIPESEAM => 999,  // 直升机不能通过管道接缝
            GridType.PLASMA => 999,   // 等离子：完全不可通行（含空军，能量墙）
            GridType.CREEPER => 999,  // 蔓生藤蔓：不可通行
            GridType.GATE => 1,       // 门地形可通行（通行由Gate兵器白名单控制）
            // ✅ CW墙系+三气候皮肤组（消耗镜像基础地形）
            GridType.WALL => 999,
            GridType.WEAK_WALL => 999,
            GridType.WASTELAND => 1,
            GridType.RUINS2 => 1,
            GridType.RUINS3 => 1,
            GridType.DESERT_GROUND => 1,
            GridType.SNOW_GROUND => 1,
            GridType.WASTE_GROUND => 1,
            GridType.DESERT_ROAD => 1,
            GridType.SNOW_ROAD => 1,
            GridType.WASTE_ROAD => 1,
            GridType.DESERT_FOREST => 1,
            GridType.SNOW_FOREST => 1,
            GridType.WASTE_FOREST => 1,
            GridType.DESERT_HILL => 1,
            GridType.SNOW_HILL => 1,
            GridType.WASTE_HILL => 1,
            GridType.DESERT_RUINS => 1,
            GridType.SNOW_RUINS => 1,
            GridType.WASTE_RUINS => 1,
            GridType.DESERT_WASTELAND => 1,
            GridType.SNOW_WASTELAND => 1,
            GridType.WASTE_WASTELAND => 1,
            GridType.DESERT_PIPE => 999,
            GridType.SNOW_PIPE => 999,
            GridType.WASTE_PIPE => 999,
            GridType.DESERT_PIPESEAM => 999,
            GridType.SNOW_PIPESEAM => 999,
            GridType.WASTE_PIPESEAM => 999,
            GridType.DESERT_BROKENPIPE => 2,
            GridType.SNOW_BROKENPIPE => 2,
            GridType.WASTE_BROKENPIPE => 2,
            // 新增地形
            GridType.TRACK => 1,
            GridType.STATION => 1,
            GridType.BRIDGE => 1,
            GridType.LAVABRIDGE => 1,
            GridType.PASSABLEPIPE => 1,
            GridType.SHIPGATE => 1,
            GridType.OVERPASS => 1,
            GridType.BROKENPIPE => 2,
            GridType.RUINS => 1,
            GridType.BROKENTRACK => 1,
            GridType.LAVAFOG => 999, 
            _ => 999
        },

        // ========== SpaceShiper（战舰）==========
        MoveType.SpaceShiper => gridType switch
        {
            GridType.GROUND => 1,
            GridType.FOREST => 1,

            GridType.ROAD => 1,
            GridType.SEA => 1,
            GridType.RIVER => 1,
            GridType.HILL => 1,
            GridType.LAVA => 1,
            GridType.BEACH => 1,
            GridType.TP => 0,
            GridType.REEF => 1,
            GridType.WHIRLPOOL => 1,
            GridType.LAVASIDE => 1,
            GridType.SEAFOG => 1,
            GridType.LANDFOG => 1,
            GridType.WATERFALL => 1,
            GridType.CLIFF => 1,
            GridType.SLOPE => 1,
            GridType.CAVE => 1,
            GridType.HOLE => 1,
            GridType.METEORITE => 1,
            GridType.PIPE => 1,
            GridType.PIPESEAM => 1,    // 战舰可通过管道接缝
            GridType.PLASMA => 999,   // 等离子：完全不可通行（含空军，能量墙）
            GridType.CREEPER => 999,  // 蔓生藤蔓：不可通行
            GridType.GATE => 1,       // 门地形可通行（通行由Gate兵器白名单控制）
            // ✅ CW墙系+三气候皮肤组（消耗镜像基础地形）
            GridType.WALL => 999,
            GridType.WEAK_WALL => 999,
            GridType.WASTELAND => 1,
            GridType.RUINS2 => 1,
            GridType.RUINS3 => 1,
            GridType.DESERT_GROUND => 1,
            GridType.SNOW_GROUND => 1,
            GridType.WASTE_GROUND => 1,
            GridType.DESERT_ROAD => 1,
            GridType.SNOW_ROAD => 1,
            GridType.WASTE_ROAD => 1,
            GridType.DESERT_FOREST => 1,
            GridType.SNOW_FOREST => 1,
            GridType.WASTE_FOREST => 1,
            GridType.DESERT_HILL => 1,
            GridType.SNOW_HILL => 1,
            GridType.WASTE_HILL => 1,
            GridType.DESERT_RUINS => 1,
            GridType.SNOW_RUINS => 1,
            GridType.WASTE_RUINS => 1,
            GridType.DESERT_WASTELAND => 1,
            GridType.SNOW_WASTELAND => 1,
            GridType.WASTE_WASTELAND => 1,
            GridType.DESERT_PIPE => 1,
            GridType.SNOW_PIPE => 1,
            GridType.WASTE_PIPE => 1,
            GridType.DESERT_PIPESEAM => 1,
            GridType.SNOW_PIPESEAM => 1,
            GridType.WASTE_PIPESEAM => 1,
            GridType.DESERT_BROKENPIPE => 1,
            GridType.SNOW_BROKENPIPE => 1,
            GridType.WASTE_BROKENPIPE => 1,
            // 新增地形
            GridType.TRACK => 1,
            GridType.STATION => 1,
            GridType.BRIDGE => 1,
            GridType.LAVABRIDGE => 1,
            GridType.PASSABLEPIPE => 1,
            GridType.SHIPGATE => 1,
            GridType.OVERPASS => 0,     // 战舰在立交桥消耗为0！
            GridType.BROKENPIPE => 1,
            GridType.RUINS => 1,
            GridType.BROKENTRACK => 1,
            GridType.LAVAFOG => 1,
            _ => 1
        },

        // ========== Hover（气垫）==========
        MoveType.Hover => gridType switch
        {
            GridType.SEA => 1,
            GridType.RIVER => 2,
            GridType.BEACH => 1,
            GridType.TP => 0,
            GridType.REEF => 2,
            GridType.WHIRLPOOL => 3,
            GridType.LAVASIDE => 999,
            GridType.SEAFOG => 3,
            GridType.LANDFOG => 999,
            GridType.WATERFALL => 999,
            GridType.CLIFF => 999,
            GridType.SLOPE => 999,
            GridType.CAVE => 1,
            GridType.HOLE => 1,
            GridType.PIPESEAM => 999,  // 气垫不能通过管道接缝
            GridType.PLASMA => 999,   // 等离子：完全不可通行（含空军，能量墙）
            GridType.CREEPER => 999,  // 蔓生藤蔓：不可通行
            GridType.GATE => 1,       // 门地形可通行（通行由Gate兵器白名单控制）
            // ✅ CW墙系+三气候皮肤组（消耗镜像基础地形）
            GridType.WALL => 999,
            GridType.WEAK_WALL => 999,
            GridType.WASTELAND => 1,
            GridType.RUINS2 => 999,
            GridType.RUINS3 => 999,
            GridType.DESERT_GROUND => 999,
            GridType.SNOW_GROUND => 999,
            GridType.WASTE_GROUND => 999,
            GridType.DESERT_ROAD => 999,
            GridType.SNOW_ROAD => 999,
            GridType.WASTE_ROAD => 999,
            GridType.DESERT_FOREST => 999,
            GridType.SNOW_FOREST => 999,
            GridType.WASTE_FOREST => 999,
            GridType.DESERT_HILL => 999,
            GridType.SNOW_HILL => 999,
            GridType.WASTE_HILL => 999,
            GridType.DESERT_RUINS => 999,
            GridType.SNOW_RUINS => 999,
            GridType.WASTE_RUINS => 999,
            GridType.DESERT_WASTELAND => 1,
            GridType.SNOW_WASTELAND => 1,
            GridType.WASTE_WASTELAND => 1,
            GridType.DESERT_PIPE => 999,
            GridType.SNOW_PIPE => 999,
            GridType.WASTE_PIPE => 999,
            GridType.DESERT_PIPESEAM => 999,
            GridType.SNOW_PIPESEAM => 999,
            GridType.WASTE_PIPESEAM => 999,
            GridType.DESERT_BROKENPIPE => 999,
            GridType.SNOW_BROKENPIPE => 999,
            GridType.WASTE_BROKENPIPE => 999,
            GridType.GROUND => 999,
            GridType.FOREST => 999,

            GridType.ROAD => 999,
            GridType.HILL => 999,
            GridType.METEORITE => 999,
            GridType.PIPE => 999,
            GridType.LAVA => 999,
            // 新增地形
            GridType.TRACK => 999,
            GridType.STATION => 999,
            GridType.BRIDGE => 1,
            GridType.LAVABRIDGE => 999,
            GridType.PASSABLEPIPE => 999,
            GridType.SHIPGATE => 1,
            GridType.OVERPASS => 1,
            GridType.BROKENPIPE => 999,
            GridType.RUINS => 999,
            GridType.BROKENTRACK => 999,
            GridType.LAVAFOG => 999, 
            _ => 999
        },

        // ========== PipeRunner（管道行者）==========
        MoveType.PipeRunner => gridType switch
        {
            GridType.PIPE => 1,
            GridType.PIPESEAM => 1,    // 管道行者可通过管道接缝
            GridType.PLASMA => 999,   // 等离子：完全不可通行（含空军，能量墙）
            GridType.CREEPER => 999,  // 蔓生藤蔓：不可通行
            GridType.GATE => 1,       // 门地形可通行（通行由Gate兵器白名单控制）
            // ✅ CW墙系+三气候皮肤组（消耗镜像基础地形）
            GridType.WALL => 999,
            GridType.WEAK_WALL => 999,
            GridType.WASTELAND => 999,
            GridType.RUINS2 => 999,
            GridType.RUINS3 => 999,
            GridType.DESERT_GROUND => 999,
            GridType.SNOW_GROUND => 999,
            GridType.WASTE_GROUND => 999,
            GridType.DESERT_ROAD => 999,
            GridType.SNOW_ROAD => 999,
            GridType.WASTE_ROAD => 999,
            GridType.DESERT_FOREST => 999,
            GridType.SNOW_FOREST => 999,
            GridType.WASTE_FOREST => 999,
            GridType.DESERT_HILL => 999,
            GridType.SNOW_HILL => 999,
            GridType.WASTE_HILL => 999,
            GridType.DESERT_RUINS => 999,
            GridType.SNOW_RUINS => 999,
            GridType.WASTE_RUINS => 999,
            GridType.DESERT_WASTELAND => 999,
            GridType.SNOW_WASTELAND => 999,
            GridType.WASTE_WASTELAND => 999,
            GridType.DESERT_PIPE => 1,
            GridType.SNOW_PIPE => 1,
            GridType.WASTE_PIPE => 1,
            GridType.DESERT_PIPESEAM => 1,
            GridType.SNOW_PIPESEAM => 1,
            GridType.WASTE_PIPESEAM => 1,
            GridType.DESERT_BROKENPIPE => 4,
            GridType.SNOW_BROKENPIPE => 4,
            GridType.WASTE_BROKENPIPE => 4,
            GridType.TP => 0,
            GridType.GROUND => 999,
            GridType.FOREST => 999,

            GridType.ROAD => 999,
            GridType.SEA => 999,
            GridType.RIVER => 999,
            GridType.HILL => 999,
            GridType.METEORITE => 999,
            GridType.LAVA => 999,
            GridType.BEACH => 999,
            GridType.REEF => 999,
            GridType.WHIRLPOOL => 999,
            GridType.LAVASIDE => 999,
            GridType.SEAFOG => 999,
            GridType.LANDFOG => 999,
            GridType.WATERFALL => 999,
            GridType.CLIFF => 999,
            GridType.SLOPE => 999,
            GridType.CAVE => 999,
            GridType.HOLE => 999,
            // 新增地形
            GridType.TRACK => 999,
            GridType.STATION => 999,
            GridType.BRIDGE => 999,
            GridType.LAVABRIDGE => 999,
            GridType.PASSABLEPIPE => 1,
            GridType.SHIPGATE => 999,
            GridType.OVERPASS => 2,
            GridType.BROKENPIPE => 4,
            GridType.RUINS => 999,
            GridType.BROKENTRACK => 999,
            GridType.LAVAFOG => 999, 
            _ => 999
        },

        // ========== LAVARUNNER（岩浆行者）==========
        MoveType.LAVARUNNER => gridType switch
        {
            GridType.LAVA => 1,
            GridType.TP => 0,
            GridType.LAVABRIDGE => 2,
            GridType.SHIPGATE => 2,
            GridType.OVERPASS => 2,
            GridType.PIPESEAM => 999,  // 岩浆行者不能通过管道接缝
            GridType.PLASMA => 999,   // 等离子：完全不可通行（含空军，能量墙）
            GridType.CREEPER => 999,  // 蔓生藤蔓：不可通行
            GridType.GATE => 1,       // 门地形可通行（通行由Gate兵器白名单控制）
            // ✅ CW墙系+三气候皮肤组（消耗镜像基础地形）
            GridType.WALL => 999,
            GridType.WEAK_WALL => 999,
            GridType.WASTELAND => 1,
            GridType.RUINS2 => 999,
            GridType.RUINS3 => 999,
            GridType.DESERT_GROUND => 999,
            GridType.SNOW_GROUND => 999,
            GridType.WASTE_GROUND => 999,
            GridType.DESERT_ROAD => 999,
            GridType.SNOW_ROAD => 999,
            GridType.WASTE_ROAD => 999,
            GridType.DESERT_FOREST => 999,
            GridType.SNOW_FOREST => 999,
            GridType.WASTE_FOREST => 999,
            GridType.DESERT_HILL => 999,
            GridType.SNOW_HILL => 999,
            GridType.WASTE_HILL => 999,
            GridType.DESERT_RUINS => 999,
            GridType.SNOW_RUINS => 999,
            GridType.WASTE_RUINS => 999,
            GridType.DESERT_WASTELAND => 1,
            GridType.SNOW_WASTELAND => 1,
            GridType.WASTE_WASTELAND => 1,
            GridType.DESERT_PIPE => 999,
            GridType.SNOW_PIPE => 999,
            GridType.WASTE_PIPE => 999,
            GridType.DESERT_PIPESEAM => 999,
            GridType.SNOW_PIPESEAM => 999,
            GridType.WASTE_PIPESEAM => 999,
            GridType.DESERT_BROKENPIPE => 999,
            GridType.SNOW_BROKENPIPE => 999,
            GridType.WASTE_BROKENPIPE => 999,
            // 新增地形
            GridType.TRACK => 999,
            GridType.STATION => 999,
            GridType.BRIDGE => 999,
            GridType.PASSABLEPIPE => 999,
            GridType.BROKENPIPE => 999,
            GridType.RUINS => 999,
            GridType.BROKENTRACK => 999,
            GridType.LAVAFOG => 3,
            _ => 999
        },

        // ========== LAVAHOVER（岩浆登陆者）==========
        MoveType.LAVAHOVER => gridType switch
        {
            GridType.LAVA => 1,
            GridType.LAVASIDE => 1,
            GridType.TP => 0,
            GridType.LAVABRIDGE => 1,
            GridType.SHIPGATE => 1,
            GridType.OVERPASS => 1,
            GridType.LAVAFOG => 3,
            GridType.PIPESEAM => 999,  // 岩浆登陆者不能通过管道接缝
            GridType.PLASMA => 999,   // 等离子：完全不可通行（含空军，能量墙）
            GridType.CREEPER => 999,  // 蔓生藤蔓：不可通行
            GridType.GATE => 1,       // 门地形可通行（通行由Gate兵器白名单控制）
            // ✅ CW墙系+三气候皮肤组（消耗镜像基础地形）
            GridType.WALL => 999,
            GridType.WEAK_WALL => 999,
            GridType.WASTELAND => 1,
            GridType.RUINS2 => 999,
            GridType.RUINS3 => 999,
            GridType.DESERT_GROUND => 999,
            GridType.SNOW_GROUND => 999,
            GridType.WASTE_GROUND => 999,
            GridType.DESERT_ROAD => 999,
            GridType.SNOW_ROAD => 999,
            GridType.WASTE_ROAD => 999,
            GridType.DESERT_FOREST => 999,
            GridType.SNOW_FOREST => 999,
            GridType.WASTE_FOREST => 999,
            GridType.DESERT_HILL => 999,
            GridType.SNOW_HILL => 999,
            GridType.WASTE_HILL => 999,
            GridType.DESERT_RUINS => 999,
            GridType.SNOW_RUINS => 999,
            GridType.WASTE_RUINS => 999,
            GridType.DESERT_WASTELAND => 1,
            GridType.SNOW_WASTELAND => 1,
            GridType.WASTE_WASTELAND => 1,
            GridType.DESERT_PIPE => 999,
            GridType.SNOW_PIPE => 999,
            GridType.WASTE_PIPE => 999,
            GridType.DESERT_PIPESEAM => 999,
            GridType.SNOW_PIPESEAM => 999,
            GridType.WASTE_PIPESEAM => 999,
            GridType.DESERT_BROKENPIPE => 999,
            GridType.SNOW_BROKENPIPE => 999,
            GridType.WASTE_BROKENPIPE => 999,
            _ => 999
        },

        // ========== Train（火车）==========
        MoveType.Train => gridType switch
        {
            GridType.TRACK => 2,
            GridType.STATION => 1,
            GridType.OVERPASS => 2,
            GridType.BROKENTRACK => 2,
            GridType.TP => 0,
            _ => 999
        },

        // ========== GasTrain（蒸汽车）==========
        MoveType.GasTrain => gridType switch
        {
            GridType.TRACK => 3,
            GridType.STATION => 2,
            GridType.OVERPASS => 3,
            GridType.BROKENTRACK => 4,
            GridType.TP => 0,
            _ => 999
        },

        // ========== FASTER（高铁）==========
        MoveType.FASTER => gridType switch
        {
            GridType.TRACK => 1,
            GridType.STATION => 0,
            GridType.OVERPASS => 0,
            GridType.BROKENTRACK => 2,
            GridType.TP => 0,
            _ => 999
        },

        // ========== Missile（洲际导弹）==========
        MoveType.Missile => gridType switch
        {
            GridType.GROUND => 1,
            GridType.FOREST => 1,

            GridType.ROAD => 1,
            GridType.SEA => 1,
            GridType.RIVER => 1,
            GridType.HILL => 1,
            GridType.LAVA => 1,
            GridType.BEACH => 1,
            GridType.TP => 0,
            GridType.REEF => 1,
            GridType.WHIRLPOOL => 1,
            GridType.LAVASIDE => 1,
            GridType.SEAFOG => 5,
            GridType.LANDFOG => 4,
            GridType.WATERFALL => 1,
            GridType.CLIFF => 1,
            GridType.SLOPE => 1,
            GridType.CAVE => 1,
            GridType.HOLE => 1,
            GridType.METEORITE => 5,
            GridType.PIPE => 5,
            GridType.PIPESEAM => 5,
            GridType.PLASMA => 999,   // 等离子：完全不可通行（含空军，能量墙）
            GridType.CREEPER => 999,  // 蔓生藤蔓：不可通行
            GridType.GATE => 1,       // 门地形可通行（通行由Gate兵器白名单控制）
            // ✅ CW墙系+三气候皮肤组（消耗镜像基础地形）
            GridType.WALL => 999,
            GridType.WEAK_WALL => 999,
            GridType.WASTELAND => 1,
            GridType.RUINS2 => 1,
            GridType.RUINS3 => 1,
            GridType.DESERT_GROUND => 1,
            GridType.SNOW_GROUND => 1,
            GridType.WASTE_GROUND => 1,
            GridType.DESERT_ROAD => 1,
            GridType.SNOW_ROAD => 1,
            GridType.WASTE_ROAD => 1,
            GridType.DESERT_FOREST => 1,
            GridType.SNOW_FOREST => 1,
            GridType.WASTE_FOREST => 1,
            GridType.DESERT_HILL => 1,
            GridType.SNOW_HILL => 1,
            GridType.WASTE_HILL => 1,
            GridType.DESERT_RUINS => 1,
            GridType.SNOW_RUINS => 1,
            GridType.WASTE_RUINS => 1,
            GridType.DESERT_WASTELAND => 1,
            GridType.SNOW_WASTELAND => 1,
            GridType.WASTE_WASTELAND => 1,
            GridType.DESERT_PIPE => 5,
            GridType.SNOW_PIPE => 5,
            GridType.WASTE_PIPE => 5,
            GridType.DESERT_PIPESEAM => 5,
            GridType.SNOW_PIPESEAM => 5,
            GridType.WASTE_PIPESEAM => 5,
            GridType.DESERT_BROKENPIPE => 1,
            GridType.SNOW_BROKENPIPE => 1,
            GridType.WASTE_BROKENPIPE => 1,
            GridType.TRACK => 1,
            GridType.STATION => 1,
            GridType.BRIDGE => 1,
            GridType.LAVABRIDGE => 1,
            GridType.PASSABLEPIPE => 1,
            GridType.SHIPGATE => 1,
            GridType.OVERPASS => 0,
            GridType.BROKENPIPE => 1,
            GridType.RUINS => 1,
            GridType.BROKENTRACK => 1,
            GridType.LAVAFOG => 6,
            _ => 999
        },

        _ => 1
    };
}

    public virtual void OnMoveSelected()
    {
        if (isStealthed && !canMoveWhileStealthed) return; // ✅ 隐身行为锁
        if (consumeFuel && fuel <= 0) return;
        EnsureGridBound();
        ShowAnimState("move");
        StopBreath();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null)
        {
            return;
        }
        
        // ✅ 清理之前的状态
        gm.gridManager.CloseRange();
        gm.gridManager.HideAttackRange();
        gm.gridManager.ClearWeaponRange();
        
        gm.gridManager.ShowMoveRange(this);
    }

// ========== ✅ 究极自由：燃料图标更新（从 APC/LightTank 迁移） ==========
public virtual void UpdateNoFuelIcon()
{
    if (noFuelIcon == null) return;
    
    if (!consumeFuel)
    {
        noFuelIcon.Visible = false;
    }
    else if (fuel <= 0)
    {
        noFuelIcon.Visible = true;
        if (noFuelIcon.SpriteFrames != null && noFuelIcon.SpriteFrames.HasAnimation("nofuel"))
        {
            noFuelIcon.Play("nofuel");
        }
    }
    else if (fuel <= lowFuelThreshold)
    {
        noFuelIcon.Visible = (Time.GetTicksMsec() / 500) % 2 == 0;
        if (noFuelIcon.SpriteFrames != null && noFuelIcon.SpriteFrames.HasAnimation("lowfuel"))
        {
            noFuelIcon.Play("lowfuel");
        }
    }
    else
    {
        noFuelIcon.Visible = false;
    }
}

// ========== ✅ 究极自由：unitsCannotMove 静态管理（从 APC 迁移） ==========
public static void UpdateUnitsCannotMove()
{
    var unitsToRemove = new List<Infantry>();
    
    foreach (var kvp in unitsCannotMove.ToList())
    {
        var unit = kvp.Key;
        int turnsLeft = kvp.Value;
        
        if (!IsInstanceValid(unit))
        {
            unitsToRemove.Add(unit);
            continue;
        }
        
        turnsLeft--;
        if (turnsLeft <= 0)
        {
            unitsToRemove.Add(unit);
        }
        else
        {
            unitsCannotMove[unit] = turnsLeft;
        }
    }
    
    foreach (var unit in unitsToRemove)
    {
        unitsCannotMove.Remove(unit);
    }
}

public static bool CanUnitMove(Infantry unit)
{
    if (unit != null && unit.IsStunned) return false;   // ✅ 麻痹中不能移动
    return !unitsCannotMove.ContainsKey(unit);
}
}
