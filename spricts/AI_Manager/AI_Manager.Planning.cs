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

// AI_Manager · Planning（由 AI_Manager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class AI_Manager
{

    // ==================== 方案评估 ====================
    private Plan BestPlan(Infantry u)
    {
        if (u.grid == null) return null;
        var plans = new List<Plan>();

        bool stealthed = u.isStealthed;
        bool freeToggle = CanToggle(u) && !u.stealthToggleCostsAction;
        bool mayMove = Infantry.CanUnitMove(u) && u.CanMove() && !u.isMoved && u.state != UnitState.Moved;
        bool preRevealForMove = false;
        if (stealthed && !u.canMoveWhileStealthed)
        {
            if (freeToggle) preRevealForMove = true; else mayMove = false;
        }

        int mp = mayMove ? u.movePoints : 0;
        var reach = ComputeReach(u, u.grid, mp);
        var dests = new List<(Grids g, EndKind k, Infantry partner)>();
        foreach (var g in reach.cost.Keys)
        {
            var k = CanEnd(u, g, out var partner);
            if (k != EndKind.None) dests.Add((g, k, partner));
        }
        if (dests.Count == 0) dests.Add((u.grid, EndKind.Free, null));

        // 仍处于继续占领状态：评估“原地继续”
        foreach (var (g, kind, partner) in dests)
        {
            bool moved = g != u.grid;
            bool stealthAtDest = stealthed && !(preRevealForMove && moved);
            bool reveal = preRevealForMove && moved;

            if (kind == EndKind.Load) { plans.Add(EvalLoad(u, g, partner, reveal)); continue; }
            if (kind == EndKind.Merge) { plans.Add(EvalMerge(u, g, partner, reveal)); continue; }

            EvalAttacks(u, g, moved, stealthAtDest, reveal, freeToggle, plans);
            EvalCapture(u, g, moved, stealthAtDest, reveal, freeToggle, plans);
            EvalSupport(u, g, moved, stealthAtDest, reveal, freeToggle, plans);
            EvalStealthAndWait(u, g, moved, stealthAtDest, reveal, freeToggle, plans);
        }

        if (mayMove) EvalTeleports(u, dests, stealthed, preRevealForMove, freeToggle, plans);

        Plan best = null;
        foreach (var p in plans)
        {
            if (p == null || float.IsNegativeInfinity(p.score)) continue;
            p.unit = u;
            if (difficulty < 2) p.score += rng.RandfRange(-1f, 1f) * (difficulty == 0 ? 1500f : 400f);
            if (best == null || p.score > best.score) best = p;
        }
        if (best != null) best.tactical = best.act != Act.Wait && best.act != Act.Hide && best.act != Act.Reveal && best.act != Act.Load && best.score > 300;
        if (best != null) PathFor(best, reach);
        return best;
    }

    private void PathFor(Plan p, Reach r) { p.path = BuildPath(r, p.unit.grid, p.tpVia ?? p.dest); }

    private List<Grids> BuildPath(Reach r, Grids start, Grids dest)
    {
        var path = new List<Grids>();
        var cur = dest;
        int guard = 0;
        while (cur != null && guard++ < 500)
        {
            path.Add(cur);
            if (cur == start) break;
            if (!r.parent.TryGetValue(cur, out cur)) break;
        }
        path.Reverse();
        if (path.Count == 0 || path[0] != start) path.Insert(0, start);
        return path;
    }

    // —— 攻击（单位 & 兵器） ——
    private void EvalAttacks(Infantry u, Grids g, bool moved, bool stealthAtDest, bool reveal, bool freeToggle, List<Plan> plans)
    {
        if (u.attackType == AttackType.NoAttack) return;
        if (moved && !u.canAttackAfterMoving) return;
        if (u.isAttacked) return;
        bool needReveal = reveal;
        bool attStealthed = stealthAtDest;
        if (stealthAtDest && !u.canAttackWhileStealthed)
        {
            if (!freeToggle) return;
            needReveal = true; attStealthed = false;
        }
        bool stillStealthAfter = attStealthed && !u.stealthRevealOnAttack;
        int maxR = MaxRange(u);

        foreach (var e in ctx.enemies)
        {
            if (e.grid == null || !gameManager.gridManager.IsRangeAllowed(g, e.grid, u.attackCanCrossScreens)) continue;
            int d = Dist(g, e.grid);
            if (d > maxR || !CanHitAtDistance(u, d)) continue;
            if (ctx.fogOn && !Infantry.IsVisibleTo(e, u.team, null)) continue;   // ✅ 公平性：AI不攻击看不见的单位（含隐身+林地隐蔽）
            if (!Infantry.CanTargetStealth(u, e)) continue;
            int dmg = EstDamage(u, u.health, e, e.health, e.grid, false, attStealthed);
            if (dmg <= 0) continue;
            int dealt = Math.Min(dmg, e.health);
            bool kill = dmg >= e.health;
            int counter = EstCounter(u, g, u.health, attStealthed, e, dmg);
            int hpAfter = Math.Max(0, u.health - counter);

            float tw = TargetWeight(u, e);
            if (tw <= 0) continue;                                    // AI 档案：不打此类目标
            float score = dealt / (float)e.maxHealth * UnitValue(e) * tw;
            if (kill && u.aiPreferKill) score += UnitValue(e) * killBonusRatio * tw;
            if (u.aiFocusFire && e.health < e.maxHealth) score += (1f - e.health / (float)e.maxHealth) * UnitValue(e) * 0.15f * tw;
            if (e.capturingGrid != null && e.capturingGrid.city != null)
                score += (e.capturingGrid.city.facilityTeam == aiTeam ? 2500 : 800) + e.currentCaptureProgress * 60;
            if (e.canTransportUnits && e.transportedUnits.Count > 0 && kill) score += 1500;
            score -= counter / (float)u.maxHealth * UnitValue(u);
            if (hpAfter <= 0) score -= UnitValue(u);
            score -= ThreatLoss(u, g, hpAfter, stillStealthAfter, kill ? e : null);
            score += Terrain(u, g) * 200 + TerrainEffectScore(u, g); // ✦适配：格子自定义效果
            bool hideAfter = freeToggle && !stillStealthAfter && WantsHide(u, g, hpAfter);
            if (hideAfter) score += ThreatLoss(u, g, hpAfter, false, kill ? e : null) - ThreatLoss(u, g, hpAfter, true, kill ? e : null);
            plans.Add(new Plan { dest = g, act = Act.Attack, target = e, score = score, preReveal = needReveal, postHide = hideAfter, note = $"伤{dealt}{(kill ? " 击杀" : "")} 反{counter}" });
        }

        foreach (var w in ctx.enemyWeapons)
        {
            if (!u.CanTargetWeapon(w)) continue;
            var wt = u.CanUsePrimaryWeapon() ? WeaponType.Primary : (u.CanUseSecondaryWeapon() ? WeaponType.Secondary : WeaponType.None);
            if (wt == WeaponType.None) continue;
            Grids hitGrid = null;
            foreach (var wg in WeaponGrids(w))
            {
                if (!gameManager.gridManager.IsRangeAllowed(g, wg, u.attackCanCrossScreens)) continue;
                if (w.occupiedGrids.Count > 1 && !w.IsWeakPointGrid(wg)) continue;
                int d = Dist(g, wg);
                if (CanHitAtDistance(u, d)) { hitGrid = wg; break; }
            }
            if (hitGrid == null) continue;
            int dmg = u.CalculateDamageAgainstWeapon(w, wt);
            if (dmg <= 0) continue;
            float wv = Math.Max(w.cost, 8000) * (w.contributesToVictory ? 2f : 1f);
            float score = Math.Min(dmg, w.health) / (float)Math.Max(1, w.maxHealth) * wv;
            if (dmg >= w.health) score += wv * killBonusRatio;
            score -= ThreatLoss(u, g, u.health, stillStealthAfter);
            plans.Add(new Plan { dest = g, act = Act.AttackWeapon, weaponTarget = w, score = score, preReveal = needReveal, note = $"兵器伤{dmg}" });
        }
    }

    private IEnumerable<Grids> WeaponGrids(Weapon w)
    {
        if (w.occupiedGrids != null && w.occupiedGrids.Count > 0) return w.occupiedGrids.Where(x => x != null);
        return w.grid != null ? new[] { w.grid } : Array.Empty<Grids>();
    }

    // —— 占领 ——
    private void EvalCapture(Infantry u, Grids g, bool moved, bool stealthAtDest, bool reveal, bool freeToggle, List<Plan> plans)
    {
        if (u.captureAbility == CaptureAbility.CannotCapture || g.city == null) return;
        if (u.aiRole == 1) return;   // AI 档案：突击单位不占领
        if (moved && !u.allowCaptureAfterMove) return;
        if (!g.city.CanBeCapturedBy(u)) return;
        bool needReveal = reveal;
        if (stealthAtDest && !u.canCaptureWhileStealthed) { if (!freeToggle) return; needReveal = true; }
        if (ctx.captureClaims.TryGetValue(g, out var claimer) && claimer != u && IsInstanceValid(claimer)) return;

        int required = Math.Max(1, g.city.capturePointsRequired);
        int progress = (u.capturingGrid == g) ? u.currentCaptureProgress : 0;
        int power = Math.Max(1, u.GetCapturePower());
        int remaining = required - progress;
        int turns = (int)Math.Ceiling(remaining / (float)power);
        float val = FacilityValue(g.city);
        float score = turns <= 1 ? val * 1.2f : val * 0.9f / turns;
        if (progress > 0) score += 1200 + progress * 40;   // 已经占了一半，别半途而废
        float risk = ThreatLoss(u, g, u.health, false);
        score -= risk * 0.7f;                               // 占领本身值得冒点险
        score += Terrain(u, g) * 150 + TerrainEffectScore(u, g) * 0.5f; // ✦适配：格子自定义效果（占领权重减半）
        if (u.aiRole == 2) score += 1500;   // AI 档案：占点优先
        plans.Add(new Plan { dest = g, act = Act.Capture, score = score, preReveal = needReveal, note = $"占领 剩{turns}回合" });
    }

    private float FacilityValue(Facility f)
    {
        float v = 3000 + f.GetCurrentIncome() * 2.5f;
        if (f.canProduce) v += 5000;
        if (!string.IsNullOrEmpty(f.facilityTeam) && f.facilityTeam != TeamHelper.Player0 && IsEnemy(f.facilityTeam)) v *= 1.4f;   // 夺敌据点=双倍收益
        return v;
    }

    // —— 补给 / 修理 / 照明 / 自爆 / 卸载 ——
    private void EvalSupport(Infantry u, Grids g, bool moved, bool stealthAtDest, bool reveal, bool freeToggle, List<Plan> plans)
    {
        bool customLocked = stealthAtDest && !u.canCustomActionWhileStealthed;
        bool customReveal = reveal || (customLocked && freeToggle);
        bool customOk = !customLocked || freeToggle;

        // 补给
        if (u.canSupplyUnits && customOk)
        {
            float val = 0;
            foreach (var t in u.CalculateSupplyRange(g).SelectMany(x => x.infantries))
            {
                if (t == null || t == u || t.team != u.team || !IsInstanceValid(t)) continue;
                val += SupplyNeed(t);
            }
            if (val > 0)
                plans.Add(new Plan { dest = g, act = Act.Supply, score = val - ThreatLoss(u, g, u.health, stealthAtDest && !customReveal), preReveal = customReveal && customLocked, note = "补给" });
        }

        // 修理（修理艇）
        var rp = RepairParams(u);
        if (rp.can && (!moved || rp.afterMove) && customOk)
        {
            Infantry bestT = null; float bestV = 0;
            foreach (var t in u.CalculateRepairRange(g).SelectMany(x => x.infantries))
            {
                if (t == null || !IsInstanceValid(t) || t.isTransported) continue;
                if (!u.IsRepairTargetAllowed(t)) continue;   // 与引擎/界面同一判定
                int amt = rp.amount;
                float v;
                if (amt > 0 && t.team == u.team) v = Math.Min(amt, t.maxHealth - t.health) / (float)t.maxHealth * UnitValue(t);
                else if (amt < 0 && IsEnemy(t.team)) v = Math.Min(-amt, t.health) / (float)t.maxHealth * UnitValue(t);
                else continue;
                if (v > bestV) { bestV = v; bestT = t; }
            }
            if (bestT != null)
                plans.Add(new Plan { dest = g, act = Act.Repair, target = bestT, score = bestV * 1.2f - ThreatLoss(u, g, u.health, stealthAtDest && !customReveal), preReveal = customReveal && customLocked, note = "修理" });
        }

        // 照明弹：先照亮，后面的单位才能打到雾中/隐身敌人
        if (u.canIlluminate && u.currentFlareAmmo > 0 && ctx.fogOn && (!moved || u.canIlluminateAfterMove) && customOk)
        {
            Grids bestC = null; float bestV = 0;
            foreach (var c in u.AnchorRingFrom(gameManager, g.GridIndex, u.unitFacing, Math.Max(0, u.minLaunchRange), Math.Max(0, u.maxLaunchRange), u.flareLaunchCanCrossScreens))   // ✅ issue#1 二期：锚点环带并集
            {
                float v = 0;
                foreach (var t in Ring(c, Math.Max(0, u.minIlluminationRange), Math.Max(0, u.maxIlluminationRange)).Where(x => gameManager.gridManager.IsRangeAllowed(c, x, u.flareEffectCanCrossScreens)))
                {
                    if (ctx.vision.Contains(t)) continue;
                    v += 20;
                    foreach (var (ru, rg, _) in ctx.remembered) if (rg == t) v += 900 + ru.cost * 0.05f;
                    if (ctx.capturables.Contains(t)) v += 40;
                }
                if (v > bestV) { bestV = v; bestC = c; }
            }
            if (bestC != null && bestV >= 300)
                plans.Add(new Plan { dest = g, act = Act.Illuminate, actGrid = bestC, score = bestV - ThreatLoss(u, g, u.health, stealthAtDest && !customReveal), preReveal = customReveal && customLocked, note = $"照明{bestC.GridIndex}" });
        }

        // 自爆
        if (u.canExplode && u.currentExplodeAmmo > 0 && customOk)
        {
            float v = 0; int kills = 0;
            foreach (var t in ctx.mine.Concat(ctx.enemies).Distinct())
            {
                if (t == null || !IsInstanceValid(t) || t.grid == null) continue;
                var tg = t == u ? g : t.grid;
                int d = Dist(g, tg);
                if (d < u.explosionMinRange || d > u.explosionMaxRange || !gameManager.gridManager.IsRangeAllowed(g, tg, u.explosionCanCrossScreens)) continue;
                if (t == u && (u.explosionDestroysSelf || !u.explosionSelfDamageEnabled || u.explosionMinRange > 0)) continue;
                bool isEnemy = t.team != u.team;
                bool include = u.explosionTargetMode switch { 1 => isEnemy, 2 => !isEnemy, _ => true };
                if (!include) continue;
                int dmg = gameManager.CalculateExplosionDamage(u, t);
                if (dmg > 0)
                {
                    int real = Math.Min(dmg, u.explosionCanKill ? t.health : Math.Max(0, t.health - 1));
                    float tv = real / (float)t.maxHealth * UnitValue(t);
                    if (u.explosionCanKill && dmg >= t.health) { tv += UnitValue(t) * killBonusRatio; if (isEnemy) kills++; }
                    v += IsEnemy(t.team) ? tv : -tv * 1.2f;
                }
                else if (dmg < 0 && !IsEnemy(t.team))
                    v += Math.Min(-dmg, t.maxHealth - t.health) / (float)t.maxHealth * UnitValue(t);
            }
            if (u.explosionAffectsWeapons)
                foreach (var w in ctx.enemyWeapons)
                {
                    int d = Dist(g, w.grid);
                    if (d < u.explosionMinRange || d > u.explosionMaxRange || !gameManager.gridManager.IsRangeAllowed(g, w.grid, u.explosionCanCrossScreens)) continue;
                    if (u.explosionTargetMode == 2) continue;
                    int dmg = gameManager.CalculateExplosionDamage(u, w);
                    if (dmg > 0) v += Math.Min(dmg, w.health) / (float)Math.Max(1, w.maxHealth) * Math.Max(w.cost, 8000);
                }
            if (u.explosionDestroysSelf) v -= UnitValue(u);
            if (v > 500)
                plans.Add(new Plan { dest = g, act = Act.Explode, score = v, preReveal = customReveal && customLocked, note = $"自爆 价值{v:F0} 击杀{kills}" });
        }

        // 卸载（运输单位）
        if (u.canTransportUnits && u.transportedUnits.Count > 0 && !(stealthAtDest && !u.canLoadUnloadWhileStealthed && !freeToggle))
        {
            var unloads = new List<(Infantry, Grids)>();
            float v = 0;
            var used = new HashSet<Grids>();
            foreach (var cargo in u.transportedUnits.ToList())
            {
                if (cargo == null || !IsInstanceValid(cargo)) continue;
                Grids bestT = null; float bestTV = float.NegativeInfinity;
                foreach (var t in Ring(g, 1, 1))
                {
                    if (used.Contains(t) || StepCost(cargo, t) < 0 || !u.CanUnloadToGrid(t)) continue;
                    if (t.infantries.Any(o => o != null && IsInstanceValid(o) && !o.isTransported)) continue;
                    float tv = 0;
                    if (cargo.captureAbility != CaptureAbility.CannotCapture)
                    {
                        if (ctx.capturables.Contains(t) && !ctx.captureClaims.ContainsKey(t)) tv += 2500;
                        var f = DistanceField(cargo, "cap", ctx.capturables);
                        int dd = FieldAt(f, t);
                        tv += dd <= cargo.defaultMovePoints ? 1200 : 0;
                    }
                    if (ctx.enemies.Any(e => CanHitAtDistance(cargo, Dist(t, e.grid)))) tv += 700;
                    tv -= ThreatLoss(cargo, t, cargo.health, false) * 0.8f;
                    if (tv > bestTV) { bestTV = tv; bestT = t; }
                }
                if (bestT != null && bestTV > 400) { unloads.Add((cargo, bestT)); used.Add(bestT); v += bestTV; }
            }
            if (unloads.Count > 0)
                plans.Add(new Plan
                {
                    dest = g, act = Act.Unload, unloads = unloads, score = v - ThreatLoss(u, g, u.health, false) * 0.6f,
                    preReveal = stealthAtDest && !u.canLoadUnloadWhileStealthed, note = $"卸载{unloads.Count}"
                });
        }
    }

    private float SupplyNeed(Infantry t)
    {
        float v = 0;
        if (t.maxPrimaryAmmo > 0 && t.maxPrimaryAmmo < 99 && t.currentPrimaryAmmo < t.maxPrimaryAmmo)
            v += (1f - t.currentPrimaryAmmo / (float)t.maxPrimaryAmmo) * UnitValue(t) * 0.25f + (t.currentPrimaryAmmo == 0 ? 800 : 0);
        if (t.consumeFuel && t.maxFuel > 0 && t.fuel < t.maxFuel)
        {
            float frac = 1f - t.fuel / (float)t.maxFuel;
            v += frac * UnitValue(t) * 0.15f;
            if (t.fuel <= Math.Max(t.lowFuelThreshold, t.dailyFuelConsumption * 2 + t.stealthFuelCostPerDay * 2)) v += UnitValue(t) * 0.4f;   // 快坠毁了
        }
        if (t.canIlluminate && t.currentFlareAmmo <= 0) v += 300;
        if (t.canExplode && t.currentExplodeAmmo <= 0) v += 300;
        return v;
    }

    // —— 装载 / 合流 ——
    private Plan EvalLoad(Infantry u, Grids g, Infantry transport, bool reveal)
    {
        // 只有离目标很远时才值得上车
        float far = 0;
        if (u.captureAbility != CaptureAbility.CannotCapture && ctx.capturables.Count > 0)
            far = FieldAt(DistanceField(u, "cap", ctx.capturables), u.grid);
        else if (ctx.enemies.Count > 0)
            far = FieldAt(DistanceField(u, "enemy", ctx.enemies.Select(e => e.grid)), u.grid);
        if (far <= u.defaultMovePoints * 2 || far >= 999) return null;
        float score = 400 + (far - u.defaultMovePoints * 2) * 120 - ThreatLoss(transport, g, transport.health, transport.isStealthed) * 0.3f;
        return new Plan { dest = g, act = Act.Load, target = transport, score = score, preReveal = reveal, note = "上车" };
    }

    private Plan EvalMerge(Infantry u, Grids g, Infantry target, bool reveal)
    {
        if (target.health >= target.maxHealth && u.health >= u.maxHealth) return null;
        if (u.health > u.maxHealth * 0.5f && target.health > target.maxHealth * 0.5f) return null;
        int combined = u.health + target.health;
        int kept = Math.Min(target.maxHealth, combined);
        float gain = (kept - Math.Max(u.health, target.health)) / (float)target.maxHealth * target.cost * 0.6f;
        float refund = Math.Max(0, combined - target.maxHealth) / (float)target.maxHealth * target.cost * 0.5f;
        float score = gain + refund + 200 - ThreatLoss(target, g, kept, target.isStealthed);
        return new Plan { dest = g, act = Act.Merge, target = target, score = score, preReveal = reveal, note = "合流" };
    }

    // —— 隐身切换 + 待机位置 ——
    private void EvalStealthAndWait(Infantry u, Grids g, bool moved, bool stealthAtDest, bool reveal, bool freeToggle, List<Plan> plans)
    {
        float pos = PositionScore(u, g);
        float riskVisible = ThreatLoss(u, g, u.health, false);
        float riskHidden = u.canStealth ? ThreatLoss(u, g, u.health, true) : riskVisible;
        float riskNow = stealthAtDest ? riskHidden : riskVisible;

        var wait = new Plan { dest = g, act = Act.Wait, score = pos - riskNow, preReveal = reveal, note = "待机" };
        if (freeToggle && u.canStealth)
        {
            if (!stealthAtDest && WantsHide(u, g, u.health)) { wait.postHide = true; wait.score = pos - riskHidden; wait.note = "待机+隐身"; }
            else if (stealthAtDest && WantsReveal(u, g)) { wait.postHide = false; wait.preReveal = true; wait.score = pos - riskVisible + 300; wait.note = "待机+显形省油"; }
        }
        plans.Add(wait);

        // 切换算独立行动：隐身/显形就是这回合的行动
        if (u.canStealth && CanToggle(u) && u.stealthToggleCostsAction)
        {
            if (!stealthAtDest && WantsHide(u, g, u.health))
                plans.Add(new Plan { dest = g, act = Act.Hide, score = pos - riskHidden - StealthCost(u), preReveal = reveal, note = "隐身" });
            else if (stealthAtDest && WantsReveal(u, g))
                plans.Add(new Plan { dest = g, act = Act.Reveal, score = pos - riskVisible + 400, note = "显形省油" });
        }
    }

    private bool CanToggle(Infantry u)
    {
        if (!u.canStealth || u.IsStealthFormLocked) return false;
        if (u.stealthToggleCostsAction && (u.state == UnitState.Acted || u.hasActed)) return false;
        if (u.stealthAmmoMode == 1) return u.isStealthed ? u.currentRevealBullets > 0 : u.currentStealthBullets > 0;
        if (u.stealthAmmoMode == 2) return u.currentCamoBullets > 0;
        return true;
    }

    private float StealthCost(Infantry u)
    {
        float c = 0;
        if (u.stealthAmmoMode != 0) c += 400;   // 弹药有限
        if (u.consumeFuel && u.stealthFuelCostPerDay > 0) c += u.stealthFuelCostPerDay * 30;
        return c;
    }

    private bool WantsHide(Infantry u, Grids g, int hp)
    {
        if (!u.canStealth || u.isStealthed) return false;
        if (u.consumeFuel && u.stealthFuelCostPerDay > 0)
        {
            int perDay = u.dailyFuelConsumption + u.stealthFuelCostPerDay;
            if (u.fuel - perDay * 2 <= Math.Max(u.lowFuelThreshold, perDay)) return false;   // 油不够撑两天就别隐身
        }
        if (u.aiRole == 4) return true;   // 刺客：能隐就隐
        return ThreatLoss(u, g, hp, false) - ThreatLoss(u, g, hp, true) > StealthCost(u);
    }

    private bool WantsReveal(Infantry u, Grids g)
    {
        if (!u.isStealthed) return false;
        if (u.consumeFuel && u.stealthFuelCostPerDay > 0)
        {
            int perDay = u.dailyFuelConsumption + u.stealthFuelCostPerDay;
            bool critical = u.fuel - perDay <= Math.Max(u.lowFuelThreshold / 2, u.dailyFuelConsumption);
            bool safe = ThreatLoss(u, g, u.health, false) < UnitValue(u) * 0.1f;
            return critical || safe;
        }
        return false;
    }

    private float Terrain(Infantry u, Grids g) =>
        u.defenseBonusType == Infantry.DefenseBonusType.CanDefenseBonus ? g.TerrainDefenseBonus : 0f;

    /// <summary>✦适配：格子自定义效果站位估值——AI 会主动规避伤害地形、利用治疗/补给/成长地形（与回合结算步骤同源数值）</summary>
    private float TerrainEffectScore(Infantry u, Grids g)
    {
        if (u == null || g == null) return 0;
        float v = 0;

        // ① 血量变化（fixedDamagePerTurn/fixedAttackPerTurn：正=扣血 负=回血；攻击经防御，按全额粗估）
        int hpDelta = g.fixedDamagePerTurn + g.fixedAttackPerTurn;
        if (hpDelta > 0)
        {
            int real = Math.Min(hpDelta, u.health);
            v -= real / (float)Math.Max(1, u.maxHealth) * UnitValue(u) * 0.8f;   // 扣血地形=按最大血量比例罚分
        }
        else if (hpDelta < 0)
        {
            int heal = -hpDelta;
            int room = g.canOverMaxHealth ? heal : Math.Max(0, u.maxHealth - u.health);
            int real = Math.Min(heal, room);
            v += real / (float)Math.Max(1, u.maxHealth) * UnitValue(u) * (u.health < u.maxHealth * 0.7f ? 0.7f : 0.2f); // 缺血时治疗地形价值更高
        }

        // ② 最大血量增减（扩容=成长 资产；缩容=侵蚀）
        if (g.fixedMaxHealthChangePerTurn != 0)
            v += g.fixedMaxHealthChangePerTurn * 30f;

        // ③ 全种类弹药增减（缺弹时补给地形价值更高）
        foreach (var k in Infantry.AllAmmoKinds)
        {
            int c = g.GetAmmoKindChange(k);
            if (c == 0) continue;
            int max = u.GetBaseMaxAmmo(k);
            if (max <= 0) continue;
            int cur = u.GetAmmo(k);
            if (c > 0) v += Math.Min(c, Math.Max(0, max - cur)) * 25f + (cur == 0 ? 200 : 0);
            else v -= Math.Min(-c, cur) * 25f;
        }

        // ④ 全种类弹药上限增减
        foreach (var k in Infantry.AllAmmoKinds)
        {
            int mc = g.GetAmmoKindMaxChange(k);
            if (mc != 0 && u.GetBaseMaxAmmo(k) > 0) v += mc * 15f;
        }

        // ⑤ 燃油增减（快断油时补给地形价值飙升；耗油地形可能直接坠毁）
        if (u.consumeFuel && g.fixedFuelChangePerTurn != 0)
        {
            if (g.fixedFuelChangePerTurn > 0)
            {
                v += Math.Min(g.fixedFuelChangePerTurn, Math.Max(0, u.maxFuel - u.fuel)) * 12f;
                if (u.fuel <= (u.dailyFuelConsumption + (u.isStealthed ? u.stealthFuelCostPerDay : 0)) * 2) v += 300;
            }
            else
            {
                int drain = Math.Min(-g.fixedFuelChangePerTurn, u.fuel);
                v -= drain * 12f;
                if (u.fuel + g.fixedFuelChangePerTurn <= 0 && u.destroyOnOutOfFuel) v -= UnitValue(u) * 0.5f;
            }
        }

        // ⑥ 燃油上限增减
        if (u.consumeFuel && g.fixedMaxFuelChangePerTurn != 0)
            v += g.fixedMaxFuelChangePerTurn * 8f;

        return v;
    }

    /// <summary>站位价值：根据角色向目标推进 / 撤退修整 / 地形 / 不堵工厂</summary>
    private float PositionScore(Infantry u, Grids g)
    {
        float s = Terrain(u, g) * 300 + TerrainEffectScore(u, g); // ✦适配：格子自定义效果（伤害/治疗/弹药/燃料/上限增减）

        // 不要堵住自家工厂
        if (g.city != null && g.city.canProduce && g.city.facilityTeam == aiTeam && g != u.grid) s -= 900;
        if (g.city != null && g.city.canProduce && g.city.facilityTeam == aiTeam && g == u.grid && gameManager.GetTeamFunds(aiTeam) >= 1000) s -= 900;

        // 撤退：残血 / 没弹 / 快没油
        bool lowHp = u.aiRetreatHpThreshold < 0 ? u.health <= u.maxHealth * 0.3f
                   : u.aiRetreatHpThreshold > 0 && u.health <= u.aiRetreatHpThreshold;
        bool noAmmo = u.maxPrimaryAmmo > 0 && u.maxPrimaryAmmo < 99 && u.currentPrimaryAmmo <= 0 && !u.CanUseSecondaryWeapon();
        bool lowFuel = u.consumeFuel && u.fuel <= Math.Max(u.lowFuelThreshold, (u.dailyFuelConsumption + (u.isStealthed ? u.stealthFuelCostPerDay : 0)) * 3);
        // 黑水晶优先：站进范围就能回血/补给（己方水晶由 AI 在全员走位后脉冲）
        float crystal = ctx.crystals.Count > 0 ? CrystalBonus(u, g) : 0;
        bool needCrystal = u.health < u.maxHealth * 0.8f || SupplyNeed(u) > 0;
        if (crystal > 0 || (ctx.crystals.Count > 0 && (lowHp || noAmmo || lowFuel || needCrystal)))
        {
            var cf = DistanceField(u, "crystal#" + u.GetInstanceId(), () => CrystalTilesFor(u).ToList());
            int c0 = FieldAt(cf, u.grid), c1 = FieldAt(cf, g);
            int reach = (lowHp || noAmmo || lowFuel) ? 998 : Math.Max(1, u.defaultMovePoints) * 2;   // 轻伤只在约两回合内可达时绕过去
            if (c0 < 999 && c0 <= reach && (lowHp || noAmmo || lowFuel || needCrystal))
                return s + (c0 - c1) * 300 + crystal * 2f + (c1 == 0 ? 2500 : 0);
        }
        s += crystal;

        if (lowHp || noAmmo || lowFuel)
        {
            var f = DistanceField(u, "haven", () => gameManager.gridManager.grids
                .Where(x => x?.city != null && x.city.facilityTeam == aiTeam && Heals(x.city, u))
                .Concat(ctx.mine.Where(m => m != u && m.canSupplyUnits).Select(m => m.grid)));
            int d0 = FieldAt(f, u.grid), d1 = FieldAt(f, g);
            if (d0 < 999)
            {
                s += (d0 - d1) * 250 + (d1 == 0 ? 1500 : 0);
                return s;
            }
        }

        if (u.canSupplyUnits && u.attackType == AttackType.NoAttack && !u.canTransportUnits)
        {
            var needy = ctx.mine.Where(m => m != u && SupplyNeed(m) > 0).Select(m => m.grid);
            return s + Approach(u, g, "needy#" + u.GetInstanceId(), needy, 120);
        }

        if (u.CanPerformRepair)
        {
            var hurt = ctx.mine.Where(m => m != u && m.health < m.maxHealth * 0.8f).Select(m => m.grid);
            return s + Approach(u, g, "hurt#" + u.GetInstanceId(), hurt, 120);
        }

        if (u.canTransportUnits)
        {
            if (u.transportedUnits.Count > 0)
                return s + Approach(u, g, "cap", ctx.capturables, 150);
            var riders = ctx.mine.Where(m => m != u && m.captureAbility != CaptureAbility.CannotCapture && u.CanTransportUnit(m)
                                             && FieldAt(DistanceField(m, "cap", ctx.capturables), m.grid) > m.defaultMovePoints * 2).Select(m => m.grid);
            if (ctx.mine.Any(m => m != u && m.captureAbility != CaptureAbility.CannotCapture)) return s + Approach(u, g, "riders#" + u.GetInstanceId(), riders, 100);
        }

        // 占领兵：去最近的未被认领据点
        if (u.aiRole == 3)   // 守备：贴近最受威胁的己方据点
        {
            var own = gameManager.gridManager.grids.Where(x => x?.city != null && x.city.facilityTeam == aiTeam).ToList();
            if (own.Count > 0 && ctx.enemies.Count > 0)
                own = own.OrderBy(x => ctx.enemies.Min(e => Dist(x, e.grid))).Take(2).ToList();
            if (own.Count > 0) return s + Approach(u, g, "guard", own, 120);
        }

        if (u.captureAbility != CaptureAbility.CannotCapture && ctx.capturables.Count > 0 && u.aiRole != 1)
        {
            var targets = CaptureTargetsFor(u);
            s += Approach(u, g, "cap#" + u.GetInstanceId(), targets, 140);
            if (ctx.capturables.Contains(g)) s += 300;
            return s;
        }

        // 战斗单位：向敌军推进（可见 > 记忆 > 敌方据点），远程停在射程边缘
        var goals = ctx.enemies.Select(e => e.grid).ToList();
        string key = "enemy";
        if (goals.Count == 0) { goals = ctx.remembered.Select(r => r.g).ToList(); key = "mem"; }
        if (goals.Count == 0)
        {
            goals = gameManager.gridManager.grids.Where(x => x?.city != null && IsEnemy(x.city.facilityTeam)).ToList();
            key = "efac";
        }
        if (goals.Count == 0) goals = ctx.capturables;
        if (IsIndirect(u) && ctx.enemies.Count > 0)
        {
            int best = ctx.enemies.Min(e => Math.Abs(Dist(g, e.grid) - MaxRange(u)));
            bool inRangeNextTurn = ctx.enemies.Any(e => CanHitAtDistance(u, Dist(g, e.grid)));
            return s - best * 120 + (inRangeNextTurn ? 600 : 0);
        }
        s += Approach(u, g, key, goals, 110);
        // 保护己方占领兵
        foreach (var m in ctx.mine)
            if (m != u && m.capturingGrid != null && Dist(m.grid, g) == 1) s += 150;
        return s;
    }

    private List<Grids> CaptureTargetsFor(Infantry u)
    {
        var t = ctx.capturables.Where(c => !ctx.captureClaims.TryGetValue(c, out var cl) || cl == u || !IsInstanceValid(cl)).ToList();
        return t.Count > 0 ? t : ctx.capturables;
    }

    private void ClaimCaptureTarget(Infantry u)
    {
        if (u.captureAbility == CaptureAbility.CannotCapture || u.grid == null) return;
        foreach (var k in moveClaims.Where(kv => kv.Value == u).Select(kv => kv.Key).ToList()) moveClaims.Remove(k);
        var f = DistanceField(u, "cap#" + u.GetInstanceId(), CaptureTargetsFor(u));
        Grids best = null; int bd = int.MaxValue;
        foreach (var c in CaptureTargetsFor(u))
        {
            // 反向距离场无法直接给出"哪个目标最近"，用曼哈顿距离挑选，再用距离场兜底
            int d = Dist(u.grid, c) * 2 + (ctx.captureClaims.ContainsKey(c) ? 50 : 0);
            if (d < bd) { bd = d; best = c; }
        }
        if (best != null && FieldAt(f, u.grid) < 999) moveClaims[best] = u;
    }

    private float Approach(Infantry u, Grids g, string key, IEnumerable<Grids> goals, float perStep)
    {
        var f = DistanceField(u, key, () => goals.Where(x => x != null).ToList());   // 惰性：缓存命中时不再枚举目标
        if (f.Count == 0) return 0;
        int d0 = FieldAt(f, u.grid), d1 = FieldAt(f, g);
        if (d1 >= 999) return -500;
        if (d0 >= 999) return -d1 * 10;
        return (d0 - d1) * perStep - d1 * 5;
    }

    private static bool Heals(Facility f, Infantry u)
    {
        if (f.healAmount <= 0 && f.healPercent <= 0) return false;
        if (f.supportedUnitTypesForHeal == null || f.supportedUnitTypesForHeal.Count == 0) return true;
        return f.supportedUnitTypesForHeal.Contains(u.UnitTypeId);
    }

    private static bool IsIndirect(Infantry u) => u.attackType != AttackType.NoAttack && (!u.canAttackAfterMoving || (u.useMinMaxAttackRange && u.minAttackRange > 1));
}
