# AutoRim

Natural-language control of a running RimWorld colony, through MCP.

Ask for what you want — "who should be cooking?", "set up a hunting run", "queue electricity
research", "wall off the north approach" — and it happens in the live game.

Built for **RimWorld 1.6** (developed against 1.6.4871).

https://steamcommunity.com/sharedfiles/filedetails/?id=3771536321

---

## How it works

RimWorld has no external API, so the only way to reach live colony state is from inside the
game process. AutoRim is therefore two pieces:

```
Claude  ──MCP/stdio──►  server/  ──HTTP on 127.0.0.1──►  AutoRim mod (inside RimWorld)
                                                              │
                                                     main-thread dispatcher
                                                              │
                                                      Verse / RimWorld API
```

- **`mod-src/`** — a C# mod (`net472`) that runs a loopback-only HTTP listener and executes
  commands against the game's own APIs.
- **`server/`** — a TypeScript MCP server that Claude talks to, forwarding to the mod.

Two design choices worth knowing:

**No Harmony.** RimWorld auto-instantiates every `GameComponent` found in a loaded mod
assembly, which gives a per-frame hook without patching anything. Alerts and events are polled
rather than intercepted. Nothing but `AutoRim.dll` is loaded into the game — even the JSON
layer is hand-rolled, because shipping a second copy of a common library into RimWorld's
AppDomain is a well-known source of conflicts with other mods.

**Socket threads never touch the game.** Unity and RimWorld are not thread-safe. Requests are
queued and executed inside `GameComponentUpdate` under a per-frame time budget; the socket
thread only ever waits on a handle. This is the load-bearing part of the design.

---

## Install

```powershell
# 1. Build and install the mod
.\scripts\deploy.ps1

# 2. Build the MCP server
cd server
npm install
npm run build
```

Then in RimWorld: **Mods** → enable **AutoRim - MCP Bridge** → restart when prompted.

Verify the bridge while a colony is loaded:

```powershell
.\scripts\smoke.ps1                  # basic checks
.\scripts\smoke.ps1 -Concurrency 50  # also stress the main-thread dispatcher
```

Register the MCP server with Claude Code (`.mcp.json` in this repo already does this):

```json
{
  "mcpServers": {
    "rimworld": {
      "command": "node",
      "args": ["server/dist/index.js"]
    }
  }
}
```

> The mod's DLL is memory-mapped while RimWorld runs, so **close the game before redeploying**.

---

## Safety

Most of what the tools do is reversible through normal play, and runs immediately. Anything
that is not sits behind an explicit gate.

**Destructive actions require `confirm: true`.** Without it the call changes nothing and
returns a preview describing exactly what would happen:

| Action | What it does |
|---|---|
| `designate.slaughter` | Kills colony animals |
| `designate.deconstruct` / `uninstall` | Tears down buildings |
| `designate.strip` | Strips gear from pawns or corpses |
| `designate.release_animal` | Releases tamed animals to the wild |
| `health.add_surgery` | Irreversible, and can maim or kill |
| `prisoners.release` / `execute` | Prisoner leaves for good, or dies |
| `trade.execute` | Goods and silver change hands |
| `caravan.form` | Pawns leave the map |

Alongside that:

- **A rolling restore point.** Before any confirmed destructive action the mod saves to
  `AutoRim-safety` (rate-limited to once a minute). One save, overwritten each time, so there
  is always a "just before it did something" state to fall back to. On by default — leave it on
  if you play permadeath.
- **`control.save` cannot overwrite your saves.** Names are forced into an `AutoRim-` slot.
- **An audit log** at `…/Config/AutoRim/actions.log`, plus an in-game message each time.
- **A kill switch**: Options → Mod settings → AutoRim, or the `control.disable_bridge` tool.
- **Loopback only**, with a random token generated on first run.

---

## Tools

24 tools, ~110 commands. Each tool takes an `action`.

| Tool | Covers |
|---|---|
| `rimworld_colony` | snapshot, alerts, letters, resources, power |
| `rimworld_pawns` | list, detail, rename, area, medical care, hostility response |
| `rimworld_work` | work priorities, bulk assignment |
| `rimworld_jobs` | draft, move, attack, prioritise, stop |
| `rimworld_designate` | hunt, mine, chop, harvest, tame, haul, forbid, claim, smooth + destructive ones |
| `rimworld_build` | place, lines and rectangles, placement checks, what's buildable |
| `rimworld_research` | current, list, set, stop, suggest |
| `rimworld_bills` | workbench production orders |
| `rimworld_zones` | stockpiles and growing zones |
| `rimworld_storage` | priorities and filters |
| `rimworld_areas` | allowed areas |
| `rimworld_policies` | apparel, food, drug |
| `rimworld_schedule` | timetables |
| `rimworld_health` | surgery |
| `rimworld_animals` | training, masters |
| `rimworld_prisoners` | interaction modes, release, execution |
| `rimworld_trade` | full trade session |
| `rimworld_caravan` | list, sendable, form |
| `rimworld_world` | factions, settlements, quests |
| `rimworld_ideology` | ideoligions (Ideology DLC) |
| `rimworld_map` | map info, thing search, ASCII region view |
| `rimworld_query` | definition lookup |
| `rimworld_analyze` | best pawn for a job, idle pawns, bottlenecks, threats |
| `rimworld_control` | speed, save, notify, bridge status |

### Bill ingredient filters

`rimworld_bills` keeps the existing compact list by default. Request `details: true` to
inspect a bill's allowed ingredient definitions, recipe-compatible definitions, special
filter states, durability and quality ranges. Search radius and allowed worker skill range
are also exposed. These are live game definitions, so modded equipment uses its exact defName.

