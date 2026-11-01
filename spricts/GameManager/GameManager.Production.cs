// GameManager.cs - 添加完整的单位统计系统
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// GameManager · Production（由 GameManager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class GameManager
{

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

        // 分隔线
        var sep = new HSeparator();
        sep.CustomMinimumSize = new Godot.Vector2(0, 8);
        productionMenuContainer.AddChild(sep);

        foreach (string unitName in unitList)
        {
            if (!UnitProductionDatabase.HasUnit(unitName)) continue;

            var info = UnitProductionDatabase.GetInfo(unitName);
            // ✅ CO 模块系统·模块12（生产模块）：生产价格百分比增减（与实际扣费一致）
            int prodCost = Mathf.Max(1, Mathf.RoundToInt(productionTargetFacility.GetProductionCost(info.Cost)
                * (100f + CoModuleSystem.GetCostModifierPercent(currentTeam)) / 100f));
            bool isCoUnit = CoModuleSystem.IsExclusiveUnit(currentTeam, unitName);
            bool canAfford = currentFunds >= prodCost;
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
                btn.Pressed += () => OnProduceUnitClicked(capturedUnitName);
            }

            productionMenuContainer.AddChild(btn);
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

    private void OnProduceUnitClicked(string unitName)
    {
        if (productionTargetGrid == null || productionTargetFacility == null) return;
        if (!UnitProductionDatabase.HasUnit(unitName)) return;
        // ✅ 每回合生产次数限制
        if (!productionTargetFacility.CanProduceNow()) return;

        var info = UnitProductionDatabase.GetInfo(unitName);
        string currentTeam = TeamHelper.GetPhaseTeamName(turnPhase) ?? "";

        // 编辑模式不判定资金
        bool isEditMode = terrainEditor != null && terrainEditor.IsEditMode;
        if (!isEditMode)
        {
            int currentFunds = GetTeamFunds(currentTeam);
            // ✅ CO 模块系统·模块12（生产模块）：价格增减与菜单显示/扣费一致
            int prodCostUI = Mathf.Max(1, Mathf.RoundToInt(productionTargetFacility.GetProductionCost(info.Cost)
                * (100f + CoModuleSystem.GetCostModifierPercent(currentTeam)) / 100f));
            if (currentFunds < prodCostUI)
            {
                return;
            }
            SetTeamFunds(currentTeam, currentFunds - prodCostUI);
            // ✅ 模块27（能量条）：消耗资金打点
            CoModuleSystem.OnFundsSpent(currentTeam, prodCostUI);
        }

        // 生成单位
        var scene = GD.Load<PackedScene>(info.ScenePath);
        if (scene == null)
        {
            return;
        }

        var unit = scene.Instantiate<Infantry>();
        if (unit == null) return;

        unit.Position = productionTargetGrid.Position;
        unit.team = currentTeam;
        // ✅ CO 模块系统·模块19/30：CO 专属单位出厂即 CO 单位（Zone 载体，被毁=CO 死亡规则用）
        if (CoModuleSystem.IsExclusiveUnit(currentTeam, unitName)) unit.isCoUnit = true;
        int unitNumber = unitManager.AllUnits.Count + 1;
        unit.Name = $"{currentTeam}_{unitName}_{unitNumber}";

        unitManager.units.AddChild(unit);
        bool bound = unitManager.BindUnitToGrid(unit, true);
        if (!bound)
        {
            unit.QueueFree();
            return;
        }

        unitManager.AllUnits.Add(unit);

        RegisterUnit(unit, InferCategory(unit));
        unit.OnClickPiece = OnSelectPiece;
        unit.UpdateTeamVisual();
        unit.UpdateHpLabel();
        if (unit.sprite != null)
            unit.StartBreath();

        // 即时移动开关：默认关闭（instantMove=false）则生产后单位直接待机
        if (productionTargetFacility != null && !productionTargetFacility.instantMoveAfterProduction)
        {
            unit.isMoved = true;
            unit.isAttacked = true;
            unit.hasActed = true;
            unit.state = UnitState.Acted;
            unit.SetWaitVisual(true);
        }

        // ✅ 累计本回合生产次数
        productionTargetFacility.productionsThisTurn++;
        // ✅ P-1公共设施：记录使用势力，添加"不可生产"状态给当前势力
        productionTargetFacility.OnUnitProducedBy(currentTeam);
        // ✅ CO 模块系统·模块27：生产单位打点（能量条）
        CoModuleSystem.OnUnitProduced(currentTeam);

        UpdateUnitLists();
        RefreshSpecializedUnitLists();


        // 刷新菜单（资金已变化，可能有些按钮要变灰）
        ShowProductionMenu(productionTargetFacility, productionTargetGrid);
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
        int prodCostAI = Mathf.Max(1, Mathf.RoundToInt(facility.GetProductionCost(info.Cost)
            * (100f + CoModuleSystem.GetCostModifierPercent(team)) / 100f));
        if (currentFunds < prodCostAI) return false;

        // 扣除资金
        SetTeamFunds(team, currentFunds - prodCostAI);
        CoModuleSystem.OnFundsSpent(team, prodCostAI);

        // 生成单位
        var scene = GD.Load<PackedScene>(info.ScenePath);
        if (scene == null)
        {
            return false;
        }

        var unit = scene.Instantiate<Infantry>();
        if (unit == null) return false;

        unit.Position = grid.Position;
        unit.team = team;
        int unitNumber = unitManager.AllUnits.Count + 1;
        unit.Name = $"{team}_{unitName}_{unitNumber}";

        unitManager.units.AddChild(unit);
        bool bound = unitManager.BindUnitToGrid(unit, true);
        if (!bound)
        {
            unit.QueueFree();
            return false;
        }

        unitManager.AllUnits.Add(unit);

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
        if (!facility.instantMoveAfterProduction)
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
}
