using Godot;
using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;

// TerrainEditor · GridClick（由 TerrainEditor.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class TerrainEditor
{

// ========== 修改后的格子点击处理（整合生成系统）=========
public void OnGridClickedForEdit(Grids grid)
{
        if (isCellBrushMode) return;   // 画笔点击由 _Input 统一处理（真空处也能拓），此处防双触
        // ✅ 连续放置开关：关=只有真实单击才放置（拖拽掠过的一律忽略→可拖屏）；开=维持旧行为
        if (!continuousPlacement)
        {
            if (!_tapPending) return;
            _tapPending = false;
        }

        // ✅ 虚空区域（矮屏下方填充）不可放置/选源/任何操作
        if (grid.screenId < 0)
        {
            ShowImportHint(TranslateGroup.T("🕳️ 虚空区域不可操作"));
            return;
        }
    if (!IsEditMode || grid == null) return;

    // 区域天气画笔独占本次点格，不触发生成、绑定、销毁或对象选择。
    if (isRegionalWeatherEditMode)
    {
        ApplyRegionalWeatherBrush(grid);
        return;
    }

    var mapHelper = GetTree()?.GetFirstNodeInGroup("map_editor_helper") as MapEditorHelper;

    selectedGrid = grid;

    // ✅ ===== 绑定选择模式：点格=增删绑定，不弹任何菜单（确认/取消走底部按钮） =====
    if (isBindingSelectMode)
    {
        HandleBindingGridClick(grid);
        return;
    }

    // ✅ ===== N屏传送链编辑模式：点格选源/目标，不弹任何菜单 =====
    if (isTeleportLinkMode)
    {
        HandleTeleportGridClick(grid);
        return;
    }

    // ✅ ===== 最优先：多格兵器预览确认（生成菜单 + 保存槽 统一走这里）=====
    if (isMultiTilePreviewMode && !string.IsNullOrEmpty(pendingMultiTileScenePath))
    {
        if (previewMultiTileGrids.Contains(grid))
        {
            var savedPath = pendingMultiTileScenePath;
            var savedTeam = pendingMultiTileTeam;
            ConfirmMultiTilePlacement();
            TrySpawnSymmetric(grid, savedPath, savedTeam, SpawnMode.Weapon);
            // 如果是从保存槽/CustomConfigUI进来的，清除它的pending状态
            if (pendingSpawnMode != SpawnMode.None)
            {
                pendingSpawnMode = SpawnMode.None;
                pendingSpawnScenePath = "";
                var h = GetNodeOrNull<Label>("SpawnModeHint");
                h?.QueueFree();
            }
        }
        else
        {
            ClearMultiTilePreview();
            TranslateGroup.SetText(spawnHintLabel, "❌ 已取消预览（点击范围外）");
            spawnHintLabel.AddThemeColorOverride("font_color", Colors.Yellow);
            if (pendingSpawnMode != SpawnMode.None)
            {
                pendingSpawnMode = SpawnMode.None;
                pendingSpawnScenePath = "";
                var h = GetNodeOrNull<Label>("SpawnModeHint");
                h?.QueueFree();
            }
        }
        return;
    }

    // ✅ ===== 多格单位预览确认（issue#1：优先级同多格兵器）=====
    if (isMultiTileUnitPreviewMode && !string.IsNullOrEmpty(pendingMultiTileUnitTemplatePath))
    {
        if (previewMultiTileUnitGrids.Contains(grid))
        {
            var savedUnitPath = pendingMultiTileUnitTemplatePath;
            var savedUnitTeam = pendingMultiTileUnitTeam;
            ConfirmMultiTileUnitPlacement();
            TrySpawnSymmetric(grid, savedUnitPath, savedUnitTeam, SpawnMode.Unit);
            // 如果是从保存槽/CustomConfigUI进来的，清除它的pending状态
            if (pendingSpawnMode != SpawnMode.None)
            {
                pendingSpawnMode = SpawnMode.None;
                pendingSpawnScenePath = "";
                var h = GetNodeOrNull<Label>("SpawnModeHint");
                h?.QueueFree();
            }
        }
        else
        {
            ClearMultiTileUnitPreview();
            TranslateGroup.SetText(spawnHintLabel, "❌ 已取消多格单位预览（点击范围外）");
            spawnHintLabel.AddThemeColorOverride("font_color", Colors.Yellow);
            if (pendingSpawnMode != SpawnMode.None)
            {
                pendingSpawnMode = SpawnMode.None;
                pendingSpawnScenePath = "";
                var h = GetNodeOrNull<Label>("SpawnModeHint");
                h?.QueueFree();
            }
        }
        return;
    }

    // ✅ ===== 优先处理生成模式（生成菜单打开 + 已选模板时，不拦截）=====
    if (currentSpawnMode != SpawnMode.None && !string.IsNullOrEmpty(selectedTemplatePath))
    {
        // 如果处于保存/加载等待模式，先取消
        var saveSlots = GetTree()?.GetFirstNodeInGroup("editor_save_slots") as EditorSaveSlots;
        if (saveSlots != null)
        {
            if (saveSlots.IsWaitingForSaveTarget) saveSlots.CancelPendingSave();
            if (saveSlots.IsWaitingForLoadTarget) saveSlots.CancelPendingLoad();
        }

        if (currentSpawnMode == SpawnMode.Unit)
        {
            // ✅ issue#1：多格单位走预览确认（单格单位在 TryPreviewMultiTileUnit 内直通直生）
            TryPreviewMultiTileUnit(grid, selectedTemplatePath, unitTemplates.First(t => t.ScenePath == selectedTemplatePath).Team);
            DestroySpawnGhost();   // ✅ 进入多格预览/实际放置后销毁幽灵预览（绿色范围预览接管）
        }
        else if (currentSpawnMode == SpawnMode.Weapon)
        {
            // 多格兵器走预览（确认逻辑在最前面统一处理）
            TryPreviewMultiTileWeapon(grid, selectedTemplatePath, weaponTemplates.First(t => t.ScenePath == selectedTemplatePath).Team);
            DestroySpawnGhost();   // ✅ 进入多格预览/实际放置后销毁幽灵预览（蓝色范围预览接管）
            return;
        }
        else if (currentSpawnMode == SpawnMode.Facility)
        {
            SpawnFacilityAtGrid(grid, selectedTemplatePath, facilityTemplates.First(t => t.ScenePath == selectedTemplatePath).Team);
            TrySpawnSymmetric(grid, selectedTemplatePath, facilityTemplates.First(t => t.ScenePath == selectedTemplatePath).Team, SpawnMode.Facility);
            DestroySpawnGhost();   // ✅ 实际放置后销毁幽灵预览
        }
        return;
    }

    // ✅ 其他UI面板打开时，才拦截
    if (spawnMenuPanel?.Visible == true) return;
    if (selectionMenuPanel?.Visible == true) return;
    if (propertyEditorPanel?.Visible == true) return;
    if (terrainMenuPanel?.Visible == true) return;
    if (globalConfigPanel?.Visible == true) return;
    if (batchTerrainPanel?.Visible == true) return;

    // ✅ MapEditorHelper 模式优先处理
    if (mapHelper != null)
    {
        if (mapHelper.IsShapeFillMode) { mapHelper.OnGridClickForShapeFill(grid); return; }
        if (mapHelper.IsAreaFillMode || mapHelper.IsTerrainFillMode) { mapHelper.OnGridClickForAreaFill(grid); return; }
        if (mapHelper.IsSelectMode) { mapHelper.OnGridClickForSelect(grid); return; }
        if (mapHelper.IsPasteMode) { mapHelper.OnGridClickForPaste(grid); return; }
        if (mapHelper.IsSymmetryMode) { mapHelper.OnGridClickForSymmetry(grid); return; }
        if (!string.IsNullOrEmpty(mapHelper.pendingTexturePath))
        {
            if (grid.infantry != null) { mapHelper.OnEntityClickForTextureReplace(grid.infantry); return; }
            if (grid.weapon != null) { mapHelper.OnEntityClickForTextureReplace(grid.weapon); return; }
            if (grid.city != null) { mapHelper.OnEntityClickForTextureReplace(grid.city); return; }
        }
    }

    // ✅ EditorSaveSlots 等待保存目标
    var saveSlots2 = GetTree()?.GetFirstNodeInGroup("editor_save_slots") as EditorSaveSlots;
    if (saveSlots2 != null && saveSlots2.IsWaitingForSaveTarget)
    {
        saveSlots2.OnGridClickedForSave(grid);
        return;
    }

    // ✅ EditorSaveSlots 等待加载目标
    var loadSlots = GetTree()?.GetFirstNodeInGroup("editor_save_slots") as EditorSaveSlots;
    if (loadSlots != null && loadSlots.IsWaitingForLoadTarget)
    {
        loadSlots.OnGridClickedForLoad(grid);
        return;
    }

    // ✅ 优先处理贴图替换模式（TerrainEditor自己的）
    if (isTextureApplyMode)
    {
        ExecuteTextureApply(grid);
        return;
    }

    // ✅ 优先处理待生成/待应用模式（CustomConfigUI加载保存的对象）
    if (HandlePendingSpawn(grid))
        return;

    // ✅ 批量地形放置模式：直接改地形，不经过选择菜单
    if (isBatchTerrainMode)
    {
        ApplyBatchBrushAt(grid);
        return;
    }

    // 处理销毁模式
    if (isDestroyMode)
    {
        DestroyTargetAtGrid(grid);
        return;
    }

    // 原有逻辑：收集所有可编辑对象
    var allTargets = new List<EditTarget>();

    var units = grid.infantries?
        .Where(u => u != null && IsInstanceValid(u))
        .OrderByDescending(u => u.health)
        .ToList() ?? new List<Infantry>();

    foreach (var unit in units)
    {
        string teamEmoji = unit.team switch { "Player1" => "🔴", "Player2" => "🔵", "Player0" => "⚪", "Player" => "🟣", _ => "⚪" };
        string hpText = unit.maxHealth <= 999 ? $"HP:{Mathf.CeilToInt(unit.health / 10f)}" : $"HP:{unit.health}";
        allTargets.Add(new EditTarget
        {
            TargetType = "unit",
            DisplayName = $"{teamEmoji} [{TranslateGroup.T("单位")}:{unit.GetType().Name}] {unit.Name} ({hpText})",
            TargetObject = unit,
            Color = new Color(0.3f, 0.5f, 0.8f)
        });
    }

    var weapons = grid.weapons?
        .Where(w => w != null && IsInstanceValid(w))
        .ToList() ?? new List<Weapon>();

    foreach (var weapon in weapons)
    {
        string teamEmoji = weapon.team switch { "Player1" => "🔴", "Player2" => "🔵", "Player0" => "⚪", "Player" => "🟣", _ => "⚪" };
        string hpText = weapon.maxHealth <= 999 ? $"HP:{Mathf.CeilToInt(weapon.health / 10f)}" : $"HP:{weapon.health}";
        allTargets.Add(new EditTarget
        {
            TargetType = "weapon",
            DisplayName = $"{teamEmoji} [{TranslateGroup.T("兵器")}:{weapon.GetType().Name}] {weapon.Name} ({hpText})",
            TargetObject = weapon,
            Color = new Color(0.7f, 0.4f, 0.3f)
        });
    }

    // 添加设施选项
    if (grid.city != null)
    {
        allTargets.Add(new EditTarget
        {
            TargetType = "facility",
            DisplayName = $"🏙️ [{TranslateGroup.T("设施")}:{grid.city.GetType().Name}] {grid.city.facilityTeam}",
            TargetObject = grid.city,
            Color = new Color(0.8f, 0.6f, 0.2f)
        });
    }

    // 添加功能选项
    allTargets.Add(new EditTarget
    {
        TargetType = "grid",
        DisplayName = TranslateGroup.T("🟫 修改格子地形/属性"),
        TargetObject = grid,
        Color = new Color(0.4f, 0.7f, 0.4f)
    });

    allTargets.Add(new EditTarget
    {
        TargetType = "spawn",
        DisplayName = TranslateGroup.T("🎮 生成新单位/兵器..."),
        TargetObject = null,
        Color = new Color(0.4f, 0.6f, 0.9f)
    });

    if (allTargets.Count == 1)
    {
        OpenTerrainQuickSwitch(grid);
    }
    else
    {
        ShowMultiSelectMenu(allTargets);
    }
}

// 修改多选菜单，添加生成分支
private void ShowMultiSelectMenu(List<EditTarget> targets)
{
    foreach (var child in selectionButtonContainer.GetChildren())
        child.QueueFree();

    int objectCount = targets.Count(t => t.TargetType != "grid" && t.TargetType != "spawn");
    selectionTitleLabel.Text = $"{TranslateGroup.T("格子")} {selectedGrid.GridIndex} - {TranslateGroup.T("选择操作")} ({objectCount}{TranslateGroup.T("个对象")})";

    foreach (var target in targets)
    {
        Button btn;
        switch (target.TargetType)
        {
            case "unit":
                btn = CreateSelectionButton(target.DisplayName, target.Color, () => {
                    CloseSelectionMenu();
                    OpenUnitEditor((Infantry)target.TargetObject);
                });
                break;
            case "weapon":
                btn = CreateSelectionButton(target.DisplayName, target.Color, () => {
                    CloseSelectionMenu();
                    OpenWeaponEditor((Weapon)target.TargetObject);
                });
                break;
            case "grid":
                btn = CreateSelectionButton(target.DisplayName, target.Color, () => {
                    CloseSelectionMenu();
                    OpenTerrainQuickSwitch(selectedGrid);
                });
                break;
            case "spawn":
                btn = CreateSelectionButton(target.DisplayName, target.Color, () => {
                    CloseSelectionMenu();
                    OpenSpawnMenu();
                });
                break;
            case "facility":
                btn = CreateSelectionButton(target.DisplayName, target.Color, () => {
                    CloseSelectionMenu();
                    OpenFacilityMenu(selectedGrid);
                });
                break;
            default:
                continue;
        }
        // ✅ 对象类选项（单位/兵器/设施）在文字前加实际 sprite 首帧小图标；grid/spawn 无对象自动跳过
        // 防御（开创人2026-11-11 编辑模式弹窗事故）：图标只是装饰，任何异常只记日志+跳过该按钮图标，绝不允许中断菜单构建。
        try { AddLiveObjectIcon(btn, target.TargetObject); }
        catch (System.Exception iconEx)
            {
                GD.PushError($"[编辑模式] 选择菜单图标生成失败({target.TargetType}): {iconEx}");
                ShowEditorError($"选择菜单图标({target.TargetType}): {iconEx.Message}");
            }
        selectionButtonContainer.AddChild(btn);
    }

    var viewportSize = GetViewport().GetVisibleRect().Size;
    selectionMenuPanel.Position = new Vector2(
        (viewportSize.X - selectionMenuPanel.Size.X) / 2,
        (viewportSize.Y - selectionMenuPanel.Size.Y) / 2
    );
    selectionMenuPanel.Visible = true;
    isMenuOpen = true;
}

    // ========== ✅ 创建选择菜单（单位/兵器/格子 多选一） ==========
    private void CreateSelectionMenu()
    {
        selectionMenuPanel = new Panel();
        selectionMenuPanel.Name = "SelectionMenuPanel";
        selectionMenuPanel.CustomMinimumSize = new Vector2(320, 400);
        selectionMenuPanel.SetAnchorsPreset(LayoutPreset.Center);
        selectionMenuPanel.Visible = false;
        selectionMenuPanel.ZIndex = 1000;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.12f, 0.12f, 0.18f, 0.96f);
        panelStyle.SetCornerRadiusAll(16);
        panelStyle.SetBorderWidthAll(3);
        panelStyle.BorderColor = new Color(0.6f, 0.6f, 0.8f);
        selectionMenuPanel.AddThemeStyleboxOverride("panel", panelStyle);

        selectionTitleLabel = new Label();
        selectionTitleLabel.Name = "SelectionTitle";
        TranslateGroup.SetText(selectionTitleLabel, "选择编辑目标");
        selectionTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        selectionTitleLabel.AddThemeFontSizeOverride("font_size", 20);
        selectionTitleLabel.AddThemeColorOverride("font_color", Colors.Yellow);
        selectionTitleLabel.CustomMinimumSize = new Vector2(0, 35);
        selectionTitleLabel.Position = new Vector2(0, 12);
        selectionTitleLabel.Size = new Vector2(320, 35);
        selectionMenuPanel.AddChild(selectionTitleLabel);


        var closeBtn = new Button();
        closeBtn.Text = "✕";
        closeBtn.CustomMinimumSize = new Vector2(32, 32);
        closeBtn.Position = new Vector2(280, 10);
        closeBtn.Pressed += CloseSelectionMenu;
        selectionMenuPanel.AddChild(closeBtn);

        var scroll = new ScrollContainer();
        scroll.Position = new Vector2(15, 55);
        scroll.Size = new Vector2(290, 330);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        selectionButtonContainer = new VBoxContainer();
        selectionButtonContainer.Name = "SelectionButtonContainer";
        selectionButtonContainer.AddThemeConstantOverride("separation", 8);
        selectionButtonContainer.Size = new Vector2(290, 0);

        scroll.AddChild(selectionButtonContainer);
        selectionMenuPanel.AddChild(scroll);

        AddChild(selectionMenuPanel);
            var dragHandle = new Panel();
    dragHandle.CustomMinimumSize = new Vector2(320, 30);
    dragHandle.Position = new Vector2(0, 0);
    var handleStyle = new StyleBoxFlat();
    handleStyle.BgColor = new Color(0.3f, 0.3f, 0.4f, 0.5f);
    handleStyle.SetCornerRadiusAll(8);
    dragHandle.AddThemeStyleboxOverride("panel", handleStyle);

    var dragHint = new Label();
    TranslateGroup.SetText(dragHint, "⋮⋮ 拖拽移动");
    dragHint.AddThemeFontSizeOverride("font_size", 10);
    dragHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.8f));
    dragHint.Position = new Vector2(8, 2);
    dragHandle.AddChild(dragHint);
    }
}
