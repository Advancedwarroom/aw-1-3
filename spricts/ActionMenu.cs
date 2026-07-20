// ActionMenu.cs - 完整修复版，移除所有unloadButton直接引用
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;

public partial class ActionMenu : Control
{
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
    private Grids pendingIlluminationTarget = null; // ✅ 待确认的照明目标格子

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
            healButton.Text = "治疗";
            healButton.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            container.AddChild(healButton);
            healButton.Pressed += OnHealPressed;
        }
    }
}


// ✅ 究极自由：通用运输单位卸载菜单
public void ShowTransportUnloadMenu(Infantry transport, bool afterMove = false)
{
        if (transport.hasActed || transport.state == UnitState.Acted)
    {
        return; // ← 已行动的运输单位不能操作
    }
    currentUnit = transport;
    currentTransport = transport;  // 兼容旧代码

    // 武器按钮可见性（与普通单位统一判定）
    attackButton.Visible = CanShowAttackButton(transport);

    if (!IsNodeReady()) return;

    // 补给按钮设置
    bool canSupply = !transport.hasActed && HasSupplyableUnits(transport);
    if (supplyButton != null)
    {
        supplyButton.Visible = canSupply;
    }

    moveButton.Visible = !afterMove && !transport.isMoved && transport.CanMove();
    if (loadButton != null) loadButton.Visible = false;
    rotateButton?.Hide();

    var container = GetNodeOrNull<VBoxContainer>("unloadContainer");
    if (container == null)
    {
        return;
    }
    container.Visible = true;

    // 清空旧内容
    foreach (var child in container.GetChildren())
        child.QueueFree();

    // 创建卸载按钮
    foreach (var unit in transport.transportedUnits)
    {
        if (unit == null || !IsInstanceValid(unit)) continue;

        if (unloadUnitButtonPreset != null)
        {
            var ubt = unloadUnitButtonPreset.Instantiate<UnloadUnitButton>();
            ubt.SetupDisplay(unit);

            var unloadBtn = ubt.GetUnLoadButton();
            if (unloadBtn != null)
            {
                // 使用闭包捕获unit和afterMove
                var capturedUnit = unit;
                var capturedAfterMove = afterMove;
                unloadBtn.Pressed += () => OnUnloadSpecificUnit(capturedUnit, capturedAfterMove);
            }
            else
            {
                // 备用点击方式
                var capturedUnit = unit;
                var capturedAfterMove = afterMove;
                ubt.GuiInput += (e) => {
                    if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                        OnUnloadSpecificUnit(capturedUnit, capturedAfterMove);
                };
            }

            ubt.CustomMinimumSize = new Vector2(140, 50);
            container.AddChild(ubt);
        }
    }

    // 设置空地回调
    var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
    if (gm?.gridManager != null)
    {
        if (afterMove)
            gm.gridManager.SetupEmptyClickForRollback(transport);
        else
            gm.gridManager.SetEmptyClickToCloseMenuOnly(transport);
    }

    Position = transport.GlobalPosition + new Vector2(-40, -120);
    Show();
}
    private AnimatedSprite2D GetUnitAnim(Infantry u)
    {
        // 1. 优先通过反射获取 animSprite 字段（通用，不硬编码类型）
        var animField = u.GetType().GetField("animSprite", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (animField != null)
        {
            var anim = animField.GetValue(u) as AnimatedSprite2D;
            if (anim != null && IsInstanceValid(anim)) return anim;
        }

        // 2. 兼容旧硬编码（保留所有类型）
        if (u is Mech m && m?.animSprite != null) return m.animSprite;
        if (u is LightTank t && t?.animSprite != null) return t.animSprite;
        if (u is APC a && a?.animSprite != null) return a.animSprite;
        if (u is Oozium o && o?.animSprite != null) return o.animSprite;
        if (u is Artillery ar && ar?.animSprite != null) return ar.animSprite;
        if (u is PipeRunner pr && pr?.animSprite != null) return pr.animSprite;
        if (u is Flare fl && fl?.animSprite != null) return fl.animSprite;
        if (u is Bike bk && bk?.animSprite != null) return bk.animSprite;
        if (u is AntiAir aa && aa?.animSprite != null) return aa.animSprite;
        if (u is AntiTank at && at?.animSprite != null) return at.animSprite;
        if (u is Recon rc && rc?.animSprite != null) return rc.animSprite;
        if (u is MdTank md && md?.MdSprite != null) return md.MdSprite;
        if (u is Rocket rk && rk?.animSprite != null) return rk.animSprite;
        if (u is FlyBomb fb && fb?.animSprite != null) return fb.animSprite;

        // 3. 递归查找子节点（备用）
        foreach (var child in u.GetChildren())
        {
            if (child is AnimatedSprite2D anim) return anim;
            foreach (var grandChild in (child as Node)?.GetChildren() ?? new Godot.Collections.Array<Node>())
                if (grandChild is AnimatedSprite2D deepAnim) return deepAnim;
        }

        return null;
    }
// ✅ 究极自由：通用运输单位装载菜单
public void ShowTransportLoadMenu(Infantry transport, Infantry unit)
{
    {
currentUnit = unit;
currentWeapon = null;
if (transport is APC apc)
{
currentTransport = apc;
}
else
{
currentTransport = transport;  // 非APC的运输单位
    }

if (!IsNodeReady()) return;

moveButton.Visible = false;
attackButton.Visible = false;
captureButton.Visible = false;
rotateButton?.Hide();
if (unloadContainer != null) unloadContainer.Visible = false;

if (loadButton != null)
{
    loadButton.Visible = true;
    var callable = new Callable(this, nameof(OnLoadPressed));
    if (loadButton.IsConnected(BaseButton.SignalName.Pressed, callable))
    {
        loadButton.Disconnect(BaseButton.SignalName.Pressed, callable);
    }
    loadButton.Connect(BaseButton.SignalName.Pressed, callable);
}

waitButton.Visible = true;
infoButton.Visible = true;

var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
if (gm?.gridManager != null)
{
    foreach (var grid in gm.gridManager.grids)
    {
        grid.OnClickEmpty = () => {
            Hide();
            gm.gridManager.ClearAllEmptyCallbacks();
            gm.ClearSelectedInfantry();
        };
    }
}

Position = unit.GlobalPosition + new Vector2(-40, -100);
Show();
    }
}


private void OnLoadPressed()
{
    if (currentUnit == null) return;

    // 如果有多个运输单位可选，显示选择菜单
    if (currentTransport == null && pendingTransportSelection != null && pendingTransportSelection.Count > 1)
    {
        ShowTransportSelectionMenu(pendingTransportSelection);
        return;
    }

    // 如果只有1个或没有currentTransport，从pending获取第一个
    if (currentTransport == null && pendingTransportSelection != null && pendingTransportSelection.Count > 0)
    {
        currentTransport = pendingTransportSelection[0];
    }

    if (currentTransport != null && currentUnit != null)
    {
        bool isFarMounting = currentUnit.grid != currentTransport.grid;

        if (isFarMounting)
            PerformFarMounting(currentTransport, currentUnit);
        else
            PerformSameGridMounting(currentTransport, currentUnit);
    }
}

// 新增：执行远程搭载
private void PerformFarMounting(Infantry transport, Infantry unit)
{
    // 直接装载，不需要移动单位位置
    bool success = transport.LoadUnit(unit);

    if (success)
    {
        Hide();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.ClearSelectedInfantry();
        gm?.gridManager?.CloseRange();

        // 标记单位已行动
        unit.isMoved = true;
        unit.isAttacked = true;
        unit.state = UnitState.Acted;
        unit.SetWaitVisual(true);

        // 清除引用
        currentTransport = null;
        pendingTransportSelection.Clear();
    }
}

// 新增：执行同格搭载（原有逻辑提取）
private void PerformSameGridMounting(Infantry transport, Infantry unit)
{
    bool isFarMounting = unit.grid != transport.grid; 

    if (isFarMounting)
    {
        // 这个分支实际上不会执行，因为同格搭载时isFarMounting=false
        // 保留原有逻辑以防万一
        if (unit.grid != null)
        {
            unit.grid.infantries.Remove(unit);
            if (unit.grid.infantry == unit)
                unit.grid.infantry = unit.grid.infantries.Count > 0 ? unit.grid.infantries[0] : null;
        }

        unit.Position = transport.Position;
        unit.grid = transport.grid;

        if (!transport.grid.infantries.Contains(unit))
            transport.grid.infantries.Add(unit);
        if (transport.grid.infantry == null)
            transport.grid.infantry = unit;
    }

    bool success = transport.LoadUnit(unit);
    if (success)
    {
        Hide();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.ClearSelectedInfantry();
        gm?.gridManager?.CloseRange();

        unit.isMoved = true;
        unit.isAttacked = true;
        unit.state = UnitState.Acted;
        unit.SetWaitVisual(true);

        currentTransport = null;
    }
}
// ✅ 究极自由：通用运输菜单入口（自动判断装载/卸载）
public void ShowTransportUnitMenu(Infantry transport, bool afterMove = false)
{
    // 有已装载单位 → 显示卸载菜单
    if (transport.transportedUnits != null && transport.transportedUnits.Count > 0)
    {
        ShowTransportUnloadMenu(transport, afterMove);
    }
    else
    {
        // 没有装载单位，尝试显示装载菜单
        // 需要 currentUnit 被设置
        if (currentUnit != null && transport.CanTransportUnit(currentUnit))
        {
            ShowTransportLoadMenu(transport, currentUnit);
        }
        else
        {
        }
    }
}

// 新增：显示APC选择菜单（当范围内有多个APC时）
private void ShowTransportSelectionMenu(List<Infantry> transports)
{
    // 清空卸载容器（借用这个容器显示APC选择）
    var container = GetNodeOrNull<VBoxContainer>("unloadContainer");
    if (container == null) return;

    container.Visible = true;

    // 清空旧内容
    foreach (var child in container.GetChildren())
        child.QueueFree();

    // 隐藏其他按钮，只显示选择标题
    moveButton.Visible = false;
    attackButton.Visible = false;
    captureButton.Visible = false;
    waitButton.Visible = false;
    infoButton.Visible = false;
    if (supplyButton != null) supplyButton.Visible = false;

    // 创建选择提示标签
    var label = new Label();
    label.Text = "选择要搭载的运输单位:";
    container.AddChild(label);

     // 为每个运输单位创建选择按钮
    foreach (var transport in transports)
    {
        var btn = new Button();
        btn.CustomMinimumSize = new Vector2(140, 40);

        // 显示运输单位信息和距离
        int dist = Mathf.Abs(currentUnit.grid.GridIndex.X - transport.grid.GridIndex.X)
                 + Mathf.Abs(currentUnit.grid.GridIndex.Y - transport.grid.GridIndex.Y);
        string transportType = transport.GetType().Name;
        btn.Text = $"{transportType} (距离:{dist}) HP:{Mathf.CeilToInt(transport.health/10f)}";

        // 捕获变量避免闭包问题
        Infantry selectedTransport = transport;
        btn.Pressed += () => {
            currentTransport = selectedTransport ;  // 兼容旧代码，实际逻辑支持任意运输单位
            PerformFarMounting(currentTransport, currentUnit);
            container.Visible = false;
        };

        container.AddChild(btn);
    }
}


private void OnUnloadSpecificUnit(Infantry unit, bool afterMove = false)
{
    if (currentTransport != null)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        ForceCloseAllRanges(gm);
        gm?.gridManager?.CloseRange();
        gm?.gridManager?.ClearAllEmptyCallbacks();

        // 传递 afterMove 参数
        StartUnloadMode(currentTransport, unit, afterMove);
    }
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

// ActionMenu.cs - 修改 StartUnloadMode 方法
private void StartUnloadMode(Infantry transport, Infantry unit, bool afterMove)
{
    Hide();

    var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
    if (gm?.gridManager == null) return;

    // 清除之前的显示
    gm.gridManager.CloseRange();
    gm.gridManager.HideAttackRange();
    gm.gridManager.ClearWeaponRange();

    foreach (var g in gm.gridManager.grids)
    {
        g.pathIcon?.Hide();
        g.pathIcon?.SetDeferred("modulate", Colors.White);
        g.OnClickGrid = null;
        g.OnClickEmpty = null;
    }

    var availableGrids = GetAdjacentGrids(transport.grid);

    // 显示卸下范围
    foreach (var grid in availableGrids)
    {
        if (transport.CanUnloadToGrid(grid))
        {
            grid.pathIcon?.Show();
            grid.pathIcon.Modulate = new Color(0, 1, 0, 0.7f);

            grid.OnClickGrid = to => 
            {
                bool success = transport.UnloadUnit(unit, to);

                // 立即强制关闭所有绿色范围
                ForceCloseUnloadRange(gm, availableGrids);

if (success)
{
    if (afterMove)
    {
        // 移动后卸载：直接待机
        transport.isMoved = true;
        transport.isAttacked = true;
        transport.hasActed = true;
        transport.state = UnitState.Acted;
        transport.SetWaitVisual(true);

        var gameMgr = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gameMgr?.ClearSelectedInfantry();

        if (transport.transportedUnits.Count > 0)
        {
            ShowTransportUnloadMenu(transport, afterMove: true);
        }
        else
        {
            Hide();
        }
    }
    else
    {
        // ✅ 原地卸载：标记已行动，不能再移动/攻击
        transport.isMoved = true;
        transport.isAttacked = true;
        transport.hasActed = true;
        transport.state = UnitState.Acted;
        transport.SetWaitVisual(true);

        var gameMgr = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gameMgr?.ClearSelectedInfantry();

        // 如果还有剩余装载单位，继续显示卸载菜单（但已标记为不能移动）
        if (transport.transportedUnits.Count > 0)
        {
            ShowTransportUnloadMenu(transport, afterMove: false);
        }
        else
        {
            // 全部卸完，直接关闭菜单
            Hide();
        }
    }
}
                else
                {
                    // 卸载失败，重新显示菜单
                    ShowTransportUnloadMenu(transport, afterMove);
                }
            };
        }
    }

    // 点击空地取消
    foreach (var grid in gm.gridManager.grids)
    {
        if (!availableGrids.Contains(grid))
        {
            grid.OnClickEmpty = () => 
            {
                ForceCloseUnloadRange(gm, availableGrids);
                ShowTransportUnloadMenu(transport, afterMove);
            };
        }
    }
}

private void ForceCloseUnloadRange(GameManager gm, List<Grids> rangeGrids)
{
    if (gm?.gridManager == null) return;

    // 关闭指定范围的绿色显示
    foreach (var grid in rangeGrids)
    {
        if (grid != null && IsInstanceValid(grid))
        {
            grid.pathIcon?.Hide();
            grid.pathIcon.Modulate = Colors.White; // 恢复白色
            grid.OnClickGrid = null;
        }
    }

    // 清除所有空地点回调
    foreach (var grid in gm.gridManager.grids)
    {
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

   public void ShowWeaponMenu(Weapon weapon)
    {
        currentUnit = null;
        currentWeapon = weapon;

        if (!IsNodeReady()) return;

        // ✅ 统一隐藏所有不需要的按钮
        moveButton.Visible = false;
        captureButton.Visible = false;
        rotateButton?.Hide();
        explodeButton?.Hide();
        illuminateButton?.Hide();
        loadButton?.Hide();
        supplyButton.Visible = false;

        // ✅ Crystal 黑水晶：显示"治疗"按钮，隐藏"攻击"按钮
        if (weapon is Crystal crystal)
        {
            bool canHeal = crystal.CanAttack(); // CanAttack = !hasActed && remainingAttacks > 0
            attackButton.Visible = false;
            if (healButton != null)
            {
                healButton.Visible = canHeal;
            }
        }
        else
        {
            if (healButton != null) healButton.Visible = false;

            // ✅ 修改：根据弹药和冷却状态判断是否可以攻击
            bool canAttack = weapon.CanAttack();
            bool hasTargetInRange = false;

            if (canAttack)
            {
                var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
                var range = weapon.CalculateAttackRange();
                var fog = gm?.fogOfWarManager;
                bool isFogEnabled = fog != null && fog.isFogOfWarEnabled;
                hasTargetInRange = range.Any(g => 
                {
                    if (isFogEnabled && fog != null && !fog.IsGridVisible(g)) return false;
                    if (weapon is BlackCannon cannon)
                        return cannon.HasValidTargetInGrid(g);
                    return g.HasEnemyInfantry(weapon.team) || (weapon.CanAttackWeapon && g.weapon != null && g.weapon.team != weapon.team);
                });
            }

            attackButton.Visible = canAttack && hasTargetInRange;
        }

        waitButton.Visible = true;
        infoButton.Visible = true;

        unitInfoLabel.Visible = false;
        closeInfoButton.Visible = false;

        SetupEmptyClickToCloseMenuOnly(weapon);

        Position = weapon.GlobalPosition + new Vector2(-40, -100);
        Show();
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

        bool canCapture = unit.CanCaptureCurrentGrid();
        bool isCapturing = unit.currentCaptureProgress > 0 && unit.capturingGrid == unit.grid;

        bool allowCaptureAfterMove = unit?.allowCaptureAfterMove ?? true;

        moveButton.Visible = !afterMove && !unit.isMoved && unit.CanMove();
        captureButton.Visible = (canCapture || isCapturing) && !unit.isAttacked && (!unit.isMoved || allowCaptureAfterMove);
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
            explodeButton.Text = isExplosionPreview ? "确认爆炸" : "爆炸";
        }

        if (illuminateButton != null)
        {
            // ✅ 照明按钮：任何开启照明模块的单位，且还有照明弹时显示
            bool canIlluminateNow = unit.canIlluminate && unit.currentFlareAmmo > 0
                                && !unit.hasActed && unit.state != UnitState.Acted
                                && (!unit.isMoved || unit.canIlluminateAfterMove);
            illuminateButton.Visible = canIlluminateNow;
            if (isIlluminationPreview)
                illuminateButton.Text = "确认照明";
            else if (isFlarePreview)
                illuminateButton.Text = "取消照明";
            else
                illuminateButton.Text = "照明";
        }

        if (isCapturing)
        {
            moveButton.Visible = false;
            attackButton.Visible = false;
        }

  pendingTransportSelection.Clear();

    bool canMountAPC = CheckCanMountTransport(unit);

    // APC自身不显示装载按钮（保持不变）
    if (isTransportUnit)
{
    ShowTransportMenu(unit, afterMove);
    return;
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
        var unitGlobalPos = unit.GlobalPosition;
        Position = new Vector2(unitGlobalPos.X - 40, unitGlobalPos.Y - Size.Y - 60);
        Show();
    }

private List<Infantry> GetAvailableTransportsInRange(Infantry unit)
{
    var result = new List<Infantry>();
    var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
    if (gm?.gridManager == null || unit.grid == null) return result;

    var currentGrid = unit.grid;

    foreach (var transport in currentGrid.infantries)
    {
        if (transport is Infantry transportUnit && transportUnit.CanTransportUnit(unit))
        {
            result.Add(transportUnit);
        }
    }

    var allNodes = gm.GetTree().GetNodesInGroup("infantry");
    var allTransports = new List<Infantry>();

    foreach (Node node in allNodes)
    {
        if (node is Infantry transportNode && transportNode.team == unit.team && transportNode != unit)
        {
            allTransports.Add(transportNode);
        }
    }

    foreach (var transport in allTransports)
    {
        if (transport.grid == null) continue;

        if (!transport.CanFarMounting) continue;

        int dist = Mathf.Abs(currentGrid.GridIndex.X - transport.grid.GridIndex.X) 
                 + Mathf.Abs(currentGrid.GridIndex.Y - transport.grid.GridIndex.Y);

        bool inRange = dist >= transport.minFarMountingDistance && dist <= transport.maxFarMountingDistance;
        if (inRange && dist > 0 && transport.CanTransportUnit(unit))
        {
            result.Add(transport);
        }
    }

    return result;
}


private bool CheckCanMountTransport(Infantry unit)
{
    if (unit.canTransportUnits && unit.maxTransportCapacity > 0) return false;
    var availabletransports = GetAvailableTransportsInRange(unit);

    if (availabletransports.Count == 0) return false;


    if (availabletransports.Count == 1)
    {
        currentTransport = availabletransports[0] ;
        return true;
    };


    currentTransport = null; 
    pendingTransportSelection = availabletransports.Cast<Infantry>().ToList();
    return true;
}
private List<Infantry> pendingTransportSelection = new List<Infantry>(); 


public void ShowTransportMenu(Infantry transport, bool afterMove)
{
        if (transport.hasActed || transport.state == UnitState.Acted || 
        (transport.isMoved && transport.isAttacked))
    {
        return;
    }
    currentUnit = transport;
    currentTransport = transport;
    currentWeapon = null;

    if (!IsNodeReady()) return;

    if (unloadContainer != null)
    {
        unloadContainer.Visible = false;
        foreach (var child in unloadContainer.GetChildren())
            child.QueueFree();
    }

    rotateButton?.Hide();

    moveButton.Visible = !afterMove && !transport.isMoved && transport.CanMove();

    if (loadButton != null)
    {
        loadButton.Visible = false; 
    }

    if (transport.transportedUnits.Count < transport.maxLoadCapacity && transport.grid != null)
    {
        var mountableUnit = transport.grid.infantries
            .FirstOrDefault(u => u != transport 
                && u.team == transport.team 
                && transport.CanTransportUnit(u) 
                && !u.isMoved 
                && u.state != UnitState.Acted);

        if (mountableUnit != null && loadButton != null)
        {
            loadButton.Visible = true;
            currentUnit = mountableUnit; 
            // ✅ 修复：安全断开再连接，防止断开不存在的连接报错
            var callable = new Callable(this, nameof(OnLoadPressed));
            if (loadButton.IsConnected(BaseButton.SignalName.Pressed, callable))
            {
                loadButton.Disconnect(BaseButton.SignalName.Pressed, callable);
            }
            loadButton.Connect(BaseButton.SignalName.Pressed, callable);
        }
    }


    if (!afterMove && transport.transportedUnits.Count > 0)
    {
        ShowTransportUnloadButtonsOnly(transport);
    }


    bool canSupply = !transport.hasActed && HasSupplyableUnits(transport);
    if (supplyButton != null)
    {
        supplyButton.Visible = canSupply;
    }

    waitButton.Visible = true;
    infoButton.Visible = true;

    // ✅ 与普通单位统一判定：尊重 attackType/弹药/射程内可伤害目标
    attackButton.Visible = CanShowAttackButton(transport);
    captureButton.Visible = false;

    var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
    if (gm?.gridManager != null)
    {
        if (afterMove)
            gm.gridManager.SetupEmptyClickForRollback(transport);
        else
            gm.gridManager.SetEmptyClickToCloseMenuOnly(transport);
    }

    Position = transport.GlobalPosition + new Vector2(-40, -100);
    Show();
}


private void ShowTransportUnloadButtonsOnly(Infantry transport)
{
    // 获取容器
    var container = GetNodeOrNull<VBoxContainer>("unloadContainer");
    if (container == null) return;

    container.Visible = true;

    // 清空旧内容
    foreach (var child in container.GetChildren())
        child.QueueFree();

    // 创建卸下单位按钮（afterMove = false，因为未移动）
    foreach (var unit in transport.transportedUnits)
    {
        if (unit == null || !IsInstanceValid(unit)) continue;

        if (unloadUnitButtonPreset != null)
        {
            var ubt = unloadUnitButtonPreset.Instantiate<UnloadUnitButton>();
            ubt.SetupDisplay(unit);

            var unloadBtn = ubt.GetUnLoadButton();
            if (unloadBtn != null)
            {
                // 未移动状态，传递 afterMove = false
                unloadBtn.Pressed += () => OnUnloadSpecificUnit(unit, afterMove: false);
            }
            else
            {
                ubt.GuiInput += (e) => {
                    if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                        OnUnloadSpecificUnit(unit, afterMove: false);
                };
            }

            ubt.CustomMinimumSize = new Vector2(140, 50);
            container.AddChild(ubt);
        }
    }
}


private bool HasSupplyableUnits(Infantry transport)
{
    // 检查单位是否有补给能力
    if (!transport.canSupplyUnits) return false;
    
    var supplyableUnits = transport.GetSupplyRangeUnits();
{
    foreach (var unit in supplyableUnits)
    {
        bool needsAmmo = unit.hasPrimaryWeapon && unit.primaryHasLimitedAmmo 
                        && unit.currentPrimaryAmmo < unit.maxPrimaryAmmo;
        bool needsFuel = unit.consumeFuel && unit.fuel < unit.maxFuel;
        bool needsFlare = unit.canIlluminate && unit.currentFlareAmmo < unit.maxFlareAmmo;
        if (needsAmmo || needsFuel || needsFlare) return true;
    }

    var cannonsNeedingAmmo = transport.GetBlackCannonsNeedingAmmo();
    if (cannonsNeedingAmmo.Count > 0) return true;

    return false;
}
}

private void OnSupplyPressed()
{
    GetViewport().SetInputAsHandled();
    var supplier = currentTransport ?? currentUnit;
    if (supplier != null)
    {
        supplier.OnSupplySelected();
        Hide();
    }
}

    private bool isCaptureProcessing = false;

    // ✅ 攻击按钮统一判定：可攻击类型 + 未攻击 + 移动后允许 + 有弹药 + 射程内有可伤害目标
    private bool CanShowAttackButton(Infantry unit)
    {
        if (unit == null) return false;
        if (unit.attackType == AttackType.NoAttack) return false;
        if (unit.isAttacked) return false;
        if (!unit.CanAttackAfterMove()) return false;
        if (!unit.CanUsePrimaryWeapon() && !unit.CanUseSecondaryWeapon()) return false;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        return gm != null && HasAttackableEnemyInRange(unit, gm);
    }

    // 射程内是否存在“实际能造成伤害”的敌人（infantry 用伤害矩阵判定，weapon 维持队伍判定）
    private bool HasAttackableEnemyInRange(Infantry unit, GameManager gm)
    {
        if (gm?.gridManager == null || unit.grid == null) return false;

        bool hasEnemyInRange;
        if (unit.useMinMaxAttackRange)
        {
            hasEnemyInRange = CheckMinMaxRangeEnemy(unit, gm);
        }
        else if (unit.attackRange > 1)
        {
            // 当 attackRange > 1 时，使用 FindRange 检查更大范围
            var extendedRange = gm.gridManager.FindRange(unit.grid, unit.attackRange, false);
            hasEnemyInRange = extendedRange.Any(g =>
                g.HasAttackableEnemyInfantry(unit) || g.HasEnemyWeapon(unit.team)
            );
            if (!hasEnemyInRange && unit.overlapType == UnitOverlapType.Overlapping)
                if (unit.grid.HasAttackableEnemyInfantry(unit) || unit.grid.HasEnemyWeapon(unit.team))
                    hasEnemyInRange = true;
        }
        else
        {
            // 默认4邻域
            var range = gm.gridManager.GetAttackNeighbours(unit.grid);
            hasEnemyInRange = range.Any(g =>
                g.HasAttackableEnemyInfantry(unit) || g.HasEnemyWeapon(unit.team)
            );
            if (!hasEnemyInRange && unit.overlapType == UnitOverlapType.Overlapping)
                if (unit.grid.HasAttackableEnemyInfantry(unit) || unit.grid.HasEnemyWeapon(unit.team))
                    hasEnemyInRange = true;
        }
        return hasEnemyInRange;
    }

    private bool CheckMinMaxRangeEnemy(Infantry unit, GameManager gm)
    {
        if (unit.grid == null) return false;

        int minRange = unit.minAttackRange;
        int maxRange = unit.maxAttackRange;

        foreach (var grid in gm.gridManager.grids)
        {
            if (grid == null || grid == unit.grid) continue;

            int distance = Mathf.Abs(grid.GridIndex.X - unit.grid.GridIndex.X) 
                         + Mathf.Abs(grid.GridIndex.Y - unit.grid.GridIndex.Y);

            if (distance >= minRange && distance <= maxRange)
            {
                if (grid.HasAttackableEnemyInfantry(unit) || grid.HasEnemyWeapon(unit.team))
                    return true;
            }
        }
        return false;
    }

    public void OnCapturePressed()
    {
        GetViewport().SetInputAsHandled();
        if (isCaptureProcessing) return;
        if (currentUnit == null) return;

        isCaptureProcessing = true;

        try 
        {
            currentUnit.PerformCapture();
            Hide();

            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm != null)
            {
                foreach (var grid in gm.gridManager.grids)
                {
                    grid.OnClickEmpty = null;
                }
                gm.ClearSelectedInfantry();
            }
        }
        finally 
        {
            var timer = GetTree().CreateTimer(0.5f);
            timer.Timeout += () => isCaptureProcessing = false;
        }
    }

    public void OnMovePressed()
    {
        GetViewport().SetInputAsHandled();
        if (currentUnit != null && currentWeapon == null)
        {
            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            if (currentTransport != null && currentUnit == null)
            {
                gm?.gridManager?.ClearWeaponRange();
                gm?.gridManager?.ShowMoveRange(currentTransport);
                Hide();
                return;
            }

            if (gm != null)
            {
                gm.gridManager?.ClearWeaponRange();
                foreach (var grid in gm.gridManager.grids)
                {
                    grid.OnClickEmpty = null;
                }
                gm.gridManager.ShowMoveRange(currentUnit);
            }
        }
        Hide();
    }

    private void OnAttackPressed()
    {
        GetViewport().SetInputAsHandled();
        if (currentWeapon != null && currentUnit == null)
        {
            currentWeapon.ShowAttackRange();
        }
        else if (currentUnit != null && currentWeapon == null)
        {
            currentUnit.OnAttackSelected();
        }
        Hide();
    }

    private void OnHealPressed()
    {
        GetViewport().SetInputAsHandled();
        if (currentWeapon is Crystal crystal)
        {
            // 显示治疗范围，等待用户点击范围内格子确认
            crystal.ShowAttackRange();
        }
        Hide();
    }

    private void OnWaitPressed()
    {
        GetViewport().SetInputAsHandled();
        if (currentWeapon != null && currentUnit == null)
        {
            if (currentWeapon is BlackCannon cannon)
                cannon.DoWait();
            currentWeapon = null;
        }
        else if (currentUnit != null && currentWeapon == null)
        {
            currentUnit.OnWaitSelected();
        }
        Hide();
    }

    public void OnInfoPressed()
    {
        GetViewport().SetInputAsHandled();
        if (currentWeapon != null && currentUnit == null)
        {
            ShowWeaponInfo(currentWeapon);
            return;
        }

        if (currentUnit != null && currentWeapon == null)
        {
            ShowUnitInfo();
            return;
        }

        unitInfoLabel.Text = "未选择单位";
        unitInfoLabel.Visible = true;
        closeInfoButton.Visible = true;
    }

    private void ShowUnitInfo()
    {
        ZIndex = 200;
        if (currentUnit == null) return;

        string infoText = $"单位名称: {currentUnit.Name}\n";
        infoText += $"团队: {currentUnit.team}\n";
        infoText += $"最大生命值: {currentUnit.maxHealth}\n";
        infoText += $"生命值: {currentUnit.health}\n";
        infoText += $"攻击力: {currentUnit.baseAttack}\n";
        infoText += $"实时主武器攻击力: {currentUnit.attack}\n";
        infoText += $"实时副武器攻击力: {currentUnit.secondaryAtk}\n";
        infoText += $"防御力: {currentUnit.defense}\n";
        string defenseBonusStr = (int)currentUnit.defenseBonusType switch
        {
            1 => "受地形加成",
            0 => "不受地形加成",
            _ => "未知"
        };
        infoText += $"防御类型: {defenseBonusStr}\n";
        infoText += $"移动力: {currentUnit.movePoints}\n";

        string unitType = currentUnit is FlyBomb ? "飞弹" :
                currentUnit is Oozium ? "Oozium" :
                currentUnit is AntiTank ? "反坦克炮" :
                currentUnit is Mech ? "反坦步兵" :
                currentUnit is LightTank ? "轻型坦克" :
                currentUnit is Recon ? "侦察车" :
                currentUnit is Artillery ? "自行火炮" :
                currentUnit is APC ? "运输车" : 
                currentUnit is Rocket ? "火箭炮" :
                currentUnit is MdTank ? "重型坦克" :
                currentUnit is AntiAir ? "防空高炮" :
                currentUnit is PipeRunner ? "管道炮" :
                currentUnit is Infantry ? "步兵" : "Unknown";

        infoText += $"单位类型: {unitType}\n";

if (currentUnit.canTransportUnits && currentUnit.maxTransportCapacity > 0)
{
    infoText += $"\n=== 可运输单位配置信息 ===\n";

    // 1. 可搭载单位种类
    string loadableTypes = currentUnit.canTransportUnitTypes != null && currentUnit.canTransportUnitTypes.Count > 0
        ? string.Join(", ", currentUnit.canTransportUnitTypes)
        : "无";
    infoText += $"可搭载单位: {loadableTypes}\n";

    // 2. 远程搭载
    infoText += $"远程搭载: {(currentUnit.CanFarMounting ? "✓ 已开启" : "✗ 未开启")}\n";
    if (currentUnit.CanFarMounting)
    {
        infoText += $"远程搭载距离: {currentUnit.minFarMountingDistance}-{currentUnit.maxFarMountingDistance}格\n";
    }

    // 3. 已搭载单位列表 - 用 maxTransportCapacity
    infoText += $"\n[已搭载单位] {currentUnit.transportedUnits.Count}/{currentUnit.maxTransportCapacity}\n";
    if (currentUnit.transportedUnits.Count > 0)
    {
        foreach (var transportedUnit in currentUnit.transportedUnits)
        {
            if (transportedUnit != null && IsInstanceValid(transportedUnit))
            {
                string loadedType = transportedUnit is Mech ? "机甲" :
                                  transportedUnit is LightTank ? "坦克" :
                                  transportedUnit is Oozium ? "史莱姆" :
                                  transportedUnit is Artillery ? "火炮" :
                                  transportedUnit is Infantry ? "步兵" : "未知";
                int hpBars = Mathf.CeilToInt(transportedUnit.health / 10f);
                infoText += $"  • {transportedUnit.Name} ({loadedType}) HP:{hpBars}\n";
            }
        }
    }
    else
    {
        infoText += "  (空)\n";
    }

    infoText += $"\n";
}

        string overlapType = currentUnit.overlapType switch
        {
            UnitOverlapType.NonOverlapping => "NonOverlapping",
            UnitOverlapType.Overlapping => "Overlapping",
            UnitOverlapType.Oozium => "Oozium",
            _ => "Unknown"
        };
        infoText += $"范围类型: {overlapType}\n";

        string primaryWeaponStr;
        if (!currentUnit.hasPrimaryWeapon)
            primaryWeaponStr = "无";
        else if (currentUnit.HasInfinitePrimaryAmmo)
            primaryWeaponStr = $"有 (∞无限)";
        else
            primaryWeaponStr = $"有 (弹药: {currentUnit.currentPrimaryAmmo}/{currentUnit.maxPrimaryAmmo})";

        string secondaryWeaponStr = currentUnit.hasSecondaryWeapon ? "有" : "无";

        string primaryEffect = "";
        if (currentUnit.hasPrimaryWeapon)
        {
            var effects = new System.Collections.Generic.List<string>();
            if (currentUnit.primaryAntiArmor) effects.Add("反装甲");
            if (currentUnit.primaryAntiInfantry) effects.Add("反步兵");
            primaryEffect = $"[{string.Join("/", effects)}]";
        }

        string secondaryEffect = "";
        if (currentUnit.hasSecondaryWeapon)
        {
            var effects = new System.Collections.Generic.List<string>();
            if (currentUnit.secondaryAntiArmor) effects.Add("反装甲");
            if (currentUnit.secondaryAntiInfantry) effects.Add("反步兵");
            secondaryEffect = $"[{string.Join("/", effects)}]";
        }

        infoText += $"主武器: {primaryWeaponStr} {primaryEffect}\n";
        infoText += $"副武器: {secondaryWeaponStr} {secondaryEffect}\n";

        string attackRangeStr = currentUnit.useMinMaxAttackRange 
            ? $"{currentUnit.minAttackRange}-{currentUnit.maxAttackRange}" 
            : currentUnit.attackRange.ToString();
        infoText += $"攻击范围: {attackRangeStr}\n";

        string state = currentUnit.state switch
        {
            UnitState.Idle => "Idle",
            UnitState.Moved => "Moved",
            UnitState.Acted => "Acted",
            _ => "Unknown"
        };
        infoText += $"状态: {state}\n";

        string posText = currentUnit.grid != null 
            ? $"(X:{currentUnit.grid.GridIndex.X},Y:{currentUnit.grid.GridIndex.Y})" 
            : "未绑定格子";
        string moveTypeStr = currentUnit.moveType switch
        {
            MoveType.Infantry => "步兵移动",
            MoveType.Mech => "机甲移动",
            MoveType.Oozium => "Oozium移动",
            MoveType.Treads => "履带移动", 
            MoveType.Tire => "轮胎移动",
            MoveType.AeroSpacer => "空天移动",
            MoveType.AirPlane => "飞机移动",
            MoveType.AirShip => "飞艇移动",
            MoveType.Drone => "无人机移动",
            MoveType.HeliCopter => "直升机移动",
            MoveType.Hover => "登陆艇移动",
            MoveType.Naval => "海军移动",
            MoveType.LAVAHOVER => "岩浆登陆者移动",
            MoveType.LAVARUNNER => "岩浆行者移动",
            MoveType.SpaceShiper => "星舰移动",
            MoveType.PipeRunner => "管道行者移动",
            MoveType.Train => "普通火车移动",
            MoveType.GasTrain => "蒸汽机移动",
            MoveType.FASTER => "高铁移动",
            MoveType.Missile => "导弹飞行",
            _ => "Unknown"
        };
        infoText += $"移动方式: {moveTypeStr}\n";
        string fuelText = currentUnit.consumeFuel 
            ? $"{currentUnit.fuel}/{currentUnit.maxFuel} (阈值:{currentUnit.lowFuelThreshold}, 日耗:{currentUnit.dailyFuelConsumption})" 
            : "无限";
        infoText += $"燃料: {fuelText}\n";

        // ✅ 自爆系统信息
        if (currentUnit.canExplode)
        {
            infoText += $"\n=== 自爆系统 ===\n";
            infoText += $"爆炸范围: {currentUnit.explosionMinRange}-{currentUnit.explosionMaxRange}格\n";
            string damageModeStr = currentUnit.explosionDamageMode switch
            {
                0 => $"固定值 {currentUnit.explosionFixedValue}",
                1 => $"{currentUnit.explosionPercentValue * 100}%最大HP",
                2 => "攻击公式",
                _ => "未知"
            };
            infoText += $"伤害模式: {damageModeStr}\n";
            string targetModeStr = currentUnit.explosionTargetMode switch
            {
                0 => "所有单位",
                1 => "仅敌方",
                2 => "仅友方",
                _ => "未知"
            };
            infoText += $"目标筛选: {targetModeStr}\n";
            infoText += $"摧毁自身: {(currentUnit.explosionDestroysSelf ? "✓" : "✗")}\n";
            infoText += $"可击杀: {(currentUnit.explosionCanKill ? "✓" : "✗ (保留1HP)")}\n";
            infoText += $"影响兵器: {(currentUnit.explosionAffectsWeapons ? "✓" : "✗")}\n";
            infoText += $"回血可超上限: {(currentUnit.explosionCanExceedMaxHealth ? "✓" : "✗")}\n";
        }

        // ✅ 照明模块信息
        if (currentUnit.canIlluminate)
        {
            infoText += $"\n=== 照明模块 ===\n";
            infoText += $"照明弹: {currentUnit.currentFlareAmmo}/{currentUnit.maxFlareAmmo}\n";
            infoText += $"投射射程: {currentUnit.minLaunchRange}-{currentUnit.maxLaunchRange}格\n";
            infoText += $"照明覆盖: {currentUnit.minIlluminationRange}-{currentUnit.maxIlluminationRange}格\n";
            infoText += $"持续回合: {currentUnit.flareDurationTurns}大回合\n";
            infoText += $"移动后可照明: {(currentUnit.canIlluminateAfterMove ? "✓" : "✗")}\n";
        }

        if (currentUnit.grid != null)
        {
            infoText += $"\n=== 自爆系统 ===\n";
            infoText += $"爆炸范围: {currentUnit.explosionMinRange}-{currentUnit.explosionMaxRange}格\n";
            string damageModeStr = currentUnit.explosionDamageMode switch
            {
                0 => $"固定值 {currentUnit.explosionFixedValue}",
                1 => $"{currentUnit.explosionPercentValue * 100}%最大HP",
                2 => "攻击公式",
                _ => "未知"
            };
            infoText += $"伤害模式: {damageModeStr}\n";
            string targetModeStr = currentUnit.explosionTargetMode switch
            {
                0 => "所有单位",
                1 => "仅敌方",
                2 => "仅友方",
                _ => "未知"
            };
            infoText += $"目标筛选: {targetModeStr}\n";
            infoText += $"摧毁自身: {(currentUnit.explosionDestroysSelf ? "✓" : "✗")}\n";
            infoText += $"可击杀: {(currentUnit.explosionCanKill ? "✓" : "✗ (保留1HP)")}\n";
            infoText += $"影响兵器: {(currentUnit.explosionAffectsWeapons ? "✓" : "✗")}\n";
            infoText += $"回血可超上限: {(currentUnit.explosionCanExceedMaxHealth ? "✓" : "✗")}\n";
        }

        if (currentUnit.grid != null)
        {
            infoText += $"\n=== 地形信息 ===\n";
            infoText += $"地形类型: {currentUnit.grid.gridType}\n";
            string terrainDefenseText = currentUnit.grid.GetTerrainDefenseDescription();
            if ((int)currentUnit.defenseBonusType == 0)
                infoText += $"地形防御: {terrainDefenseText} (单位不受加成)\n";
            else
                infoText += $"地形防御: {terrainDefenseText}\n";

            // ✅ 新增：显示格子自定义伤害系统信息
            infoText += $"\n=== 格子自定义效果 ===\n";

            // 血量相关
            infoText += $"[血量系统]\n";
            infoText += $"  可摧毁单位: {(currentUnit.grid.canDestroyUnit ? "✓" : "✗ (锁血1点)")}\n";
            if (currentUnit.grid.fixedDamagePerTurn != 0)
                infoText += $"  固定伤害/回合: {currentUnit.grid.fixedDamagePerTurn} ({(currentUnit.grid.fixedDamagePerTurn > 0 ? "扣血" : "回血")})\n";
            if (currentUnit.grid.fixedAttackPerTurn != 0)
                infoText += $"  固定攻击/回合: {currentUnit.grid.fixedAttackPerTurn} ({(currentUnit.grid.fixedAttackPerTurn > 0 ? "扣血(经防御)" : "回血")})\n";
            infoText += $"  可超血量上限: {(currentUnit.grid.canOverMaxHealth ? "✓" : "✗")}\n";

            // 弹药相关
            infoText += $"\n[弹药系统]\n";
            if (currentUnit.grid.fixedAmmoChangePerTurn != 0)
            {
                string ammoChangeText = currentUnit.grid.fixedAmmoChangePerTurn > 0 ? $"+{currentUnit.grid.fixedAmmoChangePerTurn} (补充)" : $"{currentUnit.grid.fixedAmmoChangePerTurn} (消耗)";
                infoText += $"  弹药变化/回合: {ammoChangeText}\n";
            }
            else
            {
                infoText += $"  弹药变化/回合: 无\n";
            }
            infoText += $"  可超弹药上限: {(currentUnit.grid.ammoCanOverMax ? "✓" : "✗")}\n";
            infoText += $"  可归零: {(currentUnit.grid.ammoCanReachZero ? "✓" : "✗ (强制剩1)")}\n";

            // 燃料相关
            infoText += $"\n[燃料系统]\n";
            if (currentUnit.grid.fixedFuelChangePerTurn != 0)
            {
                string fuelChangeText = currentUnit.grid.fixedFuelChangePerTurn > 0 ? $"+{currentUnit.grid.fixedFuelChangePerTurn} (补充)" : $"{currentUnit.grid.fixedFuelChangePerTurn} (消耗)";
                infoText += $"  燃料变化/回合: {fuelChangeText}\n";
            }
            else
            {
                infoText += $"  燃料变化/回合: 无\n";
            }
            infoText += $"  可超燃料上限: {(currentUnit.grid.fuelCanOverMax ? "✓" : "✗")}\n";
            infoText += $"  可归零: {(currentUnit.grid.fuelCanReachZero ? "✓" : "✗ (强制剩1)")}\n";
        }

        if (currentUnit.grid?.city != null)
        {
            string ownerText = currentUnit.grid.city.facilityTeam switch
            {
                "Player0" => "中立（双方共用）",
                "Player1" => "🔴 Player1 占领",
                "Player2" => "🔵 Player2 占领",
                _ => "无归属（无法补给）"
            };
            infoText += $"CITY归属: {ownerText}\n";
        }

        string captureAbility = currentUnit.captureAbility == CaptureAbility.CanCapture ? "可以占领" : "不能占领";
        infoText += $"占领能力: {captureAbility}\n";
        if (currentUnit.captureAbility == CaptureAbility.CanCapture && currentUnit.grid?.city != null)
        {
            infoText += $"占领力: {currentUnit.GetCapturePower()}\n";
            infoText += $"占领消耗: {currentUnit.grid.city?.capturePointsRequired ?? 20}\n";
            if (currentUnit.currentCaptureProgress > 0)
            {
                infoText += $"当前占领进度: {currentUnit.currentCaptureProgress}/{currentUnit.grid.city?.capturePointsRequired ?? 20}\n";
                if (currentUnit.capturingGrid != null)
                {
                    infoText += $"占领目标: ({currentUnit.capturingGrid.GridIndex.X},{currentUnit.capturingGrid.GridIndex.Y})\n";
                }
            }
        }
        
        // ========== ✅ 新增：战争迷雾视野信息 ==========
        infoText += $"\n=== 视野能力 ===\n";

        // 基础视野
        int actualVision = currentUnit.ActualVisionRange;
        infoText += $"基础视野: {actualVision}格";
        if (!currentUnit.useConfigVision && currentUnit.visionRange >= 0)
            infoText += $" (自定义:{currentUnit.visionRange})";
        else
            infoText += $" (配置表)";
        infoText += "\n";

        // 地形视野加成状态
        if (currentUnit.overrideGlobalTerrainBonus)
        {
            infoText += $"地形加成: 使用专属矩阵 ({currentUnit.unitTerrainVisionBonus?.Count ?? 0}项)\n";
        }
        else
        {
            infoText += "地形加成: 使用全局默认\n";
        }

        // 如果当前在格子上，显示当前地形的实际加成
        if (currentUnit.grid != null)
        {
            int terrainBonus = currentUnit.GetTerrainVisionBonus(currentUnit.grid.gridType);
            string sign = terrainBonus > 0 ? "+" : "";
            infoText += $"当前地形({currentUnit.grid.gridType}): {sign}{terrainBonus}格\n";
            infoText += $"实际视野: {actualVision + terrainBonus}格\n";
        }

infoText += $"所在位置: {posText}\n";

        unitInfoLabel.Text = infoText;
        unitInfoLabel.Visible = true;
        closeInfoButton.Visible = true;
    }

    public void OnCloseInfoPressed()
    {
        GetViewport().SetInputAsHandled();
        unitInfoLabel.Visible = false;
        closeInfoButton.Visible = false;
    }

private void ShowWeaponInfo(Weapon weapon)
    {
        ZIndex = 200;
        string info = "";

        info += $"[兵器] {weapon.Name}\n";
        info += $"团队: {weapon.team}\n";

        // ✅ 修复：兵器血量显示格式（1-99格，不是百分比）
        int hpBars = Mathf.CeilToInt((float)weapon.health / 10f);
        hpBars = Mathf.Clamp(hpBars, 1, 99);
        info += $"血量: {weapon.health}/{weapon.maxHealth} (显示{hpBars}格)\n";

        info += $"状态: {(weapon.hasActed ? "已行动" : "待机")}\n";
        info += $"每回合攻击次数: {weapon.remainingAttacks}/{weapon.maxAttacksPerTurn}\n";
        info += $"可旋转: {(weapon.canRotate ? "是" : "否")}\n";
        info += $"可攻击兵器: {(weapon.CanAttackWeapon ? "是" : "否")}\n";

        if (weapon is BlackCannon cannon)
        {
            info += $"\n=== 黑炮专用配置 ===\n";
            info += $"射程深度: {cannon.maxAttackDepth}格\n";
            info += $"伤害模式: {(cannon.useModifiedDamage ? "改良模式" : "传统模式")}\n";

            // ✅ 新增：弹药系统完整信息
            info += $"[弹药系统]\n";
            if (cannon.useAmmoSystem)
            {
                info += $"弹药: {cannon.currentAmmo}/{cannon.maxAmmo}\n";
                info += $"补给状态: {(cannon.currentAmmo >= cannon.maxAmmo ? "已满" : "可补给")}\n";
            }
            else
            {
                info += $"弹药: 无限 (∞)\n";
            }

            // ✅ 新增：冷却系统完整信息
            info += $"[冷却系统]\n";
            if (cannon.useCooldownSystem)
            {
                info += $"冷却周期: {cannon.cooldownTurns}回合\n";
                info += $"周期攻击次数: {cannon.attacksPerCooldown}次\n";
                info += $"存次数: {(cannon.storeAttacks ? "是" : "否")}\n";
                info += $"当前周期进度: {cannon.turnsSinceLastAttack}/{cannon.cooldownTurns}回合\n";
                info += $"周期剩余次数: {cannon.attacksRemainingInCycle}次\n";
                info += $"冷却就绪: {(cannon.cooldownReady ? "✓ 是" : "✗ 否")}\n";

                if (!cannon.cooldownReady)
                {
                    info += $"还需等待: {cannon.cooldownTurns - cannon.turnsSinceLastAttack}回合\n";
                }
            }
            else
            {
                info += $"冷却: 无（每回合重置）\n";
                info += $"每回合次数: {weapon.maxAttacksPerTurn}次\n";
            }

            // ✅ 新增：综合攻击状态
            info += $"[攻击状态]\n";
            info += $"可攻击: {(cannon.CanAttack() ? "✓ 是" : "✗ 否")}\n";
            if (!cannon.CanAttack())
            {
                if (cannon.useAmmoSystem && cannon.currentAmmo <= 0)
                    info += $"原因: 弹药耗尽\n";
                if (cannon.useCooldownSystem && !cannon.cooldownReady)
                    info += $"原因: 冷却中 ({cannon.turnsSinceLastAttack}/{cannon.cooldownTurns}回合)\n";
                if (cannon.useCooldownSystem && cannon.attacksRemainingInCycle <= 0)
                    info += $"原因: 周期次数已用完\n";
            }

            // 伤害配置
            if (!cannon.useModifiedDamage)
            {
                info += $"[传统伤害]\n";
                info += $"固定伤害: {cannon.fixedDamagePercent * 100:F0}%\n";
                info += $"可摧毁: {(cannon.CanDestroy ? "是" : "否")}\n";
            }
            else
            {
                info += $"[改良伤害]\n";
                info += $"血量变化: {cannon.modifiedHealthPercent * 100:F0}% ({(cannon.modifiedHealthPercent > 0 ? "扣血" : cannon.modifiedHealthPercent < 0 ? "回血" : "无变化")})\n";
                info += $"弹药变化: {cannon.modifiedAmmoPercent * 100:F0}% ({(cannon.modifiedAmmoPercent > 0 ? "消耗" : cannon.modifiedAmmoPercent < 0 ? "补充" : "无变化")})\n";
                info += $"燃料变化: {cannon.modifiedFuelPercent * 100:F0}% ({(cannon.modifiedFuelPercent > 0 ? "消耗" : cannon.modifiedFuelPercent < 0 ? "补充" : "无变化")})\n";
                info += $"可超血量上限: {(cannon.canOverMaxHp ? "是" : "否")}\n";
                info += $"可摧毁: {(cannon.CanDestroy ? "是" : "否")}\n";
            }

            info += $"[其他配置]\n";
            info += $"朝向: {cannon.direction}\n";
            info += $"可攻击兵器: {(cannon.CanAttackWeapon ? "是" : "否")}\n";
            info += $"目标选择: {cannon.targetSelectionMode switch 
            { 
                BlackCannon.TargetSelectionMode.AllSelect => "所有单位",
                BlackCannon.TargetSelectionMode.OnlyEnemyUnits => "仅敌方单位",
                BlackCannon.TargetSelectionMode.OnlyUserUnits => "仅己方单位",
                _ => "未知"
            }}\n";

            info += $"[操作说明]\n";
            info += $"• 左键点击: 选择/显示攻击范围\n";
            info += $"• R键: 旋转方向\n";
            info += $"• 攻击范围: 三角形区域，深度{cannon.maxAttackDepth}格\n";

            if (cannon.useAmmoSystem)
            {
                info += $"• 需要APC在范围内补给弹药\n";
            }
        }

else if (weapon is Laser laser)
        {
            info = laser.GetLaserFullInfo();
        }
        unitInfoLabel.Text = info;
        unitInfoLabel.Visible = true;
        closeInfoButton.Visible = true;
    }    public void ShowMoveChoiceDialog(Infantry movingUnit, Infantry targetUnit, Grids targetGrid, Infantry transport = null)
    {
        // 先隐藏当前菜单
        Hide();

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;

        // 创建弹窗面板
        var dialog = new Control();
        dialog.Name = "MoveChoiceDialog";
        dialog.ZIndex = 500;
        dialog.SetAnchorsPreset(Control.LayoutPreset.Center);

        // 背景遮罩
        var bg = new ColorRect();
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bg.Color = new Color(0, 0, 0, 0.5f);
        dialog.AddChild(bg);

        // 弹窗面板
        var panel = new Panel();
        panel.CustomMinimumSize = new Godot.Vector2(300, 200);
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.2f, 0.2f, 0.3f, 0.95f);
        style.SetCornerRadiusAll(12);
        panel.AddThemeStyleboxOverride("panel", style);
        dialog.AddChild(panel);

        // 标题
        var title = new Label();
        title.Text = $"目标有 {targetUnit.Name}";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 18);
        title.AddThemeColorOverride("font_color", Colors.Yellow);
        title.Position = new Godot.Vector2(0, 10);
        title.Size = new Godot.Vector2(300, 30);
        panel.AddChild(title);

        // 按钮容器
        var vbox = new VBoxContainer();
        vbox.Position = new Godot.Vector2(50, 50);
        vbox.Size = new Godot.Vector2(200, 140);
        vbox.AddThemeConstantOverride("separation", 10);
        panel.AddChild(vbox);

        // 取消按钮
        var cancelBtn = new Button();
        cancelBtn.Text = "❌ 取消";
        cancelBtn.CustomMinimumSize = new Godot.Vector2(200, 35);
        cancelBtn.Pressed += () => {
            dialog.QueueFree();
            // 保持当前移动范围显示，什么都不做
        };
        vbox.AddChild(cancelBtn);

        // 确认移动按钮
        var confirmBtn = new Button();
        confirmBtn.Text = "✅ 确认移动";
        confirmBtn.CustomMinimumSize = new Godot.Vector2(200, 35);
        confirmBtn.Pressed += () => {
            dialog.QueueFree();
            // 执行移动
            var gridMgr = gm?.gridManager;
            if (gridMgr != null)
            {
                gridMgr.MoveTo(movingUnit, targetGrid);
            }
        };
        vbox.AddChild(confirmBtn);

        // 切换单位按钮
        var switchBtn = new Button();
        switchBtn.Text = $"🔄 切换到{targetUnit.Name}";
        switchBtn.CustomMinimumSize = new Godot.Vector2(200, 35);
        switchBtn.Pressed += () => {
            dialog.QueueFree();
            // 关闭当前范围，切换到新单位
            gm?.gridManager?.CloseRange();
            gm?.gridManager?.HideAttackRange();
            gm?.gridManager?.ClearWeaponRange();
            Hide();
            gm?.ClearSelectedInfantry();
            gm?.OnSelectPiece(targetUnit);
        };
        vbox.AddChild(switchBtn);

        // 搭载按钮（仅当APC可用时）
        if (transport != null && transport.CanTransportUnit(movingUnit))
        {
            var mountBtn = new Button();
            mountBtn.Text = $"🚛 搭载到{transport.Name}";
            mountBtn.CustomMinimumSize = new Godot.Vector2(200, 35);
            mountBtn.Pressed += () => {
                dialog.QueueFree();
                // 显示搭载菜单
                ShowTransportLoadMenu(transport, movingUnit);
                gm?.gridManager?.CloseRange();
            };
            vbox.AddChild(mountBtn);
        }

        GetTree().CurrentScene.AddChild(dialog);

    }

    // ========== ✅ 瞭望塔旋转菜单 ==========
    public void ShowGridRotateMenu(Grids grid)
    {
        if (!IsNodeReady()) return;

        if (rotateButton == null)
        {
            return;
        }

        moveButton?.Hide();
        attackButton?.Hide();
        waitButton?.Hide();
        captureButton?.Hide();
        infoButton?.Hide();
        loadButton?.Hide();
        if (unloadContainer != null) unloadContainer.Visible = false;
        supplyButton?.Hide();

        rotateButton.Visible = true;

        foreach (var conn in rotateButton.GetSignalConnectionList(BaseButton.SignalName.Pressed))
        {
            rotateButton.Disconnect(BaseButton.SignalName.Pressed, conn["callable"].AsCallable());
        }

        rotateButton.Pressed += () => RotateTower(grid);

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager != null)
        {
            foreach (var g in gm.gridManager.grids)
            {
                g.OnClickEmpty = () => {
                    Hide();
                    gm.gridManager.ClearAllEmptyCallbacks();
                };
            }
        }

        Position = grid.GlobalPosition + new Vector2(-40, -80);
        Show();
    }

    private void RotateTower(Grids grid)
    {
        if (grid == null || !IsInstanceValid(grid)) return;

        grid.towerDirection = grid.towerDirection switch
        {
            TowerDirection.Up => TowerDirection.Right,
            TowerDirection.Right => TowerDirection.Down,
            TowerDirection.Down => TowerDirection.Left,
            TowerDirection.Left => TowerDirection.Up,
            _ => TowerDirection.Up
        };

        var arrow = grid.GetNodeOrNull<Sprite2D>("TowerDirectionArrow");
        if (arrow != null)
        {
            arrow.RotationDegrees = grid.towerDirection switch
            {
                TowerDirection.Up => 0,
                TowerDirection.Right => 90,
                TowerDirection.Down => 180,
                TowerDirection.Left => 270,
                _ => 0
            };
        }

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.fogOfWarManager?.RefreshFog();

    }

    // ========== ✅ 自爆按钮处理 ==========
    private void OnExplodePressed()
    {
        // ✅ 防重入：如果正在处理爆炸，直接忽略重复信号
        if (isProcessingExplosion) return;
        if (currentUnit == null || !currentUnit.canExplode) return;

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return;

        if (!isExplosionPreview)
        {
            // 第一阶段：显示爆炸范围预览
            isExplosionPreview = true;
            gm.gridManager?.ShowExplosionRange(currentUnit, currentUnit.explosionMinRange, currentUnit.explosionMaxRange);

            moveButton.Visible = false;
            attackButton.Visible = false;
            waitButton.Visible = false;
            infoButton.Visible = false;
            captureButton.Visible = false;
            if (loadButton != null) loadButton.Visible = false;
            if (unloadContainer != null) unloadContainer.Visible = false;
            if (supplyButton != null) supplyButton.Visible = false;
        }
        else
        {
            // 第二阶段：执行爆炸
            isProcessingExplosion = true;
            isExplosionPreview = false;
            gm.gridManager?.HideExplosionRange();
            gm.ExecuteExplosion(currentUnit);
            // ✅ 清理引用，防止已销毁单位被重复操作
            currentUnit = null;
            Hide();
            isProcessingExplosion = false;
        }
    }

    public void CancelExplosionPreview()
    {
        if (isProcessingExplosion) return;
        if (isExplosionPreview)
        {
            isExplosionPreview = false;
            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.gridManager?.HideExplosionRange();
            // 恢复按钮状态
            if (currentUnit != null)
            {
                ShowMenu(currentUnit, currentUnit.isMoved);
            }
        }
    }

    // ========== ✅ 照明按钮处理 ==========
    private void OnIlluminatePressed()
    {
        if (isProcessingIllumination) return;
        if (currentUnit == null || !currentUnit.canIlluminate) return;

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return;

        if (!isFlarePreview && !isIlluminationPreview)
        {
            // 第一阶段：显示照明弹投射射程范围（绿色）
            isFlarePreview = true;
            pendingIlluminationTarget = null;
            gm.gridManager?.ShowFlareRange(currentUnit, currentUnit.minLaunchRange, currentUnit.maxLaunchRange);

            // 设置射程内格子的点击回调
            foreach (var grid in gm.gridManager.flareRange)
            {
                grid.OnClickGrid = to => OnFlareTargetSelected(to);
            }
            // 射程外点击取消
            foreach (var grid in gm.gridManager.grids)
            {
                if (!gm.gridManager.flareRange.Contains(grid))
                {
                    grid.OnClickEmpty = () => CancelIlluminationPreview();
                    grid.OnClickGrid = to => CancelIlluminationPreview();
                }
            }

            // 隐藏其他按钮，只保留照明和待机
            moveButton.Visible = false;
            attackButton.Visible = false;
            captureButton.Visible = false;
            infoButton.Visible = false;
            if (loadButton != null) loadButton.Visible = false;
            if (unloadContainer != null) unloadContainer.Visible = false;
            if (supplyButton != null) supplyButton.Visible = false;
            if (explodeButton != null) explodeButton.Visible = false;
            illuminateButton.Text = "取消照明";
            waitButton.Visible = true;
        }
        else if (isFlarePreview)
        {
            // 在第一阶段点击"取消照明"
            CancelIlluminationPreview();
        }
        else if (isIlluminationPreview)
        {
            // 第二阶段：执行照明
            isProcessingIllumination = true;
            ExecuteIllumination(currentUnit);
            isProcessingIllumination = false;
        }
    }

    private void OnFlareTargetSelected(Grids target)
    {
        if (target == null || currentUnit == null || !currentUnit.canIlluminate) return;

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return;

        // 从第一阶段进入第二阶段
        isFlarePreview = false;
        isIlluminationPreview = true;
        pendingIlluminationTarget = target;

        // 隐藏绿色射程，显示黄色照明覆盖范围
        gm.gridManager?.HideFlareRange();
        gm.gridManager?.ShowIlluminationRange(target, currentUnit.minIlluminationRange, currentUnit.maxIlluminationRange);

        // 清除之前的回调
        foreach (var grid in gm.gridManager.grids)
        {
            grid.OnClickGrid = null;
            grid.OnClickEmpty = null;
        }

        // 更新按钮
        if (illuminateButton != null)
            illuminateButton.Text = "确认照明";
        waitButton.Visible = true;
    }

    private void ExecuteIllumination(Infantry unit)
    {
        if (pendingIlluminationTarget == null || unit == null) return;

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return;

        // 消耗照明弹
        unit.currentFlareAmmo--;

        // 添加照明效果
        gm.fogOfWarManager?.AddIllumination(
            pendingIlluminationTarget,
            unit.minIlluminationRange,
            unit.maxIlluminationRange,
            unit.flareDurationTurns
        );

        // 清理状态
        isIlluminationPreview = false;
        pendingIlluminationTarget = null;
        gm.gridManager?.HideIlluminationRange();

        // 标记单位已行动
        unit.isMoved = true;
        unit.isAttacked = true;
        unit.state = UnitState.Acted;
        unit.originalGrid = null;
        unit.SetWaitVisual(true);

        gm.ClearSelectedInfantry();
        Hide();
    }

    public void CancelIlluminationPreview()
    {
        if (isProcessingIllumination) return;
        if (isFlarePreview || isIlluminationPreview)
        {
            isFlarePreview = false;
            isIlluminationPreview = false;
            pendingIlluminationTarget = null;

            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.gridManager?.HideFlareRange();
            gm?.gridManager?.HideIlluminationRange();

            // 恢复按钮状态
            if (currentUnit != null)
            {
                ShowMenu(currentUnit, currentUnit.isMoved);
            }
        }
    }

    // ✅ 重写 Hide 确保隐藏时取消爆炸预览和照明预览
    public new void Hide()
    {
        CancelExplosionPreview();
        CancelIlluminationPreview();
        base.Hide();
    }
}
