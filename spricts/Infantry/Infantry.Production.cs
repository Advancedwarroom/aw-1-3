// Infantry.cs - 生产模块：单位可在舱内/外部生产单位
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// Infantry · Production（生产模块）
// 由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注
// ✅ 规则速览：
//    位置模式 productionLocationMode：0=关闭 1=内部 2=外部 3=可选内外（菜单显示两个按钮）
//    内部生产依赖搭载模块：点击「内部生产」时自动开启（仅需搭载模块，白名单可留空——
//      产出单位可直接进舱「待着」，但出舱后若不在搭载白名单里就装不回去）。
//    外部生产：范围 [productionMinRange, productionMaxRange]，自动随机放置或手选目标格。
//    进度制：总进度 N / 每回合进度 Z（如 20/10 = 两回合生产完毕）。
//    「内部生产时可否移动」= 移动会不会打断进度：关=移动即中断；外部生产移动即中断。
//    自动化：开启后点一次生产，之后每回合自动推进（只显示中断按钮），
//      生产不消耗单位行动——照常攻击/操作/待机。
//    冷却：productionCooldownTurns 回合内不能再开新一次生产。
//    生产弹药：currentProductionAmmo>0 才显示生产按钮；开工消耗 1 发（统一弹药系统）。
public partial class Infantry
{
    [ExportGroup("生产模块")]
    /// <summary>✅ 是否更新：填"是"则本[单位]的生产受全局"可生成单位白名单"限制（需Lab解锁）。
    /// 填"否"则与Lab无关。仅限制生产——编辑模式放置、单位使用不受影响。</summary>
    [Export] public bool isUpdatable = true;
    [Export] public bool canProduceUnits = false;                                   // ① 是否可生产
    [Export(PropertyHint.Enum, "关闭,内部,外部,可选内外")] public int productionLocationMode = 0;  // ② 生产位置
    [Export] public int productionMinRange = 1;                                     // ③ 外部生产最小范围
    [Export] public int productionMaxRange = 2;                                     // ③ 外部生产最大范围
    [Export] public bool productionCanCrossScreens = false;                        // 外部生产目标范围是否允许跨屏
    [Export] public bool productionAutoPlace = false;                                // ④ 外部自动随机放置（false=手选目标格）
    [Export] public Godot.Collections.Array<string> productionWhitelist = new();   // ⑤ 生产白名单（unitTypeId）
    [Export] public int maxProductionAmmo = 0;                                      // ⑥ 生产弹药上限
    [Export] public int currentProductionAmmo = 0;                                  // ⑥ 当前生产弹药
    [Export] public int productionTotalProgress = 20;                                // ⑦ 生产总进度 N
    [Export] public int productionProgressPerTurn = 10;                              // ⑧ 每回合进度 Z
    [Export] public bool canMoveWhileProducing = false;                             // ⑨ 内部生产时可否移动
    [Export] public bool productionAutomation = false;                               // ⑩ 自动化
    [Export] public int productionCooldownTurns = 0;                                 // ⑪ 冷却回合数
    [Export] public bool productionFree = false;                                    // ⑫ 是否无消耗（原作兵工厂模式：不耗生产弹药、不花资金
    [Export] public bool productionFullList = false;                                    // ⑬ 全生产列表（跳过白名单与全局生产限制）
    [Export] public bool productionInstantMove = false;                                 // ⑭ 产出单位立即可动（常规=本回合不可动）：不耗生产弹药）
    [Export] public bool producedCargoBoardedThisTurn = false;                          // ⑮ 产物视为本回合登舰（true=产物本回合不能起飞/卸载，旧防白嫖行为；false=DoR原作，产物当回合可出击）

    // ===== 运行时状态（不导出） =====
    public bool isProducing = false;                    // 生产中
    public int productionProgress = 0;                  // 当前累计进度
    public string productionTargetUnitId = "";         // 本次生产的单位
    public bool productionInternal = true;             // 本次生产：内部/外部
    public int productionCooldownRemaining = 0;        // 剩余冷却
    public Grids productionTargetGrid = null;          // 外部生产目标格
    public int productionPaidCost = 0;                  // 本次生产已扣资金（完工彻底失败时全额退款）

