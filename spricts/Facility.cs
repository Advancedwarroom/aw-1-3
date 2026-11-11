using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// ========== 建筑状态定义 ==========
public enum FacilityStatusType
{
    None,           // 无状态
    CannotProduce,  // 不可生产
    NoIncome,       // 无收入
    NoCover,   // 无掩体（防御加成归零）
    NoSupply   // 无物资（无法补给）
}

public partial class FacilityStatus
{
    public FacilityStatusType Type { get; set; }
    public int RemainingTurns { get; set; }  // 剩余回合数，-1=永久
    public string AppliedByTeam { get; set; }  // 由哪个势力施加/关联

    public bool IsExpired => RemainingTurns == 0;
    public bool IsPermanent => RemainingTurns < 0;

    public void Tick()
    {
        if (RemainingTurns > 0)
            RemainingTurns--;
    }

    public string GetDisplayName()
    {
        return Type switch
        {
            FacilityStatusType.CannotProduce => TranslateGroup.T("不可生产"),
            FacilityStatusType.NoIncome => TranslateGroup.T("无收入"),
            FacilityStatusType.NoCover => TranslateGroup.T("无掩体"),
            FacilityStatusType.NoSupply => TranslateGroup.T("无物资"),
            _ => TranslateGroup.T("未知")
        };
    }
}

public partial class Facility : Node2D
{
    public Grids ParentGrid { get; private set; }

    // ✅ 双图层视觉组件（设施/建筑染色用）
    [Export] public AnimatedSprite2D animSprite;
    [Export] public AnimatedSprite2D baseAnimSprite;
    [Export] public AnimatedSprite2D normalAnimSprite;
    [Export] public Sprite2D baseSprite;
    [Export] public Sprite2D normalSprite;

    [ExportGroup("生产模块")]
    [Export] public bool canProduce = false;
    [Export] public Godot.Collections.Array<string> producibleUnitNames = new();
    [ExportGroup("生产模块扩展")]
    [Export] public bool productionFree = false;                 // ⑫ 是否无消耗
    [Export] public bool dualProductionMode = false;             // 双生产菜单（每单位 立即/待机 两按钮）
    [Export] public float instantCostMultiplier = 1.2f;          // 立即单位资金倍率
    [Export] public float standbyCostMultiplier = 0.8f;          // 待机单位资金倍率

    [Export] public bool instantMoveAfterProduction = false;
    [Export] public int maxProductionsPerTurn = 1;
    [Export] public int productionsThisTurn = 0;

    // ========== 产能绑定（开创人2026-11-11）：实际血量+最高占领度 双轴平均 → 产能/出厂血量/出厂燃油 ==========
    [ExportGroup("产能绑定（血量/占领度）")]
    [Export] public bool productionHealthBound = false;      // 实际血量绑定（用当前health，非maxHealth）
    [Export] public bool productionCaptureBound = false;     // 最高占领度绑定（GetCurrentCaptureState() 实际当前值：刚占领完临时低值，逐回合回满）
    [Export] public float productionSlotsPerPoint = 1f;      // 每点平均值=几个生产名额（1=一滴血一名额）
    [Export] public int productionMinSlots = 0;              // 产能下限（残血保底）
    [Export] public int productionUnitMinHp = 1;             // 出厂血量下限（不至于出0血单位；Health资源未配Floor时的默认值）
    [Export] public int productionUnitMinFuel = 0;           // 出厂燃油下限（Fuel资源未配Floor时的默认值）

    // —— 出厂资源缩放·逐资源独立（开创人2026-11-12）：11资源=Health/Fuel+9弹种(Primary/Explode/Flare/StealthBullet/RevealBullet/CamoBullet/Production/Construction/Special) ——
    [Export] public Godot.Collections.Dictionary<string, int> productionVigorResMode = new();    // 资源名→模式：0=百分比(默认) 1=固定加减
    [Export] public Godot.Collections.Dictionary<string, int> productionVigorResValue = new();   // 资源名→数值：百分比=亏损度×基数×值/100；固定=亏损度×值。正=扣 负=倒加（残厂出超编资源）
    [Export] public Godot.Collections.Dictionary<string, int> productionVigorResFloor = new();   // 资源名→该资源下限（未配置：血量=productionUnitMinHp，燃油=productionUnitMinFuel，弹药=0）
    [Export] public Godot.Collections.Dictionary<string, bool> productionVigorResExceedMax = new(); // 资源名→可否超上限（默认false=倒加也钳在原max；true=允许超过原上限）
    [Export] public Godot.Collections.Dictionary<string, int> productionVigorResNewCap = new();   // 资源名→超编时的替代上限；可超且本项不填=无上限（显式-1也=无上限）

    // ========== 状态栏系统 ==========
    [ExportGroup("初始状态（运行时生效）")]
    [Export] public Godot.Collections.Array<FacilityStatusData> initialStatuses = new();
    
    // 运行时状态列表（不Export）
    private List<FacilityStatus> activeStatuses = new();

    // 状态栏UI引用（运行时创建）
    private VBoxContainer statusBarContainer;
    private Dictionary<FacilityStatusType, Label> statusLabels = new();

    /// <summary>产能活力 0~1：各绑定源归一化后取平均（血量+占领度并存不矛盾，就是加一块除以二；都没开=满产能）</summary>
    public float GetProductionVigor()
    {
        float sum = 0f; int n = 0;
        if (productionHealthBound)  { sum += (float)health / Mathf.Max(1, maxHealth); n++; }
        if (productionCaptureBound) { sum += (float)GetCurrentCaptureState() / Mathf.Max(1, capturePointsRequired); n++; }
        return n == 0 ? 1f : sum / n;
    }

