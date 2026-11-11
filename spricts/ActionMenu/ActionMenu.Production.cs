// ActionMenu.cs - 生产/建造模块按钮与流程
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// ActionMenu · Production（生产模块 + 建造模块 + 部署建筑）
// 由 ActionMenu.cs 按模块拆分；partial class
// ✅ 按钮流：
//    生产中 → 只显示「中断生产」（文字带进度）；否则按位置模式显示 内部生产/外部生产。
//    可选内外 → 两个按钮都显示。
//    内部生产点击时自动开启搭载模块（仅需搭载，白名单可空）。
//    外部自动放置 → 选完单位直接随机放；外部手选 → 高亮有效格，点击选择目标。
//    建造同理（内部建好存舱内，用「部署建筑」放到场上）。
public partial class ActionMenu
{
    private Button produceInternalButton;
    private Button produceExternalButton;
    private Button produceCancelButton;
    private Button buildInternalButton;
    private Button buildExternalButton;
    private Button buildCancelButton;
    private Button deployFacilityButton;
    private VBoxContainer productionChoiceContainer;

    // 目标选择模式状态
    private string pendingProductionUnitId = null;     // 外部生产：待放置单位
    private Infantry pendingProductionPreview = null;   // 预览单位（判通行用）
    private string pendingConstructionType = null;     // 外部建造：待放置建筑
    private bool deployMode = false;                   // 部署建筑模式
    private List<Grids> productionTargetGrids = new();

    private VBoxContainer menuButtonContainer;

    private Button CreateMenuButton(string nameKey)
    {
        if (menuButtonContainer == null)
            menuButtonContainer = GetNodeOrNull<VBoxContainer>("ButtonContainer");
        var b = new Button();
        b.Name = nameKey;
        b.CustomMinimumSize = new Vector2(140, 36);
        b.AddThemeFontSizeOverride("font_size", 14);
        menuButtonContainer?.AddChild(b);
        return b;
    }

    private void EnsureProductionButtons()
    {
        if (produceInternalButton == null)
        {
            produceInternalButton = CreateMenuButton("ProduceInternalButton");
            TranslateGroup.SetText(produceInternalButton, "🛠️ 内部生产");
            produceInternalButton.Pressed += () => OnProducePressed(true);
        }
        if (produceExternalButton == null)
        {
            produceExternalButton = CreateMenuButton("ProduceExternalButton");
            TranslateGroup.SetText(produceExternalButton, "🛠️ 外部生产");
            produceExternalButton.Pressed += () => OnProducePressed(false);
        }
        if (produceCancelButton == null)
        {
            produceCancelButton = CreateMenuButton("ProduceCancelButton");
            TranslateGroup.SetText(produceCancelButton, "⛔ 中断生产");
            produceCancelButton.Pressed += OnProduceCancelPressed;
        }
        if (buildInternalButton == null)
        {
            buildInternalButton = CreateMenuButton("BuildInternalButton");
            TranslateGroup.SetText(buildInternalButton, "🏗️ 内部建造");
            buildInternalButton.Pressed += () => OnBuildPressed(true);
        }
        if (buildExternalButton == null)
        {
            buildExternalButton = CreateMenuButton("BuildExternalButton");
            TranslateGroup.SetText(buildExternalButton, "🏗️ 外部建造");
            buildExternalButton.Pressed += () => OnBuildPressed(false);
        }
        if (buildCancelButton == null)
        {
            buildCancelButton = CreateMenuButton("BuildCancelButton");
            TranslateGroup.SetText(buildCancelButton, "⛔ 中断建造");
            buildCancelButton.Pressed += OnBuildCancelPressed;
        }
        if (deployFacilityButton == null)
        {
            deployFacilityButton = CreateMenuButton("DeployFacilityButton");
            TranslateGroup.SetText(deployFacilityButton, "📦 部署建筑");
            deployFacilityButton.Pressed += OnDeployFacilityPressed;
        }
    }

