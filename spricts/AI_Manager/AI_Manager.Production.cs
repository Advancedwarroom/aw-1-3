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

// AI_Manager · Production（由 AI_Manager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class AI_Manager
{

    // ==================== 阶段 2：生产 ====================
    private void StepProduction()
    {
        BuildContext();
        Produce();
        stage = 3;
        handled.RemoveWhere(u => u == null || !IsInstanceValid(u));
        Next(stepDelay);
    }

    // ==================== 生产 ====================
    private void Produce()
    {
        var gm = gameManager;
        int funds = gm.GetTeamFunds(aiTeam);
        var facilities = gm.gridManager.grids
            .Where(g => g?.city != null && g.city.canProduce && (g.city.facilityTeam == aiTeam || g.city.isPublicFacility)
                        && g.city.CanProduceNow() && g.infantries.Count == 0 && g.weapons.Count == 0
                        && g.city.producibleUnitNames != null && g.city.producibleUnitNames.Count > 0)
            .OrderBy(g => ctx.enemies.Count == 0 ? 0 : ctx.enemies.Min(e => Dist(g, e.grid)))   // 前线工厂优先
            .ToList();
        if (facilities.Count == 0) { Log($"生产：无可用工厂 资金{funds}"); return; }

        var myCounts = ctx.mine.GroupBy(u => u.UnitTypeId).ToDictionary(x => x.Key, x => x.Count());
        // 占领兵计数包含正在运输中的
        int capturers = (gm.unitManager?.AllUnits ?? new List<Infantry>())
            .Count(u => u != null && IsInstanceValid(u) && u.team == aiTeam && u.health > 0 && u.captureAbility != CaptureAbility.CannotCapture);
        int income = Math.Max(1000, gm.gridManager.grids.Count(x => x?.city != null && x.city.facilityTeam == aiTeam) * 1000);
        var known = ctx.enemies.Select(e => (e, e.health)).Concat(ctx.remembered.Select(r => (r.u, r.hp))).ToList();

        for (int i = 0; i < facilities.Count; i++)
        {
            var fg = facilities[i];
            int remainingFacilities = facilities.Count - i - 1;
            string bestName = null; float bestScore = float.NegativeInfinity;
            float saveScore = float.NegativeInfinity; string saveName = null;
            var dbg = new List<string>();
            foreach (var name in fg.city.producibleUnitNames)
            {
                if (!UnitProductionDatabase.HasUnit(name)) continue;
                int cost = fg.city.GetProductionCost(UnitProductionDatabase.GetInfo(name).Cost, name);
                if (cost <= 0) continue;
                if (cost > funds)
                {
                    // 攒钱候选：两回合内买得起
                    if (cost <= funds + income * 2)
                    {
                        float ss = ProductionScore(name, cost, known, myCounts, capturers);
                        if (ss > saveScore) { saveScore = ss; saveName = name; }
                    }
                    continue;
                }
                float s = ProductionScore(name, cost, known, myCounts, capturers);
                // 钱多工厂少：每个工厂一回合只能出一个，钱越宽裕越该买贵的好兵
                s += cost * 0.35f * Math.Min(1f, funds / (float)(remainingFacilities + 1) / 15000f);
                // 留够钱给后面的工厂至少出一个步兵（需要占领时）
                if (ctx.capturables.Count > capturers && remainingFacilities > 0 && funds - cost < 1000 * Math.Min(remainingFacilities, 2)) s *= 0.7f;
                dbg.Add($"{name}:{s:F0}");
                if (s > bestScore) { bestScore = s; bestName = name; }
            }
            // 闲钱=零价值：只要不是明显亏本就出兵；钱攒得越多门槛越低
            float threshold = -3000f - funds * 0.1f;
            if (bestName != null && bestScore < 0 && saveName != null && saveScore > bestScore + 1500
                && remainingFacilities == 0 && ctx.capturables.Count <= capturers)
            { Log($"生产：{fg.GridIndex} 攒钱买 {saveName}（{saveScore:F0} > {bestName} {bestScore:F0}）"); continue; }
            if (bestName == null || bestScore < threshold) { Log($"生产：{fg.GridIndex} 不出兵 资金{funds} [{string.Join(",", dbg)}]"); continue; }
            if (gm.AIProduceUnit(fg.city, fg, bestName, aiTeam))
            {
                funds = gm.GetTeamFunds(aiTeam);
                myCounts[bestName] = myCounts.GetValueOrDefault(bestName) + 1;
                var cfg = UnitRegistry.Get(bestName);
                if (cfg != null && cfg.CaptureAbility != CaptureAbility.CannotCapture) capturers++;
                Log($"生产 {bestName} @{fg.GridIndex} 分{bestScore:F0} 余{funds}");
            }
        }
    }

    private float ProductionScore(string name, int cost, List<(Infantry e, int hp)> known, Dictionary<string, int> myCounts, int capturers)
    {
        var cfg = UnitRegistry.Get(name);
        float s = 0;

        // 1) 对敌军的攻防交换（查攻防表；有同类在场时用它的实例表）
        if (known.Count > 0)
        {
            float trade = 0;
            foreach (var (e, hp) in known)
            {
                if (e == null || !IsInstanceValid(e)) continue;
                // 按兵种ID+参照兵种查表（自定义兵种同样适用）；优先用场上同类/场景原型的实例攻防表
                var proto = ctx.mine.FirstOrDefault(m => m.UnitTypeId == name) ?? GetPrototype(name);
                string tpl = proto?.TemplateId ?? name;
                int dealt = Math.Max(Infantry.LookupDefaultMatrix(name, tpl, e.UnitTypeId, e.TemplateId), Infantry.LookupDefaultMatrix(name, tpl, e.UnitTypeId, e.TemplateId, true));
                if (proto != null) dealt = Math.Max(proto.GetPrimaryDamageFromMatrix(e), proto.GetSecondaryDamageFromMatrix(e));
                if (e.isStealthed && Array.IndexOf(e.stealthAttackableBy ?? Array.Empty<string>(), name) < 0) dealt = 0;
                int taken = Math.Max(Infantry.LookupDefaultMatrix(e.UnitTypeId, e.TemplateId, name, tpl), Infantry.LookupDefaultMatrix(e.UnitTypeId, e.TemplateId, name, tpl, true));
                if (proto != null) taken = Math.Max(taken, Math.Max(e.GetMatrixDamage(proto, false), e.GetMatrixDamage(proto, true)));
                trade += dealt / 100f * Math.Max(e.cost, 1000) * (hp / (float)e.maxHealth) - taken / 100f * cost * 0.6f;
            }
            s += trade / known.Count * 2.2f;
        }
        else
        {
            s += cost * 0.15f;   // 没情报：偏向均衡
        }

        // 2) 占领需求
        bool canCap = cfg != null && cfg.CaptureAbility != CaptureAbility.CannotCapture;
        int wantCap = Math.Min(ctx.capturables.Count, 2 + ctx.capturables.Count / 3);
        if (canCap && capturers < wantCap) s += 2600 - cost * 0.3f;
        if (canCap && capturers >= wantCap + 2) s -= 1200;

        // 3) 功能需求
        if (cfg != null)
        {
            if (cfg.CanIlluminate && ctx.fogOn && myCounts.GetValueOrDefault(name) == 0) s += 1800;
            if (cfg.CanTransportUnits && myCounts.GetValueOrDefault(name) == 0 && capturers >= 2 && ctx.capturables.Count >= 4) s += 900;
            // 纯运输（无武器）：已有运输或没有可运的占领兵时，不再买
            if (cfg.CanTransportUnits && !cfg.HasPrimaryWeapon && !cfg.HasSecondaryWeapon)
            {
                int transports = ctx.mine.Count(m => m.canTransportUnits && !m.hasPrimaryWeapon && !m.hasSecondaryWeapon);
                if (transports >= 1 + capturers / 4 || capturers == 0) s -= 4000;
            }
            if (cfg.CanSupplyUnits && ctx.mine.Count(m => SupplyNeed(m) > 500) >= 2 && myCounts.GetValueOrDefault(name) == 0) s += 1200;
            if (cfg.AttackType == AttackType.NoAttack && !cfg.CanIlluminate && !cfg.CanTransportUnits && !cfg.CanSupplyUnits) s -= 2000;
        }
        // 能力驱动：候选兵种可隐身、且已知敌军里没有它白名单内的兵种 → 对面没反隐手段
        var pr = GetPrototype(name);
        if (pr != null && pr.canStealth && known.Count > 0)
        {
            var by = pr.stealthAttackableBy ?? Array.Empty<string>();
            if (!known.Any(k => Array.IndexOf(by, k.e.UnitTypeId) >= 0)) s += 2500;
        }

        // 4) 多样性：同兵种越多越不想要
        int same = myCounts.GetValueOrDefault(name);
        s -= same * cost * 0.15f + (same >= 3 ? (same - 2) * 800 : 0);

        // 5) 高价兵要有对应价值
        if (known.Count == 0 && cost > 15000) s -= 2000;
        return s;
    }
    private static Infantry GetPrototype(string name)
    {
        if (prototypes.TryGetValue(name, out var cached)) return cached;
        Infantry inst = null;
        if (UnitProductionDatabase.Units.TryGetValue(name, out var info))
        {
            var scene = GD.Load<PackedScene>(info.ScenePath);
            if (scene != null) inst = scene.Instantiate() as Infantry;
        }
        prototypes[name] = inst;   // 不入场景树，_Ready 不执行，只读检查器字段
        return inst;
    }
}
