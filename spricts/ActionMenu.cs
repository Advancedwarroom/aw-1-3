// ActionMenu.cs - 完整修复版，移除所有unloadButton直接引用
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;

public partial class ActionMenu : Control
{
    [ExportGroup("")]
    [Export] public Button moveButton;
    [Export] public Button attackButton;
    [Export] public Button waitButton;
    [Export] public Button infoButton; 
    [Export] public Button captureButton; 
    [Export] public Button closeInfoButton; 
    [Export] public Label unitInfoLabel; 
    [Export] public Button rotateButton; 
    [Export] public Button explodeButton; // ✅ 自爆按钮
    [Export] public Button illuminateButton; // ✅ 照明按钮
    [Export] public Button repairButton; // ✅ 修复按钮
    [Export] public Button stealthButton; // ✅ 隐身按钮
    [Export] public Button scentButton; // ✅ 嗅探按钮
    private Button pushButton;

    [Export] public Button healButton; // ✅ Crystal 黑水晶治疗按钮

    // APC专用按钮
    [Export] public Button loadButton;
    [Export] public PackedScene unloadButtonPreset;
    [Export] public VBoxContainer unloadContainer;
    [Export] public Button supplyButton;


    public Infantry currentUnit;
    private Weapon currentWeapon;
    private Infantry currentTransport;
    private bool isExplosionPreview = false; // ✅ 爆炸范围预览状态
    private bool isProcessingExplosion = false; // ✅ 防重入：防止OnExplodePressed被连续触发
    private bool isFlarePreview = false; // ✅ 照明弹射程预览状态
    private bool isIlluminationPreview = false; // ✅ 照明覆盖范围预览状态
    private bool isProcessingIllumination = false; // ✅ 防重入
    private bool isRepairPreview = false; // ✅ 修复范围预览状态
    private bool isProcessingRepair = false; // ✅ 防重入：防止OnRepairPressed被连续触发
    private Grids pendingIlluminationTarget = null; // ✅ 待确认的照明目标格子

    // ✅ 菜单拖动
    private bool isDraggingMenu = false;
    private Vector2 dragStartMousePos;
    private Vector2 dragStartMenuPos;

    [Export] public PackedScene unloadUnitButtonPreset;
public override void _Ready()
{
    ZIndex = 50;
    // 统一连接补给按钮信号（避免在各个显示方法中重复/遗漏连接）
    if (supplyButton != null)
    {
        var callable = new Callable(this, nameof(OnSupplyPressed));
        if (!supplyButton.IsConnected(BaseButton.SignalName.Pressed, callable))
        {
            supplyButton.Connect(BaseButton.SignalName.Pressed, callable);
        }
    }
    // ✅ 修复按钮信号连接（带安全检查）
    if (repairButton != null)
    {
        var repairCallable = new Callable(this, nameof(OnRepairPressed));
        if (!repairButton.IsConnected(BaseButton.SignalName.Pressed, repairCallable))
        {
            repairButton.Connect(BaseButton.SignalName.Pressed, repairCallable);
        }
    }
    // ⚠️ 注意：explodeButton 的 Pressed 信号已在编辑器中连接 OnExplodePressed
    // 不要在这里再用代码连接，否则会触发两次！
    // ✅ 照明按钮信号连接（带安全检查）
    if (illuminateButton != null)
    {
        var illumCallable = new Callable(this, nameof(OnIlluminatePressed));
        if (!illuminateButton.IsConnected(BaseButton.SignalName.Pressed, illumCallable))
        {
            illuminateButton.Connect(BaseButton.SignalName.Pressed, illumCallable);
        }
    }
    // ✅ 动态创建 Crystal 治疗按钮（如果场景中没有）
    if (healButton == null)
    {
        var container = GetNodeOrNull<VBoxContainer>("ButtonContainer");
        if (container != null)
        {
            healButton = new Button();
            healButton.Name = "HealButton";
            TranslateGroup.SetText(healButton, "治疗");
            healButton.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            container.AddChild(healButton);
            healButton.Pressed += OnHealPressed;
        }
    }

    // Push is created dynamically so existing ActionMenu scenes remain compatible.
    var buttonContainer = GetNodeOrNull<VBoxContainer>("ButtonContainer");
    if (buttonContainer != null)
    {
        pushButton = new Button { Name = "PushButton", Text = "Push", Visible = false };
        pushButton.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        pushButton.Pressed += OnPushPressed;
        buttonContainer.AddChild(pushButton);
    }

    // ✅ 订阅语言切换信号
    if (TranslateGroup.Instance != null)
    {
        TranslateGroup.Instance.LanguageChanged += OnLanguageChanged;
    }
}

