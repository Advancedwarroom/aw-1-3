using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Infantry
{
    public bool CanJointCounter(Infantry attacker)
    {
        return attacker != null && IsInstanceValid(attacker) && attacker.grid != null && grid != null
            && CounterFireAllowed(attacker) && CanTargetStealth(this, attacker)
            && SelectWeaponByMatrix(attacker) != WeaponType.None;
    }

    public void JointApplyDamageReveal(Infantry target)
    {
        ApplyStealthRevealOnDamage(target);
        target.CheckStealthThresholds();
        target.SyncDamageRevealTracker();
    }

    public void JointApplyCounterReveal()
    {
        ApplyStealthRevealOnDamage(this);
        CheckStealthThresholds();
        SyncDamageRevealTracker();
    }

    public void JointTryPushTarget(Infantry target) => TryPushTargetOnAttack(target);
}

public partial class GameManager
{
    private sealed class JointHit
    {
        public Infantry target;
        public WeaponType weapon;
        public int damage;
    }

    private bool JointRelationAllowed(Infantry attacker, Infantry target, Grids hitGrid)
    {
        if (attacker == null || target == null || !IsInstanceValid(attacker) || !IsInstanceValid(target)) return false;
        if (attacker.attackType == AttackType.NoAttack || target.grid == null) return false;
        if (attacker.isStealthed && !attacker.canAttackWhileStealthed) return false;
        if (!attacker.canAttackAfterMoving && attacker.state == UnitState.Moved) return false;
        if (!TeamHelper.IsEnemyForAttacker(attacker.team, target.team, attacker.canAttackFriendlies, attacker.canAttackAllies)) return false;
        if (!Infantry.CanTargetStealth(attacker, target) || !attacker.IsTargetInAttackRange(target)) return false;
        return attacker.SelectWeaponByMatrix(target) != WeaponType.None;
    }

    private List<JointHit> GetLegalJointHits(Infantry attacker, IEnumerable<Infantry> targets, Grids hitGrid)
    {
        var result = new List<JointHit>();
        foreach (var target in targets.Distinct().ToList())
        {
            if (!JointRelationAllowed(attacker, target, hitGrid)) continue;
            var weapon = attacker.SelectWeaponByMatrix(target);
            if (weapon != WeaponType.None) result.Add(new JointHit { target = target, weapon = weapon });
        }
        return result;
    }

    // 与正常公式同源；includeHealth=false 时同时剔除攻击方血量火力与目标动态防御里的血量缩放。
    private int CalculateJointDamage(Infantry attacker, Infantry target, WeaponType weaponType,
        bool isCounter, bool includeHealth, float outputMul)
    {
        if (attacker.attackUsesFixedDamage) return -attacker.attackFixedDamage;
        int baseDamage = weaponType == WeaponType.Primary
            ? attacker.GetPrimaryDamageFromMatrix(target)
            : attacker.GetSecondaryDamageFromMatrix(target);
        if (attacker.packBoostAttack && attacker.PackFrenzyActive())
            baseDamage = baseDamage * Mathf.Max(0, 100 + attacker.packBonusPercent) / 100;
        if (baseDamage <= 0) return 0;

        float actualAttack = baseDamage;
        if (includeHealth) actualAttack *= (float)attacker.health / attacker.GetEffectiveMaxHealth();
        actualAttack *= outputMul;
        float coOffBonus = CoModuleSystem.GetOffenseBonusPercent(attacker) + WeatherSystem.GetOffenseModifierPercent(attacker);
        actualAttack *= Mathf.Max(0f, 100f + coOffBonus) / 100f;

        float terrainDefense = (target.defenseBonusType == DefenseBonusType.CanDefenseBonus && target.grid != null
                && (target.grid.city == null || !target.grid.city.HasStatus(FacilityStatusType.NoCover)))
            ? target.grid.TerrainDefenseBonus * Mathf.Max(0, 100 + (target.grid.city?.defenseSuppressPercent ?? 0)) / 100f
            : 0f;
        terrainDefense = Mathf.Max(0f, terrainDefense + CoModuleSystem.GetTerrainDefenseBonus(target) / 100f);
        float rawDamage = actualAttack * (1.0f - terrainDefense);
        rawDamage *= Mathf.Max(0.05f, 100f - CoModuleSystem.GetDefenseBonusPercent(target)) / 100f;
        rawDamage -= target.GetEffectiveDefense(target.grid, includeHealth);

        var wm = attacker.GetTree()?.GetFirstNodeInGroup("weather_manager") as WeatherManager;
        if (wm != null)
        {
            var ac = attacker.grid != null ? attacker.grid.GridIndex : new Vector2I(-1, -1);
            var dc = target.grid != null ? target.grid.GridIndex : new Vector2I(-1, -1);
            rawDamage = rawDamage * wm.GetAttackPercent(ac) / 1000f;
            rawDamage = rawDamage * wm.GetDefensePercent(dc) / 1000f;
        }
        if (attacker.attackVsCategoryEnabled && target.unitCategory == attacker.attackVsCategory)
            rawDamage *= attacker.attackVsCategoryMultiplier;
        if (rawDamage <= 0) return 0;
        if (isCounter) rawDamage *= attacker.counterMul;
        int luckRoll = CoModuleSystem.GetLuckRoll(attacker);
        return Mathf.Max(1, Mathf.RoundToInt(rawDamage) + luckRoll);
    }