    /// <summary>菜单刷新时的按钮可见性（由 ShowActionMenu 调用）</summary>
    private void ShowProductionBuildButtons(Infantry unit)
    {
        EnsureProductionButtons();

        // ===== 生产 =====
        bool producing = unit.isProducing;
        produceInternalButton.Visible = !producing && unit.CanStartProduction(true);
        produceExternalButton.Visible = !producing && unit.CanStartProduction(false);
        if (producing)
        {
            produceCancelButton.Visible = true;
            TranslateGroup.SetText(produceCancelButton,
                $"⛔ 中断生产 {unit.productionProgress}/{unit.productionTotalProgress}");
        }
        else produceCancelButton.Visible = false;

        // ===== 建造 =====
        bool constructing = unit.isConstructing;
        buildInternalButton.Visible = !constructing && unit.CanStartConstruction(true);
        buildExternalButton.Visible = !constructing && unit.CanStartConstruction(false);
        if (constructing)
        {
            buildCancelButton.Visible = true;
            TranslateGroup.SetText(buildCancelButton,
                $"⛔ 中断建造 {unit.constructionProgress}/{unit.constructionTotalProgress}");
        }
        else buildCancelButton.Visible = false;

        // ===== 部署舱内建筑 =====
        deployFacilityButton.Visible = unit.carriedFacilityTypes != null && unit.carriedFacilityTypes.Count > 0;
    }

    private void HideProductionBuildButtons()
    {
        if (produceInternalButton != null) produceInternalButton.Visible = false;
        if (produceExternalButton != null) produceExternalButton.Visible = false;
        if (produceCancelButton != null) produceCancelButton.Visible = false;
        if (buildInternalButton != null) buildInternalButton.Visible = false;
        if (buildExternalButton != null) buildExternalButton.Visible = false;
        if (buildCancelButton != null) buildCancelButton.Visible = false;
        if (deployFacilityButton != null) deployFacilityButton.Visible = false;
    }

    // ==================== 生产 ====================

    private void OnProducePressed(bool internalMode)
    {
        GetViewport().SetInputAsHandled();
        if (currentUnit == null) return;
        if (!currentUnit.CanStartProduction(internalMode)) return;

        if (internalMode)
        {
            // ✅ 内部生产只需开启搭载模块（自动开依赖项；白名单可空）
            currentUnit.EnsureTransportForInternalProduction();
            ShowProductionChoices(true);
        }
        else
        {
            ShowProductionChoices(false);
        }
    }

