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

// AI_Manager · Units（由 AI_Manager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class AI_Manager
{

    // ==================== 阶段 1/3：单位 ====================
    private void StepUnits()
    {
        BuildContext();
        var pending = ctx.mine.Where(u => !handled.Contains(u) && u.state != UnitState.Acted && !u.hasActed).ToList();
        if (pending.Count == 0)
        {
            if (stage == 1) PulseCrystals();
            stage = stage == 1 ? 2 : 4;
            Next(0.05f);
            return;
        }

        // 每个待行动单位都评估一次，挑出最值得先执行的方案
        Plan best = null;
        float bestKey = float.NegativeInfinity;
        foreach (var u in pending)
        {
            var p = BestPlan(u);
            if (p == null) { Log($"{u.GetType().Name}@{u.grid?.GridIndex} 无可行方案"); continue; }
            float key = p.act == Act.Illuminate ? 2_000_000f + p.score : p.tactical ? 1_000_000f + p.score : -RoleOrder(u) * 10_000f - DistToFront(u);
            if (key > bestKey) { bestKey = key; best = p; }
        }

        if (best == null)
        {
            foreach (var u in pending) { handled.Add(u); TryFireSilo(u); FinishUnit(u); }
            Next(0.05f);
            return;
        }

        handled.Add(best.unit);
        TryFireSilo(best.unit);   // ✅ 行动前先发射脚下的导弹井（免费动作，不消耗单位行动）
        Log($"{best.unit.GetType().Name}@{best.unit.grid?.GridIndex} → {best.act} 目的地{best.dest?.GridIndex} 分数{best.score:F0} {best.note}");
        ExecutePlan(best);
    }

    private int RoleOrder(Infantry u)
    {
        if (u.canIlluminate && u.currentFlareAmmo > 0 && ctx.fogOn) return 0;
        if (IsIndirect(u)) return 1;
        if (u.canTransportUnits && u.transportedUnits.Count > 0) return 2;
        if (u.attackType != AttackType.NoAttack && u.captureAbility == CaptureAbility.CannotCapture) return 3;
        if (u.captureAbility != CaptureAbility.CannotCapture) return 4;
        if (u.canTransportUnits) return 5;
        return 6;   // 补给/修理最后（等别人站好位）
    }

    private float DistToFront(Infantry u)
    {
        if (u.grid == null || ctx.enemies.Count == 0) return 0;
        return ctx.enemies.Min(e => Dist(u.grid, e.grid));
    }
}
