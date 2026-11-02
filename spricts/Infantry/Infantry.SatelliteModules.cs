// Infantry.SatelliteModules.cs - 卫星模块：力场支援模块 + 天基打击模块（通用，任何单位可开启）
// 力场卫星(Black Onyx/力场卫星)：锁血/锁弹药/锁燃油（锁死当前值），范围x1-y1，效果半径x2-y2
// 攻击卫星：天基打击=自爆模块扩展（全局/圆心范围选择 + 目标圆心效果范围），固定50%
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class Infantry
{
    // ========== 力场支援模块 ==========
    [ExportGroup("力场支援模块")]
    [Export] public bool forceSupportEnabled = false;            // 1.是否开启力场支援
    // 2.力场支援目标（三个自由选，可全选）
    [Export] public bool forceSupportToUnits = true;             // 目标：单位
    [Export] public bool forceSupportToWeapons = false;          // 目标：兵器（力场卫星默认只选兵器）
    [Export] public bool forceSupportToFacilities = false;       // 目标：设施
    // 3.支援势力
    [Export] public int forceSupportTeamPolicy = 0;               // 0=仅友军 1=仅敌军 2=全部（力场卫星选仅友军）
    // 4.作用效果（均为锁死当前值）
    [Export] public bool forceSupportLockHP = true;              // 锁血（力场卫星按锁血填）
    [Export] public int forceSupportLockAmmoType = 0;            // 锁弹药弹种：0=不锁 1=普通 2=自爆 3=照明 4=隐身 5=显形 6=迷彩
    [Export] public bool forceSupportLockFuel = false;           // 锁燃油
    // 5.力场支援范围
    [Export] public int forceSupportMinRange = 0;                // x1：以本单位为圆心最小距离
    [Export] public int forceSupportMaxRange = 2;               // y1：最大距离
    // 6.力场作用效果范围
    [Export] public int forceSupportEffectMinRadius = 0;         // x2：效果最小半径
    [Export] public int forceSupportEffectMaxRadius = 0;         // y2：效果最大半径（0=仅覆盖目标本身）
    // 7.黑名单（建筑/兵器/单位全考虑：填类名或UnitId）
    [Export] public string[] forceSupportBlacklist = System.Array.Empty<string>();

    // 锁定值记录（实例→锁定量）
    private readonly Dictionary<Infantry, int> fsLockedHp = new();
    private readonly Dictionary<Infantry, int> fsLockedAmmo = new();
    private readonly Dictionary<Infantry, int> fsLockedFuel = new();
    private readonly Dictionary<Weapon, int> fsLockedWeaponHp = new();
    private readonly Dictionary<Weapon, int> fsLockedWeaponAmmo = new();
    private float fsTimer = 0f;
    [Export] public bool forceSupportManualMode = true;     // 手动模式：点按钮→支援环内选一格→效果以该格为心（false=旧自动光环）
    private Grids fsChosenTarget = null;

    // ========== 天基打击模块（自爆模块扩展）==========
    [ExportGroup("天基打击模块")]
    [Export] public bool spaceStrikeEnabled = false;             // 是否开启天基打击
    [Export] public bool spaceStrikeGlobal = false;              // 1.是否全局打击（无视射程）
    [Export] public int spaceStrikeMinRange = 1;                 // 2.以该单位为圆心最小范围
    [Export] public int spaceStrikeMaxRange = 8;                 // 2.最大范围
    [Export] public int spaceStrikeEffectMinRadius = 0;          // 3.选择目标后效果范围（以目标点为核心）
    [Export] public int spaceStrikeEffectMaxRadius = 1;          // 3.效果最大半径
    [Export] public float spaceStrikeDamagePercent = 0.5f;       // 固定伤害百分比（攻击卫星固定50%）
    [Export] public int spaceStrikeTargetPolicy = 1;              // 0=仅友军 1=仅敌军 2=全部
    [Export] public bool spaceStrikeDestroysSelf = false;        // 打击是否摧毁自身（卫星=false）
    [Export] public bool spaceStrikeAffectsWeapons = true;       // 是否影响兵器
    [Export] public bool spaceStrikeAffectsFacilities = false;   // 是否影响设施
    [Export] public int spaceStrikeMaxUses = 1;                   // 每回合次数
    [Export] public int spaceStrikeUsesThisTurn = 0;

    private static bool FsFriendly(string a, string b)
    {
        if (a == b) return true;
        if (a == TeamHelper.Player || b == TeamHelper.Player) return true;
        return false;
    }

    private bool FsTargetTeamOk(string targetTeam)
    {
        bool friendly = FsFriendly(team, targetTeam);
        return forceSupportTeamPolicy switch { 0 => friendly, 1 => !friendly, _ => true };
    }

    private bool FsBlacklisted(string name)
    {
        return forceSupportBlacklist != null && System.Array.IndexOf(forceSupportBlacklist, name) >= 0;
    }

    private bool FsInRange(Grids g)
    {
        int d = Mathf.Abs(g.GridIndex.X - grid.GridIndex.X) + Mathf.Abs(g.GridIndex.Y - grid.GridIndex.Y);
        return d >= forceSupportMinRange && d <= forceSupportMaxRange;
    }

    // 效果范围（x2-y2）：以目标格为圆心的效果圈；0=仅目标本身
    private bool FsEffectCovers(Grids targetGrid, Grids checkGrid)
    {
        if (forceSupportEffectMaxRadius <= 0) return checkGrid == targetGrid;
        int d = Mathf.Abs(checkGrid.GridIndex.X - targetGrid.GridIndex.X) + Mathf.Abs(checkGrid.GridIndex.Y - targetGrid.GridIndex.Y);
        return d >= forceSupportEffectMinRadius && d <= forceSupportEffectMaxRadius;
    }

    // 由 Infantry._Process 每帧驱动（避免 partial 重复 override）
    public void TickForceSupport(double delta)
    {
        if (!forceSupportEnabled) return;
        fsTimer += (float)delta;
        if (fsTimer < 0.5f) return;
        fsTimer = 0f;
        ApplyForceSupport();
    }

    // 力场支援主逻辑：范围内目标锁死当前值
    // 新规矩：对多格兵器，所有加成/减成必须覆盖其弱点格！只覆盖其他格子不看！
    public void ApplyForceSupport()
    {
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null || grid == null) return;

        // 清理失效记录
        CleanupFsRecords();

        var mapGrids = gm.gridManager.grids.Where(g => g != null).ToList();

        // 计算效果覆盖的所有格子（支持圈x1-y1 + 每个目标格的效果圈x2-y2）
        HashSet<Grids> covered;
        if (forceSupportManualMode)
        {
            if (fsChosenTarget == null || !GodotObject.IsInstanceValid(fsChosenTarget)) return;   // 手动模式未选目标→不生效
            covered = new HashSet<Grids> { fsChosenTarget };
            if (forceSupportEffectMaxRadius > 0)
                foreach (var g in mapGrids)
                    if (FsEffectCovers(fsChosenTarget, g)) covered.Add(g);
        }
        else
        {
            covered = new HashSet<Grids>();
            foreach (var cg in mapGrids.Where(FsInRange).ToList())
            {
                if (forceSupportEffectMaxRadius <= 0) { covered.Add(cg); continue; }
                foreach (var g in mapGrids)
                    if (FsEffectCovers(cg, g)) covered.Add(g);
            }
        }
        }

        // 单位
        if (forceSupportToUnits)
        {
            foreach (var g in covered)
            {
                g.infantries?.RemoveAll(i => !IsInstanceValid(i));
                foreach (var u in g.infantries ?? new List<Infantry>())
                {
                    if (u == null || u == this || !IsInstanceValid(u)) continue;
                    if (!FsTargetTeamOk(u.team)) continue;
                    if (FsBlacklisted(u.GetType().Name) || FsBlacklisted(u.UnitTypeId)) continue;
                    LockUnitValues(u);
                }
            }
        }

        // 兵器（必须覆盖弱点格）
        if (forceSupportToWeapons)
        {
            var gmWm = gm.weaponManager;
            if (gmWm != null)
            {
                foreach (var w in gmWm.AllWeapons.ToList())
                {
                    if (w == null || !IsInstanceValid(w) || w.isDestroyed) continue;
                    if (!FsTargetTeamOk(w.team)) continue;
                    if (FsBlacklisted(w.GetType().Name)) continue;
                    // 新规矩：效果必须覆盖弱点格
                    Grids weak = w.GetWeakPointGrid();
                    if (weak == null || !covered.Contains(weak)) continue;
                    LockWeaponValues(w);
                }
            }
        }

        // 设施
        if (forceSupportToFacilities)
        {
            foreach (var g in covered)
            {
                var f = g.city;
                if (f == null) continue;
                if (!FsTargetTeamOk(f.facilityTeam)) continue;
                if (FsBlacklisted(f.GetType().Name)) continue;
                // 设施锁血：锁死占领状态（不锁占领进度）
                LockFacilityValues(f);
            }
        }
    }

    private void CleanupFsRecords()
    {
        foreach (var k in fsLockedHp.Keys.ToList()) if (k == null || !IsInstanceValid(k)) { fsLockedHp.Remove(k); }
        foreach (var k in fsLockedAmmo.Keys.ToList()) if (k == null || !IsInstanceValid(k)) { fsLockedAmmo.Remove(k); }
        foreach (var k in fsLockedFuel.Keys.ToList()) if (k == null || !IsInstanceValid(k)) { fsLockedFuel.Remove(k); }
        foreach (var k in fsLockedWeaponHp.Keys.ToList()) if (k == null || !IsInstanceValid(k)) { fsLockedWeaponHp.Remove(k); }
        foreach (var k in fsLockedWeaponAmmo.Keys.ToList()) if (k == null || !IsInstanceValid(k)) { fsLockedWeaponAmmo.Remove(k); }
    }

    private void LockUnitValues(Infantry u)
    {
        if (forceSupportLockHP)
        {
            if (!fsLockedHp.ContainsKey(u)) fsLockedHp[u] = u.health;
            else u.health = fsLockedHp[u];
        }
        if (forceSupportLockAmmoType > 0)
        {
            int cur = u.GetAmmoValueByType(forceSupportLockAmmoType);
            if (!fsLockedAmmo.ContainsKey(u)) fsLockedAmmo[u] = cur;
            else u.SetAmmoValueByType(forceSupportLockAmmoType, fsLockedAmmo[u]);
        }
        if (forceSupportLockFuel)
        {
            if (!fsLockedFuel.ContainsKey(u)) fsLockedFuel[u] = u.fuel;
            else u.fuel = fsLockedFuel[u];
        }
        u.UpdateHpLabel();
    }

    private void LockWeaponValues(Weapon w)
    {
        if (forceSupportLockHP)
        {
            if (!fsLockedWeaponHp.ContainsKey(w)) fsLockedWeaponHp[w] = w.health;
            else w.health = fsLockedWeaponHp[w];
            w.UpdateHpLabel();
        }
        if (forceSupportLockAmmoType > 0)
        {
            int cur = w.GetAmmoByType(forceSupportLockAmmoType);
            if (!fsLockedWeaponAmmo.ContainsKey(w)) fsLockedWeaponAmmo[w] = cur;
            else w.SetAmmoByType(forceSupportLockAmmoType, fsLockedWeaponAmmo[w]);
        }
    }

    private void LockFacilityValues(Facility f)
    {
        // 设施锁血：保持所属队伍不变（势力锁定）
        // 占领进度等动态值不锁，仅锁 facilityTeam 的"血量"等价物
    }

    // 单位弹药辅助（单位侧弹种：1=主武器 2=自爆 3=照明 4=隐身 5=显形 6=迷彩）
    public int GetAmmoValueByType(int type) => type switch
    {
        1 => currentPrimaryAmmo,
        2 => currentExplodeAmmo,
        3 => currentFlareAmmo,
        4 => currentStealthBullets,
        5 => currentRevealBullets,
        6 => currentCamoBullets,
        _ => 0,
    };

    public void SetAmmoValueByType(int type, int value)
    {
        switch (type)
        {
            case 1: currentPrimaryAmmo = value; break;
            case 2: currentExplodeAmmo = value; break;
            case 3: currentFlareAmmo = value; break;
            case 4: currentStealthBullets = value; break;
            case 5: currentRevealBullets = value; break;
            case 6: currentCamoBullets = value; break;
        }
    }

    // ========== 天基打击执行 ==========
    public void PerformSpaceStrike(Grids targetGrid)
    {
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null || targetGrid == null) return;
        if (spaceStrikeUsesThisTurn >= spaceStrikeMaxUses) return;
        spaceStrikeUsesThisTurn++;

        var blastGrids = gm.gridManager.grids.Where(g =>
        {
            int d = Mathf.Abs(g.GridIndex.X - targetGrid.GridIndex.X) + Mathf.Abs(g.GridIndex.Y - targetGrid.GridIndex.Y);
            return d >= spaceStrikeEffectMinRadius && d <= spaceStrikeEffectMaxRadius;
        }).ToList();

        foreach (var g in blastGrids) ApplySpaceStrikeEffects(g, gm);
        UpdateHpLabel();

        if (spaceStrikeDestroysSelf)
        {
            health = 0;
            var gm2 = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            gm2?.unitManager?.RemoveUnit(this);
        }
    }

    private bool SsTargetTeamOk(string targetTeam)
    {
        bool friendly = FsFriendly(team, targetTeam);
        return spaceStrikeTargetPolicy switch { 0 => friendly, 1 => !friendly, _ => true };
    }

    private void ApplySpaceStrikeEffects(Grids g, GameManager gm)
    {
        g.infantries?.RemoveAll(i => !IsInstanceValid(i));
        foreach (var u in (g.infantries ?? new List<Infantry>()).ToList())
        {
            if (u == null || !IsInstanceValid(u) || u == this) continue;
            if (!SsTargetTeamOk(u.team)) continue;
            int dmg = Mathf.RoundToInt(u.maxHealth * spaceStrikeDamagePercent);
            u.health = Mathf.Max(1, u.health - dmg);
            u.UpdateHpLabel();
            if (u.health <= 1) { /* 卫星打击固定50%不击杀 */ }
        }
        if (spaceStrikeAffectsWeapons)
        {
            foreach (var w in (g.weapons ?? new List<Weapon>()).ToList())
            {
                if (w == null || !IsInstanceValid(w) || w.isDestroyed) continue;
                if (!SsTargetTeamOk(w.team)) continue;
                int dmg = Mathf.RoundToInt(w.maxHealth * spaceStrikeDamagePercent);
                w.health = Mathf.Max(0, w.health - dmg);
                w.UpdateHpLabel();
                if (w.health <= 0)
                {
                    if (w is MissileSilo ms) ms.BecomeEmptySilo();
                    else w.TakeDamage(99999);
                }
            }
        }
        if (spaceStrikeAffectsFacilities && g.city != null)
        {
            // 设施：天基打击使占领进度清零（压制）
            foreach (var u in (g.infantries ?? new List<Infantry>())) u.currentCaptureProgress = 0;
        }
    }

    public void ResetSpaceStrikeUses() => spaceStrikeUsesThisTurn = 0;

    // —— 力场支援·手动选目标（照导弹井 BeginTargetSelection 流程）——
    public void BeginForceSupportSelection(Infantry activator)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null || grid == null) return;
        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = true;
        bool any = false;
        foreach (var node in GetTree().GetNodesInGroup("grids"))
        {
            if (node is not Grids g) continue;
            if (FsInRange(g))
            {
                any = true;
                g.Modulate = new Color(0.3f, 0.9f, 1f, 0.85f);   // 支援色：青
                g.IsInWeaponRange = true;
                gm.gridManager.OverrideUnitInput(g, true);
                g.OnClickGrid = (to) => { ConfirmForceSupport(to); };
            }
            else
            {
                gm.gridManager.OverrideUnitInput(g, true);
                g.OnClickEmpty = () => CancelForceSupportSelection(gm);
            }
        }
        if (!any) CancelForceSupportSelection(gm);
    }

    public void CancelForceSupportSelection(GameManager gm)
    {
        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = false;
        foreach (var node in GetTree().GetNodesInGroup("grids"))
            if (node is Grids g) { gm.gridManager.OverrideUnitInput(g, false); g.OnClickGrid = null; g.OnClickEmpty = null; }
    }

    public void ConfirmForceSupport(Grids target)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || target == null) return;
        CancelForceSupportSelection(gm);
        if (!FsInRange(target)) return;          // 环外点击无效
        fsChosenTarget = target;                  // 再选新格=以新格为心，旧目标脱离覆盖恢复自然值
        GD.Print($"[力场支援] {Name} 支援目标格 {target.GridIndex}");
    }
}
