// GridManager.cs - 修改 HandleGridClick 和 SetupMoveRangeCallbacks 方法
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

public partial class GridManager : Node
{
    /// <summary>格子像素间距(BlackArc/巨雷定位用; 与tscn格距一致)</summary>
    public int CellWidth => 32;
    public int CellHeight => 32;

    [ExportGroup("")]
    [Export]
    public Node GridsNode;
    public Grids[,]map;
    [Export] public Vector2I searchRange;
    [Export] public Vector2I startPos;
    [Export] public Vector2I gridSize;
    [Export] public UnitManager unitManager;
    public List<Grids> previewPath = new List<Grids>();
    public Line2D pathLine;
    public List<Grids> grids =new List<Grids>();
    public Label damagePreviewLabel;

    public Infantry selectedUnit;

    public WeaponManager weaponManager;
    // ✅ N屏地图：屏数（1=普通单图）。各屏为地图横向等宽条带
    [Export] public int screenCount = 1;
    // ✅ 传送链路（编辑器传送列表维护；运行时TP按钮使用）
    public List<TeleportLink> teleportLinks = new List<TeleportLink>();

    public override void _Ready()
    {
    

        AddToGroup("grid_manager");
        isMobilePlatform = OS.GetName() == "Android" || OS.GetName() == "iOS";

        pathLine = new Line2D
        {
            Name = "PathPreview",
            Width = 4,
            DefaultColor = new Color(1,1,0,0.75f),
            ZIndex = 100
        };
        AddChild(pathLine);

        damagePreviewLabel = new Label();
        damagePreviewLabel.Name = "DamagePreviewLabel";
        damagePreviewLabel.ZIndex = 200;
        damagePreviewLabel.Hide();
        damagePreviewLabel.AddThemeColorOverride("font_color", new Color(1, 1, 0.8f));
        damagePreviewLabel.AddThemeFontSizeOverride("font_size", 14);
        damagePreviewLabel.AddThemeConstantOverride("outline_size", 3);
        damagePreviewLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        damagePreviewLabel.HorizontalAlignment = HorizontalAlignment.Left;
        damagePreviewLabel.VerticalAlignment = VerticalAlignment.Center;
        damagePreviewLabel.CustomMinimumSize = new Godot.Vector2(120, 40);
        AddChild(damagePreviewLabel);
        
        weaponManager = GetTree().GetFirstNodeInGroup("weapon_manager") as WeaponManager;
    }

    public override void _Process(double delta)
    {
    }

    private bool ShouldBlockGrid(Grids grid, Infantry requesting)
    {
        if (requesting.overlapType == UnitOverlapType.Overlapping)
            return false;
        return grid.infantry != null && grid.infantry != requesting;
    }

    // ✅ N屏工具：屏号（横向条带均分；余数归入靠前的屏）
    [Export] public int[] screenWidths = new int[0];   // ✅ 每屏宽(列数)，空数组=均分
    [Export] public int[] screenHeights = new int[0];
    [Export] public int[] screenYOffsets = new int[0];   // ✅ 每屏Y偏移（顶边悬空量），空数组=全部顶对齐
    [Export] public int screenGap = 3;   // ✅ 屏间间隔（格）：间隔带与矮屏下方不生成格子（真物理分离）

    public bool IsInsideAnyScreen(int x, int y)
    {
        if (screenCount <= 1) return true;
        int sid = GetScreenId(new Vector2I(x, y));
        if (sid < 0) return false;
        if (screenHeights != null && screenHeights.Length == screenCount)
        {
            int yoff = (screenYOffsets != null && sid < screenYOffsets.Length) ? screenYOffsets[sid] : 0;
            return y >= yoff && y < yoff + Math.Max(1, screenHeights[sid]);
        }
        return true;
    }  // ✅ 每屏高(行数)，空数组=全局高

    public int GetScreenStartX(int screen)
{
    int gap = screenGap;
    if (screenWidths != null && screenWidths.Length == screenCount && screenCount > 1)
    {
        int sum = gap * screen;
        for (int i = 0; i < screen && i < screenWidths.Length; i++) sum += Math.Max(1, screenWidths[i]);
        return sum;
    }
    int w = map != null ? map.GetLength(0) : 0;
    int per = screenCount > 0 ? (w - gap * (screenCount - 1)) / screenCount : w;
    if (per <= 0) per = 1;
    return per * screen + gap * screen;
}

