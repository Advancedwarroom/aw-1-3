// ActionMenu.SiloStrike.cs - 导弹井发射按钮 + 天基打击按钮（动态创建，不依赖.tscn连线）
using Godot;
using System.Linq;

public partial class ActionMenu
{
    private Button siloFireButton;
    private Button spaceStrikeButton;
    private bool isSiloTargeting = false;
    private bool isStrikeTargeting = false;

    private void EnsureSiloStrikeButtons()
    {
        if (siloFireButton == null)
        {
            siloFireButton = new Button();
            TranslateGroup.SetText(siloFireButton, "发射导弹");
            siloFireButton.Name = "SiloFireButton";
            if (explodeButton != null && explodeButton.GetParent() != null)
            {
                var idx = explodeButton.GetParent().GetChildren().IndexOf(explodeButton);
                explodeButton.GetParent().AddChild(siloFireButton);
                if (idx >= 0) siloFireButton.GetParent().MoveChild(siloFireButton, idx + 1);
            }
            else GetParent()?.AddChild(siloFireButton);
            siloFireButton.Pressed += OnSiloFirePressed;
        }
        if (spaceStrikeButton == null)
        {
            spaceStrikeButton = new Button();
            TranslateGroup.SetText(spaceStrikeButton, "天基打击");
            spaceStrikeButton.Name = "SpaceStrikeButton";
            if (siloFireButton != null && siloFireButton.GetParent() != null)
            {
                var idx = siloFireButton.GetParent().GetChildren().IndexOf(siloFireButton);
                siloFireButton.GetParent().AddChild(spaceStrikeButton);
                if (idx >= 0) spaceStrikeButton.GetParent().MoveChild(spaceStrikeButton, idx + 1);
            }
            else GetParent()?.AddChild(spaceStrikeButton);
            spaceStrikeButton.Pressed += OnSpaceStrikePressed;
        }
    }

    // 由 ShowMenu 调用
    public void UpdateSiloStrikeButtons()
    {
        EnsureSiloStrikeButtons();
        if (siloFireButton != null) siloFireButton.Visible = false;
        if (spaceStrikeButton != null) spaceStrikeButton.Visible = false;
        if (currentUnit == null || !IsInstanceValid(currentUnit)) return;

        // 发射导弹：单位站在导弹井上
        var grid = currentUnit.grid;
        if (grid != null && grid.weapons != null)
        {
            foreach (var w in grid.weapons)
            {
                if (w is MissileSilo silo && silo.CanBeFiredBy(currentUnit))
                {
                    if (siloFireButton != null) siloFireButton.Visible = true;
                    break;
                }
            }
        }

        // 天基打击：开启模块且未行动且还有次数
        if (currentUnit.spaceStrikeEnabled && !currentUnit.hasActed
            && currentUnit.state != UnitState.Acted
            && currentUnit.spaceStrikeUsesThisTurn < currentUnit.spaceStrikeMaxUses)
        {
            if (spaceStrikeButton != null)
            {
                spaceStrikeButton.Visible = true;
                TranslateGroup.SetText(spaceStrikeButton, isStrikeTargeting ? "确认打击目标" : "天基打击");
            }
        }
    }

    // ---------- 导弹井发射 ----------
    private void OnSiloFirePressed()
    {
        if (currentUnit == null || !IsInstanceValid(currentUnit)) return;
        var grid = currentUnit.grid;
        if (grid == null) return;
        MissileSilo silo = null;
        foreach (var w in grid.weapons)
            if (w is MissileSilo s && s.CanBeFiredBy(currentUnit)) { silo = s; break; }
        if (silo == null) return;

        isSiloTargeting = true;
        Hide();
        silo.BeginTargetSelection(currentUnit);
    }

    // ---------- 天基打击 ----------
    private void OnSpaceStrikePressed()
    {
        if (currentUnit == null || !IsInstanceValid(currentUnit)) return;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return;

        if (!isStrikeTargeting)
        {
            isStrikeTargeting = true;
            BeginSpaceStrikeTargeting(currentUnit, gm);
        }
        else
        {
            isStrikeTargeting = false;
        }
    }

    public void BeginSpaceStrikeTargeting(Infantry unit, GameManager gm)
    {
        Hide();
        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = true;

        var rangeGrids = gm.gridManager.grids.Where(g =>
        {
            if (unit.spaceStrikeGlobal) return true;
            int d = Mathf.Abs(g.GridIndex.X - unit.grid.GridIndex.X) + Mathf.Abs(g.GridIndex.Y - unit.grid.GridIndex.Y);
            return d >= unit.spaceStrikeMinRange && d <= unit.spaceStrikeMaxRange;
        }).ToList();

        foreach (var g in rangeGrids)
        {
            g.attackRangeIcon?.Show();
            if (g.attackRangeIcon != null) g.attackRangeIcon.Modulate = new Color(0.7f, 0.3f, 1f, 0.85f);
            g.IsInWeaponRange = true;
            g.OnClickGrid = (to) =>
            {
                if (!IsInstanceValid(unit)) return;
                CancelSpaceStrikeTargeting(gm);
                unit.PerformSpaceStrike(g);
            };
            gm.gridManager.OverrideUnitInput(g, true);
        }
        foreach (var g in gm.gridManager.grids)
        {
            if (rangeGrids.Contains(g)) continue;
            g.OnClickEmpty = () =>
            {
                if (!IsInstanceValid(unit)) return;
                CancelSpaceStrikeTargeting(gm);
            };
        }
    }

    public void CancelSpaceStrikeTargeting(GameManager gm)
    {
        isStrikeTargeting = false;
        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = false;
        foreach (var g in gm.gridManager.grids)
        {
            gm.gridManager.OverrideUnitInput(g, false);
            g.OnClickGrid = null; g.OnClickEmpty = null;
        }
    }
}
