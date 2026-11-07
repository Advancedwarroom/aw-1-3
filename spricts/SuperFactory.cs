using CannonDirection = BlackCannon.CannonDirection;
// SuperFactory.cs - 超级兵工厂 Black Hole Factory（AW2 原作 Factory 的完全体重制版）
// 原作机制：
// 1. 3×4 = 12 格多格占据，完全不可通行、无地形星（摧毁后仍不可通行）
// 2. 免疫一切伤害（包括导弹井和 CO 技能）——失效方式：摧毁连接它的管道接缝
// 3. 三个生产口（正面 1×3）：每回合最多生产 3 个单位，免费、出场即可移动开火、全生产列表（原作模式）
// 4. 联动：管道接缝被毁 → 兵工厂失去供能 → 变中立废铁（不可生产）
// 5. 中转口：Rig 在原管道接缝位置建造中转口（2回合 20/20 判定）→ 兵工厂改变归属
// 6. 缴获红利：接管后 n 回合内免费生产 3 个即刻行动单位；之后转常规（可选双生产菜单 1.2x 即时 / 0.8x 待机）
// 7. 旋转：canRotate 开启时可 90°/180°/270° 旋转（先检查目标位置是否被占，被占则旋转失败提醒）
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class SuperFactory : Weapon
{
        [ExportGroup("超级兵工厂 - 多格配置")]
    [Export] public Vector2I factorySize = new Vector2I(3, 4);   // 3宽×4高=12格
    [ExportGroup("生产模块扩展")]
    [Export] public bool productionFree = true;                 // ⑫ 无消耗（SuperFactory是Weapon不继承Facility，字段必须本类持有）
    [Export] public bool dualProductionMode = false;            // 双生产菜单（立即1.2×/待机0.8×）
    [Export] public float instantCostMultiplier = 1.2f;
    [Export] public float standbyCostMultiplier = 0.8f;

    private CannonDirection _direction = CannonDirection.Down;
    [Export]
    public CannonDirection direction
    {
        get => _direction;
        set
        {
            _direction = value;
            if (animSprite != null) SetDirection(value);   // 编辑器/运行时改向立即转贴图，不重进场景（开创人规矩）
        }
    }

    [ExportGroup("超级兵工厂 - 生产（原作模式）")]
    [Export] public bool productionEnabled = true;             // 是否开启生产                // 是否无消耗（原作：免费生产）
    [Export] public bool instantMoveAfterProduction = true;    // 生产后即可移动（原作：出场即可行动）
    [Export] public int maxProductionsPerTurn = 3;             // 每回合最多生产数（原作：3）
    [Export] public Godot.Collections.Array<string> producibleUnitNames = new();  // 生产列表（空=全列表）

    [ExportGroup("超级兵工厂 - 双生产菜单（自创模式：是否新玩法）")]
    /// <summary>✅ 是否更新：填"是"则本建筑的生产受全局"可生成单位白名单"限制（需Lab解锁）。</summary>
    [Export] public bool isUpdatable = true;           // 是否新玩法：开启后即时/待机两种生产       // 即时单位资金倍率（生产出来就能动）       // 待机单位资金倍率（正常生产，下回合才能动）
    /// <summary>待机单位上限（规格：0.8倍资金生产待机单位，最大三个）</summary>
    [Export] public int maxStandbyUnits = 3;
    /// <summary>当前待机单位列表（运行时，双生产菜单模式）</summary>
    public System.Collections.Generic.List<Infantry> standbyUnits = new();

    [ExportGroup("超级兵工厂 - 管道联动")]
    [Export] public Godot.Collections.Array<Vector2I> linkedSeamOffsets = new();  // 联动的管道接缝相对坐标（相对锚点格）
    [Export] public bool losePowerWhenSeamsDestroyed = true;  // 接缝全毁→失去供能变中立

    [ExportGroup("超级兵工厂 - 缴获红利")]
    [Export] public bool captureDividendEnabled = true;       // 接管后触发缴获红利
    [Export] public int dividendTurns = 1;                    // 红利持续回合数（原作：缴获后第一回合）
    [Export] public int dividendFreeUnits = 3;               // 红利期免费生产数（原作：3个即刻行动单位）
    [Export] public bool dividendRequireInstant = true;      // 红利单位必须即刻行动

    [ExportGroup("超级兵工厂 - 旋转")]
    [Export] public bool factoryCanRotate = true;             // 是否可旋转（90/180/270）
    [Export] public bool hasHealth = false;                          // 开启血量显示并可被攻击（默认关=CoW原味无敌，开创人规矩）

    // 运行时状态
    public int productionsThisTurn = 0;
    private bool _directionVisualApplied = false;
    public bool[] portUsedThisTurn = new bool[8];   // 每口一记：旋转不重置，仅新回合重置（开创人规矩）                       // 本回合已生产数
    public int dividendRemainingTurns = 0;                    // 红利剩余回合
    public int dividendUnitsRemaining = 0;                   // 红利期剩余免费单位
    public string dividendTeam = "";                          // 红利归属势力
    public bool lostPower = false;                            // 供能线已断（中立废铁）

    public override void _Ready()
    {
        isMultiTile = true;
        size = factorySize;
        canRotate = factoryCanRotate;
        CallDeferred(nameof(ApplyDirectionVisualDeferred));   // 贴图对齐延后一帧：_Ready早期UpdateMultiTileVisual NRE=进场闪退根
        if (!hasHealth)
        {
            var hl = GetNodeOrNull<Label>("HpLabel");
            if (hl != null) hl.Visible = false;   // 无血量=不显示
        }

        base._Ready();
        var dots = GetNodeOrNull<AnimatedSprite2D>("YellowDots");
        if (dots != null)
        {
            // 黄灯层只对中立皮生效：P1橙星/P2蓝月的灯是烘焙原画自带的（叠加会出第二条错位高亮）
            dots.Visible = GetFactoryAnimName() == "Neutral";
            if (dots.Visible && dots.SpriteFrames != null && dots.SpriteFrames.HasAnimation("Blink")) dots.Play("Blink");
        }
        if (animSprite != null && animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation(GetFactoryAnimName()))
            animSprite.Play(GetFactoryAnimName());   // 按势力播皮（橙星/蓝月/中立LUT）——修"不随势力换色"
        UpdateMultiTileVisual();
    }

    // ========== 生产口：正面 1×3（3 个门） ==========
    /// <summary>当前朝向的三个生产口格子（正面一排）</summary>
    public List<Grids> GetProductionExitGrids()
    {
        var result = new List<Grids>();
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null || grid == null) return result;

        var map = gm.gridManager.map;
        int w = map.GetLength(0), h = map.GetLength(1);
        var anchor = grid.GridIndex;

        // 兵工厂本体范围：anchor 为左上角，3宽×4高
        // 生产口在正面（朝向）外侧一排 3 格
        for (int i = 0; i < factorySize.X; i++)
        {
            Vector2I exitIndex = direction switch
            {
                CannonDirection.Down => new Vector2I(anchor.X + i, anchor.Y + factorySize.Y),   // 南侧
                CannonDirection.Up => new Vector2I(anchor.X + i, anchor.Y - 1),                // 北侧
                CannonDirection.Right => new Vector2I(anchor.X + factorySize.X, anchor.Y + i), // 东侧
                CannonDirection.Left => new Vector2I(anchor.X - 1, anchor.Y + i),              // 西侧
                _ => new Vector2I(-1, -1)
            };
            if (exitIndex.X >= 0 && exitIndex.X < w && exitIndex.Y >= 0 && exitIndex.Y < h)
            {
                var g = map[exitIndex.X, exitIndex.Y];
                if (g != null && IsInstanceValid(g)) result.Add(g);
            }
        }
        return result;
    }

    /// <summary>能否生产：供能正常 + 本回合有余额 + 生产开启</summary>
    public bool CanProduceNow()
    {
        return productionEnabled && !lostPower && !isDestroyed
            && productionsThisTurn < maxProductionsPerTurn;
    }

    /// <summary>红利期判定：当前势力是否在缴获红利期内</summary>
    public bool IsInDividend(string teamName)
    {
        return captureDividendEnabled && dividendRemainingTurns > 0 && teamName == dividendTeam;
    }

    // ========== 队伍变更（缴获/中转口接管）==========
    public void SetFactoryTeam(string newTeam, bool fromCapture)
    {
        if (team == newTeam) return;
        team = newTeam;
        RefreshTeamVisual();
        UpdateHpLabel();

        if (fromCapture && captureDividendEnabled && newTeam != TeamHelper.Player0)
        {
            // ✅ 缴获红利：记录获取时间，n 回合内免费 3 个即刻单位
            dividendRemainingTurns = dividendTurns;
            dividendUnitsRemaining = dividendFreeUnits;
            dividendTeam = newTeam;
        }
    }

    // ========== 回合流转 ==========
    public override void OnTurnStart()
    {
        if (isDestroyed) return;
        base.OnTurnStart();

        // 每方回合开始都重置生产次数？不——原作是每回合（任意势力）开始时工厂重置。
        // 这里按当前行动方重置（工厂属于当前方时才重置，避免中立工厂被刷）
        var gm0 = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        string currentTurnTeam = TeamHelper.GetPhaseTeamName(gm0?.turnPhase ?? 1);
        // 己方行动相位重置，或每天第一相位（新的一天开始全部充能——P-1/P0等非交战归属旧门控永不触发，开创人实测抓到）
        if (currentTurnTeam == team || (gm0?.turnPhase ?? 1) == 1)
        {
            productionsThisTurn = 0;
            portUsedThisTurn = new bool[8];
        }

        // 缴获红利倒计时（按回合递减）
        if (dividendRemainingTurns > 0)
        {
            dividendRemainingTurns--;
            if (dividendRemainingTurns <= 0)
            {
                dividendUnitsRemaining = 0;   // 红利结束
            }
        }
    }

    // ========== 管道联动：检查供能状态 ==========
    /// <summary>检查所有联动管道接缝；全部被毁→失去供能变中立废铁</summary>
    public void CheckPowerLinkage()
    {
        if (!losePowerWhenSeamsDestroyed || isDestroyed) return;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null || grid == null) return;

        var map = gm.gridManager.map;
        var anchor = grid.GridIndex;
        bool anyAlive = false;

        foreach (var offset in linkedSeamOffsets)
        {
            int sx = anchor.X + offset.X, sy = anchor.Y + offset.Y;
            if (sx < 0 || sy < 0 || sx >= map.GetLength(0) || sy >= map.GetLength(1)) continue;
            var g = map[sx, sy];
            if (g == null) continue;

            // 接缝活着 = 有兵器且未摧毁
            bool seamAlive = g.gridType == GridType.PIPESEAM &&
                              g.weapons.Any(w => IsInstanceValid(w) && !w.IsQueuedForDeletion() && !w.isDestroyed);
            if (seamAlive) { anyAlive = true; break; }
        }

        // 没配置联动 = 不做判定（默认有供能）
        if (linkedSeamOffsets.Count == 0) return;

        if (!anyAlive && !lostPower)
        {
            LosePower();
        }
        else if (anyAlive && lostPower)
        {
            RestorePower();
        }
    }

    /// <summary>失去供能：变中立废铁（不可生产）</summary>
    public void LosePower()
    {
        lostPower = true;
        SetFactoryTeam(TeamHelper.Player0, fromCapture: false);
        // 视觉：变暗
        SetVisualDark();
    }

    /// <summary>恢复供能（管道构造者修复接缝）</summary>
    public void RestorePower()
    {
        lostPower = false;
        SetVisualNormal();
    }

    // ========== 中转口接管 ==========
    /// <summary>中转口建造完成 → 兵工厂归属改为建造方</summary>
    public void TakeOverByRelay(string newTeam)
    {
        lostPower = false;
        SetVisualNormal();
        SetFactoryTeam(newTeam, fromCapture: true);   // 视作缴获：触发缴获红利
    }

    // ========== 旋转（90/180/270，带碰撞检查） ==========
    /// <summary>
    /// 旋转90°（顺时针）。先检查旋转后目标位置是否被单位/兵器/建筑阻挡；
    /// 被阻挡则旋转失败并给出提醒。
    /// </summary>
    // 编辑器放置用：直接设定方向+贴图旋转（不做占位检查——占位检查属于游戏中旋转）
    // 首帧 deferred 对齐方向（全部件就绪，避开_Ready早期的UpdateMultiTileVisual空引用）
    private void ApplyDirectionVisualDeferred()
    {
        if (_directionVisualApplied) return;
        _directionVisualApplied = true;
        SetDirection(direction);
    }


    private static readonly string[] DIR_SUFFIX = { "Up", "Right", "Down", "Left" };
    /// <summary>四向离线预渲染动画名: 团队_朝向(Up/Right/Down/Left, 与原画朝向枚举对齐)</summary>
    private string GetDirectionalAnimName() => $"{GetFactoryAnimName()}_{DIR_SUFFIX[((int)_direction) & 3]}";

    public void SetDirection(CannonDirection dir)
    {
        _direction = dir;   // 必须写后备字段——写direction会触发setter回调SetDirection=无限递归栈溢出（放完即闪退的根）
        // 足印随朝向：右/左=横向(宽4高3)，上/下=竖向(宽3高4)——幂等转置
        bool wantLandscape = (dir == CannonDirection.Right || dir == CannonDirection.Left);
        bool isLandscape = factorySize.X > factorySize.Y;
        if (wantLandscape != isLandscape)
        {
            factorySize = new Vector2I(factorySize.Y, factorySize.X);   // 实心矩形无需掩码，宽高调换即转置
            size = factorySize;
        }
        if (animSprite != null)
        {
            // ✅ 离线预渲染四向(2026-11): 运行时零旋转, 查表换Animation——BlackHoleFactory旋转错位坑根治
            var center = new Vector2(factorySize.X * 16, factorySize.Y * 16);
            animSprite.Position = center;
            animSprite.RotationDegrees = 0;
            var dAnim = GetDirectionalAnimName();
            if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation(dAnim))
                animSprite.Play(dAnim);
            var dots = GetNodeOrNull<AnimatedSprite2D>("YellowDots");
            if (dots != null) { dots.Position = center; }
            if (IsInsideTree()) UpdateMultiTileVisual();
        }
    }

    // 编辑器专用：只换皮播放，不碰调色板（编辑器无palette环境，防卡退）
    public void EditorRefreshSkin()
    {
        var anim = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (anim != null && anim.SpriteFrames != null && anim.SpriteFrames.HasAnimation(GetFactoryAnimName()))
            anim.Play(anim.SpriteFrames.HasAnimation(GetDirectionalAnimName()) ? GetDirectionalAnimName() : GetFactoryAnimName());
    }

    public bool RotateFactoryClockwise()
    {
        if (!factoryCanRotate) return false;
        if (isDestroyed) return false;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null || grid == null) return false;

        var map = gm.gridManager.map;
        var anchor = grid.GridIndex;
        int w = map.GetLength(0), h = map.GetLength(1);

        // 3×4 旋转90°后变为 4×3（宽高互换）。检查新覆盖区域是否越界/被占。
        int newW = factorySize.Y, newH = factorySize.X;
        var blocked = new List<string>();

        for (int dx = 0; dx < newW; dx++)
        {
            for (int dy = 0; dy < newH; dy++)
            {
                int nx = anchor.X + dx, ny = anchor.Y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                {
                    blocked.Add($"({nx},{ny})" + TranslateGroup.T("越界"));
                    continue;
                }
                var g = map[nx, ny];
                if (g == null) { blocked.Add($"({nx},{ny})" + TranslateGroup.T("无效格")); continue; }
                // 本兵器自己占据的格子不算阻挡
                if (occupiedGrids.Contains(g)) continue;
                if (g.infantries.Count > 0) blocked.Add($"({nx},{ny})" + TranslateGroup.T("有单位"));
                if (g.city != null && IsInstanceValid(g.city)) blocked.Add($"({nx},{ny})" + TranslateGroup.T("有建筑"));
                if (g.weapons.Any(wp => IsInstanceValid(wp) && wp != this)) blocked.Add($"({nx},{ny})" + TranslateGroup.T("有兵器"));
            }
        }

        if (blocked.Count > 0)
        {
            // ✅ 旋转失败提醒
            ShowToast($"{TranslateGroup.T("旋转失败：目标位置被阻挡")} {string.Join("、", blocked.Take(3))}");
            return false;
        }

        // 执行旋转：清除旧占据，更新朝向与尺寸，重新绑定
        var oldGrids = occupiedGrids.ToList();
        foreach (var g in oldGrids)
        {
            if (g == null || !IsInstanceValid(g)) continue;
            g.weapons.Remove(this);
            if (g.weapon == this) g.weapon = g.weapons.Count > 0 ? g.weapons[0] : null;
        }
        occupiedGrids.Clear();

        factorySize = new Vector2I(newW, newH);
        size = factorySize;
        direction = (CannonDirection)(((int)direction + 1) % 4);

        // 精灵重新对中到新足印中心（防旋转后贴图与格子错位）
        if (animSprite != null)
        {
            var center = new Vector2(newW * 16, newH * 16);
            animSprite.Position = center;
            var dots = GetNodeOrNull<AnimatedSprite2D>("YellowDots");
            if (dots != null) dots.Position = center;
        }

        // 重新绑定新区域
        var newGrids = new List<Grids>();
        for (int dx = 0; dx < newW; dx++)
        {
            for (int dy = 0; dy < newH; dy++)
            {
                int nx = anchor.X + dx, ny = anchor.Y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                var g = map[nx, ny];
                if (g == null) continue;
                g.weapons.Add(this);
                if (g.weapon == null) g.weapon = this;
                newGrids.Add(g);
            }
        }
        occupiedGrids.AddRange(newGrids);

        // 贴图朝向已由 direction 属性setter内的SetDirection设为绝对角度（此处再+=90会双重旋转——修）
        if (animSprite != null) UpdateMultiTileVisual();

        ShowToast(TranslateGroup.T("旋转成功"));
        return true;
    }

    /// <summary>简单浮动提示（旋转成功/失败提醒）</summary>
    private void ShowToast(string message)
    {
        var label = new Label();
        label.Text = message;
        label.Position = GlobalPosition + new Vector2(-60, -20);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.3f));
        label.AddThemeConstantOverride("outline_size", 4);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeFontSizeOverride("font_size", 14);
        label.ZIndex = 100;
        GetTree().Root.AddChild(label);
        var tween = label.CreateTween();
        tween.TweenProperty(label, "position", label.Position + new Vector2(0, -40), 1.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(label, "modulate:a", 0f, 1.2f);
        tween.TweenCallback(Callable.From(() => { if (IsInstanceValid(label)) label.QueueFree(); }));
    }

    // ========== 大贴图位置：3×4 中心 ==========
    public override void UpdateMultiTileVisual()
    {
        if (animSprite != null)
        {
            animSprite.Position = new Vector2(
                16 + (factorySize.X - 1) * 16f,
                16 + (factorySize.Y - 1) * 16f
            );
        }
        var dotsSync = GetNodeOrNull<AnimatedSprite2D>("YellowDots");
        if (dotsSync != null) dotsSync.Position = new Vector2(
            16 + (factorySize.X - 1) * 16f,
            16 + (factorySize.Y - 1) * 16f
        );
    }

    // ========== 生产成本计算（双生产菜单模式） ==========
    /// <summary>清理已死亡的待机单位（用于上限判定）</summary>
    private void PruneStandbyUnits()
    {
        standbyUnits.RemoveAll(u => u == null || !IsInstanceValid(u) || u.IsQueuedForDeletion());
    }

    /// <summary>计算生产成本：红利期免费；原作模式免费；双菜单模式按倍率</summary>
    public int GetProductionCost(int baseCost, bool instantMode)
    {
        // 红利期免费
        if (IsInDividend(TeamHelper.GetPhaseTeamName((GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager)?.turnPhase ?? 1)))
            return 0;
        // 原作模式：免费
        if (productionFree && !dualProductionMode) return 0;
        // 双生产菜单模式
        if (dualProductionMode)
        {
            float mult = instantMode ? instantCostMultiplier : standbyCostMultiplier;
            return Mathf.RoundToInt(baseCost * mult);
        }
        return baseCost;
    }

    // ========== 执行生产：在指定出口格生成单位 ==========
    /// <summary>
    /// 生产一个单位到出口格。
    /// instantMode=true：即刻行动（原作/红利/即时菜单）；false：待机（双菜单待机档，便宜）
    /// 返回失败原因（空串=成功）
    /// </summary>
    public string ProduceUnitAt(string unitName, Grids exitGrid, bool instantMode, string payingTeam)
    {
        if (!CanProduceNow()) return TranslateGroup.T("本回合生产次数已用完");
        var exitsCheck = GetProductionExitGrids();
        int portIdx = exitsCheck.IndexOf(exitGrid);
        if (portIdx >= 0 && portIdx < portUsedThisTurn.Length && portUsedThisTurn[portIdx])
            return TranslateGroup.T("该生产口本回合已用");
        if (exitGrid == null || !IsInstanceValid(exitGrid)) return TranslateGroup.T("无效出口格");
        if (exitGrid.infantries.Count > 0 || exitGrid.weapons.Count > 0) return TranslateGroup.T("出口格被占用");

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return "no gm";

        if (!UnitProductionDatabase.HasUnit(unitName)) return TranslateGroup.T("未知单位");
        var info = UnitProductionDatabase.GetInfo(unitName);

        // ✅ Lab白名单：仅当本建筑isUpdatable=true时，生产受全局白名单限制
        if (isUpdatable && !UnitProductionWhitelist.CanProduce(unitName))
            return TranslateGroup.T("该单位需先占领实验室解锁");

        // ✅ 待机上限：规格"0.8倍资金生产待机单位（最大三个）"
        if (!instantMode && dualProductionMode)
        {
            PruneStandbyUnits();
            if (standbyUnits.Count >= maxStandbyUnits)
                return TranslateGroup.T($"待机单位已达上限（{maxStandbyUnits}个）");
        }

        // 费用
        int cost = GetProductionCost(info.Cost, instantMode);
        int funds = gm.GetTeamFunds(payingTeam);
        if (cost > 0 && funds < cost) return TranslateGroup.T("资金不足");

        // 生成
        var scene = GD.Load<PackedScene>(info.ScenePath);
        if (scene == null) return "scene null";
        var unit = scene.Instantiate<Infantry>();
        if (unit == null) return "instantiate null";

        unit.Position = exitGrid.Position;
        unit.team = payingTeam;
        int unitNumber = gm.unitManager != null ? gm.unitManager.AllUnits.Count + 1 : 0;
        unit.Name = $"{payingTeam}_{unitName}_{unitNumber}";

        gm.unitManager.units.AddChild(unit);
        bool bound = gm.unitManager.BindUnitToGrid(unit, true);
        if (!bound)
        {
            unit.QueueFree();
            return TranslateGroup.T("出口格绑定失败");
        }
        gm.unitManager.AllUnits.Add(unit);
        gm.RegisterUnit(unit, gm.InferCategory(unit));
        unit.OnClickPiece = gm.OnSelectPiece;
        unit.UpdateTeamVisual();
        unit.UpdateHpLabel();
        if (unit.sprite != null) unit.StartBreath();

        // 即时/待机
        bool instant = dualProductionMode ? instantMode : (instantMode || instantMoveAfterProduction);   // 双菜单：待机档真待机
        if (!instant)
        {
            unit.isMoved = true;
            unit.isAttacked = true;
            unit.hasActed = true;
            unit.state = UnitState.Acted;
            unit.SetWaitVisual(true);
            // ✅ 计入待机单位列表（用于上限判定）
            if (dualProductionMode) standbyUnits.Add(unit);
        }

        productionsThisTurn++;
        var exitsNow = GetProductionExitGrids();
        int usedPort = exitsNow.IndexOf(exitGrid);
        if (usedPort >= 0 && usedPort < portUsedThisTurn.Length) portUsedThisTurn[usedPort] = true;   // 记口账（旋转后按索引存续）

        // 红利消耗
        if (IsInDividend(payingTeam) && dividendUnitsRemaining > 0)
            dividendUnitsRemaining--;

        // 扣费（绑定成功后才扣，失败不白扣钱）
        if (cost > 0) gm.SetTeamFunds(payingTeam, gm.GetTeamFunds(payingTeam) - cost);

        gm.UpdateUnitLists();
        return "";
    }

    // ========== 兵器不攻击 ==========
    public override bool CanAttack() => false;
    public override void PerformAttack(Infantry target) { }
    public override void RotateDirection()
    {
        // 走带碰撞检查的旋转
        RotateFactoryClockwise();
    }

    // ========== 免疫一切伤害（原作：包括导弹井和CO技能）==========
    public override void TakeDamage(int damage)
    {
        // 原作 Factory 免疫一切伤害——失效只能靠摧毁管道接缝；开启血量Export后才可被攻击（开创人规矩）
        if (!hasHealth) return;
        base.TakeDamage(damage);
    }

    // ========== 摧毁（编辑器删除/脚本）：通知 ==========
    public override void OnDestroyed()
    {
        OnMultiTileDestroyed();
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.gridManager?.ClearWeaponRange();
        Grids.IsForceActionMode = false;
        if (gm != null && gm.selectedWeapon == this) gm.selectedWeapon = null;

        var area = GetNodeOrNull<Area2D>("Area2D");
        if (area != null)
        {
            area.InputPickable = false;
            area.Monitoring = false;
            area.Monitorable = false;
        }
        if (hpLabel != null) hpLabel.Hide();
        gm?.CheckVictoryCondition();
    }

    public override string GetBrokenAnimName() => "SuperFactoryBroken";

    public override string GetAnimName() => GetFactoryAnimName();

    // 势力变更/接管时重选贴图动画（供能联动切中立、中转口接管都走这里）
    public override void RefreshTeamVisual()
    {
        base.RefreshTeamVisual();
        if (animSprite != null && animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation(GetFactoryAnimName()))
            animSprite.Play(GetFactoryAnimName());
        var anim = GetFactoryAnimName();
        var spr = animSprite != null ? animSprite : GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (spr != null)
        {
            // 贴图规矩：橙星/蓝月=烘焙原色不叠材质；中立皮=LUT随势力换色（材质贴在真正显示的节点上）
            if (anim == "Neutral")
            {
                // ✅ 中立皮=纯灰模（实测 R=G=B=126，红色相像素 0%）——必须走 mode3 灰度重着色；
                //    GetMaterial 是 mode2 模板色表（抓红/蓝模板色相像素），对灰模零作用=灰模原样输出
                var mat = FactionPaletteManager.GetGrayscaleMaterial(team);
                spr.Material = mat != null ? (ShaderMaterial)mat.Duplicate() : null;
            }
            else spr.Material = null;
        }
        var dots = GetNodeOrNull<AnimatedSprite2D>("YellowDots");
        if (dots != null) dots.Visible = anim == "Neutral";   // 换势力时同步黄灯层显隐
    }

    private string lastVisualTeam = null;

    public override void _Process(double delta)
    {
        base._Process(delta);
        // 编辑器/运行时直接改 team 字段也立即换皮（对齐 SetDirection 即改即转；官方通道 SetFactoryTeam 已自带刷新）
        if (IsNodeReady() && team != lastVisualTeam)
        {
            lastVisualTeam = team;
            RefreshTeamVisual();
        }
    }

    /// <summary>按队伍选贴图动画（橙/蓝/绿/黄/中立/黑洞六套原版色，无需 Shader 染色）</summary>
    public string GetFactoryAnimName()
    {
        return team switch
        {
            TeamHelper.Player1 => "OrangeStar",   // 开创人规矩：P1=橙星兵工厂
            TeamHelper.Player2 => "BlueMoon",     // P2=蓝月兵工厂
            _ => "Neutral"                          // 其余含中立=中立灰模走LUT换色（黄灯层不换色）
        };
    }

    // ========== 信息面板 ==========
    public override string GetBlackCannonFullInfo()
    {
        string info = base.GetBlackCannonFullInfo();
        info = info.Replace(TranslateGroup.T("[黑炮]"), TranslateGroup.T("[超级兵工厂]"));
        info += $"\n=== {TranslateGroup.T("超级兵工厂机制")} ===\n";
        info += $"{TranslateGroup.T("尺寸")}: {factorySize.X}×{factorySize.Y} ({TranslateGroup.T("生产口朝")}{direction})\n";
        info += $"{TranslateGroup.T("供能状态")}: {(lostPower ? TranslateGroup.T("已断电（中立废铁）") : TranslateGroup.T("正常"))}\n";
        info += $"{TranslateGroup.T("生产")}: {(productionEnabled && !lostPower ? $"{productionsThisTurn}/{maxProductionsPerTurn}" : TranslateGroup.T("关闭"))} {(productionFree && !dualProductionMode ? TranslateGroup.T("（免费·原作模式）") : dualProductionMode ? TranslateGroup.T("（双生产菜单模式）") : "")}\n";
        if (IsInDividend(dividendTeam) && dividendUnitsRemaining > 0)
            info += $"{TranslateGroup.T("缴获红利")}: {dividendUnitsRemaining}{TranslateGroup.T("个免费即刻单位")} ({dividendRemainingTurns}{TranslateGroup.T("回合")})\n";
        if (linkedSeamOffsets.Count > 0)
            info += $"{TranslateGroup.T("联动接缝")}: {linkedSeamOffsets.Count}{TranslateGroup.T("处")}\n";
        info += $"{TranslateGroup.T("旋转")}: {(factoryCanRotate ? TranslateGroup.T("可旋转（90/180/270）") : TranslateGroup.T("锁定"))}\n";
        return info;
    }

    // ========== AI 兵器操作（全适配：中立/玩家方走自带AI，AI阵营由 AI_Manager 反制选兵生产）==========

    public override void ExecuteAI()
    {
        // 自带AI只服务 P0/P-1（中立）与玩家方工厂；AI阵营工厂由 AI_Manager.OperateFactory 反制选兵生产
        if (!TeamHelper.ShouldWeaponUseAI(team) && !TeamHelper.ShouldPlayerWeaponUseAI(3, team))
            return;
        // 简易策略：能产就产，免费生产优先最高造价（价值最大化）
        while (CanProduceNow())
        {
            Grids exit = null;
            foreach (var e in GetProductionExitGrids())
                if (e != null && IsInstanceValid(e) && e.infantries.Count == 0 && e.weapons.Count == 0) { exit = e; break; }
            if (exit == null) break;   // 生产口全被占住
            var names = producibleUnitNames.Count > 0 ? producibleUnitNames.ToList()
                                                       : new List<string>(UnitProductionDatabase.GetAllUnitNames());
            string pick = null; int bestScore = int.MinValue;
            foreach (var n in names)
            {
                if (!UnitProductionDatabase.HasUnit(n)) continue;
                if (isUpdatable && !UnitProductionWhitelist.CanProduce(n)) continue;
                int c = UnitProductionDatabase.GetInfo(n).Cost;
                if (c > bestScore) { bestScore = c; pick = n; }
            }
            if (pick == null) break;
            string err = ProduceUnitAt(pick, exit, instantMoveAfterProduction, team);
            if (!string.IsNullOrEmpty(err)) break;
        }
    }
}