    public int GetScreenId(Vector2I pos)
{
    if (screenCount <= 1) return 0;
        if (freeformScreens) { var fg = GetGridAt(pos); return fg != null ? fg.screenId : -1; }   // 自由屏：直读格子存储屏号
    for (int s = 0; s < screenCount; s++)
    {
        int start = GetScreenStartX(s);
        int w = (screenWidths != null && screenWidths.Length == screenCount)
            ? Math.Max(1, screenWidths[s])
            : Math.Max(1, GetScreenStartX(1) - screenGap - start);
        if (pos.X >= start && pos.X < start + w) return s;
    }
    return -1;   // 屏间间隔带（无格子）
}

    public int GetScreenCount() => screenCount <= 0 ? 1 : screenCount;

    // ✅ 重新计算所有格子的屏归属（改屏数后调用）

    public void RefreshScreenIds()
    {
        if (map == null) return;
        foreach (var g in grids)
            if (g != null) g.screenId = GetScreenId(g.GridIndex);
    }

 // ✅ N屏隔离核心：是否同一屏
    public bool IsSameScreen(Vector2I a, Vector2I b) => GetScreenId(a) == GetScreenId(b);

    // ✅ 按索引取格（越界安全）
    public Grids GetGridAt(Vector2I idx)
    {
        if (map == null) return null;
        int w = map.GetLength(0), h = map.GetLength(1);
        if (idx.X < 0 || idx.Y < 0 || idx.X >= w || idx.Y >= h) return null;
        return map[idx.X, idx.Y];
    }

    // ✅ 传送：取某格的全部出链（N:M，一个源格可对应多个目标格）
    public List<TeleportLink> GetOutgoingTeleportLinks(Grids g)
    {
        if (g == null || teleportLinks == null) return new List<TeleportLink>();
        return teleportLinks.Where(l => l.from == g.GridIndex).ToList();
    }

    // ✅ 传送：添加链路（互传时自动补反向链，重复链去重）
    public void AddTeleportLink(Vector2I f, Vector2I t, bool oneWay)
    {
        if (f == t) return;
        if (teleportLinks.Any(l => l.from == f && l.to == t)) return;
        teleportLinks.Add(new TeleportLink(f, t, true));
        if (!oneWay && !teleportLinks.Any(l => l.from == t && l.to == f))
            teleportLinks.Add(new TeleportLink(t, f, true));
    }

    public void RemoveTeleportLink(TeleportLink l)
    {
        if (l == null) return;
        teleportLinks.RemoveAll(x => x.from == l.from && x.to == l.to);
    }

    public void ClearTeleportLinks() => teleportLinks.Clear();

    public void Init()
    {
        map = new Grids[searchRange.X, searchRange.Y];
        grids.Clear(); // ✅ 防止重复填充导致统计翻倍
        for (int i = 0; i < GridsNode.GetChildCount(); i++)
        {
            Grids grid = GridsNode.GetChild<Grids>(i);
            grids.Add(grid);
            int x = (int)((grid.Position.X - startPos.X) / gridSize.X);
            int y = (int)((grid.Position.Y - startPos.Y) / gridSize.Y);

            if (x >= 0 && x < searchRange.X && y >= 0 && y < searchRange.Y)
            {
                grid.GridIndex = new Vector2I(x, y);
                map[x, y] = grid;
                grid.screenId = GetScreenId(grid.GridIndex); // ✅ N屏条带归属

                switch (grid.gridType)
                {
                    case GridType.GROUND:
                    case GridType.ROAD:
                        break;
                    case GridType.HILL:
                        break;
                    default:
                        break;
                }

                // ✅ 初始化城市默认势力
                if (grid.city != null && string.IsNullOrEmpty(grid.city.facilityTeam))
                {
                    int middleStart = searchRange.X / 3;
                    int middleEnd = searchRange.X * 2 / 3;

                    if (x >= middleStart && x < middleEnd)
                        grid.city.facilityTeam = "Player0";
                    else if (x < middleStart)
                        grid.city.facilityTeam = "Player1";
                    else
                        grid.city.facilityTeam = "Player2";
                }
            }
            else
            {
                grid.GridIndex = new Vector2I(-1, -1);
            }
        }

        // ✅ 多方向地形：自动计算方向位掩码（PIPE / ROAD / BRIDGE 等）
        RefreshAllTerrainDirections();
    }

