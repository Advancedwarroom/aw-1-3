// AI_Manager.Teleport.cs —— ✅ N屏地图：AI 单位使用"传送(TP)"按钮
//
// 与玩家侧 ActionMenu.Teleport 同一规则：
//   · 单位站在有出链的格子上、本回合未行动/未被眩晕/能移动，即可传送到任一出链目标格
//   · 落点不能有敌军、地形必须可通行；传送视为已移动（不再走路），但仍可继续攻击/占领/待机
// AI 做法：
//   · 把"走到传送起点(可原地) → 传送 → 在落点评估攻击/占领/补给/待机"整体作为一个方案参与比较
//   · 距离场加入传送边，AI 为了前往对面屏的目标/敌人，会主动走向传送点
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class AI_Manager
{
    /// <summary>距离场里一次传送折算的代价（实际传送不耗移动力）</summary>
    private const int TeleportFieldCost = 1;

    /// <summary>落点 → 能传到该落点的全部源格坐标</summary>
    private Dictionary<Vector2I, List<Vector2I>> ReverseTeleportMap()
    {
        var rev = new Dictionary<Vector2I, List<Vector2I>>();
        var links = gameManager?.gridManager?.teleportLinks;
        if (links == null) return rev;
        foreach (var l in links)
        {
            if (!rev.TryGetValue(l.to, out var list)) rev[l.to] = list = new List<Vector2I>();
            list.Add(l.from);
        }
        return rev;
    }

    /// <summary>落点校验：地形可通行 + AI 视角下可空降（雾中敌军在真正传送时再截断）</summary>
    private bool CanLandByTeleport(Infantry u, Grids land)
    {
        if (land == null) return false;
        if (StepCost(u, land) < 0) return false;
        return CanEnd(u, land, out _) == EndKind.Free;
    }

    /// <summary>为所有可达的传送起点生成"传送后"方案</summary>
    private void EvalTeleports(Infantry u, List<(Grids g, EndKind k, Infantry partner)> dests,
                               bool stealthed, bool preRevealForMove, bool freeToggle, List<Plan> plans)
    {
        var gmg = gameManager?.gridManager;
        if (gmg == null || gmg.teleportLinks == null || gmg.teleportLinks.Count == 0) return;
        if (u.IsStunned || !u.CanMove() || u.isAttacked) return;

        // 传送视为已移动 → 与"移动后"的行动能力一致（moved=true）
        bool reveal = preRevealForMove;
        bool stealthAtDest = stealthed && !preRevealForMove;

        foreach (var (src, kind, _) in dests)
        {
            if (kind != EndKind.Free) continue;
            var outs = gmg.GetOutgoingTeleportLinks(src);
            if (outs.Count == 0) continue;
            foreach (var link in outs)
            {
                var land = gmg.GetGridAt(link.to);
                if (land == null || land == src || !CanLandByTeleport(u, land)) continue;

                var tpPlans = new List<Plan>();
                EvalAttacks(u, land, true, stealthAtDest, reveal, freeToggle, tpPlans);
                EvalCapture(u, land, true, stealthAtDest, reveal, freeToggle, tpPlans);
                EvalSupport(u, land, true, stealthAtDest, reveal, freeToggle, tpPlans);
                EvalStealthAndWait(u, land, true, stealthAtDest, reveal, freeToggle, tpPlans);
                foreach (var tp in tpPlans)
                {
                    if (tp == null) continue;
                    tp.tpVia = src;
                    tp.note = $"传送{src.GridIndex}→{land.GridIndex} " + tp.note;
                    plans.Add(tp);
                }
            }
        }
    }

    /// <summary>执行传送（与 ActionMenu.PerformTeleport 同一套状态变更）。失败返回 false，单位留在原地。</summary>
    private bool TryTeleport(Infantry u, Grids to)
    {
        if (u == null || !IsInstanceValid(u) || to == null || u.grid == null) return false;
        var gmg = gameManager?.gridManager;
        if (gmg == null) return false;
        var from = u.grid;
        if (!gmg.GetOutgoingTeleportLinks(from).Any(l => l.to == to.GridIndex)) return false;
        if (u.IsStunned || !u.CanMove() || u.isAttacked) return false;
        if (u.GetMoveCostOnGrid(to) >= 999) return false;
        if (!RealStayable(u, to)) return false;   // 含雾中/隐身敌军：与玩家一样落不下去

        // 离开正在占领的据点 → 占领中断
        if (u.capturingGrid != null && u.capturingGrid != to)
        {
            u.capturingGrid.city?.OnCaptureInterrupted(u);
            u.currentCaptureProgress = 0;
            u.capturingGrid = null;
        }

        from.infantries?.Remove(u);
        if (from.infantry == u) from.infantry = from.infantries?.FirstOrDefault();

        if (to.infantries == null) to.infantries = new List<Infantry>();
        if (!to.infantries.Contains(u)) to.infantries.Add(u);
        if (to.infantry == null) to.infantry = u;
        u.grid = to;
        u.Position = to.Position;

        u.isMoved = true;
        u.state = UnitState.Moved;
        if (u.originalGrid == null) u.originalGrid = from;
        u.movedThisTurnForAutoRepair = true;

        FollowCamera(u, to);
        return true;
    }
}
