// MapGenerator.cs - 程序地图生成核心 (开创人2026-11-14)
// 产 SavedMapData → 注入存档槽 → 走 LoadMapToGame 现成管线。种子固定=每日挑战可复现。
using Godot;
using System;
using System.Collections.Generic;

public static class MapGenerator
{
    private static int T(string name, int fallback)
        => Enum.TryParse<GridType>(name, out var g) ? (int)g : fallback;

    // 运行时反射定位(字段名各版本可能不同,能设哪个设哪个,全没有就跳过)
    public static void SetPos(object data, int x, int y, int w)
    {
        var t = data.GetType();
        t.GetField("gridX")?.SetValue(data, x);
        t.GetField("gridY")?.SetValue(data, y);
        t.GetField("gridIndex")?.SetValue(data, y * w + x);
        t.GetField("posX")?.SetValue(data, x);
        t.GetField("posY")?.SetValue(data, y);
    }

    public static SavedMapData Generate(MapGenProfile p, string slotName)
    {
        int seed = p.seed == 0 ? (int)(Time.GetTicksUsec() % int.MaxValue) : p.seed;
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        int w = Mathf.Max(6, p.width), h = Mathf.Max(6, p.height);
        int n = w * h;
        int gGround = T("GROUND", 0), gForest = T("FOREST", 1), gMountain = T("MOUNTAIN", 2), gSea = T("SEA", 3);
        int gRoad = T("ROAD", 4), gRiver = T("RIVER", 5);
        double total = p.TotalWeight;

        var terrain = new int[n];
        for (int i = 0; i < n; i++)
        {
            double r = rng.Randf() * total;
            terrain[i] = r < p.plainWeight ? gGround
                       : r < p.plainWeight + p.forestWeight ? gForest
                       : r < p.plainWeight + p.forestWeight + p.mountainWeight ? gMountain
                       : gSea;
        }

        // 咽喉线:随机折线山脉/海峡
        for (int c = 0; c < p.chokeLineCount; c++)
        {
            int x = rng.RandiRange(1, w - 2), y = rng.RandiRange(1, h - 2);
            int dx = rng.RandiRange(0, 1) == 0 ? 1 : 0, dy = 1 - dx;
            int len = (int)(Mathf.Min(w, h) * (0.4 + 0.6 * p.chokeDensity));
            bool seaLine = rng.RandiRange(0, 1) == 0;
            for (int s = 0; s < len && x >= 0 && y >= 0 && x < w && y < h; s++)
            {
                terrain[y * w + x] = seaLine ? gSea : gMountain;
                if (rng.RandiRange(0, 2) == 0) { int t2 = dx; dx = dy; dy = t2; } // 随机拐弯
                x += dx; y += dy;
            }
        }

        // 对称:1竖镜 2横镜 3点对称(后半覆盖前半)
        if (p.symmetryMode == 1)
            for (int y = 0; y < h; y++) for (int x = w / 2; x < w; x++) terrain[y * w + x] = terrain[y * w + (w - 1 - x)];
        else if (p.symmetryMode == 2)
            for (int y = h / 2; y < h; y++) for (int x = 0; x < w; x++) terrain[y * w + x] = terrain[(h - 1 - y) * w + x];
        else if (p.symmetryMode == 3)
            for (int y = h / 2; y < h; y++) for (int x = w / 2; x < w; x++) terrain[y * w + x] = terrain[(h - 1 - y) * w + (w - 1 - x)];

        // HQ落位:陆地上拒采,最小间距
        var hqPos = new List<Vector2I>();
        int factions = Mathf.Clamp(p.factionCount, 2, 4);
        for (int f = 0; f < factions; f++)
        {
            Vector2I best = new Vector2I(-1, -1);
            for (int att = 0; att < 200; att++)
            {
                int x = rng.RandiRange(1, w - 2), y = rng.RandiRange(1, h - 2);
                if (terrain[y * w + x] != gGround) continue;
                bool ok = true;
                foreach (var q in hqPos) if (Mathf.Abs(q.X - x) + Mathf.Abs(q.Y - y) < p.minStartDistance) { ok = false; break; }
                if (ok) { best = new Vector2I(x, y); break; }
            }
            if (best.X < 0)
            {
                // ✅ 修复(审查MapGen):拒采到头改取"最远距离点"而非纯随机——极端配置防HQ重叠
                int bx = 0, by = 0, bd = -1;
                for (int t = 0; t < 60; t++)
                {
                    int x = rng.RandiRange(0, w - 1), y = rng.RandiRange(0, h - 1);
                    int dmin = 9999;
                    foreach (var q in hqPos) dmin = Mathf.Min(dmin, Mathf.Abs(q.X - x) + Mathf.Abs(q.Y - y));
                    if (dmin > bd) { bd = dmin; bx = x; by = y; }
                }
                best = new Vector2I(bx, by);
                GD.PushWarning($"[MapGen] HQ{f}拒采耗尽,取全场最远距离位(间距{bd})");
            }
            terrain[best.Y * w + best.X] = gGround;
            hqPos.Add(best);
        }

        // 资源镜像:每HQ周围环带撒城/厂(城中立,厂中立可占)
        var facilities = new List<SavedFacilityData>();
        string[] facTypes = { "City", "Base", "AirPort", "Port" };
        int[] facCounts = { p.cityCountPerFaction, p.factoryCountPerFaction, p.airportCountPerFaction, p.portCountPerFaction };
        for (int f = 0; f < hqPos.Count; f++)
        {
            var hq = new SavedFacilityData { typeName = "HQ", team = f == 0 ? "Player1" : $"Player{f + 1}", displayName = $"HQ{f + 1}" };
            SetPos(hq, hqPos[f].X, hqPos[f].Y, w);
            facilities.Add(hq);
            for (int k = 0; k < facTypes.Length; k++)
            {
                int placed = 0, ring = 1;
                while (placed < facCounts[k] && ring < Mathf.Max(w, h))
                {
                    for (int dx = -ring; dx <= ring && placed < facCounts[k]; dx++)
                        for (int dy = -ring; dy <= ring && placed < facCounts[k]; dy++)
                        {
                            if (Mathf.Abs(dx) != ring && Mathf.Abs(dy) != ring) continue;
                            int x = hqPos[f].X + dx, y = hqPos[f].Y + dy;
                            if (x < 0 || y < 0 || x >= w || y >= h || terrain[y * w + x] != gGround) continue;
                            // 简易冲突避让:1格内不堆
                            bool clash = false;
                            foreach (var fd in facilities) { int fx = -1, fy = -1; var xf = fd.GetType().GetField("gridX"); if (xf != null) fx = (int)xf.GetValue(fd); var yf = fd.GetType().GetField("gridY"); if (yf != null) fy = (int)yf.GetValue(fd); if (fx == x && fy == y) { clash = true; break; } }
                            if (clash) continue;
                            var fd2 = new SavedFacilityData { typeName = facTypes[k], team = "Player0", displayName = facTypes[k] };
                            SetPos(fd2, x, y, w);
                            facilities.Add(fd2);
                            placed++;
                        }
                    ring++;
                }
            }
        }

        // 可达性:BFS从HQ0,不可达HQ→直线开路(ROAD)
        if (p.validateReachability && hqPos.Count > 1)
        {
            var reach = new bool[n];
            var q = new Queue<int>();
            int s0 = hqPos[0].Y * w + hqPos[0].X;
            reach[s0] = true; q.Enqueue(s0);
            int[] dd = { 1, -1, w, -w };
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                foreach (var d in dd)
                {
                    int nxt = cur + d;
                    if (nxt < 0 || nxt >= n || reach[nxt]) continue;
                    if ((d == 1 && nxt % w == 0) || (d == -1 && cur % w == 0)) continue; // 换行墙
                    bool pass = false;
                    foreach (var tn in p.passableTerrainNames)
                        if (Enum.TryParse<GridType>(tn, out var gt2) && (int)gt2 == terrain[nxt]) { pass = true; break; }
                    if (!pass) continue;
                    reach[nxt] = true; q.Enqueue(nxt);
                }
            }
            for (int f = 1; f < hqPos.Count; f++)
            {
                int idx = hqPos[f].Y * w + hqPos[f].X;
                if (reach[idx]) continue;
                int x = hqPos[0].X, y = hqPos[0].Y;
                while (x != hqPos[f].X) { terrain[y * w + x] = gRoad; x += Math.Sign(hqPos[f].X - x); }
                while (y != hqPos[f].Y) { terrain[y * w + x] = gRoad; y += Math.Sign(hqPos[f].Y - y); }
                GD.Print($"[MapGen] HQ{f + 1}孤岛→已强制开路");
            }
        }

        // 分屏:竖切
        var data = new SavedMapData
        {
            slotName = slotName,
            displayName = $"生成图 seed={seed}",
            width = w, height = h,
            screenCount = Mathf.Clamp(p.screenCount, 1, w),   // ✅ 修复(审查MapGen):分屏数不超过图宽,防0宽分屏
            screenWidths = new List<int>(), screenHeights = new List<int>(), screenYOffsets = new List<int>(),
        };
        int per = w / data.screenCount;
        for (int s = 0; s < data.screenCount; s++)
            { data.screenWidths.Add(s == data.screenCount - 1 ? w - per * (data.screenCount - 1) : per); data.screenHeights.Add(h); data.screenYOffsets.Add(0); }
        data.terrainIds = new List<int>(terrain);
        data.facilities = facilities;
        for (int f = 0; f < hqPos.Count; f++)
            data.teamFunds[$"Player{f + 1}"] = p.startFundsPerFaction;
        GD.Print($"[MapGen] 生成完成 {w}x{h} 势力{factions} 设施{facilities.Count} seed={seed}");
        return data;
    }
}
