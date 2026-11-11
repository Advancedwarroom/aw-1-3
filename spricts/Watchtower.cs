using Godot;
using System.Collections.Generic;

// ✅ 瞭望塔设施（开创人手绘图，蓝母版全阵营色相迁移烘焙）
// 视野：环带 n-m / 锥形 / 射线 三类独立开关、各自计算、叠加并集（开创人裁决 2026-11-09）
// 锥/射算法与辅助函数完整搬运自 Grids.cs（旧格子瞭望塔系统），实现一字未改，仅字段名对到本设施 Export；
// CO 加成在调用点临时注入字段、调用后即刻还原，不污染 Export 值。
public partial class Watchtower : Facility
{
    [ExportGroup("瞭望塔视野 · 三类独立叠加")]
    [Export] public bool useRingVision = false;   // ① 环带 n-m
    [Export] public int visionRangeMin = 1;       // 环带内圈（1 = 全圆盘）
    [Export] public int visionRangeMax = 3;       // 环带外圈
    [Export] public bool useConeVision = false;   // ② 锥形
    [Export] public int coneRange = 5;
    [Export] public TowerDirection coneDirection = TowerDirection.Up;
    [Export] public bool coneIgnoreTerrain = false;
    [Export] public bool useRayVision = false;    // ③ 射线
    [Export] public int rayRange = 8;
    [Export] public float rayAngle = 0f;          // 角度制
    [Export] public TowerDirection rayDirection = TowerDirection.Up;
    [Export] public bool rayIgnoreTerrain = false;

    protected override (string neutral, string red, string blue)? GetTeamAnimNames()
    {
        string team = TeamHelper.CanonicalId(facilityTeam);
        bool known = team == TeamHelper.Player0 || team == TeamHelper.Player1 || team == TeamHelper.Player2;
        return ("P0Watchtower", known ? "watchtowerred" : "watchtowerpurple", "watchtowerblue");
    }

    // ✅ 按阵营颜色取图：八色烘焙（开创人定板 2026-11-09），未知队伍按注册色就近匹配、恒回退
    protected override string PickArtForTeam(string neutral, string red, string blue)
        => TeamHelper.PickArtByFactionColor(facilityTeam, neutral, red, blue,
            purple: "watchtowerpurple", green: "watchtowergreen", yellow: "watchtoweryellow",
            black: "watchtowerblack", pink: "watchtowerpink");

    public override HashSet<Grids> CalculateFacilityVision(GameManager gm)
    {
        var visible = base.CalculateFacilityVision(gm);
        if (gm?.gridManager == null || ParentGrid == null) return visible;

        // CO 视野加成：与单位同一模块管线（stat 4 = visionBonus），三类射程都吃
        int coBonus = Mathf.RoundToInt(CoModuleSystem.Sum(facilityTeam, 4, "visionBonus"));

        // ① 环带 n-m（CO 只撑外圈）
        if (useRingVision)
        {
            int rOut = Mathf.Max(visionRangeMax + coBonus, visionRangeMin);
            int rIn = Mathf.Max(0, visionRangeMin - 1);
            var c = ParentGrid.GridIndex;
            foreach (var g in gm.gridManager.grids)
            {
                if (g == null || !IsInstanceValid(g)) continue;
                int dist = Mathf.Abs(g.GridIndex.X - c.X) + Mathf.Abs(g.GridIndex.Y - c.Y);
                if (dist > rIn && dist <= rOut) visible.Add(g);
            }
        }

        // ② 锥形（CO 加成注入-还原；算法本体见下方搬运区，一字未改）
        if (useConeVision)
        {
            coneRange += coBonus;
            visible.UnionWith(CalculateTowerConeVision(gm));
            coneRange -= coBonus;
        }

        // ③ 射线（同上）
        if (useRayVision)
        {
            rayRange += coBonus;
            visible.UnionWith(CalculateTowerRayVision(gm));
            rayRange -= coBonus;
        }

        visible.RemoveWhere(g => !gm.gridManager.IsRangeAllowed(ParentGrid, g, facilityVisionCanCrossScreens));
        return visible;
    }

