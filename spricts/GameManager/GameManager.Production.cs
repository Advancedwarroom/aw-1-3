// GameManager.cs - 添加完整的单位统计系统
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// GameManager · Production（由 GameManager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class GameManager
{

    public bool dualInstant = false;   // ✅ 双生产菜单：即时出厂即动（默认关=生产后待机，与其修复一致）

    public bool IsProductionMenuOpen()
    {
        return productionMenuPanel != null && productionMenuPanel.Visible;
    }

    public void ShowProductionMenu(Facility facility, Grids grid)
    {
        if (facility == null || !facility.canProduce || facility.producibleUnitNames.Count == 0) return;

        // 关闭行动菜单，避免重叠
        (GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu)?.Hide();

        productionTargetGrid = grid;
        productionTargetFacility = facility;

        if (productionMenuPanel == null)
        {
            CreateProductionMenuUI();
        }

        // 刷新列表
        foreach (var child in productionMenuContainer.GetChildren())
            child.QueueFree();

        string currentTeam = TeamHelper.GetPhaseTeamName(turnPhase) ?? "";
        int currentFunds = GetTeamFunds(currentTeam);

        // ✅ CO 模块系统·模块19（专属单位）：该势力 CO 的专属单位并入生产列表（去重、须在单位库中存在）
        var unitList = new System.Collections.Generic.List<string>(facility.producibleUnitNames);
        foreach (var coUnit in CoModuleSystem.GetExclusiveUnits(currentTeam))
            if (!unitList.Contains(coUnit) && UnitProductionDatabase.HasUnit(coUnit))
                unitList.Add(coUnit);

        int remainingProductions = Mathf.Max(0, facility.maxProductionsPerTurn - facility.productionsThisTurn);
        productionMenuTitle.Text = $"🏭 生产菜单 ({facility.GetType().Name})\n💰 资金: {currentFunds} G\n⚙️ 本回合剩余生产: {remainingProductions}/{facility.maxProductionsPerTurn}";

        // ✅ 即时移动开关
        var moveCheckBox = new CheckBox();
        TranslateGroup.SetText(moveCheckBox, "⚡ 生产后可立即移动");
        moveCheckBox.AddThemeFontSizeOverride("font_size", 12);
        moveCheckBox.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.4f));
        moveCheckBox.ButtonPressed = facility.instantMoveAfterProduction;
        moveCheckBox.Toggled += (pressed) => {
            facility.instantMoveAfterProduction = pressed;
        };
        productionMenuContainer.AddChild(moveCheckBox);

        if (EmpowermentRuntime.CanManualUse(facility))
        {
            var empowerButton = new Button();
            empowerButton.Text = facility.empowermentActionName;
            empowerButton.Pressed += () =>
            {
                HideProductionMenu();
                (GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu)?.BeginEmpowermentSelection(facility);
            };
            productionMenuContainer.AddChild(empowerButton);
        }

        // 分隔线
        var sep = new HSeparator();
        sep.CustomMinimumSize = new Godot.Vector2(0, 8);
        productionMenuContainer.AddChild(sep);

        bool dualMode = productionTargetFacility.dualProductionMode;
        foreach (string unitName in unitList)
        {
            if (!UnitProductionDatabase.HasUnit(unitName)) continue;
            if (productionTargetFacility.isUpdatable && !UnitProductionWhitelist.CanProduce(unitName)) continue;   // 不能产的就不显示

            var info = UnitProductionDatabase.GetInfo(unitName);
            // ✅ CO 模块系统·模块12（生产模块）：生产价格百分比增减（与实际扣费一致）
            int prodCost = Mathf.Max(1, Mathf.RoundToInt(productionTargetFacility.GetProductionCost(info.Cost, unitName)
                * (100f + CoModuleSystem.GetCostModifierPercent(currentTeam)) / 100f));
            if (productionTargetFacility.productionFree) prodCost = 0;
            bool isCoUnit = CoModuleSystem.IsExclusiveUnit(currentTeam, unitName);
            bool canAfford = productionTargetFacility.productionFree || currentFunds >= prodCost;
            bool gridEmpty = grid.infantries.Count == 0 && grid.weapons.Count == 0;
            bool productionAvailable = facility.CanProduceNow();

            var btn = new Button();
            btn.CustomMinimumSize = new Godot.Vector2(300, 50);
            btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            btn.Text = $"{(isCoUnit ? "★" : "")}{info.DisplayName}\n💰 {prodCost} G";
            btn.AddThemeFontSizeOverride("font_size", 13);

            if (!productionAvailable)
            {
                btn.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
                var disabledStyle = new StyleBoxFlat();
                disabledStyle.BgColor = new Color(0.2f, 0.2f, 0.2f, 0.6f);
                disabledStyle.SetCornerRadiusAll(6);
                btn.AddThemeStyleboxOverride("normal", disabledStyle);
                btn.TooltipText = TranslateGroup.T("已达本回合生产上限");
                btn.Disabled = true;
            }
            else if (!gridEmpty)
            {
                btn.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
                var disabledStyle = new StyleBoxFlat();
                disabledStyle.BgColor = new Color(0.2f, 0.2f, 0.2f, 0.6f);
                disabledStyle.SetCornerRadiusAll(6);
                btn.AddThemeStyleboxOverride("normal", disabledStyle);
                btn.TooltipText = TranslateGroup.T("格子已被占据");
                btn.Disabled = true;
            }
            else if (!canAfford)
            {
                btn.AddThemeColorOverride("font_color", new Color(0.8f, 0.3f, 0.3f));
                var poorStyle = new StyleBoxFlat();
                poorStyle.BgColor = new Color(0.3f, 0.1f, 0.1f, 0.6f);
                poorStyle.SetCornerRadiusAll(6);
                btn.AddThemeStyleboxOverride("normal", poorStyle);
                btn.TooltipText = TranslateGroup.T("资金不足");
                btn.Disabled = true;
            }
            else
            {
                btn.AddThemeColorOverride("font_color", Colors.White);
                var normalStyle = new StyleBoxFlat();
                normalStyle.BgColor = new Color(0.2f, 0.4f, 0.3f, 0.8f);
                normalStyle.SetCornerRadiusAll(6);
                btn.AddThemeStyleboxOverride("normal", normalStyle);
                var hoverStyle = new StyleBoxFlat();
                hoverStyle.BgColor = new Color(0.3f, 0.6f, 0.4f, 0.9f);
                hoverStyle.SetCornerRadiusAll(6);
                btn.AddThemeStyleboxOverride("hover", hoverStyle);

                string capturedUnitName = unitName;
                btn.Pressed += () => OnProduceUnitClicked(capturedUnitName, false);
            }

            productionMenuContainer.AddChild(btn);
            if (dualMode)
            {
                int instantCost = productionTargetFacility.productionFree ? 0 : Mathf.Max(1, Mathf.RoundToInt(prodCost * productionTargetFacility.instantCostMultiplier));
                int standbyCost = productionTargetFacility.productionFree ? 0 : Mathf.Max(1, Mathf.RoundToInt(prodCost * productionTargetFacility.standbyCostMultiplier));
                var btnInstant = new Button();
                btnInstant.CustomMinimumSize = new Godot.Vector2(300, 42);
                btnInstant.AddThemeFontSizeOverride("font_size", 12);
                btnInstant.Text = $"⚡ {info.DisplayName} 立即\n💰 {instantCost} G";
                var btnStandby = new Button();
                btnStandby.CustomMinimumSize = new Godot.Vector2(300, 42);
                btnStandby.AddThemeFontSizeOverride("font_size", 12);
                btnStandby.Text = $"😴 {info.DisplayName} 待机\n💰 {standbyCost} G";
                bool avail2 = facility.CanProduceNow() && gridEmpty;
                btnInstant.Disabled = !(avail2 && (productionTargetFacility.productionFree || currentFunds >= instantCost));
                btnStandby.Disabled = !(avail2 && (productionTargetFacility.productionFree || currentFunds >= standbyCost));
                string cap = unitName;
                btnInstant.Pressed += () => OnProduceUnitClicked(cap, true);
                btnStandby.Pressed += () => OnProduceUnitClicked(cap, false, true);
                productionMenuContainer.AddChild(btnInstant);
                productionMenuContainer.AddChild(btnStandby);
                btn.Visible = false;
            }
        }

        var closeBtn = new Button();
        TranslateGroup.SetText(closeBtn, "❌ 关闭");
        closeBtn.CustomMinimumSize = new Godot.Vector2(300, 40);
        closeBtn.AddThemeColorOverride("font_color", Colors.White);
        var closeStyle = new StyleBoxFlat();
        closeStyle.BgColor = new Color(0.4f, 0.2f, 0.2f, 0.8f);
        closeStyle.SetCornerRadiusAll(6);
        closeBtn.AddThemeStyleboxOverride("normal", closeStyle);
        closeBtn.Pressed += HideProductionMenu;
        productionMenuContainer.AddChild(closeBtn);

        productionMenuPanel.Visible = true;
    }

    private void CreateProductionMenuUI()
    {
        var canvas = GetTree().CurrentScene.GetNodeOrNull<CanvasLayer>("CanvasLayer");
        if (canvas == null) return;

        productionMenuPanel = new Panel();
        productionMenuPanel.Name = "ProductionMenuPanel";
        productionMenuPanel.CustomMinimumSize = new Godot.Vector2(360, 500);
        productionMenuPanel.SetAnchorsPreset(Control.LayoutPreset.Center);
        productionMenuPanel.Visible = false;
        productionMenuPanel.ZIndex = 1200;
        productionMenuPanel.TopLevel = true;   // 置顶：不被地图盖住

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.1f, 0.12f, 0.18f, 0.97f);
        panelStyle.SetCornerRadiusAll(16);
        panelStyle.SetBorderWidthAll(3);
        panelStyle.BorderColor = new Color(0.4f, 0.7f, 0.9f);
        productionMenuPanel.AddThemeStyleboxOverride("panel", panelStyle);

        productionMenuTitle = new Label();
        productionMenuTitle.Name = "ProductionMenuTitle";
        TranslateGroup.SetText(productionMenuTitle, "🏭 生产菜单");
        productionMenuTitle.HorizontalAlignment = HorizontalAlignment.Center;
        productionMenuTitle.AddThemeFontSizeOverride("font_size", 18);
        productionMenuTitle.AddThemeColorOverride("font_color", new Color(0.4f, 0.9f, 1f));
        productionMenuTitle.CustomMinimumSize = new Godot.Vector2(0, 60);
        productionMenuTitle.Position = new Godot.Vector2(0, 12);
        productionMenuTitle.Size = new Godot.Vector2(360, 60);
        productionMenuPanel.AddChild(productionMenuTitle);

        // ✅ 拖拽手柄
        var dragHandle = new Panel();
        dragHandle.Name = "ProductionDragHandle";
        dragHandle.CustomMinimumSize = new Godot.Vector2(360, 28);
        dragHandle.Position = new Godot.Vector2(0, 0);
        var handleStyle = new StyleBoxFlat();
        handleStyle.BgColor = new Color(0.3f, 0.3f, 0.4f, 0.5f);
        handleStyle.SetCornerRadiusAll(8);
        dragHandle.AddThemeStyleboxOverride("panel", handleStyle);
        var dragHint = new Label();
        TranslateGroup.SetText(dragHint, "⋮⋮ 拖拽移动");
        dragHint.AddThemeFontSizeOverride("font_size", 10);
        dragHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.8f));
        dragHint.Position = new Godot.Vector2(8, 2);
        dragHandle.AddChild(dragHint);
        productionMenuPanel.AddChild(dragHandle);

        // ✅ 绑定拖拽
        dragHandle.GuiInput += (InputEvent @event) => {
            if (@event is InputEventMouseButton mouseEvent && mouseEvent.ButtonIndex == MouseButton.Left)
            {
                if (mouseEvent.Pressed)
                {
                    isDraggingProductionMenu = true;
                    currentDraggedProductionMenu = productionMenuPanel;
                    dragOffsetProduction = productionMenuPanel.GlobalPosition - dragHandle.GetGlobalMousePosition();
                }
                else
                {
                    isDraggingProductionMenu = false;
                    currentDraggedProductionMenu = null;
                }
            }
        };

        var closeBtn = new Button();
        closeBtn.Name = "ProductionCloseBtn";
        closeBtn.Text = "✕";
        closeBtn.CustomMinimumSize = new Godot.Vector2(32, 32);
        closeBtn.Position = new Godot.Vector2(320, 10);
        closeBtn.ZIndex = 10;
        closeBtn.Pressed += HideProductionMenu;
        productionMenuPanel.AddChild(closeBtn);

        var scroll = new ScrollContainer();
        scroll.Position = new Godot.Vector2(15, 80);
        scroll.Size = new Godot.Vector2(330, 400);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        productionMenuContainer = new VBoxContainer();
        productionMenuContainer.AddThemeConstantOverride("separation", 6);
        productionMenuContainer.Size = new Godot.Vector2(330, 0);

        scroll.AddChild(productionMenuContainer);
        productionMenuPanel.AddChild(scroll);

        canvas.AddChild(productionMenuPanel);
    }

    private void HideProductionMenu()
    {
        if (productionMenuPanel != null)
            productionMenuPanel.Visible = false;
        productionTargetGrid = null;
        productionTargetFacility = null;
    }

    private void OnProduceUnitClicked(string unitName, bool dualInstant = false, bool dualStandby = false)
    {
        if (productionTargetGrid == null || productionTargetFacility == null) return;
        if (!UnitProductionDatabase.HasUnit(unitName)) return;
        // ✅ 每回合生产次数限制
        if (!productionTargetFacility.CanProduceNow()) return;

        var info = UnitProductionDatabase.GetInfo(unitName);
        string currentTeam = TeamHelper.GetPhaseTeamName(turnPhase) ?? "";

        // 编辑模式不判定资金
        bool isEditMode = terrainEditor != null && terrainEditor.IsEditMode;
        int prodCostUI = 0;
        if (!isEditMode)
        {
            int currentFunds = GetTeamFunds(currentTeam);
            // ✅ CO 模块系统·模块12（生产模块）：价格增减与菜单显示/扣费一致
            prodCostUI = Mathf.Max(1, Mathf.RoundToInt(productionTargetFacility.GetProductionCost(info.Cost, unitName)
                * (100f + CoModuleSystem.GetCostModifierPercent(currentTeam)) / 100f));
            if (productionTargetFacility.productionFree) prodCostUI = 0;
            if (!productionTargetFacility.productionFree && currentFunds < prodCostUI)
            {
                return;
            }
            if (!SurvivalSession.Instance?.TrySpendProduction(productionTargetFacility.productionFree ? 0 : prodCostUI) ?? true) return;   // ✅ 开创人2026-11-14 生存模式：资金预算闸门（UI线）
            if (!productionTargetFacility.productionFree) SetTeamFunds(currentTeam, currentFunds - prodCostUI);
            // ✅ 模块27（能量条）：消耗资金打点
            CoModuleSystem.OnFundsSpent(currentTeam, prodCostUI);
        }


        // ===== 远程生产落点（开创人2026-11-11）：开关+n-m 成对。开=环带高亮点选；关=现状直落建筑格 =====
        if (productionTargetFacility.remoteProductionEnabled && productionTargetFacility.productionDropMaxRange > 0)
        {
            var dropCells = GetRemoteDropCells(productionTargetFacility);
            if (dropCells.Count == 0)
            {
                if (!isEditMode && prodCostUI > 0) RefundProd(currentTeam, prodCostUI);   // ✅ P1-6 无合法落点：资金+生存预算双退
                GD.PushWarning("[生产] 远程落点 n-m 环带内无合法格，资金已返还");
                return;
            }
            EnterRemoteDropMode(unitName, productionTargetFacility, currentTeam, prodCostUI, isEditMode, dropCells, dualInstant, dualStandby);
            return;   // 落点确认后由 ConfirmRemoteDrop 继续生成
        }

        SpawnProducedUnit(unitName, productionTargetFacility, productionTargetGrid, currentTeam, dualInstant, dualStandby, prodCostUI);
    }

    // ✅ 修复(审查P1-6): 生产失败全额退款(资金+生存预算双退)
    private void RefundProd(string team, int cost)
    {
        if (cost <= 0) return;
        SetTeamFunds(team, GetTeamFunds(team) + cost);
        SurvivalSession.Instance?.RefundProduction(cost);
    }

    /// 生成生产单位（现状直落与远程落点共用；落点非法绑定失败返回 false 由调用方退款）
    private bool SpawnProducedUnit(string unitName, Facility facility, Grids dropGrid, string currentTeam, bool dualInstant, bool dualStandby, int prodCostRefund = 0)
    {
        var info = UnitProductionDatabase.GetInfo(unitName);   // 原方法头部已抽取，此处自取
        // 生成单位
        var scene = GD.Load<PackedScene>(info.ScenePath);
        if (scene == null)
        {
            RefundProd(currentTeam, prodCostRefund);   // ✅ P1-6
            return false;
        }

        var unit = scene.Instantiate<Infantry>();
        if (unit == null) { RefundProd(currentTeam, prodCostRefund); return false; }   // ✅ P1-6

        unit.Position = dropGrid.Position;
        unit.team = currentTeam;
        // ✅ CO 模块系统·模块19/30：CO 专属单位出厂即 CO 单位（Zone 载体，被毁=CO 死亡规则用）
        if (CoModuleSystem.IsExclusiveUnit(currentTeam, unitName)) unit.isCoUnit = true;
        int unitNumber = unitManager.AllUnits.Count + 1;
        unit.Name = $"{currentTeam}_{unitName}_{unitNumber}";
        ApplyProductionVigor(unit, facility);   // ✅ 产能绑定（血量/占领度平均值）→ 出厂血量/燃油（厂子越残，出的单位越残次）

        unitManager.units.AddChild(unit);
        bool bound = unitManager.BindUnitToGrid(unit, true);
        if (!bound)
        {
            unit.QueueFree();
            RefundProd(currentTeam, prodCostRefund);   // ✅ P1-6
            return false;
        }

        unitManager.AllUnits.Add(unit);
        // ✅ 修复(审查P2-8): 新产单位立即接入已获天赋
        var __tr = GetTree()?.Root?.FindChild("TalentRuntime", true, false) as TalentRuntime;
        __tr?.ApplyToUnit(unit);

        RegisterUnit(unit, InferCategory(unit));
        unit.OnClickPiece = OnSelectPiece;
        unit.UpdateTeamVisual();
        unit.UpdateHpLabel();
        if (unit.sprite != null)
            unit.StartBreath();

        // 即时移动开关：默认关闭（instantMove=false）则生产后单位直接待机
        if (facility != null && !facility.instantMoveAfterProduction)
        {
            unit.isMoved = true;
            unit.isAttacked = true;
            unit.hasActed = true;
            unit.state = UnitState.Acted;
            unit.SetWaitVisual(true);
        }

        // ✅ 累计本回合生产次数
        facility.productionsThisTurn++;
        // ✅ P-1公共设施：记录使用势力，添加"不可生产"状态给当前势力
        facility.OnUnitProducedBy(currentTeam);
        // ✅ CO 模块系统·模块27：生产单位打点（能量条）
        CoModuleSystem.OnUnitProduced(currentTeam);

        UpdateUnitLists();
        RefreshSpecializedUnitLists();


        // 刷新菜单（资金已变化，可能有些按钮要变灰）
        ShowProductionMenu(facility, dropGrid);
        return true;
    }

    /// <summary>产能绑定（开创人2026-11-11）：出厂单位血量/燃油按设施产能活力缩放——厂子越残，出的单位越残次。
    /// 绑定未开=不缩放；血量托底 productionUnitMinHp，燃油钳在 [productionUnitMinFuel, maxFuel]。</summary>
    /// <summary>产能绑定（开创人2026-11-12）：出厂资源按产能活力缩放——11资源（Health/Fuel+9弹种）各自独立配置，不统一。
    /// 双模式：百分比（亏损度×基数×值/100）/固定（亏损度×值）；正负皆可：正=扣、负=倒加（残厂出超编资源）。
    /// 旧配置兼容：productionVigorResValue 为空=血量/燃油按百分比100缩放（旧×vigor行为），弹药不动；
    /// 字典非空时逐资源：有条目才缩放、无条目不动——可只扣燃油不扣弹药，每种弹药各自考虑。</summary>
    private void ApplyProductionVigor(Infantry unit, Facility facility)
    {
        if (facility == null || (!facility.productionHealthBound && !facility.productionCaptureBound)) return;
        float deficit = 1f - facility.GetProductionVigor();

        if (VigorShouldScale(facility, "Health"))
            unit.health = Math.Clamp(VigorComputeScaled(unit.health, "Health", deficit, facility), VigorFloorOf(facility, "Health"), VigorCapOf(facility, "Health", Math.Max(1, unit.maxHealth)));
        if (VigorShouldScale(facility, "Fuel"))
            unit.fuel = Math.Clamp(VigorComputeScaled(unit.fuel, "Fuel", deficit, facility), VigorFloorOf(facility, "Fuel"), VigorCapOf(facility, "Fuel", Math.Max(0, unit.maxFuel)));
        foreach (Infantry.AmmoKind kind in Enum.GetValues<Infantry.AmmoKind>())
        {
            string key = kind.ToString();
            if (!VigorShouldScale(facility, key)) continue;
            int scaled = VigorComputeScaled(unit.GetAmmo(kind), key, deficit, facility);
            unit.SetAmmoRaw(kind, Math.Clamp(scaled, VigorFloorOf(facility, key), VigorCapOf(facility, key, Math.Max(0, unit.GetMaxAmmo(kind)))));
        }
    }

    /// <summary>超编上限解析（开创人2026-11-12）：可否超上限=false（默认/未配置）→钳在原max；=true→填了 productionVigorResNewCap 用新上限，不填=无上限（显式-1也=无上限）。</summary>
    private int VigorCapOf(Facility f, string key, int defaultMax)
    {
        if (f.productionVigorResExceedMax.TryGetValue(key, out var allow) && allow)
            return f.productionVigorResNewCap.TryGetValue(key, out var cap) ? (cap < 0 ? int.MaxValue : cap) : int.MaxValue;
        return defaultMax;
    }

    /// <summary>旧配置兼容：缩放值字典为空→血量/燃油按旧行为缩放、弹药不缩放；非空→有条目才缩放。</summary>
    private bool VigorShouldScale(Facility f, string key)
        => f.productionVigorResValue.Count == 0 ? (key == "Health" || key == "Fuel") : f.productionVigorResValue.ContainsKey(key);

    /// <summary>缩放计算：亏损度=1-活力。百分比：delta=-亏损×基数×值/100；固定：delta=-亏损×值。正=扣、负=倒加。</summary>
    private int VigorComputeScaled(int baseVal, string key, float deficit, Facility f)
    {
        int value = f.productionVigorResValue.TryGetValue(key, out var v) ? v : 100;
        bool flat = f.productionVigorResMode.TryGetValue(key, out var m) && m == 1;
        int delta = flat ? -(int)Math.Round(deficit * value)
                         : -(int)Math.Round(baseVal * deficit * value / 100f);
        return baseVal + delta;
    }

    private int VigorFloorOf(Facility f, string key)
    {
        if (f.productionVigorResFloor.TryGetValue(key, out var fl)) return fl;
        if (key == "Health") return f.productionUnitMinHp;
        if (key == "Fuel") return f.productionUnitMinFuel;
        return 0;
    }

    /// <summary>
    /// AI自动生产单位：无需UI，直接检查条件并生产
    /// </summary>
    public bool AIProduceUnit(Facility facility, Grids grid, string unitName, string team)
    {
        if (facility == null || grid == null || !UnitProductionDatabase.HasUnit(unitName)) return false;
        if (!facility.canProduce || !facility.producibleUnitNames.Contains(unitName)) return false;
        if (!facility.CanProduceNow()) return false; // ✅ 每回合生产次数限制
        if (facility.facilityTeam != team && !facility.isPublicFacility) return false;
        if (grid.infantries.Count > 0 || grid.weapons.Count > 0) return false; // 格子必须为空

        var info = UnitProductionDatabase.GetInfo(unitName);
        int currentFunds = GetTeamFunds(team);
        // ✅ CO 模块系统·模块12：AI 生产同样吃价格增减
        int prodCostAI = Mathf.Max(1, Mathf.RoundToInt(facility.GetProductionCost(info.Cost, unitName)
            * (100f + CoModuleSystem.GetCostModifierPercent(team)) / 100f));
        if (facility.productionFree) prodCostAI = 0;
        if (!facility.productionFree && currentFunds < prodCostAI) return false;
        if (!SurvivalSession.Instance?.TrySpendProduction(facility.productionFree ? 0 : prodCostAI) ?? true) return false;   // ✅ 开创人2026-11-14 生存模式：资金预算闸门（AI线）

        // 扣除资金
        if (!facility.productionFree) SetTeamFunds(team, currentFunds - prodCostAI);
        CoModuleSystem.OnFundsSpent(team, prodCostAI);

        // 生成单位
        var scene = GD.Load<PackedScene>(info.ScenePath);
        if (scene == null)
        {
            RefundProd(team, prodCostAI);   // ✅ P1-6(AI线)
            return false;
        }

        var unit = scene.Instantiate<Infantry>();
        if (unit == null) { RefundProd(team, prodCostAI); return false; }   // ✅ P1-6(AI线)

        unit.Position = grid.Position;
        unit.team = team;
        int unitNumber = unitManager.AllUnits.Count + 1;
        unit.Name = $"{team}_{unitName}_{unitNumber}";
        ApplyProductionVigor(unit, facility);   // ✅ 产能绑定：AI 出厂同规则同规

        unitManager.units.AddChild(unit);
        bool bound = unitManager.BindUnitToGrid(unit, true);
        if (!bound)
        {
            unit.QueueFree();
            return false;
        }

        unitManager.AllUnits.Add(unit);
            // ✅ 修复(审查P2-8): AI新产单位同样接入天赋
            var __trAi = GetTree()?.Root?.FindChild("TalentRuntime", true, false) as TalentRuntime;
            __trAi?.ApplyToUnit(unit);

        RegisterUnit(unit, InferCategory(unit));
        unit.OnClickPiece = OnSelectPiece;
        unit.UpdateTeamVisual();
        unit.UpdateHpLabel();

        // ✅ 修复：始终重置状态，覆盖场景预设值
        unit.isMoved = false;
        unit.isAttacked = false;
        unit.hasActed = false;
        unit.state = UnitState.Idle;
        unit.movePoints = unit.defaultMovePoints;
        unit.originalGrid = null;
        unit.SetWaitVisual(false);

        // ✅ 修复：与玩家生产一致——不支持生产后立即移动时，单位生产出来直接待机（下回合才能行动）
        bool instantNow = facility.dualProductionMode ? dualInstant : facility.instantMoveAfterProduction;
        if (!instantNow)
        {
            unit.isMoved = true;
            unit.isAttacked = true;
            unit.hasActed = true;
            unit.state = UnitState.Acted;
            unit.SetWaitVisual(true);
        }

        // ✅ 累计本回合生产次数
        facility.productionsThisTurn++;
        // ✅ P-1公共设施：记录使用势力
        facility.OnUnitProducedBy(team);
        // ✅ CO 模块系统·模块27：生产单位打点（能量条）
        CoModuleSystem.OnUnitProduced(team);

        UpdateUnitLists();
        RefreshSpecializedUnitLists();

        return true;
    }


    // ==================== 远程生产落点（开创人2026-11-11）====================
    private string pendingDropUnitName;
    private Facility pendingDropFacility;
    private string pendingDropTeam;
    private int pendingDropCost;
    private bool pendingDropEditMode;
    private bool pendingDropInstant;
    private bool pendingDropStandby;

    /// n-m 环带内合法落点：无单位/设施/兵器且非虚空格。
    private List<Grids> GetRemoteDropCells(Facility facility)
    {
        var result = new List<Grids>();
        if (facility?.ParentGrid == null || gridManager?.grids == null) return result;
        Vector2I origin = facility.ParentGrid.GridIndex;
        foreach (var g in gridManager.grids)
        {
            if (g == null || !IsInstanceValid(g)) continue;
            int dist = Math.Abs(g.GridIndex.X - origin.X) + Math.Abs(g.GridIndex.Y - origin.Y);
            if (dist < facility.productionDropMinRange || dist > facility.productionDropMaxRange) continue;
            if (g.infantry != null || g.city != null || g.weapon != null) continue;
            if (g.gridType is GridType.PLASMA or GridType.CREEPER or GridType.WALL or GridType.WEAK_WALL) continue;
            result.Add(g);
        }
        return result;
    }

    private void EnterRemoteDropMode(string unitName, Facility facility, string currentTeam, int cost, bool isEditMode, List<Grids> cells, bool dualInstant, bool dualStandby)
    {
        pendingDropUnitName = unitName;
        pendingDropFacility = facility;
        pendingDropTeam = currentTeam;
        pendingDropCost = isEditMode ? 0 : cost;
        pendingDropEditMode = isEditMode;
        pendingDropInstant = dualInstant;
        pendingDropStandby = dualStandby;
        var cellSet = new HashSet<Grids>(cells);
        foreach (var g in gridManager.grids)
        {
            if (g == null || !IsInstanceValid(g)) continue;
            if (cellSet.Contains(g))
            {
                g.pathIcon?.Show();                          // 高亮候选落点
                g.pathIcon?.SetDeferred("modulate", new Color(1.3f, 1.3f, 0.7f));
                Grids clicked = g;
                g.OnClickGrid = (_) => ConfirmRemoteDrop(clicked);
            }
            else
            {
                g.OnClickEmpty = CancelRemoteDrop;   // 点其他格=取消并退款
            }
        }
        GD.Print($"[生产] 远程落点：请在 {cells.Count} 个高亮格中点选（点其他格取消并退款）");
    }

    private void ConfirmRemoteDrop(Grids target)
    {
        string unitName = pendingDropUnitName;
        Facility facility = pendingDropFacility;
        string team = pendingDropTeam;
        int cost = pendingDropCost;
        bool instant = pendingDropInstant;
        bool standby = pendingDropStandby;
        bool editMode = pendingDropEditMode;
        ClearRemoteDropMode();
        if (!SpawnProducedUnit(unitName, facility, target, team, instant, standby) && cost > 0 && !editMode)
        {
            SetTeamFunds(team, GetTeamFunds(team) + cost);   // 绑定失败：退款
            GD.PushWarning("[生产] 远程落点绑定失败，资金已返还");
        }
    }

    private void CancelRemoteDrop()
    {
        string team = pendingDropTeam;
        int cost = pendingDropCost;
        bool editMode = pendingDropEditMode;
        ClearRemoteDropMode();
        if (cost > 0 && !editMode)
        {
            SetTeamFunds(team, GetTeamFunds(team) + cost);
            GD.PushWarning("[生产] 远程落点已取消，资金已返还");
        }
    }

    private void ClearRemoteDropMode()
    {
        pendingDropUnitName = null;
        pendingDropFacility = null;
        pendingDropTeam = null;
        pendingDropCost = 0;
        pendingDropEditMode = false;
        pendingDropInstant = false;
        pendingDropStandby = false;
        if (gridManager?.grids == null) return;
        foreach (var g in gridManager.grids)
        {
            if (g == null || !IsInstanceValid(g)) continue;
            g.OnClickGrid = null;
            g.OnClickEmpty = null;
            g.pathIcon?.Hide();
            g.pathIcon?.SetDeferred("modulate", Colors.White);
        }
    }
}
