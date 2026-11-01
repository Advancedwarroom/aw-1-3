// GrandBolt.cs - 巨型雷电/大雷弹（AWDS 原作最终兵器）
// 原作：AWDS 最终关 Von Bolt 的巨型兵器，约19宽×13高（不规则），3个弱点（不同行），
//       中下方的红色大球弱点；弱点有冷却回合；摧毁全部3个弱点=胜利
// 新机制（可Export开关）：蠕动（每回合整体平移1格）、困兽犹斗（3弱点全毁后中心大弱点+产Oozium）
// 模块：多格占据（不规则足印）+ 弱点系统 + 冷却 + 势力变色 + 蠕动 + Oozium生产，全Export可拼装
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
    [Export] public Vector2I[] weakPointOffsets = new Vector2I[] {
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
    [Export] public Vector2I[] shieldSourceOffsets = new Vector2I[0];// 3.5与上一数组同序：护盾源格（相对锚点格）

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
        for (int i = 0; i < weakPointOffsets.Length; i++)
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

        for (int i = 0; i < weakPointOffsets.Length; i++)
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

    // ✅ 弱点受击：扣血，摧毁判定
    public void DamageWeakPoint(int weakPointIndex, int damage)
    {
        if (weakPointIndex < 0 || weakPointIndex >= weakPointHealths.Count) return;
        if (weakPointDestroyed[weakPointIndex]) return; // 已摧毁
        if (IsWeakPointShielded(weakPointIndex)) return;   // ✅ 显式绑定护盾：登记源存活→伤害无效

        weakPointHealths[weakPointIndex] -= damage;
        weakPointHealths[weakPointIndex] = Mathf.Max(0, weakPointHealths[weakPointIndex]);

        if (weakPointHealths[weakPointIndex] <= 0)
        {
            weakPointDestroyed[weakPointIndex] = true;
            destroyedWeakPointCount++;
            OnWeakPointDestroyed(weakPointIndex);
        }
    }

    // ✅ Factory标准绑定表：遍历登记绑定（同 CheckPowerLinkage 接缝判定访问模式）
    private bool IsWeakPointShielded(int weakPointIndex)
    {
        if (!weakPointsShieldedByCrystals) return false;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null || grid == null) return false;
        var map = gm.gridManager.map;
        var anchor = grid.GridIndex;
        int n = Mathf.Min(shieldWeakPointIndices.Length, shieldSourceOffsets.Length);
        for (int k = 0; k < n; k++)
        {
            if (shieldWeakPointIndices[k] != weakPointIndex) continue;
            int sx = anchor.X + shieldSourceOffsets[k].X;
            int sy = anchor.Y + shieldSourceOffsets[k].Y;
            if (sx < 0 || sy < 0 || sx >= map.GetLength(0) || sy >= map.GetLength(1)) continue;
            var g = map[sx, sy];
            if (g == null) continue;
            foreach (var w in g.weapons)
            {
                if (IsInstanceValid(w) && !w.IsQueuedForDeletion() && !w.isDestroyed
                    && w.GetType().Name == crystalTypeName)
                    return true;
            }
        }
        return false;
    }

    private void OnWeakPointDestroyed(int index)
    {
        // ✅ 弱点被摧毁后不能生产单位（仅困兽犹斗的Oozium生产不受影响）
        if (destroyedWeakPointCount >= weakPointOffsets.Length)
        {
            // 全部弱点被摧毁
            if (enterDesperateOnAllDestroyed && desperateModeEnabled)
            {
                // ✅ 新机制：困兽犹斗（中心大弱点+Oozium生产）
                EnterDesperateMode();
            }
            else if (destroyOnAllWeakPointsDestroyed)
            {
                // ✅ 原作：3弱点全毁=消失（胜利）
                DestroyGrandBolt();
            }
        }
    }

    // ✅ 困兽犹斗模式：中心大弱点（不可摧毁）+ 生产Oozium
    private void EnterDesperateMode()
    {
        isInDesperateMode = true;
        grandBoltCooldownCounter = 0; // 冷却减少
        // 中心大弱点血量（不可摧毁）
        weakPointHealths.Add(centerWeakPointMaxHealth);
        weakPointDestroyed.Add(false);
    }

    // ✅ 摧毁大雷弹
    private void DestroyGrandBolt()
    {
        isDestroyed = true;
        OnMultiTileDestroyed();
    }

    // ✅ 势力变色：整体随势力变色
    protected override void ApplyPaletteMaterial()
    {
        if (!factionRecolor) return; // 不变色则跳过
        base.ApplyPaletteMaterial();
    }

    // ========== 蠕动（新机制）==========
    // ✅ 点击"蠕动"按钮后：选择方向，整体平移1格
    public bool CanWriggle()
    {
        return wriggleEnabled && !hasWriggledThisTurn && !isDestroyed;
    }

    // 执行蠕动：方向 (dx,dy)，整体平移 wriggleDistance 格
    public void Wriggle(Vector2I direction, GameManager gm)
    {
        if (!CanWriggle() || gm?.gridManager == null) return;
        if (direction == Vector2I.Zero) return;

        var oldIndices = GetOccupiedIndices(grid.GridIndex);
        var newAnchor = grid.GridIndex + direction * wriggleDistance;
        var newIndices = GetOccupiedIndices(newAnchor);

        // 检查新位置是否越界
        foreach (var idx in newIndices)
        {
            if (idx.X < 0 || idx.Y < 0 || idx.X >= gm.gridManager.searchRange.X || idx.Y >= gm.gridManager.searchRange.Y)
                return;
        }

        // ✅ 蠕动时：单位被摧毁
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

        // ✅ 原格处理：开局放的→空出平原（GROUND）；后期覆盖又离开→保持
        foreach (var idx in oldIndices)
        {
            if (newIndices.Contains(idx)) continue; // 新旧重叠部分不处理
            var g = gm.gridManager.map[idx.X, idx.Y];
            if (g == null) continue;

            if (initialPlacementVacateToPlain)
            {
                // 开局放的：原格空出平原
                g.gridType = GridType.GROUND;
            }
            // preserveTerrainOnLaterVacate: 后期覆盖又离开，地形/兵器/设施保持（不处理）
        }

        // 移动到新位置
        var newGrid = gm.gridManager.map[newAnchor.X, newAnchor.Y];
        if (newGrid != null)
        {
            grid = newGrid;
            Position = newGrid.Position;
        }

        // 重新绑定多格
        if (gm.weaponManager != null)
        {
            gm.weaponManager.BindWeaponToGrid(this, true);
        }

        hasWriggledThisTurn = true;
    }

    // ========== Oozium 生产（困兽犹斗模式）==========
    public bool CanProduceOozium()
    {
        return isInDesperateMode && ooziumProductionEnabled &&
               ooziumProducedThisTurn < ooziumProductionPerTurn;
    }

    // 在紧邻格生产Oozium（直接覆盖上面存在的单位）
    public void ProduceOozium(Grids targetGrid, GameManager gm)
    {
        if (!CanProduceOozium() || targetGrid == null || gm == null) return;

        // 覆盖现有单位
        if (ooziumOverwriteUnits)
        {
            foreach (var u in (targetGrid.infantries ?? new List<Infantry>()).ToList())
            {
                if (u == null || !IsInstanceValid(u)) continue;
                gm.RemovePiece(u);
                u.CallDeferred(Node.MethodName.QueueFree);
            }
        }

        // 生产Oozium（通过UnitProductionDatabase加载场景）
        if (!UnitProductionDatabase.HasUnit(ooziumUnitName)) return;
        var info = UnitProductionDatabase.GetInfo(ooziumUnitName);
        var scene = GD.Load<PackedScene>(info.ScenePath);
        if (scene == null) return;
        var unit = scene.Instantiate<Infantry>();
        if (unit == null) return;

        unit.Position = targetGrid.Position;
        unit.team = team;
        int unitNumber = gm.unitManager != null ? gm.unitManager.AllUnits.Count + 1 : 0;
        unit.Name = $"{team}_{ooziumUnitName}_{unitNumber}";

        gm.unitManager.units.AddChild(unit);
        bool bound = gm.unitManager.BindUnitToGrid(unit, true);
        if (bound)
        {
            ooziumProducedThisTurn++;
        }
        else
        {
            unit.QueueFree();
        }
    }

    // ========== 回合开始 ==========
    public void OnTurnStartGrandBolt(GameManager gm)
    {
        // 冷却递减
        if (grandBoltCooldownCounter > 0)
            grandBoltCooldownCounter--;

        // 困兽犹斗：冷却减少
        if (isInDesperateMode && grandBoltCooldown > 0)
        {
            grandBoltCooldownCounter = Mathf.Max(0, grandBoltCooldownCounter - 1);
        }

        // 重置蠕动
        hasWriggledThisTurn = false;
        pendingWriggleDirection = Vector2I.Zero;

        // 重置Oozium生产
        ooziumProducedThisTurn = 0;
    }

    // ========== 势力变更时刷新外观 ==========
    public override void RefreshTeamVisual()
    {
        base.RefreshTeamVisual(); // 整体随势力变色
    }
}
