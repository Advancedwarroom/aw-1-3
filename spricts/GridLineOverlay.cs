using Godot;

// 格线覆盖层：给所有现有格子描边，方便数格子（开创人2026-11-07）。
// 由全局配置面板的"格线"开关控制，开关在 GridManager 下懒创建/销毁本节点。
public partial class GridLineOverlay : Node2D
{
    private GridManager gm;

    public void Setup(GridManager gridManager)
    {
        gm = gridManager;
        ZIndex = 2000;   // 压在单位和地形之上
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (gm?.grids == null) return;
        var c = new Color(0f, 0f, 0f, 0.4f);
        foreach (var g in gm.grids)
        {
            if (g == null || !IsInstanceValid(g)) continue;
            DrawRect(new Rect2(g.Position - new Vector2(16, 16), new Vector2(32, 32)), c, false, 1.5f);
        }
    }
}
