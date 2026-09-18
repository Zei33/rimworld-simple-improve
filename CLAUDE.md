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
| `SimpleImproveComp : ThingComp, IConstructible, IThingHolder` | `1.6/Core/SimpleImproveComp.cs` | All per-building state, gizmos, material maths, `CompleteImprovement`. Declared on the defs, so it Scribes |
| `ImprovableDefs` | `1.6/Core/ImprovableDefs.cs` | `Qualifies` and `DeclareCompOn`: which defs carry the comp. Pure, and well covered |
| `WorkerSkill` | `1.6/Core/WorkerSkill.cs` | The Construction level a worker is judged on, and `FirstBlocker` over it. `Of(Pawn)` takes the readings and decides nothing; `From` and `FirstBlocker` decide everything and are pure |
| `ImproveWorkers` | `1.6/Core/ImproveWorkers.cs` | `IsAssignedToImproving`, the one place the work-settings guard lives |
| `Tests/` | NUnit, net472 | Not in the sln, excluded from the mod's compile items. See `Tests/README.md` |
| `ImproveGroup` | same file, L16-26 | DTO grouping the current selection for one shared gizmo |
| `SimpleImproveMapComponent : MapComponent` | `1.6/Core/SimpleImproveMapComponent.cs` | `Dictionary<int, QualityCategory>` of target qualities keyed on `thingIDNumber`. Redundant now the comp persists; slated for removal in #13 |
| `SimpleImproveSettings : ModSettings` | `1.6/Core/SimpleImproveSettings.cs` (789 L) | Skill table, 5 presets + Custom, v1 to v2 migration, immediate-mode settings UI |
| `MaterialStorage : ThingOwner<Thing>` | `1.6/Utils/MaterialStorage.cs` | Accepts only outstanding need. Built with `base(comp, false)`, so `Owner` is the comp and the holder tree reaches it |
| `WorkGiver_Improve : WorkGiver_Scanner` | `1.6/Jobs/WorkGiver_Improve.cs` | Emits `Job_HaulToImprove` then `Job_Improve` |
| `JobDriver_HaulToImprove`, `JobDriver_Improve` | `1.6/Jobs/` | Haul-to-container, and the work toil that accrues `WorkDone` |
| `Designator_MarkForImprovement`, `Designator_CancelImprovement` | `1.6/Designators/` | Dead code, registered by nothing (see defects) |

Defs: `Designation_Improve`, `WorkType_Improving` (naturalPriority 500, Construction),
`WorkGiver_Improve` (priorityInType 10), `Job_Improve`, `Job_HaulToImprove`.

XML patches, in `1.6/Patches/` beside the C# (RimWorld globs `*.xml` from a folder named exactly
`Patches`, so it has to live there): `MechWorkTypes.xml` appends the work type to
`Mech_Constructoid.mechEnabledWorkTypes`, and `ProjectRimFactoryDrones.xml` does the same for that
mod's drone station. Both carry `<success>Always</success>`, which is the whole of their gating.
`Tests/MechWorkTypePatchTests.cs` runs the mech xpath against the installed game's own XML, so a
RimWorld update that renames or moves the def fails a test rather than silently doing nothing.
Flow: every improvable building carries the comp from the moment it is made,
`CompGetGizmosExtra` builds `ImproveGroup`s from `Find.Selector`, and the float menu writes
`TargetQuality` (into the map component) and `IsMarkedForImprovement` (which adds the designation).
`JobGiver_Work` then calls `WorkGiver_Improve.ShouldSkip`, which exits on
`!AnySpawnedDesignationOfDef(Designation_Improve)` before any search is built; when something is
marked, `PotentialWorkThingsGlobal` returns the designated buildings and `HasJobOnThing` runs over
those, delegating to `JobOnThing`.

**The designation is the record of what is marked, not decoration.** Since 2026-09-18 it decides
whether the work giver runs at all, so anything that puts the comp's `isMarkedForImprovement` and the
designation out of step is a building that never gets improved. `SimpleImproveComp.PostSpawnSetup`
repairs the one direction that happens in normal play; `ImproveDesignations` carries why.

Harmony surface, all applied by `PatchAll()` from the `Mod` constructor:

