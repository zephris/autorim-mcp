# Bill filter extension: QA status

This branch adds bill ingredient-filter inspection and editing to the C# bridge and MCP server, plus definition information to help choose ingredients.

## Completed checks

- C# compilation against RimWorld 1.6.4850 reference assemblies.
- TypeScript compilation of the companion server.
- Source review of validation-before-mutation and special-filter preservation.
- Compatible dependency updates; npm audit reported zero known vulnerabilities at build time.

## Pending before proposing upstream

No gameplay or live API behavior tests have been performed for these changes. Do not treat successful compilation as runtime validation.

- Inspect ordinary, bulk, and modded recipe bills with detailed output enabled.
- Exercise definition/category allowlists, disallow precedence, and recipe-ineligible input rejection.
- Verify tainted-only and clean-only behavior, including preservation across ingredient resets.
- Exercise durability, quality, search-radius, worker, and skill-range limits and invalid input.
- Confirm invalid combined edits leave the original bill unchanged.
- Verify save/reload persistence and actual ingredient selection by pawns.
- Verify MCP schema refresh and compatibility with the unchanged compact list interface.
- Check performance and response sizes for large mod lists.

The updated game DLL and MCP server must be used together. This branch is not a release and no pull request has been opened.

## Storage filter extension

Adds storage quality/durability ranges, independent tainted/clean switches, and independent biocoded/non-biocoded switches for weapons and apparel. `get_settings` exposes the current restrictions and complete type allowlist. Shares range validation with bill filters.

Pending live QA: stockpile and shelf read/write round trips; range boundary handling; rejection of malformed/unknown fields without partial edits; preservation of omitted constraints, type allowlists, modded special filters and priority; each taint/biocoding combination; save/reload; storage groups and modded storage parents. No gameplay tests were run.

Storage update checks: C# and TypeScript builds passed; eight MCP schema acceptance/rejection cases passed. Local DLL deployment is staged because Windows reported the installed DLL has a user-mapped section open. No game process was launched or stopped.