    public bool TryExecuteJointAttack(Infantry selectedAttacker, Infantry selectedTarget,
        bool allowCounter = true, Grids hitGrid = null)
    {
        if (resolvingJointAttack || selectedAttacker == null || selectedTarget == null) return false;
        var attackers = GetJoinedMembers(selectedAttacker)
            .Where(a => !a.isAttacked && a.state != UnitState.Acted).ToList();
        var defenders = GetJoinedMembers(selectedTarget);
        if (attackers.Count <= 1 && defenders.Count <= 1) return false;

        resolvingJointAttack = true;
        try
        {
            var eligibleDefenders = defenders.Where(d => attackers.Any(a => JointRelationAllowed(a, d, hitGrid))).ToList();
            if (eligibleDefenders.Count == 0) return false;
            var alreadyCountered = new HashSet<Infantry>();

            if (allowCounter)
            {
                foreach (var defender in eligibleDefenders.ToList())
                {
                    if (!IsInstanceValid(defender) || defender.health <= 0) continue;
                    bool firstStrike = attackers.Any(a => IsInstanceValid(a) && a.health > 0
                        && CoModuleSystem.GetFirstStrike(defender) && defender.CanJointCounter(a));
                    if (!firstStrike) continue;
                    ResolveJointStrike(defender, attackers, hitGrid, true);
                    alreadyCountered.Add(defender);
                }
            }

            foreach (var attacker in attackers.ToList())
            {
                if (!IsInstanceValid(attacker) || attacker.health <= 0 || attacker.isAttacked || attacker.state == UnitState.Acted) continue;
                if (!ActionResourceRuntime.CanUse(attacker, ActionIds.Attack)) continue;
                ResolveJointStrike(attacker, eligibleDefenders, hitGrid, false);
            }

            if (allowCounter)
            {
                foreach (var defender in eligibleDefenders.ToList())
                {
                    if (alreadyCountered.Contains(defender) || !IsInstanceValid(defender) || defender.health <= 0) continue;
                    bool canCounter = attackers.Any(a => IsInstanceValid(a) && a.health > 0 && defender.CanJointCounter(a));
                    if (canCounter) ResolveJointStrike(defender, attackers, hitGrid, true);
                }
            }
            RefreshAllJoinVisuals();
            return true;
        }
        finally
        {
            resolvingJointAttack = false;
        }
    }

    private void ResolveJointStrike(Infantry attacker, IEnumerable<Infantry> targets, Grids hitGrid, bool isCounter)
    {
        var hits = GetLegalJointHits(attacker, targets, hitGrid);
        if (hits.Count == 0) return;

        float outputMul = Mathf.Max(0.01f, attacker.nextAttackOutputMul);
        attacker.nextAttackOutputMul = 1f;
        if (!isCounter)
        {
            if (hits.Any(h => h.weapon == WeaponType.Primary)) attacker.ConsumePrimaryAmmo();
            attacker.ShowAnimState("attack");
        }

        foreach (var hit in hits)
            hit.damage = CalculateJointDamage(attacker, hit.target, hit.weapon, isCounter,
                hit.target.jointDamageMode == JointDamageMode.Independent, outputMul);

        // 独立承伤是显式高代价模式：n 个合法成员就执行 n 次完整成员级结算。
        foreach (var hit in hits.Where(h => h.target.jointDamageMode == JointDamageMode.Independent || attacker.attackUsesFixedDamage))
            ApplyJointDamage(attacker, hit.target, hit.damage, hitGrid);

        ResolveWeightedJointGroup(attacker, hits.Where(h => !attacker.attackUsesFixedDamage && h.target.jointDamageMode == JointDamageMode.WeightedAftermath).ToList(), hitGrid);
        ResolveGentleJointGroup(attacker, hits.Where(h => !attacker.attackUsesFixedDamage && h.target.jointDamageMode == JointDamageMode.GentlePool).ToList(), hitGrid);

        if (!isCounter)
        {
            ActionResourceRuntime.Consume(attacker, ActionIds.Attack);
            ActionResourceRuntime.FireEvents(attacker, ActionIds.Attack);
            if (attacker.stealthRevealOnAttack) attacker.RevealStealth();
            attacker.isAttacked = true;
            attacker.isMoved = true;
            attacker.state = UnitState.Acted;
            attacker.originalGrid = null;
            attacker.actionMenu?.Hide();
            attacker.FreezeToIdleDark();
        }
        attacker.UpdateHpLabel();
    }

