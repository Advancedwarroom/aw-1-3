// Infantry.MultiTile.cs - 多格机动单位（issue#1 扩展·一期：占据与绑定）
// 锚点格走 infantries 主通道（BindUnitToGrid 原有逻辑），侧格走 Grids.multiTileOccupant 独占引用通道——
// 不进 infantries，避免 AoE/碾压等"逐格遍历"逻辑对同一单位重复结算。
// 消费者一律校验 IsInstanceValid && !IsQueuedForDeletion（单位死亡未重绑时引用可能残留，惰性失效）。
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class Infantry
{
    [ExportGroup("多格机动单位（issue#1·一期）")]
    [Export] public bool isMultiTileUnit = false;                 // 多格占据（默认关=零行为变化）
    [Export] public Vector2I unitFootprintSize = new Vector2I(2, 2);
    [Export] public string[] unitFootprintMask = new string[0];
    [Export] public bool crushOccupantsOnMove = false;   // 二期：移动落脚碾碎足印侧格上的敌方单格单位（默认关）   // 空=全矩形；否则每行 size.X 字符，'#'占据 '.'空

    public List<Grids> footprintGrids = new();                    // 运行时足印（含锚点格）

    /// <summary>无旋转的基础足印偏移（x外y内，与旧版 GetFootprintIndices 同序；序号=attackAnchorCells 勾选序）</summary>
    private List<Vector2I> GetBaseFootprintOffsets()
    {
        var result = new List<Vector2I>();
        if (unitFootprintMask == null || unitFootprintMask.Length == 0)
        {
            for (int x = 0; x < unitFootprintSize.X; x++)
                for (int y = 0; y < unitFootprintSize.Y; y++)
                    result.Add(new Vector2I(x, y));
            return result;
        }
        for (int y = 0; y < unitFootprintMask.Length && y < unitFootprintSize.Y; y++)
        {
            string row = unitFootprintMask[y] ?? "";
            for (int x = 0; x < row.Length && x < unitFootprintSize.X; x++)
                if (row[x] == '#') result.Add(new Vector2I(x, y));
        }
        return result;
    }

    /// <summary>足印索引（指定朝向 facing；0=按作者摆向，每+1=绕锚点顺时针90°，奇数步宽高互换）。锚点恒为包围盒左上角。</summary>
    public List<Vector2I> GetFootprintIndices(Vector2I anchorIndex, int facing)
    {
        var offsets = GetBaseFootprintOffsets();
        int f = ((facing % 4) + 4) % 4;
        int w = unitFootprintSize.X, h = unitFootprintSize.Y;
        var result = new List<Vector2I>(offsets.Count);
        foreach (var o in offsets)
        {
            Vector2I r = o;
            if (f == 1) r = new Vector2I(h - 1 - o.Y, o.X);
            else if (f == 2) r = new Vector2I(w - 1 - o.X, h - 1 - o.Y);
            else if (f == 3) r = new Vector2I(o.Y, w - 1 - o.X);
            result.Add(anchorIndex + r);
        }
        return result;
    }

    /// <summary>足印索引（按当前 unitFacing）</summary>
    public List<Vector2I> GetFootprintIndices(Vector2I anchorIndex) => GetFootprintIndices(anchorIndex, unitFacing);

    public bool IsFootprintGrid(Grids g)
        => g != null && footprintGrids != null && footprintGrids.Contains(g);

    /// <summary>预检：以 anchorIndex 为锚的足印能否落下（越界/真空/他方足印冲突→false），不改任何状态</summary>
    public bool CheckFootprintFit(Vector2I anchorIndex, GameManager gm, int facing)
    {
        if (!isMultiTileUnit) return true;
        if (gm?.gridManager?.map == null) return false;
        var sr = gm.gridManager.searchRange;
        foreach (var idx in GetFootprintIndices(anchorIndex, facing))
        {
            if (idx.X < 0 || idx.Y < 0 || idx.X >= sr.X || idx.Y >= sr.Y) return false;
            var g = gm.gridManager.map[idx.X, idx.Y];
            if (g == null) return false;
            if (g.multiTileOccupant != null && g.multiTileOccupant != this
                && IsInstanceValid(g.multiTileOccupant) && !g.multiTileOccupant.IsQueuedForDeletion())
                return false;
        }
        return true;
    }

    /// <summary>整足印落点校验（按当前 unitFacing）</summary>
    public bool CheckFootprintFit(Vector2I anchorIndex, GameManager gm) => CheckFootprintFit(anchorIndex, gm, unitFacing);

    /// <summary>整足印通行校验（寻路用，不改状态）：fit + 每格进入消耗合法</summary>
    public bool CheckFootprintPassable(Vector2I anchorIndex, GameManager gm, int facing)
    {
        if (!isMultiTileUnit) return true;
        if (!CheckFootprintFit(anchorIndex, gm, facing)) return false;
        if (gm?.gridManager?.map == null) return false;
        foreach (var idx in GetFootprintIndices(anchorIndex, facing))
        {
            var g = gm.gridManager.map[idx.X, idx.Y];
            if (g == null) return false;
            if (GetMoveCostOnGrid(g) >= 999) return false;
        }
        return true;
    }

    /// <summary>侧格占用裁定：无活单位→true；敌方→crushOccupantsOnMove 碾碎否则 false；友方→false</summary>
    public bool ResolveFootprintSideCell(Grids g, GameManager gm)
    {
        var units = g.infantries.Where(u => u != null && u != this && IsInstanceValid(u) && u.health > 0).ToList();
        if (units.Count == 0) return true;
        if (units.Any(u => u.team == team)) return false;
        if (!crushOccupantsOnMove) return false;
        foreach (var u in units)
        {
            u.health = 0;
            u.UpdateHpLabel();
            u.TryFlee();
            if (u.health <= 0) gm?.RemovePiece(u);
        }
        return true;
    }

    /// <summary>射程/视野/自爆锚点格：attackAnchorCells 勾选1~n（留空=全部足印格）；单格单位=自身格</summary>
    public List<Grids> GetAnchorCells()
    {
        var cells = new List<Grids>();
        if (!isMultiTileUnit || footprintGrids == null || footprintGrids.Count == 0)
        {
            if (grid != null) cells.Add(grid);
            return cells;
        }
        if (attackAnchorCells == null || attackAnchorCells.Length == 0)
        {
            foreach (var g in footprintGrids) if (g != null && !cells.Contains(g)) cells.Add(g);
            return cells;
        }
        foreach (var n in attackAnchorCells)
        {
            if (n < 1 || n > footprintGrids.Count) continue;
            var g = footprintGrids[n - 1];
            if (g != null && !cells.Contains(g)) cells.Add(g);
        }
        return cells;
    }

    // ==================== 朝向与旋转（issue#1·二期 2026-11-15：对齐兵器 canRotate 体系） ====================
    /// <summary>是否可旋转（对齐兵器 canRotate 语义；非多格单位无意义）</summary>
    [Export] public bool canRotate = true;
    /// <summary>当前朝向：0=N(按作者摆向) 1=E 2=S 3=W</summary>
    [Export] public int unitFacing = 0;
    /// <summary>射程/视野/自爆锚点勾选：足印格序号1~n（留空=全部格；前沿格=朝向最前一格对应序号）</summary>
    [Export] public int[] attackAnchorCells = new int[0];
    /// <summary>严格检查非法地形：任一脚非法即算非法（默认关=只看锚点格）</summary>
    [Export] public bool illegalTerrainStrictMultiCell = false;
    /// <summary>剩余n格安全阈值：严格模式下合法格≥n则安全不自毁（0=关闭=任一非法即自毁）</summary>
    [Export] public int illegalTerrainMinSafeCells = 0;
    /// <summary>转身时贴图随朝向转90°（像素画糊可关，自换横竖双版贴图）</summary>
    [Export] public bool rotateSpriteWithFacing = true;

    /// <summary>落点朝向回退：优先当前朝向，依次试 0~3，返回第一个整足印落得的朝向；-1=都落不下（卸载/推动用）</summary>
    public int FindFittingFacing(Vector2I anchorIndex, GameManager gm)
    {
        if (!isMultiTileUnit) return 0;
        if (CheckFootprintFit(anchorIndex, gm, unitFacing)) return unitFacing;
        for (int f = 0; f < 4; f++)
            if (f != unitFacing && CheckFootprintFit(anchorIndex, gm, f)) return f;
        return -1;
    }

    /// <summary>目标格到任一锚点格的最小曼哈顿距离（n-m远程资格判定用；-1=无锚点）</summary>
    public int MinAnchorDistance(Vector2I targetIdx)
    {
        int best = int.MaxValue;
        foreach (var a in GetAnchorCells())
        {
            if (a == null) continue;
            int d = Mathf.Abs(targetIdx.X - a.GridIndex.X) + Mathf.Abs(targetIdx.Y - a.GridIndex.Y);
            if (d < best) best = d;
        }
        return best == int.MaxValue ? -1 : best;
    }

    /// <summary>n-m 远程圈（假设锚点=anchorIdx/朝向=facing）：各足印锚点环带取并集，盲区=距任一锚点不足minR。AI假想位评估用。</summary>
    public List<Grids> AnchorRingFrom(GameManager gm, Vector2I anchorIdx, int facing, int minR, int maxR, bool canCross)
    {
        var result = new List<Grids>();
        if (gm?.gridManager?.map == null || maxR < 0) return result;
        var anchors = new List<Grids>();
        if (isMultiTileUnit)
        {
            var sr = gm.gridManager.searchRange;
            foreach (var idx in GetFootprintIndices(anchorIdx, facing))
            {
                if (idx.X < 0 || idx.Y < 0 || idx.X >= sr.X || idx.Y >= sr.Y) continue;
                var g = gm.gridManager.map[idx.X, idx.Y];
                if (g != null) anchors.Add(g);
            }
        }
        else
        {
            if (anchorIdx.X >= 0 && anchorIdx.Y >= 0 && anchorIdx.X < gm.gridManager.searchRange.X && anchorIdx.Y < gm.gridManager.searchRange.Y)
            {
                var g0 = gm.gridManager.map[anchorIdx.X, anchorIdx.Y];
                if (g0 != null) anchors.Add(g0);
            }
        }
        if (anchors.Count == 0) return result;
        var blind = new HashSet<Grids>();
        var seen = new HashSet<Grids>();
        foreach (var a in anchors)
        {
            foreach (var g in gm.gridManager.FindRange(a, maxR, false, canCross))
            {
                if (g == null || g == a || !seen.Add(g)) continue;
                int d = Mathf.Abs(g.GridIndex.X - a.GridIndex.X) + Mathf.Abs(g.GridIndex.Y - a.GridIndex.Y);
                if (d >= minR) result.Add(g);
            }
            if (minR > 0)
                foreach (var g in gm.gridManager.FindRange(a, minR - 1, false, canCross))
                    if (g != null) blind.Add(g);
        }
        if (blind.Count > 0) result.RemoveAll(blind.Contains);
        return result;
    }

    /// <summary>n-m 远程圈（当前位置锚点）：AnchorRingFrom(grid.GridIndex, unitFacing) 简写</summary>
    public List<Grids> AnchorRing(GameManager gm, int minR, int maxR, bool canCross)
    {
        if (grid == null) return new List<Grids>();
        return AnchorRingFrom(gm, grid.GridIndex, unitFacing, minR, maxR, canCross);
    }

    /// <summary>可旋转检查：四个朝向里是否有非当前朝向能落脚</summary>
    public bool CanAnyRotationFit(GameManager gm)
    {
        if (!isMultiTileUnit || !canRotate || grid == null) return false;
        for (int f = 0; f < 4; f++)
            if (f != unitFacing && CheckFootprintFit(grid.GridIndex, gm, f)) return true;
        return false;
    }

    /// <summary>✅ 转身：整足印顺时针90°×steps。窄口/侧格被占则拒转（AW"转不过身"）。true=已转身</summary>
    public bool TryRotate(int steps, GameManager gm)
    {
        if (!isMultiTileUnit || !canRotate || steps == 0) return false;
        if (grid == null || gm?.gridManager?.map == null) return false;
        int newFacing = ((unitFacing + steps) % 4 + 4) % 4;
        var anchor = grid.GridIndex;
        if (!CheckFootprintFit(anchor, gm, newFacing)) return false;

        var oldCells = new HashSet<Grids>(footprintGrids ?? new List<Grids>());
        foreach (var idx in GetFootprintIndices(anchor, newFacing))
        {
            var g = gm.gridManager.map[idx.X, idx.Y];
            if (g == null || g == grid || oldCells.Contains(g)) continue;
            if (!ResolveFootprintSideCell(g, gm)) return false;
        }

        unitFacing = newFacing;
        OccupyFootprint(gm, newFacing);
        ApplyFacingVisual();
        return true;
    }

    /// <summary>转身入口（ActionMenu 按钮调用，对齐兵器 RotateDirection 按钮语义）</summary>
    public void RotateDirection()
    {
        if (!isMultiTileUnit || !canRotate) return;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        TryRotate(1, gm);
    }

    /// <summary>朝向视觉：贴图随朝向转90°（rotateSpriteWithFacing 关闭时留给横竖双版贴图自处理）</summary>
    public void ApplyFacingVisual()
    {
        if (!rotateSpriteWithFacing) return;
        if (animSprite != null && IsInstanceValid(animSprite)) animSprite.RotationDegrees = unitFacing * 90f;
    }

    /// <summary>占据足印：清旧占新（锚点=unit.grid）。返回 false=冲突（调用方应拒绝本次绑定）。</summary>
    public bool OccupyFootprint(GameManager gm) => OccupyFootprint(gm, unitFacing);

    /// <summary>占据足印（指定朝向 facing；清旧占新，返回是否成功）</summary>
    public bool OccupyFootprint(GameManager gm, int facing)
    {
        ClearFootprint();
        if (!isMultiTileUnit || grid == null) return true;
        if (gm?.gridManager?.map == null) return false;
        var anchor = grid.GridIndex;
        if (!CheckFootprintFit(anchor, gm, facing)) return false;
        foreach (var idx in GetFootprintIndices(anchor, facing))
        {
            var g = gm.gridManager.map[idx.X, idx.Y];
            if (g == null) continue;
            if (!footprintGrids.Contains(g)) footprintGrids.Add(g);
            g.multiTileOccupant = this;
        }
        return true;
    }

    /// <summary>清足印（重绑/卸载前调用；死亡残留由消费者惰性校验兜住）</summary>
    public void ClearFootprint()
    {
        if (footprintGrids == null) return;
        foreach (var g in footprintGrids)
            if (g != null && g.multiTileOccupant == this)
                g.multiTileOccupant = null;
        footprintGrids.Clear();
    }

    // ========== 行走朝向（FlipH + 足印宽度补偿；贴图默认朝右绘制）==========
    private float animSpriteBaseX = float.NaN;
    private float baseAnimSpriteBaseX = float.NaN;

    /// <summary>捕获贴图基准X（首次绑定时调用；行走 FlipH 偏移补偿用）</summary>
    public void CaptureSpriteBaseX()
    {
        if (animSprite != null && IsInstanceValid(animSprite) && float.IsNaN(animSpriteBaseX))
            animSpriteBaseX = animSprite.Position.X;
        if (baseAnimSprite != null && IsInstanceValid(baseAnimSprite) && float.IsNaN(baseAnimSpriteBaseX))
            baseAnimSpriteBaseX = baseAnimSprite.Position.X;
    }

    /// <summary>FlipH 偏移补偿：镜像时把贴图右移（足印包围盒宽-1）格，翻转后的身体仍盖在足印上</summary>
    public void ApplyFacingFlipOffset(bool faceLeft, float cellWidth)
    {
        if (!isMultiTileUnit) return;
        float offsetX = Mathf.Max(0, unitFootprintSize.X - 1) * cellWidth;
        if (!float.IsNaN(animSpriteBaseX) && animSprite != null && IsInstanceValid(animSprite))
            animSprite.Position = new Vector2(animSpriteBaseX + (faceLeft ? offsetX : 0f), animSprite.Position.Y);
        if (!float.IsNaN(baseAnimSpriteBaseX) && baseAnimSprite != null && IsInstanceValid(baseAnimSprite))
            baseAnimSprite.Position = new Vector2(baseAnimSpriteBaseX + (faceLeft ? offsetX : 0f), baseAnimSprite.Position.Y);
    }
}
