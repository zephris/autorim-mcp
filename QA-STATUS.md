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
