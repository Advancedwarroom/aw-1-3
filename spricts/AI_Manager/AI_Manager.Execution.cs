// AI_Manager.cs —— 战棋 AI（效用评估 + 威胁图 + 迷雾记忆 + 全技能）
//
// 决策流程（每个 AI 小回合）：
//   1. 兵器（Weapon.ExecuteAI）逐个行动
//   2. 战术阶段：每一步为所有未行动单位重新评估"最佳方案"，先执行高价值战术（击杀/攻击/自爆/照明/完成占领/合流/修理）
//   3. 机动阶段：剩余单位按 照明→远程→近战→占领→运输→补给/修理 的顺序推进或撤退
//   4. 生产：按敌军构成反制选兵 + 占领需求 + 兵种多样性
//   5. 立即可动的新兵再走一轮机动
//
// 公平性：只用本势力视野（CalculateGlobalVisionPool + 照明弹 + 隐身规则 IsVisibleTo），不读取雾中/隐身单位。
//        被雾中或隐身敌军挡路时与玩家一样触发截断（遭遇）。
// 对外接口保持不变：gameManager / p1AIEnabled / p2AIEnabled / autoEndTurn / IsAITeam / IsCurrentPhaseAI / OnPhaseStart
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// AI_Manager · Execution（由 AI_Manager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class AI_Manager
{

    // ==================== 执行 ====================
    private async void ExecutePlan(Plan p)
    {
        isBusy = true;
        var u = p.unit;
        float extra = 0;
        try
        {
            if (p.preReveal && u.isStealthed && CanToggle(u)) u.ToggleStealth();

            bool ambushed = false;
            if (p.tpVia != null)
            {
                if (p.tpVia != u.grid)
                {
                    var (viaFinal, viaAmb, viaSteps) = MoveAlong(u, p.path, false);
                    ambushed = viaAmb || viaFinal != p.tpVia;
                    extra = viaSteps * moveStepTime;
                    if (extra > 0)
                    {
                        await ToSignal(GetTree().CreateTimer(extra), SceneTreeTimer.SignalName.Timeout);
                        if (!IsInstanceValid(this) || !isRunningAI) return;
                    }
                    if (!IsInstanceValid(u)) return;
                }
                if (!ambushed && !TryTeleport(u, p.dest)) ambushed = true;
            }
            else if (p.dest != null && p.dest != u.grid)
            {
                var (final, amb, steps) = MoveAlong(u, p.path, p.act == Act.Load || p.act == Act.Merge);
                ambushed = amb || final != p.dest;
                extra = steps * moveStepTime;
                if (extra > 0)
                {
                    await ToSignal(GetTree().CreateTimer(extra), SceneTreeTimer.SignalName.Timeout);
                    if (!IsInstanceValid(this) || !isRunningAI) return;
                }
                if (!IsInstanceValid(u)) return;
            }

            if (ambushed)
            {
                Log("  遭遇伏击，行动中止");
                FinishUnit(u);
                return;
            }
            DoAction(p);
            if (IsInstanceValid(u) && p.act == Act.Wait) ClaimCaptureTarget(u);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[AI] 执行方案出错: {e}");
            if (IsInstanceValid(u)) FinishUnit(u);
        }
        finally
        {
            isBusy = false;
            if (IsInstanceValid(gameManager)) gameManager.fogOfWarManager?.OnUnitMoved();
            Next(stepDelay);
        }
    }

    private void DoAction(Plan p)
    {
        var u = p.unit;
        var gm = gameManager;
        switch (p.act)
        {
            case Act.Attack:
                if (p.target != null && IsInstanceValid(p.target) && p.target.health > 0)
                {
                    u.Attack(p.target);
                    gm.gridManager?.HideAttackRange();
                    gm.CallDeferred(nameof(GameManager.UpdateUnitLists));
                }
                break;
            case Act.AttackWeapon:
                if (p.weaponTarget != null && IsInstanceValid(p.weaponTarget) && !p.weaponTarget.isDestroyed)
                    u.AttackTarget(p.weaponTarget);
                break;
            case Act.Capture:
                if (u.CanCaptureCurrentGrid()) u.PerformCapture();
                break;
            case Act.Merge:
                if (p.target != null && IsInstanceValid(p.target) && gm.CanMerge(u, p.target))
                {
                    gm.MergeUnits(u, p.target);
                    return;   // 源单位已被移除
                }
                break;
            case Act.Load:
                if (p.target != null && IsInstanceValid(p.target) && p.target.LoadUnit(u))
                {
                    MarkActed(u);
                    return;
                }
                break;
            case Act.Unload:
                foreach (var (cargo, to) in p.unloads)
                {
                    if (cargo == null || !IsInstanceValid(cargo) || !u.CanUnloadToGrid(to)) continue;
                    if (to.infantries.Any(o => o != null && IsInstanceValid(o) && !o.isTransported)) continue;
                    if (u.UnloadUnit(cargo, to)) { MarkActed(cargo); handled.Add(cargo); }
                }
                break;
            case Act.Supply:
                u.PerformSupply();
                break;
            case Act.Repair:
                if (p.target != null && IsInstanceValid(p.target))
                {
                    u.PerformRepair(p.target);   // 通用修理模块（任意开启 canRepair 的单位）
                }
                break;
            case Act.Illuminate:
                if (p.actGrid != null && u.currentFlareAmmo > 0 && u.grid != null)
                {
                    int launchDistance = u.MinAnchorDistance(p.actGrid.GridIndex);   // ✅ issue#1 二期
                    if (launchDistance >= u.minLaunchRange && launchDistance <= u.maxLaunchRange
                        && u.GetAnchorCells().Any(a => a != null && gm.gridManager.IsRangeAllowed(a, p.actGrid, u.flareLaunchCanCrossScreens)))
                    {
                        u.TryConsumeAmmo(Infantry.AmmoKind.Flare);
                        gm.fogOfWarManager?.AddIllumination(p.actGrid, u.minIlluminationRange, u.maxIlluminationRange, u.flareDurationTurns, u.flareEffectCanCrossScreens);
                        gm.fogOfWarManager?.RefreshFog();
                    }
                }
                break;
            case Act.Explode:
                gm.ExecuteExplosion(u);
                if (!IsInstanceValid(u) || u.IsQueuedForDeletion() || u.health <= 0) return;
                break;
            case Act.Hide:
                if (!u.isStealthed) u.ToggleStealth();
                break;
            case Act.Reveal:
                if (u.isStealthed) u.ToggleStealth();
                break;
        }

        if (!IsInstanceValid(u) || u.IsQueuedForDeletion() || u.health <= 0) return;
        if (p.postHide && !u.isStealthed && CanToggle(u)) u.ToggleStealth();
        FinishUnit(u);
    }

    /// <summary>沿路径移动；遇到看不见的敌军（雾/隐身）立即截断。返回(最终格, 是否遭遇, 移动格数)</summary>
    private (Grids final, bool ambushed, int steps) MoveAlong(Infantry u, List<Grids> path, bool joining)
    {
        var start = u.grid;
        if (path == null || path.Count < 2) return (start, false, 0);
        int stop = path.Count - 1;
        bool ambushed = false;
        for (int i = 1; i < path.Count; i++)
        {
            var n = path[i];
            if (n.infantries.Any(o => Infantry.IsTruncationBlocker(u, o)))
            {
                stop = i - 1; ambushed = true; break;
            }
        }
        // 终点需要能站（合流/上车终点由后续动作处理）
        if (!(joining && !ambushed))
            while (stop > 0 && !RealStayable(u, path[stop])) { stop--; ambushed = true; }

        var final = path[stop];
        if (final == start) return (start, ambushed, 0);

        int cost = 0, fuelCost = 0;
        for (int i = 1; i <= stop; i++)
        {
            int c = Math.Max(1, StepCost(u, path[i]));
            cost += c;
            if (path[i].gridType != GridType.TP) fuelCost += c;
        }

        // 离开正在占领的据点 → 占领中断
        if (u.capturingGrid != null && u.capturingGrid != final)
        {
            u.capturingGrid.city?.OnCaptureInterrupted(u);
            u.currentCaptureProgress = 0;
            u.capturingGrid = null;
        }

        start.infantries.Remove(u);
        if (start.infantry == u) start.infantry = start.infantries.FirstOrDefault();
        u.grid = final;
        if (!final.infantries.Contains(u)) final.infantries.Add(u);
        if (final.infantry == null) final.infantry = u;

        u.movePoints = Math.Max(0, u.movePoints - cost);
        if (u.consumeFuel) u.ConsumeFuel(fuelCost);
        u.isMoved = true;
        u.state = UnitState.Moved;
        u.originalGrid = start;
        u.movedThisTurnForAutoRepair = true;   // 与 GridManager.MoveTo 一致：行动结束时触发 autoRepairAfterMove

        // 动画
        var tween = u.CreateTween();
        for (int i = 1; i <= stop; i++) tween.TweenProperty(u, "position", path[i].Position, moveStepTime);
        FollowCamera(u, final);
        return (final, ambushed, stop);
    }

    private void FollowCamera(Infantry u, Grids g)
    {
        if (!followCamera || !u.IsVisibleInTree()) return;
        var cam = GetViewport()?.GetCamera2D();
        if (cam == null) return;
        var t = cam.CreateTween();
        t.TweenProperty(cam, "position", g.Position, 0.25f).SetTrans(Tween.TransitionType.Sine);
    }

    /// <summary>修理能力参数（字段已统一到 Infantry 基类）</summary>
    private static (bool can, bool afterMove, int min, int max, int mode, int amount) RepairParams(Infantry u)
        => (u.CanPerformRepair, u.canRepairAfterMove, u.minRepairRange, u.maxRepairRange, u.repairTargetMode, u.repairAmountPerAction);

    private void FocusCamera(Grids g)
    {
        if (!followCamera || g == null) return;
        var cam = GetViewport()?.GetCamera2D();
        if (cam == null) return;
        cam.CreateTween().TweenProperty(cam, "position", g.Position, 0.25f).SetTrans(Tween.TransitionType.Sine);
    }

    private void MarkActed(Infantry u)
    {
        u.isMoved = true;
        u.isAttacked = true;
        u.state = UnitState.Acted;
        u.originalGrid = null;
    }

    private void FinishUnit(Infantry u)
    {
        if (u == null || !IsInstanceValid(u) || u.IsQueuedForDeletion()) return;
        if (u.state != UnitState.Acted) MarkActed(u);
        u.SetWaitVisual(true);
    }
}
