# Simple Improve

Workshop 3538863870, 6356 subscribers, released, About.xml `modVersion` 1.0.8. 3194 lines of C#
across 14 files, the largest mod in the workspace and the one with the most defects. packageId
`Zei33.SimpleImprove`, Harmony id `com.zei33.simpleimprove`, namespace `SimpleImprove`.

Full evidence for everything below:
`/Users/matthewscott/Programming/rimworld/docs/recon/2026-09-17-recon-dossier.md`. Severities follow
the adversarial critic who re-verified the two original audits against source and the decompiled game.

## What it does

The player picks "Improve" from a gizmo dropdown on a finished building that carries quality,
optionally naming a target. The building gets a designation, haulers carry the full build cost into
it, and a constructor works for the building's `WorkToBuild` then rolls a fresh quality with
`QualityUtility.GenerateQualityCreatedByPawn`, the same odds as building it new. A strictly better
roll is kept, a worse one discarded, and the materials are consumed either way. With a target set the
building stays marked and the loop repeats until a roll reaches it.

## Architecture

| Type | File | Role |
|---|---|---|
| `SimpleImproveMod : Mod` | `1.6/ModEntry.cs` | Entry point, `GetSettings`, `harmony.PatchAll()` |
| `SimpleImproveComp : ThingComp, IConstructible` | `1.6/Core/SimpleImproveComp.cs` (992 L) | All per-building state, gizmos, material maths, `CompleteImprovement` |
| `ImproveGroup` | same file, L16-26 | DTO grouping the current selection for one shared gizmo |
| `SimpleImproveMapComponent : MapComponent` | `1.6/Core/SimpleImproveMapComponent.cs` | `Dictionary<int, QualityCategory>` of target qualities keyed on `thingIDNumber`. The only state that is correctly Scribed |
| `SimpleImproveSettings : ModSettings` | `1.6/Core/SimpleImproveSettings.cs` (789 L) | Skill table, 5 presets + Custom, v1 to v2 migration, immediate-mode settings UI |
| `MaterialStorage : ThingOwner<Thing>` | `1.6/Utils/MaterialStorage.cs` | Accepts only outstanding need. Built with `base(null, false)`, so `Owner` is null |
| `WorkGiver_Improve : WorkGiver_Scanner` | `1.6/Jobs/WorkGiver_Improve.cs` | Emits `Job_HaulToImprove` then `Job_Improve` |
| `JobDriver_HaulToImprove`, `JobDriver_Improve` | `1.6/Jobs/` | Haul-to-container, and the work toil that accrues `WorkDone` |
| `Designator_MarkForImprovement`, `Designator_CancelImprovement` | `1.6/Designators/` | Dead code, registered by nothing (see defects) |

Defs: `Designation_Improve`, `WorkType_Improving` (naturalPriority 500, Construction),
`WorkGiver_Improve` (priorityInType 10), `Job_Improve`, `Job_HaulToImprove`.
`1.6/Patches/Patches_Improve.xml` is a comment-only stub.

Flow: selection triggers `ThingWithComps.GetGizmos`, the prefix attaches the comp,
`CompGetGizmosExtra` builds `ImproveGroup`s from `Find.Selector`, and the float menu writes
`TargetQuality` (into the map component) and `IsMarkedForImprovement` (which adds the designation).
`JobGiver_Work` then reaches `WorkGiver_Improve.HasJobOnThing`, which delegates to `JobOnThing`.

Harmony surface, all applied by `PatchAll()` from the `Mod` constructor:

| Class | File:line | Target | Kind |
|---|---|---|---|
| `DynamicComponentPatch` | `1.6/Patches/DynamicComponentPatch.cs:20` | `Verse.ThingWithComps.GetGizmos` | Prefix, attaches the comp |
| `DynamicComponentPatch.GameInitPatch` | `1.6/Patches/DynamicComponentPatch.cs:116` | `Verse.Game.InitNewGame` **and** `Verse.Game.LoadGame` | Postfix, clears the cache and re-attaches comps |
| `DesignationCancelPatch` | `1.6/Patches/DesignationCancelPatch.cs:12` | `Verse.Designation.Notify_Removing` | Prefix, drops materials and kills jobs |

