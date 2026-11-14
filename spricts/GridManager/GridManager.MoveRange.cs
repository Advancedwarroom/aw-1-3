// GridManager.cs - 修改 HandleGridClick 和 SetupMoveRangeCallbacks 方法
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

// GridManager · MoveRange（由 GridManager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class GridManager
{

    public void SetEmptyClickToCloseMenuOnly(Infantry unit)
    {
        foreach (var grid in grids)
        {
            grid.OnClickEmpty = () => 
            {
                CloseRange();
                HideAttackRange();
                ClearWeaponRange();
                var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                actionMenu?.CancelExplosionPreview(); // ✅ 取消爆炸预览
                actionMenu?.Hide();
                
                var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
                gm?.ClearSelectedInfantry();
            };
        }
    }

    public List<Grids> BuildPath(Grids from, Grids to, Infantry infantry = null, bool ignoreMovePoints = false)
    {
        if (from == null || to == null) return new List<Grids>();
        if (from == to) return new List<Grids>();

        if (infantry != null)
        {
            var open = new PriorityQueue<Grids, int>();
            var parent = new Dictionary<Grids, Grids>();
            var best = new Dictionary<Grids, int>();
            open.Enqueue(from, 0);
            parent[from] = null;
            best[from] = 0;

            while (open.Count > 0)
            {
                var cur = open.Dequeue();
                int accCost = best[cur];

                if (cur == to) break;

                for (int i = 0; i < 4; i++)
                {
                    var nIdx = cur.GridIndex + offset[i];
                    if (nIdx.X < 0 || nIdx.X >= searchRange.X ||
                        nIdx.Y < 0 || nIdx.Y >= searchRange.Y) continue;

                    var neighbor = map[nIdx.X, nIdx.Y];
                    if (neighbor == null) continue;

                    // ✅ N屏隔离：寻路不跨屏
                    if (neighbor.screenId != from.screenId) continue;

                    int moveCost = infantry.GetMoveCostOnGrid(neighbor);   // ✅ 与实际移动一致（含港口靠泊等设施覆盖）
                    if (moveCost >= 999 || moveCost == int.MaxValue) continue;

                    if (neighbor.weapon != null && !neighbor.weapon.AllowsPassage(infantry))
                        continue;

                    // ✅ issue#1：他方多格机动单位足印阻挡（Oozium 豁免）
                    if (neighbor.multiTileOccupant != null && neighbor.multiTileOccupant != infantry
                        && IsInstanceValid(neighbor.multiTileOccupant) && !neighbor.multiTileOccupant.IsQueuedForDeletion())
                        continue;

                    int newCost = accCost + moveCost;
                    if (best.TryGetValue(neighbor, out int old) && newCost >= old) continue;
                    if (!ignoreMovePoints && newCost > infantry.movePoints) continue;

                    best[neighbor] = newCost;
                    parent[neighbor] = cur;
                    open.Enqueue(neighbor, newCost);
                }
            }

            var path = new List<Grids>();
            var walk = to;
            while (walk != null)
            {
                path.Add(walk);
                if (walk == from) break;
                walk = parent.GetValueOrDefault(walk, null);
            }
            if (walk == from)
            {
                path.Reverse();
                return path;
            }
            return new List<Grids>();
        }

        var queue = new Queue<Vector2I>();
        var parent2 = new Dictionary<Vector2I, Vector2I>();
        var fromIdx = from.GridIndex;
        var toIdx = to.GridIndex;
        queue.Enqueue(fromIdx);
        parent2[fromIdx] = new Vector2I(-1, -1);

        while (queue.Count > 0)
        {
            var curIdx = queue.Dequeue();
            if (curIdx == toIdx) break;

            var curGrid = map[curIdx.X, curIdx.Y];
            if (curGrid == null) continue;

            foreach (var n in GetNeighbours(curGrid, true))
            {
                if (n == null) continue;
                var nIdx = n.GridIndex;
                if (parent2.ContainsKey(nIdx)) continue;
                parent2[nIdx] = curIdx;
                queue.Enqueue(nIdx);
            }
        }

        var path2 = new List<Grids>();
        var walkIdx = toIdx;
        while (walkIdx != new Vector2I(-1, -1))
        {
            if (walkIdx.X >= 0 && walkIdx.X < searchRange.X && walkIdx.Y >= 0 && walkIdx.Y < searchRange.Y)
            {
                var grid = map[walkIdx.X, walkIdx.Y];
                if (grid != null) path2.Add(grid);
            }
            walkIdx = parent2.GetValueOrDefault(walkIdx, new Vector2I(-1, -1));
        }
        path2.Reverse();
        return path2.Count > 1 ? path2 : new List<Grids>();
    }

    public void OnMouseEnteredGrid(Grids grid)
    {
        if (selectedUnit == null || selectedUnit.grid == null) return;
        if (!moveRange.Contains(grid)) return;

        // 恢复所有移动范围格子的基础颜色（白色可达/红色不可停留）
        ResetMoveRangeIconColors();

        previewPath = BuildPath(selectedUnit.grid, grid, selectedUnit);

        // ✅ 战争迷雾截断：正序扫描路径，撞到不可移动格子（敌方单位/兵器/不可通行地形）即截断，
        // 与 HandleGridClick / HandleTruncatedMove 的执行判定保持一致（不检查可见性）
        int truncateCount = previewPath.Count;
        if (selectedUnit.overlapType == UnitOverlapType.NonOverlapping)
        {
            for (int i = 1; i < previewPath.Count; i++)
            {
                var g = previewPath[i];
                bool blocked = false;

                g.infantries.RemoveAll(u => u == null || !IsInstanceValid(u));
                if (g.infantries.Any(u => Infantry.IsTruncationBlocker(selectedUnit, u)
                        && Infantry.IsVisibleTo(u, selectedUnit.team, grids)))
                    blocked = true; // 敌方单位阻挡（✅ 隐身中看不见的不参与预览截断，防暴露；实际移动仍会被截断）
                else if (selectedUnit is not Oozium && g.weapon != null && !g.weapon.AllowsPassage(selectedUnit))
                    blocked = true; // 兵器阻挡
                else
                {
                    int moveCost = selectedUnit.GetMoveCostOnGrid(g);
                    if (moveCost >= 999 || moveCost == int.MaxValue)
                        blocked = true; // 地形不可通行
                    else if (unitManager?.CanMoveTo(g.GridIndex, selectedUnit) == false)
                        blocked = true;
                }

                if (blocked)
                {
                    truncateCount = i; // 截断到阻挡格之前
                    break;
                }
            }
        }

        // ✅ 截断后的路径前缀标记为紫色（仅移动范围内的格子）
        for (int i = 1; i < truncateCount; i++)
        {
            var g = previewPath[i];
            if (moveRange.Contains(g) && g?.pathIcon != null)
                g.pathIcon.Modulate = new Color(0.7f, 0.3f, 1.0f, 1.0f); // 紫色
        }

        UpdatePathLine(truncateCount);
    }

    public void OnMouseExitedGrid(Grids _)
    {
        ClearPathPreview();
    }

    // 清除轨迹预览（紫色格 + 黄色线 + 方向箭头），恢复移动范围基础颜色
    public void ClearPathPreview()
    {
        previewPath.Clear();
        pathLine?.ClearPoints();
        ClearMoveArrows();   // ✅ CoW 方向箭头同步清除
        ResetMoveRangeIconColors();
    }

    // 恢复移动范围格子的基础颜色：可见的不可停留格为红色，其余为白色
    private void ResetMoveRangeIconColors()
    {
        if (selectedUnit == null) return;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var fog = gm?.fogOfWarManager;
        bool isFogEnabled = fog != null && fog.isFogOfWarEnabled;

        foreach (var g in moveRange)
        {
            if (g?.pathIcon == null) continue;
            bool canStay = CanStayOnGrid(g, selectedUnit);
            bool gridVisible = !isFogEnabled || fog.IsGridVisible(g);
            if (HasOnlyHiddenStealthBlockers(g, selectedUnit)) gridVisible = false;   // ✅ 只被看不见的隐身敌军占据：不标红（防暴露）
            if (!canStay && selectedUnit.overlapType == UnitOverlapType.NonOverlapping && gridVisible)
                g.pathIcon.Modulate = new Color(1, 0.3f, 0.3f, 0.6f); // 红色不可停留
            else
                g.pathIcon.Modulate = Colors.White;
        }
    }

    public void UpdatePathLine(int pointCount = -1)
    {
        if (pathLine == null) return;
        pathLine.ClearPoints();
        int count = pointCount < 0 ? previewPath.Count : Mathf.Min(pointCount, previewPath.Count);
        for (int i = 0; i < count; i++)
            pathLine.AddPoint(previewPath[i].Position + new Godot.Vector2(16, 16));
        pathLine.Visible = !moveArrowsReplaceLine;   // ✅ CoW 箭头模式下可隐藏黄线
        UpdateMoveArrows(pointCount);                // ✅ CoW 式路径方向箭头
    }

    public List<Grids> FindRange(Grids grid, int distance, bool walk = false, bool allowCrossScreens = false)
    {
        List<Grids> range =new List<Grids>();
        List<Grids> now =new List<Grids>();
        List<Grids> open =new List<Grids>();

        now.Add(grid);
        for (int i = 0; i < distance; i++)
        {
            foreach (var current in now)
            {
                List<Grids> neighbours = GetNeighbours(current,true,allowCrossScreens);
                foreach (var neighbour in neighbours)
                {
                    if(!open.Contains(neighbour) && neighbour !=grid)
                    {
                        open.Add(neighbour);
                        range.Add(neighbour);
                    }
                }
            }
            now.Clear();
            foreach(var item in open)
                now.Add(item);
            open.Clear();
        }
        return range;
    }

    public void ShowMoveRange(Infantry infantry)
    {
        Grids.IsForceActionMode = true;
    var terrainEditor = GetTree().GetFirstNodeInGroup("terrain_editor") as TerrainEditor;
    if (terrainEditor != null && terrainEditor.ShouldBlockUnitOperations())
    {
        return;
    }

        selectedUnit = infantry;
        if (infantry == null || infantry.grid == null) return;

        ForceClearAllRangeVisuals();
        
        CloseRange();
        moveRange.Clear();
        HideAttackRange();
        ClearWeaponRange(); 

        int effectiveMovePoints = infantry.movePoints;
        // ✅ CO 模块系统·模块5（移动模块：移动力加成）
        effectiveMovePoints += CoModuleSystem.GetMovementBonus(infantry);
        if (infantry.consumeFuel)
            effectiveMovePoints = Mathf.Min(effectiveMovePoints, infantry.movePoints + infantry.fuel);

        // ✅ 究极自由：所有单位使用统一的通用移动范围计算
        CalculateMoveRangeGeneric(infantry, effectiveMovePoints);
        SetupMoveRangeCallbacks(infantry);

        // ✅ issue#1 多格机动单位：选中时足印高亮（本体轮廓蓝；ForceClearAllRangeVisuals 统一清理，不用额外擦除）
        if (infantry.isMultiTileUnit && infantry.footprintGrids != null)
        {
            foreach (var fg in infantry.footprintGrids)
            {
                if (fg == null || !IsInstanceValid(fg)) continue;
                if (fg.pathIcon != null)
                {
                    fg.pathIcon.Modulate = new Color(0.3f, 0.7f, 1f, 0.9f);   // 本体轮廓蓝
                    fg.pathIcon.Show();
                }
            }
        }

        // ✅ 移动端：移动范围激活期间禁用格子拾取，防止触摸按下即触发点击（改为滑动轨迹+松手确认）
        if (isMobilePlatform)
        {
            foreach (var g in grids)
                g?.SetAreaPickable(false);
        }
    }

    private void ForceClearAllRangeVisuals()
    {
        foreach (var grid in grids)
        {
            if (grid == null || !IsInstanceValid(grid)) continue;
            
            grid.pathIcon?.Hide();
            grid.attackRangeIcon?.Hide();
            
            if (grid.pathIcon != null)
                grid.pathIcon.Modulate = Colors.White;
            
            if (grid.attackRangeIcon != null)
            {
                grid.attackRangeIcon.Modulate = new Color(1, 0.2f, 0.2f, 0.9f);
                grid.attackRangeIcon.Hide(); // ✅ 确保隐藏
            }
                
            grid.OnClickGrid = null;
            grid.OnClickEmpty = null;
            grid.OnMouseEnteredAttackGrid = null;
            grid.OnMouseExitedAttackGrid = null;
            
            grid.IsInAttackRangeMode = false;
            grid.IsInWeaponRange = false;
        }
        
        attackRange.Clear();
        explosionRange.Clear(); // ✅ 清除爆炸范围缓存
        repairRange.Clear(); // ✅ 清除修复范围缓存
        flareRange.Clear(); // ✅ 清除照明弹射程范围
        illuminationRange.Clear(); // ✅ 清除照明覆盖范围
        
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm != null) 
        {
            gm.isSelectingAttackTarget = false;
        }
    }

    // ========== ✅ 核心修改：SetupMoveRangeCallbacks ==========
    // ✅ 公共方法：纯计算移动范围（不修改UI，供玩家和AI统一使用）
    public List<Grids> GetMoveRange(Infantry infantry, int maxMove)
    {
        // ✅ 开创人2026-11-15 区域天气:移动力增减(flat delta,保底0)——寻路深度上限
        var __wmM = GetTree()?.GetFirstNodeInGroup("weather_manager") as WeatherManager;
        if (__wmM != null && infantry?.grid != null)
            maxMove = Mathf.Max(0, maxMove + __wmM.GetMovePointsDelta(infantry.grid.GridIndex));
        var result = new List<Grids>();
        if (infantry == null || infantry.grid == null || map == null) return result;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var fog = gm?.fogOfWarManager;
        bool isFogEnabled = fog != null && fog.isFogOfWarEnabled;

        var open = new Queue<(Grids grid, int cost)>();
        var best = new Dictionary<Grids, int>();
        open.Enqueue((infantry.grid, 0));
        best[infantry.grid] = 0;

        while (open.Count > 0)
        {
            var (cur, accCost) = open.Dequeue();

            // ✅ 可见格子或迷雾关闭时：当前格子有敌方单位则阻挡后续路径扩展（仅NonOverlapping；可穿过敌方单位则豁免）
            if (cur != infantry.grid && infantry.overlapType == UnitOverlapType.NonOverlapping && !infantry.canPassThroughEnemyUnits)
            {
                bool curVisible = !isFogEnabled || fog.IsGridVisible(cur);
                if (curVisible)
                {
                    cur.infantries.RemoveAll(u => u == null || !IsInstanceValid(u));
                    var enemyOnCur = cur.infantries.FirstOrDefault(u => u != infantry && TeamHelper.IsEnemyTeam(infantry.team, u.team)
                        && Infantry.IsVisibleTo(u, infantry.team, grids));   // ✅ 隐身敌军不阻挡范围计算（否则范围出现空洞=暴露位置），实际移动时由截断逻辑触发遭遇
                    if (enemyOnCur != null)
                        continue;
                }
            }

            for (int dirIdx = 0; dirIdx < 4; dirIdx++)
            {
                var dir = offset[dirIdx];
                var nIdx = cur.GridIndex + dir;

                if (nIdx.X < 0 || nIdx.X >= searchRange.X ||
                    nIdx.Y < 0 || nIdx.Y >= searchRange.Y) continue;

                var neighbor = map[nIdx.X, nIdx.Y];
                if (neighbor == null) continue;

                // ✅ N屏隔离：移动范围不跨屏（跨屏只能走传送）
                if (neighbor.screenId != infantry.grid.screenId) continue;

                int moveCost = infantry.GetMoveCostOnGrid(neighbor);   // ✅ 与实际移动一致（含港口靠泊等设施覆盖）
                if (moveCost >= 999 || moveCost == int.MaxValue) continue;

                if (unitManager?.CanMoveTo(neighbor.GridIndex, infantry) == false)
                    continue;

                int newCost = accCost + moveCost;
                if (best.TryGetValue(neighbor, out int old) && newCost >= old) continue;
                if (newCost > maxMove) continue;

                if (neighbor.weapon != null && !neighbor.weapon.AllowsPassage(infantry))
                    continue;

                // ✅ issue#1：他方多格机动单位足印阻挡（Oozium 豁免）
                if (neighbor.multiTileOccupant != null && neighbor.multiTileOccupant != infantry
                    && IsInstanceValid(neighbor.multiTileOccupant) && !neighbor.multiTileOccupant.IsQueuedForDeletion())
                    continue;

                // ✅ 可见格子或迷雾关闭时：敌方单位所在格子不加入移动范围（仅NonOverlapping；可穿过敌方单位则敌格入范围但不可停留）
                if (infantry.overlapType == UnitOverlapType.NonOverlapping && !infantry.canPassThroughEnemyUnits)
                {
                    bool neighborVisible = !isFogEnabled || fog.IsGridVisible(neighbor);
                    if (neighborVisible)
                    {
                        neighbor.infantries.RemoveAll(u => u == null || !IsInstanceValid(u));
                        var enemyOnNeighbor = neighbor.infantries.FirstOrDefault(u =>
                            u != infantry
                            && TeamHelper.IsEnemyTeam(infantry.team, u.team)
                            && Infantry.IsVisibleTo(u, infantry.team, grids)); // ✅ 看不见的隐身敌军不剔除（防暴露位置）
                        if (enemyOnNeighbor != null)
                            continue;
                    }
                }

                best[neighbor] = newCost;
                open.Enqueue((neighbor, newCost));

                if (!result.Contains(neighbor))
                    result.Add(neighbor);
            }
        }
        return result;
    }

    // ✅ 新增：通用移动范围计算方法（替代所有 ShowXXXMoveRange）
    private void CalculateMoveRangeGeneric(Infantry infantry, int maxMove)
    {
        var range = GetMoveRange(infantry, maxMove);
        moveRange.Clear();
        moveRange.AddRange(range);
    }

