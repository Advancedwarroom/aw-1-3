using Godot;
using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;

public partial class TerrainEditor : Control
{
    [ExportGroup("")]
    [Export] public GameManager gameManager;
    [Export] public ExtraSave extraSave;
    [Export] public GridManager gridManager;
    [Export] public CustomConfigUI customConfigUI; // ✅ Extra配置界面

    // ========== ✅ 编辑器图标预览（生成菜单/地形菜单/选择菜单/幽灵光标） ==========
    [ExportGroup("编辑器图标预览")]
    [Export] public float templateIconSize = 40f;       // ✅ 生成菜单模板图标边长（像素）
    [Export] public float terrainIconPreviewSize = 32f; // ✅ 地形菜单图标边长（像素）
    [Export] public float selectionIconSize = 32f;      // ✅ 选择/销毁菜单对象图标边长（像素）
    [Export] public bool enableGhostPreview = true;     // ✅ 是否启用光标幽灵预览
    [Export] public float ghostPreviewAlpha = 0.6f;     // ✅ 幽灵预览透明度（0~1）
    [Export] public float teamIconTintLighten = 0.3f;   // ✅ 队伍图标染色提亮幅度

    // ========== UI 组件 ==========
    private Button modeToggleButton;

    // 主选择菜单（单位/兵器/格子）
    private Panel selectionMenuPanel;
    private VBoxContainer selectionButtonContainer;
    private Label selectionTitleLabel;

    // 地形编辑菜单
    private Panel terrainMenuPanel;
    private ScrollContainer terrainScrollContainer;
    private VBoxContainer terrainButtonContainer;
    private Label terrainTitleLabel;

    // 属性编辑器面板（通用：单位+兵器）
    private Panel propertyEditorPanel;
    private ScrollContainer propertyScrollContainer;
    private VBoxContainer propertyContainer;
    private Label propertyEditorTitle;
    private Button propertyEditorCloseBtn;
    private Button propertyEditorSaveBtn;

    private SpawnMode currentSpawnMode = SpawnMode.None;
    private string selectedTemplatePath = "";
    private string selectedTemplateCategory = "";
    // 自定义伤害字段UI（格子专用）
    private VBoxContainer customDamageContainer;
    private CheckBox canDestroyCheckBox;
    private SpinBox fixedDamageSpinBox;
    private SpinBox fixedAttackSpinBox;
    private CheckBox canOverMaxCheckBox;
    private SpinBox maxHealthChangeSpinBox; // 最大血量增减
    private SpinBox weightChangeSpinBox;
    private OptionButton ammoKindOptionButton; // ✦优化：弹药种类选择
    private SpinBox ammoKindChangeSpinBox;    // ✦优化：选中种类增减
    private SpinBox ammoKindMaxSpinBox;       // ✦优化：选中种类上限增减
    private SpinBox fixedAmmoSpinBox;
    private Grids ammoKindEditGrid; // ✦优化：当前正在编辑弹药种类的格子
    private CheckBox ammoCanOverMaxCheckBox;
    private CheckBox ammoCanReachZeroCheckBox;
    private SpinBox fixedFuelSpinBox;
    private SpinBox maxFuelChangeSpinBox; // ✦优化：燃油上限增减
    private CheckBox fuelCanOverMaxCheckBox;
    private CheckBox fuelCanReachZeroCheckBox;
    private VBoxContainer ammoFuelContainer;