Only `AddSimpleImproveComp` has a try/catch. Nothing else is exception-guarded.

## Invariants and traps

- **The comp is never declared on a ThingDef.** It exists only for things selected this session that
  passed the prefix filter, or that carried `Designation_Improve` at load. Every consumer tests
  `TryGetComp<SimpleImproveComp>() != null` and silently no-ops otherwise, and most of the defect
  register follows from that. Declaring it through `ThingDef.comps` is the fix under discussion, not a
  casual refactor.
- Prefix eligibility order (`DynamicComponentPatch.cs:37-64`): player faction,
  `ThingCategory.Building`, not already in `processedThings`, has `CompQuality`, has no comp yet,
  `def.blueprintDef != null`. Failing any check means no gizmo, forever, with no message.
- **Comp state does not round-trip a save.** `ThingWithComps.ExposeData` calls `InitializeComps()` on
  `LoadingVars`, rebuilding `comps` strictly from `def.comps`, and the dynamic comp is not in there.
  `PostExposeData` (`SimpleImproveComp.cs:429`) writes `workDone` and `materialContainer` and never
  reads them back. The only state that persists is the designation and
  `SimpleImproveMapComponent.targetQualities`.
- **Never mark `SimpleImproveComp` sealed.** `GetComp<T>` consults `compsByType`, built only inside
  `InitializeComps`, so a runtime-appended comp is missing from it. On a thing with three or more
  comps the linear fallback is reached only because the type is unsealed, since
  `GenTypes.IsSealedWithCache` short-circuits to null otherwise. Sealing it returns null on most
  buildings.
- Target quality is off-comp: `comp.TargetQuality` reads through
  `parent.Map.GetComponent<SimpleImproveMapComponent>()` every access, so it is null off-map or
  despawned. `CleanupOrphanedEntries` (`SimpleImproveMapComponent.cs:83`) runs on `FinalizeInit` and
  every 120000 ticks, dropping any entry whose id is not a spawned, player-faction, quality-bearing,
  blueprint-having thing on that map, so minified, caravanned or transferred buildings lose their
  target silently.
- `GameInitPatch` carries two `[HarmonyPatch]` attributes on one class
  (`DynamicComponentPatch.cs:116-117`). `HarmonyMethod.Merge` assigns field by field per attribute, so
  the last non-null value wins, the class resolves to one target and the other is silently dropped.
  Which one survives depends on `GetCustomAttributes` ordering and has not been checked in a running
  game. Settle it with `harmony.GetPatchedMethods()` in dev mode before editing this class.
- `Mathf.Clamp(baseQuality, 0, 5)` at `SimpleImproveSettings.cs:261` indexes the skill table, but
  `QualityCategory.Legendary` is 6, so the configured Legendary requirement is dead and the Masterwork
  row is used instead. Fixing it raises the default preset from 18 to 20 for every existing colony: a
  live balance change, not a quiet bug fix.
- The gizmo acts on the selection, not on `parent`. `CompGetGizmosExtra` (`:677`) re-analyses the
  whole of `Find.Selector` once per selected comp per frame and only the group `Representative` yields
  a gizmo. `ApplyQualityTargetToGroup` deliberately applies to every selected building when the acting
  group is already marked and more than one group exists. `ShowQualityTargetFloatMenu` and
  `GetImproveGizmoLabel` are the older single-building path, now unreachable.
- `MaterialStorage.GetCountCanAccept` returns 0 unless `IsMarkedForImprovement`, so a load that failed
  to restore the comp also makes the container refuse deliveries.
