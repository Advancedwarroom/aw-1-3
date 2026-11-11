# Physics Push System

The push system is configured per entity through Godot Export properties. The current runtime supports infantry stacks and single-tile pushable weapons/facilities.

## Trigger modes

- `Disabled`: no push behavior.
- `Attack`: resolve a push after the normal attack damage/counter/death pipeline.
- `SeparateAction`: expose a Push button in the unit action menu and let the player select a target tile.
- `Movement`: when moving along a path, push eligible infantry, weapons, or facilities out of blocking tiles in the direction of travel. The mover continues only when the blocking tile is cleared.

For a separate action:
- `pushActionCountsAsAction = false` records the per-turn push use without changing `hasActed` or `state`.
- `pushActionCountsAsAction = true` marks the unit as acted.
- `pushOncePerTurn` and `pushCooldownTurns` control reuse.
- `pushCanBeUsedAfterMoving` controls whether movement blocks the action.

## Weight and stacks

- `weight` is a floating-point value clamped to 0–9999, with 0.01 inspector precision.
- `weightScalesWithHealth` scales effective weight by current HP / maximum HP.
- All infantry on one tile form a single stack. Stack weight is the sum of its members' effective weights; the whole stack moves together.
- Equal-weight behavior is controlled by each entity's `equalWeightPushMode`. Both sides allowing simultaneous movement means the push proceeds; both sides set to stop means it stops; mixed settings resolve 50/50 once for that link.
- `pushCanTargetInfantry`, `pushCanTargetWeapons`, and `pushCanTargetFacilities` independently filter target entity classes.
- `pushMaxChainUnits` limits the number of infantry in a chain without splitting co-located stacks. `pushMaxChainStacks` continues to limit occupied tiles.
- `pushChainWeightMode` controls force transmission from the initiating pusher: constant force, add each displaced stack's mass, or subtract each displaced stack's mass. `pushChainMaxAccumulatedWeight` caps accumulated force when positive.
- Weapon and facility weights use the same health-scaling model. They can be pushed when `pushableByPhysics` is enabled. Multi-tile weapons are intentionally excluded from displacement until footprint-safe movement is implemented.

## Direction, chain, costs and effects

- `pushDirectionMode` supports away-from-pusher, fixed cardinal direction, and opposite-sides drag. In opposite-sides mode, the target is displaced along the push vector and the pusher retreats in the reverse direction; the pusher stops at blockers and applies its own invalid-tile destruction setting.
- `pushDistance` is the maximum displacement; `pushMinDistance` is the minimum. Attack-triggered pushes use the maximum. Separate actions use the maximum when `pushAutoDistance` is enabled; otherwise a popup lets the player choose a distance in the configured range.
- When `pushForceScalesWithDistance` is enabled, force uses the actual selected displacement distance, not always the maximum.
- `pushMaxChainStacks` and `pushMaxChainUnits` limit chain length.
- Resource costs can use funds, HP, fuel, primary ammo, or any configured `AmmoKind` through `pushCostAmmoKind`.
- Target ammo changes, rebound costs, and transfer costs each select their own `AmmoKind`, so the complete shared ammunition catalog is available rather than primary ammo only.
- Target effects include damage, one-HP locking, healing, max-HP changes, fuel changes, and primary-ammo changes.
- Infantry stacks support configurable rebound, distance mitigation, and anti-gravity transfer Export settings.
- `pushSelfDestructOnInvalidIntermediateTile` is configured on each infantry entity: enabled destroys it on any invalid intermediate tile; disabled only destroys it if the final requested tile is invalid.

## Current limitations

- Multi-tile weapons cannot yet be moved by push.
- Facility/weapon chain stacks and mixed infantry/weapon/facility mass transmission are not yet unified into one chain solver.
- Facility and weapon target effects currently support their health damage/one-HP behavior only; infantry has the broader resource and healing effects.
- Rebound/transfer are implemented for infantry stacks, not static entities.
- Visual push animation, dedicated direction-picker UI, opposite-sides drag mode, fragile collision outcomes, and full simultaneous rebound/mitigation/transfer conflict arbitration remain future work.

This is a staged implementation; run the Android CI workflow and in-game scenarios before merging.