| Class | File:line | Target | Kind |
|---|---|---|---|
| `CompInjectionPatch` | `1.6/Patches/CompInjectionPatch.cs` | `RimWorld.DefGenerator.GenerateImpliedDefs_PostResolve` | Postfix, declares the comp on the improvable defs |
| `LateCompInjectionPatch` | same file | `Verse.StaticConstructorOnStartupUtility.CallAll` | Postfix, second idempotent pass for quality added by another mod's C# |
| `DesignationCancelPatch` | `1.6/Patches/DesignationCancelPatch.cs:12` | `Verse.Designation.Notify_Removing` | Prefix, drops materials and kills jobs |

Nothing is exception-guarded. `DynamicComponentPatch`, which prefixed `ThingWithComps.GetGizmos` to
attach the comp and postfixed `Game.InitNewGame`/`Game.LoadGame` to re-attach it, was deleted on
2026-09-17 when the comp moved into `def.comps`.

## Invariants and traps

- **The comp is declared on the defs, and that is what makes it persist.** `ImprovableDefs` adds
  `CompProperties_SimpleImprove` to every `ThingDef` with `ThingCategory.Building`,
  `blueprintDef != null` and a `CompQuality`. Measured against the shipped game: 43 buildings carry
  quality, 35 of them have a blueprint and get the comp. The eight that do not are SculptureSmall,
  SculptureLarge, SculptureGrand, SculptureTerror, Statue, Harp, Harpsichord and Piano, all crafted
  from a recipe with no `designationCategory`, so `ThingDefGenerator_Buildings` gives them no
  blueprint.
- **It must be applied once per play-data load, not once per process.** This is why
  `CompInjectionPatch` postfixes `DefGenerator.GenerateImpliedDefs_PostResolve` instead of the
  obvious `[StaticConstructorOnStartup]`. `StaticConstructorOnStartupUtility.CallAll` goes through
  `RuntimeHelpers.RunClassConstructor`, a no-op once the type is initialised, and
  `LanguageDatabase.SelectLanguage` is literally `PlayDataLoader.ClearAllPlayData()` then
  `LoadAllPlayData()`, which rebuilds every `ThingDef` from XML. A static constructor would leave a
  player who changed language with a mod that silently does nothing until they restart. Dev mode's
  reload-defs has the same shape, shallow-copying fresh defs over the old ones and replacing `comps`
  wholesale. `CreateModClasses` also skips a `Mod` type already in `runningModClasses`, so the
  constructor and its `PatchAll()` genuinely do run only once; the patch survives because Harmony
  patches the method, not the mod.
