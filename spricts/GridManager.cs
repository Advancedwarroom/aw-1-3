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