    /// <summary>生产/建造选项弹窗（白名单逐项按钮）</summary>
    private void ShowProductionChoices(bool internalMode)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || currentUnit == null) return;

        if (productionChoiceContainer == null)
        {
            productionChoiceContainer = new VBoxContainer();
            AddChild(productionChoiceContainer);
        }
        foreach (Node child in productionChoiceContainer.GetChildren()) child.QueueFree();
        productionChoiceContainer.Visible = true;
        productionChoiceContainer.Position = new Vector2(Size.X + 8, 0);

        var unit = currentUnit;
        bool internal_ = internalMode;
        foreach (var id in unit.productionWhitelist)
        {
            string unitId = id;
            var info = UnitProductionDatabase.GetInfo(unitId);
            var btn = new Button();
            btn.CustomMinimumSize = new Vector2(120, 32);
            btn.AddThemeFontSizeOverride("font_size", 13);
            // ✅ 价格显示 + 资金不足置灰（与建筑生产菜单同款式/同政策）
            int cost = unit.GetUnitProductionCost(unitId);
            string costText = cost <= 0 ? "免费" : $"{cost}G";
            string name = string.IsNullOrEmpty(info.DisplayName) ? unitId : info.DisplayName;
            TranslateGroup.SetText(btn, $"{name} [{costText}]");
            if (!unit.productionFree && cost > 0 && gm.GetTeamFunds(unit.team) < cost)
                btn.Disabled = true;
            btn.Pressed += () =>
            {
                productionChoiceContainer.Visible = false;
                OnProductionUnitChosen(unitId, internal_);
            };
            productionChoiceContainer.AddChild(btn);
        }
        var cancelBtn = new Button();
        cancelBtn.CustomMinimumSize = new Vector2(120, 32);
        cancelBtn.AddThemeFontSizeOverride("font_size", 13);
        TranslateGroup.SetText(cancelBtn, "取消");
        cancelBtn.Pressed += () => productionChoiceContainer.Visible = false;
        productionChoiceContainer.AddChild(cancelBtn);
    }

    private void OnProductionUnitChosen(string unitId, bool internalMode)
    {
        if (currentUnit == null || !IsInstanceValid(currentUnit)) return;

        if (internalMode)
        {
            if (currentUnit.StartProduction(unitId, true)) { }
        }
        else if (currentUnit.productionAutoPlace)
        {
            // ④ 外部自动随机放置
            var preview = CreatePreviewUnit(unitId);
            var grid = currentUnit.FindValidExternalProductionGrid(preview);
            preview?.Free();
            if (grid != null) currentUnit.StartProduction(unitId, false, grid);
            else GD.PushWarning("[生产] 外部范围内没有可放置的格子");
        }
        else
        {
            // ④ 外部手选目标格
            EnterExternalProductionTargetMode(unitId);
        }
    }

    private Infantry CreatePreviewUnit(string unitId)
    {
        var info = UnitProductionDatabase.GetInfo(unitId);
        if (string.IsNullOrEmpty(info.ScenePath)) return null;
        var scene = GD.Load<PackedScene>(info.ScenePath);
        return scene?.Instantiate() as Infantry;
    }

    private void EnterExternalProductionTargetMode(string unitId)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || currentUnit == null) return;

        pendingProductionUnitId = unitId;
        pendingProductionPreview = CreatePreviewUnit(unitId);
        productionTargetGrids = currentUnit.GetValidExternalProductionGrids(pendingProductionPreview);

        foreach (var g in productionTargetGrids)
        {
            g.pathIcon?.Show();
            g.OnClickGrid = to => OnExternalProductionTargetSelected(to);
        }
        foreach (var g in gm.gridManager.grids)
        {
            if (!productionTargetGrids.Contains(g))
            {
                g.OnClickEmpty = () => CancelExternalTargetMode();
                g.OnClickGrid = to => CancelExternalTargetMode();
            }
        }
        HideProductionBuildButtons();
        Hide();
    }

    private void OnExternalProductionTargetSelected(Grids target)
    {
        if (currentUnit == null || pendingProductionUnitId == null) return;
        var unitId = pendingProductionUnitId;
        ClearExternalTargetMode();
        currentUnit.StartProduction(unitId, false, target);
        // 非自动化：开工即消耗行动，菜单收起
        if (!currentUnit.productionAutomation)
        {
            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.ClearSelectedInfantry();
        }
    }

    private void CancelExternalTargetMode()
    {
        ClearExternalTargetMode();
        if (currentUnit != null && IsInstanceValid(currentUnit))
            ShowMenu(currentUnit, currentUnit.isMoved);
    }

    private void ClearExternalTargetMode()
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        foreach (var g in productionTargetGrids)
        {
            if (g == null || !IsInstanceValid(g)) continue;
            g.pathIcon?.Hide();
            g.OnClickGrid = null;
        }
        var allGrids = gm?.gridManager?.grids ?? new List<Grids>();
        foreach (var g in allGrids)
        {
            if (g == null || !IsInstanceValid(g)) continue;
            if (!productionTargetGrids.Contains(g))
            {
                g.OnClickEmpty = null;
                g.OnClickGrid = null;
            }
        }
        productionTargetGrids.Clear();
        pendingProductionUnitId = null;
        pendingProductionPreview?.Free();
        pendingProductionPreview = null;
        pendingConstructionType = null;
        deployMode = false;
    }

    private void OnProduceCancelPressed()
    {
        GetViewport().SetInputAsHandled();
        currentUnit?.CancelProduction();
        Hide();
    }

    // ==================== 建造 ====================

    private void OnBuildPressed(bool internalMode)
    {
        GetViewport().SetInputAsHandled();
        if (currentUnit == null) return;
        if (!currentUnit.CanStartConstruction(internalMode)) return;
        ShowConstructionChoices(internalMode);
    }

    private void ShowConstructionChoices(bool internalMode)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || currentUnit == null) return;

        if (productionChoiceContainer == null)
        {
            productionChoiceContainer = new VBoxContainer();
            AddChild(productionChoiceContainer);
        }
        foreach (Node child in productionChoiceContainer.GetChildren()) child.QueueFree();
        productionChoiceContainer.Visible = true;
        productionChoiceContainer.Position = new Vector2(Size.X + 8, 0);

        var unit = currentUnit;
        bool internal_ = internalMode;
        foreach (var type in unit.constructionWhitelist)
        {
            string facilityType = type;
            var btn = new Button();
            btn.CustomMinimumSize = new Vector2(120, 32);
            btn.AddThemeFontSizeOverride("font_size", 13);
            // 地形建造条目显示友好名（Terrain:PIPESEAM → 管道接缝）
            var terrainEntry = Infantry.ParseTerrainBuildEntry(facilityType);
            string weaponKey = Infantry.ParseWeaponBuildEntry(facilityType);
            string display = terrainEntry.HasValue
                ? TranslateGroup.T(terrainEntry.Value == GridType.PIPESEAM ? "管道接缝" : terrainEntry.Value.ToString())
                : weaponKey != null ? $"{TranslateGroup.T("兵器")}:{weaponKey}"
                : facilityType;
            TranslateGroup.SetText(btn, display);
            btn.Pressed += () =>
            {
                productionChoiceContainer.Visible = false;
                OnConstructionTypeChosen(facilityType, internal_);
            };
            productionChoiceContainer.AddChild(btn);
        }
        var cancelBtn = new Button();
        cancelBtn.CustomMinimumSize = new Vector2(120, 32);
        cancelBtn.AddThemeFontSizeOverride("font_size", 13);
        TranslateGroup.SetText(cancelBtn, "取消");
        cancelBtn.Pressed += () => productionChoiceContainer.Visible = false;
        productionChoiceContainer.AddChild(cancelBtn);
    }

    private void OnConstructionTypeChosen(string facilityType, bool internalMode)
    {
        if (currentUnit == null || !IsInstanceValid(currentUnit)) return;

        if (internalMode)
        {
            currentUnit.StartConstruction(facilityType, true);
        }
        else if (currentUnit.constructionAutoPlace)
        {
            var grid = currentUnit.FindValidExternalConstructionGrid(facilityType);
            if (grid != null) currentUnit.StartConstruction(facilityType, false, grid);
            else GD.PushWarning("[建造] 外部范围内没有可放置的格子");
        }
        else
        {
            EnterExternalConstructionTargetMode(facilityType);
        }
    }

    private void EnterExternalConstructionTargetMode(string facilityType)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || currentUnit == null) return;

        pendingConstructionType = facilityType;
        productionTargetGrids = currentUnit.GetValidExternalConstructionGrids(facilityType);

        foreach (var g in productionTargetGrids)
        {
            g.pathIcon?.Show();
            g.OnClickGrid = to => OnExternalConstructionTargetSelected(to);
        }
        foreach (var g in gm.gridManager.grids)
        {
            if (!productionTargetGrids.Contains(g))
            {
                g.OnClickEmpty = () => CancelExternalTargetMode();
                g.OnClickGrid = to => CancelExternalTargetMode();
            }
        }
        HideProductionBuildButtons();
        Hide();
    }

    private void OnExternalConstructionTargetSelected(Grids target)
    {
        if (currentUnit == null || pendingConstructionType == null) return;
        string type = pendingConstructionType;
        ClearExternalTargetMode();
        currentUnit.StartConstruction(type, false, target);
        if (!currentUnit.constructionAutomation)
        {
            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.ClearSelectedInfantry();
        }
    }

    private void OnBuildCancelPressed()
    {
        GetViewport().SetInputAsHandled();
        currentUnit?.CancelConstruction();
        Hide();
    }

    // ==================== 部署舱内建筑 ====================

    private void OnDeployFacilityPressed()
    {
        GetViewport().SetInputAsHandled();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || currentUnit == null) return;
        if (currentUnit.carriedFacilityTypes == null || currentUnit.carriedFacilityTypes.Count == 0) return;

        // 部署范围：相邻一圈
        deployMode = true;
        productionTargetGrids = new List<Grids>();
        int x = currentUnit.grid?.GridIndex.X ?? -1;
        int y = currentUnit.grid?.GridIndex.Y ?? -1;
        if (x < 0) return;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int gx = x + dx, gy = y + dy;
                if (!gm.unitManager.IsValidGrid(new Vector2I(gx, gy))) continue;
                var g = gm.gridManager.map[gx, gy];
                if (g == null || !gm.gridManager.IsRangeAllowed(currentUnit.grid, g, currentUnit.deployFacilityCanCrossScreens)) continue;
                if (g.city != null && IsInstanceValid(g.city)) continue;
                if (g.weapon != null) continue;
                if (!currentUnit.CanBuildFacilityOnTerrain(currentUnit.carriedFacilityTypes[0], g.gridType)) continue;   // ⑫ 可建造地形
                productionTargetGrids.Add(g);
            }

        foreach (var g in productionTargetGrids)
        {
            g.pathIcon?.Show();
            g.OnClickGrid = to => OnDeployTargetSelected(to);
        }
        foreach (var g in gm.gridManager.grids)
        {
            if (!productionTargetGrids.Contains(g))
            {
                g.OnClickEmpty = () => CancelExternalTargetMode();
                g.OnClickGrid = to => CancelExternalTargetMode();
            }
        }
        HideProductionBuildButtons();
        Hide();
    }

    private void OnDeployTargetSelected(Grids target)
    {
        if (currentUnit == null) return;
        ClearExternalTargetMode();
        if (!currentUnit.DeployCarriedFacility(target)) return;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.ClearSelectedInfantry();
    }
}
