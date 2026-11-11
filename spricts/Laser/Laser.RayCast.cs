// Laser.cs - 高级激光炮系统（修复版）
// 修复内容：
// 1. 多次攻击同步 remainingAttacks 和 attacksRemainingInCycle
// 2. 添加局内角度配置API（EnableAngle/DisableAngle/SetFireAngles等）
// 3. ✅ 移除 attackedThisRound 限制：允许同一回合多次攻击同一目标
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// Laser · RayCast（由 Laser.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Laser
{

    // ========== 射线计算 ==========

    public List<RayGridInfo> CalculateRayGrids(float angleDegrees)
    {
        if (rayCache.ContainsKey(angleDegrees))
            return new List<RayGridInfo>(rayCache[angleDegrees]);

        var result = new List<RayGridInfo>();
        if (grid == null) return result;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null) return result;

        float angleRad = Mathf.DegToRad(angleDegrees);
        Vector2 dir = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
        float cellSize = gm.gridManager.gridSize.X;
        Vector2 startPos = grid.Position + new Vector2(cellSize * 0.5f, cellSize * 0.5f);
        int mapWidth = gm.gridManager.searchRange.X;
        int mapHeight = gm.gridManager.searchRange.Y;
        int maxSteps = useInfiniteRange ? 1000 : maxLaserLength;

        Vector2 currentPos = startPos;
        int currentX = grid.GridIndex.X;
        int currentY = grid.GridIndex.Y;
        int stepX = dir.X > 0 ? 1 : (dir.X < 0 ? -1 : 0);
        int stepY = dir.Y > 0 ? 1 : (dir.Y < 0 ? -1 : 0);

        HashSet<Grids> visited = new HashSet<Grids>();
        float totalDist = 0f;

        for (int step = 0; step < maxSteps * 2; step++)
        {
            float x0 = gm.gridManager.startPos.X + currentX * cellSize;
            float x1 = x0 + cellSize;
            float y0 = gm.gridManager.startPos.Y + currentY * cellSize;
            float y1 = y0 + cellSize;

            float tX = float.MaxValue, tY = float.MaxValue;
            if (stepX > 0) tX = (x1 - currentPos.X) / dir.X;
            else if (stepX < 0) tX = (x0 - currentPos.X) / dir.X;
            if (stepY > 0) tY = (y1 - currentPos.Y) / dir.Y;
            else if (stepY < 0) tY = (y0 - currentPos.Y) / dir.Y;

            float tStep = Mathf.Min(tX, tY);
            if (tStep < 0.0001f) tStep = 0.0001f;

            var targetGrid = gm.gridManager.map[currentX, currentY];
            if (targetGrid != null && !visited.Contains(targetGrid))
            {
                float segmentLength = tStep * dir.Length();
                if (segmentLength > cellSize * 0.001f)
                {
                    visited.Add(targetGrid);
                    float diagonal = cellSize * Mathf.Sqrt(2);
                    float contactRatio = Mathf.Clamp(segmentLength / diagonal, 0f, 1f);
                    bool isBlocked = targetGrid.gridType == GridType.METEORITE;
                    result.Add(new RayGridInfo
                    {
                        Grid = targetGrid,
                        ContactRatio = contactRatio,
                        EntryT = totalDist,
                        ExitT = totalDist + segmentLength,
                        IsBlocked = isBlocked
                    });
                    if (isBlocked)
                    {
                        rayCache[angleDegrees] = result;
                        return result;
                    }
                }
            }

            currentPos += dir * tStep;
            totalDist += tStep * dir.Length();
            bool xExit = Mathf.Abs(tX - tStep) < 0.0001f;
            bool yExit = Mathf.Abs(tY - tStep) < 0.0001f;
            if (xExit && yExit) { currentX += stepX; currentY += stepY; }
            else if (xExit) currentX += stepX;
            else if (yExit) currentY += stepY;

            if (currentX < 0 || currentX >= mapWidth || currentY < 0 || currentY >= mapHeight)
                break;

            if (!useInfiniteRange)
            {
                int manhattanDist = Mathf.Abs(currentX - grid.GridIndex.X) + Mathf.Abs(currentY - grid.GridIndex.Y);
                if (manhattanDist > maxLaserLength) break;
            }
        }

        rayCache[angleDegrees] = result;
        return result;
    }

    private float CalculateBaseDamagePercent(float contactRatio)
    {
        if (useContactAreaDamage)
            return minContactDamagePercent + (maxContactDamagePercent - minContactDamagePercent) * contactRatio;
        return fixedDamagePercent;
    }

    public List<LaserTargetInfo> GetAllValidTargets()
    {
        var allTargets = new List<LaserTargetInfo>();
        foreach (float angle in currentFireAngles)
        {
            var rayGrids = CalculateRayGrids(angle);
            int pierceCount = 0;
            foreach (var rayInfo in rayGrids)
            {
                var g = rayInfo.Grid;
                if (g == null || IsScreenBlocked(g)) continue;
                if (maxPiercePerLine > 0 && pierceCount >= maxPiercePerLine) break;
                var targetsInGrid = GetValidTargetsInGrid(g);
                foreach (var target in targetsInGrid)
                {
                    int distance = CalculateGridDistance(grid.GridIndex, g.GridIndex);
                    float baseDamagePercent = CalculateBaseDamagePercent(rayInfo.ContactRatio);
                    allTargets.Add(new LaserTargetInfo
                    {
                        Target = target,
                        Grid = g,
                        Angle = angle,
                        Distance = distance,
                        PierceIndex = pierceCount,
                        ContactRatio = rayInfo.ContactRatio,
                        BaseDamagePercent = baseDamagePercent
                    });
                    pierceCount++;
                }
            }
        }
        return allTargets;
    }

    private bool CanSelectFacility(Grids g)
    {
        var facility = g?.city;
        if (facility == null || !IsInstanceValid(facility) || !facility.CanBeAttackedFrom(grid)) return false;
        if (team == TeamHelper.Player0) return true;
        return targetSelectionMode switch
        {
            LaserTargetMode.AllSelect => true,
            LaserTargetMode.OnlyEnemyUnits => TeamHelper.IsEnemyTeam(team, facility.facilityTeam),
            LaserTargetMode.OnlyUserUnits => TeamHelper.IsFriendlyTeam(team, facility.facilityTeam),
            _ => false
        };
    }

    private List<Node2D> GetValidTargetsInGrid(Grids g)
    {
        var targets = new List<Node2D>();
        if (CanSelectFacility(g)) targets.Add(g.city);

        // P0 兵器：无差别攻击，除了自己谁都能打，跳过友军/敌军判定
        if (team == TeamHelper.Player0)
        {
            targets.AddRange(g.infantries.Where(i => IsInstanceValid(i)).Cast<Node2D>());
            if (canAttackWeapons && g.weapon != null && IsInstanceValid(g.weapon) && g.weapon != this)
                targets.Add(g.weapon);
            return targets;
        }

        switch (targetSelectionMode)
        {
            case LaserTargetMode.AllSelect:
                targets.AddRange(g.infantries.Where(i => IsInstanceValid(i)).Cast<Node2D>());
                if (canAttackWeapons && g.weapon != null && IsInstanceValid(g.weapon) && g.weapon != this)
                    targets.Add(g.weapon);
                break;
            case LaserTargetMode.OnlyEnemyUnits:
                targets.AddRange(g.infantries.Where(i => IsInstanceValid(i) && TeamHelper.IsEnemyTeam(team, i.team)).Cast<Node2D>());
                if (canAttackWeapons && g.weapon != null && IsInstanceValid(g.weapon) && TeamHelper.IsEnemyTeam(team, g.weapon.team) && g.weapon != this)
                    targets.Add(g.weapon);
                break;
            case LaserTargetMode.OnlyUserUnits:
                targets.AddRange(g.infantries.Where(i => IsInstanceValid(i) && i.team == team).Cast<Node2D>());
                if (canAttackWeapons && g.weapon != null && IsInstanceValid(g.weapon) && g.weapon.team == team && g.weapon != this)
                    targets.Add(g.weapon);
                break;
        }
        return targets;
    }

    public override List<Grids> CalculateAttackRange()
    {
        var allGrids = new List<Grids>();
        foreach (float angle in currentFireAngles)
        {
            var rayGrids = CalculateRayGrids(angle);
            foreach (var rayInfo in rayGrids)
            {
                if (rayInfo.Grid == null) continue;
                // ✅ N屏隔离：跨屏格子不进射程（开canCrossScreens才可跨图）
                if (!canCrossScreens && rayInfo.Grid.screenId != grid.screenId) continue;
                if (!allGrids.Contains(rayInfo.Grid))
                    allGrids.Add(rayInfo.Grid);
            }
        }
        return allGrids;
    }
}
