using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Atomic push transaction machinery. ResolvePushChain dry-runs the entire chain on
/// a virtual snapshot of the board and only records move plans plus a delayed effect
/// queue. Nothing touches the real board, unit stats, ammo, fuel or funds until the
/// whole chain is proven resolvable; any failed link discards everything.
/// The virtual board is pure data (no Godot nodes, nothing enters the scene tree),
/// so it can never leak visuals into the live game.
/// </summary>
public partial class Infantry
{
    private enum PushPlanKind { StackMove, WeaponMove, FacilityMove }

    private sealed class PushMovePlan
    {
        public PushPlanKind Kind;
        public List<Infantry> Stack = new();
        public Weapon Weapon;
        public Facility Facility;
        public Grids Destination;

        public static PushMovePlan ForStack(List<Infantry> stack, Grids destination) =>
            new() { Kind = PushPlanKind.StackMove, Stack = stack, Destination = destination };
        public static PushMovePlan ForWeapon(Weapon weapon, Grids destination) =>
            new() { Kind = PushPlanKind.WeaponMove, Weapon = weapon, Destination = destination };
        public static PushMovePlan ForFacility(Facility facility, Grids destination) =>
            new() { Kind = PushPlanKind.FacilityMove, Facility = facility, Destination = destination };
    }

    private enum PushEffectKind
    {
        PayPushCost, FundsDelta, ConsumePrimaryAmmo, RawFuelCost, RawHealthCost,
        TargetFuelDelta, TargetAmmoDelta, MaxHealthDelta, LockToOneHp, Heal, Damage, Destroy,
        DamageWeapon, DamageFacility, AmmoKindCost
    }

    private sealed class PushEffect
    {
        public PushEffectKind Kind;
        public Infantry Unit;
        public string Team;
        public long Amount;
        public PushResourceType Resource;
        public Weapon Weapon;
        public Facility Facility;
        public AmmoKind AmmoKind;
        public bool CanDestroy;
        public bool CanOverMaxHealth;

        public static PushEffect Cost(Infantry payer, PushResourceType resource, int amount) =>
            new() { Kind = PushEffectKind.PayPushCost, Unit = payer, Resource = resource, Amount = amount };
        public static PushEffect Funds(string team, int amount) =>
            new() { Kind = PushEffectKind.FundsDelta, Team = team, Amount = amount };
        public static PushEffect Ammo(Infantry unit, int amount) =>
            new() { Kind = PushEffectKind.ConsumePrimaryAmmo, Unit = unit, Amount = amount };
        public static PushEffect FuelCost(Infantry unit, int amount) =>
            new() { Kind = PushEffectKind.RawFuelCost, Unit = unit, Amount = amount };
        public static PushEffect HealthCost(Infantry unit, int amount) =>
            new() { Kind = PushEffectKind.RawHealthCost, Unit = unit, Amount = amount };
        public static PushEffect TargetFuel(Infantry unit, int delta) =>
            new() { Kind = PushEffectKind.TargetFuelDelta, Unit = unit, Amount = delta };
        public static PushEffect TargetAmmo(Infantry unit, int delta) =>
            new() { Kind = PushEffectKind.TargetAmmoDelta, Unit = unit, Amount = delta };
        public static PushEffect TargetMaxHealth(Infantry unit, int delta) =>
            new() { Kind = PushEffectKind.MaxHealthDelta, Unit = unit, Amount = delta };
        public static PushEffect LockOneHp(Infantry unit) =>
            new() { Kind = PushEffectKind.LockToOneHp, Unit = unit };
        public static PushEffect HealUnit(Infantry unit, int amount, bool canOverMax) =>
            new() { Kind = PushEffectKind.Heal, Unit = unit, Amount = amount, CanOverMaxHealth = canOverMax };
        public static PushEffect DamageUnit(Infantry unit, long amount, bool canDestroy) =>
            new() { Kind = PushEffectKind.Damage, Unit = unit, Amount = amount, CanDestroy = canDestroy };
        public static PushEffect DestroyUnit(Infantry unit) =>
            new() { Kind = PushEffectKind.Destroy, Unit = unit };
        public static PushEffect DamageWeapon(Weapon w, int amount) =>
            new() { Kind = PushEffectKind.DamageWeapon, Weapon = w, Amount = amount };
        public static PushEffect DamageFacility(Facility f, int amount) =>
            new() { Kind = PushEffectKind.DamageFacility, Facility = f, Amount = amount };
        public static PushEffect AmmoKindCost(Infantry payer, AmmoKind kind, int amount) =>
            new() { Kind = PushEffectKind.AmmoKindCost, Unit = payer, Amount = amount, AmmoKind = kind };
    }

