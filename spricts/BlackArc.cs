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
    // ========== ✅ CoW (R,G)编码贴图 → LUT解码(随势力换色) ==========
    protected override void ApplyPaletteMaterial()
    {
        if (animSprite == null || !IsInstanceValid(animSprite)) return;
        var lut = FactionPaletteManager.GetCowLutMaterial(team);
        if (lut == null) return;
        animSprite.Material = (ShaderMaterial)lut.Duplicate(); // 独立副本防 tone 污染共享缓存
    }

    // ========== 1. 是否开启天基打击 ==========
    [ExportGroup("天基打击模块")]
    [Export] public bool arcStrikeEnabled = true;             // 1.Export是否开启天基打击

    // ========== 2. 打击范围（以该兵器为核心）==========
    [Export] public int arcStrikeMinRange = 1;               // 2.最小射程（0=可打自身所在格）
    [Export] public int arcStrikeMaxRange = 10;             // 2.最大射程
    [Export] public bool arcStrikeCanCrossScreens = false; // 天基打击选点射程是否允许跨屏

    // ========== 3. 打击效果范围（以目标点为核心 x-y）==========
    [Export] public int arcEffectRangeX = 1;                 // 3.以目标格为核心 x 范围（±x）
    [Export] public int arcEffectRangeY = 1;                 // 3.以目标格为核心 y 范围（±y）
    [Export] public bool arcEffectCanCrossScreens = false; // 天基打击效果面是否允许跨屏

    // ========== 4. 打击伤害 ==========
    [Export] public int arcStrikeDamage = 50;                // 4.伤害（千分制，50=5HP）

    // ========== 5. 冷却 ==========
    [Export] public int arcStrikeCooldown = 1;               // 5.冷却回合数（0=每回合可用）
    private int arcStrikeCooldownCounter = 0;

    // ========== 6. 是否影响兵器 ==========
    [Export] public bool arcStrikeAffectsWeapons = true;    // 6.是否影响兵器（同原作）

    // ========== 7. 目标策略 ==========
    [ExportGroup("打击目标势力")]
    [Export] public bool arcStrikeIncludeFriendly = false;
    [Export] public bool arcStrikeIncludeEnemy = true;
    [Export] public bool arcStrikeIncludeAllied = false;

    // ========== 8. 血量（继承基类 maxHealth/health 千分制）==========
    // 可被攻击扣血（原作可被攻击）

    // ========== 9. 力场模块（原作：存活期间保护地图目标物，对其攻击全部无效）==========
    [ExportGroup("力场模块（原作：黑弧存活时地图目标物攻击全部无效）")]
    [Export] public bool forceFieldEnabled = false;              // 是否开启力场（默认关，不强制）
    [Export] public bool forceFieldCanCrossScreens = false;   // 力场保护范围是否允许跨屏
    [Export] public Godot.Collections.Array<Vector2I> forceFieldGridOffsets = new(); // 保护的目标格（相对锚点偏移；空=不保护）

    // ========== 10. 护卫联动模块（原作：护卫炮全毁→黑弧坠落）==========
    [ExportGroup("护卫联动模块（原作：护卫兵器全毁→黑弧坠落）")]
    [Export] public bool escortLinkageEnabled = false;           // 是否开启护卫联动（默认关，不强制）
    [Export] public bool escortCanCrossScreens = false;       // 护卫联动绑定是否允许跨屏
    [Export] public Godot.Collections.Array<Vector2I> escortGridOffsets = new();   // 护卫兵器所在格（相对锚点偏移；空=无护卫）

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
            return d >= arcStrikeMinRange && d <= arcStrikeMaxRange && gm.gridManager.IsRangeAllowed(grid, g, arcStrikeCanCrossScreens);
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
        if (targetGrid == null || gm?.gridManager == null || grid == null) return;
        int strikeDistance = Mathf.Abs(targetGrid.GridIndex.X - grid.GridIndex.X) + Mathf.Abs(targetGrid.GridIndex.Y - grid.GridIndex.Y);
        if (strikeDistance < arcStrikeMinRange || strikeDistance > arcStrikeMaxRange || !gm.gridManager.IsRangeAllowed(grid, targetGrid, arcStrikeCanCrossScreens)) return;
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
                // ✅ N屏隔离：跨屏格子不受天基打击（开canCrossScreens才可跨图）
                if (!gm.gridManager.IsRangeAllowed(targetGrid, g, arcEffectCanCrossScreens)) continue;

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

                // 对范围内兵器造成伤害（被力场保护的地图目标物不受影响）
                if (arcStrikeAffectsWeapons && !IsGridForceFieldProtected(g))
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
        return TeamHelper.MatchesTeamRelation(team, targetTeam,
            arcStrikeIncludeFriendly, arcStrikeIncludeEnemy, arcStrikeIncludeAllied);
    }

    /// <summary>回合开始：冷却递减 + 护卫联动检查</summary>
    public override void OnTurnStart(GameManager gm) { OnTurnStartArc(); }
    public override bool OperateByAI(AI_Manager ai) => ai.OperateArc(this);
    public void OnTurnStartArc()
    {
        if (arcStrikeCooldownCounter > 0)
            arcStrikeCooldownCounter--;
        CheckEscortLinkage();
    }

    // ========== 力场/护卫联动实现 ==========

    /// <summary>力场判定：某格子是否被任意存活黑弧的力场保护（武器受伤入口调用，Export开关+偏移列表均为空时不保护）</summary>
    public static bool IsGridForceFieldProtected(Grids target)
    {
        if (target == null || !IsInstanceValid(target)) return false;
        var gm = target.GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.weaponManager?.AllWeapons == null) return false;
        foreach (var w in gm.weaponManager.AllWeapons)
        {
            if (w is not BlackArc arc || !IsInstanceValid(w) || w.isDestroyed || w.IsQueuedForDeletion()) continue;
            if (!arc.forceFieldEnabled || arc.grid == null || arc.forceFieldGridOffsets.Count == 0) continue;
            var gmGrid = arc.GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            if (gmGrid?.gridManager != null && !gmGrid.gridManager.IsRangeAllowed(arc.grid, target, arc.forceFieldCanCrossScreens)) continue;
            var anchor = arc.grid.GridIndex;
            foreach (var off in arc.forceFieldGridOffsets)
                if (anchor.X + off.X == target.GridIndex.X && anchor.Y + off.Y == target.GridIndex.Y)
                    return true;
        }
        return false;
    }

    /// <summary>护卫联动检查：护卫兵器全毁→黑弧坠落（任意兵器被毁后由引擎通知调用）</summary>
    public void CheckEscortLinkage()
    {
        if (isDestroyed || health <= 0 || IsQueuedForDeletion() || !escortLinkageEnabled) return;
        if (escortGridOffsets.Count == 0) return;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null || grid == null) return;
        var map = gm.gridManager.map;
        var anchor = grid.GridIndex;
        foreach (var off in escortGridOffsets)
        {
            int ex = anchor.X + off.X, ey = anchor.Y + off.Y;
            if (ex < 0 || ey < 0 || ex >= map.GetLength(0) || ey >= map.GetLength(1)) continue;
            var g = map[ex, ey];
            if (g == null) continue;
            if (!escortCanCrossScreens && !gm.gridManager.IsSameScreen(anchor, g.GridIndex)) continue;
            bool escortAlive = g.weapons.Any(w => w != null && IsInstanceValid(w) && !w.IsQueuedForDeletion() && !w.isDestroyed);
            if (escortAlive) return;   // 还有护卫活着
        }
        FallDown();   // 护卫全毁 → 坠落
    }

    /// <summary>坠落：原作护卫全毁后黑弧坠落（直接摧毁，无视力场）</summary>
    public void FallDown()
    {
        if (isDestroyed || health <= 0) return;
        ignoreForceField = true;    // 坠落流程绕过力场（防止自己保护自己导致坠不下来）
        try
        {
            health = 0;
            TakeDamage(99999, null);
        }
        finally { ignoreForceField = false; }
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
                var targetGrid = gm.gridManager.map[tx, ty];
                if (targetGrid == null || !gm.gridManager.IsRangeAllowed(grid, targetGrid, arcStrikeCanCrossScreens)) continue;
                int score = AIStrikeScore(gm, tx, ty, visibleFilter);
                if (score > bestScore) { bestScore = score; best = gm.gridManager.map[tx, ty]; }
            }
        }
        return best;
    }

    private int AIStrikeScore(GameManager gm, int tx, int ty, System.Func<Infantry, bool> visibleFilter)
    {
        int score = 0;
        var targetGrid = gm.gridManager.map[tx, ty];
        if (targetGrid == null) return 0;
        for (int dx = -arcEffectRangeX; dx <= arcEffectRangeX; dx++)
        {
            for (int dy = -arcEffectRangeY; dy <= arcEffectRangeY; dy++)
            {
                int x = tx + dx, y = ty + dy;
                if (x < 0 || y < 0 || x >= gm.gridManager.searchRange.X || y >= gm.gridManager.searchRange.Y) continue;
                var g = gm.gridManager.map[x, y];
                if (g == null || !gm.gridManager.IsRangeAllowed(targetGrid, g, arcEffectCanCrossScreens)) continue;
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