    // ==================== 以下完整搬运自 Grids.cs（旧格子瞭望塔系统），实现一字未改 ====================

    private HashSet<Grids> CalculateTowerConeVision(GameManager gm)
    {
        var visible = new HashSet<Grids>();
        var dir = coneDirection switch
        {
            TowerDirection.Up => new Vector2I(0, -1), TowerDirection.Right => new Vector2I(1, 0),
            TowerDirection.Down => new Vector2I(0, 1), TowerDirection.Left => new Vector2I(-1, 0),
            _ => new Vector2I(0, -1)
        };
        var perp = new Vector2I(dir.Y, -dir.X);

        for (int depth = 1; depth <= coneRange; depth++)
        {
            Vector2I center = ParentGrid.GridIndex + dir * depth;
            int halfWidth = depth;
            for (int w = -halfWidth; w <= halfWidth; w++)
            {
                var pos = center + perp * w;
                if (IsValidGrid(pos, gm))
                {
                    var g = gm.gridManager.map[pos.X, pos.Y];
                    if (g != null)
                    {
                        visible.Add(g);
                        if (!coneIgnoreTerrain && IsVisionBlockingTerrain(g.gridType)) break;
                    }
                }
            }
        }
        return visible;
    }

    private HashSet<Grids> CalculateTowerRayVision(GameManager gm)
    {
        var visible = new HashSet<Grids>();
        if (gm?.gridManager == null) return visible;

        float angleRad = Mathf.DegToRad(rayAngle);
        Godot.Vector2 dir = new(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
        float cellSize = gm.gridManager.gridSize.X;
        Godot.Vector2 startPos = Position + new Godot.Vector2(cellSize * 0.5f, cellSize * 0.5f);

        Godot.Vector2 currentPos = startPos;
        int currentX = ParentGrid.GridIndex.X, currentY = ParentGrid.GridIndex.Y;
        int stepX = dir.X > 0 ? 1 : (dir.X < 0 ? -1 : 0);
        int stepY = dir.Y > 0 ? 1 : (dir.Y < 0 ? -1 : 0);
        int steps = 0;
        int maxSteps = rayRange * 2;
        HashSet<Grids> visited = new();

        for (int step = 0; step < maxSteps && steps < rayRange; step++)
        {
            float x0 = gm.gridManager.startPos.X + currentX * cellSize;
            float x1 = x0 + cellSize;
            float y0 = gm.gridManager.startPos.Y + currentY * cellSize;
            float y1 = y0 + cellSize;

            float tX = float.MaxValue, tY = float.MaxValue;
            if (stepX > 0) tX = (x1 - currentPos.X) / dir.X;
            else if (stepX < 0) tX = (x0 - currentPos.X) / dir.X;
            if (stepY > 0) tY = (y1 - currentPos.Y) / dir.Y;
            else if (stepY < 0) tY = (y0 - currentPos.Y) / dir.Y;

            float tStep = Mathf.Min(tX, tY);
            if (tStep < 0.0001f) tStep = 0.0001f;

            var targetGrid = gm.gridManager.map[currentX, currentY];
            if (targetGrid != null && !visited.Contains(targetGrid) && targetGrid != ParentGrid)
            {
                visited.Add(targetGrid);
                visible.Add(targetGrid);
                steps++;
                if (!rayIgnoreTerrain && IsVisionBlockingTerrain(targetGrid.gridType)) break;
            }
        }
        return visible;
    }

    private bool IsValidGrid(Vector2I pos, GameManager gm)
    {
        return pos.X >= 0 && pos.X < gm.gridManager.searchRange.X &&
               pos.Y >= 0 && pos.Y < gm.gridManager.searchRange.Y;
    }

    private bool IsVisionBlockingTerrain(GridType type)
    {
        return type == GridType.METEORITE || type == GridType.PIPE || type == GridType.CLIFF;
    }
}
