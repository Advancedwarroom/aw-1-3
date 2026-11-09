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
    [Export] public Vector2I grandBoltSize = new Vector2I(19, 12);  // 2.占位宽×高（终稿19×12，180格）
    // 足印掩码：每行一个字符串，'#'=占据，'.'=空（长度须=size.X，行数须=size.Y）
    [Export] public string[] footprintMask = new string[] {
        "......#######......",   // y0  7
        "....###########....",   // y1 11
        "..###############..",   // y2 15
        "###################",   // y3 19 ┐
        "###################",   // y4 19 │
        "###################",   // y5 19 │ 核心 y5-6 (8-10,5-6)
        "###################",   // y6 19 │
        "###################",   // y7 19 │
        "###################",   // y8 19 ┘ 19×6
        "..###############..",   // y9 15 A(3)/C(15)外侧各1肉
        "....###########....",   // y10 11
        "......#######......",   // y11 7 B(9,11)居中
    };

    // ========== 3. 弱点系统（3个，不同行）==========
    [ExportGroup("弱点系统")]
    // ✅ 终稿弱点坐标，与 GrandBoltVisual.PTS / gb_points.png 逐位重合
    [Export] public Godot.Collections.Array<Vector2I> weakPointOffsets = new Godot.Collections.Array<Vector2I> {
        new Vector2I(3, 9),     // A（左）
        new Vector2I(9, 11),   // B（最底行七格中央）
        new Vector2I(15, 9),  // C（右）
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
    [Export] public bool allowCrossScreenBind = true;   // 🌐 是否可跨屏绑定：false=护盾源必须与本雷同屏，跨屏源不保护

    // ========== 4. 中心大弱点（困兽犹斗模式）==========
    [ExportGroup("困兽犹斗（中心大弱点）")]
    [Export] public bool desperateModeEnabled = true;          // 4.是否开启困兽犹斗模式
    [Export] public Vector2I centerWeakPointOffset = new Vector2I(9, 6); // 中心大弱点偏移（最下面红色大球）
    [Export] public bool defeatOwnerOnDestroyed = true;        // ✅ 摧毁后守方失败（兵器胜利判定目标，默认开）
    [Export] public int centerWeakPointMaxHealth = 9999;       // 中心大弱点血量（困兽犹斗：极难摧毁但可摧毁）
    public int centerWeakPointHealth = -1;                      // ✅ -1=未激活；困兽犹斗开启时=Max（可摧毁：打到0=胜利）
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
    // ✅ 每器官独立生产状态（完全分开）：弱点i器官（仅存活弱点、困兽前阶段）/ 核心器官（困兽犹斗）/ 蠕动冷却本就独立(grandBoltCooldown)
    private int[] weakProducedThisTurn = new int[0];
    private int[] weakCooldownCounters = new int[0];
    private int coreProducedThisTurn = 0;
    private int coreCooldownCounter = 0;
    // ✅ 首蠕平原：开局足印格集合（"原格空出平原"按格判定，无需时序标记）
    private System.Collections.Generic.HashSet<Vector2I> initialFootprint = new();

    // ✅ 势力换色（factionRecolor）：大雷弹本体+部件视觉应用调色板 Shader（黑弧同款机制，普通美术用 GetMaterial 模板色表模式）
    protected override void ApplyPaletteMaterial()
    {
        if (!factionRecolor) { base.ApplyPaletteMaterial(); return; }
        var mat = FactionPaletteManager.GetMaterial(team);
        if (mat == null) return;
        var main = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (main != null) main.Material = (ShaderMaterial)mat.Duplicate();   // Duplicate 防跨实例串参
        var pv = GetNodeOrNull<Node2D>("PartVisual");
        if (pv != null)
            foreach (var c in pv.GetChildren())
                if (c is CanvasItem ci && ci is not Label && ci is not Control) ci.Material = (ShaderMaterial)mat.Duplicate();   // 精灵2D/动画精灵通吃
    }

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

        // ✅ 四向旋转格子绑定：先冻结朝南(S)原始足印/弱点，再按当前方向重绑定（BHF教训：旋转必须绑格子）
        CaptureSouthFacing();
        RebindForDirection();

        // 初始化弱点血量
        weakPointHealths.Clear();
        weakPointDestroyed.Clear();
        for (int i = 0; i < weakPointOffsets.Count; i++)
        {
            weakPointHealths.Add(weakPointMaxHealth);
            weakPointDestroyed.Add(false);
        }
        weakProducedThisTurn = new int[weakPointOffsets.Count];
        weakCooldownCounters = new int[weakPointOffsets.Count];

        contributesToVictory = defeatOwnerOnDestroyed;   // ✅ 挂兵器胜利判定旗（默认开）
        victoryCountRequired = 1;
        base._Ready();
    }

    // ========== 四向旋转（格子级绑定，与 GrandBoltVisual 离线四向图同向同序）==========
    private BlackCannon.CannonDirection _direction = BlackCannon.CannonDirection.Down;  // 朝南=底图原向
    [Export]
    public BlackCannon.CannonDirection direction
    {
        get => _direction;
        set
        {
            _direction = value;
            if (IsNodeReady()) RebindForDirection();
        }
    }
    private string[] _maskS;                            // 朝南原始掩码（终稿）
    private Godot.Collections.Array<Vector2I> _weakS;   // 朝南原始弱点
    private Vector2I _sizeS;                          // 朝南原始占地

    private void CaptureSouthFacing()
    {
        _maskS = (string[])footprintMask.Clone();
        _weakS = new Godot.Collections.Array<Vector2I>(weakPointOffsets);
        _sizeS = grandBoltSize;
    }

    private void RebindForDirection()
    {
        if (_maskS == null) return;
        // Down=南=k0, Left=西=k1, Up=北=k2, Right=东=k3（与 GrandBoltVisual.SetDirection/RotRect 的 CCW 步数一致）
        int k = ((int)_direction + 2) & 3;
        var mask = (string[])_maskS.Clone();
        var weak = new Godot.Collections.Array<Vector2I>(_weakS);
        var center = centerWeakPointOffset;
        int w = _sizeS.X, h = _sizeS.Y;
        for (int i = 0; i < k; i++)
        {
            for (int j = 0; j < weak.Count; j++) weak[j] = RotCellCCW(weak[j], w, h);
            center = RotCellCCW(center, w, h);
            mask = RotateMaskCCW(mask, w, h);
            (w, h) = (h, w);
        }
        footprintMask = mask;
        weakPointOffsets = weak;
        centerWeakPointOffset = center;
        size = new Vector2I(w, h);
    }

    // (x,y) → (h-1-y, x)，与 GrandBoltVisual.RotRect 同公式
    private static Vector2I RotCellCCW(Vector2I c, int w, int h) => new Vector2I(h - 1 - c.Y, c.X);

    private static string[] RotateMaskCCW(string[] m, int w, int h)
    {
        var rows = new char[w][];                    // 新 H'=w 行
        for (int y = 0; y < w; y++) rows[y] = new string('.', h).ToCharArray();  // 新 W'=h 列
        for (int y = 0; y < h && y < (m?.Length ?? 0); y++)
        {
            string row = m[y] ?? "";
            for (int x = 0; x < w && x < row.Length; x++)
                if (row[x] == '#')
                    rows[x][h - 1 - y] = '#';        // (x,y) → 新 (x'=h-1-y, y'=x)
        }
        var r = new string[w];
        for (int y = 0; y < w; y++) r[y] = new string(rows[y]);
        return r;
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
    public override void TakeDamage(int damage, Infantry attacker = null)
    {
        if (!IsInstanceValid(this)) return;

        // 3.5 护盾源保护：命中格对应弱点的绑定源全部存活→本次伤害无效
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) { base.TakeDamage(damage, attacker); return; }
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
                if (!allowCrossScreenBind && !gm.gridManager.IsSameScreen(anchorIndex, srcIndex)) continue;   // 🌐 异屏源不保护
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
                if (!allowCrossScreenBind && !gm.gridManager.IsSameScreen(anchorIndex, srcIndex)) continue;   // 🌐 异屏源不保护
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
            // 困兽犹斗：中心大弱点命中→扣血（9999血，极难摧毁但可摧毁；打空=真正死亡=胜利）
            if (isInDesperateMode)
            {
                var anchorIndex2 = grid?.GridIndex ?? new Vector2I(0, 0);
                var cIndex2 = anchorIndex2 + centerWeakPointOffset;
                if (cIndex2 == attackerGrid.GridIndex && centerWeakPointHealth > 0)
                {
                    centerWeakPointHealth -= damage;
                    if (centerWeakPointHealth <= 0)
                    {
                        centerWeakPointHealth = 0;
                        EnterFinalDeath();   // ✅ 核心被摧毁：先演终局（核心死切片），再结算移除
                    }
                    return;
                }
            }
        }
        base.TakeDamage(damage, attacker);
    }

    [Export] public float finalDeathDelay = 1.5f;   // 核心摧毁后终局演出时长（秒）
    private bool _finalDeathStarted = false;

    // ✅ 终局演出：核心死切片上屏+动画定格，延迟后走正规死亡结算（dead 终局贴图终于能上屏）
    private async void EnterFinalDeath()
    {
        if (_finalDeathStarted) return;
        _finalDeathStarted = true;
        var vis = GetNodeOrNull<GrandBoltVisual>("PartVisual");
        if (vis != null)
        {
            vis.SetWeakPointDead(3);   // 核心死态切片（gb_core.png 死状态）
            vis.SetAnimFps(0);          // 定格当前帧
        }
        await ToSignal(GetTree().CreateTimer(finalDeathDelay), SceneTreeTimer.SignalName.Timeout);
        if (!GodotObject.IsInstanceValid(this)) return;   // 演出期间被别的伤害提前拆了就别补刀
        base.TakeDamage(99999);
    }

    private void OnWeakPointDestroyed(int index)
    {
        // ✅ 弱点死亡→视觉同步换死切片（此前逻辑死了皮还活着；三死齐即自动呈现困兽犹斗=lost 合成态）
        var vis = GetNodeOrNull<GrandBoltVisual>("PartVisual");
        if (vis != null) vis.SetWeakPointDead(index);

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
        if (isInDesperateMode) return;        if (!grandBoltEnabled) return;   // ✅ 总开关：不进困兽犹斗

        isInDesperateMode = true;
        grandBoltCooldownCounter = 0;
        centerWeakPointHealth = centerWeakPointMaxHealth;   // ✅ 中心大弱点激活（9999血，可摧毁）
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
    // ✅ 旧签名兼容：AI 用，默认直达最大距离
    public void Wriggle(Vector2I direction, GameManager gm)
    {
        Wriggle(direction, wriggleDistance, gm);
    }

    // ✅ 手动蠕动核心：步数显式指定（点击格曼哈顿距离），不再默认直达最大
    public void Wriggle(Vector2I direction, int steps, GameManager gm)
    {
        if (!wriggleEnabled || !grandBoltEnabled || hasWriggledThisTurn || direction == Vector2I.Zero || steps <= 0 || gm?.gridManager?.map == null) return;
        steps = Mathf.Min(steps, wriggleDistance);
        var oldAnchor = grid?.GridIndex ?? new Vector2I(0, 0);
        var newAnchor = oldAnchor + direction * steps;

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
                    if (HasVacateKeepTerrain(idx)) continue;
                    w.CallDeferred(Node.MethodName.QueueFree);
                    g.weapons.Remove(w);
                    gm.weaponManager?.UnregisterWeapon(w);
                }
            }
            if (oldFacilities.TryGetValue(idx, out var fac))
            {
                if (!HasVacateKeepTerrain(idx) && fac != null)
                    fac.CallDeferred(Node.MethodName.QueueFree);
            }
            if (g.gridType != oldGridTypeIds[idx] && HasVacateKeepTerrain(idx))
                g.gridType = oldGridTypeIds[idx];
            else if (!HasVacateKeepTerrain(idx))
                g.gridType = GridType.GROUND;
            g.UpdateTileSprite();
        }

        hasWriggledThisTurn = true;
        pendingWriggleDirection = Vector2I.Zero;
    }

    // ✅ 回合开始：重置蠕动、Oozium生产、冷却、卡死规则扫描
    public void OnTurnStartGrandBolt()
    {
        if (!grandBoltEnabled) return;   // ✅ 总开关：关闭则蠕动/生产/卡死扫描全部停摆
        hasWriggledThisTurn = false;
        ooziumProducedThisTurn = 0;
        coreProducedThisTurn = 0;
        if (coreCooldownCounter > 0) coreCooldownCounter--;                 // ✅ 核心冷却独立递减
        for (int i = 0; i < weakProducedThisTurn.Length; i++)
        {
            weakProducedThisTurn[i] = 0;
            if (weakCooldownCounters[i] > 0) weakCooldownCounters[i]--;     // ✅ 每个弱点冷却独立递减
        }
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
        if (!grandBoltEnabled || !ooziumProductionEnabled) return;
        if (targetGrid == null || gm == null) return;

        // ✅ 器官分流：弱点格→弱点器官i；中心格→核心；其余格拒绝
        int organ = ResolveOrganIndex(targetGrid);
        if (organ < 0) return;
        bool isCore = organ == 99;
        if (isCore)
        {
            if (!isInDesperateMode) return;                 // 核心仅困兽犹斗阶段可产
        }
        else
        {
            if (isInDesperateMode) return;                  // 弱点器官仅困兽前阶段可产
            if (organ >= weakPointDestroyed.Count || weakPointDestroyed[organ]) return;   // 该弱点已毁→该器官停产
        }

        int produced = isCore ? coreProducedThisTurn : weakProducedThisTurn[organ];
        int cd = isCore ? coreCooldownCounter : weakCooldownCounters[organ];
        if (cd > 0) return;                                 // ✅ 该器官冷却中（与其他器官互不影响）
        if (produced >= ooziumProductionPerTurn) return;  // 该器官本回合配额已满

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
            if (isCore)
            {
                coreProducedThisTurn++;
                if (coreProducedThisTurn >= ooziumProductionPerTurn)
                    coreCooldownCounter = Mathf.Max(0, ooziumProductionCooldown);           // ✅ 核心独立冷却
            }
            else
            {
                weakProducedThisTurn[organ]++;
                if (weakProducedThisTurn[organ] >= ooziumProductionPerTurn)
                    weakCooldownCounters[organ] = Mathf.Max(0, ooziumProductionCooldown);   // ✅ 弱点器官独立冷却（互不影响）
            }
        }
    }

    // ✅ 器官解析：弱点下标 0..N-1；核心=99；非器官=-1
    private int ResolveOrganIndex(Grids g)
    {
        if (g == null || grid == null) return -1;
        var baseIdx = grid.GridIndex;
        for (int k = 0; k < weakPointOffsets.Count; k++)
            if (baseIdx + weakPointOffsets[k] == g.GridIndex) return k;
        if (baseIdx + centerWeakPointOffset == g.GridIndex) return 99;
        return -1;
    }

    // ✅ 首蠕平原判定：开局足印格按"空出平原"开关，其余格走"后期保持"
    private bool HasVacateKeepTerrain(Vector2I idx)
    {
        if (initialFootprint.Count == 0 && grid != null)
            foreach (var i2 in GetOccupiedIndices(grid.GridIndex)) initialFootprint.Add(i2);
        if (initialFootprint.Count == 0) return preserveTerrainOnLaterVacate;
        return initialFootprint.Contains(idx) ? !initialPlacementVacateToPlain : preserveTerrainOnLaterVacate;
    }

    // ==================== 手动蠕动：范围预览 + 曼哈顿步数 ====================
    // ✅ 可达范围：四轴各 1..wriggleDistance 格，越界/无格/被单位挡 → 该轴截断
    public System.Collections.Generic.List<Grids> GetWriggleReachableCells(GameManager gm)
    {
        var list = new System.Collections.Generic.List<Grids>();
        if (gm?.gridManager == null || grid == null || !CanWriggle() || !wriggleEnabled || !grandBoltEnabled) return list;
        var anchor = grid.GridIndex;
        foreach (var dir in new[] { Vector2I.Left, Vector2I.Right, Vector2I.Up, Vector2I.Down })
        {
            for (int k = 1; k <= wriggleDistance; k++)
            {
                var na = anchor + dir * k;
                var idxs = GetOccupiedIndices(na);
                bool valid = idxs.Count > 0;
                foreach (var idx in idxs)
                {
                    if (idx.X < 0 || idx.Y < 0 || idx.X >= gm.gridManager.searchRange.X || idx.Y >= gm.gridManager.searchRange.Y) { valid = false; break; }
                    var g = gm.gridManager.GetGridAt(idx);
                    if (g == null) { valid = false; break; }
                    if (occupantsBlockWriggle && g.infantry != null) { valid = false; break; }
                }
                if (!valid) break;                      // 遇阻断轴（不跳过障碍）
                var cell = gm.gridManager.GetGridAt(na);
                if (cell != null) list.Add(cell);
            }
        }
        return list;
    }

    // ✅ 点击格 → 主曼哈顿轴步数 → 蠕动（近格=少走，不再默认直达最大）
    public void WriggleToward(Grids targetCell, GameManager gm)
    {
        if (targetCell == null || grid == null) return;
        var d = targetCell.GridIndex - grid.GridIndex;
        if (d == Vector2I.Zero) return;
        Vector2I dir; int steps;
        if (Mathf.Abs(d.X) >= Mathf.Abs(d.Y)) { dir = new Vector2I(Mathf.Sign(d.X), 0); steps = Mathf.Abs(d.X); }
        else { dir = new Vector2I(0, Mathf.Sign(d.Y)); steps = Mathf.Abs(d.Y); }
        steps = Mathf.Clamp(steps, 1, wriggleDistance);
        Wriggle(dir, steps, gm);
    }

    // ✅ 手动选格 UI：绿色高亮可达格，点击确认
    public void BeginWriggleSelection(GameManager gm)
    {
        if (!wriggleEnabled || !grandBoltEnabled || !CanWriggle() || gm?.gridManager == null) return;
        var reachable = GetWriggleReachableCells(gm);
        if (reachable.Count == 0) return;
        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = true;
        foreach (var node in GetTree().GetNodesInGroup("grids"))
        {
            if (node is not Grids g) continue;
            if (reachable.Contains(g))
            {
                g.Modulate = new Color(0.35f, 1f, 0.55f, 0.85f);   // 蠕动绿
                g.IsInWeaponRange = true;
                gm.gridManager.OverrideUnitInput(g, true);
                g.OnClickGrid = (to) => ConfirmWriggle(to, gm);
            }
            else
            {
                gm.gridManager.OverrideUnitInput(g, true);
                g.OnClickEmpty = () => CancelWriggleSelection(gm);
            }
        }
    }

    public void ConfirmWriggle(Grids target, GameManager gm)
    {
        if (gm == null || target == null) return;
        CancelWriggleSelection(gm);
        if (!GetWriggleReachableCells(gm).Contains(target)) return;
        WriggleToward(target, gm);
    }

    public void CancelWriggleSelection(GameManager gm)
    {
        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = false;
        foreach (var node in GetTree().GetNodesInGroup("grids"))
            if (node is Grids g) { gm.gridManager.OverrideUnitInput(g, false); g.OnClickGrid = null; g.OnClickEmpty = null; }
    }

    // ✅ 全部存活器官剩余产能（AI 决策用；蠕动不在此列——它有自己独立的 grandBoltCooldown）
    public int TotalOoziumCapacityLeft()
    {
        int left = 0;
        if (!isInDesperateMode)
        {
            for (int k = 0; k < weakProducedThisTurn.Length; k++)
                if (k < weakPointDestroyed.Count && !weakPointDestroyed[k] && weakCooldownCounters[k] <= 0)
                    left += Mathf.Max(0, ooziumProductionPerTurn - weakProducedThisTurn[k]);
        }
        else if (coreCooldownCounter <= 0)
        {
            left += Mathf.Max(0, ooziumProductionPerTurn - coreProducedThisTurn);
        }
        return left;
    }

    // ✅ 第一个有产能的器官格（AI 备用目标）
    public Grids FirstAvailableOrganCell(GameManager gm)
    {
        if (gm?.gridManager == null || grid == null) return null;
        var baseIdx = grid.GridIndex;
        if (!isInDesperateMode)
        {
            for (int k = 0; k < weakPointOffsets.Count; k++)
            {
                if (k < weakPointDestroyed.Count && weakPointDestroyed[k]) continue;
                if (k >= weakProducedThisTurn.Length || weakProducedThisTurn[k] >= ooziumProductionPerTurn || weakCooldownCounters[k] > 0) continue;
                var g = gm.gridManager.GetGridAt(baseIdx + weakPointOffsets[k]);
                if (g != null) return g;
            }
        }
        else if (coreCooldownCounter <= 0 && coreProducedThisTurn < ooziumProductionPerTurn)
        {
            return gm.gridManager.GetGridAt(baseIdx + centerWeakPointOffset);
        }
        return null;
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
        if (grandBoltEnabled && ooziumProductionEnabled && TotalOoziumCapacityLeft() > 0)
        {
            var target = AIBestOoziumGrid(gm, null);
            if (target == null || ResolveOrganIndex(target) < 0) target = FirstAvailableOrganCell(gm);   // ✅ 非器官格→回退到有产能的器官
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
                                wp.TakeDamage(empBurstWeaponDamage, this);
                    if (empBurstInfantryDamage > 0 && t.infantry != null && t.infantry.team != team)
                    {
                        var u = t.infantry;
                        if (!u.healthLocked)
                            u.health = Mathf.Max(u.health - empBurstInfantryDamage, 1); // ✅ 不击杀(保底1血), 击杀结算语义待作者定
                    }
                }
    }
}