    // 状态
    public bool IsEditMode { get; private set; } = false;

public bool IsAnyMenuOpen()
{
    return selectionMenuPanel?.Visible == true 
        || terrainMenuPanel?.Visible == true 
        || propertyEditorPanel?.Visible == true 
        || spawnMenuPanel?.Visible == true
        || globalConfigPanel?.Visible == true
        || batchTerrainPanel?.Visible == true
        || regionalWeatherPanel?.Visible == true;
}
public override void _Ready()
{
    ScreenErrorLogger.EnsureInstalled(this);   // 开创人2026-11-11：全引擎报错上屏
    AddToGroup("terrain_editor");

    // ✅ 自定义兵种（res://Prefabs/CustomUnits/）自动加入编辑器单位面板
    foreach (var kv in UnitProductionDatabase.Units)
    {
        if (!kv.Value.ScenePath.StartsWith(UnitProductionDatabase.CustomUnitDir)) continue;
        if (unitTemplates.Any(t => t.ScenePath == kv.Value.ScenePath)) continue;
        unitTemplates.Add(new UnitTemplate { DisplayName = kv.Value.DisplayName, ScenePath = kv.Value.ScenePath,
            Category = TranslateGroup.T("自定义"), Team = "Player1", DisplayColor = new Color(0.7f, 0.6f, 0.4f),
            Description = $"{TranslateGroup.T("自定义兵种")} {kv.Key}  {TranslateGroup.T("造价")}{kv.Value.Cost}" });
    }

    if (gameManager == null)
        gameManager = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
    if (gridManager == null)
        gridManager = GetTree().GetFirstNodeInGroup("grid_manager") as GridManager;
    if (customConfigUI == null)
        customConfigUI = GetTree().GetFirstNodeInGroup("custom_config_ui") as CustomConfigUI;

    CreateModeToggleButton();
    CreateSelectionMenu();
    CreateTerrainMenu();
    CreatePropertyEditor();
    CreateSpawnMenu();
    CreateGlobalConfigPanel();
    CreateRegionalWeatherPanel();

    var viewportSize = GetViewport().GetVisibleRect().Size;
    modeToggleButton.SetAnchorsPreset(LayoutPreset.TopLeft);
    modeToggleButton.Position = new Vector2(viewportSize.X - 160, viewportSize.Y - 70);

    // ✅ 创建批量地形按钮（编辑模式专用）
    CreateBatchTerrainButton();
    batchTerrainBtn.SetAnchorsPreset(LayoutPreset.TopLeft);
    batchTerrainBtn.Position = new Vector2(viewportSize.X - 160, viewportSize.Y - 130);
    batchTerrainBtn.Visible = false; // 初始隐藏，进入编辑模式时显示

    // ✅ 实例化编辑器助手组件
    var mapHelper = new MapEditorHelper();
    mapHelper.Name = "MapEditorHelper";
    mapHelper.gridManager = gridManager;
    mapHelper.gameManager = gameManager;
    mapHelper.terrainEditor = this;
    mapHelper.extraSave = extraSave;
    AddChild(mapHelper);

    var saveSlots = new EditorSaveSlots();
    saveSlots.Name = "EditorSaveSlots";
    saveSlots.extraSave = extraSave;
    saveSlots.terrainEditor = this;
    saveSlots.gridManager = gridManager;
    saveSlots.gameManager = gameManager;
    AddChild(saveSlots);

    var panelDragger = new EditorPanelDragger();
    panelDragger.Name = "EditorPanelDragger";
    panelDragger.terrainEditor = this;
    AddChild(panelDragger);

    // ✅ 订阅格子悬停事件
    if (gridManager?.grids != null)
    {
        foreach (var grid in gridManager.grids)
        {
            if (grid != null)
            {
                grid.OnHoverGrid += (g) => {
                    var helper = GetTree()?.GetFirstNodeInGroup("map_editor_helper") as MapEditorHelper;
                    if (helper != null && helper.IsShapeFillMode)
                        helper.OnGridHoverForShapeFill(g);
                };
            }
        }
    }
}

// ========== 修改 _Input 添加 ESC 关闭生成菜单 ==========
[Export] public bool continuousPlacement = true;   // ✅ 连续放置：开=拖拽连刷；关=拖拽平移相机、单击才放置
private bool _tapPending;

// ✅ 延迟清除单击标记（release 下一帧再清，避免与格子 click 的时序竞争误杀单击）
private void ClearTapPendingFlag() => _tapPending = false;

public override void _Input(InputEvent @event)
{
        // ✅ 格子画笔：点击有格就扣/真空处点紧邻空地就拓（真空无格子节点，只能走全局坐标换算——开创人批2）
        if (isCellBrushMode && @event is InputEventMouseButton mbBrush && mbBrush.Pressed && mbBrush.ButtonIndex == MouseButton.Left)
        {
            if (gridManager != null)
            {
                var vp = gridManager.GetViewport();
                HandleBrushClickAt(vp.CanvasTransform.AffineInverse() * vp.GetMousePosition());   // GridManager非Node2D，经视口反变换取世界坐标
            }
        }

        // ✅ ESC 退出传送编辑模式（修复：进入后无有效退出路径）
        if (@event is InputEventKey ek && ek.Pressed && !ek.Echo && ek.Keycode == Key.Escape && isTeleportLinkMode)
        {
            CancelTeleportLinkMode();
            return;
        }
        // ✅ 单击记账：区分"真实按下"与"拖拽掠过"（连续放置开关用）
        // 释放改为延迟一帧清，防止 release 先于格子 click 到达时误杀单击；手机触摸走 ScreenTouch 分支
        if (@event is InputEventMouseButton emb && emb.ButtonIndex == MouseButton.Left)
        {
            if (emb.Pressed) _tapPending = true;
            else CallDeferred(nameof(ClearTapPendingFlag));
        }
        else if (@event is InputEventScreenTouch touch)
        {
            if (touch.Pressed) _tapPending = true;
            else CallDeferred(nameof(ClearTapPendingFlag));
        }
    if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
    {
        if (isRegionalWeatherEditMode || regionalWeatherPanel?.Visible == true)
        {
            CloseRegionalWeatherEditor();
            return;
        }

        if (globalConfigPanel?.Visible == true)
        {
            CloseGlobalConfigPanel();
            return;
        }

        if (spawnMenuPanel?.Visible == true)
        {
            CloseSpawnMenu();
            return;
        }
        if (propertyEditorPanel?.Visible == true)
        {
            ClosePropertyEditor();
            return;
        }
        if (terrainMenuPanel?.Visible == true)
        {
            CloseTerrainMenu();
            return;
        }
        if (selectionMenuPanel?.Visible == true)
        {
            CloseSelectionMenu();
            isMenuOpen = false;
            return;
        }

        // ✅ ESC 取消贴图替换模式
        if (isTextureApplyMode)
        {
            isTextureApplyMode = false;
            pendingTextureApplyPath = "";
            pendingTextureApplyTargetType = "";
            TranslateGroup.SetText(spawnHintLabel, "❌ 已取消贴图替换模式");
            spawnHintLabel.AddThemeColorOverride("font_color", Colors.Yellow);
            return;
        }

        // ✅ ESC 取消 N屏传送链编辑模式
        if (isTeleportLinkMode)
        {
            CancelTeleportLinkMode();
            TranslateGroup.SetText(spawnHintLabel, "❌ 已退出传送编辑模式");
            spawnHintLabel.AddThemeColorOverride("font_color", Colors.Yellow);
            return;
        }

        // ✅ ESC 取消保存槽等待模式
        var saveSlotsEsc = GetTree()?.GetFirstNodeInGroup("editor_save_slots") as EditorSaveSlots;
        if (saveSlotsEsc != null && saveSlotsEsc.IsWaitingForSaveTarget)
        {
            saveSlotsEsc.CancelPendingSave();
            TranslateGroup.SetText(spawnHintLabel, "❌ 已取消保存操作");
            spawnHintLabel.AddThemeColorOverride("font_color", Colors.Yellow);
            return;
        }
        if (saveSlotsEsc != null && saveSlotsEsc.IsWaitingForLoadTarget)
        {
            saveSlotsEsc.CancelPendingLoad();
            TranslateGroup.SetText(spawnHintLabel, "❌ 已取消加载操作");
            spawnHintLabel.AddThemeColorOverride("font_color", Colors.Yellow);
            return;
        }

        // ✅ ESC 取消待生成/应用模式
        if (pendingSpawnMode != SpawnMode.None || pendingTerrainData != null)
        {
            CancelPendingMode();
            return;
        }

        // ✅ ESC 退出批量地形模式
        if (isBatchTerrainMode)
        {
            StopBatchTerrainMode();
            return;
        }
        if (isMultiTilePreviewMode)
        {
            ClearMultiTilePreview();
            TranslateGroup.SetText(spawnHintLabel, "取消多格兵器放置");
            spawnHintLabel.AddThemeColorOverride("font_color", Colors.Yellow);
            return;
        }
        if (isMultiTileUnitPreviewMode)
        {
            ClearMultiTileUnitPreview();
            TranslateGroup.SetText(spawnHintLabel, "取消多格单位放置");
            spawnHintLabel.AddThemeColorOverride("font_color", Colors.Yellow);
            return;
        }
        if (batchTerrainPanel?.Visible == true)
        {
            CloseBatchTerrainPanel();
            return;
        }

        // ✅ ESC 取消 MapEditorHelper 的所有模式
        var mapHelper = GetTree()?.GetFirstNodeInGroup("map_editor_helper") as MapEditorHelper;
        if (mapHelper != null && (
            mapHelper.IsShapeFillMode || mapHelper.IsAreaFillMode || mapHelper.IsTerrainFillMode ||
            mapHelper.IsSelectMode || mapHelper.IsPasteMode || mapHelper.IsSymmetryMode))
        {
            mapHelper.CancelAllModes();
            TranslateGroup.SetText(spawnHintLabel, "❌ 已取消编辑器助手模式");
            spawnHintLabel.AddThemeColorOverride("font_color", Colors.Yellow);
            return;
        }

        // ESC 打开全局配置面板
        OpenGlobalConfigPanel();
    }
}