    // One tile of the dry-run board. Holds references only; simulated entities are
    // never mutated here — plans and effects record what the commit phase must do.
    private sealed class VirtualTile
    {
        public List<Infantry> Stack = new();
        public List<Weapon> Weapons = new();
        public Facility City;
        public Infantry MultiTileOccupant;
    }

    // 统一混合链节点（原文#1/#2）：一格上的单位+兵器+设施作为整体一环，重量合计传导。
    private sealed class PushNode
    {
        public Grids Tile;
        public List<Infantry> Units = new();
        public Weapon Weapon;
        public Facility Facility;
        public bool HasStatics => Weapon != null || Facility != null;
        public float StaticWeight => (Weapon != null ? Weapon.GetEffectiveWeight() : 0f)
            + (Facility != null ? Facility.GetEffectiveWeight() : 0f);
    }

    private sealed class PushVirtualBoard
    {
        public readonly Dictionary<Grids, VirtualTile> Tiles = new();

        public VirtualTile Of(Grids grid)
        {
            if (grid == null) return null;
            if (!Tiles.TryGetValue(grid, out var tile))
            {
                tile = new VirtualTile();
                Tiles.Add(grid, tile);
            }
            return tile;
        }
    }

    private static PushVirtualBoard SnapshotPushBoard(GridManager gridManager)
    {
        var board = new PushVirtualBoard();
        foreach (var grid in gridManager.grids)
        {
            if (grid == null) continue;
            var tile = board.Of(grid);
            tile.Stack.AddRange(GetLivingStack(grid));
            if (grid.weapons != null)
                tile.Weapons.AddRange(grid.weapons.Where(w => w != null
                    && GodotObject.IsInstanceValid(w) && !w.IsQueuedForDeletion()));
            tile.City = grid.city;
            if (IsLivingUnit(grid.multiTileOccupant)) tile.MultiTileOccupant = grid.multiTileOccupant;
        }
        return board;
    }

    private static void VMoveStack(PushVirtualBoard board, Grids from, Grids to, List<Infantry> stack)
    {
        if (to == null) return;
        if (from != null)
            foreach (var unit in stack) board.Of(from).Stack.Remove(unit);
        var destination = board.Of(to);
        foreach (var unit in stack)
            if (!destination.Stack.Contains(unit)) destination.Stack.Add(unit);
    }

    private static bool HasForeignVirtualMultiTileOccupant(VirtualTile tile, List<Infantry> movingStack)
    {
        var occupant = tile?.MultiTileOccupant;
        return IsLivingUnit(occupant) && (movingStack == null || !movingStack.Contains(occupant));
    }

    // Sim-time destroy: mark the unit, strip it from the virtual stack it currently
    // occupies (tracked position, not its stale real grid) and queue the real
    // destruction for the effect flush. The unit receives no further effects.
    private static void QueueSimDestroy(Infantry unit, Grids virtualTile, HashSet<Infantry> destroyed,
        PushVirtualBoard board, List<PushEffect> effects)
    {
        if (unit == null || destroyed.Contains(unit)) return;
        destroyed.Add(unit);
        if (virtualTile != null) board.Of(virtualTile).Stack.Remove(unit);
        effects.Add(PushEffect.DestroyUnit(unit));
    }

    private static bool CanMoveStaticTargetVirtually(PushVirtualBoard board, Grids destination)
    {
        if (destination == null) return false;
        var tile = board.Of(destination);
        if (tile.Stack.Count > 0 || tile.MultiTileOccupant != null) return false;
        if (tile.City != null) return false;
        return tile.Weapons.Count == 0;
    }

    // ===== Commit phase: the ONLY writers of real state =====

