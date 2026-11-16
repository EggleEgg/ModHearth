# ModHearth.RawIssueAudit

Development-only tool that compares three views of the same mod set:

1. the raw references captured by ModHearth's mod scan,
2. the issues `ModPossibleIssueAnalyzer` predicts statically, and
3. the raw errors Dwarf Fortress actually wrote to `errorlog.txt`.

The tool is read-only with respect to `config.json`, the raw dependency cache, and all other
ModHearth metadata. It is intentionally not shipped with the app. The audit itself never modifies
Dwarf Fortress data; the opt-in `--automate` mode does write `errorlog.txt` and control DF, as
described below.

## Why this exists

`ModPossibleIssueAnalyzer` models Dwarf Fortress raw loading rules. When DF changes its diagnostics
or resolution rules, the analyzer can become too loud (false positives) or too quiet (false
negatives) without any visible symptom in ModHearth. This audit surfaces those differences so the
analyzer can be updated against real data instead of guesswork.

The comparison is token-level: a prediction and a log line match only when the mod, reference kind,
and target object all agree.

## Prerequisites

- Dwarf Fortress installed and launched at least once.
- ModHearth launched once so `metadata/mod_raw_dependency_cache.json` exists.
- For a clean audit, delete `errorlog.txt`, launch Dwarf Fortress once with the target modlist, then
  close the game and run the audit. The log is cumulative across launches, so old errors can look
  like current mismatches.
- For automation, DFHack must be installed and working with Dwarf Fortress. The game must be at the
  title screen or the new-world setup screen, with a valid active modlist. Automation uses the
  preset DF actually loads; select it in ModHearth before running the audit.

## Usage

```powershell
dotnet run --project ModHearth.RawIssueAudit
```

The tool auto-detects ModHearth's `config.json`, the raw cache in the build output, the DF install,
the vanilla raws, and DFHack's `dfhack-config/mod-manager.json`.

To make the inputs explicit:

```powershell
dotnet run --project ModHearth.RawIssueAudit -- `
  --df "C:\Program Files (x86)\Steam\steamapps\common\Dwarf Fortress" `
  --cache "bin\Debug\net10.0\metadata\mod_raw_dependency_cache.json" `
  --json "artifacts\raw-audit.json"
