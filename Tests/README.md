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
`SimpleImproveMapComponent` (the target quality store), `WorkerSkill` (the skill gate that keeps a
skill-less worker away from the quality roll), `ImproveWorkers` (the mech priority correction),
`ImproveDesignations` (the work giver's search set and the designation re-sync), the work giver's
declared surface, the shipped `PatchOperation` XML, and the container allocation behaviour on
`SimpleImproveComp`. That last one is small and matters more than its size: `GetDirectlyHeldThings`
must report null until something is hauled, because declaring the component on the defs puts every
quality building into `ThingRequestGroup.ThingHolder` and vanilla traversals call it on all of them.

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

**Be precise about what that does not cover, because the nine above make it look wider than it is.**
Three method bodies in this fix are unreachable, and mutations inside them were measured to pass the
whole suite: dropping the `!` from `ShouldSkip` (97/97), replacing
`PotentialWorkThingsGlobal`'s body with `ScanTargets(null)` (97/97), and deleting the entire
`PostSpawnSetup` repair (97/97). The reflection tests hold the *declarations*, not the bodies. No test
can reach them either: `Map` is unconstructible outside a running game and `DesignationManager` needs
one, so `ShouldSkip`, `PotentialWorkThingsGlobal` and `PostSpawnSetup` cannot be executed here at all.
What the split buys is that the decisions those three bodies delegate to are covered; what it does not
buy is any assurance they still call them. Only an in-game check closes that.

Quote coverage against those eight types, never the repo: most of this mod needs a spawned `Thing` on
a `Map` and a whole-repo figure would be misleading.

The background is `docs/spikes/test-harness/README.md` in the workspace, which records what each
experiment established, including the two whose failure is the finding.
