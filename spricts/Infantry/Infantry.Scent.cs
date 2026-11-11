using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · Scent（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{

    [ExportGroup("嗅探")]
    [Export] public bool scentEnabled = false;
    [Export] public int scentRangeMin = 1;
    [Export] public int scentRangeMax = 1;
    [Export] public bool scentIgnoresConcealment = false;
    [Export] public bool scentIgnoresStealth = false;
    [Export] public bool scentKeepObserving = false;
    [Export] public int scentTrackTurns = 1;
    [Export] public bool scentCanCrossScreens = false; // 嗅探扫描是否允许跨屏





    [ExportGroup("感知强化")]
    [Export] public bool strongSight = false;
    [Export] public bool strongDetect = false;

    // ==================== 嗅探引擎 ====================
    private readonly System.Collections.Generic.Dictionary<Infantry, int> scentTracked = new();

    public void ToggleScent()
    {
        if (!scentEnabled) return;
        SetWaitVisual(true);
        state = UnitState.Acted;
        PerformScentScan();
    }

    public void PerformScentScan()
    {
        if (!scentEnabled || grid == null || gridManager == null) return;
        scentTracked.Clear();
        foreach (var g in gridManager.grids)
        {
            if (g == null || g == grid) continue;
            int dist = Math.Abs(g.GridIndex.X - grid.GridIndex.X) + Math.Abs(g.GridIndex.Y - grid.GridIndex.Y);
            if (dist < scentRangeMin || dist > scentRangeMax) continue;
            if (!gridManager.IsRangeAllowed(grid, g, scentCanCrossScreens)) continue;
            foreach (var u in g.infantries)
            {
                if (u == null || u.team == team || !GodotObject.IsInstanceValid(u)) continue;
                if (u.isStealthed && !scentIgnoresStealth) continue;
                if (!u.isStealthed && u.terrainConcealment && !scentIgnoresConcealment
                    && (g.gridType == GridType.FOREST || g.gridType == GridType.REEF)) continue;
                if (scentKeepObserving)
                {
                    scentTracked[u] = scentTrackTurns;
                    u.stealthFlareRevealed = true;
                    u.UpdateStealthVisual();
                }
            }
        }
    }

    public void MaintainScentTracking()
    {
        if (scentTracked.Count == 0) return;
        var expired = new System.Collections.Generic.List<Infantry>();
        foreach (var kv in scentTracked)
        {
            int left = kv.Value - 1;
            if (left <= 0 || !GodotObject.IsInstanceValid(kv.Key) || grid == null || kv.Key.grid == null || !gridManager.IsRangeAllowed(grid, kv.Key.grid, scentCanCrossScreens)) expired.Add(kv.Key);
            else { scentTracked[kv.Key] = left; kv.Key.stealthFlareRevealed = true; }
        }
        foreach (var u in expired) scentTracked.Remove(u);
    }
}
