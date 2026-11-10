using Godot;
using System;
using System.Linq;

// ActionMenu.PaidResupply.cs —— 自费补给菜单接线（开创人2026-11-11）
// 懒创建至多3个按钮（资金/自身/献祭），启用几种亮几种——多方式组合即"弹出菜单自选"。
public partial class ActionMenu
{
    private Button prBtnFunds, prBtnSelf, prBtnSacrifice;
    private bool prWired = false;
    private Infantry prTargetUnit;
    private Weapon prTargetWeapon;
    private Facility prTargetFacility;

    private void EnsurePrButtons()
    {
        if (prBtnFunds != null) return;
        var parent = (moveButton?.GetParent() as Container) ?? (waitButton?.GetParent() as Container);
        if (parent == null) return;
        prBtnFunds = new Button { Text = "自费补给·资金" };
        prBtnSelf = new Button { Text = "自费补给·自身" };
        prBtnSacrifice = new Button { Text = "自费补给·献祭" };
        parent.AddChild(prBtnFunds);
        parent.AddChild(prBtnSelf);
        parent.AddChild(prBtnSacrifice);
        if (!prWired)
        {
            prBtnFunds.Pressed += () => { if (currentUnit != null && IsInstanceValid(currentUnit)) currentUnit.ExecutePaidResupplyOn(prTargetUnit, prTargetWeapon, prTargetFacility, 0); Hide(); };
            prBtnSelf.Pressed += () => { if (currentUnit != null && IsInstanceValid(currentUnit)) currentUnit.ExecutePaidResupplyOn(prTargetUnit, prTargetWeapon, prTargetFacility, 1); Hide(); };
            prBtnSacrifice.Pressed += () => { if (currentUnit != null && IsInstanceValid(currentUnit)) currentUnit.ExecutePaidResupplyOn(prTargetUnit, prTargetWeapon, prTargetFacility, 2); Hide(); };
            prWired = true;
        }
    }

    public void UpdatePaidResupplyButtons()
    {
        var u = currentUnit;
        if (prBtnFunds != null) { prBtnFunds.Visible = false; prBtnSelf.Visible = false; prBtnSacrifice.Visible = false; }
        if (u == null || !IsInstanceValid(u) || !u.paidResupplyEnabled) return;
        EnsurePrButtons();
        if (prBtnFunds == null) return;
        if (!u.PrCanUseNow()) return;

        prTargetUnit = null; prTargetWeapon = null; prTargetFacility = null;
        bool selfOk = u.paidResupplyMode == 0 || u.paidResupplyMode == 2;
        bool otherOk = u.paidResupplyMode == 1 || u.paidResupplyMode == 2;

        if (selfOk && PrNeedsRefillUnit(u)) prTargetUnit = u;
        if (prTargetUnit == null && otherOk)
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm?.gridManager?.map != null && u.grid != null)
            {
                int w = gm.gridManager.map.GetLength(0), h = gm.gridManager.map.GetLength(1);
                for (int x = 0; x < w && prTargetUnit == null && prTargetWeapon == null && prTargetFacility == null; x++)
                    for (int y = 0; y < h; y++)
                    {
                        var g = gm.gridManager.map[x, y];
                        if (g == null) continue;
                        int d = Mathf.Abs(g.GridIndex.X - u.grid.GridIndex.X) + Mathf.Abs(g.GridIndex.Y - u.grid.GridIndex.Y);
                        if (d < u.prRangeMin || d > u.prRangeMax) continue;
                        if (u.prTargetUnits) foreach (var tu in g.infantries ?? new System.Collections.Generic.List<Infantry>())
                            if (tu != null && IsInstanceValid(tu) && tu != u && PrNeedsRefillUnit(tu) && PrWhitelistOk(u, tu.UnitTypeId, tu.GetType().Name))
                            { prTargetUnit = tu; break; }
                        if (prTargetUnit == null && u.prTargetWeapons) foreach (var tw in g.weapons ?? new System.Collections.Generic.List<Weapon>())
                            if (tw != null && IsInstanceValid(tw) && tw.PrNeedsRefillW() && PrWhitelistOk(u, null, tw.GetType().Name))
                            { prTargetWeapon = tw; break; }
                        if (prTargetUnit == null && prTargetWeapon == null && u.prTargetFacilities && g.city != null
                            && IsInstanceValid(g.city) && g.city.PrNeedsRefillF() && PrWhitelistOk(u, null, g.city.GetType().Name))
                        { prTargetFacility = g.city; break; }
                        if (prTargetUnit != null || prTargetWeapon != null || prTargetFacility != null) break;
                    }
            }
        }
        if (prTargetUnit == null && prTargetWeapon == null && prTargetFacility == null) return;

        if (u.prUseFunds) prBtnFunds.Visible = true;
        if (u.prUseSelf) prBtnSelf.Visible = true;
        if (u.prUseSacrifice) prBtnSacrifice.Visible = true;
    }

    private bool PrWhitelistOk(Infantry u, string typeId, string className)
    {
        if (u.prTargetWhitelist == null || u.prTargetWhitelist.Length == 0) return true;
        return (typeId != null && u.prTargetWhitelist.Contains(typeId)) || u.prTargetWhitelist.Contains(className);
    }
    private bool PrNeedsRefillUnit(Infantry tu)
    {
        for (int r = 0; r <= 10; r++)
            if (tu.PrTargetResEnabled(r) && !tu.PrIsInfinite(r) && tu.PrGet(r) < Mathf.Max(tu.PrGet(r), tu.PrMax(r)))
                return true;
        return false;
    }
}

// 兵器/设施"是否需要补给"（以各自被勾选的可补资源为准）
public partial class Weapon
{
    public bool PrNeedsRefillW()
    {
        for (int r = 0; r <= 10; r++)
            if (PrGetW(r) < PrMaxW(r)) return true;
        return false;
    }
}
public partial class Facility
{
    public bool PrNeedsRefillF()
    {
        for (int r = 0; r <= 10; r++)
            if (PrGetF(r) < PrMaxF(r)) return true;
        return false;
    }
}