    /// <summary>刷新全地图多方向地形的方向位掩码（地图加载/地形变化后调用）</summary>
    public void RefreshAllTerrainDirections()
    {
        if (map == null) return;
        int w = map.GetLength(0), h = map.GetLength(1);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                map[x, y]?.RefreshDirectionFromNeighbors(map);
    }

    /// <summary>
    /// 重新调整地图大小：删除旧格子，创建新格子，重置 searchRange
    /// </summary>
    public void ResizeMap(int newWidth, int newHeight)
    {
        teleportLinks.Clear(); // ✅ 尺寸变化后原索引失效，清空传送链（导入时会重建）
        if (GridsNode == null) return;

        // 1. 删除所有旧格子（立即删除，避免延迟删除导致 Init 遍历到旧格子）
        var oldChildren = GridsNode.GetChildren().ToList();
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

        // 2. 更新尺寸
        searchRange = new Vector2I(newWidth, newHeight);

        // 3. 加载格子场景并实例化
        var gridScene = GD.Load<PackedScene>("res://Prefabs/grid.tscn");
        if (gridScene == null)
        {
            return;
        }

        for (int y = 0; y < newHeight; y++)
        {
            for (int x = 0; x < newWidth; x++)
            {
                if (!IsInsideAnyScreen(x, y)) continue;   // ✅ 间隔带/矮屏下方=真空（无格子节点）
                var grid = gridScene.Instantiate<Grids>();
                grid.Name = $"Grid_{x}_{y}";
                grid.Position = new Godot.Vector2(startPos.X + x * gridSize.X, startPos.Y + y * gridSize.Y);
                grid.gridType = GridType.GROUND;
                grid.GridIndex = new Vector2I(x, y);
                GridsNode.AddChild(grid);
            }
        }

        // 4. 重新初始化
        Init();
    
    }

    // ✅ 跨屏判定：格子属于第几个屏（-1=真空/屏外）。绑定系统"是否可跨屏绑定"开关的判定入口
    public int GetScreenIndexOf(Vector2I cell)
    {
        int n = GetScreenCount();
        for (int k = 0; k < n; k++)
        {
            int sx = GetScreenStartX(k);
            int yo = (screenYOffsets != null && k < screenYOffsets.Length) ? screenYOffsets[k] : 0;
            int hh = (screenHeights != null && k < screenHeights.Length && screenHeights[k] > 0) ? screenHeights[k] : searchRange.Y;
            int ww = (screenWidths != null && k < screenWidths.Length && screenWidths[k] > 0) ? screenWidths[k] : (searchRange.X / Mathf.Max(1, n));
            if (cell.X >= sx && cell.X < sx + ww && cell.Y >= yo && cell.Y < yo + hh) return k;
        }
        return -1;
    }

    public bool IsSameScreen(Vector2I a, Vector2I b)
    {
        int sa = GetScreenIndexOf(a), sb = GetScreenIndexOf(b);
        return sa >= 0 && sa == sb;
    }