    /// <summary>有效产能（每回合生产次数上限）：绑定开启时=平均值原始点×每点名额（按下限托底）；未开启=maxProductionsPerTurn</summary>
    public int GetEffectiveMaxProductions()
    {
        if (!productionHealthBound && !productionCaptureBound) return maxProductionsPerTurn;
        float sum = 0f; int n = 0;
        if (productionHealthBound)  { sum += health; n++; }
        if (productionCaptureBound) { sum += GetCurrentCaptureState(); n++; }
        return Mathf.Max(productionMinSlots, Mathf.FloorToInt(sum / n * productionSlotsPerPoint));
    }

    public bool CanProduceNow() => canProduce && productionsThisTurn < GetEffectiveMaxProductions() && !HasStatus(FacilityStatusType.CannotProduce);

    public void ResetProductionCount()
    {
        productionsThisTurn = 0;
        TickStatuses();
        UpdateStatusBarUI();
    }

    [ExportGroup("设施基础")]
    /// <summary>✅ 是否更新：填"是"则本建筑的生产受全局"可生成单位白名单"限制（需Lab解锁）。
    /// 填"否"则与Lab无关（不受白名单限制）。原作所有建筑模板默认"是"(true)。
    /// 编辑模式放置、单位使用均不受影响——仅限制本建筑的【生产】。</summary>
    [Export] public bool isUpdatable = true;
    [Export] public string facilityTeam = "";

    // ✅ 开创人2026-11-10：存活胜负倒计时（全部设施通用，默认关闭=0；与单位/兵器同机制同字段名）
    [ExportGroup("存活胜负倒计时")]
    [Export] public int surviveVictoryTurns = 0;   // 本设施存活满 N 大回合 → 所属势力直接胜利（0=关闭）
    [Export] public int surviveDefeatTurns = 0;    // 本设施存活满 N 大回合 → 所属势力失败出局（0=关闭）
    [Export] public int survivedTurns = 0;         // 已存活大回合数（计时器；[Export] 随存档/导出/导入自动带走）
    [Export] public int capturePointsRequired = 20;
    [Export] public int healMode = 0;
    [Export] public int healAmount = 0;
    [Export] public float healPercent = 0.0f;

    [ExportGroup("补给配置：数值驱动")]
    [Export] public int flareAmmoSupply = 0;
    [Export] public int explodeAmmoSupply = 0;
    [Export] public int primaryAmmoSupply = 0;
    [Export] public int fuelSupply = 0;
    [Export] public int productionAmmoSupply = 0;     // ✅ 新弹药：生产弹补给（999=补满）
    [Export] public int constructionAmmoSupply = 0;   // ✅ 新弹药：建造弹补给（999=补满）
    [Export] public int specialAmmoSupply = 0;      // ✅ 第11资源：特殊弹补给（999=补满；useSpecialAmmo=false的单位不收）

    [ExportGroup("补给支持兵种过滤（为空=不限制）")]
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForHeal = new();
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForFlare = new();
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForExplode = new();
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForPrimaryAmmo = new();
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForFuel = new();
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForProductionAmmo = new();
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForConstructionAmmo = new();
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForSpecialAmmo = new();

    // ========== P-1设施收入共享配置 ==========
    [ExportGroup("P-1公共设施配置")]
    [Export] public bool isPublicFacility = false;
    [Export] public int fundsPerTurn = 1000;
    [Export] public bool provideIncomeToAllTeams = false;

    // ========== 通行控制三件套（照兵器同构，开创人2026-11-11）==========
    // allowAllPass 默认 true=AW 经典（单位可站上城市/基地）；false=Warside 模式（建筑正上方不能呆着）。
    [ExportGroup("通行控制")]
    [Export] public bool movementBlockIncludeFriendly = true;
    [Export] public bool movementBlockIncludeEnemy = true;
    [Export] public bool movementBlockIncludeAllied = true;
    [Export] public bool allowAllPass = true;
    [Export] public string[] movementBlockWhitelist = System.Array.Empty<string>();

    // ========== 远程占领（只看设施判定，不看单位）==========
    [ExportGroup("远程占领")]
    [Export] public bool remoteCaptureEnabled = false;   // 开关：关=现状必须站上
    [Export] public int captureRangeMin = 0;             // n-m 环带
    [Export] public int captureRangeMax = 0;

    // ========== 远程生产落点 ==========
    [ExportGroup("远程生产落点")]
    [Export] public bool remoteProductionEnabled = false; // 开关：关=现状直接生产不选点
    [Export] public int productionDropMinRange = 0;       // n-m 环带
    [Export] public int productionDropMaxRange = 0;

    // ========== 占领后状态（Warside：刚占完只有 m，每回合 +n 回满）==========
    [ExportGroup("占领后状态")]
    [Export] public int captureStateAfterCapture = -1;    // -1=回满(现状)；>=0=占领完成后新归属防御值（如 2）
    [Export] public int captureStateRegenPerTurn = 0;     // 每回合回复量（如 2）；0=不回

    public int currentCaptureState = -1;                  // 运行时：当前占领防御值；-1=惰性满值
    /// 当前占领防御值：未初始化时视为满值 capturePointsRequired。
    public int GetCurrentCaptureState()
        => currentCaptureState < 0 ? capturePointsRequired : System.Math.Min(currentCaptureState, capturePointsRequired);
    /// 每回合回复（由势力相位开始统一调用）。
    public void RegenerateCaptureState()
    {
        if (captureStateRegenPerTurn <= 0 || currentCaptureState < 0) return;
        currentCaptureState = System.Math.Min(capturePointsRequired, currentCaptureState + captureStateRegenPerTurn);
    }

    public List<Infantry> capturingUnits = new();
    private CaptureContestEffect contestEffect;