    // Separate push action entry point. Target selection is handled by Infantry.PushRuntime.\n    private void OnPushPressed()
    {
        if (currentUnit == null || !IsInstanceValid(currentUnit)) return;
        currentUnit.BeginSeparatePushTargetSelection();
    }

    // ✅ 菜单拖动支持
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                if (GetGlobalRect().HasPoint(GetGlobalMousePosition()))
                {
                    isDraggingMenu = true;
                    dragStartMousePos = GetGlobalMousePosition();
                    dragStartMenuPos = GlobalPosition;
                }
            }
            else
            {
                isDraggingMenu = false;
            }
        }
    }

    public override void _Process(double delta)
    {
        if (isDraggingMenu && Input.IsMouseButtonPressed(MouseButton.Left))
        {
            GlobalPosition = dragStartMenuPos + (GetGlobalMousePosition() - dragStartMousePos);
        }
    }
    private AnimatedSprite2D GetUnitAnim(Infantry u)
    {
        // 1. 基类 animSprite（迁移后所有单位统一走这里）
        if (u.animSprite != null && IsInstanceValid(u.animSprite))
            return u.animSprite;

        // 2. 反射获取子类可能自定义的动画字段（如 MdTank.MdSprite）
        var animField = u.GetType().GetField("animSprite", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (animField != null)
        {
            var anim = animField.GetValue(u) as AnimatedSprite2D;
            if (anim != null && IsInstanceValid(anim)) return anim;
        }
        var mdField = u.GetType().GetField("MdSprite", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (mdField != null)
        {
            var md = mdField.GetValue(u) as AnimatedSprite2D;
            if (md != null && IsInstanceValid(md)) return md;
        }

        // 3. 递归查找子节点（备用）
        foreach (var child in u.GetChildren())
        {
            if (child is AnimatedSprite2D anim) return anim;
            foreach (var grandChild in (child as Node)?.GetChildren() ?? new Godot.Collections.Array<Node>())
                if (grandChild is AnimatedSprite2D deepAnim) return deepAnim;
        }

        return null;
    }

private void ForceCloseAllRanges(GameManager gm)
{
    if (gm?.gridManager == null) return;

    // 关闭移动范围
    gm.gridManager.CloseRange();

    // 关闭攻击范围
    gm.gridManager.HideAttackRange();

    // 关闭兵器范围
    gm.gridManager.ClearWeaponRange();

    // 清除所有回调
    gm.gridManager.ClearAllEmptyCallbacks();

    // 强制隐藏所有格子的图标
    foreach (var grid in gm.gridManager.grids)
    {
        grid.pathIcon?.Hide();
        grid.attackRangeIcon?.Hide();
        grid.OnClickGrid = null;
        grid.OnClickEmpty = null;
    }


}
private List<Grids> GetAdjacentGrids(Grids center)
    {
        var result = new List<Grids>();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null || center == null) return result;

        Vector2I[] offsets = {
            new Vector2I(0, 1),
            new Vector2I(-1, 0),
            new Vector2I(0, -1),
            new Vector2I(1, 0)
        };

        foreach (var offset in offsets)
        {
            var pos = center.GridIndex + offset;
            if (gm.unitManager.IsValidGrid(pos))
            {
                var grid = gm.gridManager.map[pos.X, pos.Y];
                if (grid != null) result.Add(grid);
            }
        }

        return result;
    }


    private void SetupEmptyClickToCloseMenuOnly(Weapon weapon)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return;

        foreach (var grid in gm.gridManager.grids)
        {
            grid.OnClickEmpty = () => 
            {
                Hide();
                gm.gridManager.ClearWeaponRange();

                if (gm.selectedWeapon == weapon)
                {
                    gm.selectedWeapon = null;
                    weapon.SetVisualNormal();
                }
            };
        }
    }


    public void ShowMenu(Infantry unit, bool afterMove = false, bool onlyShowMenu = false)
    {
        var container = GetNode<VBoxContainer>("unloadContainer");
        foreach (var child in container.GetChildren())
        child.QueueFree();
        bool canSupply = !unit.hasActed && HasSupplyableUnits(unit);
        supplyButton.Visible = canSupply;
        currentTransport = null;

        // ✅ 究极自由：搭载菜单对所有可搭载单位显示！
        // 检查单位是否有搭载能力（通过属性而非类型）
        bool isTransportUnit = unit.canTransportUnits && unit.maxTransportCapacity > 0;
        bool hasNoWeapons = !unit.hasPrimaryWeapon && !unit.hasSecondaryWeapon;


        currentWeapon = null;

        // ✅ 切换单位时重置爆炸预览状态，防止状态残留
        if (currentUnit != unit)
        {
            isExplosionPreview = false;
            isFlarePreview = false;
            isIlluminationPreview = false;
            pendingIlluminationTarget = null;
        }

        currentUnit = unit;
        if (pushButton != null)
            pushButton.Visible = unit.pushEnabled && unit.pushTriggerMode == Infantry.PushTriggerMode.SeparateAction
                && unit.CanUseSeparatePushAction();

        if (!IsNodeReady()) return;
        if (currentUnit == null) return;

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.gridManager?.ClearWeaponRange();
        gm?.gridManager?.HideAttackRange();

        if (gm?.gridManager != null)
        {
            if (afterMove)
            {
                gm.gridManager.SetupEmptyClickForRollback(unit);
            }
            else
            {
                gm.gridManager.SetEmptyClickToCloseMenuOnly(unit);
            }
        }

        // ✅ 统一判定：可攻击类型 + 未攻击 + 移动后允许 + 有弹药 + 射程内有可伤害目标
        attackButton.Visible = CanShowAttackButton(unit);
        // ✅ 舱内攻击
        if (!attackButton.Visible)
            attackButton.Visible = unit.HasLoadedAttackOption();

        bool canCapture = unit.CanCaptureCurrentGrid();
        bool canRemoteCapture = unit.GetRemoteCapturableFacilities().Count > 0;   // 开创人2026-11-11：设施判定远程占领
        bool isCapturing = unit.currentCaptureProgress > 0 && unit.capturingGrid == unit.grid;

        bool allowCaptureAfterMove = unit?.allowCaptureAfterMove ?? true;

        moveButton.Visible = !afterMove && !unit.isMoved && unit.CanMove();
        bool canTear = unit.CanTearCurrentGrid();
        bool isTearing = unit.currentTearProgress > 0 && unit.tearingGrid == unit.grid;
        bool canLoadedCapture = unit.CanPerformLoadedCapture();
        bool canLoadedTear = unit.CanPerformLoadedTearFlag();      // ✅ 舱内撕旗   // ✅ 内部单位操作：舱内占领
        captureButton.Visible = (canCapture || canRemoteCapture || isCapturing || canTear || isTearing || canLoadedCapture || canLoadedTear) && !unit.isAttacked && (!unit.isMoved || allowCaptureAfterMove);
        rotateButton?.Hide();

        waitButton.Visible = true;
        infoButton.Visible = true;
        if (loadButton != null) loadButton.Visible = false;
        if (healButton != null) healButton.Visible = false;
        if (unloadContainer != null) unloadContainer.Visible = false;
        if (explodeButton != null)
        {
            // ✅ 爆炸按钮：单位可自爆、未行动，且（一次性自爆 或 有自爆弹）时显示
            bool hasExplodeAmmo = !unit.explosionDestroysSelf ? unit.currentExplodeAmmo > 0 : true;
            explodeButton.Visible = unit.canExplode && !unit.hasActed && unit.state != UnitState.Acted && hasExplodeAmmo;
            TranslateGroup.SetText(explodeButton, isExplosionPreview ? "确认爆炸" : "爆炸");
        }

        // ✅ 导弹井发射 / 天基打击按钮
        UpdateSiloStrikeButtons();
        UpdatePaidResupplyButtons();   // ✅ 自费补给：自点自补/代点他补（开创人2026-11-11）
        UpdateForceSupportButtons();
        UpdateEmpowermentButton(unit);
        UpdateShelterButtons(null);
        UpdateWriggleButton();   // ✅ 统一蠕动更新器（原双生UpdateGrandBoltWriggleButtons已废弃）   // ✅ GrandBolt 手动蠕动（范围预览+曼哈顿步数）

        // ✅ N屏地图：传送按钮（站在有出链的格子上时显示）
        UpdateTeleportButtonVisibility(unit);

        // ✅ CO 三十模块体系：COP/SCOP 按钮（能量条攒满时显示）
        UpdateCoPowerButtons(unit);

        if (repairButton != null)
        {
            // ✅ 修复按钮：任何开启修复模块的单位，且未行动时显示
            bool canRepairNow = unit.CanPerformRepair && !unit.hasActed && unit.state != UnitState.Acted
                                && (!unit.isMoved || unit.canRepairAfterMove);
            repairButton.Visible = canRepairNow;
            if (isRepairPreview)
                TranslateGroup.SetText(repairButton, "确认修复");
            else
                TranslateGroup.SetText(repairButton, "修复");
        }

        if (illuminateButton != null)
        {
            // ✅ 照明按钮：任何开启照明模块的单位，且还有照明弹时显示
            bool canIlluminateNow = unit.canIlluminate && unit.currentFlareAmmo > 0
                                && !unit.hasActed && unit.state != UnitState.Acted
                                && (!unit.isMoved || unit.canIlluminateAfterMove);
            illuminateButton.Visible = canIlluminateNow;
            if (isIlluminationPreview)
                TranslateGroup.SetText(illuminateButton, "确认照明");
            else if (isFlarePreview)
                TranslateGroup.SetText(illuminateButton, "取消照明");
            else
                TranslateGroup.SetText(illuminateButton, "照明");
        }

        // ✅ 隐身按钮：canStealth 单位显示；形态锁定隐藏；耗行动模式要求未行动
        if (stealthButton == null && illuminateButton != null)
        {
            stealthButton = new Button();
            stealthButton.Pressed += OnStealthPressed;
            illuminateButton.GetParent().AddChild(stealthButton);
            scentButton = new Button();
            scentButton.Pressed += OnScentPressed;
            illuminateButton.GetParent().AddChild(scentButton);
        }
        if (stealthButton != null)
        {
            bool canToggleStealth = unit.canStealth && !unit.IsStealthFormLocked
                && (!unit.stealthToggleCostsAction || (!unit.hasActed && unit.state != UnitState.Acted));
            stealthButton.Visible = canToggleStealth || unit.CanPerformLoadedStealth();
            TranslateGroup.SetText(stealthButton, unit.isStealthed ? unit.GetStealthShowButtonText() : unit.GetStealthHideButtonText());
        }
        if (scentButton != null)
        {
            scentButton.Visible = unit.scentEnabled && !unit.hasActed && unit.state != UnitState.Acted;
            TranslateGroup.SetText(scentButton, "嗅探");
        }

        // ✅ 生产模块 / 建造模块 / 部署建筑 按钮可见性
        ShowProductionBuildButtons(unit);

        if (isCapturing)
        {
            moveButton.Visible = false;
            attackButton.Visible = false;
        }

  pendingTransportSelection.Clear();

    bool canMountAPC = CheckCanMountTransport(unit);

    // ✅ 统一菜单（开创人 2026-11-11 裁决）：运输单位不再走独立运输菜单链。
    // 装载/卸载并入主菜单——此后一切新功能按钮对运输单位自动生效，永不再双链适配饿死。
    if (isTransportUnit)
    {
        var mountableUnit = unit.grid?.infantries?.FirstOrDefault(u =>
            u != null && IsInstanceValid(u) && u != unit
            && unit.CanTransportUnit(u)
            && unit.transportedUnits != null
            && unit.transportedUnits.Count < unit.maxTransportCapacity
            && (u.team == unit.team || unit.team == TeamHelper.Player)
            && (afterMove || !u.isMoved)
            && u.state != UnitState.Acted);
        if (mountableUnit != null)
        {
            currentTransport = unit;
            currentUnit = mountableUnit;
            canMountAPC = true;   // 复用装载按钮（OnLoadPressed 消费 currentUser/currentTransport）
        }
    }


    // 处理装载按钮显示逻辑
    if (loadButton != null)
    {
        loadButton.Visible = canMountAPC;

        // ✅ 修复：安全断开再连接，防止断开不存在的连接报错
        var callable = new Callable(this, nameof(OnLoadPressed));
        if (loadButton.IsConnected(BaseButton.SignalName.Pressed, callable))
        {
            loadButton.Disconnect(BaseButton.SignalName.Pressed, callable);
        }
        loadButton.Connect(BaseButton.SignalName.Pressed, callable);
    }

    waitButton.Visible = true;
        // ✅ 开创人2026-11-11: 卸载按钮生成挪到待机可见之后——容器锚定用最终btnCol尺寸，根治"待机盖住第一个卸载项"
        if (unit.transportedUnits != null && unit.transportedUnits.Count > 0)
            ShowTransportUnloadButtonsOnly(unit, afterMove);
        var unitGlobalPos = unit.GlobalPosition;
        Position = new Vector2(unitGlobalPos.X - 40, unitGlobalPos.Y - Size.Y - 60);
        Show();
    }
private List<Infantry> pendingTransportSelection = new List<Infantry>(); 

    // ✅ 重写 Hide 确保隐藏时取消所有预览
    public new void Hide()
    {
        CancelExplosionPreview();
        CancelIlluminationPreview();
        CancelRepairPreview();
        base.Hide();
    }
}