    // ✅ 每屏独立宽高加减（Extra配置·分屏调整）：宽锚 0左/1中/2右，高锚 0上/1中/2下（严格：底边/顶边真正钉死）
    // 高方向引入每屏Y偏移：下锚扩=顶边上移、内容原地不动（底边严格固定）；世界最小y<0时整体下移归一化
    // 收缩区有 单位/兵器/城市/传送端点 时整体拒绝
    public string ResizeScreen(int idx, int dw, int dh, int wAnchor, int hAnchor)
    {
        int n = GetScreenCount();
        if (idx < 0 || idx >= n) return TranslateGroup.T("屏索引越界");
        dw = (int)Mathf.Clamp(dw, -300, 300);
        dh = (int)Mathf.Clamp(dh, -300, 300);
        if (dw == 0 && dh == 0) return TranslateGroup.T("增减量都是 0");

        int[] ws = new int[n];
        int baseW = searchRange.X / n;
        for (int k = 0; k < n; k++) ws[k] = (screenWidths != null && screenWidths.Length == n) ? screenWidths[k] : baseW;
        int[] hs = new int[n];
        for (int k = 0; k < n; k++) hs[k] = (screenHeights != null && screenHeights.Length == n && screenHeights[k] > 0) ? screenHeights[k] : searchRange.Y;
        int[] yoffs = new int[n];
        for (int k = 0; k < n; k++) yoffs[k] = (screenYOffsets != null && k < screenYOffsets.Length) ? screenYOffsets[k] : 0;

        int newW = (int)Mathf.Clamp(ws[idx] + dw, 5, 300); dw = newW - ws[idx];
        int newH = (int)Mathf.Clamp(hs[idx] + dh, 5, 300); dh = newH - hs[idx];
        if (dw == 0 && dh == 0) return TranslateGroup.T("已达尺寸上下限");

        int leftShift = (wAnchor == 2) ? dw : (wAnchor == 1 ? dw / 2 : 0);
        int rightShift = dw - leftShift;
        int topShift = (hAnchor == 2) ? dh : (hAnchor == 1 ? dh / 2 : 0);   // >0=顶边上移（严格底边）
        int startX = GetScreenStartX(idx);
        int endX = startX + ws[idx];
        int newStartX = startX - leftShift;
        int newEndX = endX + rightShift;

        if (idx > 0 && leftShift > 0)
        {
            int prevEnd = GetScreenStartX(idx - 1) + ws[idx - 1];
            if (newStartX < prevEnd) return TranslateGroup.T("左扩会撞上邻屏，已拒绝");
        }

        // —— 新几何 ——
        ws[idx] = newW; hs[idx] = newH;
        int oldOff = yoffs[idx];
        int newOff = oldOff - topShift;
        int worldShift = 0;
        if (newOff < 0) { worldShift = -newOff; newOff = 0; }   // 世界归一化：全体下移
        for (int k = 0; k < n; k++) yoffs[k] += worldShift;
        yoffs[idx] = newOff;
        int totalW = screenGap * (n - 1); foreach (var v in ws) totalW += v;
        int totalH = 0; for (int k = 0; k < n; k++) totalH = Mathf.Max(totalH, yoffs[k] + hs[k]);

        screenWidths = ws; screenHeights = hs; screenYOffsets = yoffs;

        // —— 旧→新坐标映射（宽：内容随带平移；高：内容原地不动 + 世界归一化）——
        var moved = new System.Collections.Generic.Dictionary<Vector2I, Vector2I>();
        foreach (var g in grids)
        {
            if (g == null) continue;
            int gx = g.GridIndex.X, gy = g.GridIndex.Y, nx = gx, ny = gy;
            if (dw != 0)
            {
                if (gx >= endX) nx = gx + rightShift;
                else if (gx >= startX) nx = gx - leftShift;
            }
            if (dh != 0 || worldShift > 0) ny = gy + worldShift;
            moved[g.GridIndex] = new Vector2I(nx, ny);
        }

        var newCells = new System.Collections.Generic.HashSet<Vector2I>();
        for (int y = 0; y < totalH; y++)
            for (int x = 0; x < totalW; x++)
                if (IsInsideAnyScreen(x, y)) newCells.Add(new Vector2I(x, y));

        // —— 收缩区守卫 ——
        foreach (var g in grids)
        {
            if (g == null) continue;
            var np = moved[g.GridIndex];
            if (newCells.Contains(np)) continue;
            if (g.infantry != null || g.weapon != null || g.city != null)
                return TranslateGroup.T("收缩区域有单位/兵器/城市，先移走再调整");
        }
        if (teleportLinks != null)
            foreach (var l in teleportLinks)
            {
                if (l == null) continue;
                bool fromOk = moved.TryGetValue(l.from, out var nf) && newCells.Contains(nf);
                bool toOk = moved.TryGetValue(l.to, out var nt) && newCells.Contains(nt);
                if (!fromOk || !toOk) return TranslateGroup.T("收缩区域有传送链路端点，先删除链路再调整");
            }

        // —— 执行 ——
        foreach (var g in grids)
        {
            if (g == null) continue;
            var np = moved[g.GridIndex];
            if (!newCells.Contains(np)) { GridsNode.RemoveChild(g); g.Free(); continue; }
            g.GridIndex = np;
            g.Name = $"Grid_{np.X}_{np.Y}";
            g.Position = startPos + new Vector2(np.X * gridSize.X, np.Y * gridSize.Y);
        }
        var scene = GD.Load<PackedScene>("res://grid.tscn");
        foreach (var c in newCells)
        {
            if (GetGridAt(c) != null) continue;
            var grid = scene.Instantiate<Grids>();
            grid.gridType = GridType.GROUND;
            grid.GridIndex = c;
            grid.Name = $"Grid_{c.X}_{c.Y}";
            grid.Position = startPos + new Vector2(c.X * gridSize.X, c.Y * gridSize.Y);
            GridsNode.AddChild(grid);
        }
        searchRange = new Vector2I(totalW, totalH);
        if (teleportLinks != null)
            foreach (var l in teleportLinks)
            {
                if (l == null) continue;
                if (moved.TryGetValue(l.from, out var nf2)) l.from = nf2;
                if (moved.TryGetValue(l.to, out var nt2)) l.to = nt2;
            }
        Init();   // 依坐标重建 map/grids 索引
        return null;
    }

