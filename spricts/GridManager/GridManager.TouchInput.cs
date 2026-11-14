// GridManager.cs - 修改 HandleGridClick 和 SetupMoveRangeCallbacks 方法
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

// GridManager · TouchInput（由 GridManager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class GridManager
{

    // ========== ✅ 移动端触摸轨迹预览状态 ==========
    private bool isMobilePlatform = false;
    private bool touchPathActive = false;
    private System.Collections.Generic.List<Grids> touchPathGrids;   // ✅ 手画路径航点

    // ✅ 单位交互激活态：移动范围显示/画轨迹期间，相机须让位
    public bool IsMoveRangeActive => moveRange != null && moveRange.Count > 0 && selectedUnit != null;
    private Grids touchHoverGrid = null;

    // ========== ✅ 移动端：手指划过显示移动轨迹（移动范围激活时接管触摸输入） ==========
    public override void _Input(InputEvent @event)
    {
        if (!isMobilePlatform) return;

        bool moveRangeActive = moveRange.Count > 0 && selectedUnit != null;

        if (@event is InputEventScreenTouch touch)
        {
            if (touch.Pressed)
            {
                // ✅ 2026-11-14 多指判定改用系统 touch.Index（第一指恒为 0，系统回收复用）——
                //    旧的 activeTouchCount 加减法会因安卓吞掉 Released 事件而卡死 ≥1，
                //    此后每根手指都被误判"第二根"（双指缩放分支），单指画轨迹全局报废。Index 法无状态无卡死。
                if (!moveRangeActive) { touchPathActive = false; return; }
                if (touch.Index > 0)
                {
                    // 双指操作（缩放相机）：退出轨迹模式，交给相机控制器
                    touchPathActive = false;
                    touchHoverGrid = null;
                    ClearPathPreview();
                    return;
                }
                // 起点在 UI 控件上：不干预，让按钮正常响应
                if (IsTouchOverUI(touch.Position)) { touchPathActive = false; return; }
                // 不拦截事件：格子拾取已被禁用，相机平移由 Drag 拦截
                touchPathActive = true;
                UpdateTouchHover(touch.Position);
            }
            else
            {
                if (!touchPathActive) return;
                touchPathActive = false;
                var target = touchHoverGrid;
                touchHoverGrid = null;
                ClearPathPreview();
                // ✅ 手画路径优先：有航点按航点执行（路径路程计费/绕圈合法/截断于阻挡）
                var drawn = touchPathGrids;
                touchPathGrids = null;
                if (moveRangeActive && target != null)
                {
                    if (drawn != null && drawn.Count > 0 && HandleDrawnPath(drawn))
                        return;
                    if (target.OnClickGrid != null) target.OnClickGrid.Invoke(target);
                    else target.OnClickEmpty?.Invoke();
                }
                // 不拦截：让相机控制器清理触摸状态
            }
        }
        else if (@event is InputEventScreenDrag drag)
        {
            if (!moveRangeActive || !touchPathActive) return;
            UpdateTouchHover(drag.Position);
            GetViewport().SetInputAsHandled(); // 画轨迹期间阻止相机平移
        }
    }

    private void UpdateTouchHover(Vector2 screenPos)
    {
        var grid = ScreenPosToGrid(screenPos);
        touchHoverGrid = grid;
        if (grid != null && moveRange.Contains(grid))
        {
            // ✅ 记录手画航点（去重、断链容忍，校验在松手时）
            if (touchPathGrids == null) touchPathGrids = new System.Collections.Generic.List<Grids>();
            if (touchPathGrids.Count == 0 || touchPathGrids[touchPathGrids.Count - 1] != grid)
                touchPathGrids.Add(grid);
            // ✅ 手画预览：画线走你的手指路线（含截断红标），不再是优化最短路径
            if (touchPathGrids.Count > 0)
                DrawTouchPathPreview(touchPathGrids);
            else
                OnMouseEnteredGrid(grid);
        }
        else
            ClearPathPreview();
    }

    private Grids ScreenPosToGrid(Vector2 screenPos)
    {
        if (map == null) return null;
        Vector2 world = GetViewport().GetCanvasTransform().AffineInverse() * screenPos;
        int x = Mathf.FloorToInt((world.X - startPos.X) / gridSize.X);
        int y = Mathf.FloorToInt((world.Y - startPos.Y) / gridSize.Y);
        if (x < 0 || x >= searchRange.X || y < 0 || y >= searchRange.Y) return null;
        return map[x, y];
    }

    // 检查屏幕坐标是否落在任何可见可交互 UI 控件上（避免拦截按钮点击）
    private bool IsTouchOverUI(Vector2 screenPos)
    {
        return FindUIAtPoint(GetTree().Root, screenPos);
    }

    private bool FindUIAtPoint(Node node, Vector2 screenPos)
    {
        foreach (var child in node.GetChildren())
        {
            // 先递归子控件（更具体的优先）
            if (FindUIAtPoint(child, screenPos)) return true;
            // ✅ 2026-11-14 收紧：只拦"会真正吃掉点击"的交互控件。原版任何可见 Control（纯布局
            //    容器/标签/透明装饰底衬，Godot 默认鼠标过滤=STOP）都算挡路，画轨迹被大面积误杀。
            if (child is Control c && c.IsVisibleInTree()
                && c.GetGlobalRect().HasPoint(screenPos)
                && IsInteractiveBlockingUI(c))
                return true;
        }
        return false;
    }

    // 真正会消费触摸的控件才算阻挡：按钮/面板/弹窗/输入框/列表/滑条等。
    // 纯布局容器、Label、透明装饰 ColorRect 不拦——它们的 STOP 只是 Godot 默认值，不是真要交互。
    private static bool IsInteractiveBlockingUI(Control c)
    {
        if (c.MouseFilter != Control.MouseFilterEnum.Stop) return false;
        // 注意：PopupPanel/PopupMenu 均派生自 Popup；CodeEdit 派生自 TextEdit——只写基类避免警告。
        // AcceptDialog/ConfirmationDialog 是独立 Window（不挂场景树），天然不在拾取范围。
        return c is BaseButton || c is Panel || c is PanelContainer   // ✅ Popup为Window非Control,静态不可能——移除消警告(其135行注释已述)
            || c is LineEdit || c is TextEdit
            || c is ItemList || c is Tree || c is TabContainer
            || c is ScrollContainer || c is SplitContainer
            || c is Slider || c is SpinBox || c is ColorPicker;
    }

    // ✅ 手画轨迹预览：沿手指路线画线，第一个可见阻挡格红标截断
    private void DrawTouchPathPreview(System.Collections.Generic.List<Grids> path)
    {
        if (selectedUnit == null) return;
        ResetMoveRangeIconColors();
        previewPath = path;
        pathLine?.ClearPoints();
        if (pathLine != null)
            foreach (var g in path)
                if (g != null) pathLine.AddPoint(g.Position + new Vector2(20, 20));
        if (pathLine != null) pathLine.Visible = !moveArrowsReplaceLine;   // ✅ CoW 箭头模式下可隐藏黄线
        UpdateMoveArrows();   // ✅ CoW 式路径方向箭头（手画路径同步）
        for (int i = 0; i < path.Count; i++)
        {
            var g = path[i];
            if (g?.infantries == null) continue;
            if (g.infantries.Any(u => Infantry.IsTruncationBlocker(selectedUnit, u) && Infantry.IsVisibleTo(u, selectedUnit.team, grids)))
            {
                g.pathIcon?.Show();
                g.pathIcon?.SetDeferred("modulate", Colors.Red);
                break;   // 截断点之后不再渲染
            }
        }
    }

    // ✅ 手画路径执行：路径路程计费、绕圈合法、终点≠起点、截断于路上单位（可见判定，隐形标准直通雾战）
    private bool HandleDrawnPath(System.Collections.Generic.List<Grids> waypoints)
    {
        if (selectedUnit == null || waypoints == null) return false;
        var infantry = selectedUnit;
        int budget = infantry.movePoints;
        var valid = new System.Collections.Generic.List<Grids>();
        int spent = 0;
        Grids prev = infantry.grid;
        foreach (var wp in waypoints)
        {
            if (wp == null || wp == prev) continue;
            // 终点=起点：本回合拒绝绕回原格
            if (wp == infantry.grid) break;
            int dist = Mathf.Abs(wp.GridIndex.X - prev.GridIndex.X) + Mathf.Abs(wp.GridIndex.Y - prev.GridIndex.Y);
            if (dist != 1) break;   // 断链（快速滑动跳格）
            int c = GetActualMoveCost(infantry, wp);
            // 路上有“敌方”单位即截断（可见判定——隐形的存在不截断、不泄漏，雾战同标准）；友军可穿过
            var blockerU = wp.infantries.FirstOrDefault(u => Infantry.IsTruncationBlocker(infantry, u) && Infantry.IsVisibleTo(u, infantry.team, grids));
            if (blockerU != null)
            {
                pendingEncounterBlocker = blockerU;   // ✅ 记录截断者，落定后结算遭遇战
                break;
            }
            if (spent + c > budget) break;
            spent += c;
            valid.Add(wp);
            prev = wp;
        }
        if (valid.Count == 0) { pendingEncounterBlocker = null; return false; }
        var dest = valid[valid.Count - 1];
        if (dest == infantry.grid) { pendingEncounterBlocker = null; return false; }
        // ✅ 终点有友军：无敌情时——能上车/合流就走统一点击入口（搭载菜单/合流），否则是非法落点，拒绝而非静默截断；
        //    被敌方截断时——退到最后一个空的可停留格
        if (pendingEncounterBlocker == null)   // 终点藏着看不见的敌人 → 视为伏击截断
            pendingEncounterBlocker = dest.infantries.FirstOrDefault(u => Infantry.IsTruncationBlocker(infantry, u));
        bool Occupied(Grids g) => g.infantries.Any(u => u != null && u != infantry && IsInstanceValid(u));
        if (Occupied(dest))
        {
            if (pendingEncounterBlocker == null)
            {
                if (!CanStayOnGrid(dest, infantry)) return false;
                HandleGridClick(infantry, dest);
                return true;
            }
            while (valid.Count > 0 && (Occupied(valid[valid.Count - 1]) || !CanStayOnGrid(valid[valid.Count - 1], infantry)))
                valid.RemoveAt(valid.Count - 1);
            if (valid.Count == 0) { pendingEncounterBlocker = null; return false; }
            dest = valid[valid.Count - 1];
        }
        CloseRange();   // ✅ 手画路径直接执行，须自清范围（点按流由回调收拾；不清=全局输入卡死）
        MoveTo(infantry, dest, showMenu: true, forcedPath: valid);
        // ✅ 截断遭遇结算：落定后按双方Export仲裁（总开关=是否攻击）
        var bk = pendingEncounterBlocker; pendingEncounterBlocker = null;
        if (bk != null)
        {
            Infantry.ResolveTruncationEncounter(infantry, bk);   // ✅ 回合中回合由入口判定人类/AI
            // ✅ 非强制截断：新格重绑+剩余实际移动力+二次移动
            if (!infantry.truncForcedStop && infantry.movePoints > 0 && infantry.health > 0)
            {
                infantry.state = UnitState.Idle;
                infantry.isMoved = false;
                infantry.isAttacked = false;
                infantry.hasActed = false;
                infantry.SetWaitVisual(false);
                ShowMoveRange(infantry);
            }
        }
        return true;
    }
}