    /// <summary>位置模式是否允许指定方式</summary>
    public bool ProductionAllowsInternal => productionLocationMode == 1 || productionLocationMode == 3;
    public bool ProductionAllowsExternal => productionLocationMode == 2 || productionLocationMode == 3;

    /// <summary>能否开始一次生产（按钮显示条件）</summary>
    virtual public bool CanStartProduction(bool internalMode)
    {
        if (!canProduceUnits) return false;
        if (isProducing) return false;
        if (productionCooldownRemaining > 0) return false;
        if (!productionFullList && (productionWhitelist == null || productionWhitelist.Count == 0)) return false;
        if (currentProductionAmmo <= 0) return false;                    // ⑥ 生产弹药决定能否生产
        if (internalMode && !ProductionAllowsInternal) return false;
        if (!internalMode && !ProductionAllowsExternal) return false;
        if (internalMode)
        {
            // 内部生产依赖搭载模块（点击时自动开启，这里只检查舱位）
            EnsureTransportForInternalProduction();
            if (transportedUnits.Count >= maxTransportCapacity) return false;
        }
        return true;
    }

    /// <summary>本[生产者]生产指定单位的实际造价：检查器造价 → 实例降价/逐兵种字典 → CO模块12修正。
    /// productionFree（无消耗）时返回 0。与建筑生产同一套公式。</summary>
    public int GetUnitProductionCost(string unitId)
    {
        if (productionFree) return 0;
        var info = UnitProductionDatabase.GetInfo(unitId);
        int cost = GetInstanceProductionCost(info.Cost, unitId);
        return Mathf.Max(1, Mathf.RoundToInt(cost * (100f + CoModuleSystem.GetCostModifierPercent(team)) / 100f));
    }

    /// <summary>内部生产只需开启搭载模块：点「内部生产」时自动打开依赖项</summary>
    public void EnsureTransportForInternalProduction()
    {
        if (!canTransportUnits) canTransportUnits = true;
        if (maxTransportCapacity < 1) maxTransportCapacity = 1;
    }

    /// <summary>
    /// 开始生产。返回 false=条件不满足。
    /// 非自动化模式下，开工即消耗本回合行动；自动化模式下不消耗。
    /// </summary>
    virtual public bool StartProduction(string unitId, bool internalMode, Grids externalTargetGrid = null)
    {
        if (!CanStartProduction(internalMode)) return false;
        if (!internalMode && externalTargetGrid != null)
        {
            if (grid == null) return false;
            int productionDistance = Mathf.Abs(externalTargetGrid.GridIndex.X - grid.GridIndex.X) + Mathf.Abs(externalTargetGrid.GridIndex.Y - grid.GridIndex.Y);
            var rangeGm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            if (productionDistance < productionMinRange || productionDistance > productionMaxRange || rangeGm?.gridManager == null || !rangeGm.gridManager.IsRangeAllowed(grid, externalTargetGrid, productionCanCrossScreens)) return false;
        }
        var info = UnitProductionDatabase.GetInfo(unitId);
        if (string.IsNullOrEmpty(info.ScenePath)) return false;

        // ✅ Lab白名单：仅当本[生产者]isUpdatable=true时，生产受全局白名单限制
        if (isUpdatable && !productionFullList && !UnitProductionWhitelist.CanProduce(unitId)) return false;

        // ✅ 外部手选：先校验目标格（非法/被占 → 直接拒绝，不吞弹药/资金）
        if (!internalMode && externalTargetGrid != null)
        {
            if (!IsInstanceValid(externalTargetGrid)) return false;
            if (externalTargetGrid.infantries.Any(u => u != null && IsInstanceValid(u))) return false;
        }

        // ✅ 资金预检（不扣）：无消耗模式跳过；与建筑生产同一公式
        int prodCost = GetUnitProductionCost(unitId);
        var gm0 = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm0 != null && prodCost > 0 && gm0.GetTeamFunds(team) < prodCost) return false;

        if (!productionFree && !TryConsumeAmmo(AmmoKind.Production)) return false;  // 开工消耗 1 发生产弹药（无消耗模式跳过）