```

### Automated errorlog generation

`--automate` removes the manual "delete the log and open the new-world screen" step:

```powershell
dotnet run --project ModHearth.RawIssueAudit -- --automate
```

The audit then:

1. backs up an existing `errorlog.txt` to `<df>/errorlog.audit-backup.txt` and clears it in place,
2. launches Dwarf Fortress if it is not already running,
3. waits for DFHack's RPC server and confirms the game is at the title or new-world screen,
4. drives the UI through **Create new world**, selects **Pocket** world size and **5 years** of
   history, then clicks **Create world**, which reloads and finalizes every active raw,
5. lets worldgen finish, clicks **Keep world and return to main menu**, and waits for the title
   screen, and
6. closes DF if the audit launched it, waits for the new log to stabilize, removes the generated
   world from `<df>/save/regionN`, and audits it.

The log is cleared by truncating it, not by deleting it: DF keeps the file handle open while it
runs, so deleting the file would silently discard the new diagnostics. Cleanup only removes
`save/regionN` folders that did not exist before automation, so real saves are never touched. If the
save folder cannot be inspected first, cleanup is skipped.

The automation refuses to run while a saved game is loaded. It also aborts if DF reports that mods
in the active preset failed to load, because DF disables the Create world button in that state. An
empty `mod-manager.json` (`[]`) is accepted before worldgen because DF falls back to its built-in
vanilla preset; the audit re-reads the file afterwards and uses the preset DF recorded.

Use `--keep-df-running` to leave a DF instance launched by the audit open. An instance that was
already running is always left alone. `--automation-timeout` bounds the worldgen phase; a timeout
still leaves the errorlog backup in place and removes any generated world it can identify.

Lua contact point: `GenerateErrorLog.lua` drives the menus and writes a status file that the C#
runner polls, so a long raw reload never holds the RPC connection open. `CheckDFState.lua` is
the preflight guard that rejects a loaded game before the log is touched.

#### DFHack Lua Commands and APIs Used

The audit uses two Lua scripts interacting with DFHack APIs:

1. `CheckDFState.lua`: Preflight probe that inspects game state before automation runs.
   - `dfhack.screen.getWindowSize()`: Obtains screen dimensions.
   - `dfhack.screen.readTile()`: Reads tile characters for text scanning.
   - `dfhack.gui.getDFViewscreen()`: Gets current viewscreen hierarchy.
   - `df.viewscreen_titlest:is_instance()`: Identifies title screen state.
   - `df.viewscreen_new_regionst:is_instance()`: Identifies world creation screen state.
   - `df.global.gamemode`: Checks game mode (`0` for active game).

2. `GenerateErrorLog.lua`: Drives worldgen automation and status reporting.
   - `require('gui')`: Loads DFHack GUI helper module.
   - `qerror()`: Aborts execution with error message.
   - `dfhack.getTickCount()`: Tracks execution timing and timeouts.
   - `dfhack.screen.getWindowSize()`: Queries terminal/screen size.
   - `dfhack.screen.readTile()`: Scans screen tiles for UI labels.
   - `df.global.gps.mouse_x` / `df.global.gps.mouse_y`: Sets mouse cursor position.
   - `gui.simulateInput()`: Simulates mouse clicks and key presses.
   - `dfhack.gui.getCurViewscreen()`: Retrieves current viewscreen for input injection.
   - `dfhack.gui.getDFViewscreen()`: Inspects viewscreen tree.
   - `df.viewscreen_titlest:is_instance()` & `df.viewscreen_new_regionst:is_instance()`: Checks viewscreen types.
   - `df.global.gamemode`: Preflight check against loaded game sessions.
   - `dfhack.timeout()`: Schedules asynchronous frame-based steps.

### Hardcoded token report

`--hardcoded-report` is a separate read-only mode that does not need `errorlog.txt` or DFHack. It
inventories the tokens Dwarf Fortress resolves itself and checks how the active mods use them:

```powershell
dotnet run --project ModHearth.RawIssueAudit -- --hardcoded-report
dotnet run --project ModHearth.RawIssueAudit -- --hardcoded-report --verify-hardcoded
```

The catalog lives in `utils/modRaws/DwarfFortressHardcodedTokens.cs`. Each group carries its
domain, raw context, behavior, verification strategy, source, and the DF version it was verified
against:

| Group | What it models | Drift check |
| --- | --- | --- |
| Reaction buildings | The 31 built-in workshop/furnace ids accepted by a reaction's `[BUILDING:ID]` (`CARPENTER`, `SMELTER`, `DYER`, ...). No raw file defines them, and they are not valid `PERMITTED_BUILDING` ids. | Compares the catalog with the building ids vanilla reactions actually use. Several short ids are inlined in the executable and graphics use prefixed names such as `WORKSHOP_LOOM`, so standalone executable strings are not reliable. |
| Cut-immune dye reactions | The vanilla dye reactions defined by `vanilla_reactions/objects/reaction_dyes.txt` and permitted by `entity_default.txt`. DF carries their ids in the executable and silently ignores `CUT_REACTION` for them. Mod-added dye reactions are cut normally. | The token list is derived at runtime by intersecting `[REACTION:...]` in `reaction_dyes.txt` with `[PERMITTED_REACTION:...]` in `entity_default.txt`, then checked by exact standalone NUL-terminated string comparison against the executable. |
| Concrete built-in materials | The 18 materials DF constructs itself (`WATER`, `ASH`, `GLASS_CLEAR`, ...). They need no raw definition and are valid as the first material token in a `REAGENT`/`PRODUCT` slot. | Exact standalone NUL-terminated string comparison against the executable. |
| Material grammar tokens | The 10 material keywords and sentinels (`NONE`, `INORGANIC`, `PLANT_MAT`, `LOCAL_PLANT_MAT`, `GET_MATERIAL_FROM_REAGENT`, ...). They describe how the rest of the slot is parsed; they are not material definitions. | Exact standalone NUL-terminated string comparison against the executable. |

Only the dye group is derived; the other groups keep their token lists in the catalog. The
distinction matters. The dye entries do **not** claim the reactions are built into raws or hidden
from mods: `reaction_dyes.txt` defines them and `entity_default.txt` permits them. What is
hardcoded is their cut immunity, which controlled runs confirmed by cutting one, three, and all 68
dye ids from different files and load positions; every cut was ignored, while `PRESS_OIL_FRUIT`
and a mod-added `DCCF_DEEP_OAK_BARK_DYE` were cut normally. The current DF 53.17 raws define 68
dye reactions, permit 126 reactions, and the intersection is exactly those 68 dye ids. A DF update
that changes either raw file updates the analyzer without a catalog edit; `--verify-hardcoded`
reports drift if the derived set and the executable disagree.

The report contains:

1. **Catalog inventory**: every group, description, source, verification version and strategy, and
   token.
2. **Active-mod usage**: reaction building ids, ignored dye cuts, built-in materials, and material
   grammar tokens used by the active modpack, with use counts and owning mods.
3. **Invalid `PERMITTED_BUILDING` contexts**: mods that put a built-in workshop/furnace id where
   only a raw-defined custom building is accepted.
4. **Unresolved material references**: `REAGENT`/`PRODUCT` material tokens that name an
   `INORGANIC`, `PLANT`, or `CREATURE` object no active mod or vanilla raw defines. The scan reads
   the active mod folders from `mod_folder_snapshot.json`; it skips
   `GET_ITEM_DATA_FROM_REAGENT` and creature-caste item types such as `FISH_RAW`.
5. **Vanilla consistency checks**: catalog entries vanilla raws unexpectedly define, and vanilla
   reactions that use a `BUILDING` id no raw defines and the catalog does not cover.
6. **Unclassified material samples**: token shapes the parser does not model yet. These are
   catalog-maintenance leads, not necessarily DF errors.
7. **Optional executable verification**: `--verify-hardcoded` extracts standalone NUL-terminated
   ASCII strings from the installed `Dwarf Fortress.exe`. Each derived dye reaction passes when it
   appears exactly as a standalone string; material and grammar entries use the same
   string-presence check. The report also fails if the executable contains a vanilla reaction id
   that the derived dye set does not cover. Reaction building ids are covered by the vanilla
   consistency check described above instead.

`--verify-hardcoded` requires `--hardcoded-report` and a resolvable DF folder. `--hardcoded-report`
cannot be combined with `--automate`; it never writes the log, cache, config, or DF data.

### Options

| Option | Meaning |
| --- | --- |
| `--df <folder>` | Dwarf Fortress folder. Defaults to the path in ModHearth's config. |
| `--dfhack <folder>` | DFHack folder used only for the `dfhack-run` fallback. Auto-detected by default. |
| `--errorlog <path>` | `errorlog.txt`. Defaults to `<df>/errorlog.txt`. |
| `--cache <path>` | `mod_raw_dependency_cache.json`. Defaults to the newest app cache. |
| `--vanilla <path>` | Vanilla raws. Defaults to `<df>/data/vanilla`. |
| `--modlist <path>` | DFHack `mod-manager.json`. Defaults to `<df>/dfhack-config/mod-manager.json`. |
| `--config <path>` | ModHearth `config.json` used to locate Dwarf Fortress. |
| `--all-mods` | Audit every cached mod instead of the active modpack. Cannot be combined with `--automate`. |
| `--automate` | Opt in to automated errorlog generation with a short worldgen (see above). Alias: `--automate-worldgen`. |
| `--automation-timeout <seconds>` | Maximum worldgen automation time. Default: `180`; range: `30`-`3600`. |
| `--keep-df-running` | Leave DF running when the audit launched it. Has no effect on an already-running instance. |
| `--hardcoded-report` | Inventory the hardcoded-token catalog and the active mods' use of it. Read-only; does not need `errorlog.txt`. Cannot be combined with `--automate`. |
| `--verify-hardcoded` | With `--hardcoded-report`, verify the catalog against the installed Dwarf Fortress executable and vanilla raws. |
| `--json <path>` | Write the complete report as JSON, including matches and warnings. |
| `--show-matches` | List matched predictions in the text report. |
| `--max-details <n>` | Maximum entries per text-report section. Default: `40`. |
| `-h`, `--help` | Show usage. |

### Exit codes

| Code | Meaning |
| --- | --- |
| `0` | No mismatches, or the hardcoded report completed without catalog drift. |
| `1` | Mismatches found, or `--verify-hardcoded` found drift. Inspect the sections in the report. |
| `2` | Invalid input or audit failure (missing files, unsupported cache schema, unreadable log, ...). |

`--json` still writes the report when mismatches or drift are found; the exit code remains `1`.

## Reading the report

### Inputs and summary

The input block records which files were used, the raw cache schema, the number of active cached
rows, and the vanilla definition count. Check these first: auditing the wrong cache or a stale
`errorlog.txt` produces misleading results.

The summary distinguishes:

- **Predictions**: analyzer output, split into references that `errorlog.txt` can verify and
  references it cannot.
- **Reported**: mapped reference errors attributed to a mod.
- **Matched**: prediction and log agree.
- **Errorlog**: considered, attributed, and unattributed line counts.

### Mismatch statuses

| Status | Meaning | Typical action |
| --- | --- | --- |
| Predicted but not reported | The analyzer predicted an error that was not in the log. Possible false positive, or the log came from another mod set. | Check the evidence line. If the raw scan captured the reference on the predicted mod, verify DF's current behavior. |
| Reported but not predicted | DF reported an error the analyzer did not predict. Possible false negative or scanner gap. | If the scan captured the reference on the reported mod, update the analyzer. If not, investigate the raw scanner. |
| Attribution mismatch | Both sides saw the same token, but assigned it to different mods. | Compare the raw scan reference owners with the log attribution rules. |

Each mismatch also includes:

- the logged category/categories,
- the active mods that directly define the target,
- the active mods that CUT the target,
- the active mods whose raw scan contains the reference,
- a short evidence sentence describing the most likely explanation.

### `ExplainedByCut`

`ModPossibleIssueAnalyzer` resolves the reference kinds `errorlog.txt` can verify
(`PERMITTED_REACTION`, `PERMITTED_BUILDING`, and a reaction's `BUILDING`) against the final merged
raw set. It walks the active mods in DF load order: vanilla definitions are the initial state, each
mod removes its CUT targets, then adds its own definitions. A later definition restores a target an
earlier mod cut. Within one mod, cuts are applied before definitions because the cache stores an
unordered set per mod; a replacer that cuts a vanilla object and defines its own replacement keeps
that replacement.

One DF-specific exception is modeled explicitly. DF hardcodes the vanilla dye reactions defined by
`vanilla_reactions/objects/reaction_dyes.txt` and permitted by `entity_default.txt`, and silently
ignores `CUT_REACTION` for exactly those ids, even when the cut comes from the last-loaded mod. The
analyzer derives the set from those two files at load time and exempts it; mod-added dye reactions
such as `DCCF_DEEP_OAK_BARK_DYE` are cut normally. If the raw files are missing or unreadable, the
derived set is empty and the hardcoded report records a warning.

The exception was confirmed with controlled runs against the same 256-mod setup:

- one dye cut, the three-entry pressing block, and all 68 dye cuts were all ignored;
- moving the dye cut to `reaction_aa_pressing.txt`, then to a new file in the last-loaded
  `VanillaDyeRebalance`, still left `ALDER_CONE_DYE` usable;
- the control `PRESS_OIL_FRUIT` cut worked in the same files and positions;
- `DCCF_DEEP_OAK_BARK_DYE` (defined by `dc_more_cavestuff`, cut by the later
  `dc_cavern_flora_aa_patch`) was reported by DF.

These runs rule out block size, file grouping, and load order. The derived ids are the same strings
DF stores next to its "Unrecognized permitted reaction token" diagnostic in `Dwarf Fortress.exe`;
`--verify-hardcoded` is the executable-string drift check for that assumption.

Audit evidence on the 256-mod baseline:

- Without the exemption, a load-order simulation listed all 68 vanilla dyes as predicted-only false
  positives.
- With the exemption, the 21 genuinely cut reactions (clay/glaze, metals, `PRESS_OIL_FRUIT`, and two
  `DCCF_*` reactions) are predicted and match DF's log.
- The remaining predicted-only entries are the two separate entity-replacement cases below; the
  exemption adds no new predicted-only entries.

`ExplainedByCut` still marks a mismatch where the raw scan sees both a definition and a CUT and the
log disagrees with the prediction. That is now a lead to investigate, not an expected limitation.
`SELECT`/`CUT`/`COPY_TAGS_FROM` predictions stay outside the load-order model because DF has no
stable diagnostic for them.

The reverse case can produce a predicted-only entry: a `SELECT_ENTITY` patch adds a permitted
reaction, then a later mod CUTs and re-adds the entity, discarding the earlier patch. The 256-mod
baseline's `SMELT_ANTIMONY_FROM_STIBNITE` and `PIG_IRON_MAKING` entries are this case; see Known
limitations.
This is consistent with the wiki's rule that a later CUT "will completely replace that mod's edit"
([Modding pitfalls](https://dwarffortresswiki.org/Modding_pitfalls)).

### Unattributed reference errors

`ModErrorLogAnalyzer` can understand a raw error but fail to attribute it to any mod. Those lines are
invisible in the normal issue list because no mod owns them. The audit reports them separately,
along with the active mods that contain the reference, so the attribution rules can be improved.

### Unmapped errorlog categories

When a reference-like category (for example a hypothetical new `Unrecognized permitted material
token`) is not mapped by the audit, it appears here. Add the category to
`ErrorLogReferenceMapping` and decide whether it corresponds to an analyzer issue kind.

### Not verifiable from errorlog

`SELECT`, `CUT`, and `COPY_TAGS_FROM` predictions are listed here but excluded from mismatch counts.
DF does not currently emit a stable, parseable diagnostic that can be mapped back to those
references, so treating their absence from the log as a false positive would be wrong.

## What is compared

`ModPossibleIssueAnalyzer` reference kinds map to these `errorlog.txt` categories:

| Analyzer kind | Errorlog category |
| --- | --- |
| `MissingPermittedReaction` | `Unrecognized permitted reaction token` |
| `MissingPermittedBuilding` | `Unrecognized permitted building token` |
| `MissingReactionBuilding` | `Unrecognized building token` |

Category matching is prefix-based because DF can append an `in <context>` suffix.

`MissingSelectTarget`, `MissingCutTarget`, and `MissingCopyTagsFromSource` have no known errorlog
wording and are reported as not verifiable.

## Known limitations

- **Cumulative errorlog**: `errorlog.txt` can contain errors from several launches and mod sets.
  Always confirm a run represents the current cache/modlist. `--automate` backs up and clears the
  log first.
- **Automation depends on DFHack and the stock menu labels**. It requires DFHack's RPC server and
  recognizes the English labels "Create new world", "Pocket", "5 years", "Create world", and
  "Keep world and return to main menu". A UI mod that renames them, or a DF version with different
  screens, makes the automation time out without touching gameplay state. The manual workflow
  remains available.
- **Automation needs a short but real worldgen**. It generates a Pocket world with 5 years of
  history (roughly 30 seconds on a typical machine) and then deletes the generated `save/regionN`
  folder. Worldgen failures, a full disk, or a preset that cannot load leave DF at the new-world
  screen and the automation reports an error.
- **Automation uses the active preset**. `--automate` cannot be combined with `--all-mods`, and a
  log generated under one preset must not be compared against another. An empty
  `mod-manager.json` makes DF load its built-in vanilla preset; after worldgen the audit uses the
  preset DF recorded and fails if no modpack can be resolved.
- **Automation does not refresh the raw cache**. Run ModHearth once after changing the modlist so
  the cache versions match the preset DF actually loads.
- **Hardcoded vanilla dye cuts are exempt**. DF hardcodes the vanilla dye reaction ids and silently
  ignores `CUT_REACTION` for them. The analyzer derives the ids by intersecting
  `vanilla_reactions/objects/reaction_dyes.txt` with `entity_default.txt`; run
  `--hardcoded-report --verify-hardcoded` after a DF update to check the derived set against the
  executable. Mod-added dyes are still cut normally.
- **CUT-aware resolution uses mod-level load order**. The cache stores cuts and definitions as
  unordered per-mod sets, so a cut and a definition inside the same mod are applied as "cut first,
  then define". `SELECT`/`CUT`/`COPY_TAGS_FROM` predictions are not load-order aware.
- **Built-in workshop/furnace ids** such as `CARPENTER` and `SMELTER` are valid in a reaction's
  `[BUILDING:ID]` token even though no raw file defines them. A log line for one of those ids is
  not treated as a normal missing definition.
- **Vanilla definitions** resolve references. A reported-only line whose target exists in the
  vanilla raws is usually evidence of a stale log, an incomplete vanilla baseline, or a cache from a
  different DF version.
- **Cache versions can drift**: if the active modlist version is not present in the cache, the tool
  falls back to the newest cached row for that mod and records a warning. Rescan with ModHearth
  before trusting the result. The hardcoded report's material scan also uses the cache's definition
  ids, so a version fallback can show unresolved material references that the installed folder
  actually defines.
- **Attribution is heuristic**. The audit intentionally reports unattributed lines instead of
  silently dropping them or guessing a mod.
- **SELECT_* patches can look like attribution mismatches**. When a mod adds a permitted reaction or
  building inside a `[SELECT_ENTITY:...]` (or similar) block, Dwarf Fortress finalizes the patched
  object under its original mod. The log therefore names the base object's mod, while the analyzer
  names the patch owner. Use the mismatch's raw reference mods to find the real owner; the evidence
  sentence calls this out.
- **Entity replacement can discard an earlier SELECT patch**. The 256-mod baseline has two
  predicted-only entries. `SMELT_ANTIMONY_FROM_STIBNITE` is added to `MOUNTAIN` and `PLAINS` by
  `real_alloys_reforged` at load position 139, then `march_of_wrath` CUTs both entities at 140.
  `PIG_IRON_MAKING` is added to `MOUNTAIN`, `PLAINS`, and `EVIL` by `Vanilla_Materials_Overhaul` at
  138, and `march_of_wrath` CUTs those entities at 140 after also cutting the reaction. In both
  cases the finalized entities do not contain the patch, while later SELECT patches at positions
  211 and 238 are validated. This is the documented behavior that a later CUT completely replaces an
  earlier mod's edit ([Modding pitfalls](https://dwarffortresswiki.org/Modding_pitfalls)). The cache
  stores permitted reactions per mod rather than per patched entity, so the analyzer cannot
  distinguish a discarded patch from an active one without a schema change.
- **No body-plan/tissue simulation**: only raw references that can be modeled reliably are compared.
  Tissue, body-part, graphics, and similar diagnostics remain outside this tool's scope.

## Maintenance workflow

1. Run ModHearth once so the raw cache matches the modlist under test.
2. Generate a clean log: either delete `errorlog.txt` and launch DF once with the modlist, or run
   the audit with `--automate` (which creates and then removes a short Pocket world).
3. Run the audit with `--json` and keep the report.
4. Start with `Reported but not predicted` and `Attribution mismatch`; these usually identify real
   analyzer or scanner gaps.
5. For each `ExplainedByCut` entry, verify whether DF actually honors the cut. A remaining entry is
   either a DF exception that needs modeling or a scanner/cache gap.
6. Treat `Predicted but not reported` as a false-positive lead only after confirming the log is
   from the same mod set and launch.
7. Add regression tests in `ModHearth.App.Tests` for any analyzer change.
8. Re-run the audit against the same inputs and confirm the mismatch set moves in the expected
   direction.

After a Dwarf Fortress update, also run:

```powershell
dotnet run --project ModHearth.RawIssueAudit -- --hardcoded-report --verify-hardcoded
```

A clean run exits `0`. Exit `1` means the executable or vanilla raws changed relative to the
catalog or the derived dye set. The report lists each drifted token and whether it is missing from
the executable, newly present, unexpectedly defined by vanilla raws, or used by a vanilla reaction
without being cataloged. Dye drift is reported when the executable no longer carries an id derived
from `reaction_dyes.txt`/`entity_default.txt`, or when it carries a vanilla reaction id outside that
derived set. Re-run the controlled cut probes before changing how cut immunity is modeled.

## Tests

`ModHearth.App.Tests` contains:

- `RawIssueAuditComparerTests` for mismatch classification, cut evidence, unattributed references,
  unmapped categories, and SELECT/CUT/COPY exclusion.
- `RawIssueAuditOptionsTests` for command-line parsing and category mapping.
- `RawIssueAuditAutomationTests` for the automation options, status/state parsing, log rotation,
  missing-log handling, and DFHack path discovery.
- `RawIssueAuditRunnerTests` for synthetic end-to-end cache/modlist/vanilla/errorlog runs.
- `RawHardcodedTokenTests` for catalog invariants, material-token parsing, report option validation,
  material scanning, drift verification, and synthetic hardcoded report runs.