    public override void _Ready()
    {
        ZIndex = 5;
        ParentGrid = GetParent() as Grids;
        if (ParentGrid == null)
        {
            GD.PushError(TranslateGroup.T("[Facility] Facility 节点必须是 Grids 的子节点！"));
            return;
        }

        if (facilityTeam == TeamHelper.Player)
        {
            isPublicFacility = true;
            provideIncomeToAllTeams = true;
        }

        // ========== 初始化初始状态 ==========
        foreach (var data in initialStatuses)
        {
            if (data != null)
            {
                activeStatuses.Add(new FacilityStatus
                {
                    Type = data.Type,
                    RemainingTurns = data.RemainingTurns,
                    AppliedByTeam = data.AppliedByTeam
                });
            }
        }

        // ✅ 视觉组件自动获取
        if (animSprite == null)
            animSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (animSprite == null)
            animSprite = GetNodeOrNull<AnimatedSprite2D>("Base2D");
        if (baseAnimSprite == null)
            baseAnimSprite = GetNodeOrNull<AnimatedSprite2D>("BaseAnimSprite");
        if (baseAnimSprite == null)
            baseAnimSprite = GetNodeOrNull<AnimatedSprite2D>("BaseAnimatedSprite");
        if (normalAnimSprite == null)
            normalAnimSprite = GetNodeOrNull<AnimatedSprite2D>("NormalAnimSprite");
        if (normalAnimSprite == null)
            normalAnimSprite = GetNodeOrNull<AnimatedSprite2D>("NormalAnimatedSprite");
        // ✅ 开创人2026-11-11: 递归兜底——四件套场景把覆盖层挂在BaseAnimSprite子级，直接子级找不到会永久掉回旧单层路（Port能紫纯侥幸）
        if (normalAnimSprite == null && baseAnimSprite != null)
            normalAnimSprite = baseAnimSprite.GetNodeOrNull<AnimatedSprite2D>("NormalAnimSprite");
        if (baseSprite == null)
            baseSprite = GetNodeOrNull<Sprite2D>("BaseSprite");
        if (normalSprite == null)
            normalSprite = GetNodeOrNull<Sprite2D>("NormalSprite");
        if (normalSprite == null && baseSprite != null)
            normalSprite = baseSprite.GetNodeOrNull<Sprite2D>("NormalSprite");

        // ✅ 开创人2026-11-11: 底图色相运行时自测（自测优先，手写锚点兜底）
        MeasureBaseArtHue();

        // 设施是像素画：显式锁定最近邻，避免父节点/Viewport 的线性过滤把小灯点糊开。
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        ForceNearestTextureFilter(animSprite);
        ForceNearestTextureFilter(baseAnimSprite);
        ForceNearestTextureFilter(normalAnimSprite);
        ForceNearestTextureFilter(baseSprite);
        ForceNearestTextureFilter(normalSprite);
        
        // ✅ 双图层 Sprite2D：把 Normal 层挂到 Base 层下，继承所有变换
        if (baseSprite != null && normalSprite != null && normalSprite.GetParent() != baseSprite)
        {
            normalSprite.Reparent(baseSprite, true);
        }
        // ✅ 双图层 AnimatedSprite2D：同理
        if (baseAnimSprite != null && normalAnimSprite != null && normalAnimSprite.GetParent() != baseAnimSprite)
        {
            normalAnimSprite.Reparent(baseAnimSprite, true);
        }

        UpdateCityVisual();
        UpdateHpLabel();
        CreateStatusBarUI();
    }

    // ========== 状态栏系统核心方法 ==========

    public void AddStatus(FacilityStatusType type, int durationTurns = -1, string appliedByTeam = "")
    {
        var existing = activeStatuses.FirstOrDefault(s => s.Type == type);
        if (existing != null)
        {
            existing.RemainingTurns = durationTurns;
            existing.AppliedByTeam = appliedByTeam;
        }
        else
        {
            activeStatuses.Add(new FacilityStatus
            {
                Type = type,
                RemainingTurns = durationTurns,
                AppliedByTeam = appliedByTeam
            });
        }
        UpdateStatusBarUI();
    }

    public void RemoveStatus(FacilityStatusType type)
    {
        activeStatuses.RemoveAll(s => s.Type == type);
        UpdateStatusBarUI();
    }

    public bool HasStatus(FacilityStatusType type)
    {
        return activeStatuses.Any(s => s.Type == type && !s.IsExpired);
    }

    public int GetStatusRemainingTurns(FacilityStatusType type)
    {
        var status = activeStatuses.FirstOrDefault(s => s.Type == type && !s.IsExpired);
        return status?.RemainingTurns ?? 0;
    }

    private void TickStatuses()
    {
        foreach (var status in activeStatuses.ToList())
        {
            status.Tick();
            if (status.IsExpired)
            {
                activeStatuses.Remove(status);
                GD.Print($"[Facility] {ParentGrid?.Name} 的状态 '{status.GetDisplayName()}' 已过期");
            }
        }
    }

    private void CreateStatusBarUI()
    {
        if (ParentGrid == null) return;

        statusBarContainer = new VBoxContainer();
        statusBarContainer.Name = "StatusBar";
        statusBarContainer.ZIndex = 300;
        statusBarContainer.Position = new Vector2(-20, -40);
        statusBarContainer.Size = new Vector2(40, 0);
        statusBarContainer.AddThemeConstantOverride("separation", 2);

        AddChild(statusBarContainer);
        UpdateStatusBarUI();
    }