    // ========== 模式切换按钮 ==========
    private void CreateModeToggleButton()
    {
        modeToggleButton = new Button();
        modeToggleButton.Name = "ModeToggleButton";
        TranslateGroup.SetText(modeToggleButton, "🛠️ 编辑模式");
        modeToggleButton.CustomMinimumSize = new Vector2(140, 50);

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.3f, 0.5f, 0.3f, 0.9f);
        style.SetCornerRadiusAll(8);
        modeToggleButton.AddThemeStyleboxOverride("normal", style);

        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = new Color(0.4f, 0.6f, 0.4f, 0.95f);
        hoverStyle.SetCornerRadiusAll(8);
        modeToggleButton.AddThemeStyleboxOverride("hover", hoverStyle);

        modeToggleButton.AddThemeFontSizeOverride("font_size", 16);
        modeToggleButton.AddThemeColorOverride("font_color", Colors.White);
        modeToggleButton.Pressed += OnModeTogglePressed;

        AddChild(modeToggleButton);
    }

    private void OnModeTogglePressed() => SetEditMode(!IsEditMode);


public void SetEditMode(bool editMode)
{
    if (IsEditMode == editMode) return;
    
    IsEditMode = editMode;

    if (IsEditMode)
    {
        TranslateGroup.SetText(modeToggleButton, "🎮 游玩模式");
        var style = new StyleBoxFlat { BgColor = new Color(0.5f, 0.3f, 0.3f, 0.9f) };
        style.SetCornerRadiusAll(8);
        modeToggleButton.AddThemeStyleboxOverride("normal", style);
        var hoverStyle = new StyleBoxFlat { BgColor = new Color(0.6f, 0.4f, 0.4f, 0.95f) };
        hoverStyle.SetCornerRadiusAll(8);
        modeToggleButton.AddThemeStyleboxOverride("hover", hoverStyle);

        gameManager?.gridManager?.CloseRange();
        gameManager?.gridManager?.HideAttackRange();
        gameManager?.gridManager?.ClearWeaponRange();
        var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
        actionMenu?.Hide();
        gameManager?.ClearSelectedInfantry();

        // ✅ 关键修复：记录当前迷雾状态（从 FogOfWarManager 直接读取）
        var fowManager = GetTree()?.GetFirstNodeInGroup("fog_of_war_manager") as FogOfWarManager;
        
        // ✅ 确保 FogOfWarManager 已初始化
        if (fowManager != null && !fowManager.isInitialized)
        {
            // 强制初始化
            fowManager.CallDeferred(nameof(FogOfWarManager.DeferredInit));
        }
        
        fogStateBeforeEdit = fowManager?.isFogOfWarEnabled ?? true;

        // ✅ 关闭迷雾
        gameManager?.SetFogOfWarEnabled(false);
        
        // ✅ 显示批量地形按钮
        if (batchTerrainBtn != null)
            batchTerrainBtn.Visible = true;
        // ✅ 显示Extra配置按钮
        if (customConfigUI != null)
            customConfigUI.SetOpenButtonVisible(true);
        // ✅ 显示编辑器助手和保存槽按钮
        var helper = GetTree()?.GetFirstNodeInGroup("map_editor_helper") as MapEditorHelper;
        if (helper != null)
        {
            var hBtn = helper.GetNodeOrNull<Button>("HelperToggleBtn");
            if (hBtn != null) hBtn.Visible = true;
        }
        var slots = GetTree()?.GetFirstNodeInGroup("editor_save_slots") as EditorSaveSlots;
        if (slots != null)
        {
            var sBtn = slots.GetNodeOrNull<Button>("SlotsToggleBtn");
            if (sBtn != null) sBtn.Visible = true;
        }
        
        // ✅ 同步全局配置面板中的复选框
        if (fogOfWarCheckBox != null)
        {
            fogOfWarCheckBox.ButtonPressed = false;
            fogOfWarCheckBox.Disabled = true;  // 编辑模式下禁用
        }
    }
    else
    {
        TranslateGroup.SetText(modeToggleButton, "🛠️ 编辑模式");
        var style = new StyleBoxFlat { BgColor = new Color(0.3f, 0.5f, 0.3f, 0.9f) };
        style.SetCornerRadiusAll(8);
        modeToggleButton.AddThemeStyleboxOverride("normal", style);
        var hoverStyle = new StyleBoxFlat { BgColor = new Color(0.4f, 0.6f, 0.4f, 0.95f) };
        hoverStyle.SetCornerRadiusAll(8);
        modeToggleButton.AddThemeStyleboxOverride("hover", hoverStyle);

        CloseAllMenus();

        // ✅ 隐藏批量地形按钮，退出批量模式
        if (batchTerrainBtn != null)
            batchTerrainBtn.Visible = false;
        StopBatchTerrainMode();
        // ✅ 隐藏Extra配置按钮
        if (customConfigUI != null)
        {
            customConfigUI.SetOpenButtonVisible(false);
            customConfigUI.Hide(); // 关闭配置面板
        }
        // ✅ 隐藏编辑器助手和保存槽按钮
        var helperOff = GetTree()?.GetFirstNodeInGroup("map_editor_helper") as MapEditorHelper;
        if (helperOff != null)
        {
            var hBtn = helperOff.GetNodeOrNull<Button>("HelperToggleBtn");
            if (hBtn != null) hBtn.Visible = false;
            helperOff.GetNodeOrNull<Panel>("HelperPanel")?.Hide();
        }
        var slotsOff = GetTree()?.GetFirstNodeInGroup("editor_save_slots") as EditorSaveSlots;
        if (slotsOff != null)
        {
            var sBtn = slotsOff.GetNodeOrNull<Button>("SlotsToggleBtn");
            if (sBtn != null) sBtn.Visible = false;
            slotsOff.GetNodeOrNull<Panel>("SlotsPanel")?.Hide();
        }

        
        // ✅ 关键修复：先恢复迷雾状态，再解锁UI
        gameManager?.SetFogOfWarEnabled(fogStateBeforeEdit);
        
        // ✅ 关键修复：强制刷新迷雾（确保遮罩已创建）
        var fowManager = GetTree()?.GetFirstNodeInGroup("fog_of_war_manager") as FogOfWarManager;
        if (fogStateBeforeEdit && fowManager != null)
        {
            // 使用 CallDeferred 确保在下一帧执行，给 SetFogOfWarEnabled 时间完成初始化
            CallDeferred(nameof(DeferredRefreshFog));
        }
        
        // ✅ 解锁全局配置面板中的复选框并同步状态
        if (fogOfWarCheckBox != null)
        {
            fogOfWarCheckBox.Disabled = false;
            fogOfWarCheckBox.ButtonPressed = fogStateBeforeEdit;
        }

        // ✅ 修复：切回游玩模式时只刷新状态，不立即判定胜负
        // 避免空地图/刚导入地图时误报平局
        CallDeferred(nameof(DeferredCheckVictoryOnExitEdit));
    }
}

