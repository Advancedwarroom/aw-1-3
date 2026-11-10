// ActionMenu.GrandBolt.cs - 大雷弹"蠕动"按钮（新机制：每回合整体平移1格）
using Godot;

public partial class ActionMenu
{
    private Button wriggleButton;
    private bool isWriggleTargeting = false;

    private Button produceButton;
    private bool produceWired = false;

    // ✅ 生产方式②（开创人2026-11-11）：手动生产按钮——与蠕动按钮同位懒创建
    private void EnsureProduceButton()
    {
        if (produceButton != null) return;
        var parent = (wriggleButton?.GetParent() as Container) ?? (attackButton?.GetParent() as Container);
        if (parent == null) return;
        produceButton = new Button { Text = "生产" };
        parent.AddChild(produceButton);
        if (!produceWired)
        {
            produceButton.Pressed += OnProducePressed;
            produceWired = true;
        }
    }

    private void OnProducePressed()
    {
        if (currentWeapon is not GrandBolt gb) return;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        gb.ManualProduce(gm);
        Hide();
    }

    // 由 ShowWeaponMenu 调用：模式②手动生产
    public void UpdateGrandBoltProduceButton()
    {
        EnsureProduceButton();
        if (produceButton == null) return;
        produceButton.Visible = currentWeapon is GrandBolt gb && gb.CanManualProduce();
    }

    private void EnsureWriggleButton()
    {
        if (wriggleButton != null) return;
        wriggleButton = new Button();
        TranslateGroup.SetText(wriggleButton, "蠕动");
        wriggleButton.Name = "WriggleButton";
        if (attackButton != null && attackButton.GetParent() != null)
        {
            var idx = attackButton.GetParent().GetChildren().IndexOf(attackButton);
            attackButton.GetParent().AddChild(wriggleButton);
            if (idx >= 0) wriggleButton.GetParent().MoveChild(wriggleButton, idx + 1);
        }
        else GetParent()?.AddChild(wriggleButton);
        wriggleButton.Pressed += OnWrigglePressed;
    }

    // 由 ShowWeaponMenu 调用
    public void UpdateWriggleButton()
    {
        EnsureWriggleButton();
        if (wriggleButton != null) wriggleButton.Visible = false;
        if (currentWeapon == null || !IsInstanceValid(currentWeapon)) return;
        if (currentWeapon is GrandBolt gb && gb.wriggleEnabled && gb.CanWriggle())
        {
            if (wriggleButton != null) wriggleButton.Visible = true;
        }
    }

    private void OnWrigglePressed()
    {
        if (currentWeapon == null || !IsInstanceValid(currentWeapon)) return;
        if (currentWeapon is not GrandBolt gb) return;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return;

        if (!isWriggleTargeting)
        {
            isWriggleTargeting = true;
            BeginWriggleTargeting(gb, gm);
        }
        else
        {
            isWriggleTargeting = false;
            CancelWriggleTargeting(gm);
        }
    }

    // ✅ 蠕动瞄准：高亮4个方向格，点击方向格执行蠕动
    private void BeginWriggleTargeting(GrandBolt gb, GameManager gm)
    {
        Hide();
        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = true;

        // 4个方向：上下左右
        var directions = new Vector2I[] {
            new Vector2I(0, -1), // 上
            new Vector2I(0, 1),  // 下
            new Vector2I(-1, 0), // 左
            new Vector2I(1, 0),  // 右
        };

        foreach (var dir in directions)
        {
            var anchor = gb.grid.GridIndex + dir;
            if (anchor.X < 0 || anchor.Y < 0 || anchor.X >= gm.gridManager.searchRange.X || anchor.Y >= gm.gridManager.searchRange.Y) continue;
            var g = gm.gridManager.map[anchor.X, anchor.Y];
            if (g == null) continue;

            g.attackRangeIcon?.Show();
            if (g.attackRangeIcon != null)
                g.attackRangeIcon.Modulate = new Color(0.3f, 0.9f, 0.3f, 0.85f); // 绿色
            g.IsInWeaponRange = true;
            g.OnClickGrid = (target) =>
            {
                if (!IsInstanceValid(gb)) return;
                CancelWriggleTargeting(gm);
                gb.Wriggle(dir, gm); // ✅ 执行蠕动
            };
            gm.gridManager.OverrideUnitInput(g, true);
        }
        foreach (var g in gm.gridManager.grids)
        {
            g.OnClickEmpty = () =>
            {
                if (!IsInstanceValid(gb)) return;
                CancelWriggleTargeting(gm);
            };
        }
    }

    private void CancelWriggleTargeting(GameManager gm)
    {
        isWriggleTargeting = false;
        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = false;
        foreach (var g in gm.gridManager.grids)
        {
            gm.gridManager.OverrideUnitInput(g, false);
            g.OnClickGrid = null;
            g.OnClickEmpty = null;
        }
    }
}