private void SetupMoveRangeCallbacks(Infantry infantry)
{
    var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
    var fog = gm?.fogOfWarManager;
    bool isFogEnabled = fog != null && fog.isFogOfWarEnabled;

    // ✅ 本单位脚下格同样禁选（起点不在范围集内，循环盖不到它）
    OverrideUnitInput(infantry.grid, true);

    foreach (var g in moveRange)
    {
        g.pathIcon?.Show();

        // ✅ 禁用移动范围内所有单位的输入，防止误选（包括己方未行动单位）
        OverrideUnitInput(g, true);

        bool canStay = CanStayOnGrid(g, infantry);

        if (canStay)
        {
            var transport = Infantry.FindTransportFor(g.infantries, infantry);

            if (infantry.overlapType != UnitOverlapType.Overlapping 
                && transport != null 
                && transport.CanTransportUnit(infantry))
            {
                g.OnClickGrid = to => HandleNonOverlappingTransportMount(infantry, transport, to);
            }
            else if (infantry.overlapType == UnitOverlapType.Overlapping)
            {
                var otherUnits = g.infantries.Where(u => u != infantry && IsInstanceValid(u) && !u.isMoved).ToList();
                if (otherUnits.Count > 0)
                {
                    var targetUnit = otherUnits[0];
                    g.OnClickGrid = to => ShowOverlappingMoveChoiceDialog(infantry, targetUnit, to, transport);
                }
                else
                {
                    g.OnClickGrid = to => HandleGridClick(infantry, to);
                }
            }
            else
            {
                g.OnClickGrid = to => HandleGridClick(infantry, to);
            }
        }
        else
        {
            // ⬅️ 截断移动：不可停留但可达的格子
            if (infantry.overlapType == UnitOverlapType.NonOverlapping)
            {
                // ✅ 只有可见格子才显示红色（不可见格子不暴露敌方单位位置）
                bool gridVisible = !isFogEnabled || fog.IsGridVisible(g);
                if (HasOnlyHiddenStealthBlockers(g, infantry)) gridVisible = false;   // ✅ 只被看不见的隐身敌军占据：不标红（防暴露）
                if (gridVisible)
                {
                    g.pathIcon.Modulate = new Color(1, 0.3f, 0.3f, 0.6f);
                }
                g.OnClickGrid = to => HandleTruncatedMove(infantry, to);
            }
            else
            {
                g.OnClickGrid = to => HandleGridClick(infantry, to);
            }
        }

        g.OnClickEmpty = null;

        // ✅ 悬停轨迹预览（鼠标划过/手指划过显示移动轨迹）
        g.OnHoverGrid = OnMouseEnteredGrid;
        g.OnUnhoverGrid = OnMouseExitedGrid;
    }

    // ✅ 空地点击回退（包括范围外的单位所在格子）
    rangeOutsideGrids.Clear();
    foreach (var grid in grids)
    {
        if (!moveRange.Contains(grid))
        {
            // ✅ 禁用范围外单位的选择，统一由格子回调处理（CloseRange 时恢复）
            OverrideUnitInput(grid, true);
            var g0 = grid;
            Action onEmpty = () => OnOutsideRangeClicked(infantry, g0);
            Action<Grids> onGrid = _ => OnOutsideRangeClicked(infantry, g0);
            grid.OnClickEmpty = onEmpty;
            grid.OnClickGrid = onGrid;
            rangeOutsideGrids[grid] = onGrid;
        }
    }
}

    // 范围外格子：记录本次挂上的回调，CloseRange 时只撤销仍是自己挂的（不误删攻击范围等后续回调）
    private readonly Dictionary<Grids, Action<Grids>> rangeOutsideGrids = new();

    /// <summary>
    /// 移动范围外点击：
    /// ① 已移动未确认 → 回退；
    /// ② 点本单位脚下格 → 原地打开行动菜单（快速移动下的「不动直接行动」）；
    /// ③ 点范围外的己方可行动单位 → 直接切换选中；
    /// ④ 其余 → 取消选择。
    /// </summary>
    private void OnOutsideRangeClicked(Infantry owner, Grids grid)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
        bool shouldRollback = gm?.selectedInfantry != null &&
                              gm.selectedInfantry.state == UnitState.Moved &&
                              gm.selectedInfantry.originalGrid != null;
        if (shouldRollback) { gm.RollbackMove(); return; }

        CloseRange();
        HideAttackRange();
        ClearWeaponRange();

        if (owner != null && IsInstanceValid(owner) && grid == owner.grid && !owner.isMoved && gm?.selectedInfantry == owner)
        {
            actionMenu?.ShowMenu(owner);
            return;
        }

        actionMenu?.Hide();
        gm?.ClearSelectedInfantry();

        if (gm != null && grid?.infantries != null)
        {
            var next = grid.infantries
                .Where(u => u != null && IsInstanceValid(u) && u.IsVisibleInTree() && gm.IsTurnPhaseValid(u.team)
                            && !u.isMoved && u.state != UnitState.Acted)
                .OrderByDescending(u => u.health)
                .FirstOrDefault();
            if (next != null) gm.OnSelectPiece(next);
        }
    }

    private void ShowOverlappingMoveChoiceDialog(Infantry movingUnit, Infantry targetUnit, Grids targetGrid, Infantry transport)
    {
        // 先关闭移动范围（防止重复点击）
        CloseRange();

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;

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
        panel.CustomMinimumSize = new Godot.Vector2(320, 280);
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
        title.Position = new Godot.Vector2(0, 15);
        title.Size = new Godot.Vector2(320, 30);
        panel.AddChild(title);

        // 按钮容器
        var vbox = new VBoxContainer();
        vbox.Position = new Godot.Vector2(60, 60);
        vbox.Size = new Godot.Vector2(200, 200);
        vbox.AddThemeConstantOverride("separation", 12);
        panel.AddChild(vbox);

        // 1. 取消按钮
        var cancelBtn = new Button();
        TranslateGroup.SetText(cancelBtn, "❌ 取消");
        cancelBtn.CustomMinimumSize = new Godot.Vector2(200, 40);
        cancelBtn.Pressed += () => {
            dialog.QueueFree();
            // 恢复选中移动单位，但不显示范围（用户需要重新点击）
            gm?.ClearSelectedInfantry();
        };
        vbox.AddChild(cancelBtn);

        // 2. 确认移动按钮
        var confirmBtn = new Button();
        TranslateGroup.SetText(confirmBtn, "✅ 确认移动");
        confirmBtn.CustomMinimumSize = new Godot.Vector2(200, 40);
        confirmBtn.Pressed += () => {
            dialog.QueueFree();
            // 执行移动
            MoveTo(movingUnit, targetGrid);
        };
        vbox.AddChild(confirmBtn);

        // 3. 切换单位按钮
        var switchBtn = new Button();
        switchBtn.Text = $"🔄 切换到{targetUnit.Name}";
        switchBtn.CustomMinimumSize = new Godot.Vector2(200, 40);
        switchBtn.Pressed += () => {
            dialog.QueueFree();
            // 关闭当前范围，切换到新单位
            HideAttackRange();
            ClearWeaponRange();
            actionMenu?.Hide();
            gm?.ClearSelectedInfantry();
            gm?.OnSelectPiece(targetUnit);
        };
        vbox.AddChild(switchBtn);

        // 4. 搭载按钮（仅当APC可用时）
        if (transport != null && transport.CanTransportUnit(movingUnit))
        {
            var mountBtn = new Button();
            mountBtn.Text = $"🚛 搭载到{transport.Name}";
            mountBtn.CustomMinimumSize = new Godot.Vector2(200, 40);
            mountBtn.Pressed += () => {
                dialog.QueueFree();
                // 显示APC装载菜单（不是直接搭载）
                var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                if (actionMenu != null)
                {
                    actionMenu.Hide();
                    actionMenu.ShowTransportLoadMenu(transport, movingUnit);
                }
            };
            vbox.AddChild(mountBtn);
        }

        GetTree().CurrentScene.AddChild(dialog);

    }

    // ✅ 究极自由：非Overlapping单位显示通用装载菜单
    private void HandleNonOverlappingTransportMount(Infantry infantry, Infantry transport, Grids to)
    {
        var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
        if (actionMenu != null)
        {
            actionMenu.Hide();
            // ✅ 究极自由：使用通用运输菜单
            actionMenu.ShowTransportLoadMenu(transport,infantry);
        }
        CloseRange();

    }