    private void ResolveWeightedJointGroup(Infantry attacker, List<JointHit> hits, Grids hitGrid)
    {
        if (hits.Count == 0) return;
        hits = hits.Where(h => IsInstanceValid(h.target) && h.target.health > 0 && h.damage > 0).ToList();
        if (hits.Count == 0) return;
        double hpPool = hits.Sum(h => (double)h.target.health);
        double damagePool = hits.Sum(h => h.damage * (h.target.health / hpPool));
        double damageReferenceSum = hits.Sum(h => (double)h.damage);
        if (damageReferenceSum <= 0) return;

        var weights = hits.ToDictionary(h => h,
            h => (h.target.health / hpPool) * (h.damage / damageReferenceSum));
        double weightSum = weights.Values.Sum();
        var remaining = hits.OrderByDescending(h => h.damage).ThenBy(h => h.target.health).ToList();

        while (remaining.Count > 0 && damagePool > 0.0001)
        {
            double remainingWeight = remaining.Sum(h => weights[h]);
            if (remainingWeight <= 0) break;
            bool killed = false;
            var roundShares = remaining.ToDictionary(h => h, h => damagePool * weights[h] / remainingWeight);
            foreach (var hit in remaining.ToList())
            {
                double share = roundShares[hit];
                int hp = hit.target.health;
                if (share + 0.0001 < hp)
                {
                    int amount = Math.Min(hp, Math.Max(0, Mathf.RoundToInt((float)share)));
                    ApplyJointDamage(attacker, hit.target, amount, hitGrid);
                    damagePool = Math.Max(0, damagePool - amount);
                    remaining.Remove(hit);
                    continue;
                }

                // 恰好摧毁无余波；超额摧毁只扣实际剩余血量，原权重在余下成员间顺延。
                ApplyJointDamage(attacker, hit.target, hp, hitGrid);
                damagePool = Math.Max(0, damagePool - hp);
                remaining.Remove(hit);
                killed = true;
                break;
            }
            if (!killed && remaining.Count == 0) break;
        }
    }

    private void ResolveGentleJointGroup(Infantry attacker, List<JointHit> hits, Grids hitGrid)
    {
        hits = hits.Where(h => IsInstanceValid(h.target) && h.target.health > 0).ToList();
        if (hits.Count == 0) return;
        double hpPool = hits.Sum(h => (double)h.target.health);
        double damage = hits.Sum(h => h.damage * (h.target.health / hpPool));
        double ratio = Math.Max(0, (hpPool - damage) / hpPool);
        foreach (var hit in hits)
        {
            int newHealth = ratio <= 0 ? 0 : Math.Min(hit.target.health, Mathf.RoundToInt((float)(hit.target.health * ratio)));
            ApplyJointDamage(attacker, hit.target, hit.target.health - newHealth, hitGrid);
        }
    }

    private void ApplyJointDamage(Infantry attacker, Infantry target, int damage, Grids hitGrid)
    {
        if (target == null || !IsInstanceValid(target) || target.health <= 0) return;
        var oldGrid = target.grid;
        int oldGroupId = target.joinGroupId;
        target.lastAttackerTeam = attacker.team;

        if (hitGrid != null && target.isMultiTileUnit && !target.healthLocked)
        {
            damage = Mathf.Max(0, Mathf.RoundToInt(damage * target.GetWeakCellDamageFactor(hitGrid)));
            float outMul = target.GetWeakCellOutputFactor(hitGrid);
            if (!Mathf.IsEqualApprox(outMul, 1f)) target.nextAttackOutputMul = outMul;
            ActionResourceRuntime.OnHitMisfire(target, target.health, damage);
            ActionResourceRuntime.OnWeakCellHit(target, target.GetUnitCellIndexOf(hitGrid));
            ActionResourceRuntime.OnUnitHit(target);
        }
        if (!target.healthLocked) target.health = Math.Min(target.maxHealth, Math.Max(0, target.health - damage));
        CoModuleSystem.OnDamageDealt(attacker.team, damage);
        CoModuleSystem.OnDamageTaken(target.team, damage);
        attacker.JointApplyDamageReveal(target);
        target.UpdateHpLabel();

        if (target.health <= 0)
        {
            if (target.TryFlee()) return;
            CoModuleSystem.OnUnitLost(target.team);
            attacker.GainXp(CoModuleSystem.GetKillXp(attacker));
            CampaignSettlement.Instance?.RecordKill(target.campaignScoreValue, target.campaignCrystalValue, attacker.team);
            RemoveUnit(target);
            if (oldGrid != null && oldGroupId > 0)
            {
                var rest = oldGrid.infantries.Where(u => u != null && IsInstanceValid(u) && u.joinGroupId == oldGroupId).ToList();
                if (rest.Count == 1) rest[0].joinGroupId = 0;
                RefreshJoinVisuals(oldGrid);
            }
        }
        else attacker.JointTryPushTarget(target);
    }
}
