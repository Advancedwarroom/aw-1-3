// GrandBolt.cs - 巨型雷电/大雷弹（AWDS 原作最终兵器）
// 原作：AWDS 最终关 Von Bolt 的巨型兵器，约19宽×13高（不规则），3个弱点（不同行），
//       中下方的红色大球弱点；弱点有冷却回合；摧毁全部3个弱点=胜利
// 新机制（可Export开关）：蠕动（每回合整体平移1格）、困兽犹斗（3弱点全毁后中心大弱点+产Oozium）
// 模块：多格占据（不规则足印）+ 弱点系统 + 冷却 + 势力变色 + 蠕动 + Oozium生产，全Export可拼装
// 2026-11-06 新增（issue#1 卡死规则）：敌人卡足印→双向禁动（occupantsBlockWriggle /
//   holesTrapEnemies / ooziumBlockedByOccupants 三个开关，默认全关=零行为变化，符合模块图鉴铁律）
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class GrandBolt : Weapon
{
    // ========== 1. 是否开启 ==========
    [ExportGroup("大雷弹模块")]
    [Export] public bool grandBoltEnabled = true;               // 1.Export是否开启

    // ========== 2. 多格占据（19×13，不规则足印）==========
    [ExportGroup("多格占据（不规则足印）")]
    [Export] public Vector2I grandBoltSize = new Vector2I(19, 13);  // 2.占位宽×高
    // 足印掩码：每行一个字符串，'#'=占据，'.'=空（长度须=size.X，行数须=size.Y）
    [Export] public string[] footprintMask = new string[] {
        "...................",
        "......#######.......",
        "....#############...",
        "...###############..",
        "..#################.",
        "..#################.",
        ".###################",
        ".###################",
        ".###################",
        "..#################.",
        "..#################.",
        "...###############..",
        ".....#########......",
    };

    // ========== 3. 弱点系统（3个，不同行）==========
    [ExportGroup("弱点系统")]
    [Export] public Godot.Collections.Array<Vector2I> weakPointOffsets = new Godot.Collections.Array<Vector2I> {
        new Vector2I(4, 2),    // 左弱点
        new Vector2I(9, 1),   // 中弱点
        new Vector2I(14, 2),  // 右弱点
    };
    [Export] public int weakPointMaxHealth = 999;              // 每个弱点血量（千分制）
    public List<int> weakPointHealths = new List<int>();        // 各弱点当前血量
    public List<bool> weakPointDestroyed = new List<bool>();     // 各弱点是否已摧毁
    [Export] public int destroyedWeakPointCount = 0;           // 已摧毁弱点数

    // ========== 3.5 弱点护盾（原作：黑水晶保护弱点·Factory标准 N:M 显式绑定表）==========
    [ExportGroup("弱点护盾（原作：黑水晶保护·显式绑定）")]
    [Export] public bool weakPointsShieldedByCrystals = false;        // 3.5原作机制：弱点由护盾源保护
    [Export] public string crystalTypeName = "Crystal";                  // 3.5护盾源类型名（默认黑水晶，可换任意兵器）
    [Export] public int[] shieldWeakPointIndices = new int[0];           // 3.5绑定表：每条绑定一项=被保护弱点序号（一弱点多源/一源多弱点均可）
    [Export] public Godot.Collections.Array<Vector2I> shieldSourceOffsets = new Godot.Collections.Array<Vector2I>();// 3.5与上一数组同序：护盾源格（相对锚点格）

    // ========== 4. 中心大弱点（困兽犹斗模式）==========
    [ExportGroup("困兽犹斗（中心大弱点）")]
    [Export] public bool desperateModeEnabled = true;          // 4.是否开启困兽犹斗模式
    [Export] public Vector2I centerWeakPointOffset = new Vector2I(9, 6); // 中心大弱点偏移（最下面红色大球）
    [Export] public int centerWeakPointMaxHealth = 9999;       // 中心大弱点血量（不可摧毁）
    public bool isInDesperateMode = false;                     // 是否处于困兽犹斗

    // ========== 5. 摧毁行为 ==========
    [ExportGroup("摧毁行为")]
    [Export] public bool destroyOnAllWeakPointsDestroyed = false; // 5.原作：3弱点全毁=消失（胜利）
    [Export] public bool enterDesperateOnAllDestroyed = true;   // 5.新机制：3弱点全毁→困兽犹斗

    // ========== 6. 冷却 ==========
    [ExportGroup("冷却")]
    [Export] public int grandBoltCooldown = 3;                 // 6.冷却回合数
    private int grandBoltCooldownCounter = 0;

    // ========== 7. 势力变色 ==========
    [Export] public bool factionRecolor = true;                // 7.整体随势力变色（ApplyPaletteMaterial）

    // ========== 8. 蠕动（新机制）==========
    [ExportGroup("蠕动（新机制）")]
    [Export] public bool wriggleEnabled = false;               // 8.是否开启蠕动玩法
    [Export] public int wriggleDistance = 1;                  // 8.每回合蠕动距离（格）
    public Vector2I pendingWriggleDirection = Vector2I.Zero;    // 待执行的蠕动方向
    public bool hasWriggledThisTurn = false;

    // ========== 9. Oozium 生产（困兽犹斗模式）==========
    [ExportGroup("Oozium生产（困兽犹斗）")]
    [Export] public bool ooziumProductionEnabled = true;       // 9.困兽犹斗时是否生产Oozium
    [Export] public string ooziumUnitName = "Oozium";         // 9.生产的单位名
    [Export] public int ooziumProductionPerTurn = 3;          // 9.每回合生产次数
    [Export] public int ooziumProductionCooldown = 1;        // 9.生产冷却（困兽犹斗时冷却减少）
    [Export] public bool ooziumOverwriteUnits = true;         // 9.直接覆盖上面存在的单位
    private int ooziumProducedThisTurn = 0;

    // ========== 9.5 卡死规则（issue#1：内部敌人禁动）==========
    [ExportGroup("卡死规则（issue#1：内部敌人禁动）")]
    [Export] public bool occupantsBlockWriggle = false;       // 9.5-1 蠕动目标足印有任意单位→蠕动拒绝（敌人卡住我，我也动不了）
    [Export] public bool holesTrapEnemies = false;            // 9.5-2 足印全封闭内洞困住的敌方单位→不能移动（回合开始时挂/摘 trapped 标记）
    [Export] public bool ooziumBlockedByOccupants = false;    // 9.5-3 生产目标格有单位→拒绝生产（开此则不再覆盖活人）

    // ========== 10. 地图编辑器开局：蠕动后原格处理 ==========
    [ExportGroup("蠕动后原格处理")]
    [Export] public bool initialPlacementVacateToPlain = true; // 10.开局放的蠕动后原格空出平原（GROUND）
    [Export] public bool preserveTerrainOnLaterVacate = true; // 10.后期覆盖又离开：地形/兵器/设施保持

    public override void _Ready()
    {
        isMultiTile = true;
        size = grandBoltSize;

        // 初始化弱点血量
        weakPointHealths.Clear();
        weakPointDestroyed.Clear();
        for (int i = 0; i < weakPointOffsets.Count; i++)
        {
            weakPointHealths.Add(weakPointMaxHealth);
            weakPointDestroyed.Add(false);
        }

        base._Ready();
    }

    // ✅ 不规则足印：根据掩码计算占据格子
    public override List<Vector2I> GetOccupiedIndices(Vector2I anchorIndex)
    {
        var result = new List<Vector2I>();
        if (footprintMask == null || footprintMask.Length == 0)
        {
            // 无掩码：默认全矩形
            for (int x = 0; x < size.X; x++)
                for (int y = 0; y < size.Y; y++)
                    result.Add(anchorIndex + new Vector2I(x, y));
            return result;
        }
        for (int y = 0; y < footprintMask.Length && y < size.Y; y++)
        {
            string row = footprintMask[y] ?? "";
            for (int x = 0; x < row.Length && x < size.X; x++)
            {
                if (row[x] == '#')
                    result.Add(anchorIndex + new Vector2I(x, y));
            }
        }
        return result;
    }

    // ✅ 掩码内洞：包围盒内 '.' 格（卡死规则用）
    public List<Vector2I> GetHoleIndices(Vector2I anchorIndex)
    {
        var result = new List<Vector2I>();
        if (footprintMask == null || footprintMask.Length == 0) return result;
        for (int y = 0; y < footprintMask.Length && y < size.Y; y++)
        {
            string row = footprintMask[y] ?? "";
            for (int x = 0; x < row.Length && x < size.X; x++)
            {
                if (row[x] == '.')
                    result.Add(anchorIndex + new Vector2I(x, y));
            }
        }
        return result;
    }

    // ✅ 弱点判定：3个弱点 + 中心大弱点
    public override bool IsWeakPointGrid(Grids checkGrid)
    {
        if (!isMultiTile || occupiedGrids.Count == 0) return true;
        return GetWeakPointGrids().Contains(checkGrid);
    }

    public List<Grids> GetWeakPointGrids()
    {
        var result = new List<Grids>();
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null) return result;
        var anchorIndex = grid?.GridIndex ?? new Vector2I(0, 0);

        for (int i = 0; i < weakPointOffsets.Count; i++)
        {
            if (weakPointDestroyed.Count > i && weakPointDestroyed[i]) continue; // 已摧毁的不再可攻击
            var wpIndex = anchorIndex + weakPointOffsets[i];
            if (wpIndex.X >= 0 && wpIndex.X < gm.gridManager.searchRange.X &&
                wpIndex.Y >= 0 && wpIndex.Y < gm.gridManager.searchRange.Y)
            {
                var g = gm.gridManager.map[wpIndex.X, wpIndex.Y];
                if (g != null) result.Add(g);
            }
        }
        // 困兽犹斗：中心大弱点（不可摧毁）
        if (isInDesperateMode)
        {
            var cIndex = anchorIndex + centerWeakPointOffset;
            if (cIndex.X >= 0 && cIndex.X < gm.gridManager.searchRange.X &&
                cIndex.Y >= 0 && cIndex.Y < gm.gridManager.searchRange.Y)
            {
                var g = gm.gridManager.map[cIndex.X, cIndex.Y];
                if (g != null) result.Add(g);
            }
        }
        return result;
    }

    public override Grids GetWeakPointGrid()
    {
        var grids = GetWeakPointGrids();
        return grids.Count > 0 ? grids[0] : grid;
    }

    // ✅ 弱点伤害：按弱点单独扣血；全毁→困兽犹斗或摧毁
    public override void TakeDamage(int damage)
    {
        if (!IsInstanceValid(this)) return;

        // 3.5 护盾源保护：命中格对应弱点的绑定源全部存活→本次伤害无效
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) { base.TakeDamage(damage); return; }
        var attackerGrid = gm.CurrentAttackerGrid;
        if (attackerGrid != null && weakPointsShieldedByCrystals)
        {
            var anchorIndex = grid?.GridIndex ?? new Vector2I(0, 0);
            for (int b = 0; b < shieldWeakPointIndices.Length && b < shieldSourceOffsets.Count; b++)
            {
                if (anchorIndex + weakPointOffsets[shieldWeakPointIndices[b]] != attackerGrid.GridIndex)
                    continue;   // 本次命中的不是这个绑定表里的弱点
                var srcIndex = anchorIndex + shieldSourceOffsets[b];
                if (srcIndex.X < 0 || srcIndex.Y < 0 || srcIndex.X >= gm.gridManager.searchRange.X || srcIndex.Y >= gm.gridManager.searchRange.Y) continue;
                var srcGrid = gm.gridManager.map[srcIndex.X, srcIndex.Y];
                if (srcGrid == null) continue;
                foreach (var w in srcGrid.weapons)
                {
                    if (w != null && IsInstanceValid(w) && !w.IsQueuedForDeletion() && !w.isDestroyed && w.GetType().Name == crystalTypeName)
                        return;   // 护盾源存活→伤害无效
                }
            }
        }

        // 3.5.2 任何存活绑定源存在→主血量锁血（原作：水晶在=大雷弹无敌）
        if (weakPointsShieldedByCrystals && gm != null)
        {
            var anchorIndex = grid?.GridIndex ?? new Vector2I(0, 0);
            for (int b = 0; b < shieldWeakPointIndices.Length && b < shieldSourceOffsets.Count; b++)
            {
                var srcIndex = anchorIndex + shieldSourceOffsets[b];
                if (srcIndex.X < 0 || srcIndex.Y < 0 || srcIndex.X >= gm.gridManager.searchRange.X || srcIndex.Y >= gm.gridManager.searchRange.Y) continue;
                var srcGrid = gm.gridManager.map[srcIndex.X, srcIndex.Y];
                if (srcGrid == null) continue;
                foreach (var w in srcGrid.weapons)
                {
                    if (w != null && IsInstanceValid(w) && !w.IsQueuedForDeletion() && !w.isDestroyed && w.GetType().Name == crystalTypeName)
                        return;   // 主血量锁血：水晶在=无敌
                }
            }
        }

        // 按弱点扣血
        if (attackerGrid != null)
        {
            var weakGrids = GetWeakPointGrids();
            for (int i = 0; i < weakPointOffsets.Count && i < weakPointHealths.Count; i++)
            {
                if (weakPointDestroyed.Count > i && weakPointDestroyed[i]) continue;
                var anchorIndex = grid?.GridIndex ?? new Vector2I(0, 0);
                var wpIndex = anchorIndex + weakPointOffsets[i];
                if (wpIndex.X >= 0 && wpIndex.X < gm.gridManager.searchRange.X &&
                    wpIndex.Y >= 0 && wpIndex.Y < gm.gridManager.searchRange.Y)
                {
                    var wpGrid = gm.gridManager.map[wpIndex.X, wpIndex.Y];
                    if (wpGrid != null && wpGrid == attackerGrid)
                    {
                        weakPointHealths[i] -= damage;
                        if (weakPointHealths[i] <= 0)
                        {
                            weakPointDestroyed[i] = true;
                            destroyedWeakPointCount++;
                            OnWeakPointDestroyed(i);
                        }
                        return;
                    }
                }
            }
            // 困兽犹斗：中心大弱点命中→仅提示不可摧毁（不掉血）
            if (isInDesperateMode && attackerGrid != null)
            {
                var anchorIndex2 = grid?.GridIndex ?? new Vector2I(0, 0);
                var cIndex = anchorIndex2 + centerWeakPointOffset;
                if (cIndex == attackerGrid.GridIndex) return;   // 不可摧毁，不掉血
            }
        }
        base.TakeDamage(damage);
    }

    private void OnWeakPointDestroyed(int index)
    {
        if (destroyedWeakPointCount >= weakPointOffsets.Count)
        {
            if (enterDesperateOnAllDestroyed && desperateModeEnabled)
            {
                EnterDesperateMode();
            }
            else if (destroyOnAllWeakPointsDestroyed)
            {
                health = 0;
                base.TakeDamage(99999);
            }
        }
    }

    public void EnterDesperateMode()
    {
        if (isInDesperateMode) return;
        isInDesperateMode = true;
        grandBoltCooldownCounter = 0;
    }

    // ✅ 冷却管理
    public bool IsCooldownReady() => grandBoltCooldownCounter <= 0;
    public void StartCooldown() => grandBoltCooldownCounter = grandBoltCooldown;

    // ✅ 卡死规则：目标足印是否被任何单位阻挡
    public bool IsWriggleBlockedByOccupants(List<Vector2I> newIndices, GameManager gm)
    {
        if (!occupantsBlockWriggle || gm?.gridManager?.map == null) return false;
        foreach (var idx in newIndices)
        {
            if (idx.X < 0 || idx.Y < 0 || idx.X >= gm.gridManager.searchRange.X || idx.Y >= gm.gridManager.searchRange.Y) continue;
            var g = gm.gridManager.map[idx.X, idx.Y];
            if (g == null) continue;
            if (g.infantries != null && g.infantries.Any(u => u != null && IsInstanceValid(u)))
                return true;   // 有任意单位卡住→阻挡
        }
        return false;
    }

    // ✅ 卡死规则：洞格是否被本足印完全合围（四邻全被本方占据格/图外/真空封死）
    public bool IsHoleEnclosed(Vector2I holeIndex, GameManager gm)
    {
        if (gm?.gridManager?.map == null) return false;
        foreach (var dir in new[] { Vector2I.Left, Vector2I.Right, Vector2I.Up, Vector2I.Down })
        {
            var n = holeIndex + dir;
            if (n.X < 0 || n.Y < 0 || n.X >= gm.gridManager.searchRange.X || n.Y >= gm.gridManager.searchRange.Y)
                continue;   // 图外=墙，合围成立
            var g = gm.gridManager.map[n.X, n.Y];
            if (g == null) continue;          // 真空/无格=墙，合围成立
            if (g.weapons != null && g.weapons.Any(w => w == this && IsInstanceValid(w) && !w.isDestroyed))
                continue;   // 本方占据格=墙
            return false;   // 有一个活口→不合围
        }
        return true;
    }

    // ✅ 卡死规则：回合开始扫描内洞，给被困敌方单位挂/摘 trapped 标记
    public void RefreshHoleTraps(GameManager gm)
    {
        if (!holesTrapEnemies || gm?.gridManager?.map == null) return;
        var anchor = grid?.GridIndex ?? new Vector2I(0, 0);
        foreach (var hole in GetHoleIndices(anchor))
        {
            if (hole.X < 0 || hole.Y < 0 || hole.X >= gm.gridManager.searchRange.X || hole.Y >= gm.gridManager.searchRange.Y) continue;
            var g = gm.gridManager.map[hole.X, hole.Y];
            if (g == null) continue;
            bool enclosed = IsHoleEnclosed(hole, gm);
            foreach (var u in (g.infantries ?? new List<Infantry>()).ToList())
            {
                if (u == null || !IsInstanceValid(u)) continue;
                if (!enclosed) { u.trappedByWeapon = null; continue; }
                // 只困敌方（同队单位在洞里照常，队友不坑队友）
                if (TeamHelper.IsEnemyForAttacker(team, u.team))
                    u.trappedByWeapon = this;
            }
        }
    }

    // ✅ 蠕动：整体平移，原格地形/兵器/设施可选清空，新格单位摧毁或阻挡（卡死规则）
    public void Wriggle(Vector2I direction, GameManager gm)
    {
        if (!wriggleEnabled || hasWriggledThisTurn || direction == Vector2I.Zero || gm?.gridManager?.map == null) return;
        var oldAnchor = grid?.GridIndex ?? new Vector2I(0, 0);
        var newAnchor = oldAnchor + direction * wriggleDistance;

        // 计算新足印
        var newIndices = GetOccupiedIndices(newAnchor);

        // 边界检查：新足印必须全在图内
        foreach (var idx in newIndices)
        {
            if (idx.X < 0 || idx.Y < 0 || idx.X >= gm.gridManager.searchRange.X || idx.Y >= gm.gridManager.searchRange.Y)
                return;
            if (gm.gridManager.map[idx.X, idx.Y] == null) return;   // 屏间真空不可蠕动进入
        }

        // ✅ 卡死规则：敌人卡足印→不能移动
        if (IsWriggleBlockedByOccupants(newIndices, gm)) return;

        // 记录旧足印
        var oldIndices = GetOccupiedIndices(oldAnchor);
        var oldGridTypeIds = new Dictionary<Vector2I, GridType>();
        var oldWeapons = new Dictionary<Vector2I, List<Weapon>>();
        var oldFacilities = new Dictionary<Vector2I, Facility>();
        foreach (var idx in oldIndices)
        {
            var g = gm.gridManager.map[idx.X, idx.Y];
            if (g == null) continue;
            oldGridTypeIds[idx] = g.gridType;
            if (g.weapons != null && g.weapons.Count > 0)
                oldWeapons[idx] = new List<Weapon>(g.weapons);
            if (g.city != null)
                oldFacilities[idx] = g.city;
        }

        // 新足印上的单位：摧毁（蠕动的直接破坏效果，Oozium除外）
        foreach (var idx in newIndices)
        {
            var g = gm.gridManager.map[idx.X, idx.Y];
            if (g == null) continue;
            foreach (var u in (g.infantries ?? new List<Infantry>()).ToList())
            {
                if (u == null || !IsInstanceValid(u)) continue;
                u.health = 0;
                u.UpdateHpLabel();
                if (!u.TryFlee())
                {
                    gm.RemovePiece(u);
                    u.CallDeferred(Node.MethodName.QueueFree);
                }
            }
        }

        // 更新锚点
        var oldGrid = grid;
        grid = gm.gridManager.map[newAnchor.X, newAnchor.Y];

        // 新足印标记占据
        occupiedGrids.Clear();
        foreach (var idx in newIndices)
        {
            var g = gm.gridManager.map[idx.X, idx.Y];
            if (g == null) continue;
            if (g.weapons == null) g.weapons = new List<Weapon>();
            if (!g.weapons.Contains(this)) g.weapons.Add(this);
            if (!occupiedGrids.Contains(g)) occupiedGrids.Add(g);
        }

        // 移动视觉
        Vector2 oldPos = oldGrid != null ? oldGrid.GlobalPosition : GlobalPosition;
        if (oldGrid != null)
        {
            GlobalPosition = new Vector2(
                oldGrid.GlobalPosition.X + (newAnchor.X - oldAnchor.X) * gm.gridManager.CellWidth,
                oldGrid.GlobalPosition.Y + (newAnchor.Y - oldAnchor.Y) * gm.gridManager.CellHeight);
        }
        Position = new Vector2(newAnchor.X * gm.gridManager.CellWidth, newAnchor.Y * gm.gridManager.CellHeight);

        // 旧足印：地形/兵器/设施按Export处理
        foreach (var idx in oldIndices)
        {
            var g = gm.gridManager.map[idx.X, idx.Y];
            if (g == null) continue;
            if (oldWeapons.TryGetValue(idx, out var wList))
            {
                foreach (var w in wList)
                {
                    if (w == null || !IsInstanceValid(w) || w == this) continue;
                    if (preserveTerrainOnLaterVacate) continue;
                    w.CallDeferred(Node.MethodName.QueueFree);
                    g.weapons.Remove(w);
                    gm.weaponManager?.UnregisterWeapon(w);
                }
            }
            if (oldFacilities.TryGetValue(idx, out var fac))
            {
                if (!preserveTerrainOnLaterVacate && fac != null)
                    fac.CallDeferred(Node.MethodName.QueueFree);
            }
            if (g.gridType != oldGridTypeIds[idx] && preserveTerrainOnLaterVacate)
                g.gridType = oldGridTypeIds[idx];
            else if (!preserveTerrainOnLaterVacate)
                g.gridType = GridType.GROUND;
            g.UpdateTileSprite();
        }

        hasWriggledThisTurn = true;
        pendingWriggleDirection = Vector2I.Zero;
    }

    // ✅ 回合开始：重置蠕动、Oozium生产、冷却、卡死规则扫描
    public void OnTurnStartGrandBolt()
    {
        hasWriggledThisTurn = false;
        ooziumProducedThisTurn = 0;
        if (grandBoltCooldownCounter > 0) grandBoltCooldownCounter--;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return;

        // ✅ 卡死规则：扫描内洞困敌（先摘全部旧标记再重挂，防足印挪动后残留）
        if (holesTrapEnemies)
        {
            foreach (var u in gm.unitManager?.AllInfantries ?? new List<Infantry>())
            {
                if (u != null && IsInstanceValid(u) && ReferenceEquals(u.trappedByWeapon, this))
                    u.trappedByWeapon = null;
            }
            RefreshHoleTraps(gm);
        }
    }

    // ✅ 困兽犹斗：生产Oozium
    public void ProduceOozium(Grids targetGrid, GameManager gm)
    {
        if (!isInDesperateMode || !ooziumProductionEnabled) return;
        if (ooziumProducedThisTurn >= ooziumProductionPerTurn) return;
        if (targetGrid == null || gm == null) return;

        // 目标必须是本足印占据格
        if (!IsOccupiedGrid(targetGrid)) return;

        // ✅ 卡死规则：有单位→拒绝生产（不再覆盖活人）
        if (ooziumBlockedByOccupants && targetGrid.infantries != null && targetGrid.infantries.Any(u => u != null && IsInstanceValid(u)))
            return;

        var unit = gm.UnitManager_CreateUnitAt(ooziumUnitName, targetGrid.GridIndex, team);
        if (unit != null)
        {
            if (ooziumOverwriteUnits && !ooziumBlockedByOccupants)
            {
                foreach (var u in (targetGrid.infantries ?? new List<Infantry>()).ToList())
                {
                    if (u == null || u == unit || !IsInstanceValid(u)) continue;
                    u.health = 0;
                    u.UpdateHpLabel();
                    if (!u.TryFlee())
                    {
                        gm.RemovePiece(u);
                        u.CallDeferred(Node.MethodName.QueueFree);
                    }
                }
            }
            unit.isPowerActivated = true;
            ooziumProducedThisTurn++;
        }
    }

    // ========== AI：蠕动 + Oozium生产（全适配：中立/玩家方走自带AI，AI阵营由 AI_Manager 带视野操作）==========

    public override void ExecuteAI()
    {
        if (!TeamHelper.ShouldWeaponUseAI(team) && !TeamHelper.ShouldPlayerWeaponUseAI(3, team))
            return;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return;
        if (wriggleEnabled && !hasWriggledThisTurn)
        {
            var dir = AIBestWriggleDirection(gm, null);
            if (dir != Vector2I.Zero) Wriggle(dir, gm);
        }
        if (isInDesperateMode && ooziumProductionEnabled && ooziumProducedThisTurn < ooziumProductionPerTurn)
        {
            var target = AIBestOoziumGrid(gm, null);
            if (target != null) ProduceOozium(target, gm);
        }
    }

    /// <summary>AI评分：选最佳蠕动方向（优先攻击单位多的方向）</summary>
    public Vector2I AIBestWriggleDirection(GameManager gm, System.Func<Infantry, bool> visibleFilter)
    {
        Vector2I best = Vector2I.Zero;
        int bestScore = 0;
        var anchor = grid?.GridIndex ?? new Vector2I(0, 0);
        foreach (var dir in new[] { Vector2I.Left, Vector2I.Right, Vector2I.Up, Vector2I.Down })
        {
            var newAnchor = anchor + dir * wriggleDistance;
            var newIndices = GetOccupiedIndices(newAnchor);
            // 边界检查
            bool valid = true;
            foreach (var idx in newIndices)
            {
                if (idx.X < 0 || idx.Y < 0 || idx.X >= gm.gridManager.searchRange.X || idx.Y >= gm.gridManager.searchRange.Y) { valid = false; break; }
                if (gm.gridManager.map[idx.X, idx.Y] == null) { valid = false; break; }
            }
            if (!valid) continue;
            // ✅ 卡死规则：被单位挡住的方向直接跳过
            if (IsWriggleBlockedByOccupants(newIndices, gm)) continue;
            int score = 0;
            foreach (var idx in newIndices)
            {
                var g = gm.gridManager.map[idx.X, idx.Y];
                if (g == null) continue;
                foreach (var u in (g.infantries ?? new List<Infantry>()))
                {
                    if (u == null || !IsInstanceValid(u)) continue;
                    if (visibleFilter != null && !visibleFilter(u)) continue;
                    if (TeamHelper.IsEnemyForAttacker(team, u.team))
                        score += Mathf.Max(1000, u.cost);
                }
            }
            if (score > bestScore) { bestScore = score; best = dir; }
        }
        return best;
    }

    /// <summary>AI：选最佳Oozium生产格（优先有敌方单位且可覆盖的）</summary>
    public Grids AIBestOoziumGrid(GameManager gm, System.Func<Infantry, bool> visibleFilter)
    {
        Grids best = null;
        int bestScore = 0;
        foreach (var g in occupiedGrids)
        {
            if (g == null) continue;
            // ✅ 卡死规则：有单位且拒绝覆盖→跳过
            if (ooziumBlockedByOccupants && g.infantries != null && g.infantries.Any(u => u != null && IsInstanceValid(u)))
                continue;
            int score = 0;
            foreach (var u in (g.infantries ?? new List<Infantry>()))
            {
                if (u == null || !IsInstanceValid(u)) continue;
                if (visibleFilter != null && !visibleFilter(u)) continue;
                if (TeamHelper.IsEnemyForAttacker(team, u.team))
                    score += Mathf.Max(1000, u.cost);
            }
            if (score > bestScore) { bestScore = score; best = g; }
        }
        return best;
    }

    /// <summary>AI/玩家菜单共用: 本回合是否还能蠕动</summary>
    public bool CanWriggle() => wriggleEnabled && !hasWriggledThisTurn;

    /// <summary>AI_Manager入口: 蠕动逼近最近可见敌 + 绝望模式产Oozium(复用ExecuteAI同款判定)</summary>
    public void AIWriggleAndSpawn(GameManager gm, System.Func<Infantry, bool> canSee)
    {
        if (gm?.gridManager == null) return;
        if (wriggleEnabled && !hasWriggledThisTurn)
        {
            var dir = AIBestWriggleDirection(gm, canSee);
            if (dir != Vector2I.Zero) Wriggle(dir, gm);
        }
        if (isInDesperateMode && ooziumProductionEnabled && ooziumProducedThisTurn < ooziumProductionPerTurn)
        {
            var target = AIBestOoziumGrid(gm, canSee);
            if (target != null) ProduceOozium(target, gm);
        }
    }

    /// <summary>回合开始(GameManager.Turn调用入口, 转调无参版+预留gm上下文)</summary>
    public void OnTurnStartGrandBolt(GameManager gm) => OnTurnStartGrandBolt();

    [Export] public int empBurstWeaponDamage = 1;    // ✅ 电磁爆: 对敌对武器伤害(占位语义, 待作者调)
    [Export] public int empBurstInfantryDamage = 0;  // ✅ 电磁爆: 对敌对步兵伤害(占位语义, 待作者调)

    /// <summary>电磁爆: 回合开始爆发(GameManager.Turn调用)——占据格周围8格, 对敌对武器/步兵生效</summary>
    public void EmpBurst(GameManager gm)
    {
        if (gm?.gridManager?.map == null || grid == null) return;
        if (empBurstWeaponDamage <= 0 && empBurstInfantryDamage <= 0) return;
        var map = gm.gridManager.map;
        int w = map.GetLength(0), h = map.GetLength(1);
        foreach (var idx in GetOccupiedIndices(grid.GridIndex))
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = idx.X + dx, ny = idx.Y + dy;
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    var t = map[nx, ny];
                    if (t == null || !IsInstanceValid(t)) continue;
                    if (empBurstWeaponDamage > 0 && t.weapons != null)
                        foreach (var wp in t.weapons.ToList())
                            if (wp != this && IsInstanceValid(wp) && wp.team != team)
                                wp.TakeDamage(empBurstWeaponDamage);
                    if (empBurstInfantryDamage > 0 && t.infantry != null && t.infantry.team != team)
                    {
                        var u = t.infantry;
                        if (!u.healthLocked)
                            u.health = Math.Max(u.health - empBurstInfantryDamage, 1); // ✅ 不击杀(保底1血), 击杀结算语义待作者定
                    }
                }
    }
}
