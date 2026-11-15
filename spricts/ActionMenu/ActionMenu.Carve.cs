// ActionMenu.Carve.cs —— 开凿按钮接线（开创人2026-11-15）
// 单位身侧存在可凿的 999 原始山体时亮"开凿"，点击对首个有效邻格执行 TryCarveMountain。
// 可见性判据与 TryCarveMountain 内部校验逐项对齐：山体/已凿过/资金/燃油。
using Godot;

public partial class ActionMenu
{
    private Button carveButton;
    private Grids carveTargetGrid;

    private void EnsureCarveButton()
    {
        if (carveButton != null) return;
        var parent = moveButton?.GetParent() as Container;
        if (parent == null) return;
        carveButton = new Button { Text = "开凿" };
        parent.AddChild(carveButton);
        carveButton.Pressed += OnCarvePressed;
    }

    private void OnCarvePressed()
    {
        var u = currentUnit;
        if (u != null && IsInstanceValid(u) && carveTargetGrid != null && IsInstanceValid(carveTargetGrid))
        {
            if (!u.TryCarveMountain(carveTargetGrid))
                GD.PushWarning("[开凿] 执行失败——资金/燃油/地形校验未过（按钮判据外的时间差，属预期）");
        }
        Hide();
    }

    // 由单位刷新枢纽调用（与 UpdatePaidResupplyButtons 同排）
    public void UpdateCarveButton()
    {
        if (carveButton != null) carveButton.Visible = false;
        carveTargetGrid = null;
        var u = currentUnit;
        if (u == null || !IsInstanceValid(u) || !u.canCarveMountains) return;
        if (u.grid == null) return;
        EnsureCarveButton();
        if (carveButton == null) return;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null) return;
        var map = gm.gridManager.map;
        var dirs = new Godot.Vector2I[] { new Godot.Vector2I(0, -1), new Godot.Vector2I(1, 0), new Godot.Vector2I(0, 1), new Godot.Vector2I(-1, 0) };
        foreach (var d in dirs)
        {
            var gi = u.grid.GridIndex + d;
            if (gi.X < 0 || gi.Y < 0) continue;
            if (gi.X >= map.GetLength(0) || gi.Y >= map.GetLength(1)) continue;
            var g = map[gi.X, gi.Y];
            if (g == null || g.carvedPath) continue;
            if (u.GetMoveCost(g.gridType) < 999) continue;            // 只凿 999 原始山（与 TryCarveMountain 同一判据）
            if (gm.GetTeamFunds(u.team) < u.carveCostPerCell) continue;
            if (u.fuel < u.carveFuelPerCell) continue;
            carveTargetGrid = g;
            break;
        }
        if (carveTargetGrid != null)
            carveButton.Visible = true;
    }
}
