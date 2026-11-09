// GameManager.cs - 添加完整的单位统计系统
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// GameManager · Turn（由 GameManager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class GameManager
{
    // ✅ totalPhases 动态计算 = 启用的战斗势力数（不含 P-1 和 P0）
    public int totalPhases => Mathf.Max(1, TeamHelper.GetActiveFactions().Count(f => f.Id != TeamHelper.Player && f.Id != TeamHelper.Player0));

    // 切换回合
    public void NextPhase()
    {
        if (encounterPause) return;   // ✅ 回合中回合冻结
        // ✅ 关键修复：编辑模式下禁止切换回合
        var terrainEditor = GetTree()?.GetFirstNodeInGroup("terrain_editor") as TerrainEditor;
        if (terrainEditor != null && terrainEditor.IsEditMode)
        {
            return;
        }

        HideProductionMenu();
        // ✅ CO 模块系统：当前势力回合结束（COP/SCOP 效果过期判定 + 每回合重置）
        CoModuleSystem.OnTeamTurnEnd(TeamHelper.GetPhaseTeamName(turnPhase));
        turnEndLabel?.Show();
        GetTree().CreateTimer(1.2f).Timeout += () => turnEndLabel?.Hide();
        int previousTurn = turns; 
        turnPhase++;
        if (turnPhase > totalPhases)
        {
            turns++;
            turnPhase = 1;
            gridManager?.PerformTurnSupply();

        weaponManager?.OnTurnStartForNewTurn(); // 使用新方法

        // ✅ 照明弹效果大回合过期处理
        ProcessTerrainSpreadTurnStart();
        fogOfWarManager?.OnBigTurnEnd();

        }

        // ✅ 跳过已失败势力的回合
        int skipSafety = 0;
        while (skipSafety < totalPhases)
        {
            string checkTeam = TeamHelper.GetPhaseTeamName(turnPhase);
            if (TeamHelper.ActiveTeams.Contains(checkTeam) && defeatedTeams.Contains(checkTeam))
            {
                turnPhase++;
                if (turnPhase > totalPhases)
                {
                    turnPhase = 1;
                }
            }
            else
            {
                break;
            }
            skipSafety++;
        }

        string currentTeam = TeamHelper.GetPhaseTeamName(turnPhase);

        // ✅ 切换回合：刷新当前势力建筑的生产次数（P-1通用建筑每个势力回合开始都刷新）
        if (gridManager?.grids != null)
        {
            foreach (var g in gridManager.grids)
            {
                if (g?.city != null && (g.city.facilityTeam == currentTeam || g.city.facilityTeam == TeamHelper.Player))
                    g.city.ResetProductionCount();
                // ✅ 雷达：每回合开始重置/激活（自动模式）
                if (g?.city is Radar radar)
                    radar.OnTurnStartRadar();
                // ✅ 通信塔：每回合开始重置/激活（自动模式）
                if (g?.city is ComTower comTower)
                    comTower.OnTurnStartComTower();
            }
        }

        // ✅ 兵器回合开始：大雷弹（蠕动/冷却/Oozium生产）、黑色弧光（冷却）
        foreach (var wp in weaponManager?.AllWeapons ?? new List<Weapon>())
        {
            if (wp == null || !IsInstanceValid(wp)) continue;
            if (wp.team != currentTeam) continue;
            if (wp is GrandBolt gb) { gb.OnTurnStartGrandBolt(this); gb.EmpBurst(this); }   // ✅ 电磁爆：回合开始爆发
            if (wp is BlackArc arc) arc.OnTurnStartArc();
        }

        // ✅ 资金系统：每回合开始时给当前phase的势力增加资金
        string fundTeam = TeamHelper.GetPhaseTeamName(turnPhase);
        if (!string.IsNullOrEmpty(fundTeam))
        {
            AddFundsForTeam(fundTeam);
        }

        // ✅ P0 AI：在当前phase结束后（最后一个大回合阶段）执行 P0 和 P-1 兵器 AI
        bool isLastPhase = turnPhase == totalPhases;
        if (isLastPhase)
        {
            foreach (var wp in weaponManager?.AllWeapons ?? new List<Weapon>())
            {
                if (IsInstanceValid(wp) && (TeamHelper.ShouldWeaponUseAI(wp.team) || TeamHelper.ShouldPlayerWeaponUseAI(turnPhase + 1, wp.team)))
                {
                    wp.ExecuteAI();
                }
            }
        }

        UpdateTurnLabel();

        // ========== ✅ 刷新战争迷雾 ==========
        fogOfWarManager?.OnTurnChanged();
        bool isNewTurn = (turns > previousTurn);
        if (isNewTurn)
        {
            WeatherSystem.AdvanceDay();   // ✅ 天气系统（模块15）：新一天按出现率轮换天气
            WeatherManager.Instance?.OnTurnStart();   // ✅ issue#1：区域天气层（格子栈）回合递减（大回合一次，非每阶段）
            ProcessSurvivalCountdowns();              // ✅ 开创人2026-11-10：单位/兵器/设施 存活胜负倒计时（默认关闭，0=不计）
        }
        selectedInfantry = null;
                        if (turnPhase == 1)

        {
        }
        Infantry.UpdateUnitsCannotMove();

        // ✅ 自动修复/补给（回合开始时）
        foreach (var unit in unitManager?.AllUnits ?? new List<Infantry>())
        {
            if (!IsInstanceValid(unit)) continue;
            // APC 自动补给
            if (unit is APC apc && apc.autoSupplyOnTurnStart)
            {
                apc.OnTurnStartAutoSupply();
            }
            // RepairBoat 自动修复
            // ✅ 通用：任何开启修理模块的单位
            if (unit.CanPerformRepair && unit.autoRepairOnTurnStart)
            {
                unit.OnTurnStartAutoRepair();
            }

            // 设施压制：回合开始刷新施加（Aura）
            if (unit.canSuppressFacilities)
            {
                unit.OnTurnStartFacilitySuppression();
            }

            // 群体狂热：回合开始刷新（覆盖式生效/持续回合衰减）
            if (unit.packFrenzyEnabled)
            {
                unit.OnTurnStartPackFrenzy();
            }
        }

        // 重置所有单位状态

                foreach (var wp in weaponManager?.AllWeapons ?? new List<Weapon>())
        {
            if (IsInstanceValid(wp))
            {
                wp.OnTurnStart();
            }
        }
        foreach (var unit in unitManager?.AllUnits ?? new List<Infantry>())
        {
            if (!IsInstanceValid(unit)) 
            {
                continue;
            }
        // ✅ 通用运输单位自动补给（排除 RepairBoat，它走专门的 OnTurnStartAutoRepair）
        if (isNewTurn && unit.canTransportUnits && unit.maxTransportCapacity > 0 && unit is not RepairBoat)
        {
            unit.OnTurnStartAutoSupply();
        }
        if (isNewTurn && unit.canTransportUnits) unit.ResetCargoBoardTracking();   // ✅ 舰载起飞：新回合起舱内单位均可起飞
            if (unit.currentCaptureProgress > 0)
            {
                if (unit.grid != unit.capturingGrid)
                {
                    // 不在占领的格子上了，重置进度
                    unit.currentCaptureProgress = 0;
                    unit.capturingGrid = null;
                }
                else
                {
                }
            }
            // ✅ 运输单位移动后中断舱内占领
            if (unit.loadedCapturingGrid != null && unit.grid != unit.loadedCapturingGrid)
            {
                unit.loadedCaptureProgress = 0;
                unit.loadedCapturingGrid = null;
            }
            bool canMoveThisTurn = APC.CanUnitMove(unit);
            unit.isMoved = false;
            unit.isAttacked = false;

            // ✅ 电磁爆麻痹：麻痹中强制待机（不能移动/攻击）；当前行动势力的单位在其回合开始时递减
            if (unit.stunTurnsRemaining > 0)
            {
                if (unit.team == TeamHelper.GetPhaseTeamName(turnPhase))
                    unit.stunTurnsRemaining--;
                unit.hasActed = true;
                unit.state = UnitState.Acted;
                unit.SetWaitVisual(true);
            }

            // ✅ CO 模块25（状态系统）：燃烧掉血/虚弱递减（当前行动势力单位在其回合开始结算）
            if (unit.team == TeamHelper.GetPhaseTeamName(turnPhase))
            {
                if (unit.burnTurnsRemaining > 0)
                {
                    unit.health = Mathf.Max(1, unit.health - unit.burnDamage * 10);   // 燃烧最低留1HP
                    unit.UpdateHpLabel();
                    unit.burnTurnsRemaining--;
                }
                if (unit.weakTurnsRemaining > 0)
                    unit.weakTurnsRemaining--;
            }

            // ✅ 生产/建造占用回合（非自动化）：进度没走完的回合，单位保持待机
            if (unit.isProducing && !unit.productionAutomation)
            {
                unit.hasActed = true;
                unit.state = UnitState.Acted;
                unit.SetWaitVisual(true);
            }
            if (unit.isConstructing && !unit.constructionAutomation)
            {
                unit.hasActed = true;
                unit.state = UnitState.Acted;
                unit.SetWaitVisual(true);
            }




                if (unit is Infantry inf)
    {
        inf.UpdateTeamVisual();
    }



            unit.OnTurnEnd();  // ✅ 虚方法多态已自动调用最终版本，无需反射

            if (unit.hpLabel != null)
            {
                unit.hpLabel.Modulate = Colors.White;
            }

            // 重新绑定格子
            if (unit.grid == null)
            {
                unitManager.BindUnitToGrid(unit, true);
            }
            else
            {
                var expectedGridPos = unitManager.WorldToGrid(unit.Position);
                if (unit.grid.GridIndex != expectedGridPos)
                {
                    unitManager.BindUnitToGrid(unit, true);
                }
            }
        }
        // ✅ 每完整 Turn 燃料消耗（仅在完整回合切换时，即两 Phase 都结束后）
        if (isNewTurn)
        {
            foreach (var u in unitManager?.AllUnits ?? new List<Infantry>())
            {
                if (!IsInstanceValid(u)) continue;
                u.ConsumeDailyFuel();
                u.ProcessInternalServices();      // ✅ 内部补给/内部回血（舱内单位）
                u.ProcessTurnProduction();        // ✅ 生产模块：进度推进/完工/冷却
        u.ProcessTurnSupport();             // ✅ 力场支援模块：每回合扫描上锁
                u.ProcessTurnConstruction();      // ✅ 建造模块：进度推进/完工/冷却
            }
            Infantry.EnforcePermanentForceFields();   // ✅ 永久力场：全局执行（卫星死亡后仍回写锁死值）
        }

        weaponManager?.OnPhaseStart();
        CoModuleSystem.CheckCoSurvival(TeamHelper.GetPhaseTeamName(turnPhase), this);   // ✅ CO绑屏：每相位查存活（开创人2026-11-07）
        // 关闭所有UI
        gridManager.CloseRange();
        gridManager.HideAttackRange();
        (GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu)?.Hide();

        // ✅ 回合切换时更新统计（可能有单位死亡）
        UpdateUnitLists();

        // ========== ✅ 新增：回合切换时触发地形伤害（完整回合切换时）==========
        if (isNewTurn)
        {
            ApplyTerrainDamageToAllUnits();
        }

        // ✅ AI 回合触发
        var aiManager = GetTree()?.GetFirstNodeInGroup("ai_manager") as AI_Manager;
        if (aiManager != null && aiManager.IsCurrentPhaseAI())
        {
            aiManager.OnPhaseStart();
        }
    }

    // ========== ✅ 新增：对所有单位应用地形伤害 ==========
    public void ApplyTerrainDamageToAllUnits(string onlyTeam = null)
    {
        if (unitManager?.AllUnits == null) return;


        foreach (var unit in unitManager.AllUnits.ToList())
        {
            if (unit == null || !IsInstanceValid(unit)) continue;
            if (unit.grid == null) continue;
            if (onlyTeam != null && unit.team != onlyTeam) continue;

            var grid = unit.grid;

            // 1. 地形固有伤害
            grid.ApplyTerrainDamage(unit);

            // 检查单位是否已被消灭
            if (!IsInstanceValid(unit) || unit.health <= 0) continue;

            // 2. 格子自定义攻击（经防御判定）
            grid.ApplyGridCustomAttack(unit);

            if (!IsInstanceValid(unit) || unit.health <= 0) continue;

            // 2.5 ✦优化：格子最大值变化（血量/弹药/燃油上限，先于实际值变化）
            grid.ApplyGridMaxStatChanges(unit);

            if (!IsInstanceValid(unit) || unit.health <= 0) continue;

            // 3. 格子自定义固定伤害（不走防御，可为负回血）
            grid.ApplyGridCustomDamage(unit);

            if (!IsInstanceValid(unit) || unit.health <= 0) continue;

            // 4. 格子弹药变化（固定值加减，无判定）
            grid.ApplyGridAmmoChange(unit);

            // 5. 格子燃料变化（固定值加减，无判定）
            grid.ApplyGridFuelChange(unit);

            // 6. 锁血判定
            grid.LockHealthIfNeeded(unit);
        }

        // 更新统计（可能有单位死亡）
        UpdateUnitLists();
        CheckVictoryCondition();

    }


    // 更新回合标签
    public void UpdateTurnLabel()
    {
        if (TurnLabel != null)
        {
            TurnLabel.Text = $"当前回合：{turns} - 阶段：{turnPhase}";
        }
    }

    // 判断当前阶段是否允许该阵营移动
    public bool IsTurnPhaseValid(string team)
    {
        return TeamHelper.CanOperateTeam(turnPhase, team);
    }
}
