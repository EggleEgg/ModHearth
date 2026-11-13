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
| `--json <path>` | Write the complete report as JSON, including matches and warnings. |
| `--show-matches` | List matched predictions in the text report. |
| `--max-details <n>` | Maximum entries per text-report section. Default: `40`. |
| `-h`, `--help` | Show usage. |

### Exit codes

| Code | Meaning |
| --- | --- |
| `0` | No mismatches. The analyzer and the log agree for every mapped reference category. |
| `1` | Mismatches found. Inspect the sections in the report. |
| `2` | Invalid input or audit failure (missing files, unsupported cache schema, unreadable log, ...). |

`--json` still writes the report when mismatches are found; the exit code remains `1`.

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

`ModPossibleIssueAnalyzer` currently treats any direct definition as satisfying a reference. It does
not model a later mod CUTting that definition out of the merged raw set. When a target is defined by
one active mod and CUT by another, DF can still report the reference as unrecognized while the
analyzer stays silent.

The audit flags those false negatives with `ExplainedByCut`, lists the defining and cutting mods,
and includes the same explanation in the evidence sentence. This is a known analyzer limitation, not
automatically a scanner bug.

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
- **CUT-aware resolution is not implemented** in `ModPossibleIssueAnalyzer`; the audit explains the
  resulting false negatives rather than hiding them.
- **Built-in workshop/furnace ids** such as `CARPENTER` and `SMELTER` are valid in a reaction's
  `[BUILDING:ID]` token even though no raw file defines them. A log line for one of those ids is
  not treated as a normal missing definition.
- **Vanilla definitions** resolve references. A reported-only line whose target exists in the
  vanilla raws is usually evidence of a stale log, an incomplete vanilla baseline, or a cache from a
  different DF version.
- **Cache versions can drift**: if the active modlist version is not present in the cache, the tool
  falls back to the newest cached row for that mod and records a warning. Rescan with ModHearth
  before trusting the result.
- **Attribution is heuristic**. The audit intentionally reports unattributed lines instead of
  silently dropping them or guessing a mod.
- **SELECT_* patches can look like attribution mismatches**. When a mod adds a permitted reaction or
  building inside a `[SELECT_ENTITY:...]` (or similar) block, Dwarf Fortress finalizes the patched
  object under its original mod. The log therefore names the base object's mod, while the analyzer
  names the patch owner. Use the mismatch's raw reference mods to find the real owner; the evidence
  sentence calls this out.
- **No body-plan/tissue simulation**: only raw references that can be modeled reliably are compared.
  Tissue, body-part, graphics, and similar diagnostics remain outside this tool's scope.

## Maintenance workflow

1. Run ModHearth once so the raw cache matches the modlist under test.
2. Generate a clean log: either delete `errorlog.txt` and launch DF once with the modlist, or run
   the audit with `--automate` (which creates and then removes a short Pocket world).
3. Run the audit with `--json` and keep the report.
4. Start with `Reported but not predicted` and `Attribution mismatch`; these usually identify real
   analyzer or scanner gaps.
5. For each `ExplainedByCut` entry, decide whether to implement cut-aware resolution in the analyzer
   or accept the limitation.
6. Treat `Predicted but not reported` as a false-positive lead only after confirming the log is
   from the same mod set and launch.
7. Add regression tests in `ModHearth.App.Tests` for any analyzer change.
8. Re-run the audit against the same inputs and confirm the mismatch set moves in the expected
   direction.

## Tests

`ModHearth.App.Tests` contains:

- `RawIssueAuditComparerTests` for mismatch classification, cut evidence, unattributed references,
  unmapped categories, and SELECT/CUT/COPY exclusion.
- `RawIssueAuditOptionsTests` for command-line parsing and category mapping.
- `RawIssueAuditAutomationTests` for the automation options, status/state parsing, log rotation,
  missing-log handling, and DFHack path discovery.
- `RawIssueAuditRunnerTests` for synthetic end-to-end cache/modlist/vanilla/errorlog runs.
