// Infantry.MultiTile.cs - 多格机动单位（issue#1 扩展·一期：占据与绑定）
// 锚点格走 infantries 主通道（BindUnitToGrid 原有逻辑），侧格走 Grids.multiTileOccupant 独占引用通道——
// 不进 infantries，避免 AoE/碾压等"逐格遍历"逻辑对同一单位重复结算。
// 消费者一律校验 IsInstanceValid && !IsQueuedForDeletion（单位死亡未重绑时引用可能残留，惰性失效）。
using Godot;
using System.Collections.Generic;

public partial class Infantry
{
    [ExportGroup("多格机动单位（issue#1·一期）")]
    [Export] public bool isMultiTileUnit = false;                 // 多格占据（默认关=零行为变化）
    [Export] public Vector2I unitFootprintSize = new Vector2I(2, 2);
    [Export] public string[] unitFootprintMask = new string[0];
    [Export] public bool crushOccupantsOnMove = false;   // 二期：移动落脚碾碎足印侧格上的敌方单格单位（默认关）   // 空=全矩形；否则每行 size.X 字符，'#'占据 '.'空

    public List<Grids> footprintGrids = new();                    // 运行时足印（含锚点格）

    /// <summary>足印索引：掩码优先（同 Weapon 规范），无掩码=全矩形</summary>
    public List<Vector2I> GetFootprintIndices(Vector2I anchorIndex)
    {
        var result = new List<Vector2I>();
        if (unitFootprintMask == null || unitFootprintMask.Length == 0)
        {
            for (int x = 0; x < unitFootprintSize.X; x++)
                for (int y = 0; y < unitFootprintSize.Y; y++)
                    result.Add(anchorIndex + new Vector2I(x, y));
            return result;
        }
        for (int y = 0; y < unitFootprintMask.Length && y < unitFootprintSize.Y; y++)
        {
            string row = unitFootprintMask[y] ?? "";
            for (int x = 0; x < row.Length && x < unitFootprintSize.X; x++)
                if (row[x] == '#')
                    result.Add(anchorIndex + new Vector2I(x, y));
        }
        return result;
    }

    public bool IsFootprintGrid(Grids g)
        => g != null && footprintGrids != null && footprintGrids.Contains(g);

    /// <summary>预检：以 anchorIndex 为锚的足印能否落下（越界/真空/他方足印冲突→false），不改任何状态</summary>
    public bool CheckFootprintFit(Vector2I anchorIndex, GameManager gm)
    {
        if (!isMultiTileUnit) return true;
        if (gm?.gridManager?.map == null) return false;
        var sr = gm.gridManager.searchRange;
        foreach (var idx in GetFootprintIndices(anchorIndex))
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

    /// <summary>占据足印：清旧占新（锚点=unit.grid）。返回 false=冲突（调用方应拒绝本次绑定）。</summary>
    public bool OccupyFootprint(GameManager gm)
    {
        ClearFootprint();
        if (!isMultiTileUnit || grid == null) return true;
        if (gm?.gridManager?.map == null) return false;
        var anchor = grid.GridIndex;
        if (!CheckFootprintFit(anchor, gm)) return false;
        foreach (var idx in GetFootprintIndices(anchor))
        {
            var g = gm.gridManager.map[idx.X, idx.Y];
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
