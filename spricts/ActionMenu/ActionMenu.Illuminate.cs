// ActionMenu.cs - 完整修复版，移除所有unloadButton直接引用
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;

// ActionMenu · Illuminate（由 ActionMenu.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class ActionMenu
{

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
            TranslateGroup.SetText(illuminateButton, "取消照明");
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
        if (target == null || currentUnit == null || !currentUnit.canIlluminate || currentUnit.grid == null) return;
        int launchDistance = currentUnit.MinAnchorDistance(target.GridIndex);   // ✅ issue#1 二期：锚点合集取最近
        var rangeGm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (rangeGm?.gridManager == null || launchDistance < currentUnit.minLaunchRange || launchDistance > currentUnit.maxLaunchRange
            || !currentUnit.GetAnchorCells().Any(a => a != null && rangeGm.gridManager.IsRangeAllowed(a, target, currentUnit.flareLaunchCanCrossScreens))) return;

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return;

        // 从第一阶段进入第二阶段
        isFlarePreview = false;
        isIlluminationPreview = true;
        pendingIlluminationTarget = target;

        // 隐藏绿色射程，显示黄色照明覆盖范围
        gm.gridManager?.HideFlareRange();
        gm.gridManager?.ShowIlluminationRange(target, currentUnit.minIlluminationRange, currentUnit.maxIlluminationRange, currentUnit.flareEffectCanCrossScreens);

        // 清除之前的回调
        foreach (var grid in gm.gridManager.grids)
        {
            grid.OnClickGrid = null;
            grid.OnClickEmpty = null;
        }

        // 更新按钮
        if (illuminateButton != null)
            TranslateGroup.SetText(illuminateButton, "确认照明");
        waitButton.Visible = true;
    }

    private void ExecuteIllumination(Infantry unit)
    {
        if (pendingIlluminationTarget == null || unit == null || unit.grid == null) return;
        int targetDistance = unit.MinAnchorDistance(pendingIlluminationTarget.GridIndex);   // ✅ issue#1 二期：锚点合集取最近
        var validationGm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (validationGm?.gridManager == null || targetDistance < unit.minLaunchRange || targetDistance > unit.maxLaunchRange
            || !unit.GetAnchorCells().Any(a => a != null && validationGm.gridManager.IsRangeAllowed(a, pendingIlluminationTarget, unit.flareLaunchCanCrossScreens))) return;

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return;

        // 消耗照明弹
        unit.TryConsumeAmmo(Infantry.AmmoKind.Flare);   // ✅ 统一弹药系统

        // 添加照明效果
        gm.fogOfWarManager?.AddIllumination(
            pendingIlluminationTarget,
            unit.minIlluminationRange,
            unit.maxIlluminationRange,
            unit.flareDurationTurns,
            unit.flareEffectCanCrossScreens
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
}
