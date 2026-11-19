using Godot;
using System;
using System.Linq;

// Infantry.Summon.cs —— 单位存在时限 + CO时限单位召唤（开创人 2026-11-19 定案）
// 时限：lifespanTurns >0 每整回合递减，归零立即消失（蒸发语义，不触发亡语）；<0=永存/关闭
public partial class Infantry
{
    [ExportGroup("单位存在时限")]
    [Export] public int lifespanTurns = -1;

    [ExportGroup("召唤模块（CO时限单位召唤）")]
    [Export] public bool summonEnabled = false;
    [Export] public string summonUnitName = "";
    [Export] public int summonCount = 1;
    [Export] public int summonRange = 1;
    [Export] public int summonLifespan = 5;
    [Export] public int summonCooldownTurns = 0;
    [Export] public bool summonConsumesAction = true;
    [Export] public bool summonUnitsCanActImmediately = true;
    [Export] public bool summonRequiresCoUnit = false;
    [Export] public string summonActionName = "召唤";

    public int summonCooldownRemaining = 0;

    public void PerformSummon()
    {
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.unitManager == null || gm.gridManager == null) return;
        if (!summonEnabled) return;
        if (summonRequiresCoUnit && !isCoUnit) return;
        if (summonCooldownRemaining > 0) return;
        if (string.IsNullOrWhiteSpace(summonUnitName) || !UnitProductionDatabase.HasUnit(summonUnitName)) return;
        var scene = GD.Load<PackedScene>(UnitProductionDatabase.GetInfo(summonUnitName).ScenePath);
        if (scene == null || grid == null) return;

        int remaining = Mathf.Max(1, summonCount);
        var cells = gm.gridManager.grids
            .Where(g => g != null && g.infantries.Count == 0 && g.weapon == null)
            .Select(g => new { g, d = Mathf.Abs(g.GridIndex.X - grid.GridIndex.X) + Mathf.Abs(g.GridIndex.Y - grid.GridIndex.Y) })
            .Where(t => t.d >= 1 && t.d <= Mathf.Max(1, summonRange))
            .OrderBy(t => t.d).ThenBy(t => t.g.GridIndex.Y).ThenBy(t => t.g.GridIndex.X)
            .ToList();

        foreach (var t in cells)
        {
            if (remaining <= 0) break;
            var spawned = gm.unitManager.SpawnUnit(scene, t.g.GridIndex, team);
            if (spawned == null) continue;
            spawned.lifespanTurns = summonLifespan;
            if (summonUnitsCanActImmediately) spawned.isPowerActivated = true;
            remaining--;
        }

        summonCooldownRemaining = Mathf.Max(0, summonCooldownTurns);
        if (summonConsumesAction)
        {
            isMoved = true; isAttacked = true; hasActed = true;
            state = UnitState.Acted; SetWaitVisual(true);
        }
        gm.UpdateUnitLists();
    }
}
