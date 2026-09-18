# Tests

```sh
export FrameworkPathOverride=/opt/homebrew/opt/mono/lib/mono/4.7.2-api
dotnet test Tests/SimpleImprove.Tests.csproj
```

net472 NUnit, running against the real `Assembly-CSharp.dll` from the installed game rather than a
stub. `RimWorldDir` must be set; the workspace `.claude/settings.json` and `~/.zshrc` both export it.

`MechWorkTypePatchTests` goes further and reads the game's shipped def XML under
`$RimWorldDir/Data`, so that a RimWorld update which renames or moves `Mech_Constructoid` fails a
test rather than silently turning the mod's def patch into a no-op. The two tests that need Biotech
`Assume` the file exists and are skipped rather than failed when it does not, because Biotech is a
paid DLC and the patch is a no-op without it. Nothing else here depends on a DLC.

This project is deliberately not in `rimworld-simple-improve.sln`, so `dotnet build` on the solution
still builds only the mod and stays at zero warnings. The mod's own `.csproj` removes `Tests/**` from
its compile items, so nothing here can reach the shipped assembly.

## How it is wired

The mod's sources are compiled into the test assembly rather than referenced as a built DLL. That
keeps private and internal members reachable without an `InternalsVisibleTo`, and it avoids a
`ProjectReference` rebuilding the mod into `1.6/Assemblies/net472` in Debug, which is where
`build.sh` expects to stage a Release artefact from.

## What can and cannot be tested

Unity types load fine outside a Unity player. The real boundary is static game state and native
calls, and in this mod it is wide:

- `SimpleImproveSettings` used to have a static constructor that read `ModsConfig.IdeologyActive`,
  which initialises `Verse.UnityData`, which makes a native call, so merely naming the type threw.
  That chain is broken: `InitializePawnModifiers` now takes the DLC flag and the mod's constructor
  passes it in. The skill table, the presets and the modifier registration are all covered.
- `new ThingDef()` throws. Its constructor reaches `Verse.BaseContent`, whose static constructor
  initialises `Verse.ShaderDatabase` and calls `UnityEngine.Resources.Load`. Defs for predicate
  tests have to be built with `FormatterServices.GetUninitializedObject`, which leaves every field
  at its type default, so a test must set every field it intends to read. Defs built this way are
  for predicate logic only and must never stand in for real def data.
- Harmony cannot patch on this runtime at all, in or out of the game process, so
  `CompInjectionPatch` and `DesignationCancelPatch` have no path to automated coverage. The logic
  `CompInjectionPatch` calls is covered; the patch attribute that calls it is not.
- `SimpleImproveComp`, the JobDrivers, the WorkGiver and the gizmo code all need a spawned `Thing`,
  a `Map` and in most cases `Find.Selector`. None of those **methods** is reachable. A work giver's
  declared **surface** is: `new WorkGiver_Improve()` constructs, and reading a property that returns
  a struct touches no static state, so `PotentialWorkThingRequest`, `PathEndMode` and
  `MaxRegionsToScanBeforeGlobalSearch` can all be asserted. `WorkGiverSurfaceTests` exists for that
  and is worth knowing about before concluding something here is untestable.
- `new Thing()`, `new Building()` and `new Designation(LocalTargetInfo, DesignationDef)` all work,
  which is more than the rest of this list suggests. `DesignationManager` does not: its `DefMap`
  throws without an initialised def database, which is what keeps `ShouldSkip` out of reach.
- `Thing.Spawned` reads `Find.Maps` and is therefore **false for every `Thing` built here**, which
  is a trap rather than a limit. A predicate filtering on `Spawned` can only be tested for
  exclusion, and such a test passes just as well against a predicate that returns nothing at all.
  Decide over primitives instead, as `ImproveDesignations.RepairNeeded` does.

Covered today: `ImprovableDefs` (the decision the save/load fix rests on),
`SimpleImproveSettings` (skill table, presets, modifier registration),
`SimpleImproveMapComponent` (what is left of the legacy target quality store, and the migration
decision over it), `WorkerSkill` (the skill gate that keeps a skill-less worker away from the
quality roll), `ImproveWorkers` (the mech priority correction), `ImproveDesignations` (the work
giver's search set and the designation re-sync), `StoredMaterials` (when hauled materials come
back), the work giver's declared surface, the shipped `PatchOperation` XML, and the target quality
field and container allocation behaviour on `SimpleImproveComp`.

`WorkerSkill` is the worked example of the split this harness rewards, and it is worth copying. The
readings that cannot be tested (`pawn.skills`, `RaceProps.IsMechanoid`, `mechFixedSkillLevel`) are
taken in one method, `Of(Pawn)`, which decides nothing. Everything that decides anything takes
primitives: `From` picks the branch, `FirstBlocker` orders the two guards. Both are exercised here,
and the fixture records which mutations each one catches.

What that still does not reach: the `FirstBlocker` calls in `WorkGiver_Improve` and
`JobDriver_Improve` themselves. Deleting either is invisible to this suite, because both need a
spawned `Thing` on a `Map`. Routing both through one function shrinks the gap rather than closing it.

`ImproveWorkers.ShouldEnableForMech` is covered the same way. The readings it decides over
(`IsColonyMech`, `WorkTypeIsDisabled`, `GetPriority`) are all unreachable, so the map component takes
them and passes three primitives in. `PotentialOnMap` beside it is pure readings and has no coverage,
which is the right split rather than an omission.