```json
{"action":"list","bench":490020,"details":true}
```

Use `set` to edit one bill's `ingredientFilter`. Omitted fields stay unchanged. For an
explicit whitelist, clear the existing allowed ingredients first with `reset: "nothing"`,
then supply `allowDefs`. For example, after checking that these definitions are allowed by
the selected washing recipe:

```json
{
  "action": "set",
  "bench": 490020,
  "index": 0,
  "ingredientFilter": {
    "reset": "nothing",
    "allowDefs": ["Apparel_FlakVest", "Apparel_PowerArmor"],
    "allowTainted": true,
    "allowClean": false,
    "hitPoints": {"min": 0, "max": 1},
    "quality": {"min": "Awful", "max": "Legendary"}
  }
}
```

`allowDefs`, `disallowDefs`, `allowCategories`, and `disallowCategories` accept exact
definition names. `reset: "recipe"` restores the recipe's allowed ingredients. Taint and
clean-apparel flags filter the selected apparel types; they do not add types back to a
whitelist. Recipe restrictions still apply, including restrictions imposed by mods.
Within a single edit, reset is applied first, then allows, then disallows; a disallow wins
if the same definition is selected by both lists or categories.
Durability uses fractions from 0 to 1, and quality uses the ordered names Awful, Poor,
Normal, Good, Excellent, Masterwork, Legendary. Supply both ends of any range.

Bill-level `ingredientSearchRadius` accepts 0–999 cells and `allowedSkillRange` accepts
integer levels 0–20, for example `{"min": 6, "max": 20}`. `worker: null` clears a pawn
restriction. Filter editing applies to `set`; create a bill with `add` first if needed.
Apply the change to each relevant batch bill separately. There is no dynamic market-value
or smeltability predicate: build the explicit whitelist from inspected definitions.

`rimworld_query` with `action: "thing_info"` now reports `smeltable`, `destroyable`,
`tradeability`, `isApparel`, `thingCategories`, and `apparelTags` to help choose that
whitelist. `marketValueStatBase`, when present, is an explicitly declared definition base
stat, not the current value of an individual item: material, quality, damage, and other
modifiers can change its actual value. A `smeltable` flag describes the definition; it
does not automatically create a filter rule or guarantee a particular recycling recipe.

Both the game DLL and companion MCP server must be updated for these fields. Restart
RimWorld to load a new DLL and restart/reconnect the MCP client to refresh its tool schema.

### Two that carry more weight than their size suggests

**`rimworld_query`** — the game's internal names rarely match what a person says. When
something is rejected as not found or ambiguous, the error carries candidates; this tool is how
you resolve them properly.

**`rimworld_analyze best_pawn_for`** — returns the whole ranking with reasoning, not a winner.
"Ivy, cooking 8 with a burning passion, only on two other jobs" is something you can disagree
with; "assign Ivy" is not.

---

## Known limitations

- **Caravans leave with only what pawns carry.** `caravan.form` uses the immediate-departure
  path. Assembling a cargo manifest is the game's multi-step loading flow and is not covered —
  use the in-game Form Caravan dialog for a real supply run.
- **Designations act on the map currently on screen**, because that is what RimWorld's
  `Designator` classes bind to. Reads accept a `map` index; designations do not.
- **Only Core + Ideology are exercised.** Royalty, Biotech, Anomaly and Odyssey commands are
  gated on `ModsConfig` and return a clean `DLC_NOT_ACTIVE` rather than failing oddly, but they
  are untested here.
- **Redeploying needs the game closed**, since Windows keeps the loaded DLL mapped.

---

## Repository layout

```
mod-src/           C# mod source
  AutoRim/
    Bridge/        HTTP listener, JSON, main-thread dispatcher
    Core/          command registry, safety gate, def resolver, identity
    Read/          state serializers
    Commands/      one file per subsystem
dist-mod/AutoRim/  deployable mod folder (About/ + Assemblies/)
server/            TypeScript MCP server
scripts/           deploy.ps1, smoke.ps1, rpc.ps1
```

`scripts/rpc.ps1` sends a single command and prints the raw response with its size — useful
when developing, since keeping reads small is a hard constraint here rather than an
afterthought.

```powershell
.\scripts\rpc.ps1 colony.snapshot
.\scripts\rpc.ps1 analyze.best_pawn_for '{"work":"Cooking"}'
```

## Storage condition filters

`rimworld_storage` now supports `set_filter` for stockpile zones and storage buildings. Use the existing `zone` or `building` target and a `filter` object:

```json
{"action":"set_filter","zone":"Armour","filter":{"quality":{"min":"Good","max":"Legendary"},"hitPoints":{"min":0.5,"max":1},"allowTainted":false,"allowClean":true,"allowBiocodedWeapons":false,"allowNonBiocodedWeapons":true,"allowBiocodedApparel":false,"allowNonBiocodedApparel":true}}
```

Durability is a fraction from 0 to 1. Quality endpoints use exact names from Awful through Legendary. Both endpoints are required when changing a range. Omitted fields preserve existing values; all supplied fields are validated before committing. These restrictions intersect with the existing item allowlist and do not enable additional item types. Biocoding is controlled separately for weapons and apparel using the game's native special filters, independent of who an item is coded to. `get_settings` returns the complete allowed defNames and all these condition controls under `filter`. Missing special filter definitions are omitted from inspection and rejected when explicitly edited.

Restart the game and reconnect the MCP server after installing the updated bridge/server. Runtime QA remains pending.
