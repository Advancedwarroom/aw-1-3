// ForceSatellite.Support.cs —— 力场支援模块（Black Onyx / 力场卫星）
// 规矩：支援环 x1-y1 选目标 → 对每个目标以 x2-y2 菱形扩散锁效果（同导弹井 范围/伤害面 老规矩）
// 多格兵器：效果面必须覆盖其弱点格才生效；只盖到非弱点格一律不管
using Godot;
using System.Collections.Generic;

public partial class ForceSatellite
{
    [ExportGroup("力场支援模块")]
    [Export] public bool forceSupportEnabled = true;
    [Export] public bool supportTargetFacilities = false;   // 设施（设施无血量通道，锁不生效，开关为模块完整性保留）
    [Export] public bool supportTargetWeapons = true;       // 兵器（力场卫星=只选兵器）
    [Export] public bool supportTargetInfantry = false;     // 单位
    [Export] public int supportTeamPolicy = 0;              // 0=仅友军 1=仅敌军 2=全部（力场卫星=仅友军）
    [Export] public int supportMinRange = 0;                // 支援范围 x1
    [Export] public int supportMaxRange = 5;                //           y1
    [Export] public int effectMinRadius = 0;                // 作用效果范围 x2（以被支援目标为心）
    [Export] public int effectMaxRadius = 0;                //               y2
    [Export] public bool lockHealth = true;                 // 锁血（锁死当前值）
    [Export] public bool lockFuel = false;                  // 锁燃油
    [Export] public bool lockPrimaryAmmo = false;           // 锁单位主弹药
    [Export] public bool[] lockAmmoTypes = new bool[8];     // 兵器每种弹药独立锁（8弹药系统）
    [Export] public string supportBlacklist = "";           // 黑名单：类名逗号分隔

    private readonly List<Node> lockedTargets = new();

    public override void ProcessTurnSupport()
    {
        if (!forceSupportEnabled) return;
        ClearAllLocks();
        ScanAndLock();
    }

    private bool PolicyOk(string targetTeam)
    {
        return supportTeamPolicy switch
        {
            0 => targetTeam == team,
            1 => targetTeam != team,
            _ => true,
        };
    }

    private bool SupportBlacklisted(Node n)
    {
        if (string.IsNullOrEmpty(supportBlacklist) || n == null) return false;
        var name = n.GetType().Name;
        foreach (var item in supportBlacklist.Split(','))
            if (item.Trim() == name) return true;
        return false;
    }

    private void ClearAllLocks()
    {
        foreach (var n in lockedTargets)
        {
            if (n is Infantry u) { u.healthLocked = false; u.fuelLocked = false; u.primaryAmmoLocked = false; }
            else if (n is Weapon w) { w.healthLocked = false; if (w.ammoLocked != null) System.Array.Clear(w.ammoLocked, 0, w.ammoLocked.Length); }
        }
        lockedTargets.Clear();
    }

    private void ScanAndLock()
    {
        if (grid == null) return;
        var myIndex = grid.GridIndex;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.unitManager == null) return;

        var gridMap = new Dictionary<Vector2I, Grids>();
        foreach (var node in GetTree().GetNodesInGroup("grids"))
            if (node is Grids g) gridMap[g.GridIndex] = g;

        var unitAt = new Dictionary<Vector2I, Infantry>();
        foreach (var u in gm.unitManager.AllUnits)
            if (u != null && GodotObject.IsInstanceValid(u) && u.grid != null) unitAt[u.grid.GridIndex] = u;

        int Dist(Vector2I a) => Mathf.Abs(a.X - myIndex.X) + Mathf.Abs(a.Y - myIndex.Y);

        // 阶段A：支援环 x1-y1 内选目标
        var selected = new List<Vector2I>();
        if (supportTargetWeapons)
            foreach (var kv in gridMap)
            {
                var w = kv.Value.weapon;
                int d = Dist(kv.Key);
                if (w != null && d >= supportMinRange && d <= supportMaxRange && PolicyOk(w.team) && !SupportBlacklisted(w))
                    selected.Add(kv.Key);
            }
        if (supportTargetInfantry)
            foreach (var kv in unitAt)
            {
                int d = Dist(kv.Key);
                if (d >= supportMinRange && d <= supportMaxRange && PolicyOk(kv.Value.team) && !SupportBlacklisted(kv.Value))
                    selected.Add(kv.Key);
            }

        // 阶段B：对每个目标按 x2-y2 菱形扩散锁效果；多格兵器要求效果面覆盖弱点格
        foreach (var pos in selected)
        {
            var cells = new List<Vector2I>();
            for (int dx = -effectMaxRadius; dx <= effectMaxRadius; dx++)
                for (int dy = -effectMaxRadius; dy <= effectMaxRadius; dy++)
                {
                    int dd = Mathf.Abs(dx) + Mathf.Abs(dy);
                    if (dd >= effectMinRadius && dd <= effectMaxRadius) cells.Add(new Vector2I(pos.X + dx, pos.Y + dy));
                }
            foreach (var cell in cells)
            {
                if (!gridMap.TryGetValue(cell, out var g)) continue;
                if (unitAt.TryGetValue(cell, out var u)) TryLockInfantry(u);
                TryLockWeapon(g.weapon, cells, gridMap);
            }
        }
    }

    private void TryLockInfantry(Infantry u)
    {
        if (u == null || !supportTargetInfantry || !PolicyOk(u.team) || SupportBlacklisted(u) || lockedTargets.Contains(u)) return;
        if (lockHealth) u.healthLocked = true;
        if (lockFuel) u.fuelLocked = true;
        if (lockPrimaryAmmo) u.primaryAmmoLocked = true;
        lockedTargets.Add(u);
    }

    private void TryLockWeapon(Weapon w, List<Vector2I> effectCells, Dictionary<Vector2I, Grids> gridMap)
    {
        if (w == null || !supportTargetWeapons || !PolicyOk(w.team) || SupportBlacklisted(w) || lockedTargets.Contains(w)) return;
        // 多格兵器弱点格规矩：效果面覆盖格中，必须存在"该兵器占据且为其弱点"的格子；无弱点概念(单格)恒真
        if (w.HasMethod("IsWeakPointGrid"))
        {
            bool covered = false;
            foreach (var c in effectCells)
                if (gridMap.TryGetValue(c, out var g2) && g2.weapon == w && (bool)w.Call("IsWeakPointGrid", g2)) { covered = true; break; }
            if (!covered) return;
        }
        if (lockHealth) w.healthLocked = true;
        if (w.ammoLocked != null)
            for (int t = 0; t < 8 && t < lockAmmoTypes.Length; t++)
                if (lockAmmoTypes[t]) w.ammoLocked[t] = true;
        lockedTargets.Add(w);
    }
}