- **Accepted risks from the 2026-09-17 adversarial review, not defects to re-find.**
  - The comp scribes `isMarkedForImprovement`, `workDone` and `materialContainer` flat onto the
    parent thing's node, because that is how `ThingWithComps.ExposeData` calls `PostExposeData`.
    `workDone` is a key vanilla also uses, on `Frame` and `Building_GeneAssembler`, but neither
    carries quality so neither collides. A modded quality building whose `thingClass` scribed its
    own `workDone` would collide and the first element would win for both readers. The keys were
    deliberately **not** renamed: renaming orphans exactly the data the fix exists to recover from
    existing saves. Re-examine only if such a mod is actually reported.
  - Staged materials now count toward colony wealth and `PlayerItemAccessibilityUtility`, because
    the comp is an `IThingHolder` with a real owner. Arguably the correct accounting, since the
    resources do still exist, but it is a live balance change and needs a changelog note alongside
    simple-improve#14 and the chair-bug fix.
  - `DesignationCancelPatch` dropping into a null map (S-9, #10) is now reachable across a reload,
    because materials survive one. It was already reachable within a session. Fix it with #10 and
    #11 together, as ordering constraint 3 requires, not on its own.
  - A save from the 1.0.5 or 1.0.6 era can hold a `Designation_Improve` with no
    `isMarkedForImprovement` key, giving a building a mark that the work giver ignores. Narrow, and
    it belongs with the designation work in #12, not here.
- Do not move this to an XML `PatchOperation`. `LoadedModManager.LoadAllActiveMods` applies patches
  at line 116 and resolves inheritance at line 373 of `ParseAndProcessXML`, so an XPath runs before
  inheritance and sees only nodes as authored. 41 of the 43 quality buildings inherit the comp from
  `FurnitureWithQualityBase`, `BedWithQualityBase`, `ArtBuildingBase`, `MusicalInstrumentBase` or
  `RitualSeatBase`; only `Sarcophagus` and `GibbetCage` declare it directly. The XPath would match
  those seven nodes, would reach the eight blueprint-less buildings, could not filter on category
  (declared further up the chain again), and would miss any def another mod gives quality to. There
  is also no `CompProperties_Quality` type: vanilla writes `<li><compClass>CompQuality</compClass></li>`.
- The injector cannot test faction, which the old runtime prefix did, so the comp now exists on
  non-player quality buildings too. That is safe because `CompGetGizmosExtra` (`:677`) independently
  re-checks player faction, `CompQuality` and `blueprintDef` before yielding anything.
- **Comp state round-trips a save, as of 2026-09-17.** It did not before: the comp was attached at
  runtime and `ThingWithComps.ExposeData` calls `InitializeComps()` on `LoadingVars`, rebuilding
  `comps` strictly from `def.comps`, so `PostExposeData` wrote `workDone` and `materialContainer`
  and nothing ever read them. Comps scribe flat onto the parent thing's node rather than into a
  wrapper, so those orphaned keys are still addressable and an existing save recovers its work and
  materials on the first load after the fix rather than merely stopping the bleeding.
- **Never mark `SimpleImproveComp` sealed.** `GetComp<T>` takes a fast path below three comps that
  type-tests `comps[0]` and `comps[1]` directly; at three or more it consults `compsByType`, and a
  sealed type parameter that misses the dictionary short-circuits to null instead of falling through
  to the linear scan. `InitializeComps` does key the dictionary correctly now, so sealing would
  probably work, which is exactly why the rule is worth keeping written down rather than rediscovered.
- Target quality is off-comp: `comp.TargetQuality` reads through
  `parent.Map.GetComponent<SimpleImproveMapComponent>()` every access, so it is null off-map or
  despawned. `CleanupOrphanedEntries` (`SimpleImproveMapComponent.cs:83`) runs on `FinalizeInit` and
  every 120000 ticks, dropping any entry whose id is not a spawned, player-faction, quality-bearing,
  blueprint-having thing on that map, so minified, caravanned or transferred buildings lose their
  target silently.
- `Mathf.Clamp(baseQuality, 0, 5)` at `SimpleImproveSettings.cs:261` indexes the skill table, but
  `QualityCategory.Legendary` is 6, so the configured Legendary requirement is dead and the Masterwork
  row is used instead. Fixing it raises the default preset from 18 to 20 for every existing colony: a
  live balance change, not a quiet bug fix.
- The gizmo acts on the selection, not on `parent`. `CompGetGizmosExtra` (`:677`) re-analyses the
  whole of `Find.Selector` once per selected comp per frame and only the group `Representative` yields
  a gizmo. `ApplyQualityTargetToGroup` deliberately applies to every selected building when the acting
  group is already marked and more than one group exists. `ShowQualityTargetFloatMenu` and
  `GetImproveGizmoLabel` are the older single-building path, now unreachable.
- `MaterialStorage.GetCountCanAccept` returns 0 unless `IsMarkedForImprovement`, so anything that
  loses the marked flag also makes the container refuse deliveries.
- `ThingCountNeeded` (`:258`) reads `cachedMaterialsNeeded` without populating it, and the haul
  deposit toil (`JobDriver_HaulToImprove.cs:147`) sizes its transfer from it, so the amount moved
  depends on an earlier unrelated `GetTotalMaterialCost()` call on the same comp instance.
  `GetTotalMaterialCost` also hands callers the shared mutable field itself.
- **A def patch is the only way to give a colony mech a work type, and its two obvious gates do not
  work.** `Verse.Pawn.GetDisabledWorkTypes` reads `RaceProps.mechEnabledWorkTypes` live off the race
  def as a whitelist, so XML is sufficient and no Harmony patch is wanted. But:
  - **`MayRequire` on an `<Operation>` is read by nothing.** `ModContentPack.LoadPatches` iterates
    every `<Operation>` child and deserialises it unconditionally; the `MayRequire` check in
    `LoadedModManager` applies to def nodes after patching. It looks like a DLC gate and is a no-op.
    It does work on an `<li>` inside a list and on a def node.
  - **`success` is a child element, not an attribute.** `DirectXmlToObject` maps only child nodes
    onto fields, so `success="Always"` is silently ignored. Write `<success>Always</success>`.
  - An xpath that matches nothing logs a red error naming the file at the end of def loading, which
    is why `<success>Always</success>` is the gate: without the DLC the def is not in the document.
  - Patches apply **before** `XmlInheritance.Resolve`, so an xpath only sees what a def declares
    literally. All seven vanilla mechs declare `mechEnabledWorkTypes` directly, so this is safe here,
    but a modded mech inheriting the list from an abstract parent would not match.
  - Load order does not matter. `CombineIntoUnifiedXML` builds the document from every mod first and
    `ApplyPatches` runs afterwards, so a patch against a third-party def needs no `loadAfter`.
  - Seven vanilla mechs declare the field, all Biotech: Constructoid (Construction), Lifter
    (Hauling), Tunneler (Mining), Fabricor (five production types), Agrihand (PlantCutting, Growing),
    Cleansweeper (Cleaning), Paramedic (Doctor, Firefighter). Combat mechs declare none, so under the
    whitelist every work type is disabled for them.
- **A def patch that enables a work type for mechs is INERT on its own, and this is the trap that
  nearly shipped.** Removing the type from the disabled list does not raise the priority already
  stored against the mech. `Pawn_WorkSettings.ExposeData` wrote 0 there on every load while the type
  was still disabled; nothing re-runs `EnableAndInitialize` for a pawn that already has settings (its
  four call sites are `PawnGenerator` for a new pawn, `ResurrectionUtility`, `LordToil_Siege` and
  `Pawn.SetFaction` when joining the player faction); and `GetPriority`'s "any non-zero counts as 3"
  shortcut is gated on `RaceProps.Humanlike`, so a mech is held to the stored 0 exactly.
  **And the player cannot fix it**: RimWorld's only per-work-type priority UI is the Work tab, whose
  `MainTabWindow_PawnTable.Pawns` is `mapPawns.FreeColonists`, filtered on `RaceProps.Humanlike`, so
  it never lists a mech. Biotech's Mechs tab has a work mode and no per-work-type column. So the mod
  raises it itself in `SimpleImproveMapComponent`, from 0 only, for colony mechs only, guarded on
  `WorkTypeIsDisabled` because `SetPriority` logs a red error and refuses otherwise. That is not
  overriding a player choice only because no UI lets a player make that choice for a mech; if one
  ever appears, revisit it.
- **A work type added to an existing save arrives switched off, and the two halves of that differ by
  pawn kind.** `DefMap.ExposeData` pads an unseen def with `new V()`, which for an int is 0, and
  nothing downstream raises it: `Pawn_WorkSettings.ExposeData` only ever calls `Disable` on load. For
  a pawn generated afterwards, `EnableAndInitialize` assigns priority 3 to only the **six** work types
  with the highest average relevant skill, because `LimitInitialActiveWorks` is
  `!pawn.RaceProps.IsMechanoid`. So a new colonist weak at Construction can still arrive with the work
  type off, while a mech has no cap and always gets it. Say this in the docs or the fix reads as
  ineffective.
- **A worker's Construction level is not `pawn.skills.GetSkill(...).Level`, and the two vanilla APIs
  that answer the question disagree with each other.** Read it through `WorkerSkill.Of`, never
  directly. `Pawn.skills` is assigned only inside `if (pawn.RaceProps.Humanlike)` in
  `PawnComponentsUtility.CreateInitialComponents`, and `Humanlike` is `intelligence >= Humanlike`
  while every mechanoid ships `ToolUser`, so it is null on mechs, animals and any modded drone race.
  - `QualityUtility.GenerateQualityCreatedByPawn` branches on `RaceProps.IsMechanoid` and has **no
    null guard** on its other branch, so it throws for a non-mechanoid with no tracker. That call is
    where every improve job ends, which is why the gate has to refuse such a pawn outright rather
    than treat it as skill 0, and why it has to refuse it even on an "any improvement" mark where no
    requirement is checked at all.
  - `GenConstruct.CanConstruct` uses a different shape again: two **independent** `if`s, one on
    `p.skills != null` and one on `p.IsColonyMech`, not an if/else and not a fall-through. A pawn
    that is neither passes its skill gate entirely ungated.
  - `Bill.PawnAllowedToStartAnew` is the vanilla site that does produce an effective level as an int,
    and it pre-guards with `(p.skills != null || p.IsColonyMech)`. There is no general helper:
    `mechFixedSkillLevel` has nine read sites and every one spells the fallback inline.
  - `RaceProperties.mechFixedSkillLevel` defaults to **10** and no shipped def overrides it, so every
    vanilla mech is judged at Construction 10. Mechs get no skill factor on `ConstructionSpeed`
    though: `StatWorker` applies `noSkillFactor`, which defaults to 1, so a constructoid works at
    base speed while being gated as a 10.
- **`pawn.workSettings` is not null on a colony mech.** `PawnComponentsUtility.AddAndRemoveDynamicComponents`
  constructs one for every player-faction mechanoid carrying a `CompOverseerSubject`, which is all of
  them. It is null on a dead pawn (`RemoveComponentsOnKilled` nulls it) and on other non-humanlikes.
  Guard with `workSettings != null && workSettings.EverWork` as `JobGiver_Work.GetPriority` does, not
  with a bare null test: `WorkIsActive` does **not** throw on an uninitialised one, it logs an error
  and silently rewrites the pawn's whole priority table, which the player never sees.
- `MapPawns.FreeColonistsSpawned` gates on `RaceProps.Humanlike`, so it **excludes colony mechs**, and
  so do `FreeColonists`, `FreeColonistsAndPrisoners(Spawned)` and every `PawnsFinder` colonist list.
  There is no vanilla list of colonists plus mechs; `SpawnedColonyMechs` is mechs only. Both getters
  return shared buffers they clear on access, but they are different buffers, so reading one does not
  clobber the other.
- **A work-type column header is `WorkTypeDef.labelShort`, capitalised.** `PawnColumnWorker_WorkPriority`
  draws `def.workType.labelShort.CapitalizeFirst()`. English `labelShort` here is **Improve**, not
  "Improving", and the store copy said "the Improving column" until an adversarial review caught it.
  Same class of error as Chrono Save's "Mod Settings". The Japanese Work tab is another: vanilla
  calls it **優先順位**, not 作業 or 仕事, so a literal translation names a screen that does not exist.
- The mod's Japanese strings say `建設スキル`; vanilla's Construction skill is `建築`. The new
  `SimpleImprove_NoConstructionSkill` follows vanilla, because it renders in a float menu beside
  vanilla's own text. The four older keys were left alone. Normalise them with the localisation work
  in #18, and pick vanilla's term.
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

Not re-derived here. `/Users/matthewscott/Programming/rimworld/docs/DEFECTS.md` is the ranked
register and supersedes this table; the 35 filed issues carry the ordering constraints. Several rows
are the consequence of a trap above; the traps carry the mechanism.

Fixed on 2026-09-17: the comp persistence (issue #2), which also retired `DynamicComponentPatch` and
the two-`[HarmonyPatch]`-attribute trap along with it. Fixed on 2026-09-18: the unguarded
`pawn.skills` and `pawn.workSettings` dereferences (issue #9), colony mechs being unable to hold
the work type (issue #8), and the work giver scanning every artificial building on every job search
(issue #3).

One trap came out of #3 and is not obvious from either the code or the issue. **An Odyssey gravship
jump strands a thing designation on the map it left.** `Thing.DeSpawn` never touches the designation
manager at all, and `Building.DeSpawn` only asks `Notify_BuildingDespawned` to clear defs that set
`removeIfBuildingDespawned`, which vanilla sets on Plan, Mine and MineVein alone.
`GravshipUtility.GenerateGravship` then despawns every building *before* sweeping the substructure
cells, and that sweep looks through `thingGrid`, which the despawn has already emptied, so the
designation is never swept; the `Gravship` holds its things in a plain `Dictionary` rather than a
`ThingOwner` and carries only the three floor designations across. That was invisible while the
comp's bool decided whether work happened. It is not invisible now, which is why `PostSpawnSetup`
restores it.

**Minifying is not a second case, and an earlier version of this paragraph said it was.**
`ThingOwner.NotifyAdded` calls `RemoveAllDesignationsOn` on *every* map when the holder
`IsEnclosingContainer()` (which excludes only carry trackers, corpses, maps, caravans, trader
trackers and trade ships, so a `MinifiedThing` qualifies). That goes through `RemoveDesignation` and
so fires `Notify_Removing`. Uninstalling a marked building loses the mark on both sides and stays
consistent. The same applies to anything else that puts a thing in a container.

| Defect | file:line | What breaks |
|---|---|---|
| ~~No `ShouldSkip`, no `PotentialWorkThingsGlobal`~~ | fixed 2026-09-18, issue #3 | Was: every pawn reachability-scanned every `BuildingArtificial` on the map on every job search, even with nothing marked. `ShouldSkip` now exits on the designation count, and the search set is the designated buildings with `PotentialWorkThingRequest` left `Undefined` so no region walk is built |
| `HasJobOnThing` delegates to `JobOnThing` | `1.6/Jobs/WorkGiver_Improve.cs:49` | Full job construction runs as the scan validator, then again on the winner |
| Nested `GenClosest.ClosestThingReachable` inside that validator | `1.6/Jobs/WorkGiver_Improve.cs:186` | Unbounded (9999f) map search per required material, per candidate |
| A completed `Building` is handed to `GenConstruct.CanConstruct` | `1.6/Jobs/WorkGiver_Improve.cs:105` | An argument shape no vanilla caller produces. Any third-party postfix that assumes a blueprint or frame throws and kills the whole scan |
| Chairs cannot be improved (likely) | `1.6/Jobs/WorkGiver_Improve.cs:105` | `CanConstruct(..., checkSkills: true, ...)` enforces `constructionSkillPrerequisite`, which `DiningChair` (4), `Armchair` (5) and `Couch` (5) declare and beds, stools and dressers do not |
| ~~Unguarded `pawn.skills` dereference~~ | fixed 2026-09-18, issue #9 | Was: any non-humanlike worker NREs, every tick during the job. All five sites and both `workSettings` sites now go through `WorkerSkill` and `ImproveWorkers` |
| ~~Mechs can never take the work type~~ | fixed 2026-09-18, issue #8 | Was: `Mech_Constructoid.mechEnabledWorkTypes` lists only `Construction`. `1.6/Patches/MechWorkTypes.xml` appends the improving work type to it |
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

Tests: `dotnet test Tests/SimpleImprove.Tests.csproj`, 99 passing as of 2026-09-18, against the real
`Assembly-CSharp.dll`. Outside the sln so the solution build stays mod-only and warning-free, and
`Compile Remove="Tests/**"` keeps the sources out of the shipped DLL. See `Tests/README.md`.

`SimpleImproveSettings` no longer has a static constructor. It used to call `InitializePawnModifiers`,
which read `ModsConfig.IdeologyActive` and initialised `Verse.UnityData` through a native call, so
naming the type at all threw outside the game. `InitializePawnModifiers(bool ideologyActive)` is now
public and `ModEntry` passes the flag in. `new ThingDef()` still throws (`BaseContent` ->
`ShaderDatabase` -> `Resources.Load`), so tests build defs with `FormatterServices.GetUninitializedObject`
and must set every field they intend to read.

No CI. `./build.sh` stages into `$RimWorldDir/Mods/SimpleImprove` and is destructive about it; the
root file has the detail.

The best test that does not need the game: `SimpleImproveSettings.GetSkillRequirement(q, pawn: null)`
is pure over the skill dictionary, so a table test across all seven `QualityCategory` values catches
the Legendary clamp. `DetermineClosestPreset`, `ValidateAndFixLoadedData` and the
`SimpleImproveMapComponent` dictionary methods (constructible with `new SimpleImproveMapComponent(null)`)
are equally pure. Verifying the save/load round-trip end to end still needs the game: a dev-mode
debug action that saves, reloads and asserts `WorkDone`.

## Repo-specific notes

- Nothing under `1.6/Assemblies/net472/` has been tracked since 2026-09-17, so a rebuild no longer
  shows in the diff. The published Workshop build still carries the old binaries; see the register.
- `Workshop/*.md` holds nine translated store descriptions that the in-game uploader cannot
  republish, so changing store copy means the Steam website. See `ship-mod`.
- GPLv3, but `About/About.xml` carries no `<url>`, so the binaries reach 6356 subscribers with no
  source pointer.