    private void CommitPushTransaction(List<PushMovePlan> movePlans, List<PushEffect> effects,
        GameManager gameManager)
    {
        // 视觉反馈（原文#12）：提交前记录各单位/兵器/设施起点，提交后补间滑动+碰撞闪白抖动
        var slideFrom = new Dictionary<Infantry, Vector2>();
        var slideFromStatic = new Dictionary<GodotObject, Vector2>();
        foreach (var plan in movePlans)
        {
            if (plan.Kind == PushPlanKind.StackMove)
                foreach (var u in plan.Stack)
                    if (u != null && GodotObject.IsInstanceValid(u) && !slideFrom.ContainsKey(u))
                        slideFrom[u] = u.Position;
            else if (plan.Kind == PushPlanKind.WeaponMove && plan.Weapon is Node2D wn && !slideFromStatic.ContainsKey(plan.Weapon))
                slideFromStatic[plan.Weapon] = wn.Position;
            else if (plan.Kind == PushPlanKind.FacilityMove && plan.Facility is Node2D fn && !slideFromStatic.ContainsKey(plan.Facility))
                slideFromStatic[plan.Facility] = fn.Position;
        }

        foreach (var plan in movePlans)
        {
            switch (plan.Kind)
            {
                case PushPlanKind.StackMove:
                    MoveStackByPush(plan.Stack, plan.Destination);
                    break;
                case PushPlanKind.WeaponMove:
                    if (!plan.Weapon.MoveByPhysicsPush(plan.Destination, gameManager?.weaponManager))
                        GD.PushWarning("[Push] 兵器提交失败(虚拟预检已通过): " + plan.Weapon?.GetType().Name);
                    break;
                case PushPlanKind.FacilityMove:
                    if (!plan.Facility.MoveByPhysicsPush(plan.Destination))
                        GD.PushWarning("[Push] 设施提交失败(虚拟预检已通过): " + plan.Facility?.GetType().Name);
                    break;
            }
        }
        FlushPushEffects(effects, gameManager);
        PlayPushVisuals(movePlans, slideFrom, effects, slideFromStatic);
    }

    // 原文#11/#12：推动滑动级联动画 + 碰撞/受击闪白抖动。纯代码生成，无外部素材。
    private static void PlayPushVisuals(List<PushMovePlan> movePlans,
        Dictionary<Infantry, Vector2> slideFrom, List<PushEffect> effects,
        Dictionary<GodotObject, Vector2> slideFromStatic = null)
    {
        float delay = 0f;
        foreach (var plan in movePlans)
        {
            if (plan.Kind == PushPlanKind.StackMove && plan.Destination != null)
            {
                var to = plan.Destination.Position;
                foreach (var u in plan.Stack)
                {
                    if (u == null || !GodotObject.IsInstanceValid(u) || !u.IsInsideTree()) continue;
                    if (!slideFrom.TryGetValue(u, out var from)) continue;
                    if ((to - from).LengthSquared() < 1f) continue;
                    var tw = u.CreateTween();
                    tw.TweenInterval(delay);
                    tw.TweenProperty(u, "position", to, 0.18f)
                        .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
                }
            }
            delay += 0.06f;
        }
        // 兵器/设施滑动（同格级联节奏）
        if (slideFromStatic != null)
        {
            foreach (var plan in movePlans)
            {
                GodotObject obj = plan.Kind == PushPlanKind.WeaponMove ? plan.Weapon
                    : plan.Kind == PushPlanKind.FacilityMove ? plan.Facility : null;
                if (obj == null || !GodotObject.IsInstanceValid(obj) || obj is not Node2D nd) continue;
                if (!slideFromStatic.TryGetValue(obj, out var from)) continue;
                if (plan.Destination == null) continue;
                var to = plan.Destination.Position;
                if ((to - from).LengthSquared() < 1f) continue;
                var tw = nd.CreateTween();
                tw.TweenProperty(nd, "position", to, 0.18f)
                    .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            }
        }
        foreach (var fx in effects)
        {
            var u = fx.Unit;
            if (u == null || !GodotObject.IsInstanceValid(u) || !u.IsInsideTree()) continue;
            bool hit = fx.Kind == PushEffectKind.Damage || fx.Kind == PushEffectKind.Destroy
                || fx.Kind == PushEffectKind.LockToOneHp;
            if (!hit) continue;
            var basePos = u.Position;
            var tw = u.CreateTween();
            tw.TweenProperty(u, "modulate", new Color(2f, 2f, 2f), 0.06f);
            tw.TweenProperty(u, "modulate", new Color(1f, 1f, 1f), 0.12f);
            for (int k = 0; k < 3; k++)
            {
                tw.TweenProperty(u, "position", basePos + new Vector2(2, 0), 0.03f);
                tw.TweenProperty(u, "position", basePos - new Vector2(2, 0), 0.03f);
            }
            tw.TweenProperty(u, "position", basePos, 0.03f);
        }
    }