public void CloseRange()
{
    Grids.IsForceActionMode = false;
    if (moveRange == null) return;
    foreach (var grid in moveRange)
    {
        if (grid != null && IsInstanceValid(grid))
        {
            grid.pathIcon?.Hide();
            grid.pathIcon?.SetDeferred("modulate", Colors.White);
            grid.OnClickGrid = null;
            grid.OnClickEmpty = null;
            grid.OnHoverGrid = null;
            grid.OnUnhoverGrid = null;
            
            // ✅ 恢复单位输入
            OverrideUnitInput(grid, false);
        }
    }
    // ✅ 恢复本单位脚下格的输入（显示范围时单独禁用过）
    if (selectedUnit != null && IsInstanceValid(selectedUnit) && selectedUnit.grid != null)
        OverrideUnitInput(selectedUnit.grid, false);

    moveRange.Clear();
    // ✅ 恢复范围外格子：单位重新可点、撤销本次挂的范围外回调
    foreach (var kv in rangeOutsideGrids)
    {
        var og = kv.Key;
        if (og == null || !IsInstanceValid(og)) continue;
        OverrideUnitInput(og, false);
        if (og.OnClickGrid == kv.Value) og.OnClickGrid = null;
    }
    rangeOutsideGrids.Clear();
    HideExplosionRange(); // ✅ 关闭移动范围时也清除爆炸预览

    // ✅ 清理轨迹预览与触摸状态，恢复格子拾取
    previewPath.Clear();
    pathLine?.ClearPoints();
    ClearMoveArrows();   // ✅ CoW 方向箭头同步清除
    touchPathActive = false;
    touchHoverGrid = null;
    if (isMobilePlatform)
    {
        foreach (var g in grids)
            g?.SetAreaPickable(true);
    }
}

    public void ClearAllEmptyCallbacks()
    {
        foreach (var grid in grids)
            grid.OnClickEmpty = null;
    }

    public void SetupEmptyClickForRollback(Infantry infantry)
    {
        foreach (var grid in grids)
        {
            grid.OnClickEmpty = () => 
            {
                var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
                var unit = gm?.selectedInfantry;
                
                bool shouldRollback = unit != null && 
                                      unit.state == UnitState.Moved && 
                                      unit.originalGrid != null;

                if (shouldRollback)
                {
                    gm.RollbackMove();
                }
                else
                {
                    CloseRange();
                    HideAttackRange();
                    ClearWeaponRange();
                    var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                    actionMenu?.CancelExplosionPreview(); // ✅ 取消爆炸预览
                    actionMenu?.Hide();
                    gm?.ClearSelectedInfantry();
                }
            };
        }
    }

    public void SetEmptyClickToCloseMenu(Infantry unit)
    {
        foreach (var grid in grids)
            grid.OnClickEmpty = null;

        foreach (var grid in grids)
        {
            grid.OnClickEmpty = () => 
            {
                var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                actionMenu?.Hide();
                var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
                gm?.ClearSelectedInfantry();
                foreach (var g in grids)
                    g.OnClickEmpty = null;
            };
        }
    }
}
