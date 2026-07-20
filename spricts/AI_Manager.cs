// AI_Manager.cs - 战棋AI管理器（类似AW原版AI）
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class AI_Manager : Node
{
    [Export] public GameManager gameManager;

    public bool p1AIEnabled = false;
    public bool p2AIEnabled = false;
    public bool autoEndTurn = false;

    private bool isRunningAI = false;
    private int aiUnitIndex = 0;
    private List<object> aiActionQueue = new();
    private Timer aiTimer;

    public override void _Ready()
    {
        AddToGroup("ai_manager");
        if (gameManager == null)
            gameManager = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;

        aiTimer = new Timer();
        aiTimer.WaitTime = 0.5f; // ✅ 增加延迟：让玩家能看到AI行动过程
        aiTimer.OneShot = true;
        aiTimer.Timeout += OnAIStep;
        AddChild(aiTimer);
    }

    public bool IsAITeam(string team)
    {
        return (team == TeamHelper.Player1 && p1AIEnabled)
            || (team == TeamHelper.Player2 && p2AIEnabled);
    }

    public bool IsCurrentPhaseAI()
    {
        string phaseTeam = TeamHelper.GetPhaseTeamName(gameManager?.turnPhase ?? 1);
        return IsAITeam(phaseTeam);
    }

    // 在 GameManager.NextPhase 的末尾调用
    public void OnPhaseStart()
    {
        if (gameManager == null) return;
        string phaseTeam = TeamHelper.GetPhaseTeamName(gameManager.turnPhase);
        if (!IsAITeam(phaseTeam)) return;


        // 先执行生产（在单位行动之前）
        AIProduction(phaseTeam);

        isRunningAI = true;
        aiUnitIndex = 0;
        BuildAIQueue(phaseTeam);
        if (aiActionQueue.Count > 0)
            aiTimer.Start();
        else
            TryAutoEndTurn();
    }

    private void BuildAIQueue(string team)
    {
        aiActionQueue.Clear();

        // 兵器优先
        var weapons = gameManager.weaponManager?.AllWeapons
            ?.Where(w => IsInstanceValid(w) && w.team == team && w.CanAttack())
            .ToList() ?? new List<Weapon>();
        foreach (var w in weapons) aiActionQueue.Add(w);

        // 然后单位
        var units = gameManager.unitManager?.AllUnits
            ?.Where(u => IsInstanceValid(u) && u.team == team && u.state != UnitState.Acted)
            .ToList() ?? new List<Infantry>();
        foreach (var u in units) aiActionQueue.Add(u);
    }

    private void OnAIStep()
    {
        if (!isRunningAI || aiUnitIndex >= aiActionQueue.Count)
        {
            isRunningAI = false;
            TryAutoEndTurn();
            return;
        }

        var target = aiActionQueue[aiUnitIndex];
        aiUnitIndex++;

        if (target is Weapon weapon && IsInstanceValid(weapon))
        {
            if (weapon.CanAttack())
            {
                weapon.ExecuteAI();
            }
        }
        else if (target is Infantry unit && IsInstanceValid(unit))
        {
            if (unit.state != UnitState.Acted)
            {
                ExecuteUnitAI(unit);
            }
        }

        // 检查是否继续
        if (aiUnitIndex < aiActionQueue.Count)
            aiTimer.Start();
        else
        {
            isRunningAI = false;
            TryAutoEndTurn();
        }
    }

    private void TryAutoEndTurn()
    {
        if (!autoEndTurn || gameManager == null) return;
        string phaseTeam = TeamHelper.GetPhaseTeamName(gameManager.turnPhase);
        if (!IsAITeam(phaseTeam)) return;

        // 延迟检查是否还有未行动的单位
        var checkTimer = GetTree().CreateTimer(0.5f);
        checkTimer.Timeout += () =>
        {
            if (!HasPendingActions(phaseTeam))
            {
                gameManager.NextPhase();
            }
        };
    }

    private bool HasPendingActions(string team)
    {
        // 检查兵器
        if (gameManager.weaponManager?.AllWeapons != null)
            foreach (var w in gameManager.weaponManager.AllWeapons)
                if (IsInstanceValid(w) && w.team == team && w.CanAttack())
                    return true;
        // 检查单位
        if (gameManager.unitManager?.AllUnits != null)
            foreach (var u in gameManager.unitManager.AllUnits)
                if (IsInstanceValid(u) && u.team == team && u.state != UnitState.Acted)
                    return true;
        return false;
    }

    // ========== 单位AI核心逻辑 ==========
    private void ExecuteUnitAI(Infantry unit)
    {
        if (unit == null || !IsInstanceValid(unit)) return;
        var gm = gameManager;
        var gridMgr = gm?.gridManager;
        if (gridMgr == null) return;

        // 1. 检查是否只剩APC（无战斗单位）→ 躲避
        if (IsOnlyAPCRemaining(unit.team))
        {
            AIEvade(unit);
            return;
        }

        // 2. 如果是APC（运输车），不是只剩APC的情况 → 向敌人前线靠近（搭载步兵）
        if (unit is APC)
        {
            AITransportMove(unit);
            return;
        }

        // ✅ 3. 特殊能力决策（按最优价值，优先于移动/攻击）
        // 3.1 补给：如果附近有友方单位需要补给
        if (unit.canSupplyUnits && unit.state != UnitState.Acted && !unit.hasActed)
        {
            if (TryAISupply(unit))
                return;
        }

        // 3.2 照明弹：如果迷雾开启且前方有不可见敌人
        if (unit.canIlluminate && unit.currentFlareAmmo > 0 && 
            unit.state != UnitState.Acted && !unit.hasActed &&
            (!unit.isMoved || unit.canIlluminateAfterMove))
        {
            if (TryAIIlluminate(unit))
                return;
        }

        // 3.3 自爆：如果周围敌方价值远高于自身
        if (unit.canExplode && unit.state != UnitState.Acted && !unit.hasActed)
        {
            if (TryAIExplode(unit))
                return;
        }

        // 4. 战斗单位AI：先找可攻击的敌人
        var enemies = GetAllEnemyUnitsAndWeapons(unit.team);
        if (enemies.Count == 0)
        {
            // 无敌人：向最近城市移动占领
            AICaptureMove(unit);
            return;
        }

        // 找到最佳攻击目标（优先：可击杀 > 血量最低 > 最近）
        var bestTarget = FindBestAttackTarget(unit, enemies);
        if (bestTarget != null)
        {
            // 检查是否已经在攻击范围内
            if (IsInAttackRange(unit, bestTarget))
            {
                AIAttack(unit, bestTarget);
                return;
            }

            // 尝试移动到可攻击范围内
            var moveTarget = FindMoveToAttackPosition(unit, bestTarget);
            if (moveTarget != null && moveTarget != unit.grid)
            {
                AIMove(unit, moveTarget);
                // 移动后如果还能攻击
                if (unit.state == UnitState.Moved && !unit.isAttacked && IsInAttackRange(unit, bestTarget))
                {
                    AIAttack(unit, bestTarget);
                }
                else if (unit.state == UnitState.Moved && !unit.isAttacked)
                {
                    // 移动后仍不能攻击，找新目标
                    var newTarget = FindBestAttackTarget(unit, enemies);
                    if (newTarget != null && IsInAttackRange(unit, newTarget))
                        AIAttack(unit, newTarget);
                    else
                        AIEndTurn(unit);
                }
                return;
            }
        }

        // 5. 无法攻击：占领或待机
        AICaptureMove(unit);
    }

    // ========== AI子方法 ==========

    private bool IsOnlyAPCRemaining(string team)
    {
        var units = gameManager.unitManager?.AllUnits
            ?.Where(u => IsInstanceValid(u) && u.team == team).ToList() ?? new List<Infantry>();
        if (units.Count == 0) return false;
        return units.All(u => u is APC || u.attackType == AttackType.NoAttack);
    }

    private void AIEvade(Infantry unit)
    {
        // APC躲避：向离敌人最远的方向移动
        var enemies = GetAllEnemyUnitsAndWeapons(unit.team);
        if (enemies.Count == 0 || unit.grid == null)
        {
            AIEndTurn(unit);
            return;
        }

        // 计算所有敌人重心
        float avgX = 0, avgY = 0;
        foreach (var e in enemies)
        {
            var g = e is Infantry i ? i.grid : (e is Weapon w ? w.grid : null);
            if (g != null) { avgX += g.GridIndex.X; avgY += g.GridIndex.Y; }
        }
        avgX /= enemies.Count;
        avgY /= enemies.Count;

        // 找到移动范围内离敌人重心最远的格子
        var moveRange = CalculateAIMoveRange(unit);
        Grids bestGrid = null;
        float maxDist = -1;
        foreach (var g in moveRange)
        {
            float dist = Mathf.Abs(g.GridIndex.X - avgX) + Mathf.Abs(g.GridIndex.Y - avgY);
            if (dist > maxDist) { maxDist = dist; bestGrid = g; }
        }

        if (bestGrid != null && bestGrid != unit.grid)
            AIMove(unit, bestGrid);
        else
            AIEndTurn(unit);
    }

    private void AITransportMove(Infantry unit)
    {
        // APC：向最近的己方步兵移动（搭载），或者向敌人靠近
        var allies = gameManager.unitManager?.AllUnits
            ?.Where(u => IsInstanceValid(u) && u.team == unit.team && u != unit && u is Infantry && !(u is APC))
            .ToList() ?? new List<Infantry>();

        if (allies.Count > 0 && unit.maxTransportCapacity > unit.transportedUnits.Count)
        {
            // 找最近的未搭载步兵
            var nearest = allies.OrderBy(a => Distance(unit.grid, a.grid)).FirstOrDefault();
            if (nearest != null && unit.grid != null)
            {
                var moveRange = CalculateAIMoveRange(unit);
                var targetGrid = moveRange.OrderBy(g => Distance(g, nearest.grid)).FirstOrDefault();
                if (targetGrid != null && targetGrid != unit.grid)
                {
                    AIMove(unit, targetGrid);
                    return;
                }
            }
        }

        // 没有步兵可搭载：向敌人方向靠近（但不要太近）
        var enemies = GetAllEnemyUnitsAndWeapons(unit.team);
        if (enemies.Count > 0 && unit.grid != null)
        {
            var nearestEnemy = enemies.OrderBy(e => Distance(unit.grid, GetEntityGrid(e))).FirstOrDefault();
            if (nearestEnemy != null)
            {
                var enemyGrid = GetEntityGrid(nearestEnemy);
                var moveRange = CalculateAIMoveRange(unit);
                // 保持3格距离
                Grids bestGrid = null;
                float bestScore = float.MinValue;
                foreach (var g in moveRange)
                {
                    float distToEnemy = Distance(g, enemyGrid);
                    float score = -Mathf.Abs(distToEnemy - 3); // 尽量保持3格距离
                    if (score > bestScore) { bestScore = score; bestGrid = g; }
                }
                if (bestGrid != null && bestGrid != unit.grid)
                {
                    AIMove(unit, bestGrid);
                    return;
                }
            }
        }

        AIEndTurn(unit);
    }

    private void AICaptureMove(Infantry unit)
    {
        if (unit.captureAbility != CaptureAbility.CanCapture || unit.grid == null)
        {
            AIEndTurn(unit);
            return;
        }

        // ✅ 如果已经在可占领的城市上，直接执行占领（不依赖 moveRange 包含当前格子）
        if (unit.grid?.city != null && unit.grid.city.facilityTeam != unit.team)
        {
            AICapture(unit);
            return;
        }

        // 找最近的可占领城市
        var cities = gameManager.gridManager?.grids
            ?.Where(g => g != null && IsInstanceValid(g) && g.city != null)
            .OrderBy(g => Distance(unit.grid, g))
            .ToList() ?? new List<Grids>();

        foreach (var cityGrid in cities)
        {
            if (cityGrid.city.facilityTeam == unit.team) continue; // 已占领
            var moveRange = CalculateAIMoveRange(unit);
            var target = moveRange.OrderBy(g => Distance(g, cityGrid)).FirstOrDefault();
            if (target != null)
            {
                if (target == unit.grid)
                {
                    // 已经在城市上，执行占领
                    AICapture(unit);
                }
                else
                {
                    AIMove(unit, target);
                }
                return;
            }
        }

        AIEndTurn(unit);
    }

    private void AICapture(Infantry unit)
    {
        if (unit.grid?.city == null) return;
        unit.isMoved = true;
        unit.state = UnitState.Moved;
        unit.PerformCapture();
        AIEndTurn(unit);
    }

    private Infantry FindBestAttackTarget(Infantry unit, List<object> enemies)
    {
        if (enemies.Count == 0) return null;

        // 优先：能击杀的 > 血量最低的 > 最近的
        var validEnemies = enemies
            .Select(e => e is Infantry i ? i : null)
            .Where(i => i != null && IsInstanceValid(i) && i.health > 0)
            .ToList();

        if (validEnemies.Count == 0) return null;

        // 优先攻击可击杀的（估算伤害）
        foreach (var e in validEnemies.OrderBy(e => e.health))
        {
            WeaponType wt = unit.SelectWeaponByMatrix(e);
            if (wt == WeaponType.None) continue;
            int estimatedDmg = unit.CalculateFinalDamage(e, wt);
            if (estimatedDmg >= e.health) return e;
        }

        // 没有可击杀的：优先攻击血量最低的
        return validEnemies.OrderBy(e => e.health).FirstOrDefault();
    }

    private bool IsInAttackRange(Infantry unit, Infantry target)
    {
        if (unit.grid == null || target.grid == null) return false;
        int dist = Mathf.Abs(unit.grid.GridIndex.X - target.grid.GridIndex.X)
                 + Mathf.Abs(unit.grid.GridIndex.Y - target.grid.GridIndex.Y);
        if (unit.useMinMaxAttackRange)
            return dist >= unit.minAttackRange && dist <= unit.maxAttackRange;
        return dist <= unit.attackRange;
    }

    private Grids FindMoveToAttackPosition(Infantry unit, Infantry target)
    {
        if (unit.grid == null || target.grid == null) return null;
        var moveRange = CalculateAIMoveRange(unit);
        Grids best = null;
        int bestDist = int.MaxValue;
        foreach (var g in moveRange)
        {
            int dist = Mathf.Abs(g.GridIndex.X - target.grid.GridIndex.X)
                     + Mathf.Abs(g.GridIndex.Y - target.grid.GridIndex.Y);
            bool canAttack = unit.useMinMaxAttackRange
                ? (dist >= unit.minAttackRange && dist <= unit.maxAttackRange)
                : dist <= unit.attackRange;
            if (canAttack && dist < bestDist) { bestDist = dist; best = g; }
        }
        return best;
    }

    private List<Grids> CalculateAIMoveRange(Infantry unit)
    {
        if (unit == null || unit.grid == null || gameManager?.gridManager == null)
            return new List<Grids>();

        int effectiveMove = unit.movePoints;
        if (unit.consumeFuel)
            effectiveMove = Mathf.Min(unit.movePoints, unit.fuel);

        // ✅ 统一使用 GridManager.GetMoveRange（与我方单位相同的移动判定）
        return gameManager.gridManager.GetMoveRange(unit, effectiveMove);
    }

    private void AIMove(Infantry unit, Grids to)
    {
        if (unit == null || to == null || unit.grid == null) return;
        var gridMgr = gameManager?.gridManager;
        if (gridMgr == null) return;

        // 计算路径和消耗
        var path = gridMgr.BuildPath(unit.grid, to, unit);
        if (path.Count == 0) { AIEndTurn(unit); return; }

        int totalMoveCost = 0;
        int totalFuelCost = 0;
        bool isFirst = true;
        foreach (var g in path)
        {
            if (isFirst) { isFirst = false; continue; }
            int cost = unit.GetMoveCost(g.gridType);
            totalMoveCost += cost;
            if (unit.consumeFuel && g.gridType != GridType.TP)
                totalFuelCost += cost;
        }

        if (unit.movePoints < totalMoveCost) { AIEndTurn(unit); return; }
        if (unit.consumeFuel && unit.fuel < totalFuelCost) { AIEndTurn(unit); return; }

        // 执行移动
        unit.movePoints -= totalMoveCost;
        if (unit.consumeFuel) unit.ConsumeFuel(totalFuelCost);

        var fromGrid = unit.grid;
        fromGrid.infantries.Remove(unit);
        if (fromGrid.infantry == unit)
            fromGrid.infantry = fromGrid.infantries.Count > 0 ? fromGrid.infantries[0] : null;

        unit.Position = to.Position;
        unit.grid = to;
        if (!to.infantries.Contains(unit)) to.infantries.Add(unit);
        if (to.infantry == null || unit.overlapType != UnitOverlapType.Overlapping)
            to.infantry = unit;

        unit.state = UnitState.Moved;
        unit.isMoved = true;
        unit.originalGrid = null;

        gameManager?.fogOfWarManager?.OnUnitMoved();
    }

    private void AIAttack(Infantry unit, Infantry target)
    {
        if (unit == null || target == null || !IsInstanceValid(target)) return;
        if (!IsInAttackRange(unit, target)) { AIEndTurn(unit); return; }


        // ✅ 攻击前动画闪烁：让玩家看到攻击过程
        if (unit.sprite != null)
        {
            var tween = unit.CreateTween();
            tween.TweenProperty(unit.sprite, "modulate", Colors.Red, 0.15f);
            tween.TweenProperty(unit.sprite, "modulate", new Color(0.7f, 0.7f, 0.7f, 1f), 0.15f);
        }

        unit.Attack(target);

        // ✅ 更新UI
        gameManager?.gridManager?.HideAttackRange();
        gameManager?.ClearSelectedInfantry();
        gameManager?.CallDeferred(nameof(gameManager.UpdateUnitLists));
    }

    private void AIEndTurn(Infantry unit)
    {
        if (unit == null || !IsInstanceValid(unit)) return;
        unit.isAttacked = true;
        if (unit.state != UnitState.Acted)
        {
            unit.state = UnitState.Acted;
            unit.SetWaitVisual(true);
        }
        unit.originalGrid = null;
    }

    // ========== AI 智能生产系统 ==========
    private void AIProduction(string team)
    {
        if (gameManager?.gridManager?.grids == null) return;

        int currentFunds = gameManager.GetTeamFunds(team);
        int minReserve = 1000; // 最便宜单位 Infantry 的价格

        // 单位优先级（数值越高越优先，综合战略价值和性价比）
        var unitPriority = new Dictionary<string, int>
        {
            ["Infantry"] = 100,
            ["Bike"] = 95,
            ["Mech"] = 90,
            ["Recon"] = 80,
            ["APC"] = 75,
            ["Flare"] = 70,
            ["Artillery"] = 60,
            ["LightTank"] = 55,
            ["AntiAir"] = 50,
            ["AntiTank"] = 40,
            ["Rocket"] = 30,
            ["MdTank"] = 25,
            ["Oozium"] = 15,
            ["FlyBomb"] = 10
        };

        foreach (var grid in gameManager.gridManager.grids)
        {
            if (grid == null || !IsInstanceValid(grid)) continue;
            var facility = grid.city;
            if (facility == null || !facility.canProduce || facility.facilityTeam != team) continue;
            if (grid.infantries.Count > 0 || grid.weapons.Count > 0) continue; // 格子必须为空
            if (facility.producibleUnitNames == null || facility.producibleUnitNames.Count == 0) continue;

            // 按优先级排序可生产单位
            var candidates = facility.producibleUnitNames
                .Where(name => UnitProductionDatabase.HasUnit(name))
                .Select(name => new { Name = name, Info = UnitProductionDatabase.GetInfo(name) })
                .Where(x => x.Info.Cost > 0)
                .OrderByDescending(x => unitPriority.GetValueOrDefault(x.Name, 0))
                .ThenBy(x => x.Info.Cost)
                .ToList();

            foreach (var candidate in candidates)
            {
                int cost = candidate.Info.Cost;
                int reserve = Mathf.Max(minReserve, currentFunds / 3); // 保留至少1000或资金的1/3

                // 存钱逻辑：生产后剩余资金必须 >= reserve
                if (currentFunds - cost < reserve) continue;

                bool success = gameManager.AIProduceUnit(facility, grid, candidate.Name, team);
                if (success)
                {
                    currentFunds -= cost;
                    break; // 每个设施每回合只生产一个单位
                }
            }
        }
    }

    private List<object> GetAllEnemyUnitsAndWeapons(string team)
    {
        var result = new List<object>();
        var fog = gameManager?.fogOfWarManager;
        bool isFogEnabled = fog != null && fog.isFogOfWarEnabled;

        if (gameManager?.unitManager?.AllUnits != null)
            foreach (var u in gameManager.unitManager.AllUnits)
                if (IsInstanceValid(u) && u.health > 0 && TeamHelper.IsEnemyForAttacker(team, u.team))
                {
                    // ✅ AI迷雾：雾外看不到，不能用底层统计
                    if (isFogEnabled && u.grid != null && !fog.IsGridVisible(u.grid))
                        continue;
                    result.Add(u);
                }

        if (gameManager?.weaponManager?.AllWeapons != null)
            foreach (var w in gameManager.weaponManager.AllWeapons)
                if (IsInstanceValid(w) && w.health > 0 && TeamHelper.IsEnemyForAttacker(team, w.team))
                {
                    // ✅ AI迷雾：雾外看不到，不能用底层统计
                    if (isFogEnabled && w.grid != null && !fog.IsGridVisible(w.grid))
                        continue;
                    result.Add(w);
                }

        return result;
    }

    private Grids GetEntityGrid(object entity)
    {
        if (entity is Infantry i) return i.grid;
        if (entity is Weapon w) return w.grid;
        return null;
    }

    private int Distance(Grids a, Grids b)
    {
        if (a == null || b == null) return int.MaxValue;
        return Mathf.Abs(a.GridIndex.X - b.GridIndex.X) + Mathf.Abs(a.GridIndex.Y - b.GridIndex.Y);
    }

    // ========== ✅ AI特殊能力：寻找最优价值决策 ==========

    /// <summary>
    /// AI补给决策：如果附近有友方单位需要补给（弹药/燃料/照明弹），执行补给
    /// </summary>
    private bool TryAISupply(Infantry unit)
    {
        if (!unit.canSupplyUnits || unit.grid == null) return false;

        var supplyableUnits = unit.GetSupplyRangeUnits();
        if (supplyableUnits == null || supplyableUnits.Count == 0) return false;

        int supplyValue = 0;
        foreach (var target in supplyableUnits)
        {
            if (target == null || !IsInstanceValid(target)) continue;
            if (target.hasPrimaryWeapon && target.primaryHasLimitedAmmo &&
                target.currentPrimaryAmmo < target.maxPrimaryAmmo)
                supplyValue += 30; // 弹药补给价值
            if (target.canIlluminate && target.currentFlareAmmo < target.maxFlareAmmo)
                supplyValue += 20; // 照明弹补给价值
            if (target.consumeFuel && target.fuel < target.maxFuel)
                supplyValue += 15; // 燃料补给价值
        }

        // 补给价值阈值：至少有一个单位需要补给
        if (supplyValue > 0)
        {
            unit.PerformSupply();
            unit.isAttacked = true;
            unit.state = UnitState.Acted;
            unit.SetWaitVisual(true);
            unit.originalGrid = null;
            return true;
        }
        return false;
    }

    /// <summary>
    /// AI照明弹决策：如果迷雾开启且前方有不可见的敌人/重要区域，发射照明弹
    /// </summary>
    private bool TryAIIlluminate(Infantry unit)
    {
        if (!unit.canIlluminate || unit.currentFlareAmmo <= 0 || unit.grid == null) return false;

        var fog = gameManager?.fogOfWarManager;
        if (fog == null || !fog.isFogOfWarEnabled) return false;

        var gridMgr = gameManager?.gridManager;
        if (gridMgr == null) return false;

        // 找到最佳照明目标：在投射射程内，能照亮最多不可见敌方单位的格子
        Grids bestTarget = null;
        int bestValue = 0;

        var launchRange = gridMgr.FindRange(unit.grid, unit.maxLaunchRange, false);
        var blindZone = unit.minLaunchRange > 0 ? gridMgr.FindRange(unit.grid, unit.minLaunchRange - 1, false) : new List<Grids>();

        foreach (var g in launchRange)
        {
            if (blindZone.Contains(g)) continue; // 盲区内不发射
            if (g == null) continue;

            // 计算该格子作为照明中心的价值
            int value = 0;
            var illumRange = gridMgr.FindRange(g, unit.maxIlluminationRange, false);
            var illumBlind = unit.minIlluminationRange > 0 ? gridMgr.FindRange(g, unit.minIlluminationRange - 1, false) : new List<Grids>();

            foreach (var ig in illumRange)
            {
                if (illumBlind.Contains(ig)) continue;
                if (ig == null) continue;
                if (!fog.IsGridVisible(ig)) // 只计算不可见格子
                {
                    value += 5; // 照亮未知区域的基础价值
                    // 如果格子有敌方单位，价值更高
                    ig.infantries.RemoveAll(u => u == null || !IsInstanceValid(u));
                    if (ig.infantries.Any(u => u.team != unit.team))
                        value += 20; // 发现敌人的价值
                    if (ig.weapon != null && ig.weapon.team != unit.team)
                        value += 15; // 发现敌方兵器的价值
                }
            }

            if (value > bestValue)
            {
                bestValue = value;
                bestTarget = g;
            }
        }

        // 照明价值阈值：至少照亮一个敌人或照亮大片未知区域
        if (bestTarget != null && bestValue >= 25)
        {
            unit.currentFlareAmmo--;
            fog.AddIllumination(bestTarget, unit.minIlluminationRange, unit.maxIlluminationRange, unit.flareDurationTurns);
            unit.isMoved = true;
            unit.isAttacked = true;
            unit.state = UnitState.Acted;
            unit.originalGrid = null;
            unit.SetWaitVisual(true);
            return true;
        }
        return false;
    }

    /// <summary>
    /// AI自爆决策：如果周围敌方价值远高于自身，执行自爆
    /// </summary>
    private bool TryAIExplode(Infantry unit)
    {
        if (!unit.canExplode || unit.grid == null) return false;

        var gm = gameManager;
        var gridPos = unit.grid.GridIndex;
        int minR = unit.explosionMinRange;
        int maxR = unit.explosionMaxRange;

        int totalEnemyDamage = 0;
        int totalAllyDamage = 0;
        int enemyCount = 0;

        // 计算爆炸范围内的单位
        foreach (var u in gm.unitManager.AllUnits)
        {
            if (u == null || !IsInstanceValid(u) || u == unit) continue;
            var uGridPos = u.grid?.GridIndex ?? gm.unitManager.WorldToGrid(u.Position);
            int dist = Mathf.Abs(uGridPos.X - gridPos.X) + Mathf.Abs(uGridPos.Y - gridPos.Y);
            if (dist >= minR && dist <= maxR)
            {
                int damage = gm.CalculateExplosionDamage(unit, u);
                if (u.team != unit.team)
                {
                    totalEnemyDamage += damage;
                    enemyCount++;
                }
                else if (unit.explosionTargetMode != 1) // 不是仅敌
                {
                    totalAllyDamage += damage;
                }
            }
        }

        // 评估：敌方伤害价值 > 自身生命值 * 1.5，且敌方数量 >= 2
        int selfValue = unit.health * unit.baseAttack / 100;
        if (totalEnemyDamage > selfValue * 1.5f && enemyCount >= 2 && totalEnemyDamage > totalAllyDamage * 2)
        {
            gm.ExecuteExplosion(unit);
            return true;
        }
        return false;
    }
}
