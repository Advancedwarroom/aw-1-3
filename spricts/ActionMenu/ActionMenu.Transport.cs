// ActionMenu.cs - 完整修复版，移除所有unloadButton直接引用
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;

// ActionMenu · Transport（由 ActionMenu.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class ActionMenu
{


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

    // ✅ P-1 公共运输单位：预计算当前回合势力
    var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
    string currentTurnTeam = "";
    if (transport.team == TeamHelper.Player)
    {
        currentTurnTeam = TeamHelper.GetPhaseTeamName(gm?.turnPhase ?? 1);
    }

    // 创建卸载按钮
    foreach (var unit in transport.transportedUnits)
    {
        if (unit == null || !IsInstanceValid(unit)) continue;
        if (transport.team == TeamHelper.Player && unit.team != currentTurnTeam)
            continue;

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

    // ✅ 究极自由：通用运输单位装载菜单
    public void ShowTransportLoadMenu(Infantry transport, Infantry unit)
    {
        currentUnit = unit;
        currentWeapon = null;
        currentTransport = transport;  // ✅ 通用：任何 canTransportUnits 的单位

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
        // ✅ 统一菜单（开创人 2026-11-11）：独立运输菜单链废弃，全部调用并入主 ShowMenu
        ShowMenu(transport, afterMove, onlyShowMenu: true);
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
    TranslateGroup.SetText(label, "选择要搭载的运输单位:");
    container.AddChild(label);

     // 为每个运输单位创建选择按钮
    foreach (var transport in transports)
    {
        var btn = new Button();
        btn.CustomMinimumSize = new Vector2(140, 40);

        // 显示运输单位信息和距离
        int dist = Mathf.Abs(currentUnit.grid.GridIndex.X - transport.grid.GridIndex.X)
                 + Mathf.Abs(currentUnit.grid.GridIndex.Y - transport.grid.GridIndex.Y);
        string transportType = transport.DisplayLabel;
        btn.Text = $"{transportType} ({TranslateGroup.T("距离")}:{dist}) HP:{Mathf.CeilToInt(transport.health/10f)}";

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
        if (transport.CanUnloadUnitToGrid(unit, grid))   // ✅ 乘员必须能站上该地形
        {
            bool launching = transport.CanLaunchCargoUnit(unit);   // ✅ 舰载起飞：青色范围
            grid.pathIcon?.Show();
            grid.pathIcon.Modulate = launching ? new Color(0, 1, 1, 0.7f) : new Color(0, 1, 0, 0.7f);

            grid.OnClickGrid = to => 
            {
                bool success = launching ? transport.LaunchCargoUnit(unit, to) : transport.UnloadUnit(unit, to);

                // 立即强制关闭所有绿色范围
                ForceCloseUnloadRange(gm, availableGrids);

                if (success && launching)
                {
                    // 起飞：舰载机保留完整行动，载具只是本回合不能再移动
                    var gmL = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
                    gmL?.ClearSelectedInfantry();
                    if (transport.transportedUnits.Any(transport.CanLaunchCargoUnit)) ShowTransportUnloadMenu(transport, afterMove);
                    else Hide();
                    return;
                }

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
                && (u.team == transport.team || transport.team == TeamHelper.Player)
                && transport.CanTransportUnit(u) 
                && (afterMove || !u.isMoved) 
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
    captureButton.Visible = transport.CanPerformLoadedCapture();

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

        // ✅ P-1 公共运输单位：只显示当前势力可卸载的单位
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        string currentTurnTeam = TeamHelper.GetPhaseTeamName(gm?.turnPhase ?? 1);
        if (transport.team == TeamHelper.Player && unit.team != currentTurnTeam)
            continue;


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
}
