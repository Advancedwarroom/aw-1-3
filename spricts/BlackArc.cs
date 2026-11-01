// BlackArc.cs - 黑色弧光/黑色弧形兵器（AW 原作 + 天基打击扩展）
// 原作：可被攻击扣血的兵器，拥有"天基打击"（轨道轰炸）能力
// 表现：点击兵器 → 弹出"天基打击"按钮 → 点击后进入十字准星瞄准模式 →
//       点击目标格子 → 以准心对准的目标格子为核心 x-y 范围内单位受扣血伤害
// 模块：天基打击（全 Export，可拼装）
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class BlackArc : Weapon
{
    // ========== 1. 是否开启天基打击 ==========
    [ExportGroup("天基打击模块")]
    [Export] public bool arcStrikeEnabled = true;             // 1.Export是否开启天基打击

    // ========== 2. 打击范围（以该兵器为核心）==========
    [Export] public int arcStrikeMinRange = 1;               // 2.最小射程（0=可打自身所在格）
    [Export] public int arcStrikeMaxRange = 10;             // 2.最大射程

    // ========== 3. 打击效果范围（以目标点为核心 x-y）==========
    [Export] public int arcEffectRangeX = 1;                 // 3.以目标格为核心 x 范围（±x）
    [Export] public int arcEffectRangeY = 1;                 // 3.以目标格为核心 y 范围（±y）

    // ========== 4. 打击伤害 ==========
    [Export] public int arcStrikeDamage = 50;                // 4.伤害（千分制，50=5HP）

    // ========== 5. 冷却 ==========
    [Export] public int arcStrikeCooldown = 1;               // 5.冷却回合数（0=每回合可用）
    private int arcStrikeCooldownCounter = 0;

    // ========== 6. 是否影响兵器 ==========
    [Export] public bool arcStrikeAffectsWeapons = true;    // 6.是否影响兵器（同原作）

    // ========== 7. 目标策略 ==========
    [Export] public int arcStrikeTargetPolicy = 1;          // 7.0=仅友军 1=仅敌军 2=全部

    // ========== 8. 血量（继承基类 maxHealth/health 千分制）==========
    // 可被攻击扣血（原作可被攻击）

    // 十字准星瞄准模式状态
    private bool isArcTargeting = false;

    /// <summary>冷却是否就绪</summary>
    public bool ArcStrikeReady => arcStrikeEnabled && arcStrikeCooldownCounter <= 0;

    /// <summary>进入十字准星瞄准模式（高亮打击范围 + 点击目标格触发）</summary>
    public void BeginArcTargeting(GameManager gm)
    {
        if (gm?.gridManager == null || !ArcStrikeReady) return;
        isArcTargeting = true;
        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = true;

        var rangeGrids = gm.gridManager.grids.Where(g =>
        {
            int d = Mathf.Abs(g.GridIndex.X - grid.GridIndex.X) + Mathf.Abs(g.GridIndex.Y - grid.GridIndex.Y);
            return d >= arcStrikeMinRange && d <= arcStrikeMaxRange;
        }).ToList();

        foreach (var g in rangeGrids)
        {
            g.attackRangeIcon?.Show();
            if (g.attackRangeIcon != null)
                g.attackRangeIcon.Modulate = new Color(0.9f, 0.2f, 0.2f, 0.85f); // 红色十字准星
            g.IsInWeaponRange = true;
            g.OnClickGrid = (target) =>
            {
                if (!IsInstanceValid(this)) return;
                CancelArcTargeting(gm);
                PerformArcStrike(target, gm);
            };
            gm.gridManager.OverrideUnitInput(g, true);
        }
        foreach (var g in gm.gridManager.grids)
        {
            if (rangeGrids.Contains(g)) continue;
            g.OnClickEmpty = () =>
            {
                if (!IsInstanceValid(this)) return;
                CancelArcTargeting(gm);
            };
        }
    }

    /// <summary>取消瞄准模式</summary>
    public void CancelArcTargeting(GameManager gm)
    {
        isArcTargeting = false;
        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = false;
        foreach (var g in gm.gridManager.grids)
        {
            gm.gridManager.OverrideUnitInput(g, false);
            g.OnClickGrid = null;
            g.OnClickEmpty = null;
        }
    }

    /// <summary>执行天基打击：以目标格为核心 x-y 范围内单位受扣血伤害</summary>
    public void PerformArcStrike(Grids targetGrid, GameManager gm)
    {
        if (targetGrid == null || gm == null) return;
        if (!ArcStrikeReady) return;

        int cx = targetGrid.GridIndex.X;
        int cy = targetGrid.GridIndex.Y;

        for (int dx = -arcEffectRangeX; dx <= arcEffectRangeX; dx++)
        {
            for (int dy = -arcEffectRangeY; dy <= arcEffectRangeY; dy++)
            {
                int nx = cx + dx;
                int ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= gm.gridManager.map.GetLength(0) || ny >= gm.gridManager.map.GetLength(1)) continue;
                var g = gm.gridManager.map[nx, ny];
                if (g == null) continue;

                // 对范围内单位造成伤害
                foreach (var u in (g.infantries ?? new List<Infantry>()).ToList())
                {
                    if (u == null || !IsInstanceValid(u)) continue;
                    if (!ArcTargetTeamOk(u.team)) continue;
                    u.health = Mathf.Max(0, u.health - arcStrikeDamage);
                    u.UpdateHpLabel();
                    if (u.health <= 0 && !u.TryFlee())
                    {
                        gm.RemovePiece(u);
                        u.CallDeferred(Node.MethodName.QueueFree);
                    }
                }

                // 对范围内兵器造成伤害
                if (arcStrikeAffectsWeapons)
                {
                    foreach (var w in (g.weapons ?? new List<Weapon>()).ToList())
                    {
                        if (w == null || !IsInstanceValid(w) || w.isDestroyed || w == this) continue;
                        if (!ArcTargetTeamOk(w.team)) continue;
                        w.health = Mathf.Max(0, w.health - arcStrikeDamage);
                        w.UpdateHpLabel();
                        if (w.health <= 0) w.TakeDamage(99999);
                    }
                }
            }
        }

        // 进入冷却
        arcStrikeCooldownCounter = arcStrikeCooldown;
        hasActed = true;
    }

    private bool ArcTargetTeamOk(string targetTeam)
    {
        bool friendly = team == targetTeam ||
            (team == TeamHelper.Player || targetTeam == TeamHelper.Player);
        return arcStrikeTargetPolicy switch
        {
            0 => friendly,
            1 => !friendly,
            _ => true
        };
    }

    /// <summary>回合开始：冷却递减</summary>
    public void OnTurnStartArc()
    {
        if (arcStrikeCooldownCounter > 0)
            arcStrikeCooldownCounter--;
    }

    // ========== AI 兵器操作（全适配：中立/玩家方走自带AI，AI阵营由 AI_Manager 按视野操作）==========

    public override void ExecuteAI()
    {
        // 自带AI只服务 P0/P-1（中立）与玩家方兵器；AI阵营兵器由 AI_Manager.OperateArc 带视野操作
        if (!TeamHelper.ShouldWeaponUseAI(team) && !TeamHelper.ShouldPlayerWeaponUseAI(3, team))
            return;
        if (hasActed || !ArcStrikeReady) return;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return;
        var best = AIBestStrikeGrid(gm, null);
        if (best != null) PerformArcStrike(best, gm);
    }

    /// <summary>AI评分：射程内找天基打击收益最大的目标格（visibleFilter 非空=只用可见单位计分）</summary>
    public Grids AIBestStrikeGrid(GameManager gm, System.Func<Infantry, bool> visibleFilter)
    {
        Grids best = null;
        int bestScore = 0;
        int cx = grid.GridIndex.X, cy = grid.GridIndex.Y;
        for (int tx = 0; tx < gm.gridManager.searchRange.X; tx++)
        {
            for (int ty = 0; ty < gm.gridManager.searchRange.Y; ty++)
            {
                int dist = Mathf.Abs(tx - cx) + Mathf.Abs(ty - cy);
                if (dist < arcStrikeMinRange || dist > arcStrikeMaxRange) continue;
                int score = AIStrikeScore(gm, tx, ty, visibleFilter);
                if (score > bestScore) { bestScore = score; best = gm.gridManager.map[tx, ty]; }
            }
        }
        return best;
    }

    private int AIStrikeScore(GameManager gm, int tx, int ty, System.Func<Infantry, bool> visibleFilter)
    {
        int score = 0;
        for (int dx = -arcEffectRangeX; dx <= arcEffectRangeX; dx++)
        {
            for (int dy = -arcEffectRangeY; dy <= arcEffectRangeY; dy++)
            {
                int x = tx + dx, y = ty + dy;
                if (x < 0 || y < 0 || x >= gm.gridManager.searchRange.X || y >= gm.gridManager.searchRange.Y) continue;
                var g = gm.gridManager.map[x, y];
                if (g == null) continue;
                foreach (var u in g.infantries)
                {
                    if (u == null || !IsInstanceValid(u)) continue;
                    if (visibleFilter != null && !visibleFilter(u)) continue;
                    if (!ArcTargetTeamOk(u.team)) continue;       // 打击政策外的目标不受影响
                    int v = AITargetValue(u.maxHealth, u.health, u.cost);
                    score += TeamHelper.IsEnemyForAttacker(team, u.team) ? v : -v;   // 误伤己方=负分
                }
                if (arcStrikeAffectsWeapons)
                {
                    foreach (var w in g.weapons)
                    {
                        if (w == null || !IsInstanceValid(w) || w.isDestroyed || w == this) continue;
                        if (!ArcTargetTeamOk(w.team)) continue;
                        int v = AITargetValue(w.maxHealth, w.health, w.cost);
                        score += TeamHelper.IsEnemyForAttacker(team, w.team) ? v : -v;
                    }
                }
            }
        }
        return score;
    }

    private int AITargetValue(int maxHp, int hp, int targetCost)
    {
        int baseV = Mathf.Max(1000, targetCost);
        if (hp <= arcStrikeDamage) return baseV;                   // 一击必杀=全额价值
        float frac = arcStrikeDamage / (float)Mathf.Max(1, maxHp);
        return Mathf.RoundToInt(frac * baseV);
    }
}
