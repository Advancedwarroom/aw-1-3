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

// AI_Manager · Weapons（由 AI_Manager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class AI_Manager
{

    // ==================== 阶段 0：兵器 ====================
    private void StepWeapons()
    {
        while (weaponQueue.Count > 0)
        {
            var w = weaponQueue.Dequeue();
            if (w == null || !IsInstanceValid(w) || w.isDestroyed) continue;
            // 黑炮/巨炮/死光自带 AI 只服务 P0/P-1，阵营兵器由这里按"能力"操作；激光自带 AI 不设阵营门，直接用
            if (w is Crystal) continue;   // 黑水晶等单位走位完再脉冲（见 PulseCrystals）
            // ✅ 阵营兵器AI用AI_Manager操作（带视野）；P0/P-1兵器走自带AI
            bool acted = w.OperateByAI(this);
            Next(acted ? stepDelay : 0.05f);
            return;
        }
        stage = 1;
        Next(0.05f);
    }

    /// <summary>己方黑水晶：所有单位走位完成后再脉冲，净收益为正才用（避免给敌人回血）</summary>
    private void PulseCrystals()
    {
        foreach (var c in (gameManager.weaponManager?.AllWeapons ?? new List<Weapon>()).OfType<Crystal>().ToList())
        {
            if (!IsInstanceValid(c) || c.isDestroyed || c.team != aiTeam || !c.CanAttack()) continue;
            float v = 0; int supplyCount = 0;
            foreach (var u in gameManager.unitManager.AllUnits)
            {
                if (u == null || !IsInstanceValid(u) || u.grid == null || u.isTransported || u.health <= 0) continue;
                float sign = u.team == aiTeam ? 1f : TeamHelper.IsEnemyForAttacker(aiTeam, u.team) ? -1f : 0f;
                if (c.AIInHealRange(u.grid)) v += sign * c.AIPreviewHealDelta(u) / (float)Math.Max(1, u.maxHealth) * UnitValue(u);
                if (c.AIInSupplyRange(u.grid) && c.AIWouldSupply(u)) { supplyCount++; v += sign * 300; }
            }
            if (supplyCount > 0 && c.AISupplyBlockedByAmmo(supplyCount)) Log($"黑水晶@{c.grid?.GridIndex} 弹药不足，本次补给会被跳过");
            if (v <= 0) { Log($"黑水晶@{c.grid?.GridIndex} 不脉冲（净收益{v:F0}）"); continue; }
            c.ExecuteAI();
            Log($"黑水晶@{c.grid?.GridIndex} 脉冲 治疗/补给 净收益{v:F0}");
        }
    }

    /// <summary>单位站在 g 时能从会作用到我方的黑水晶拿到的收益（回血价值 + 补给需求）</summary>
    private float CrystalBonus(Infantry u, Grids g)
    {
        float b = 0;
        foreach (var c in ctx.crystals)
        {
            if (c.AIInHealRange(g)) b += c.AIPreviewHealDelta(u) / (float)Math.Max(1, u.maxHealth) * UnitValue(u);
            if (c.AIInSupplyRange(g) && c.AIWouldSupply(u)) b += 200 + SupplyNeed(u) * 0.5f;
        }
        return b;
    }

    private IEnumerable<Grids> CrystalTilesFor(Infantry u)
    {
        foreach (var c in ctx.crystals)
        {
            bool heal = c.AIPreviewHealDelta(u) > 0, sup = c.AIWouldSupply(u);
            if (!heal && !sup) continue;
            foreach (var g in Ring(c.grid, 0, Math.Max(c.crystalHealMaxRange, c.crystalSupplyMaxRange)))
                if ((heal && c.AIInHealRange(g)) || (sup && c.AIInSupplyRange(g))) yield return g;
        }
    }

    public bool RunBuiltinWeaponAI(Weapon w)
    {
        bool before = w.hasActed; int ra = w.remainingAttacks;
        w.ExecuteAI();
        Log($"兵器 {w.GetType().Name}@{w.grid?.GridIndex} 自带AI 执行（已行动={w.hasActed} 剩余次数{ra}→{w.remainingAttacks}）");
        return w.hasActed != before || w.remainingAttacks != ra;
    }

    /// <summary>
    /// 阵营炮台 AI：遍历可转向的所有朝向，算出每个朝向下最佳开火方案（单体炮取最佳格，死光取整条光束），
    /// 选最高分转过去开火；无利可图则恢复原朝向不动。
    /// </summary>
    public bool OperateCannon(BlackCannon bc)
    {
        if (bc.isDestroyed || !bc.CanAttack()) { Log($"兵器 {bc.GetType().Name}@{bc.grid?.GridIndex} 不可攻击（冷却/弹药/已行动）"); return false; }
        BuildContext();
        var origDir = bc.direction;
        float bestScore = 0; BlackCannon.CannonDirection bestDir = origDir; Grids bestGrid = null; string bestNote = "";

        var tried = new HashSet<BlackCannon.CannonDirection>();
        for (int r = 0; r < 4; r++)
        {
            if (!tried.Add(bc.direction)) break;   // 不能转向时只评估当前朝向
            var range = bc.CalculateAttackRange() ?? new List<Grids>();
            if (bc is DeathRay dr)
            {
                // 光束一次打中射程内全部敌方单位（死光自身只打敌方）
                float s = 0; int n = 0;
                foreach (var g in range)
                    foreach (var t in g.infantries)
                    {
                        if (t == null || !IsInstanceValid(t) || t.team == aiTeam || !CanSee(t)) continue;
                        if (!TeamHelper.IsEnemyForAttacker(aiTeam, t.team)) continue;
                        s += CannonHitValue(t, Mathf.Max(1, Mathf.RoundToInt(t.maxHealth * dr.fixedDamagePercent)), dr.CanDestroy); n++;
                    }
                if (s > bestScore && range.Count > 0) { bestScore = s; bestDir = bc.direction; bestGrid = range[0]; bestNote = $"光束命中{n}"; }
            }
            else
            {
                foreach (var g in range)
                {
                    if (!SeesGrid(g)) continue;
                    var tgt = bc.AIPeekTargetInGrid(g);
                    float s = 0; string note = null;
                    if (tgt is Infantry t && IsInstanceValid(t))
                    {
                        if (t.team != aiTeam && !CanSee(t)) continue;
                        s = CannonEffectValue(bc, t); note = $"{t.GetType().Name}@{g.GridIndex}";
                    }
                    else if (tgt is Weapon tw && IsInstanceValid(tw) && TeamHelper.IsEnemyForAttacker(aiTeam, tw.team))
                    {
                        float pct = bc.useModifiedDamage ? bc.modifiedHealthPercent : bc.fixedDamagePercent;
                        if (pct <= 0) continue;
                        int dmg = Mathf.RoundToInt(tw.maxHealth * pct);
                        s = Math.Min(dmg, tw.health) / (float)Math.Max(1, tw.maxHealth) * Math.Max(8000, tw.cost);
                        if (bc.CanDestroy && dmg >= tw.health) s += Math.Max(8000, tw.cost) * killBonusRatio;
                        note = $"兵器{tw.GetType().Name}@{g.GridIndex}";
                    }
                    if (s > bestScore) { bestScore = s; bestDir = bc.direction; bestGrid = g; bestNote = note; }
                }
            }
            if (!bc.canRotate) break;
            bc.RotateDirection();
        }

        // 转到最佳朝向（没有目标则恢复原朝向）
        var want = bestGrid != null ? bestDir : origDir;
        for (int i = 0; i < 4 && bc.direction != want; i++) bc.RotateDirection();
        if (bestGrid == null) { Log($"兵器 {bc.GetType().Name}@{bc.grid?.GridIndex} 四向无可打目标"); return false; }

        bool fired;
        if (bc is DeathRay ray) { int b = ray.remainingAttacks; ray.PerformAttack(null); fired = ray.remainingAttacks < b || ray.hasActed; }
        else fired = bc.AIFireAtGrid(bestGrid);
        if (fired)
        {
            Log($"兵器 {bc.GetType().Name}@{bc.grid?.GridIndex} 朝{bc.direction} 开火 {bestNote} 分{bestScore:F0}");
            if (followCamera) FocusCamera(bestGrid);
        }
        return fired;
    }

    /// <summary>黑炮/巨炮对单个单位的效果价值：敌方掉血为正，己方回血为正，误伤为负</summary>
    private float CannonEffectValue(BlackCannon bc, Infantry t)
    {
        float pct = bc.useModifiedDamage ? bc.modifiedHealthPercent : bc.fixedDamagePercent;
        bool enemy = TeamHelper.IsEnemyForAttacker(aiTeam, t.team);
        if (pct > 0)
        {
            int dmg = Mathf.Max(1, Mathf.RoundToInt(t.maxHealth * pct));
            float v = CannonHitValue(t, dmg, bc.CanDestroy);
            return enemy ? v : (t.team == aiTeam ? -v : 0);
        }
        if (bc.useModifiedDamage && pct == 0)
        {
            // 纯弹药/燃料效果：正=扣，负=补
            float v = 0;
            if (t.maxPrimaryAmmo > 0 && bc.modifiedAmmoPercent != 0)
            {
                int a = Mathf.RoundToInt(t.maxPrimaryAmmo * Math.Abs(bc.modifiedAmmoPercent));
                int real = bc.modifiedAmmoPercent > 0 ? Math.Min(a, t.currentPrimaryAmmo) : Math.Min(a, t.maxPrimaryAmmo - t.currentPrimaryAmmo);
                v += real / (float)t.maxPrimaryAmmo * UnitValue(t) * 0.3f;
            }
            if (t.consumeFuel && t.maxFuel > 0 && bc.modifiedFuelPercent != 0)
            {
                int f = Mathf.RoundToInt(t.maxFuel * Math.Abs(bc.modifiedFuelPercent));
                int real = bc.modifiedFuelPercent > 0 ? Math.Min(f, t.fuel) : Math.Min(f, t.maxFuel - t.fuel);
                v += real / (float)t.maxFuel * UnitValue(t) * 0.2f;
                if (bc.modifiedFuelPercent > 0 && t.fuel - f <= 0 && t.destroyOnOutOfFuel) v += UnitValue(t) * 0.8f;   // 油耗尽坠毁
            }
            bool drain = bc.modifiedAmmoPercent > 0 || bc.modifiedFuelPercent > 0;
            if (enemy) return drain ? v : -v;
            if (t.team == aiTeam) return drain ? -v : v;
            return 0;
        }
        if (pct < 0 && t.team == aiTeam)   // 治疗炮：给最缺血的己方单位
        {
            int heal = Mathf.RoundToInt(t.maxHealth * -pct);
            int room = bc.canOverMaxHp ? heal : Math.Max(0, t.maxHealth - t.health);
            return Math.Min(heal, room) / (float)t.maxHealth * UnitValue(t) * 0.8f;
        }
        return 0;
    }

    private float CannonHitValue(Infantry t, int dmg, bool canDestroy)
    {
        int real = canDestroy ? Math.Min(dmg, t.health) : Math.Min(dmg, Math.Max(0, t.health - 1));
        float v = real / (float)Math.Max(1, t.maxHealth) * UnitValue(t);
        if (canDestroy && dmg >= t.health) v += UnitValue(t) * killBonusRatio;
        if (t.capturingGrid != null && t.currentCaptureProgress > 0) v += 800;   // 打断占领
        return v;
    }

    // ========== 新兵器 AI 操作（黑色弧光/大雷弹/超级兵工厂）==========

    /// <summary>黑色弧光 AI：射程内找天基打击收益最大的目标格（只用本势力视野内的目标计分）</summary>
    public bool OperateArc(BlackArc arc)
    {
        if (arc == null || !IsInstanceValid(arc) || arc.isDestroyed) return false;
        if (!arc.arcStrikeEnabled || arc.hasActed || !arc.ArcStrikeReady) return false;
        if (!TeamHelper.ShouldWeaponUseAI(arc.team)) return false;   // 玩家方兵器不在此操作
        if (gameManager?.gridManager == null) return false;
        var best = arc.AIBestStrikeGrid(gameManager, u => IsInstanceValid(u) && CanSee(u));
        if (best == null) return false;
        arc.PerformArcStrike(best, gameManager);
        return true;
    }

    /// <summary>大雷弹 AI：蠕动压敌/逼近 + 绝境模式生产Oozium（只用本势力视野内的目标计分）</summary>
    public bool OperateGrandBolt(GrandBolt gb)
    {
        if (gb == null || !IsInstanceValid(gb) || gb.isDestroyed) return false;
        if (!TeamHelper.ShouldWeaponUseAI(gb.team)) return false;   // 玩家方兵器不在此操作
        if (gameManager?.gridManager == null) return false;
        gb.AIWriggleAndSpawn(gameManager, u => IsInstanceValid(u) && CanSee(u));
        return true;
    }

    /// <summary>超级兵工厂 AI：反制选兵（同城市生产评分），能产就产（出口被占满/次数用完为止）</summary>
    public bool OperateFactory(SuperFactory f)
    {
        if (f == null || !IsInstanceValid(f) || f.isDestroyed || !f.CanProduceNow()) return false;
        if (!TeamHelper.ShouldWeaponUseAI(f.team)) return false;   // 玩家方工厂由玩家操作
        if (gameManager?.unitManager == null) return false;
        var myCounts = ctx.mine.GroupBy(u => u.UnitTypeId).ToDictionary(x => x.Key, x => x.Count());
        int capturers = (gameManager.unitManager.AllUnits ?? new List<Infantry>())
            .Count(u => u != null && IsInstanceValid(u) && u.team == aiTeam && u.health > 0 && u.captureAbility != CaptureAbility.CannotCapture);
        var known = ctx.enemies.Select(e => (e, e.health)).Concat(ctx.remembered.Select(r => (r.u, r.hp))).ToList();
        bool produced = false;
        while (f.CanProduceNow())
        {
            var exits = f.GetProductionExitGrids()
                .Where(g => g != null && IsInstanceValid(g) && g.infantries.Count == 0 && g.weapons.Count == 0).ToList();
            if (exits.Count == 0) break;
            var names = f.producibleUnitNames.Count > 0 ? f.producibleUnitNames.ToList()
                                                       : UnitProductionDatabase.GetAllUnitNames().ToList();
            string best = null; float bestScore = float.NegativeInfinity;
            foreach (var name in names)
            {
                if (!UnitProductionDatabase.HasUnit(name)) continue;
                int cost = f.GetProductionCost(UnitProductionDatabase.GetInfo(name).Cost, f.instantMoveAfterProduction, name);
                // 免费/红利期价格为0，仍是合法候选；估价与真实生产保持一致。
                if (cost < 0) continue;
                if (!f.productionFree && cost > gameManager.GetTeamFunds(aiTeam)) continue;
                if (f.isUpdatable && !UnitProductionWhitelist.CanProduce(name)) continue;
                float s = ProductionScore(name, cost, known, myCounts, capturers);
                if (s > bestScore) { bestScore = s; best = name; }
            }
            if (best == null) break;
            string err = f.ProduceUnitAt(best, exits[0], f.instantMoveAfterProduction, aiTeam);
            if (!string.IsNullOrEmpty(err)) break;
            produced = true;
        }
        return produced;
    }

    // ========== 导弹井（单位踩井发射的免费动作）==========

    /// <summary>导弹井 AI：单位行动前先发射脚下的井（LaunchMissile 不消耗单位行动）。目标=打击面内可见敌人价值最大、不误伤己方的格子</summary>
    private void TryFireSilo(Infantry u)
    {
        if (u == null || !IsInstanceValid(u) || u.grid == null) return;
        foreach (var w in u.grid.weapons)
        {
            if (w is not MissileSilo silo || !IsInstanceValid(silo)) continue;
            if (!silo.CanBeFiredBy(u)) continue;
            var target = BestSiloTarget(silo);
            if (target != null)
            {
                Log($"{u.GetType().Name}@{u.grid.GridIndex} 发射导弹井 → {target.GridIndex}");
                silo.LaunchMissile(target, u);
            }
            return;   // 一格一井
        }
    }

    private Grids BestSiloTarget(MissileSilo silo)
    {
        if (gameManager?.gridManager == null || silo.grid == null) return null;
        Grids best = null; float bestScore = 0;
        int sx = silo.grid.GridIndex.X, sy = silo.grid.GridIndex.Y;
        foreach (var cand in gameManager.gridManager.grids)
        {
            if (cand == null) continue;
            int d = Mathf.Abs(cand.GridIndex.X - sx) + Mathf.Abs(cand.GridIndex.Y - sy);
            if (d < silo.strikeMinRange || d > silo.strikeMaxRange) continue;
            if (silo.IsScreenBlocked(cand)) continue; // ✅ N屏：AI 选弹着点默认仅限己图
            float score = 0;
            foreach (var g in gameManager.gridManager.grids)
            {
                int bd = Mathf.Abs(g.GridIndex.X - cand.GridIndex.X) + Mathf.Abs(g.GridIndex.Y - cand.GridIndex.Y);
                if (bd < silo.blastMinRadius || bd > silo.blastMaxRadius) continue;
                foreach (var e in g.infantries)
                {
                    if (e == null || !IsInstanceValid(e) || e.health <= 0) continue;
                    if (!silo.TargetUnitTeamOk(e.team)) continue;              // 打击政策外不受影响
                    bool enemy = TeamHelper.IsEnemyForAttacker(aiTeam, e.team);
                    if (enemy && !CanSee(e)) continue;                        // 雾中敌人不计分（公平性）
                    if (!enemy) { score -= 400; continue; }                    // 误伤己方/友军
                    int dmg = -silo.effectHealthDelta;                        // 负值=伤害
                    if (dmg <= 0) continue;
                    int real = Math.Min(dmg, e.health);
                    score += real / (float)Math.Max(1, e.maxHealth) * UnitValue(e);
                    if (dmg >= e.health) score += UnitValue(e) * 0.5f;         // 击杀奖励
                }
                if (silo.affectsWeapons)
                {
                    foreach (var w in g.weapons)
                    {
                        if (w == null || !IsInstanceValid(w) || w.isDestroyed) continue;
                        if (!silo.TargetTeamOk(w.team)) continue;
                        if (!TeamHelper.IsEnemyForAttacker(aiTeam, w.team)) { score -= 300; continue; }
                        score += Mathf.Max(300f, w.cost * 0.3f);
                    }
                }
            }
            if (score > bestScore) { bestScore = score; best = cand; }
        }
        return best;
    }
}