private void DeferredRefreshFog()
{
    var fowManager = GetTree()?.GetFirstNodeInGroup("fog_of_war_manager") as FogOfWarManager;
    if (fowManager != null)
    {
        fowManager.ForceRefresh();
    }
}

// ✅ 新增：切回游玩模式时只刷新状态，不立即判定胜负
// 避免空地图/刚导入地图时误报平局
private void DeferredCheckVictoryOnExitEdit()
{
    var gm = gameManager;
    if (gm != null && !gm.gameEnded)
    {
        // 只刷新迷雾，不判定胜负
        gm.fogOfWarManager?.ForceRefresh();
        // 注意：不调用 gm.CheckVictoryCondition()
        // 胜负判定应在正常游戏流程中（单位死亡、回合切换时）自动触发
    }
}

   private void CloseAllMenus()
    {
        selectionMenuPanel.Visible = false;
        terrainMenuPanel.Visible = false;
        propertyEditorPanel.Visible = false;
        spawnMenuPanel.Visible = false;
        globalConfigPanel.Visible = false;
        CloseRegionalWeatherEditor();
        if (batchTerrainPanel != null) batchTerrainPanel.Visible = false;
        isMenuOpen = false;
        selectedGrid = null;
        currentEditingTarget = null;
        currentSpawnMode = SpawnMode.None;
        isDestroyMode = false;
        selectedTemplatePath = "";
        DestroySpawnGhost();   // ✅ 关闭所有菜单时销毁幽灵预览
    }

    public bool ShouldBlockUnitOperations()
    {
        return IsEditMode || isMenuOpen;
    }

    private void MakeMenuDraggable(Control menuPanel, Control dragHandle = null)
    {
        Control handle = dragHandle ?? menuPanel;

        handle.GuiInput += (InputEvent @event) => {
            if (@event is InputEventMouseButton mouseEvent)
            {
                if (mouseEvent.ButtonIndex == MouseButton.Left)
                {
                    if (mouseEvent.Pressed)
                    {
                        isDraggingMenu = true;
                        currentDraggedMenu = menuPanel;
                        dragOffset = menuPanel.GlobalPosition - handle.GetGlobalMousePosition();
                    }
                    else
                    {
                        isDraggingMenu = false;
                        currentDraggedMenu = null;
                    }
                }
            }
        };
    }

    public override void _Process(double delta)
    {
        if (isDraggingMenu && currentDraggedMenu != null)
        {
            Vector2 newPosition = currentDraggedMenu.GetGlobalMousePosition() + dragOffset;

            var viewportSize = GetViewport().GetVisibleRect().Size;
            newPosition.X = Mathf.Clamp(newPosition.X, 0, viewportSize.X - currentDraggedMenu.Size.X);
            newPosition.Y = Mathf.Clamp(newPosition.Y, 0, viewportSize.Y - currentDraggedMenu.Size.Y);

            currentDraggedMenu.GlobalPosition = newPosition;
        }

        // ✅ 批量地形滑动铺设：按住鼠标左键在地图上拖动时自动铺设
        if (isBatchTerrainMode && IsEditMode && Input.IsMouseButtonPressed(MouseButton.Left))
        {
            if (gridManager != null && gridManager.map != null)
            {
                // ✅ 修复坐标偏移：使用视口坐标+CanvasTransform转换，正确处理Camera缩放/偏移
                Vector2 viewportPos = GetViewport().GetMousePosition();
                Transform2D canvasTransform = GetViewport().GetCanvasTransform();
                Vector2 worldPos = canvasTransform.AffineInverse() * viewportPos;
                
                int x = (int)((worldPos.X - gridManager.startPos.X) / gridManager.gridSize.X);
                int y = (int)((worldPos.Y - gridManager.startPos.Y) / gridManager.gridSize.Y);

                if (x >= 0 && x < gridManager.map.GetLength(0) && y >= 0 && y < gridManager.map.GetLength(1))
                {
                    var grid = gridManager.map[x, y];
                    if (grid != null && grid != lastBatchGrid)
                    {
                        lastBatchGrid = grid;
                        var oldTerrain = grid.gridType;
                        grid.gridType = batchTerrainType;
                        grid.BakeCowBrush(); // ✅ 放置/修改地形时把当前画笔风格烘焙进格子
                        // ✅ 多方向地形：地形变化后刷新方向（本格+邻格）
                        gridManager?.RefreshAllTerrainDirections();
                        UpdateGridVisual(grid);
                        PushEditorAction(EditorActionType.PlaceTerrain, grid, oldTerrain);
                    }
                }
            }
        }

        // ✅ 光标幽灵预览：跟随鼠标并吸附最近格子
        if (spawnGhost != null)
            UpdateSpawnGhostPosition();
    }



}
