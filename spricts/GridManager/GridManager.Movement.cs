// GridManager.cs - 修改 HandleGridClick 和 SetupMoveRangeCallbacks 方法
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

// GridManager · Movement（由 GridManager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class GridManager
{

    // ========== ✅ 核心修改：HandleGridClick ==========
    public void HandleGridClick(Infantry infantry, Grids to)
    {
        // ✅ 编辑模式下禁止单位移动操作
        var terrainEditor = GetTree().GetFirstNodeInGroup("terrain_editor") as TerrainEditor;
        if (terrainEditor != null && terrainEditor.ShouldBlockUnitOperations())
        {
            return;
        }

        // ✅ 行走动画播放期间锁定点击（CoW 手感：走完再操作，防连点错位）
        if (IsWalkAnimPlaying)
        {
            return;
        }

        // ✅ 究极自由：检查任何可搭载单位
        var transport = to.infantries.FirstOrDefault(u => 
            u.canTransportUnits 
            && u.maxTransportCapacity > 0 
            && u.team == infantry.team 
            && u != infantry) as Infantry;
        if (transport != null && transport.CanTransportUnit(infantry))
        {
            var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
            actionMenu?.ShowTransportMenu(transport, afterMove: false);
            CloseRange();
            return;
        }

        var shelter = Weapon.FindShelterFor(to.weapons, infantry);
        if (shelter != null)
        {
            MoveTo(infantry, to);
            return;
        }

        var otherUnits = to.infantries.Where(i => i != infantry && IsInstanceValid(i)).ToList();
        bool hasOtherUnits = otherUnits.Count > 0 || (to.infantry != null && to.infantry != infantry);

        if (hasOtherUnits)
        {
            switch (infantry.overlapType)
            {
                case UnitOverlapType.NonOverlapping:
                var friendlyTransport = Infantry.FindTransportFor(otherUnits, infantry);
                if (friendlyTransport != null)
                {
                    var menu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                    menu?.ShowTransportLoadMenu(friendlyTransport, infantry);
                    CloseRange();
                    return;
                }
                // ✅ 合流检测：同类型友方残血单位 → 自动合流
                var mergeTarget = otherUnits
                    .Where(u => u.team == infantry.team && u.UnitTypeId == infantry.UnitTypeId)
                    .OrderBy(u => u.health)
                    .FirstOrDefault();
                if (mergeTarget != null)
                {
                    var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
                    if (gm != null && !(infantry.isStealthed && infantry.stealthOverlapMode == 1) && gm.CanMerge(infantry, mergeTarget))
                    {
                        MoveTo(infantry, to);
                        return;
                    }
                }
                // ⬅️ 改为截断移动而非直接取消
                HandleTruncatedMove(infantry, to);
                return;

                case UnitOverlapType.Overlapping:
                    MoveTo(infantry, to);
                    return;

                case UnitOverlapType.Oozium:
                    // ✅ 选Oozium=自动覆盖+吞噬（Overlapping值才是"只重叠不吞噬"）
                    foreach (var target in otherUnits)
                    {
                        if (!infantry.canDevourAllies && target.team == infantry.team) continue;
                        DevourUnit(target, infantry);
                    }
                    to.infantries.Clear();
                    to.infantry = null;
                    
                    MoveTo(infantry, to);
                    ForceOoziumActed(infantry);
                    return;
            }
        }
        else
        {
            // ✅ NonOverlapping单位检查路径上的不可移动格子，有则截断待机
            if (infantry.overlapType == UnitOverlapType.NonOverlapping)
            {
                var path = BuildPath(infantry.grid, to, infantry);
                Grids lastValidGrid = infantry.grid;
                
                for (int i = 1; i < path.Count; i++)
                {
                    var g = path[i];
                    
                    // 检查1: 敌方单位阻挡
                    g.infantries.RemoveAll(u => u == null || !IsInstanceValid(u));
                    var enemies = g.infantries.Where(u => Infantry.IsTruncationBlocker(infantry, u)).ToList();
                    if (enemies.Count > 0)
                    {
                        // 截断到最后一个可停留的格子
                        TruncateAndMove(infantry, lastValidGrid);
                        return;
                    }
                    
                    // 检查2: 兵器阻挡（非Oozium单位；宇宙战舰/空天战机可飞越；导弹井可站上发射）
                    if (g.weapon != null && !g.weapon.AllowsPassage(infantry) && !g.weapon.CanShelterUnit(infantry))
                    {
                        TruncateAndMove(infantry, lastValidGrid);
                        return;
                    }
                    
                    // 检查3: 地形不可移动
                    int moveCost = infantry.GetMoveCostOnGrid(g);
                    if (moveCost >= 999 || moveCost == int.MaxValue)
                    {
                        TruncateAndMove(infantry, lastValidGrid);
                        return;
                    }
                    
                    // 检查4: CanMoveTo判定
                    if (unitManager?.CanMoveTo(g.GridIndex, infantry) == false)
                    {
                        TruncateAndMove(infantry, lastValidGrid);
                        return;
                    }
                    
                    // 禁停格可以经过，但不能成为遇阻后的回退落点。
                    if (CanStayOnGrid(g, infantry)) lastValidGrid = g;
                }
            }
            
            MoveTo(infantry, to);
        }
    }

    private bool TryPushUnitsAlongMovementPath(Infantry mover, Grids destination)
    {
        if (mover == null || destination == null || !mover.CanUseMovementPush())
            return false;
        var path = BuildPath(mover.grid, destination, mover);
        if (path == null || path.Count < 2) return false;

        bool pushedAny = false;
        for (int i = 1; i < path.Count; i++)
        {
            var tile = path[i];
            var blockers = tile?.infantries?.Where(u => u != null && IsInstanceValid(u)
                && u.health > 0 && u.grid == tile && u != mover).ToList()
                ?? new List<Infantry>();
            Vector2I direction = tile.GridIndex - path[i - 1].GridIndex;
            if (blockers.Count > 0)
            {
                if (!mover.TryPushTargetOnMovement(tile, direction))
                    return pushedAny;
                pushedAny = true;
                bool stillOccupied = tile.infantries?.Any(u => u != null && IsInstanceValid(u)
                    && u.health > 0 && u.grid == tile && u != mover) ?? false;
                if (stillOccupied) return pushedAny;
            }

            // Static blockers use the same movement-trigger configuration and
            // displacement vector. Each object is moved only if its own push Export
            // and the pusher's target-class/team/weight policies permit it.
            var blockingWeapon = tile.weapons?.FirstOrDefault(w => w != null && IsInstanceValid(w)
                && !w.AllowsPassage(mover) && !w.CanShelterUnit(mover));
            if (blockingWeapon != null)
            {
                if (!mover.TryPushWeaponOnMovement(blockingWeapon, direction))
                    return pushedAny;
                pushedAny = true;
            }

            if (tile.city != null && IsInstanceValid(tile.city) && !tile.city.allowAllPass
                && !CanMoveToGrid(tile, mover))
            {
                if (!mover.TryPushFacilityOnMovement(tile.city, direction))
                    return pushedAny;
                pushedAny = true;
            }
        }
        return pushedAny;
    }

    private void ForceOoziumActed(Infantry unit)
    {
        if (unit == null) return;
        
        unit.isMoved = true;
        unit.isAttacked = true;
        unit.state = UnitState.Acted;
        unit.originalGrid = null;
        
        CloseRange();
        HideAttackRange();
        
        var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
        actionMenu?.Hide();
        
        ClearAllEmptyCallbacks();
        
        // ✅ 通用：任何有 animSprite 的单位都变暗
        if (unit.animSprite != null && IsInstanceValid(unit.animSprite))
        {
            unit.animSprite.Modulate = new Color(0.5f, 0.5f, 0.5f, 1.0f);
            unit.StopBreath();
        }
        
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.ClearSelectedInfantry();
    }

    private bool CanMoveToGrid(Grids grid, Infantry requestingUnit, bool checkPassThrough = false)
    {
        // ✅ 开创人2026-11-11 通行控制三件套：阻塞权完全下放兵器自身。
        // ②全部通行开=零阻挡（①③失效）；关=按①队伍范围阻挡，③白名单豁免，留空全档。
        if (grid.weapons != null)
        {
            foreach (var wBlk in grid.weapons)
            {
                if (wBlk == null || wBlk.allowAllPass) continue;
                bool teamHit = TeamHelper.MatchesTeamRelation(wBlk.team, requestingUnit.team,
                    wBlk.movementBlockIncludeFriendly, wBlk.movementBlockIncludeEnemy, wBlk.movementBlockIncludeAllied);
                if (!teamHit) continue;
                if (wBlk.movementBlockWhitelist != null && wBlk.movementBlockWhitelist.Length > 0
                    && (wBlk.movementBlockWhitelist.Contains(requestingUnit.UnitTypeId)
                        || wBlk.movementBlockWhitelist.Contains(requestingUnit.GetType().Name)))
                    continue;   // ③白名单豁免
                return false;
            }
        }
        // ✅ 设施通行控制三件套（照兵器同构）：设施自己决定本格能否进入；默认 allowAllPass=true 零影响。
        if (grid.city != null && IsInstanceValid(grid.city) && !grid.city.allowAllPass)
        {
            var fBlk = grid.city;
            bool fTeamHit = TeamHelper.MatchesTeamRelation(fBlk.facilityTeam, requestingUnit.team,
                fBlk.movementBlockIncludeFriendly, fBlk.movementBlockIncludeEnemy, fBlk.movementBlockIncludeAllied);
            if (fTeamHit)
            {
                if (fBlk.movementBlockWhitelist == null || fBlk.movementBlockWhitelist.Length == 0
                    || !(fBlk.movementBlockWhitelist.Contains(requestingUnit.UnitTypeId)
                        || fBlk.movementBlockWhitelist.Contains(requestingUnit.GetType().Name)))
                    return false;
            }
        }

        // ✅ 宇宙战舰/空天战机：可飞越兵器（不 blocked）
            
        var otherUnits = grid.infantries.Where(i => i != requestingUnit && IsInstanceValid(i)).ToList();
        if (otherUnits.Count == 0) return true;

        switch (requestingUnit.overlapType)
        {
            case UnitOverlapType.NonOverlapping:
                bool hasEnemy = otherUnits.Any(u => TeamHelper.IsEnemyTeam(requestingUnit.team, u.team));
                bool hasAlly = otherUnits.Any(u => TeamHelper.IsFriendlyTeam(requestingUnit.team, u.team) || TeamHelper.IsAlliedTeam(requestingUnit.team, u.team));
                
                if (hasEnemy)
                {
                    return false;
                }
                else if (hasAlly)
                {
                    var allyTransport = Infantry.FindTransportFor(otherUnits, requestingUnit);
                    if (allyTransport != null)
                    {
                    return true;
                    }

                    // ✅ P-1 公共运输单位可以搭载任何势力
                    var p1Transport = Infantry.FindTransportFor(otherUnits, requestingUnit);
                    if (p1Transport != null)
                    {
                        return true;
                    }

                    return checkPassThrough;
                }
                return false;
                
            case UnitOverlapType.Overlapping:
            case UnitOverlapType.Oozium: 
                return true;
                
            default: 
                return false;
        }
    }

    // ✅ 该格是否"只"被移动方看不见的隐身敌军挡住（用于显示层：不标红、不留空洞）
    public bool HasOnlyHiddenStealthBlockers(Grids grid, Infantry mover)
    {
        if (grid == null || mover == null) return false;
        var others = grid.infantries.Where(u => u != null && IsInstanceValid(u) && u != mover).ToList();
        if (others.Count == 0) return false;
        return others.All(u => TeamHelper.IsEnemyTeam(mover.team, u.team) && !Infantry.IsVisibleTo(u, mover.team, grids));
    }

    private bool CanStayOnGrid(Grids grid, Infantry requestingUnit)
    {
        if (grid == null || requestingUnit == null) return false;
        // 地形禁停优先于搭载/合流/重叠：可以穿过，但不能把这里当最终落点。
        if (grid.PreventsUnitStop(this)) return false;

        if (Weapon.FindShelterFor(grid.weapons, requestingUnit) != null) return true;

        var otherUnits = grid.infantries.Where(i => i != requestingUnit && IsInstanceValid(i)).ToList();
        if (otherUnits.Count == 0) return true;

        var friendlyTransport = Infantry.FindTransportFor(otherUnits, requestingUnit);
        if (friendlyTransport != null)
        {
        return true;
        }


        // ✅ P-1 公共运输单位可以搭载任何势力的单位
        var p1Transport = Infantry.FindTransportFor(otherUnits, requestingUnit);
        if (p1Transport != null) return true;

        // ✅ 合流检测：同类型友方残血单位可以合流
        var mergeTarget = otherUnits.FirstOrDefault(u => u.team == requestingUnit.team && u.UnitTypeId == requestingUnit.UnitTypeId);
        if (mergeTarget != null)
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm != null && !(requestingUnit.isStealthed && requestingUnit.stealthOverlapMode == 1) && gm.CanMerge(requestingUnit, mergeTarget))
                return true;
        }

        switch (requestingUnit.overlapType)
        {
            case UnitOverlapType.NonOverlapping:
                return false;
            case UnitOverlapType.Overlapping:
            case UnitOverlapType.Oozium:
                return true;
            default:
                return false;
        }
    }

    public void MoveTo(Infantry infantry, Grids to, bool showMenu = true, System.Collections.Generic.List<Grids> forcedPath = null)
    {
        if (infantry == null || to == null) return;
        // 最终安全门：任何调用路径都不能把单位停进禁停地形；经过禁停格不受影响。
        if (to.PreventsUnitStop(this)) return;

        // Resolve movement-triggered pushes before overlap checks so a successfully
        // displaced blocker can vacate the destination for both human and AI moves.
        TryPushUnitsAlongMovementPath(infantry, to);

        // ✅ forcedPath：手画路径模式——路程/绕圈以画线为准，不再重算最短路径

        if (infantry.overlapType == UnitOverlapType.NonOverlapping)
        {
            var otherUnits = to.infantries.Where(i => i != infantry && IsInstanceValid(i)).ToList();
            if (otherUnits.Count > 0)
            {
                // ✅ 允许合流：同类型友方残血单位
                var mergeTarget = otherUnits.FirstOrDefault(u => u.team == infantry.team && u.UnitTypeId == infantry.UnitTypeId);
                var gmer = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
                bool canMerge = mergeTarget != null && gmer != null && !(infantry.isStealthed && infantry.stealthOverlapMode == 1) && gmer.CanMerge(infantry, mergeTarget);
                // ✅ 允许搭载：友方运输单位
                var transported = Infantry.FindTransportFor(otherUnits, infantry);
                // ✅ P-1 公共运输单位可以搭载任何势力
                if (transported == null)
                    transported = Infantry.FindTransportFor(otherUnits, infantry);
                if (transported == null && !canMerge)
                    return;
            }
        }

        if (infantry.overlapType == UnitOverlapType.Oozium)   // ✅ 选Oozium覆盖方式即自动覆盖+吞噬   // ✅ canDevour：可进入占用格之外，是否吞噬占用者
        {
            var targetsToRemove = to.infantries.Where(i => i != infantry && IsInstanceValid(i) && (infantry.canDevourAllies || TeamHelper.IsEnemyTeam(infantry.team, i.team))).ToList();
            foreach (var target in targetsToRemove)
                DevourUnit(target, infantry);
            to.infantries.Clear();
            to.infantry = null;
        }

        var path = forcedPath ?? BuildPath(infantry.grid, to, infantry);
        if (path == null || path.Count == 0) return;
        if (forcedPath != null && path[path.Count - 1] != to) return;   // 手画路径必须终于目标格

        int totalMoveCost = 0;
        int totalFuelCost = 0;
        bool isFirst = true;
        foreach (var g in path)
        {
            if (isFirst) { isFirst = false; continue; }
            int moveCost = GetActualMoveCost(infantry, g);
            totalMoveCost += moveCost;
            // ✅ 传送点(TP)不消耗燃料，其他地形正常消耗
            if (infantry.consumeFuel && g.gridType != GridType.TP)
                totalFuelCost += moveCost;
        }

        // ✅ CO 模块系统·模块5：移动力加成同步到实际移动判定（与 ShowMoveRange 显示一致）
        if (infantry.movePoints + CoModuleSystem.GetMovementBonus(infantry) < totalMoveCost) return;

        if (infantry.consumeFuel && infantry.fuel < totalFuelCost) return;

        if (infantry.originalGrid == null)
            infantry.originalGrid = infantry.grid;

        infantry.movePoints -= totalMoveCost;
        if (infantry.consumeFuel)
            infantry.ConsumeFuel(totalFuelCost);

        var fromGrid = infantry.grid;
        if (fromGrid != null)
        {
            fromGrid.infantries.Remove(infantry);
            if (fromGrid.infantry == infantry)
                fromGrid.infantry = fromGrid.infantries.Count > 0 ? fromGrid.infantries[0] : null;
        }

        infantry.grid = to;
        if (!to.infantries.Contains(infantry))
            to.infantries.Add(infantry);
        if (to.infantry == null || infantry.overlapType != UnitOverlapType.Overlapping)
            to.infantry = infantry;

        infantry.state = UnitState.Moved;
        infantry.isMoved = true;
        infantry.movedThisTurnForAutoRepair = true;   // ✅ autoRepairAfterMove：行动结束时触发
        infantry.OnMovedInterruptCheck();             // ✅ 生产/建造：移动中断判定（关=移动即打断进度）

        // ✅ 移动完成后立即刷新战争迷雾
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.fogOfWarManager?.OnUnitMoved();
        infantry.PerformScentScan();

        CloseRange();
        HideAttackRange();

        // ✅ 移动后结算（运输/合流/行动菜单）：行走动画播完才执行（CoW 手感）
        void PostMoveActions()
        {
       var transport = to.infantries.FirstOrDefault(u =>
        u.canTransportUnits
        && u.maxTransportCapacity > 0
        && u.team == infantry.team
        && u != infantry) as Infantry;
    // ✅ P-1 公共运输单位可以搭载任何势力
    if (transport == null)
    {
        transport = to.infantries.FirstOrDefault(u =>
            u.canTransportUnits
            && u.maxTransportCapacity > 0
            && u.team == TeamHelper.Player
            && u != infantry) as Infantry;
    }
    bool hasMountedToTransport = false;

    var shelter = Weapon.FindShelterFor(to.weapons, infantry);
    if (shelter != null && shelter.LoadShelterUnit(infantry)) hasMountedToTransport = true;

    if (!hasMountedToTransport && transport != null && transport.CanTransportUnit(infantry))
    {
        var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
        if (actionMenu != null)
        {

            actionMenu.ShowTransportMenu(transport, afterMove: true);
            hasMountedToTransport = true;
        }
    }

    // ✅ 合流检测：移动到同类型友方残血单位时自动合流
    bool hasMerged = false;
    if (!hasMountedToTransport)
    {
        var mergeTarget = to.infantries
            .Where(u => u != infantry && IsInstanceValid(u) && u.team == infantry.team && u.UnitTypeId == infantry.UnitTypeId)
            .OrderBy(u => u.health)
            .FirstOrDefault() as Infantry;
        if (mergeTarget != null && gm != null && !(infantry.isStealthed && infantry.stealthOverlapMode == 1) && gm.CanMerge(infantry, mergeTarget))
        {
            gm.MergeUnits(infantry, mergeTarget);
            hasMerged = true;
        }
    }

    if (!hasMountedToTransport && !hasMerged && showMenu)
    {
        SetupEmptyClickForRollback(infantry);
        var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
        actionMenu?.ShowMenu(infantry, true, true);
    }
    else if (!hasMountedToTransport && !hasMerged)
    {
        // ⬅️ 不弹菜单，直接待机
        infantry.isAttacked = true;
        infantry.state = UnitState.Acted;
        infantry.SetWaitVisual(true);
        infantry.originalGrid = null;
        gm?.ClearSelectedInfantry();
    }
        }

        // ✅ CoW 式行走动画：沿路径逐格走完再结算（enableWalkAnim 关闭=旧瞬移行为）
        if (enableWalkAnim && path.Count > 1)
        {
            infantry.Position = path[0].Position;
            StartWalkAlongPath(infantry, path, PostMoveActions);
        }
        else
        {
            infantry.Position = to.Position;
            PostMoveActions();
        }
    }

    // ========== ✅ 新增：移动截断辅助方法 ==========
    private void TruncateAndMove(Infantry infantry, Grids lastValidGrid)
    {
        if (lastValidGrid != infantry.grid && CanStayOnGrid(lastValidGrid, infantry))
        {
            MoveTo(infantry, lastValidGrid, showMenu: false);
        }
        else
        {
            CloseRange();
            var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
            actionMenu?.Hide();
            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.ClearSelectedInfantry();
        }
    }

    // ========== ✅ 新增：移动截断（正序路径检查，遇到不可移动格子截断后待机） ==========
    public void HandleTruncatedMove(Infantry infantry, Grids target)
    {        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        var path = BuildPath(infantry.grid, target, infantry);
        if (path.Count <= 1)
        {
            CloseRange();
            var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
            actionMenu?.Hide();
            gm?.ClearSelectedInfantry();
            return;
        }



        // 从起点正序检查路径，找最后一个可停留的格子
        // ✅ 友军格：可穿过不可停留（继续往前找）；只有敌方/兵器/地形/规则阻挡才截断
        Grids truncatedTarget = infantry.grid;
        bool hardBlocked = false;
        for (int i = 1; i < path.Count; i++)
        {
            var g = path[i];
            g.infantries.RemoveAll(u => u == null || !IsInstanceValid(u));

            // ✅ 检查1: 敌方单位（NonOverlapping单位才被阻挡）
            if (infantry.overlapType == UnitOverlapType.NonOverlapping
                && g.infantries.Any(u => Infantry.IsTruncationBlocker(infantry, u)))
            { hardBlocked = true; break; }

            // ✅ 检查2: 兵器阻挡（非Oozium；宇宙战舰/空天战机可飞越；导弹井可站上发射）
            if (g.weapon != null && !g.weapon.AllowsPassage(infantry) && !g.weapon.CanShelterUnit(infantry)) { hardBlocked = true; break; }

            // ✅ 检查3: 地形不可移动
            int moveCost = infantry.GetMoveCostOnGrid(g);
            if (moveCost >= 999 || moveCost == int.MaxValue) { hardBlocked = true; break; }

            // ✅ 检查4: CanMoveTo判定
            if (unitManager?.CanMoveTo(g.GridIndex, infantry) == false) { hardBlocked = true; break; }

            if (CanStayOnGrid(g, infantry))
                truncatedTarget = g;
            // 否则=友军占用：穿过，不更新落点
        }

        // ✅ 没有任何真实阻挡、只是终点被友军占着 → 这是非法落点，拒绝移动（保持选择），而不是静默截断
        if (!hardBlocked && truncatedTarget != target)
            return;

        if (truncatedTarget != infantry.grid)
        {
            MoveTo(infantry, truncatedTarget, showMenu: false);
        }
        else
        {
            CloseRange();
            var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
            actionMenu?.Hide();
            gm?.ClearSelectedInfantry();
        }
    }

    private int GetActualMoveCost(Infantry infantry, GridType gridType)
    {
        int cost = infantry.GetMoveCost(gridType);
        // ✅ 如果返回 int.MaxValue，表示不可通行，但在路径计算中应该已经被过滤了
        // 这里作为安全检查，返回一个极大值
        return cost >= 999 ? 999 : cost;
    }

    private int GetActualMoveCost(Infantry infantry, Grids g)
    {
        int cost = infantry.GetMoveCostOnGrid(g);
        // ✅ CO 模块系统·模块5（移动模块：移动消耗减少，下限1）+ 天气系统（模块15：雪+1，道路/桥/建筑格/空军除外）
        if (cost < 999)
            cost = Mathf.Max(1, cost - CoModuleSystem.GetMovementCostReduction(infantry, g)
                                  + WeatherSystem.GetMovementCostModifier(infantry, g));
        return cost >= 999 ? 999 : cost;
    }

    private int GetActualMoveCostOld(Infantry infantry, GridType gridType)
    {
        int cost = infantry.GetMoveCost(gridType);
        // ✅ 如果返回 int.MaxValue，表示不可通行，但在路径计算中应该已经被过滤了
        // 这里作为安全检查，返回一个极大值
        return cost >= 999 ? 999 : cost;
    }

    private void DevourUnit(Infantry target, Infantry devourer = null)
    {
        if (target == null || !IsInstanceValid(target)) return;
        // ✅ 吞噬回血/吞噬消耗（DevourModules）：在移除前结算，被吞物数据仍有效
        devourer?.ApplyDevourEffects(target);
        if (target.grid != null)
        {
            target.grid.infantries.Remove(target);
            if (target.grid.infantry == target)
                target.grid.infantry = null;
            target.grid = null;
        }
        var gameManager = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gameManager?.RemovePiece(target);
        target.QueueFree();
    }
}
