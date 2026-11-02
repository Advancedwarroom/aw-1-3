// ForceSatellite.Support.cs —— 力场支援模块（Black Onyx / 力场卫星）
// 手动激活（新的按钮）：点"力场支援" → 在支援环 x1-y1 内手动选目标格 → 效果以目标格为心按 x2-y2 菱形扩散
// 默认配置即力场卫星规格：范围0-1（能选临格）、效果0-0（仅限该格）、只锁兵器、仅友军、锁血
// 多格兵器规矩：效果面必须覆盖其弱点格（Weapon.IsWeakPointGrid 基类虚函数），只盖非弱点格一律不管
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
    [Export] public int supportMinRange = 0;                // 支援范围 x1（选目标的环）
    [Export] public int supportMaxRange = 1;                //           y1（力场卫星默认0-1=能选临格）
    [Export] public int effectMinRadius = 0;                // 作用效果范围 x2（以目标格为心）
    [Export] public int effectMaxRadius = 0;                //               y2（力场卫星默认0-0=仅限该格）
    [Export] public bool lockHealth = true;                 // 锁血（锁死当前值）
    [Export] public bool lockFuel = false;                  // 锁燃油
    [Export] public bool lockPrimaryAmmo = false;           // 锁单位主弹药
    [Export] public bool[] lockAmmoTypes = new bool[8];     // 兵器每种弹药独立锁（8弹药系统）
    [Export] public string supportBlacklist = "";           // 黑名单：类名逗号分隔

    private readonly List<Node> lockedTargets = new();

    public override void _ExitTree() { ClearAllLocks(); }  // 卫星被毁→全部锁解除

    // —— 手动选目标流程（照导弹井 BeginTargetSelection 模式）——
    public void BeginSupportSelection(Infantry activator)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null || grid == null) return;
        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = true;
        bool any = false;
        var myIndex = grid.GridIndex;
        foreach (var node in GetTree().GetNodesInGroup("grids"))
        {
            if (node is not Grids g) continue;
            int d = Mathf.Abs(g.GridIndex.X - myIndex.X) + Mathf.Abs(g.GridIndex.Y - myIndex.Y);
            if (d >= supportMinRange && d <= supportMaxRange)
            {
                any = true;
                g.Modulate = new Color(0.3f, 0.9f, 1f, 0.85f);   // 支援色：青
                g.IsInWeaponRange = true;
                g.OverrideUnitInput(g, true);
                g.OnClickGrid = (to) => { ExecuteSupport(to, activator); };
            }
            else
            {
                g.OverrideUnitInput(g, true);
                g.OnClickEmpty = () => CancelSupportSelection(gm);
            }
        }
        if (!any) CancelSupportSelection(gm);
    }

    public void CancelSupportSelection(GameManager gm)
    {
        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = false;
        foreach (var node in GetTree().GetNodesInGroup("grids"))
            if (node is Grids g) { g.OverrideUnitInput(g, false); g.OnClickGrid = null; g.OnClickEmpty = null; }
    }

    public void ExecuteSupport(Grids targetGrid, Infantry activator)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || targetGrid == null || grid == null) return;
        CancelSupportSelection(gm);
        int d0 = Mathf.Abs(targetGrid.GridIndex.X - grid.GridIndex.X) + Mathf.Abs(targetGrid.GridIndex.Y - grid.GridIndex.Y);
        if (d0 < supportMinRange || d0 > supportMaxRange) return;   // 环外点击无效

        ClearAllLocks();   // 重新指定目标：旧锁全解，新目标生效（不自动索敌）

        var cells = new List<Vector2I>();
        for (int dx = -effectMaxRadius; dx <= effectMaxRadius; dx++)
            for (int dy = -effectMaxRadius; dy <= effectMaxRadius; dy++)
            {
                int dd = Mathf.Abs(dx) + Mathf.Abs(dy);
                if (dd >= effectMinRadius && dd <= effectMaxRadius) cells.Add(new Vector2I(targetGrid.GridIndex.X + dx, targetGrid.GridIndex.Y + dy));
            }

        var gridMap = new Dictionary<Vector2I, Grids>();
        foreach (var node in GetTree().GetNodesInGroup("grids"))
            if (node is Grids g) gridMap[g.GridIndex] = g;
        var unitAt = new Dictionary<Vector2I, Infantry>();
        if (gm.unitManager != null)
            foreach (var u in gm.unitManager.AllUnits)
                if (u != null && GodotObject.IsInstanceValid(u) && u.grid != null) unitAt[u.grid.GridIndex] = u;

        foreach (var cell in cells)
        {
            if (!gridMap.TryGetValue(cell, out var g)) continue;
            if (unitAt.TryGetValue(cell, out var u)) TryLockInfantry(u);
            TryLockWeapon(g.weapon, cells, gridMap);
        }
        GD.Print($"[力场支援] {name} 支援目标格 {targetGrid.GridIndex}，锁定 {lockedTargets.Count} 个目标");
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
        // 多格兵器弱点格规矩：效果面覆盖格中，必须存在"该兵器占据且为其弱点"的格子（单格兵器基类恒真）
        bool covered = false;
        foreach (var c in effectCells)
            if (gridMap.TryGetValue(c, out var g2) && g2.weapon == w && w.IsWeakPointGrid(g2)) { covered = true; break; }
        if (!covered) return;
        if (lockHealth) w.healthLocked = true;
        if (w.ammoLocked != null)
            for (int t = 0; t < 8 && t < lockAmmoTypes.Length; t++)
                if (lockAmmoTypes[t]) w.ammoLocked[t] = true;
        lockedTargets.Add(w);
    }
}
