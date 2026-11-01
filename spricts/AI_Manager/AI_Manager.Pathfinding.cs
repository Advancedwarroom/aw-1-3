// AI_Manager.cs —— 战棋 AI（效用评估 + 威胁图 + 迷雾记忆 + 全技能）
//
// 决策流程（每个 AI 小回合）：
//   1. 兵器（Weapon.ExecuteAI）逐个行动
//   2. 战术阶段：每一步为所有未行动单位重新评估"最佳方案"，先执行高价值战术（击杀/攻击/自爆/照明/完成占领/合流/修理）
//   3. 机动阶段：剩余单位按 照明→远程→近战→占领→运输→补给/修理 的顺序推进或撤退
//   4. 生产：按敌军构成反制选兵 + 占领需求 + 兵种多样性
//   5. 立即可动的新兵再走一轮机动
//
// 公平性：只用本势力视野（CalculateGlobalVisionPool + 照明弹 + 隐身规则 IsVisibleTo），不读取雾中/隐身单位。
//        被雾中或隐身敌军挡路时与玩家一样触发截断（遭遇）。
// 对外接口保持不变：gameManager / p1AIEnabled / p2AIEnabled / autoEndTurn / IsAITeam / IsCurrentPhaseAI / OnPhaseStart
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// AI_Manager · Pathfinding（由 AI_Manager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class AI_Manager
{

    // ==================== 寻路 ====================
    private static readonly Vector2I[] Dirs = { new(0, 1), new(-1, 0), new(0, -1), new(1, 0) };

    private Grids At(int x, int y)
    {
        var gmg = gameManager.gridManager;
        if (x < 0 || y < 0 || x >= gmg.searchRange.X || y >= gmg.searchRange.Y) return null;
        return gmg.map[x, y];
    }

    private IEnumerable<Grids> Ring(Grids c, int minR, int maxR)
    {
        if (c == null) yield break;
        for (int dx = -maxR; dx <= maxR; dx++)
            for (int dy = -maxR; dy <= maxR; dy++)
            {
                int d = Math.Abs(dx) + Math.Abs(dy);
                if (d < minR || d > maxR) continue;
                var g = At(c.GridIndex.X + dx, c.GridIndex.Y + dy);
                if (g == null) continue;
                if (g.screenId != c.screenId) continue; // ✅ N屏隔离：环形扫描不跨屏
                yield return g;
            }
    }

    private static int Dist(Grids a, Grids b) =>
        (a == null || b == null) ? 999 : Math.Abs(a.GridIndex.X - b.GridIndex.X) + Math.Abs(a.GridIndex.Y - b.GridIndex.Y);

    private static int StepCost(Infantry u, Grids g)
    {
        int c = u.GetMoveCostOnGrid(g);
        return (c >= 999 || c == int.MaxValue || c < 0) ? -1 : Math.Max(1, c);
    }

    /// <summary>可达格（Dijkstra）。enemyView=true 时以敌方视角计算（AI 单位阻挡它）。</summary>
    private Reach ComputeReach(Infantry u, Grids start, int movePts, bool enemyView = false)
    {
        var r = new Reach();
        if (start == null) return r;
        r.cost[start] = 0;
        if (movePts <= 0) return r;
        int fuelLimit = (u.consumeFuel && !enemyView) ? Math.Max(0, u.fuel) : int.MaxValue;
        var fuelUsed = new Dictionary<Grids, int> { [start] = 0 };
        var open = new PriorityQueue<Grids, int>();
        open.Enqueue(start, 0);
        while (open.TryDequeue(out var cur, out int cc))
        {
            if (cc > r.cost[cur]) continue;
            foreach (var d in Dirs)
            {
                var n = At(cur.GridIndex.X + d.X, cur.GridIndex.Y + d.Y);
                if (n == null) continue;
                if (n.screenId != start.screenId) continue; // ✅ N屏隔离：AI 不跨屏行走（跨屏走传送）
                int sc = StepCost(u, n);
                if (sc < 0) continue;
                if (n.weapon != null && IsInstanceValid(n.weapon) && u.overlapType != UnitOverlapType.Oozium && !n.weapon.AllowsPassage(u)) continue; // ✦适配：门禁白名单（Gate）放行则可通过
                int nc = cc + sc;
                if (nc > movePts) continue;
                int nf = fuelUsed[cur] + (n.gridType == GridType.TP ? 0 : sc);
                if (nf > fuelLimit) continue;
                if (Blocks(u, n, enemyView)) continue;
                if (r.cost.TryGetValue(n, out int old) && old <= nc) continue;
                r.cost[n] = nc; r.parent[n] = cur; fuelUsed[n] = nf;
                open.Enqueue(n, nc);
            }
        }
        return r;
    }

    private bool Blocks(Infantry mover, Grids g, bool enemyView)
    {
        foreach (var o in g.infantries)
        {
            if (o == null || o == mover || !IsInstanceValid(o) || o.isTransported) continue;
            if (o.team == mover.team || o.team == TeamHelper.Player) continue;
            if (enemyView) { if (o.team == aiTeam) return true; continue; }
            if (ctx.enemies.Contains(o)) return true;   // 只有"看得见"的敌军才会被 AI 绕开
        }
        return false;
    }

    private enum EndKind { None, Free, Load, Merge }

    private EndKind CanEnd(Infantry u, Grids g, out Infantry partner)
    {
        partner = null;
        if (g == u.grid) return EndKind.Free;
        if (g.weapon != null && IsInstanceValid(g.weapon) && u.overlapType != UnitOverlapType.Oozium && !g.weapon.AllowsPassage(u)) return EndKind.None; // ✦适配：白名单放行可停留
        var others = g.infantries.Where(o => o != null && o != u && IsInstanceValid(o) && !o.isTransported).ToList();
        // AI 不知道雾中/隐身敌军：当作空格（真实移动时会被截断）
        var known = others.Where(o => !IsEnemy(o.team) || ctx.enemies.Contains(o)).ToList();
        if (known.Count == 0) return EndKind.Free;
        if (known.Any(o => IsEnemy(o.team))) return EndKind.None;
        if (u.isStealthed && u.stealthOverlapMode == 2) return EndKind.None;

        partner = known.FirstOrDefault(o => o.canTransportUnits && (o.team == u.team || o.team == TeamHelper.Player) && o.CanTransportUnit(u));
        if (partner != null && !(u.isStealthed && !u.canLoadUnloadWhileStealthed)) return EndKind.Load;

        partner = known.FirstOrDefault(o => o.team == u.team && o.UnitTypeId == u.UnitTypeId);
        if (partner != null && !(u.isStealthed && u.stealthOverlapMode == 1) && gameManager.CanMerge(u, partner)) return EndKind.Merge;

        partner = null;
        if (u.overlapType == UnitOverlapType.Overlapping || u.overlapType == UnitOverlapType.Oozium) return EndKind.Free;
        return EndKind.None;
    }

    // 真实占用（执行移动时用，含雾中单位）
    private bool RealStayable(Infantry u, Grids g)
    {
        if (g == u.grid) return true;
        var others = g.infantries.Where(o => o != null && o != u && IsInstanceValid(o) && !o.isTransported).ToList();
        if (others.Count == 0) return g.weapon == null || !IsInstanceValid(g.weapon) || u.overlapType == UnitOverlapType.Oozium || g.weapon.AllowsPassage(u); // ✦适配
        if (others.Any(o => o.team != u.team && o.team != TeamHelper.Player)) return false;
        return u.overlapType == UnitOverlapType.Overlapping || u.overlapType == UnitOverlapType.Oozium;
    }

    // 距离场：从目标集合出发，按单位移动消耗反向 Dijkstra（缓存到本步）
    private Dictionary<Grids, int> DistanceField(Infantry u, string key, IEnumerable<Grids> goals) => DistanceField(u, key, () => goals);

    private Dictionary<Grids, int> DistanceField(Infantry u, string key, Func<IEnumerable<Grids>> goalsFn)
    {
        string k = u.UnitTypeId + "|" + key;
        if (ctx.distCache.TryGetValue(k, out var f)) return f;
        var goals = goalsFn();
        f = new Dictionary<Grids, int>();
        var open = new PriorityQueue<Grids, int>();
        var rev = ReverseTeleportMap();   // ✅ N屏传送：落点 → 所有可传来的源格
        foreach (var g in goals.Where(x => x != null).Distinct()) { f[g] = 0; open.Enqueue(g, 0); }
        while (open.TryDequeue(out var cur, out int cc))
        {
            if (cc > f[cur]) continue;
            int enter = StepCost(u, cur);
            if (enter < 0 && cc > 0) continue;
            foreach (var d in Dirs)
            {
                var n = At(cur.GridIndex.X + d.X, cur.GridIndex.Y + d.Y);
                if (n == null || StepCost(u, n) < 0) continue;
                if (n.screenId != cur.screenId) continue;   // ✅ N屏隔离：步行不跨屏，跨屏只能走传送
                int nc = cc + Math.Max(1, enter < 0 ? 1 : enter);
                if (f.TryGetValue(n, out int old) && old <= nc) continue;
                f[n] = nc; open.Enqueue(n, nc);
            }
            // ✅ N屏传送：从落点反向走回传送源格（传送本身不耗移动力，场内记 TeleportFieldCost 以便优先近路）
            if (rev.TryGetValue(cur.GridIndex, out var srcs))
                foreach (var si in srcs)
                {
                    var n = gameManager.gridManager.GetGridAt(si);
                    if (n == null || StepCost(u, n) < 0) continue;
                    int nc = cc + TeleportFieldCost;
                    if (f.TryGetValue(n, out int old) && old <= nc) continue;
                    f[n] = nc; open.Enqueue(n, nc);
                }
        }
        ctx.distCache[k] = f;
        return f;
    }

    private static int FieldAt(Dictionary<Grids, int> f, Grids g) => f != null && g != null && f.TryGetValue(g, out int v) ? v : 999;
}