`ImproveDesignations` covers the work giver's search set, and the two halves of it are covered
differently on purpose. `ScanTargets` is exercised against real `Designation` objects, because those
can be built outside the game; `RepairNeeded` takes two booleans, because everything it decides over
(`Thing.Map`, `DesignationOn`) cannot be. The call sites of both, `PotentialWorkThingsGlobal` and
`PostSpawnSetup`, are still unreachable.

`StoredMaterials` is the same split again for the materials staged inside a building. Both of its
call sites, `SimpleImproveComp.PostDeSpawn` and `ReturnStoredMaterialsWhileSpawned`, need a spawned
`Thing` on a `Map`; the three booleans they decide over do not. The gravship case is the one worth
knowing about, because it is the only one where keeping the materials is correct and because the
seven vanilla components that make the same call guard it differently, on
`mode != DestroyMode.WillReplace`, which would be wrong here.

The target quality field on `SimpleImproveComp` is genuinely reachable rather than split, and that is
itself the fix: the property used to go through `parent?.Map?.GetComponent`, so it could not be
constructed or read without a map. It is a field now, so `TargetQuality` round-trips on a bare
`new SimpleImproveComp()`, and so does the invariant that clearing the mark clears the target.

`SimpleImproveMapComponent.FinalizeInit` is reachable too, which is rare enough here to be worth
naming. `MapComponent.FinalizeInit` is an empty virtual and `EnableImprovingForColonyMechs` returns
early on a null map, so the whole override runs against `new SimpleImproveMapComponent(null)` and the
discard it performs is tested rather than inferred.

`WorkGiverSurfaceTests` is a different kind of test and the reason it exists is worth repeating. A
performance fix has no functional signature: re-adding the `PotentialWorkThingRequest` override would
restore the thirty-region scan that was this mod's most-reported defect, and every other test here
would still pass. Where the absence of a fix is invisible to behaviour, the declaration is what has
to be asserted.

Fixes here are checked by mutation rather than by inspection, and the fixtures say which mutations
they catch. For the work giver those are: re-adding the `ThingRequest`; deleting either override;
dropping the null test in `ScanTargets`; turning `ScanTargets` back into a lazy iterator; inverting,
widening or making `RepairNeeded` two-way; and `ScanTargets` ignoring its input. All nine fail as
test failures rather than as compile errors, which is a distinction worth keeping: an assertion on
`.Count` silently became a LINQ method group under the iterator mutation and had to be rewritten as
`Has.Count` to fail properly.

For the container, three more: reverting `GetDirectlyHeldThings` to the raw field, widening
`ShouldScribeContainer` back to a non-null test, and making `GetChildHolders` allocate.

For the material return and the target quality move, measured on 2026-09-18 against the suite at 136
tests, eighteen more are caught: `OnDeSpawn` ignoring the gravship flag, the map, or whether
anything is staged, or answering unconditionally; `OnUnmark` ignoring the map;
`ShouldMigrateTargetQuality` dropping any one of its three conditions; `TakeTargetQuality` not
removing what it read; `DiscardUnclaimedTargetQualities` doing nothing, not being called from
`FinalizeInit`, or `FinalizeInit` no longer overriding; `SetMarkedForImprovementDirect` leaving the
target behind, or clearing it when marking rather than unmarking; the `TargetQuality` setter
becoming a no-op; and the `PostDeSpawn` override being removed.

One mutation that was expected to be caught and is not, which is worth recording rather than
quietly dropping: rewriting `OnUnmark` to call `OnDeSpawn` with a hardcoded `false` passes the
suite. That is because it is behaviour preserving, not because of a gap.
`TheTwoDecisionsDisagreeAboutAGravship` says so in the fixture rather than implying it catches
something it does not.

**Be precise about what that does not cover, because the list above makes it look wider than it is.**
Several method bodies are unreachable, and mutations inside them were measured to pass the suite
entire. Against the suite at 97 tests: dropping the `!` from `WorkGiver_Improve.ShouldSkip`,
replacing `PotentialWorkThingsGlobal`'s body with `ScanTargets(null)`, and deleting the
`PostSpawnSetup` designation repair. Against the suite at 136 tests, six more: emptying
`PostDeSpawn`'s body, emptying `ReturnStoredMaterialsWhileSpawned`'s body, deleting the target
quality migration call, hardcoding `true` for the mark argument it passes, deleting the
`Scribe_Values.Look` for `targetQuality`, and making the designation-cancel prefix stop returning
materials.

The reflection tests hold the *declarations*, not the bodies, and no test can reach the bodies
either: `Map` is unconstructible outside a running game, `DesignationManager` needs one, `Scribe` is
static game state, and Harmony cannot patch on this runtime at all. What the split buys is that the
decisions those bodies delegate to are covered; what it does not buy is any assurance they still
call them. Only an in-game check closes that, and it is the largest outstanding gap in this suite.

Quote coverage against those nine types, never the repo: most of this mod needs a spawned `Thing` on
a `Map` and a whole-repo figure would be misleading.

The background is `docs/spikes/test-harness/README.md` in the workspace, which records what each
experiment established, including the two whose failure is the finding.