    private static void FlushPushEffects(List<PushEffect> effects, GameManager gameManager)
    {
        var touched = new HashSet<Infantry>();
        foreach (var fx in effects)
        {
            var u = fx.Unit;
            switch (fx.Kind)
            {
                case PushEffectKind.PayPushCost:
                    ApplyPayPushCostEffect(u, fx, gameManager);
                    break;
                case PushEffectKind.FundsDelta:
                    if (!string.IsNullOrEmpty(fx.Team))
                        gameManager?.SetTeamFunds(fx.Team, gameManager.GetTeamFunds(fx.Team) - (int)fx.Amount);
                    break;
                case PushEffectKind.ConsumePrimaryAmmo:
                    if (u != null && u.health > 0 && fx.Amount > 0)
                        u.TryConsumeAmmo(AmmoKind.Primary, (int)fx.Amount);
                    break;
                case PushEffectKind.RawFuelCost:
                    if (u != null && u.health > 0) u.fuel = Mathf.Max(0, u.fuel - (int)fx.Amount);
                    break;
                case PushEffectKind.RawHealthCost:
                    if (u != null && u.health > 0) u.health = Mathf.Max(1, u.health - (int)fx.Amount);
                    break;
                case PushEffectKind.TargetFuelDelta:
                    if (u != null && u.health > 0)
                        u.fuel = (int)Math.Clamp((long)u.fuel + fx.Amount, 0L, int.MaxValue);
                    break;
                case PushEffectKind.TargetAmmoDelta:
                    if (u != null && u.health > 0) u.AddAmmo(AmmoKind.Primary, (int)fx.Amount);
                    break;
                case PushEffectKind.MaxHealthDelta:
                    if (u != null && u.health > 0)
                        u.maxHealth = (int)Math.Clamp((long)u.maxHealth + fx.Amount, 1L, int.MaxValue);
                    break;
                case PushEffectKind.LockToOneHp:
                    if (u != null && u.health > 0) u.health = 1;
                    break;
                case PushEffectKind.Heal:
                    if (u != null && u.health > 0)
                        u.health = (int)Math.Min(fx.CanOverMaxHealth ? int.MaxValue : u.GetEffectiveMaxHealth(),
                            Math.Min(int.MaxValue, (long)u.health + fx.Amount));
                    break;
                case PushEffectKind.Damage:
                    if (u == null || u.health <= 0) break;
                    long damaged = (long)u.health - fx.Amount;
                    if (damaged <= 0 && fx.CanDestroy)
                    {
                        DestroyPushedUnit(u, gameManager);
                        break;
                    }
                    u.health = (int)Math.Max(1L, damaged);
                    break;
                case PushEffectKind.Destroy:
                    if (u != null && u.health > 0) DestroyPushedUnit(u, gameManager);
                    break;
                case PushEffectKind.DamageWeapon:
                    if (fx.Weapon != null && fx.Weapon.health > 0)
                    {
                        fx.Weapon.health -= (int)fx.Amount;
                        if (fx.Weapon.health <= 0) fx.Weapon.QueueFree();
                    }
                    break;
                case PushEffectKind.DamageFacility:
                    if (fx.Facility != null)
                    {
                        // 设施有血量体系（health/maxHealth，见生产模块）：先扣血，归零才移除
                        fx.Facility.health -= (int)fx.Amount;
                        if (fx.Facility.health <= 0)
                        {
                            var parent = fx.Facility.ParentGrid;
                            if (parent != null && parent.city == fx.Facility) parent.city = null;
                            fx.Facility.QueueFree();
                        }
                    }
                    break;
                case PushEffectKind.AmmoKindCost:
                    if (u != null && u.health > 0 && fx.Amount > 0)
                        u.TryConsumeAmmo(fx.AmmoKind, (int)fx.Amount);
                    break;
            }
            if (u != null) touched.Add(u);
        }
        foreach (var unit in touched)
            if (IsLivingUnit(unit)) unit.UpdateHpLabel();
    }

    private static void ApplyPayPushCostEffect(Infantry payer, PushEffect fx, GameManager gm)
    {
        if (payer == null || fx.Amount <= 0 || fx.Resource == PushResourceType.None) return;
        int cost = (int)fx.Amount;
        switch (fx.Resource)
        {
            case PushResourceType.Funds:
                gm?.SetTeamFunds(payer.team, gm.GetTeamFunds(payer.team) - cost);
                break;
            case PushResourceType.Health:
                payer.health = Mathf.Max(1, payer.health - cost);
                break;
            case PushResourceType.Fuel:
                payer.fuel = Mathf.Max(0, payer.fuel - cost);
                break;
            case PushResourceType.PrimaryAmmo:
                payer.TryConsumeAmmo(AmmoKind.Primary, cost);
                break;
            case PushResourceType.PushAmmo:
                payer.TryConsumeAmmo(AmmoKind.Push, cost);
                break;
        }
    }
}
