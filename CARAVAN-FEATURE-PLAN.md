# Pawn and caravan control: draft feature plan

Status: proposal only; no caravan features implemented by this document. Runtime QA remains pending for the existing bridge. Vehicle convoys and SOS2 travel are separate optional integrations, not dependencies of the normal-caravan work.

## Existing capabilities

The bridge exposes pawn inspection, drafting, local movement, attacks, prioritized jobs, equipment changes, work priorities, schedules and policies. Caravans can be listed, eligible pawns inspected, and a caravan immediately created toward a settlement or world tile. The existing immediate departure command takes only carried supplies; it does not implement normal packing. Existing caravan code needs validation review before extending it (ownership, pawn eligibility, destination validity and route reachability).

## Phase 1: Inspect and direct ordinary caravans

Proposed operations on `rimworld_caravan`:

- `detail`: resolve a player-owned caravan by stable ID; return members, inventories, current tile, destination, movement state, arrival action, capacity and supply/health warnings. Include ETA and food estimates only where the game provides reliable values; otherwise report unavailable with a reason.
- `destinations`: list player colonies, quest sites and settlements using stable world-object IDs and layer-aware tile references. Describe available actions and blockers rather than calling every listed tile reachable.
- `route_preview`: validate destination, layer and route; report reachability, estimated travel and available arrival actions without changing anything.
- `set_destination`: start or redirect a caravan using the game's path follower and a valid arrival action. Support a colony, site, settlement or tile; never silently replace an interaction with a plain move.
- `stop`: halt travel while retaining the caravan and its inventory.
- `return_home`: select an explicit player colony and use the same validated route/arrival flow. Require an explicit target when multiple homes are possible.

Use the game's ordinary travel mechanics. No teleportation, instant entry, or spawning colonists directly on destination maps.

## Phase 2: Arrival and map interaction

- `arrival_options`: inspect legal actions at a destination, with reasons for disabled actions.
- `enter_map`: enter an existing player colony using the normal caravan arrival mechanism. Check ownership, valid entry and destination existence again on arrival.
- `visit_site`: select a supported quest/site arrival action; allow the game to generate a map where required. Expose unsupported site-specific actions explicitly.
- `attack_settlement`: separate explicit hostile action with preview and confirmation; never infer an attack from a movement command.
- `trade`: add world-caravan trader resolution to the existing trade session API; preserve its quantity review and confirmation flow.

Travel completion is asynchronous: return an order identifier and observable caravan state. A successful command means an order was accepted, not that the caravan arrived. Handle sites disappearing, quest expiry, impassable routes and arrival rejection with actionable status.

## Phase 3: Normal formation, cargo and group management

- `formation_preview`: choose source map, exact pawn IDs, animals, prisoners where supported, and item IDs/counts. Show eligibility, capacity, estimated food, route and remaining colony population before committing.
- `form_loaded`: initiate the game's gathering/packing caravan workflow with the validated manifest. Do not bypass reservations or remove cargo instantly. Expose gathering, packing, departing, blocked and cancelled states using game state rather than a fixed timer.
- `formation_status` / `cancel_formation`: inspect progress and cancel safely without deleting pawns/items. Validate whether the game's underlying job/lord supports cancellation at each stage.
- `split_preview` / `split`: divide members and cargo with quantity, capacity, ownership and mobile-colonist checks.
- `merge_preview` / `merge`: combine compatible player caravans at the same location using game mechanics.

Retain the existing immediate departure action under an explicit description; normal loaded formation should be the preferred path for supply runs.

## Contract and implementation requirements

- Stable pawn, caravan, world-object and item IDs; include world layer in tile references. Names may aid discovery but ambiguous names must fail with candidates.
- Explicit source map: never assume the currently viewed colony for an off-map operation.
- Validate ownership, availability, counts, pawn state, destination and operation prerequisites before mutation. Revalidate after a preview because the simulation may change.
- Inspect how current map-only pawn commands behave for caravan members; reject invalid commands clearly instead of trying to use local-map jobs off-map.
- Use preview/confirmation and the existing save safeguards for departure, hostile arrivals, cargo transfers and split/merge. Route and stop operations should follow existing reversible-command conventions.
- Reuse the bridge's main-thread command execution. Avoid duplicate formation on retries; use operation IDs or an equivalent deduplication mechanism for multi-step creation.
- Preserve inventory, relationships, quests and vanilla ownership transitions. Respect modded restrictions where the normal game flow already enforces them; report unsupported behavior instead of promising compatibility.

## Acceptance criteria and future QA

No gameplay testing is authorized by this draft. Before release, a separately authorized QA pass should cover:

- Colonist/animal eligibility, downed or unavailable pawns, foreign pawns, duplicate IDs and malformed manifests.
- Valid/invalid routes, occupied/removed destinations, different world layers and multiple colonies.
- Departure, redirect, stop, return, colony entry, quest-site entry and blocked arrivals.
- Food/capacity warnings, partial loading, inventory accounting and cancellation without loss or duplication.
- Split/merge edge cases, trade transfers, save/reload during travel and loading, repeated requests and stale previews.
- Regression of existing pawn commands and current caravan operations; representative installed-mod interactions.

Suggested delivery order: phase 1, friendly colony entry from phase 2, phase 3 formation/loading, then site interactions, split/merge and world trading. This limits each implementation and QA pass to a reviewable scope.

## Separate potential add-ons

### Vehicle convoys

Optional Vehicle Framework integration: discover vehicles and seating/crew requirements, assign passengers and cargo, inspect fuel and terrain/path restrictions, preview and form ground convoys, route/stop/return and enter destination maps. Boats and aircraft may need separate adapters. Inspect the installed framework's public API and use its own loading/travel mechanisms. This is a separate project-sized feature with its own compatibility matrix and QA; do not treat vehicles as ordinary pack animals.

### Save Our Ship 2 travel

Optional SOS2 integration: inspect ships, crew and destination capabilities; preview boarding/loading and supported launch, landing or transfer operations; enforce life-support, power, fuel and ship-state requirements through SOS2's own systems. Orbit, space maps and ship encounters must use mod-specific transitions rather than overland caravan entry. Determine stable APIs and exact supported SOS2 version before specifying command schemas. This add-on requires independent design, implementation and QA and is not included in the caravan phases above.
