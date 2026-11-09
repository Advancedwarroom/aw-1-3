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

    // ========== 状态栏系统 ==========
    [ExportGroup("初始状态（运行时生效）")]
    [Export] public Godot.Collections.Array<FacilityStatusData> initialStatuses = new();
    
    // 运行时状态列表（不Export）
    private List<FacilityStatus> activeStatuses = new();

    // 状态栏UI引用（运行时创建）
    private VBoxContainer statusBarContainer;
    private Dictionary<FacilityStatusType, Label> statusLabels = new();

    public bool CanProduceNow() => canProduce && productionsThisTurn < maxProductionsPerTurn && !HasStatus(FacilityStatusType.CannotProduce);

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

    [ExportGroup("补给支持兵种过滤（为空=不限制）")]
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForHeal = new();
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForFlare = new();
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForExplode = new();
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForPrimaryAmmo = new();
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForFuel = new();
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForProductionAmmo = new();
    [Export] public Godot.Collections.Array<string> supportedUnitTypesForConstructionAmmo = new();

    // ========== P-1设施收入共享配置 ==========
    [ExportGroup("P-1公共设施配置")]
    [Export] public bool isPublicFacility = false;
    [Export] public int fundsPerTurn = 1000;
    [Export] public bool provideIncomeToAllTeams = false;

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
        if (baseSprite == null)
            baseSprite = GetNodeOrNull<Sprite2D>("BaseSprite");
        if (normalSprite == null)
            normalSprite = GetNodeOrNull<Sprite2D>("NormalSprite");
        
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

        // 单图层（原路径不变）
        if (animSprite != null && IsInstanceValid(animSprite) && animSprite.SpriteFrames != null)
        {
            var frames = animSprite.SpriteFrames;
            if (!frames.HasAnimation(anim)) anim = frames.HasAnimation(red) ? red : neutral;
            if (!frames.HasAnimation(anim)) return;
            if (animSprite.Animation != anim || !animSprite.IsPlaying()) animSprite.Play(anim);
            animSprite.Modulate = Colors.White; // 旧版 P-1 用 Modulate 染色，现统一由 Shader 处理
        }

        // ✅ 双图层：Base/Normal 层用同名动画同步驱动（Base层灰阶mask随shader染势力色）
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
    
    /// <summary>
    /// ✅ 为设施应用调色板Shader（双图层 + 兼容模式）
    /// </summary>
    protected virtual void ApplyPaletteMaterial()
    {
        // ✅ 双图层 AnimatedSprite2D 方案（优先）
        if (baseAnimSprite != null && IsInstanceValid(baseAnimSprite) &&
            normalAnimSprite != null && IsInstanceValid(normalAnimSprite))
        {
            var baseMat = FactionPaletteManager.GetBaseLayerMaterial(facilityTeam);
            if (baseMat != null)
            {
                baseAnimSprite.Material = (ShaderMaterial)baseMat.Duplicate();
                baseAnimSprite.Show();
            }
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
            var baseMat = FactionPaletteManager.GetBaseLayerMaterial(facilityTeam);
            if (baseMat != null)
            {
                baseSprite.Material = (ShaderMaterial)baseMat.Duplicate();
                baseSprite.Show();
            }
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
        // (染花水晶同款雷; P1/P2靠IsNativeArt幸免, 其余势力全在吃误判)——原生固定画直出Material=null;
        // 若未来某设施接入(R,G)编码贴图, 在此改挂 FactionPaletteManager.GetCowLutMaterial(facilityTeam)
        if (animSprite != null && IsInstanceValid(animSprite))
        {
            animSprite.Material = null;
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
    public int GetProductionCost(int baseCost) => Mathf.Max(1, baseCost * (100 + productionSuppressPercent) / 100);

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