# Physics Push System

The push system is configured per entity through Godot Export properties. The current runtime supports infantry stacks and single-tile pushable weapons/facilities.

## Trigger modes

- `Disabled`: no push behavior.
- `Attack`: resolve a push after the normal attack damage/counter/death pipeline.
- `SeparateAction`: expose a Push button in the unit action menu and let the player select a target tile.
- `Movement`: when moving along a path, push eligible infantry, weapons, or facilities out of blocking tiles in the direction of travel. The mover continues only when the blocking tile is cleared.

For a separate action:
- Target range uses `pushMinTargetRange`–`pushRange`. At exactly one-tile range, a single eligible target is auto-selected; multiple eligible targets still require selection.
- `pushActionCountsAsAction = false` records the per-turn push use without changing `hasActed` or `state`.
- `pushAutoDistance = false` opens a distance picker only when more than one distance is available; a fixed one-tile distance executes directly.
- `pushActionCountsAsAction = true` marks the unit as acted.
- `pushOncePerTurn` and `pushCooldownTurns` control reuse.
- `pushCanBeUsedAfterMoving` controls whether movement blocks the action.

## Weight and stacks

- `weight` is a floating-point value clamped to 0–9999, with 0.01 inspector precision.
- `weightScalesWithHealth` scales effective weight by current HP / maximum HP.
- All infantry on one tile form a single stack. Stack weight is the sum of its members' effective weights; the whole stack moves together.
- Equal-weight behavior is controlled by the two adjacent stacks' `equalWeightPushMode`. Both sides allowing simultaneous movement means both move; both sides set to stop means the push stops; mixed settings resolve 50/50 once for that link.
- `pushCanTargetInfantry`, `pushCanTargetWeapons`, and `pushCanTargetFacilities` independently filter target entity classes.
- `pushChainRules` is an indexed Resource array. Entry 0 controls the first displaced tile, entry 1 the second, and so on; each entry can override team policy, target classes, whitelist, and blacklist. Missing entries inherit the global Export settings. These rules are included in the shared Export persistence codec.
- `pushMaxChainUnits` limits the number of infantry in a chain without splitting co-located stacks. `pushMaxChainStacks` continues to limit occupied tiles.
- `pushChainWeightMode` controls force transmission from the initiating pusher: constant force, add each displaced stack's mass, or subtract each displaced stack's mass. `pushChainMaxAccumulatedWeight` caps accumulated force when positive.
- Weapon and facility weights use the same health-scaling model. They can be pushed when `pushableByPhysics` is enabled. Direct multi-tile weapon pushes validate the complete destination footprint.

## Direction, chain, costs and effects

- `pushDirectionMode` supports away-from-pusher, fixed eight-way grid direction, and opposite-sides drag. Transfer vectors also support eight-way grid directions. In opposite-sides mode, the target is displaced along the push vector and the pusher retreats in the reverse direction; the pusher stops at blockers and applies its own invalid-tile destruction setting.
- `pushDistance` is the maximum displacement; `pushMinDistance` is the minimum. Attack-triggered pushes use the maximum. Separate actions use the maximum when `pushAutoDistance` is enabled; otherwise a popup lets the player choose a distance in the configured range.
- When `pushForceScalesWithDistance` is enabled, force uses the actual selected displacement distance, not always the maximum.
- `pushMaxChainStacks` and `pushMaxChainUnits` limit chain length.
- Resource costs can use funds, HP, fuel, primary ammo, or any configured `AmmoKind` through `pushCostAmmoKind`.
- Push uses three dedicated shared ammunition kinds: `PushRebound`, `PushMitigation`, and `AntiGravityTransfer`. They are part of the unit ammo read/write/refill API, paid resupply/sacrifice, facility supply, devour effects, terrain per-turn ammo/max-ammo changes, and generic Export saves.
- Push costs and target ammo changes can select any `AmmoKind`; rebound, mitigation, and transfer default to their dedicated ammo types.
- Infantry target effects include damage, one-HP locking, healing, max-HP changes, fuel/ammo changes, and team-funds deltas. Health, fuel, ammo, and funds have independent over-max controls.
- Infantry stacks support configurable rebound, distance mitigation, and anti-gravity transfer Export settings. Mitigation can protect HP, funds, fuel, and a selected ammo type using fixed and percentage reductions. Its ammo is charged once per unit per push action when it actually reduces displacement or a resource loss.
- Rebound checks the pusher's original distance from the rebounder, team filters, and a fresh weight comparison against the initiating stack. It can restore the target to its origin or retreat the pusher one tile to let the target rebound into the pusher's old tile. Rebound supports enemy/friendly/ally filters, health/fuel/funds/ammo costs and reflected damage/fuel/ammo effects on the pusher.
- `pushSelfDestructOnInvalidIntermediateTile` is checked on each displaced infantry entity: enabled destroys it on any invalid intermediate tile; disabled only destroys it if the final requested tile is invalid. The pusher's `pushDestroyOnProperty` option destroys displaced units on any property tile, regardless of ownership.

## Current limitations

- Directly pushed multi-tile weapons use full-footprint collision checks. Multi-tile weapons are not yet supported as intermediate obstacles in mixed infantry/static domino chains.
- Facility/weapon chain stacks and mixed infantry/weapon/facility mass transmission are not yet unified into one chain solver.
- Weapon and facility push targets support health damage, one-HP locking, healing, and maximum-health changes. Weapons also map the six compatible shared ammo kinds to their native ammo slots. Infantry additionally supports fuel and all shared ammo kinds.
- When a pushed stack collides with another infantry stack, heavier mass wins and destroys the lighter stack; equal mass follows the two sides' simultaneous/stop rules. `pushChainEnabled=false` makes an occupied link stop both sides.
- Moving weapons/facilities can overlap infantry when `pushBlocksInfantryWhenOverlapped=false`. When enabled, the moving static entity resolves the occupied destination by weight/equal-weight rules; the stationary tile occupant is not treated as the moving entity.
- Infantry `pushFragileEnabled` applies health/fuel/ammo impact to survivors of unit-stack and static-obstacle collisions. Health supports fixed damage (including negative healing), primary-attack damage calculation, or a health/funds conversion; the attack-calculation mode uses the initiating pusher's primary damage formula. Fragile health, fuel, and ammo effects have independent over-max controls.
- Rebound/transfer are implemented for infantry stacks, not static entities.
- Visual push animation, continuous free-angle 0–360° direction selection (the current grid solver is eight-way), manual rebound/transfer selection, and full simultaneous rebound/mitigation/transfer conflict arbitration remain future work.
- Chain force and per-link policy now apply to infantry stacks; a single unified solver for mixed infantry/weapon/facility chains is still outstanding.

This is a staged implementation; run the Android CI workflow and in-game scenarios before merging.
