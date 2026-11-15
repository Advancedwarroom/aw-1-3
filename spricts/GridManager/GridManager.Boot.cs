using Godot;
using System.Threading.Tasks;

// ✅ 方案甲（2026-11-15）：分帧版 ResizeMap——逐行实例化格子并上报 BootMapLoader 进度。
// 与 GridManager.ResizeMap（同步权威版）逐步对应：改生成逻辑需两边同步改。
// 目的：消灭 change_scene_to_packed 携带 700+ 格子子节点时的原子实例化黑块（开盘加载大头）。
public partial class GridManager
{
    public async Task ResizeMapChunked(int newWidth, int newHeight, BootMapLoader boot)
    {
        teleportLinks.Clear();
        if (GridsNode == null) return;

        // 1. 删除旧格子（同 ResizeMap）
        var oldChildren = GridsNode.GetChildren();
        foreach (var child in oldChildren)
        {
            if (child is Grids g)
            {
                GridsNode.RemoveChild(g);
                g.Free();
            }
        }
        grids.Clear();
        map = null;

        // 2. 设置尺寸
        searchRange = new Vector2I(newWidth, newHeight);

        // 3. 逐行实例化（每行让一帧，进度按行 tick）
        var gridScene = GD.Load<PackedScene>("res://Prefabs/grid.tscn");
        if (gridScene == null) { GD.PushError("[Boot] grid.tscn 缺失"); return; }

        for (int y = 0; y < newHeight; y++)
        {
            for (int x = 0; x < newWidth; x++)
            {
                if (!IsInsideAnyScreen(x, y)) continue;
                var grid = gridScene.Instantiate<Grids>();
                grid.Name = $"Grid_{x}_{y}";
                grid.Position = new Godot.Vector2(startPos.X + x * gridSize.X, startPos.Y + y * gridSize.Y);
                grid.gridType = GridType.GROUND;
                grid.GridIndex = new Vector2I(x, y);
                GridsNode.AddChild(grid);
            }
            boot?.Tick(1);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        // 4. 重建索引（同 ResizeMap 尾部）
        Init();
    }
}
