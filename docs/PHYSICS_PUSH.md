# Physics Push System

The push system is configured per entity through Godot Export properties. The current runtime supports infantry stacks and single-tile pushable weapons/facilities.

## Trigger modes

- `Disabled`: no push behavior.
- `Attack`: resolve a push after the normal attack damage/counter/death pipeline.
- `SeparateAction`: expose a Push button in the unit action menu and let the player select a target tile.

For a separate action:
- `pushActionCountsAsAction = false` records the per-turn push use without changing `hasActed` or `state`.
- `pushActionCountsAsAction = true` marks the unit as acted.
- `pushOncePerTurn` and `pushCooldownTurns` control reuse.
- `pushCanBeUsedAfterMoving` controls whether movement blocks the action.

## Weight and stacks

- `weight` is a float.
- `weightScalesWithHealth` scales effective weight by current HP / maximum HP.
- All infantry on one tile form a single stack. Stack weight is the sum of its members' effective weights; the whole stack moves together.
- Equal-weight behavior is controlled by each entity's `equalWeightPushMode`. Both sides allowing simultaneous movement means the push proceeds; both sides set to stop means it stops; mixed settings resolve 50/50 once for that link.
- Weapon and facility weights use the same health-scaling model. They can be pushed when `pushableByPhysics` is enabled. Multi-tile weapons are intentionally excluded from displacement until footprint-safe movement is implemented.

## Direction, chain, costs and effects

- `pushDirectionMode` selects away-from-pusher or fixed cardinal direction.
- `pushDistance`, `pushMaxChainStacks`, and `pushForceScalesWithDistance` tune reach and force.
- Resource costs can use funds, HP, fuel, or primary ammo.
- Target effects include damage, one-HP locking, healing, max-HP changes, fuel changes, and primary-ammo changes.
- Rebound, mitigation, and anti-gravity transfer each have their own Export settings.
- `pushSelfDestructOnInvalidIntermediateTile` is configured on each infantry entity: enabled destroys it on any invalid intermediate tile; disabled only destroys it if the final requested tile is invalid.

## Current limitations

- Multi-tile weapons cannot yet be moved by push.
- Facility/weapon chain stacks and mixed infantry/weapon/facility mass transmission are not yet unified into one chain solver.
- Facility and weapon target effects currently support their health damage/one-HP behavior only; infantry has the broader resource and healing effects.
- Rebound/transfer are implemented for infantry stacks, not static entities.
- Visual push animation, dedicated direction-picker UI, advanced target class whitelist/blacklist, and automated conflict resolution for simultaneous multi-module reactions remain future work.

This is a staged implementation; run the Android CI workflow and in-game scenarios before merging.
