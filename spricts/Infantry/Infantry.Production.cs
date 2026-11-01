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
    /// <summary>✅ 是否更新：填"是"则该单位的生产受全局"可生成单位白名单"限制（需Lab解锁）。
    /// 填"否"则与Lab无关（不受白名单限制）。编辑模式放置不受限制。</summary>
    [Export] public bool isUpdatable = false;
    [Export] public bool canProduceUnits = false;                                   // ① 是否可生产
    [Export(PropertyHint.Enum, "关闭,内部,外部,可选内外")] public int productionLocationMode = 0;  // ② 生产位置
    [Export] public int productionMinRange = 1;                                     // ③ 外部生产最小范围
    [Export] public int productionMaxRange = 2;                                     // ③ 外部生产最大范围
    [Export] public bool productionAutoPlace = false;                                // ④ 外部自动随机放置（false=手选目标格）
    [Export] public Godot.Collections.Array<string> productionWhitelist = new();   // ⑤ 生产白名单（unitTypeId）
    [Export] public int maxProductionAmmo = 0;                                      // ⑥ 生产弹药上限
    [Export] public int currentProductionAmmo = 0;                                  // ⑥ 当前生产弹药
    [Export] public int productionTotalProgress = 20;                                // ⑦ 生产总进度 N
    [Export] public int productionProgressPerTurn = 10;                              // ⑧ 每回合进度 Z
    [Export] public bool canMoveWhileProducing = false;                             // ⑨ 内部生产时可否移动
    [Export] public bool productionAutomation = false;                               // ⑩ 自动化
    [Export] public int productionCooldownTurns = 0;                                 // ⑪ 冷却回合数
    [Export] public bool productionFree = false;                                    // ⑫ 是否无消耗（原作兵工厂模式：不耗生产弹药）

    // ===== 运行时状态（不导出） =====
    public bool isProducing = false;                    // 生产中
    public int productionProgress = 0;                  // 当前累计进度
    public string productionTargetUnitId = "";         // 本次生产的单位
    public bool productionInternal = true;             // 本次生产：内部/外部
    public int productionCooldownRemaining = 0;        // 剩余冷却
    public Grids productionTargetGrid = null;          // 外部生产目标格

    /// <summary>位置模式是否允许指定方式</summary>
    public bool ProductionAllowsInternal => productionLocationMode == 1 || productionLocationMode == 3;
    public bool ProductionAllowsExternal => productionLocationMode == 2 || productionLocationMode == 3;

    /// <summary>能否开始一次生产（按钮显示条件）</summary>
    public bool CanStartProduction(bool internalMode)
    {
        if (!canProduceUnits) return false;
        if (isProducing) return false;
        if (productionCooldownRemaining > 0) return false;
        if (productionWhitelist == null || productionWhitelist.Count == 0) return false;
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
    public bool StartProduction(string unitId, bool internalMode, Grids externalTargetGrid = null)
    {
        if (!CanStartProduction(internalMode)) return false;
        var info = UnitProductionDatabase.GetInfo(unitId);
        if (string.IsNullOrEmpty(info.ScenePath)) return false;

        // ✅ Lab白名单：isUpdatable=true 的单位必须在全局可生成白名单中（编辑模式放置不受限）
        if (!UnitProductionWhitelist.CanProduceUnit(unitId)) return false;

        if (!productionFree && !TryConsumeAmmo(AmmoKind.Production)) return false;  // 开工消耗 1 发生产弹药（无消耗模式跳过）

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
        if (string.IsNullOrEmpty(info.ScenePath)) return;

        var scene = GD.Load<PackedScene>(info.ScenePath);
        if (scene == null) return;
        var unit = scene.Instantiate() as Infantry;
        if (unit == null) return;

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
                unit.QueueFree();
                return;
            }
            EnsureTransportForInternalProduction();
            transportedUnits.Add(unit);
            unit.isTransported = true;
            MarkCargoBoardedThisTurn(unit);   // 刚造好的舰载机本回合不能起飞
            unit.Hide();
            unit.ProcessMode = ProcessModeEnum.Disabled;
            gm.unitManager.units.AddChild(unit);
            unit.Position = Vector2.Zero;
        }
        else
        {
            // 外部：放到目标格（自动放置时由调用方选好格）
            if (target == null || !IsInstanceValid(target))
                target = FindValidExternalProductionGrid(unit);
            if (target == null) { unit.QueueFree(); return; }

            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm == null || gm.unitManager == null) { unit.QueueFree(); return; }

            gm.unitManager.AddChild(unit);
            unit.GlobalPosition = target.GlobalPosition;
            unit.grid = target;
            if (!target.infantries.Contains(unit)) target.infantries.Add(unit);
            if (target.infantry == null) target.infantry = unit;
            gm.unitManager.BindUnitToGrid(unit, true);
        }
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
    public List<Grids> GetValidExternalProductionGrids(Infantry producedUnit)
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
                if (g == null) continue;
                if (g.infantries.Any(u => u != null && IsInstanceValid(u))) continue;
                if (producedUnit != null && producedUnit.GetMoveCostOnGrid(g) >= 999) continue;
                result.Add(g);
            }
        }
        return result;
    }

}