    // ✅ 移除不在任何屏范围内的格子（多屏真空带），用于导入/载入后清理几何
    public void PruneOutsideScreens()
    {
        for (int i = grids.Count - 1; i >= 0; i--)
        {
            var g = grids[i];
            if (g == null) continue;
            if (IsInsideAnyScreen(g.GridIndex.X, g.GridIndex.Y)) continue;
            GridsNode.RemoveChild(g);
            g.Free();
            grids.RemoveAt(i);
        }
        Init();
    }

    public Vector2I[] offset =
    {
        new Vector2I(0,1),
        new Vector2I(-1,0),
        new Vector2I(0,-1),
        new Vector2I(1,0),
    };

    public List<Grids> GetAttackNeighbours(Grids grid)
    {
        var neighbours = new List<Grids>();
        if (grid == null) return neighbours;

        for (int i = 0; i < 4; i++)
        {
            var vector = grid.GridIndex + offset[i];
            if (vector.X < 0 || vector.X >= searchRange.X ||
                vector.Y < 0 || vector.Y >= searchRange.Y) continue;
            if (map[vector.X, vector.Y] == null) continue;
            neighbours.Add(map[vector.X, vector.Y]);
        }
        return neighbours;
    }

    public List<Grids> GetNeighbours(Grids grid, bool walk = false)
    {
        List<Grids> neighbours = new List<Grids>();
        for (int i = 0; i < 4; i++)
        {
            Vector2I vector = grid.GridIndex + offset[i];
            if (vector.X < 0 || vector.X >= searchRange.X
                || vector.Y < 0 || vector.Y >= searchRange.Y) continue;
            if (map[vector.X, vector.Y] == null) continue;

            var neighbor = map[vector.X, vector.Y];

            // ✅ N屏隔离：邻格与起点不同屏则不连通（跨屏只能走传送）
            if (neighbor.screenId != grid.screenId) continue;

            // ✅ 移除METEORITE硬编码：所有地形通行性由GetMoveCost统一控制
            // SpaceShiper等移动方式可以通过METEORITE
            neighbours.Add(neighbor);
        }
        return neighbours;
    }

    public  List<Grids> moveRange = new List<Grids>() ;
    public List<Grids> repairRange = new List<Grids>(); // ✅ 修复范围缓存

    private Infantry pendingEncounterBlocker;   // ✅ 截断遭遇：路上第一个可见阻挡者

    public void RemoveUnit(Infantry unit)
    {
        if (unit == null) return;
        if (unit.grid != null)
        {
            unit.grid.infantry = null;
            unit.grid = null;
            var gameManager = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            gameManager?.RemovePiece(unit);
        }
        unit.QueueFree();
    }

    public void SetGrid(Infantry infantry)
    {
        int xindex = (int)(infantry.Position.X - startPos.X) / gridSize.X;
        int yindex = (int)(infantry.Position.Y - startPos.Y) / gridSize.Y;
        if((xindex >= 0) && (xindex < searchRange.X) &&
            (yindex >= 0) && (yindex < searchRange.Y))
        {
            if(map[xindex, yindex] != null)
            {
                if (map[xindex, yindex].infantries.Count > 0 && 
                    infantry.overlapType == UnitOverlapType.NonOverlapping)
                    return;

                infantry.grid = map[xindex, yindex];
                map[xindex, yindex].infantries.Add(infantry);
            }
        }
    }

    public List<Facility> GetAllCities()
    {
        var cities = new List<Facility>();
        foreach (var grid in grids)
        {
            if (grid != null && grid.city != null)
            {
                cities.Add(grid.city);
            }
        }
        return cities;
    }

    public void PerformTurnSupply()
    {
        var cities = GetAllCities();
        foreach (var city in cities)
            city.PerformSupply();
    }

    public List<Grids> attackRange = new List<Grids>();
    public List<Grids> explosionRange = new List<Grids>(); // ✅ 爆炸范围
    public List<Grids> flareRange = new List<Grids>(); // ✅ 照明弹射程范围（绿色）
    public List<Grids> illuminationRange = new List<Grids>(); // ✅ 照明覆盖范围（黄色）
}