        // ✅ 扣款：实例降价+逐兵种字典+CO修正已在 GetUnitProductionCost 内统一计算
        if (gm0 != null && prodCost > 0)
        {
            gm0.SetTeamFunds(team, gm0.GetTeamFunds(team) - prodCost);
            CoModuleSystem.OnFundsSpent(team, prodCost);   // ✅ 模块27（能量条）：消耗资金打点
        }
        productionPaidCost = prodCost;   // 记录已付：完工彻底失败时 RefundFailedProduction 全额退款

        productionTargetUnitId = unitId;
        productionInternal = internalMode;
        productionTargetGrid = externalTargetGrid;
        productionProgress = 0;
        isProducing = true;

        // ⑩ 自动化=生产不消耗行动；非自动化=本回合被生产占用（待机）
        if (!productionAutomation)
        {
            isMoved = true;
            isAttacked = true;
            hasActed = true;
            state = UnitState.Acted;
            SetWaitVisual(true);
        }
        // ✅ 总进度 N ≤ 0 = 即时生产（DoR 航母造水上飞机：当回合完成）
        if (productionTotalProgress <= 0) CompleteProduction();
        return true;
    }

    /// <summary>每回合推进（由 GameManager.Turn 调用）</summary>
    public virtual void ProcessTurnProduction()
    {
        if (productionCooldownRemaining > 0) productionCooldownRemaining--;
        if (!isProducing) return;
        productionProgress += Math.Max(0, productionProgressPerTurn);
        if (productionProgress >= productionTotalProgress) CompleteProduction();
    }

    /// <summary>中断生产（进度清零，不返还弹药，冷却照常开始）</summary>
    public void CancelProduction()
    {
        if (!isProducing) return;
        isProducing = false;
        productionProgress = 0;
        productionTargetUnitId = "";
        productionPaidCost = 0;   // 手动中断：不退款（与生产弹药中断不退政策一致）
        productionTargetGrid = null;
        productionCooldownRemaining = Math.Max(0, productionCooldownTurns);
    }

    private void CompleteProduction()
    {
        isProducing = false;
        productionCooldownRemaining = Math.Max(0, productionCooldownTurns);
        string unitId = productionTargetUnitId;
        bool internalMode = productionInternal;
        Grids target = productionTargetGrid;
        productionProgress = 0;
        productionTargetUnitId = "";
        productionTargetGrid = null;

        var info = UnitProductionDatabase.GetInfo(unitId);
        if (string.IsNullOrEmpty(info.ScenePath)) { RefundFailedProduction(); return; }

        var scene = GD.Load<PackedScene>(info.ScenePath);
        if (scene == null) { RefundFailedProduction(); return; }
        var unit = scene.Instantiate() as Infantry;
        if (unit == null) { RefundFailedProduction(); return; }

        unit.team = team;

        if (internalMode)
        {
            // 内部：产出单位直接进舱（不检查搭载白名单——白名单可为空；
            // 出舱后能否再装回由搭载白名单决定）
            // ✅ 修复：产出单位必须挂到单位容器（与正常运输中的单位一致），
            //    不能挂在生产舰名下——卸载/BindUnitToGrid 都按「局部坐标=世界坐标」处理，
            //    挂在舰船名下会叠加舰船坐标，出厂单位飞到错误位置甚至图外（表现为没有贴图/凭空消失）
            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm == null || gm.unitManager == null || gm.unitManager.units == null)
            {
                RefundFailedProduction();   // 彻底失败：全额退款（资金+弹药），与建筑远程落点失败退款一致
                unit.QueueFree();
                return;
            }
            EnsureTransportForInternalProduction();
            transportedUnits.Add(unit);
            unit.isTransported = true;
            if (producedCargoBoardedThisTurn)
                MarkCargoBoardedThisTurn(unit);   // ⑮旧行为：true=刚造好的舰载机本回合不能起飞（DoR原作=false当场可出击）
            unit.Hide();
            unit.ProcessMode = ProcessModeEnum.Disabled;
            gm.unitManager.units.AddChild(unit);

            // 内部生产的单位同样必须注册到全局单位表；回合重置、燃料和状态结算均遍历 AllUnits。
            if (!gm.unitManager.AllUnits.Contains(unit))
                gm.unitManager.AllUnits.Add(unit);
            gm.RegisterUnit(unit, gm.InferCategory(unit));
            unit.OnClickPiece = gm.OnSelectPiece;

            unit.Position = Vector2.Zero;
            productionPaidCost = 0;   // 交货成功：清已付记录
        }
        else
        {
            // 外部：放到目标格（自动放置时由调用方选好格）
            if (target == null || !IsInstanceValid(target))
                target = FindValidExternalProductionGrid(unit);
            // 完工复检：目标格被占/不可通行时回退自动找格。
            if (target != null && (target.infantries.Any(u => u != null && IsInstanceValid(u))
                || unit.GetMoveCostOnGrid(target) >= 999))
                target = FindValidExternalProductionGrid(unit);
            if (target == null)
            {
                RefundFailedProduction();   // 彻底无合法落点：全额退款（资金+弹药），与建筑远程落点失败退款一致
                unit.QueueFree();
                return;
            }

            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm == null || gm.unitManager == null) { RefundFailedProduction(); unit.QueueFree(); return; }

            gm.unitManager.AddChild(unit);
            unit.GlobalPosition = target.GlobalPosition;
            unit.grid = target;
            if (!target.infantries.Contains(unit)) target.infantries.Add(unit);
            if (target.infantry == null) target.infantry = unit;
            gm.unitManager.BindUnitToGrid(unit, true);
            if (productionInstantMove) unit.hasActed = false;   // ⑭ 立即可动：显式重置行动标记
            productionPaidCost = 0;   // 交货成功：清已付记录
        }
    }

    /// <summary>生产彻底失败（无任何合法落点/场景异常）：全额退款——资金 + 生产弹药。
    /// 与建筑生产「远程落点失败退款」同一政策；手动中断不调用本函数（中断不退，与弹药政策一致）。</summary>
    private void RefundFailedProduction()
    {
        if (productionPaidCost > 0)
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm != null)
                gm.SetTeamFunds(team, gm.GetTeamFunds(team) + productionPaidCost);
            productionPaidCost = 0;
        }
        if (!productionFree && currentProductionAmmo < maxProductionAmmo)
            currentProductionAmmo = Math.Min(maxProductionAmmo, currentProductionAmmo + 1);
    }

    /// <summary>外部生产自动放置：在范围内随机挑一个可放格子</summary>
    public Grids FindValidExternalProductionGrid(Infantry producedUnit)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return null;
        var candidates = GetValidExternalProductionGrids(producedUnit);
        if (candidates.Count == 0) return null;
        var rng = new Random();
        return candidates[rng.Next(candidates.Count)];
    }

    /// <summary>外部生产有效目标格：范围内 + 空位 + 产出单位可通行</summary>
    virtual public List<Grids> GetValidExternalProductionGrids(Infantry producedUnit)
    {
        var result = new List<Grids>();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null || gm.gridManager.map == null) return result;

        int x = grid != null ? grid.GridIndex.X : -1;
        int y = grid != null ? grid.GridIndex.Y : -1;
        if (x < 0) return result;

        int minX = Math.Max(0, x - productionMaxRange);
        int maxX = Math.Min(gm.gridManager.map.GetLength(0) - 1, x + productionMaxRange);
        int minY = Math.Max(0, y - productionMaxRange);
        int maxY = Math.Min(gm.gridManager.map.GetLength(1) - 1, y + productionMaxRange);

        for (int gx = minX; gx <= maxX; gx++)
        {
            for (int gy = minY; gy <= maxY; gy++)
            {
                int dist = Math.Abs(gx - x) + Math.Abs(gy - y);   // 曼哈顿距离=范围
                if (dist < productionMinRange || dist > productionMaxRange) continue;
                var g = gm.gridManager.map[gx, gy];
                if (g == null || !gm.gridManager.IsRangeAllowed(grid, g, productionCanCrossScreens)) continue;
                if (g.infantries.Any(u => u != null && IsInstanceValid(u))) continue;
                if (producedUnit != null && producedUnit.GetMoveCostOnGrid(g) >= 999) continue;
                result.Add(g);
            }
        }
        return result;
    }

}
