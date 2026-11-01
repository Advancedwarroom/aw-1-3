// GameManager.cs - 添加完整的单位统计系统
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class GameManager : Node
{
    [ExportGroup("")]
    [Export] public GridManager gridManager;
    [Export] public UnitManager unitManager;
    [Export] public WeaponManager weaponManager;
    [Export] public FogOfWarManager fogOfWarManager;
    [Export] public TerrainEditor terrainEditor;
    public bool gameEnded = false;
    // 运行时统计：记录需要被摧毁的兵器列表（按团队）
    public Dictionary<string, List<Weapon>> victoryRequiredWeapons = new();
    // 势力失败系统：记录已失败的势力
    private HashSet<string> defeatedTeams = new();

    public List<Weapon> weapons = new List<Weapon>(); 
    // 各类单位列表
    public Godot.Collections.Array<Infantry> infantryUnits = new();
    public Godot.Collections.Array<Infantry> mechUnits = new();
    public Godot.Collections.Array<Infantry> ooziumUnits = new();
    public Godot.Collections.Array<Infantry> tankUnits = new();
    public Godot.Collections.Array<Infantry> mdTankUnits = new();
    public Godot.Collections.Array<Infantry> vehicleUnits = new();
    public Godot.Collections.Array<Infantry> artilleryUnits = new(); 
    public Godot.Collections.Array<Infantry> antiairUnits = new(); 
    public Godot.Collections.Array<Infantry> antiTankUnits = new();
    public Godot.Collections.Array<Infantry> apcUnits = new();
    public Godot.Collections.Array<Infantry> reconUnits = new();
    public Godot.Collections.Array<Infantry> flyBombUnits = new();
    public Godot.Collections.Array<Infantry> flareUnits = new();
    public Godot.Collections.Array<Infantry> bikeUnits = new();
    public Godot.Collections.Array<Infantry> repairBoatUnits = new();

    public Godot.Collections.Array<Infantry> PipeRunnerUnits = new();
    public Godot.Collections.Array<Infantry> lightTankUnits = new();
    public Godot.Collections.Array<Infantry> rocketUnits = new();
    public Godot.Collections.Array<Infantry> antiAirUnits = new();
    // 回合管理
    public int turns = 1;
    public int turnPhase = 1;
    public bool encounterPause = false;   // ✅ 回合中回合：遭遇战决策期间的全局冻结
    
    // ✅ 通用资金字典（支持多势力，Player1/Player2 兼容旧存档）
    private Godot.Collections.Dictionary<string, int> _teamFunds = new();
    private Godot.Collections.Dictionary<string, int> _teamFundsMax = new();

    // 当前选中的单位
    public Infantry selectedInfantry = null;
    public Weapon selectedWeapon = null;
    public bool isSelectingAttackTarget = false;

    // UI 引用
    public Label turnEndLabel;
    public Label TurnLabel;
    public RichTextLabel UnitLists;

    // ✅ 新增：缩放滑块和统计面板收缩
    public HSlider zoomSlider;
    public Button unitListsToggleBtn;
    public Control unitListsPanel;
    private bool unitListsExpanded = true;
    private CameraTouchController cameraController;

    public Dictionary<Infantry, UnitCategory> unitCategories = new();
    public StatsMode statsMode = StatsMode.Units;
    private Button unitStatsBtn;
    private Button facilityStatsBtn;
    private Button fundsStatsBtn;
    private Button valueStatsBtn;
    private Button nextPhaseButton;

    // ✅ 生产菜单拖拽
    private bool isDraggingProductionMenu = false;
    private Vector2 dragOffsetProduction = Vector2.Zero;
    private Control currentDraggedProductionMenu = null;

    // ✅ 导入地图时屏蔽胜利检查
    public bool blockVictoryCheck = false;

    public override void _Ready()
    {   

        AddToGroup("game_manager");
        
        // ✅ 初始化势力系统（动态调色板 + 多势力支持）
        TeamHelper.InitializeDefaults();
        TeamHelper.LoadFactionsFromExtraSave();
        
        RequestStoragePermissionsIfNeeded();
        CallDeferred(nameof(DeferredInit));
        turnEndLabel = GetTree().CurrentScene.GetNodeOrNull<Label>("TurnEndLabel");
        TurnLabel = GetTree().CurrentScene.GetNodeOrNull<Label>("TurnLabel");
        UnitLists = GetTree().CurrentScene.GetNodeOrNull<RichTextLabel>("UnitLists");

        // 确保 GridManager 先初始化
        gridManager?.Init();

        // 延迟一帧确保所有节点就绪
        CallDeferred(nameof(DeferredUnitInit));

        UpdateTurnLabel();
        UpdateUnitLists();  // ✅ 初始化单位统计

        // ✅ 创建统计切换按钮
        CreateStatsToggleButtons();
        SetProcessInput(true);
            if (restartButton != null)
    {
        restartButton.Pressed += OnRestartPressed;

        // ========== ✅ 战争迷雾初始化 ==========
        if (fogOfWarManager == null)
        {
            fogOfWarManager = GetTree().GetFirstNodeInGroup("fog_of_war_manager") as FogOfWarManager;
        }

    }

    if (turnEndLabel != null) turnEndLabel.ZIndex = 200;
    if (TurnLabel != null) TurnLabel.ZIndex = 200;
    if (UnitLists != null) UnitLists.ZIndex = 200;
    gameEnded = false;
    // ✅ 新对局：重置全局可生成单位白名单（Lab解锁）
    UnitProductionWhitelist.Reset();
    Lab.ResetGlobalCount();

        if (fogOfWarManager == null)
    {
        fogOfWarManager = GetTree().GetFirstNodeInGroup("fog_of_war_manager") as FogOfWarManager;
    }

    // 延迟刷新战争迷雾，确保所有节点就绪
    if (fogOfWarManager != null && fogOfWarManager.isFogOfWarEnabled)
    {
        CallDeferred(nameof(DeferredFogRefresh));
    }

    // ✅ 确保 AI_Manager 存在
    var aiManager = GetTree().GetFirstNodeInGroup("ai_manager") as AI_Manager;
    if (aiManager == null)
    {
        aiManager = new AI_Manager();
        aiManager.Name = "AI_Manager";
        aiManager.gameManager = this;
        AddChild(aiManager);
    }

    gameEnded = false;
}

private void DeferredFogRefresh()
{
    fogOfWarManager?.RefreshFog();
}
    // GameManager.cs - 在类顶部添加
// 已移除：三连击/右键切换回合逻辑，改用独立 UI 按钮

public override void _Input(InputEvent @event)
{
    var terrainEditor = GetTree().GetFirstNodeInGroup("terrain_editor") as TerrainEditor;
    if (terrainEditor != null && terrainEditor.ShouldBlockUnitOperations())
        return;

    if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
    {
        if (productionMenuPanel != null && productionMenuPanel.Visible)
        {
            HideProductionMenu();
            GetViewport().SetInputAsHandled();
            return;
        }
    }

    if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
    {
        // 左键/右键不再用于切换回合，使用专用 UI 按钮
    }
}

    private void OnRestartPressed()
{
     GetViewport().SetInputAsHandled();
    RestartGame();
}
    private void DeferredInit()
    {
        // 1. 获取UI引用（从Main.tscn）
        var mainScene = GetTree().CurrentScene;
        turnEndLabel = mainScene.GetNodeOrNull<Label>("TurnEndLabel");
        TurnLabel = mainScene.GetNodeOrNull<Label>("TurnLabel");
        UnitLists = mainScene.GetNodeOrNull<RichTextLabel>("UnitLists");

        // 2. 初始化GridManager
        gridManager?.Init();

        // 3. 初始化WeaponManager（如果存在）
        if (weaponManager != null)
        {
            weaponManager.RefreshWeaponList();

        }

        // 4. 初始化UnitManager（如果存在）
        if (unitManager != null)
        {
            unitManager.RefreshUnitList();

        }

        // 5. 更新UI
        UpdateTurnLabel();
        UpdateUnitLists();

        // ✅ 新增：创建缩放滑块和统计面板收缩按钮
        SetupZoomSlider();
        SetupUnitListsToggle();
    }
    public void DeferredUnitInit()
    {
        if (unitManager == null) return;

        // 强制刷新单位列表
        unitManager.RefreshUnitList();

        // ✅ 新增：从 AllUnits 中收集所有兵种到专用列表
        RefreshSpecializedUnitLists();

        foreach (var unit in unitManager.AllUnits)
        {
            // 确保绑定到格子
            if (unit.grid == null)
            {
                unitManager.BindUnitToGrid(unit, true);
            }

            // 设置点击回调（Area2D 信号在 Infantry._Ready() 中已连接）
            unit.OnClickPiece = OnSelectPiece;
        }

        // ✅ 初始化后更新统计
        UpdateUnitLists();
    }


public void RestartGame()
{
    // 重新加载当前场景
    GetTree().ReloadCurrentScene();
}

    // ========== ✅ 生产系统 ==========
    private Panel productionMenuPanel;
    private Label productionMenuTitle;
    private VBoxContainer productionMenuContainer;
    private Grids productionTargetGrid;
    private Facility productionTargetFacility;

    // ========== ✅ 生产菜单拖拽处理 ==========
    public override void _Process(double delta)
    {
        if (isDraggingProductionMenu && currentDraggedProductionMenu != null)
        {
            Vector2 newPosition = currentDraggedProductionMenu.GetGlobalMousePosition() + dragOffsetProduction;
            currentDraggedProductionMenu.GlobalPosition = newPosition;
        }
    }
}
