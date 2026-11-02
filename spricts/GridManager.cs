// GridManager.cs - 修改 HandleGridClick 和 SetupMoveRangeCallbacks 方法
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

public partial class GridManager : Node
{
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
    [Export] public int[] screenHeights = new int[0];  // ✅ 每屏高(行数)，空数组=全局高

    public int GetScreenStartX(int screen)
    {
        int w = map != null ? map.GetLength(0) : 0;
        if (screenWidths == null || screenWidths.Length != screenCount || screenCount <= 1)
        {
            int per = screenCount > 0 ? w / screenCount : w; if (per <= 0) per = 1;
            return per * screen;
        }
        int sum = 0;
        for (int i = 0; i < screen && i < screenWidths.Length; i++) sum += Math.Max(1, screenWidths[i]);
        return sum;
    }

    public int GetScreenId(Vector2I pos)
    {
        // ✅ 每屏独立高：单屏也生效
        if (screenCount <= 1)
        {
            if (screenHeights != null && screenHeights.Length == 1 && pos.Y >= Math.Max(1, screenHeights[0])) return -1;
            return 0;
        }
        int sid;
        if (screenWidths != null && screenWidths.Length == screenCount)
        {
            int acc = 0; sid = screenCount - 1;
            for (int s = 0; s < screenCount; s++)
            {
                acc += Math.Max(1, screenWidths[s]);
                if (pos.X < acc) { sid = s; break; }
            }
        }
        else
        {
            int w2 = map.GetLength(0);
            int per2 = w2 / screenCount; if (per2 <= 0) per2 = 1;
            sid = pos.X / per2; if (sid >= screenCount) sid = screenCount - 1;
        }
        // ✅ 超出该屏高度=不属于任何屏(-1)：地图生成时填海+禁行
        if (screenHeights != null && screenHeights.Length == screenCount && pos.Y >= Math.Max(1, screenHeights[sid])) return -1;
        return sid;
    }

    public int GetScreenCount() => screenCount <= 0 ? 1 : screenCount;

    // ✅ 重新计算所有格子的屏归属（改屏数后调用）
    private readonly System.Collections.Generic.List<Control> _screenDividers = new();

    public void RefreshScreenIds()
    {
        if (map == null) return;
        foreach (var g in grids)
            if (g != null) g.screenId = GetScreenId(g.GridIndex);
        RedrawScreenDividers();   // ✅ 多屏视觉鸿沟
    }

 // ✅ N屏视觉隔离带：屏间半透明鸿沟+屏号标签（纯视觉：不挡输入、不改寻路，逻辑隔离由 IsSameScreen 保证）
 private void RedrawScreenDividers()
 {
     foreach (var d in _screenDividers) if (IsInstanceValid(d)) d.QueueFree();
     _screenDividers.Clear();
     if (screenCount <= 1 || map == null) return;
     int w = map.GetLength(0), h = map.GetLength(1);
     float totalH = h * gridSize.Y;
     for (int i = 1; i < screenCount; i++)
     {
         float x = startPos.X + GetScreenStartX(i) * gridSize.X;
         var band = new ColorRect
         {
             Position = new Vector2(x - 12, startPos.Y),
             Size = new Vector2(24, totalH),
             Color = new Color(0f, 0f, 0f, 0.65f),
             MouseFilter = Control.MouseFilterEnum.Ignore,
             ZIndex = 500,
         };
         band.AddChild(new Label
         {
             Text = $"屏{i} ⇄ 屏{i + 1}",
             Position = new Vector2(-28, 8),
             Modulate = new Color(1, 1, 1, 0.85f),
         });
         AddChild(band);
         _screenDividers.Add(band);
     }
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