- `ThingCountNeeded` (`:258`) reads `cachedMaterialsNeeded` without populating it, and the haul
  deposit toil (`JobDriver_HaulToImprove.cs:147`) sizes its transfer from it, so the amount moved
  depends on an earlier unrelated `GetTotalMaterialCost()` call on the same comp instance.
  `GetTotalMaterialCost` also hands callers the shared mutable field itself.
- Translation keys are not named after what they display: `SimpleImprove_PresetVeryEasy` renders
  "Apprentice", `...Easy` "Novice", `...Normal` "Default", `...Hard` "Master", `...Expert` "Artisan".
  The preset enum values are newer than the keys, so change the key values, not the key names. 21 of
  the 56 keyed strings in each of the nine locales are orphaned, including every
  `SimpleImprove_Preset*Tooltip` and the keys for the removed calculator.
- No `DefInjected/WorkGiverDef/` exists in any of the nine languages, so the `[MustTranslate]`
  `label`, `verb` and `gerund` on `WorkGiver_Improve` render English in every non-English game. Eight
  more user-facing strings are hardcoded English: `SimpleImproveComp.cs:496,520,526,532` and
  `WorkGiver_Improve.cs:129,133,141,145`.
- **The docs describe a mod that does not exist.** `README.md:41` and `:125`,
  `Documentation/Features.md:97` and `Documentation/Architecture.md:82` document a Quality
  Distribution Calculator that was removed from the code; `README.md:69-71` sends the player to an
  Architect "Improve" tab; `About/About.xml` and `Workshop/English.md:43` advertise drag-select batch
  marking. None of it exists. Do not treat these files as a spec.

## Defect register

Confirmed critical and high, and not re-derived here. Evidence, reasoning and the remaining 12
medium/low entries are in the dossier. Several rows are the consequence of a trap above; the traps
carry the mechanism.

| Defect | file:line | What breaks |
|---|---|---|
| Work progress and every hauled stack destroyed on save/load | `1.6/Core/SimpleImproveComp.cs:429` | See traps. Reload zeroes `workDone` and deletes the container contents, with no message |
| Two `[HarmonyPatch]` attributes merge to one target | `1.6/Patches/DynamicComponentPatch.cs:116` | See traps. Either the static cache survives into a new colony, where `thingIDNumber` collisions leave buildings permanently gizmo-less, or `RestoreComponentsAfterLoad` never runs after a load |
| No `ShouldSkip`, no `PotentialWorkThingsGlobal` | `1.6/Jobs/WorkGiver_Improve.cs:14,20` | Every pawn reachability-scans every `BuildingArtificial` on the map on every job search, even with nothing marked |
| `HasJobOnThing` delegates to `JobOnThing` | `1.6/Jobs/WorkGiver_Improve.cs:49` | Full job construction runs as the scan validator, then again on the winner |
| Nested `GenClosest.ClosestThingReachable` inside that validator | `1.6/Jobs/WorkGiver_Improve.cs:186` | Unbounded (9999f) map search per required material, per candidate |
| A completed `Building` is handed to `GenConstruct.CanConstruct` | `1.6/Jobs/WorkGiver_Improve.cs:105` | An argument shape no vanilla caller produces. Any third-party postfix that assumes a blueprint or frame throws and kills the whole scan |
| Chairs cannot be improved (likely) | `1.6/Jobs/WorkGiver_Improve.cs:105` | `CanConstruct(..., checkSkills: true, ...)` enforces `constructionSkillPrerequisite`, which `DiningChair` (4), `Armchair` (5) and `Couch` (5) declare and beds, stools and dressers do not |
| Unguarded `pawn.skills` dereference | `1.6/Jobs/WorkGiver_Improve.cs:117`, `1.6/Jobs/JobDriver_Improve.cs:89,102` | Any non-humanlike worker NREs, every tick during the job |
| Mechs can never take the work type | `1.6/Defs/WorkTypeDefs/WorkTypes_Improve.xml:3` | `Mech_Constructoid.mechEnabledWorkTypes` lists only `Construction`; `GetDisabledWorkTypes` disables every unlisted type for colony mechs |
| Both `Designator` classes are dead code | `1.6/Designators/Designator_MarkForImprovement.cs:14` | No `DesignationCategoryDef` in the mod's source or its git history, while three published descriptions advertise the tab |
| Legendary skill requirement unreachable | `1.6/Core/SimpleImproveSettings.cs:261` | See traps |
| Two unrelated DLLs ship inside the published mod | fixed in the repo 2026-09-17 | The 17 Aug 2025 Workshop file carries `ISharpZipLib.dll` and `com.rlabrecque.steamworks.net.dll` beside `SimpleImprove.dll`, and RimWorld loads them as mod assemblies for all 6356 subscribers. `build.sh` now deletes everything in the staged output bar `SimpleImprove.dll` and every csproj `<Reference>` is `<Private>false</Private>`, so the repo no longer produces them. Live until the next upload, so it is a reason to ship one |