    private void UpdateStatusBarUI()
    {
        if (statusBarContainer == null) return;

        foreach (var child in statusBarContainer.GetChildren())
            child.QueueFree();
        statusLabels.Clear();

        if (activeStatuses.Count == 0)
        {
            statusBarContainer.Hide();
            return;
        }

        statusBarContainer.Show();

        foreach (var status in activeStatuses.Where(s => !s.IsExpired))
        {
            var label = new Label();
            label.Name = $"Status_{status.Type}";
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.AddThemeFontSizeOverride("font_size", 10);

            Color statusColor = status.Type switch
            {
                FacilityStatusType.CannotProduce => new Color(1f, 0.3f, 0.3f),
                FacilityStatusType.NoIncome => new Color(0.8f, 0.8f, 0.2f),
                _ => Colors.White
            };

            label.AddThemeColorOverride("font_color", statusColor);

            string durationText = status.IsPermanent ? "" : $" ({status.RemainingTurns})";
            label.Text = $"{status.GetDisplayName()}{durationText}";

            var bg = new StyleBoxFlat();
            bg.BgColor = new Color(0.1f, 0.1f, 0.1f, 0.85f);
            bg.SetCornerRadiusAll(4);
            label.AddThemeStyleboxOverride("normal", bg);

            statusBarContainer.AddChild(label);
            statusLabels[status.Type] = label;
        }

        statusBarContainer.CustomMinimumSize = new Vector2(40, activeStatuses.Count * 16);
    }

    // ========== 生产权限判定（支持P-1轮流生产） ==========

    public bool CanBeProducedBy(string team)
    {
        if (!canProduce || string.IsNullOrEmpty(facilityTeam)) return false;
        if (facilityTeam == TeamHelper.Player0) return false;

        if (facilityTeam == TeamHelper.Player || isPublicFacility)
        {
            var cannotProduceStatus = activeStatuses.FirstOrDefault(s => 
                s.Type == FacilityStatusType.CannotProduce && 
                !s.IsExpired &&
                s.AppliedByTeam == team);

            if (cannotProduceStatus != null)
                return false;

            return productionsThisTurn < maxProductionsPerTurn;
        }

        return facilityTeam == team;
    }

    public void OnUnitProducedBy(string team)
    {
        productionsThisTurn++;

        if ((facilityTeam == TeamHelper.Player || isPublicFacility) && team != TeamHelper.Player0)
        {
            AddStatus(FacilityStatusType.CannotProduce, durationTurns: 1, appliedByTeam: team);
            GD.Print($"[Facility] P-1公共设施 {ParentGrid?.Name} 被 {team} 使用，{team} 下回合不可再生产");
        }
    }

    public bool CanProduceUnit(string unitName)
    {
        return canProduce && producibleUnitNames.Contains(unitName);
    }

    // ========== 占领系统 ==========

    public bool CanBeCapturedBy(Infantry unit)
    {
        if (unit == null || facilityTeam == unit.team) return false;
        return true;
    }

    public void OnCaptureStarted(Infantry unit)
    {
        capturingUnits.RemoveAll(u => u == null || !IsInstanceValid(u));
        if (HasContest()) ShowCaptureContestVisual();
    }

    public void OnCaptureInterrupted(Infantry unit)
    {
        capturingUnits.RemoveAll(u => u == null || !IsInstanceValid(u));
        capturingUnits.Remove(unit);
        if (capturingUnits.Count > 0)
        {
            if (capturingUnits.Select(u => u.team).Distinct().Count() > 1)
            {
                foreach (var u in capturingUnits) u.currentCaptureProgress = 0;
                ShowCaptureContestVisual();
            }
        }
        else ClearCaptureVisual();
        UpdateCaptureVisual();
    }

    public virtual void OnCaptureCompleted(Infantry unit)
    {
        foreach (var u in capturingUnits.ToList()) { if (u != unit) { u.currentCaptureProgress = 0; u.capturingGrid = null; } }
        capturingUnits.Clear();

        string oldTeam = facilityTeam;
        facilityTeam = unit.team;

        if (facilityTeam == TeamHelper.Player)
        {
            isPublicFacility = true;
            provideIncomeToAllTeams = true;
        }

        activeStatuses.Clear();

        ClearCaptureVisual();
        ShowCaptureCompletedVisual(unit.team);
        UpdateCityVisual();
        UpdateStatusBarUI();
    }

    public bool HasContest()
    {
        if (ParentGrid == null) return false;
        var capturing = ParentGrid.infantries
            .Where(u => u.currentCaptureProgress > 0 && u.capturingGrid == ParentGrid)
            .ToList();
        capturing.RemoveAll(u => u == null || !IsInstanceValid(u));
        if (capturing.Count < 2) return false;
        return capturing.Select(u => u.team).Distinct().Count() > 1;
    }

    // ========== 视觉系统 ==========

    public virtual void UpdateCityVisual()
    {
        PlayTeamAnimation();
        ApplyPaletteMaterial();
    }

    /// <summary>
    /// 各设施的三套原版动画名：中立 / 红色模板(P1) / 蓝色模板(P2)。返回 null 表示单套贴图不切换。
    /// 其它势力（P-1、P3…P100）一律使用红色模板动画，由调色板 Shader 按势力主色染色。
    /// </summary>
    protected virtual (string neutral, string red, string blue)? GetTeamAnimNames() => null;

    // ✅ 按阵营取图钩子：默认旧三路（全家设施零变化）；多色烘焙的设施 override 后走 TeamHelper.PickArtByFactionColor
    protected virtual string PickArtForTeam(string neutral, string red, string blue)
        => TeamHelper.PickTeamArt(facilityTeam, neutral, red, blue);

