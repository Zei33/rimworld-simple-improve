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
| `SimpleImproveMapComponent : MapComponent` | `1.6/Core/SimpleImproveMapComponent.cs` | Colony mech work priorities on `FinalizeInit`, plus a read-only `Dictionary<int, QualityCategory>` that migrates target qualities out of a pre-1.0.9 save and is discarded at the end of that same load |
| `StoredMaterials` | `1.6/Core/StoredMaterials.cs` | `OnDeSpawn` and `OnUnmark`: when staged materials are returned to the map. Pure, and covered |
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
`TargetQuality` (a field on the comp since 2026-09-18) and `IsMarkedForImprovement` (which adds the
designation).
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
| `DesignationCancelPatch` | `1.6/Patches/DesignationCancelPatch.cs:12` | `Verse.Designation.Notify_Removing` | Prefix, returns materials on the still-spawned paths and kills jobs |

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
  - ~~`DesignationCancelPatch` dropping into a null map (S-9, #10)~~ fixed 2026-09-18 with #11 and
    #13 in one commit, as ordering constraint 3 required.
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
- **Exactly one thing returns staged materials, and it is `SimpleImproveComp.PostDeSpawn`.** Three
  places used to disagree, which is issues #10 and #11. `Thing.Destroy` despawns before it removes
  designations and before `PostDestroy`, and `MinifyUtility.MakeMinified` despawns before it assigns
  `InnerThing`, so the despawn hook runs first on every path and the container is already empty by
  the time anything else looks. `ReturnStoredMaterialsWhileSpawned` is the second owner and covers
  the one case that never despawns: a plain cancel on a building that keeps standing.
  `StoredMaterials` carries both decisions.
  **The gravship guard is `parent.BeingTransportedOnGravship`, not the
  `mode != DestroyMode.WillReplace` that all seven equivalent vanilla comps gate on**
  (`CompThingContainer`, `CompTransporter`, `CompGenepackContainer`, `CompBiosculpterPod`,
  `CompGaumakerPod`, `CompDryadCocoon`, `CompDryadHealingPod`). `WillReplace` also means "something
  is being built here instead", which is how `GenSpawn` wipes what it covers and how
  `SmoothableWallUtility` swaps a wall, and in those the materials must come back or they are lost
  with the old thing. Do not "correct" it to match vanilla. Vanilla does not think the mode is a
  sufficient gravship test either: `CompTransporter.PostDeSpawn`, the one holding things the colony
  put there deliberately, opens with an early return on `parent.BeingTransportedOnGravship` before
  it reaches its own `WillReplace` drop.
- Target quality is a field on the comp and is scribed with no default argument, which is what
  vanilla does for every nullable in the assembly and the only spelling that keeps unset fields out
  of the file. It used to read through `parent.Map.GetComponent<SimpleImproveMapComponent>()` on
  every access, so it was null off-map, null while despawned and null for every thing during loading,
  and the setter silently discarded writes in all three. The map component's orphan sweep then
  deleted the entry for any marked building that was minified, in a caravan or in a container when a
  map loaded. That is issue #13; the dictionary survives as a one-way migration read on spawn, and
  `FinalizeInit` discards whatever is left once the map has finished loading.
  **Do not remove that discard to be kinder to off-map buildings.** An entry can only ever be claimed
  by a building standing on a map when a save loads: a reinstall spawns with `respawningAfterLoad`
  false and never consults the store. A surviving entry therefore gets exactly one more chance, the
  next load, by which time the player may have set a target of their own, and the comp writes nothing
  when the target is null so "any improvement" and "never had one" are the same state. That is a
  wrong setting applied silently, which is worse than a lost one. Nothing marked is discarded either:
  a building that is off a map went into a container, and `ThingOwner.NotifyAdded` clears its
  designations and so its mark. The migration is additionally gated on the mark for the same reason,
  because before 1.0.9 cancelling an improvement left the target behind in this dictionary.
- **The material cost range lives in `MaterialCostField`, and nothing else is allowed to write a
  bound.** Until 1.0.9 the range was spelled out in four places that disagreed: the settings window
  clamp and the `ValidateAndFixLoadedData` clamp both said 5% to 100000%, the field's doc comment
  said 0.1 to 5.0, the tooltip said 10% to 500% in all nine languages and the store copy said 5% and
  upwards in all nine. The tooltip's band won because three of the four sources agreed on it, so the
  clamp moved rather than the copy. `MaterialCostFieldTests` reads the nine shipped Keyed files and
  fails if any of them stops naming the constants, which is the only automated defence this repo has
  against shipped copy drifting from the code.
- **A text field that clamps and rewrites its own buffer on the same keystroke is untypable below
  its minimum.** The old material cost field replaced a typed `1` with the clamped `5` before the
  player could reach `100`, so every percentage starting 0 to 4 was unreachable, the default among
  them. `MaterialCostField.Typing` hands the text back untouched and `MaterialCostField.Unfocused`
  does the clamping once the control loses focus, told apart by a `GUI.SetNextControlName` name
  compared against `GUI.GetNameOfFocusedControl()`. **Vanilla's `Widgets.TextFieldNumeric<T>` has
  the same trap**, because `ResolveParseNow` clamps and rewrites as soon as the text is a fully
  typed number; it gets away with it only because its float fields are nearly all minimum 0.
  `SetSkillBuffer` is sound for that same reason and was deliberately left alone.
- **A quality modifier says which kind it is; never infer it from the pawn.** `PawnQualityModifiers`
  entries are `PawnQualityModifier.Attainable(worth, f)` or `PawnQualityModifier.Carried(f)`.
  Attainable means any pawn could come to have it, so `GetBestCaseSkillRequirement` counts it once
  for the colony whether or not anybody has it (inspired creativity, worth 2). Carried means only
  the pawns who have it, so the best case is the best of them (the Ideology production role, whose
  offset is def data). Until 1.0.9 the best-case scan told them apart by asking whether the pawn was
  inspired, which skipped **every** modifier belonging to an inspired pawn, so a colony whose only
  production specialist happened to be inspired had them counted for nothing. `GetSkillRequirement`
  reads `BonusFor(pawn)`, what this pawn is getting now; the best case reads `Worth`, what it would
  be worth to somebody who had it. Both go through one `RequirementForBonus`, so there is one clamp
  and one table lookup rather than two of each.
- **The best case scans `FreeColonistsSpawned`, not `ImproveWorkers.PotentialOnMap`, and that is
  deliberate.** The two call sites use `PotentialOnMap` when deciding whether to warn, because a
  colony mech can do the work. The bonus scan excludes mechs because a mech can hold neither bonus,
  and the reason is not in the quality roll. `QualityUtility.GenerateQualityCreatedByPawn` reads
  `pawn.InspirationDef` with **no race test**, and its `IsMechanoid` ternary picks the skill level
  and nothing else, so that method would happily add two tiers to an inspired mechanoid. The gate is
  upstream: `InspirationWorker.InspirationCanOccur` rejects `!pawn.IsColonist` unless the def sets
  `allowedOnNonColonists`, `Inspired_Creativity` does not set it, and `Pawn.IsColonist` requires
  `RaceProps.Humanlike`; a mech also has no mood need, so `InspirationHandler.StartInspirationMTBDays`
  returns -1. The role half is simpler: `PawnComponentsUtility` creates `pawn.ideo` only inside
  `if (pawn.RaceProps.Humanlike)`, so `Pawn.Ideo` is null for a mechanoid. An auditor diffing the two
  sites will otherwise "fix" this one to match.
- **Nothing in this mod may hand a completed `Building` to `GenConstruct.CanConstruct`, and
  `ImproveSite` exists to keep it that way.** Every one of vanilla's seven call sites passes a
  `Blueprint` or a `Frame`, the only two implementors of `IConstructible`, and both always carry a
  non-null `def.entityDefToBuild`. A finished building carries none, so a third-party postfix reading
  that field throws here and nowhere else, and the exception kills the whole work giver rather than
  reading as a fault in the patch. Two players reported it a month apart in 2025 with the same mod in
  the stack.
  - **A null `jobForReservation` is not the unusual part.** Two of the seven vanilla sites pass null,
    `JobDriver_ConstructFinishFrame` and `WorkGiver_ConstructFinishFrames`, so passing a job def
    would have changed the shape without touching what differs and fixed neither report.
  - **There were two call sites, not one.** The issue named only `WorkGiver_Improve`.
    `JobDriver_Improve` made the same call from inside a `FailOn` lambda, which runs while the pawn
    works rather than once per scan, and which a search for the call in that method does not find
    because the compiler hoists the lambda into a nested type. A test now asserts the whole assembly
    makes no such call, for that reason.
  - **Hand-rolling is not reimplementing.** With `checkSkills: false` and a finished building exactly
    five of `CanConstruct`'s checks are reachable, and all five are public vanilla methods called
    directly: `FirstBlockingThing`, `CanTouchTargetFromValidCell`, `CanReserveAndReach`, `IsBurning`
    and `Ideo.MembersCanBuild`. The skill block is the one this mod bypasses on purpose, and the
    trailing blueprint-or-frame block is dead code for something that already exists.
  - **The cost is Humanoid Alien Races.** Most postfixes on this method never answered for a finished
    building anyway: Vanilla Expanded Framework's three bail on a null `entityDefToBuild` and Alpha
    Genes' compares it against a def, so it is false for the very building it gates. HAR's is
    `RaceRestrictionSettings.CanBuild(t.def.entityDefToBuild ?? t.def, p.def)`, which answers
    correctly, so a race forbidden to build something was also forbidden to improve it and now is
    not. Invisible in game, so it needs a release note.
  - **`FirstBlockingThing` still gets the finished building, and one line keeps that safe.** It
    reaches `BlocksConstruction`, which dereferences `BlueprintDefOf(constructible).entityDefToBuild`,
    and `BlueprintDefOf` returns `def.blueprintDef` for anything that is neither blueprint nor frame.
    So a marked building whose def has no blueprint throws inside vanilla as soon as anything else
    occupies one of its cells, including the worker, because `BlocksConstruction` is evaluated before
    the `!= pawnToIgnore` test. Nothing in vanilla holds that shut; `ImprovableDefs.Qualifies` and the
    two marking guards do. `CanWorkOn` now refuses such a building itself rather than relying on them.

- **Unity never takes keyboard focus off a text field when the player clicks somewhere else, and
  neither does RimWorld's window code.** `GUI.HandleTextFieldEventForDesktop` assigns
  `GUIUtility.keyboardControl` only when a mouse down lands inside the field, `GUI.DoControl` behind
  every button and checkbox never touches it, `Dialog_ModSettings` makes no focus call, and the close
  button, the close X, the click-outside path and Escape all leave it where it was. So anything built
  on "the field lost focus" has to arrange that itself. `Widgets.DelayedTextField` is vanilla's own
  proof: it detects the outside mouse down and forces focus onto a dummy label. The material cost
  field does the same test through `OriginalEventUtility.EventType`, which has to be read rather than
  `Event.current.type` because a click on a preset button has already consumed the event, and
  `SimpleImproveMod.WriteSettings` is the backstop for closing the window, because
  `Dialog_ModSettings.PreClose` calls it on every exit.
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
the work type (issue #8), the work giver scanning every artificial building on every job search
(issue #3), the null container handed to the selection code (issue #22), and the staged materials
being dropped into a null map, stranded on uninstall, or losing their target quality (issues #10,
#11 and #13 as one commit, with #12 closed as overtaken).

One residual from that last commit, checked and left alone. A gravship jump leaves the old map's
designation manager holding a `Designation_Improve` whose target thing is now on another map.
`SpawnedDesignationsOfDef`, `AnySpawnedDesignationOfDef` and `DrawDesignations` all filter on
`!target.HasThing || target.Thing.Map == map`, so it is invisible to the work giver and is not
drawn, and `AddDesignation` only rejects a double-add within one manager, so the new map's
designation is added cleanly. If the building ever comes back to that map the orphan becomes valid
again and `PostSpawnSetup` correctly does nothing. It costs a few bytes in the old map's save.

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
| ~~`HasJobOnThing` delegates to `JobOnThing`~~ | fixed 2026-09-18, issue #5 | Was: full job construction ran as the scan validator and again on the winner. **The issue's own fix was refused**: a cheap field-test predicate breaks the invariant `JobGiver_Work` relies on, and a desync there makes the pawn abandon every remaining work giver across every work type, diagnosed only by a `Log.ErrorOnce` on key 6112651 that any other mod may already have consumed. Both methods answer from one memoised `JobFor` instead |
| ~~Nested `GenClosest.ClosestThingReachable` inside that validator~~ | fixed 2026-09-18, issue #6 | Was: an unbounded map search per required material per candidate, at `Danger.Deadly`, because the bare `TraverseParms.For(pawn)` defaults to the LOOSEST threshold rather than none. Now `forced ? Deadly : NormalMaxDanger()`, bounded by a per-tick cache of failed searches in vanilla's own shape. 9999f is correct and kept. **Player-visible tightening**, needs a release note |
| ~~A completed `Building` is handed to `GenConstruct.CanConstruct`~~ | fixed 2026-09-18, issue #7 | Was: an argument shape no vanilla caller produces, from **two** call sites rather than the one the issue named. `ImproveSite.CanWorkOn` calls the same five public vanilla methods in the same order instead. Costs Humanoid Alien Races players its building restriction on improvement; needs a release note |
| ~~Chairs cannot be improved~~ | fixed 2026-09-17, confirmed in game | Was: `CanConstruct(..., checkSkills: true, ...)` enforcing `constructionSkillPrerequisite`, which `DiningChair` (4), `Armchair` (5) and `Couch` (5) declare and beds, stools and dressers do not. There is no `checkSkills` argument anywhere in the mod since #7: the block that read it is not transcribed into `ImproveSite` at all, so the regression cannot return by flipping a flag |
| ~~Unguarded `pawn.skills` dereference~~ | fixed 2026-09-18, issue #9 | Was: any non-humanlike worker NREs, every tick during the job. All five sites and both `workSettings` sites now go through `WorkerSkill` and `ImproveWorkers` |
| ~~Mechs can never take the work type~~ | fixed 2026-09-18, issue #8 | Was: `Mech_Constructoid.mechEnabledWorkTypes` lists only `Construction`. `1.6/Patches/MechWorkTypes.xml` appends the improving work type to it |
| Both `Designator` classes are dead code | `1.6/Designators/Designator_MarkForImprovement.cs:14` | No `DesignationCategoryDef` in the mod's source or its git history. The shipped docs no longer advertise the tab (issue #4, #20); the nine generated store pages still do, and that copy belongs to `workshop-content-builder` |
| ~~Legendary skill requirement unreachable~~ | fixed 2026-09-18, issue #14 | Was: `Mathf.Clamp(baseQuality, 0, 5)` indexed the skill table while `QualityCategory.Legendary` is 6, so the configured Legendary number was never read. The bound is `HighestQualityIndex` now. Raises an ordinary pawn's Default requirement from 18 to 20: a live balance change, and a pawn with an inspiration or a role bonus was already getting the right number |
| ~~The best case drops the production specialist when inspired~~ | fixed 2026-09-18, issue #16 | Was: the best-case scan used the pawn's inspiration state as a proxy for which modifier it held, so an inspired pawn's modifiers were all skipped and the warning quoted a number up to six Construction levels too high. Modifiers declare their kind now. See traps |
| The skill warning drops "assigned to improvement" in eight languages | issue #24, open | The four warning strings say "no colonist can improve this" outside English, while the check only ever looked at pawns with the work type switched on. Bound to the #17/#18 locale pass by constraint 11a |
| ~~The material cost percentage cannot be typed~~ | fixed 2026-09-18, issue #15 | Was: three defects locking each other in. The field rewrote its own buffer to the clamped value on every keystroke, `ResetToDefaults` wrote the multiplier into a percentage buffer, and the clamp, the tooltip and the store copy named three different ranges. See traps |
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

## What this week's issue sweep established

All twelve open issues were closed on 2026-09-18. The findings worth keeping are these.

**`HasJobOnThing` must agree with `JobOnThing`, and the penalty for disagreeing is not local.**
`JobGiver_Work` uses the first as the scan validator and calls the second on the winner with no
re-check between. On a desync it leaves `bestTargetOfLastPriority` set, breaks at the next
`priorityInType` boundary and returns `NoJob`, and `workGiversInOrderNormal` is one flat list across
every enabled work type, so the pawn stops looking for **any** work. The only diagnostic is
`Log.ErrorOnce` on the literal key 6112651, shared by every work giver in the game, and `Log.Clear`
never resets `usedKeys`, so whichever mod desyncs first in a session silences every later one. Never
treat "no red error in testing" as evidence here.

**`TraverseParms.For(pawn)` means `Danger.Deadly`, not "unset".** The bare overload's default is the
loosest value. Anywhere this reads as a missing argument, the behaviour is the opposite of what it
looks like, and fixing it tightens rather than loosens.

**A per-frame cache keyed on `Time.frameCount` alone is wrong, and the reasoning that suggests it is
sound.** The selection can change part-way through a frame: `UIRoot_Play` draws the gizmo grid and
then runs `Selector.SelectorOnGUI`, whose mouse-up branch changes the selection, and the next pass
has the same frame number. Vanilla's `GizmoGridDrawer` keys on the frame **plus** an
element-by-element snapshot, and `ImproveSelection` copies that key exactly so it can never outlive
vanilla's. The opposite worry, Layout versus Repaint, is not a hazard: `Time.frameCount` is stable
across the passes of one frame and vanilla already builds in Layout and draws in Repaint.

**`ContentFinder<T>.Get` is not a dictionary lookup**, it walks every running mod in reverse load
order before falling through to `Resources.Load`. Cache it in a static, and refresh with an explicit
`== null`, never `??` or `?.`: a language change or a dev content reload calls
`UnityEngine.Object.Destroy` on every cached texture, and a destroyed Unity object is fake null, so
the null-coalescing operators see a live object and draw a destroyed texture.

**A `Job` is pooled.** `JobMaker` hands them out from `SimplePool<Job>` and `Pawn_JobTracker` returns
them. Anything that caches a `Job` must stop holding it the moment it hands it out.

**A test that reads source or a shell script as text cannot tell a use from a description of one.**
Two tests failed on their first run against the comment explaining the very thing they forbid, once
for `cp -r 1.6 release` in `build.sh` and once for `defaultLabel` in the detox mod. Strip comments
first, and say in the test why.

**Deleting a key from one language is invisible without a test, and so is a whole missing
`DefInjected` directory.** `Tests/LanguageParityTests.cs` now holds the nine languages to the same
Keyed set, the same injected def types and the same injected fields, checks every key the C# asks
for, and fails a language whose injection file is byte-identical to English. That last one is what a
copied and untranslated file looks like.

## Build and test

```sh
export FrameworkPathOverride=/opt/homebrew/opt/mono/lib/mono/4.7.2-api
dotnet build rimworld-simple-improve.sln -c Release   # clean, zero warnings
```

Tests: `dotnet test Tests/SimpleImprove.Tests.csproj`, 260 passing as of 2026-09-18, against the real
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
the Legendary clamp. `MaterialCostField`, `DetermineClosestPreset`, `ValidateAndFixLoadedData`,
`StoredMaterials`, and the
`SimpleImproveMapComponent` migration methods (constructible with `new SimpleImproveMapComponent(null)`)
are equally pure. Verifying the save/load round-trip end to end still needs the game: a dev-mode
debug action that saves, reloads and asserts `WorkDone`.

## Repo-specific notes

- Nothing under `1.6/Assemblies/net472/` has been tracked since 2026-09-17, so a rebuild no longer
  shows in the diff. The published Workshop build still carries the old binaries; see the register.
- `Workshop/*.md` holds nine translated store descriptions that the in-game uploader cannot
  republish, so changing store copy means the Steam website. See `ship-mod`.
- GPLv3, but `About/About.xml` carries no `<url>`, so the binaries reach 6356 subscribers with no
  source pointer.
