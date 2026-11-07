using Godot;
using System.Collections.Generic;
using System.Linq;

// 格子集屏（freeform 拓扑引擎）：屏=格子连通块（开创人2026-11-07批2）
// 规矩：桥断→外块成新屏；贴连→并屏；扣光允许（屏消失）；编号=包围盒中心阅读顺序
public partial class GridManager
{
    [Export] public bool freeformScreens = false;   // 自由屏模式（画笔开启即置真；矩形参数降级为包围盒）

    /// <summary>洪泛填充重算全部屏：连通块按阅读顺序编号，宽高/偏移数组重建为该块包围盒（兼容老系统）</summary>
    public int RecalculateScreensFromCells()
    {
        if (map == null) return 0;
        var visited = new HashSet<Vector2I>();
        var comps = new List<List<Grids>>();
        foreach (var g in grids)
        {
            if (g == null || !IsInstanceValid(g)) continue;
            var idx = g.GridIndex;
            if (visited.Contains(idx)) continue;
            var comp = new List<Grids>();
            var q = new Queue<Grids>();
            q.Enqueue(g); visited.Add(idx);
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                comp.Add(cur);
                foreach (var d in new[] { new Vector2I(1, 0), new Vector2I(-1, 0), new Vector2I(0, 1), new Vector2I(0, -1) })
                {
                    var ni = cur.GridIndex + d;
                    if (visited.Contains(ni)) continue;
                    var ng = GetGridAt(ni);
                    if (ng == null) continue;
                    visited.Add(ni); q.Enqueue(ng);
                }
            }
            comps.Add(comp);
        }
        // 阅读顺序：包围盒中心（先上后下、先左后右）
        comps = comps.OrderBy(c => (c.Min(x => x.GridIndex.Y) + c.Max(x => x.GridIndex.Y)) / 2f)
                     .ThenBy(c => (c.Min(x => x.GridIndex.X) + c.Max(x => x.GridIndex.X)) / 2f).ToList();
        for (int s = 0; s < comps.Count; s++)
            foreach (var g in comps[s]) g.screenId = s;
        // 矩形参数降级为包围盒（老系统的 IsInsideAnyScreen/分屏显示按此自洽）
        int n = comps.Count;
        screenCount = n <= 0 ? 1 : n;
        screenWidths = new int[n]; screenHeights = new int[n];
        screenYOffsets = new int[n];
        for (int s = 0; s < n; s++)
        {
            int minX = comps[s].Min(x => x.GridIndex.X), maxX = comps[s].Max(x => x.GridIndex.X);
            int minY = comps[s].Min(x => x.GridIndex.Y), maxY = comps[s].Max(x => x.GridIndex.Y);
            screenWidths[s] = maxX - minX + 1; screenHeights[s] = maxY - minY + 1;
            screenYOffsets[s] = minY;   // X偏移无独立字段（GetScreenStartX按宽度和算），自由屏模式不走矩形路径
        }
        return n;
    }
}