    protected void PlayTeamAnimation()
    {
        var names = GetTeamAnimNames();
        if (names == null) return;
        var (neutral, red, blue) = names.Value;
        string anim = PickArtForTeam(neutral, red, blue);

        // Legacy single-layer sprite is optional. A missing animation here must not abort
        // playback on the independent Base/Normal layers below.
        if (animSprite != null && IsInstanceValid(animSprite) && animSprite.SpriteFrames != null)
        {
            var frames = animSprite.SpriteFrames;
            string singleLayerAnim = anim;
            if (!frames.HasAnimation(singleLayerAnim))
                singleLayerAnim = frames.HasAnimation(red) ? red : neutral;
            if (frames.HasAnimation(singleLayerAnim))
            {
                if (animSprite.Animation != singleLayerAnim || !animSprite.IsPlaying())
                    animSprite.Play(singleLayerAnim);
                animSprite.Modulate = Colors.White;
            }
        }

        // Drive Base and Normal independently; each layer has its own safe animation fallback.
        PlayOnLayer(baseAnimSprite, anim, neutral, red);
        PlayOnLayer(normalAnimSprite, anim, neutral, red);
    }

    private static void PlayOnLayer(AnimatedSprite2D layer, string anim, string neutral, string red)
    {
        if (layer == null || !IsInstanceValid(layer) || layer.SpriteFrames == null) return;
        var f = layer.SpriteFrames;
        if (!f.HasAnimation(anim)) anim = f.HasAnimation(red) ? red : neutral;
        if (!f.HasAnimation(anim)) return;
        if (layer.Animation != anim || !layer.IsPlaying()) layer.Play(anim);
    }
    

    // Hue-only recoloring: preserve each source pixel's saturation, value and alpha.
    // Facility base art normalized to 0° red baseline (S/V preserved). Hue shift is pure.
    private static Shader _hueShader;
    private static void ForceNearestTextureFilter(CanvasItem item)
    {
        if (item != null && GodotObject.IsInstanceValid(item))
            item.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
    }

    // Source hue of this facility's base artwork. Override only for assets with a different
    // source palette (for example TempPort's cyan artwork).
    protected virtual float GetBaseArtHue() => 0f;  // 2026-11-11: base art normalized to 0° red baseline

    // ✅ 开创人2026-11-11: 底图色相运行时自测。手写锚点曾出现 300° 级灾难性错误
    // （City 写 1° 实测 311°、Base 写 0.7° 实测 305°——昨晚"归一化"把底图转成品红却按红基线登记）。
    // 自测优先：_Ready 时从将被染色的底图层真实纹理采样高饱和众数色相；手写值降级为兜底。
    private float? _measuredBaseHue = null;

    private float EffectiveBaseArtHue() => _measuredBaseHue ?? GetBaseArtHue();