## Open user reports

All still live, none shipped. The last Workshop file update was 17 Aug 2025 and the author's last
reply was 23 Sep 2025. The four that carry the most weight:

| Report | Reporters | What is actually wrong |
|---|---|---|
| Scans every building even with nothing marked | IQ250, 19 Aug 2025; KahirDragoon, 14 Sep 2026 | The work giver defect above, and KahirDragoon named the mechanism correctly. Zei said in Aug 2025 he would review it, then shipped nothing |
| Dining chairs cannot be improved | laurent.mialon, 2 Aug 2025; Shin, 1 Mar 2026 | The chairs defect above, or `FirstBlockingThing` seeing a seated pawn. Improve a Stool against a DiningChair to discriminate. laurent also mentioned modded doormats, unexamined |
| No Improve option in the Architect tab | Smoovie, 3 Aug 2025; ReDawn, 27 Jan 2026 | The dead designators above. Smoovie also saw no entry in mod options, which would mean the assembly failed to load at all; undiagnosed |
| NRE with Forsaken Faction For Alpha Genes | VincentRoth, GitHub #1, 18 Sep 2025; TurtleShroom, 19 Oct 2025 | The `CanConstruct` argument shape. Not the author's bug, unanswered on both channels |

Also open and unanswered: constructoids blocked by `mechEnabledWorkTypes`, Chinese button text, a
donated Project RimFactory drone patch that needs a `MayRequire` guard and walks into the
`pawn.skills` NRE, the "improving costs more than rebuilding" balance complaint, and three feature
requests. Refresh the set with the `workshop-feedback` skill; the dossier holds the full register with
reporters and dates.

## Build and test

```sh
export FrameworkPathOverride=/opt/homebrew/opt/mono/lib/mono/4.7.2-api
dotnet build rimworld-simple-improve.sln -c Release   # clean, zero warnings
```

There are no tests and no CI. `./build.sh` stages into `$RimWorldDir/Mods/SimpleImprove` and is
destructive about it; the root file has the detail.

The best test that does not need the game: `SimpleImproveSettings.GetSkillRequirement(q, pawn: null)`
is pure over the skill dictionary, so a table test across all seven `QualityCategory` values catches
the Legendary clamp. `DetermineClosestPreset`, `ValidateAndFixLoadedData` and the
`SimpleImproveMapComponent` dictionary methods (constructible with `new SimpleImproveMapComponent(null)`)
are equally pure. The save/load defect needs the game: a dev-mode debug action that saves, reloads and
asserts `WorkDone`.

## Repo-specific notes

- Nothing under `1.6/Assemblies/net472/` has been tracked since 2026-09-17, so a rebuild no longer
  shows in the diff. The published Workshop build still carries the old binaries; see the register.
- `Workshop/*.md` holds nine translated store descriptions that the in-game uploader cannot
  republish, so changing store copy means the Steam website. See `ship-mod`.
- GPLv3, but `About/About.xml` carries no `<url>`, so the binaries reach 6356 subscribers with no
  source pointer.
