// ActionMenu.cs - 完整修复版，移除所有unloadButton直接引用
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;

// ActionMenu · BasicActions（由 ActionMenu.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class ActionMenu
{

    private bool isCaptureProcessing = false;

    public void OnCapturePressed()
    {
        GetViewport().SetInputAsHandled();
        if (isCaptureProcessing) return;
        if (currentUnit == null) return;

        isCaptureProcessing = true;

        try 
        {
            // 拆旗分流：可拆旗且本格不可占领且无占领进度时，执行拆旗
            if (currentUnit.canTearFlag && !currentUnit.CanCaptureCurrentGrid() && currentUnit.currentCaptureProgress <= 0)
                currentUnit.PerformTearFlag();
            else if (currentUnit.CanPerformLoadedTearFlag())
                currentUnit.PerformLoadedTearFlag();   // ✅ 舱内撕旗
            else if (currentUnit.CanPerformLoadedCapture())
                    currentUnit.PerformLoadedCapture();   // ✅ 舱内占领
                else if (!currentUnit.CanCaptureCurrentGrid())
            {
                // 开创人2026-11-11 远程占领：单目标直接开工，多目标弹选择菜单（只看设施判定）
                var remoteTargets = currentUnit.GetRemoteCapturableFacilities();
                if (remoteTargets.Count == 1) currentUnit.PerformRemoteCapture(remoteTargets[0]);
                else if (remoteTargets.Count > 1) ShowRemoteCaptureTargetMenu(remoteTargets);
            }
            else
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

    /// <summary>
    /// 快速移动：选中即进入移动范围（不弹菜单）。不能移动时返回 false，由调用方照常弹菜单。
    /// 判定与「移动」按钮可见性一致：未移动 + CanMove（燃料）+ 有移动力 + 隐身行为锁。
    /// </summary>
    public bool TryQuickMove(Infantry unit)
    {
        if (unit == null || !IsInstanceValid(unit)) return false;
        if (unit.isMoved || unit.state == UnitState.Acted || !unit.CanMove()) return false;
        if (unit.movePoints <= 0) return false;
        if (unit.isStealthed && !unit.canMoveWhileStealthed) return false;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return false;

        Hide();
        currentUnit = unit;
        currentWeapon = null;
        currentTransport = null;
        gm.gridManager.ClearWeaponRange();
        gm.gridManager.HideAttackRange();
        foreach (var grid in gm.gridManager.grids) grid.OnClickEmpty = null;
        gm.gridManager.ShowMoveRange(unit);   // 无可达格也进入范围模式：再点本单位即原地菜单
        // ✅ 开创人2026-11-11：移动范围为空（四周皆不可通行/脚下非法）不再吞首击——当场弹菜单，攻击/等待照常可操作
        if (gm.gridManager.moveRange == null || gm.gridManager.moveRange.Count == 0)
            ShowMenu(unit);
        return true;
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
            if (currentUnit.HasLoadedAttackOption())
                currentUnit.OnLoadedAttackSelected();   // ✅ 舱内攻击
            else
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
public void ShowMoveChoiceDialog(Infantry movingUnit, Infantry targetUnit, Grids targetGrid, Infantry transport = null)
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
        title.Text = $"{TranslateGroup.T("目标有")} {targetUnit.Name}";
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
        TranslateGroup.SetText(cancelBtn, "❌ 取消");
        cancelBtn.CustomMinimumSize = new Godot.Vector2(200, 35);
        cancelBtn.Pressed += () => {
            dialog.QueueFree();
            // 保持当前移动范围显示，什么都不做
        };
        vbox.AddChild(cancelBtn);

        // 确认移动按钮
        var confirmBtn = new Button();
        TranslateGroup.SetText(confirmBtn, "✅ 确认移动");
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
        switchBtn.Text = $"🔄 {TranslateGroup.T("切换到")}{targetUnit.Name}";
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
            mountBtn.Text = $"🚛 {TranslateGroup.T("搭载到")}{transport.Name}";
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


    private PopupPanel remoteCapturePopup;
    /// 远程占领多目标选择菜单（开创人2026-11-11）
    private void ShowRemoteCaptureTargetMenu(List<Facility> targets)
    {
        if (remoteCapturePopup != null && IsInstanceValid(remoteCapturePopup)) remoteCapturePopup.QueueFree();
        remoteCapturePopup = new PopupPanel();
        var vbox = new VBoxContainer();
        remoteCapturePopup.AddChild(vbox);
        vbox.AddChild(new Label { Text = "选择远程占领目标" });
        foreach (var t in targets)
        {
            if (t == null || !IsInstanceValid(t)) continue;
            var btn = new Button { Text = $"{t.GetType().Name} ({t.ParentGrid?.GridIndex.X},{t.ParentGrid?.GridIndex.Y})" };
            Facility captured = t;
            btn.Pressed += () =>
            {
                if (remoteCapturePopup != null && IsInstanceValid(remoteCapturePopup)) remoteCapturePopup.QueueFree();
                remoteCapturePopup = null;
                currentUnit?.PerformRemoteCapture(captured);
            };
            vbox.AddChild(btn);
        }
        AddChild(remoteCapturePopup);
        remoteCapturePopup.PopupCentered();
    }
}