    private void MeasureBaseArtHue()
    {
        try
        {
            var frames = baseAnimSprite != null && IsInstanceValid(baseAnimSprite) ? baseAnimSprite.SpriteFrames
                       : animSprite != null && IsInstanceValid(animSprite) ? animSprite.SpriteFrames
                       : null;
            if (frames == null) return;
            // 只测"红色模板"动画——它是重染色的源画；混入 P0/蓝模会污染测量
            string probe = null;
            var names = GetTeamAnimNames();
            if (names.HasValue && frames.HasAnimation(names.Value.red)) probe = names.Value.red;
            else
            {
                foreach (var animName in frames.GetAnimationNames()) { probe = animName; break; }
            }
            if (probe == null) return;
            var tex = frames.GetFrameTexture(probe, 0);
            var img = tex?.GetImage();
            if (img == null) return;
            var tallies = new System.Collections.Generic.Dictionary<int, int>();
            for (int y = 0; y < img.GetHeight(); y += 2)
            {
                for (int x = 0; x < img.GetWidth(); x += 2)
                {
                    var c = img.GetPixel(x, y);
                    if (c.A < 0.8f) continue;
                    float h = c.H, s = c.S, v = c.V;
                    if (s < 0.45f || v < 0.35f) continue;
                    int key = ((Mathf.RoundToInt(h * 360f) % 360) + 360) % 360;
                    tallies[key] = tallies.TryGetValue(key, out var n) ? n + 1 : 1;
                }
            }
            if (tallies.Count == 0) return;
            // 色环圆形众数：±12° 包裹窗口内票数最多者
            int best = -1, bestN = 0;
            foreach (var kv in tallies)
            {
                int n = 0;
                foreach (var kv2 in tallies)
                {
                    int d = Mathf.Abs(kv.Key - kv2.Key);
                    d = Mathf.Min(d, 360 - d);
                    if (d <= 12) n += kv2.Value;
                }
                if (n > bestN) { bestN = n; best = kv.Key; }
            }
            if (best >= 0) _measuredBaseHue = best / 360.0f;
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"[Facility] 底图色相自测失败，退回手写 GetBaseArtHue: {e.Message}");
        }
    }

    protected ShaderMaterial GetHueMaterial(string team, bool guardSourceHue = false)
    {
        team = TeamHelper.CanonicalId(team);
        if (_hueShader == null || !GodotObject.IsInstanceValid(_hueShader))
            _hueShader = GD.Load<Shader>("res://asscets/shaders/hue_rotate.gdshader");
        if (_hueShader == null)
        {
            GD.PushError("[Facility] Failed to load hue_rotate.gdshader.");
            return null;
        }

        float targetHue = 0f;
        float saturation = 1f;
        float targetSat = 1f;   // ✅ 开创人2026-11-12: S/V吸附目标——P-1正典粉不再是"黑洞紫"
        float targetVal = 1f;
        if (team == TeamHelper.Player)
        {
            // P-1 is fixed canonical purple, independent of saved faction palette state.
            var canonical = new Color(0.7f, 0.4f, 0.9f);
            targetHue = canonical.H; targetSat = canonical.S; targetVal = canonical.V;
        }
        else if (team == TeamHelper.Player0)
        {
            // Neutral buildings retain their source brightness but lose most saturation.
            saturation = 0.12f;
        }
        else
        {
            var faction = TeamHelper.GetFaction(team);
            if (faction != null)
            {
                targetHue = faction.PrimaryColor.H;
                targetSat = faction.PrimaryColor.S;
                targetVal = faction.PrimaryColor.V;
            }
        }

        // Shader adds this delta to the source pixel hue. Subtract the source-art hue first;
        // sending the target hue directly would rotate pink-red 352 degrees past the target.
        float hueShift = Mathf.PosMod((targetHue - EffectiveBaseArtHue()) * 360.0f, 360.0f);  // ✅ 自测优先
        var material = new ShaderMaterial { Shader = _hueShader };
        material.SetShaderParameter("hue_shift", hueShift);
        material.SetShaderParameter("saturation", saturation);
        // ✅ 2026-11-11 单图层成品画: 只转源色带(队伍红)内像素, 其余原样——非编码画的"只染队伍色"守卫
        if (guardSourceHue)
        {
            material.SetShaderParameter("guard_center", EffectiveBaseArtHue());
            material.SetShaderParameter("guard_range", 0.10f);
        }
        // S/V吸附: 0.65折中——镶边呈现势力正典彩度, 又保留画作的明暗层次
        material.SetShaderParameter("sv_snap", 1.0f);   // ✅ 开创人2026-11-12: 正色——镶边完全吸附势力S/V
        material.SetShaderParameter("target_sat", targetSat);
        material.SetShaderParameter("target_val", targetVal);
        return material;
    }

    /// <summary>
    /// ✅ 为设施应用调色板Shader（双图层 + 兼容模式）
    /// </summary>
    protected virtual void ApplyPaletteMaterial()
    {
        // ✅ 双图层 AnimatedSprite2D 方案（优先）
        if (baseAnimSprite != null && IsInstanceValid(baseAnimSprite) && baseAnimSprite.SpriteFrames != null &&
            normalAnimSprite != null && IsInstanceValid(normalAnimSprite) && normalAnimSprite.SpriteFrames != null)
        {
            baseAnimSprite.Material = TeamHelper.GetMaterial(facilityTeam);   // ✅ 开创人2026-11-12: 接入mode2模板色表(单位同款), 明暗阶全保留
            baseAnimSprite.Show();
            normalAnimSprite.Material = null;
            normalAnimSprite.Show();
            // 隐藏旧版单图层组件
            if (animSprite != null) animSprite.Hide();
            if (baseSprite != null) baseSprite.Hide();
            if (normalSprite != null) normalSprite.Hide();
            return;
        }
        
        // ✅ 双图层 Sprite2D 方案
        if (baseSprite != null && IsInstanceValid(baseSprite) &&
            normalSprite != null && IsInstanceValid(normalSprite))
        {
            baseSprite.Material = GetHueMaterial(facilityTeam);
            baseSprite.Show();
            normalSprite.Material = null;
            normalSprite.Show();
            // 隐藏旧版单图层组件
            if (animSprite != null) animSprite.Hide();
            if (baseAnimSprite != null) baseAnimSprite.Hide();
            if (normalAnimSprite != null) normalAnimSprite.Hide();
            return;
        }
        
        // Fallback：兼容模式（单图层贴图直接染色）
        // 先隐藏双图层节点
        if (baseAnimSprite != null) baseAnimSprite.Hide();
        if (normalAnimSprite != null) normalAnimSprite.Hide();
        if (baseSprite != null) baseSprite.Hide();
        if (normalSprite != null) normalSprite.Hide();
        
        // ✅ 2026-11 设施拆雷: 城市设施全是每势力成品固定画(非CoW编码图/非红蓝模板画), mode2模板识别(mode=2)对非模板画=误判染色
        // (染花水晶同款雷; P1/P2靠IsNativeArt幸免, 其余势力全在吃误判)——原生固定画不挂 CoW LUT;
        // ✅ 2026-11-11 开创人: 单图层成品画补挂色相Shader(带源色带守卫)兑现"P-1正典紫/自定义势力染色"。
        // P-1/P3+ 播红色模板(PickTeamArt), 源色相≈GetBaseArtHue()实测校准; 守卫只转红色带内像素, 米色/灰/白不动——
        // 与拆雷针对的LUT mode2是两套机制, 不会染花。P2蓝色模板源色相≠红基线→不挂; P0中立已灰→不挂。
        // P1原生红直出保像素级保真, 仅改色后的P1挂Shader。若未来某设施接入(R,G)编码贴图, 在此改挂 FactionPaletteManager.GetCowLutMaterial(facilityTeam)
        if (animSprite != null && IsInstanceValid(animSprite))
        {
            string t = TeamHelper.CanonicalId(facilityTeam);
            bool useHueShader = t != TeamHelper.Player0 && t != TeamHelper.Player2;
            if (t == TeamHelper.Player1)
            {
                var f1 = TeamHelper.GetFaction(t);
                useHueShader = !FactionPaletteManager.IsNativeArt(t, f1);   // P1原生红: 直出保真
            }
            animSprite.Material = useHueShader ? GetHueMaterial(facilityTeam, true) : null;
            animSprite.Show();
        }
    }

    public void ShowCaptureContestVisual()
    {
        if (contestEffect != null && IsInstanceValid(contestEffect)) contestEffect.QueueFree();
        contestEffect = new CaptureContestEffect { Position = new Godot.Vector2(0, -20), ZIndex = 200 };
        AddChild(contestEffect);
    }

    public void ShowCaptureCompletedVisual(string newTeam)
    {
        ClearCaptureVisual();
        var effect = new CaptureEffect
        {
            CaptureProgress = 100f,
            TeamColor = TeamHelper.GetTeamColor(newTeam),
            IsCapturing = false, Position = new Godot.Vector2(0, -10), ZIndex = 200
        };
        AddChild(effect);
    }

    private void UpdateCaptureVisual() { }

    public void ClearContestVisual()
    {
        if (contestEffect != null && IsInstanceValid(contestEffect)) { contestEffect.QueueFree(); contestEffect = null; }
    }

    private void ClearCaptureVisual()
    {
        if (contestEffect != null && IsInstanceValid(contestEffect)) { contestEffect.QueueFree(); contestEffect = null; }
    }

    // ========== 补给系统 ==========

    public void PerformSupply()
    {
        if (ParentGrid == null) return;
        ParentGrid.infantries?.RemoveAll(i => !IsInstanceValid(i));
        bool anySupplied = false;

        foreach (var unit in ParentGrid.infantries)
        {
            if (unit == null || !IsInstanceValid(unit)) continue;
            bool canSupply = TeamHelper.CanFacilitySupply(facilityTeam, unit.team);
            if (!canSupply) continue;

            string unitType = unit.UnitTypeId;
            bool supplied = false;
            string supplyText = "";
            Color supplyColor = Colors.Green;
            var supplyParts = new List<string>();

            int healValue = 0;
            if (healMode == 0)
                healValue = healAmount;
            else if (healMode == 1)
                healValue = Mathf.RoundToInt(unit.maxHealth * healPercent);

            if (healValue != 0)
            {
                if (supportedUnitTypesForHeal.Count == 0 || supportedUnitTypesForHeal.Contains(unitType))
                {
                    int oldHealth = unit.health;
                    unit.health += healValue;
                    if (!unit.explosionCanExceedMaxHealth)
                        unit.health = Mathf.Clamp(unit.health, 1, unit.maxHealth);
                    else
                        unit.health = Mathf.Max(1, unit.health);
                    int delta = unit.health - oldHealth;
                    if (delta != 0)
                    {
                        supplied = true;
                        supplyParts.Add(delta > 0 ? $"+{delta}HP" : $"{delta}HP");
                        supplyColor = new Color(0.2f, 0.9f, 0.3f);
                    }
                }
            }

            if (primaryAmmoSupply != 0)
            {
                if (unit.hasPrimaryWeapon && unit.primaryHasLimitedAmmo && unit.maxPrimaryAmmo < 99)
                {
                    if (supportedUnitTypesForPrimaryAmmo.Count == 0 || supportedUnitTypesForPrimaryAmmo.Contains(unitType))
                    {
                        int oldAmmo = unit.currentPrimaryAmmo;
                        if (primaryAmmoSupply >= 999)
                            unit.currentPrimaryAmmo = unit.GetEffectiveMaxPrimaryAmmo();
                        else
                            unit.currentPrimaryAmmo = Mathf.Clamp(unit.currentPrimaryAmmo + primaryAmmoSupply, 0, unit.maxPrimaryAmmo);
                        unit.currentStealthBullets = unit.GetEffectiveMaxStealthBullets();   // ✅ 隐身弹补给
                        unit.currentRevealBullets = unit.GetEffectiveMaxRevealBullets();     // ✅ 显形弹补给
                        unit.currentCamoBullets = unit.GetEffectiveMaxCamoBullets();         // ✅ 迷彩弹补给
                        if (unit.currentPrimaryAmmo != oldAmmo)
                        {
                            supplied = true;
                            supplyParts.Add("Ammo+");
                            if (supplyColor == Colors.Green) supplyColor = new Color(0.9f, 0.3f, 0.2f);
                        }
                    }
                }
            }

            if (fuelSupply != 0)
            {
                if (unit.consumeFuel)
                {
                    if (supportedUnitTypesForFuel.Count == 0 || supportedUnitTypesForFuel.Contains(unitType))
                    {
                        int oldFuel = unit.fuel;
                        if (fuelSupply >= 999)
                            unit.fuel = unit.GetEffectiveMaxFuel();
                        else
                            unit.fuel = Mathf.Clamp(unit.fuel + fuelSupply, 0, unit.maxFuel);
                        if (unit.fuel != oldFuel)
                        {
                            supplied = true;
                            supplyParts.Add("Fuel+");
                            if (supplyColor == Colors.Green) supplyColor = new Color(0.9f, 0.7f, 0.2f);
                        }
                    }
                }
            }

            if (flareAmmoSupply != 0)
            {
                if (unit.canIlluminate)
                {
                    if (supportedUnitTypesForFlare.Count == 0 || supportedUnitTypesForFlare.Contains(unitType))
                    {
                        int oldFlare = unit.currentFlareAmmo;
                        if (flareAmmoSupply >= 999)
                            unit.currentFlareAmmo = unit.maxFlareAmmo;
                        else
                            unit.currentFlareAmmo = Mathf.Clamp(unit.currentFlareAmmo + flareAmmoSupply, 0, unit.maxFlareAmmo);
                        if (unit.currentFlareAmmo != oldFlare)
                        {
                            supplied = true;
                            supplyParts.Add("Flare+");
                            if (supplyColor == Colors.Green) supplyColor = new Color(0.9f, 0.9f, 0.2f);
                        }
                    }
                }
            }

            if (explodeAmmoSupply != 0)
            {
                if (unit.canExplode && !unit.explosionDestroysSelf)
                {
                    if (supportedUnitTypesForExplode.Count == 0 || supportedUnitTypesForExplode.Contains(unitType))
                    {
                        int oldExplode = unit.currentExplodeAmmo;
                        if (explodeAmmoSupply >= 999)
                            unit.currentExplodeAmmo = unit.maxExplodeAmmo;
                        else
                            unit.currentExplodeAmmo = Mathf.Clamp(unit.currentExplodeAmmo + explodeAmmoSupply, 0, unit.maxExplodeAmmo);
                        if (unit.currentExplodeAmmo != oldExplode)
                        {
                            supplied = true;
                            supplyParts.Add("Bomb+");
                            if (supplyColor == Colors.Green) supplyColor = new Color(0.9f, 0.5f, 0.1f);
                        }
                    }
                }
            }

            // ✅ 新弹药：生产弹补给（统一弹药系统）
            if (productionAmmoSupply != 0)
            {
                if (unit.canProduceUnits)
                {
                    if (supportedUnitTypesForProductionAmmo.Count == 0 || supportedUnitTypesForProductionAmmo.Contains(unitType))
                    {
                        int oldProd = unit.currentProductionAmmo;
                        if (productionAmmoSupply >= 999)
                            unit.currentProductionAmmo = unit.maxProductionAmmo;
                        else
                            unit.currentProductionAmmo = Mathf.Clamp(unit.currentProductionAmmo + productionAmmoSupply, 0, unit.maxProductionAmmo);
                        if (unit.currentProductionAmmo != oldProd)
                        {
                            supplied = true;
                            supplyParts.Add("Prod+");
                            if (supplyColor == Colors.Green) supplyColor = new Color(0.6f, 0.5f, 0.9f);
                        }
                    }
                }
            }

            // ✅ 新弹药：建造弹补给（统一弹药系统）
            if (constructionAmmoSupply != 0)
            {
                if (unit.canBuildFacilities)
                {
                    if (supportedUnitTypesForConstructionAmmo.Count == 0 || supportedUnitTypesForConstructionAmmo.Contains(unitType))
                    {
                        int oldBuild = unit.currentConstructionAmmo;
                        if (constructionAmmoSupply >= 999)
                            unit.currentConstructionAmmo = unit.maxConstructionAmmo;
                        else
                            unit.currentConstructionAmmo = Mathf.Clamp(unit.currentConstructionAmmo + constructionAmmoSupply, 0, unit.maxConstructionAmmo);
                        if (unit.currentConstructionAmmo != oldBuild)
                        {
                            supplied = true;
                            supplyParts.Add("Build+");
                            if (supplyColor == Colors.Green) supplyColor = new Color(0.5f, 0.7f, 0.9f);
                        }
                    }
                }
            }

            // ✅ 第11资源：特殊弹补给（开创人2026-11-11，useSpecialAmmo=false的单位自动跳过）
            if (specialAmmoSupply != 0)
            {
                if (unit.useSpecialAmmo)
                {
                    if (supportedUnitTypesForSpecialAmmo.Count == 0 || supportedUnitTypesForSpecialAmmo.Contains(unitType))
                    {
                        int oldSpecial = unit.currentSpecialAmmo;
                        if (specialAmmoSupply >= 999)
                            unit.currentSpecialAmmo = unit.maxSpecialAmmo;
                        else
                            unit.currentSpecialAmmo = Mathf.Clamp(unit.currentSpecialAmmo + specialAmmoSupply, 0, Mathf.Max(unit.maxSpecialAmmo, unit.currentSpecialAmmo));
                        if (unit.currentSpecialAmmo != oldSpecial)
                        {
                            supplied = true;
                            supplyParts.Add("Special+");
                            if (supplyColor == Colors.Green) supplyColor = new Color(0.5f, 0.7f, 0.9f);
                        }
                    }
                }
            }

            if (supplied)
            {
                anySupplied = true;
                supplyText = string.Join(" ", supplyParts);
                unit.UpdateHpLabel();
                ShowSupplyEffectNew(unit, supplyText, supplyColor);
            }
        }
        if (anySupplied) ShowGridSupplyEffectNew();
    }

    private void ShowSupplyEffectNew(Infantry unit, string text, Color color)
    {
        var effect = new SupplyEffect(); AddChild(effect);
        effect.Setup(unit.Position - GlobalPosition, text, color);
    }

    private void ShowGridSupplyEffectNew() { var effect = new GridSupplyEffect(); AddChild(effect); }

    // ========== 设施视野（P-1设施给所有势力共享） ==========

    public virtual HashSet<Grids> CalculateFacilityVision(GameManager gm)
    {
        var visible = new HashSet<Grids>();
        if (gm == null || gm.gridManager?.map == null) return visible;

        if ((facilityTeam == TeamHelper.Player || isPublicFacility) && ParentGrid != null)
        {
            visible.Add(ParentGrid);
        }
        else if (TeamHelper.DoesFacilityProvideVision(facilityTeam) && ParentGrid != null)
        {
            visible.Add(ParentGrid);
        }
        return visible;
    }

    public int productionSuppressPercent = 0;   // 生产资金消耗压制百分比（负=降价 正=涨价）
    /// <summary>设施压制：生产实际造价（百分比乘区，下限1G）。UI显示与扣款、AI扣款三处共用。</summary>
    public int GetProductionCost(int baseCost)
        => ProductionPricing.Calculate(baseCost, productionDiscountPercent, productionDiscountByUnit, "", productionSuppressPercent);

    public int incomeSuppressPercent = 0;    // 资金压制百分比（设施压制Export写入）
    public int defenseSuppressPercent = 0;   // 掩体压制百分比
    public void SetIncomeSuppression(int percent) { incomeSuppressPercent = percent; }

    public int GetCurrentIncome()
    {
        if (HasStatus(FacilityStatusType.NoIncome))
            return 0;
        // 设施压制百分比（负=减弱 正=增强）；制止走 NoIncome 状态
        return Mathf.RoundToInt(fundsPerTurn * (100 + incomeSuppressPercent) / 100f);
    }

    public bool ProvidesIncomeTo(string team)
    {
        if (HasStatus(FacilityStatusType.NoIncome))
            return false;

        if (facilityTeam == TeamHelper.Player || isPublicFacility || provideIncomeToAllTeams)
            return true;

        return facilityTeam == team;
    }
}