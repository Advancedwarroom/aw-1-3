using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · Flee（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{
    [ExportGroup("逃跑模块")]
    [Export] public bool canFleeOnDeath = false;   // 逃跑开关：致命伤触发
    [Export] public int fleeRangeMin = 0;          // 逃跑目标格检查范围 x
    [Export] public int fleeRangeMax = 2;          // 逃跑目标格检查范围 y
    [Export] public bool fleeCanCrossScreens = false; // 致命逃跑目标范围是否允许跨屏
    
    // ========== 逃跑模块（野化）==========
    /// <summary>
    /// 致命伤触发：向 x-y 环内随机空格逃跑并野化（满血中立）。
    /// 成功返回 true（死亡被逃跑抵消）；无处可逃返回 false（照常死亡）。
    /// 规格裁定：永久存续 / 原主人可攻击 / 落地满血（致命伤被逃跑抵消，代价是脱离编制而非血量）
    /// 一次性：逃跑成功后自动关闭 canFleeOnDeath，野化单位不会二次野化
    /// </summary>
    public virtual bool TryFlee()
    {
        // 尸化优先于野化：旧单位死亡并在原格生成新兵种；生成失败才继续尝试野化/正常死亡。
        if (deathTransformEnabled && TryDeathTransform()) return true;
        if (!canFleeOnDeath || grid == null) return false;
        var gm = gameManager;
        if (gm?.unitManager == null || gm?.gridManager == null) return false;

        var origin = grid.GridIndex;
        var candidates = new List<Grids>();
        for (int dx = -fleeRangeMax; dx <= fleeRangeMax; dx++)
            for (int dy = -fleeRangeMax; dy <= fleeRangeMax; dy++)
            {
                int dist = Mathf.Abs(dx) + Mathf.Abs(dy);
                if (dist < fleeRangeMin || dist > fleeRangeMax || (dx == 0 && dy == 0)) continue;
                var pos = new Vector2I(origin.X + dx, origin.Y + dy);
                if (!gm.unitManager.IsValidGrid(pos)) continue;
                var g = gm.gridManager.map[pos.X, pos.Y];
                if (g == null || !gm.gridManager.IsRangeAllowed(grid, g, fleeCanCrossScreens) || g.infantries.Count > 0) continue;      // 有单位的不选
                if (GetMoveCostOnGrid(g) >= 999) continue;           // 本兵种不可通行的地形不选（水面/陨石）
                candidates.Add(g);
            }
        if (candidates.Count == 0) return false;                        // 无处可逃：原地阵亡

        var target = candidates[GD.RandRange(0, candidates.Count - 1)]; // 规格无顺序要求 → 随机

        // 搬格子（照运输卸载的既有模式）
        grid.infantries.Remove(this);
        if (grid.infantry == this)
            grid.infantry = grid.infantries.Count > 0 ? grid.infantries[0] : null;
        Position = target.Position;
        grid = target;
        if (!target.infantries.Contains(this)) target.infantries.Add(this);
        if (target.infantry == null) target.infantry = this;

        canFleeOnDeath = false;                // 只能野化一次：已逃过的单位再受致命伤照常阵亡（随 Export 字段一起存档）
        health = maxHealth;                    // 野化=满血
        team = TeamHelper.Player0;             // 中立（P0）：无相位、不动不操作、挨打照反击（反击链队伍无关）
        UpdateHpLabel();
        UpdateTeamVisual();                    // 换中立灰（含呼吸动画重启）
        GD.Print($"[逃跑] {unitDisplayName} 野化：{origin} → {target.GridIndex}，满血中立在野");
        return true;
    }
}
