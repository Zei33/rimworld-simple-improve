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
- A `Building` can carry comps without a def. `new CompQuality()` constructs as well, and writing a
  list of one or two comps into the private `ThingWithComps.comps` field is enough for `TryGetComp`
  to answer: below three comps `GetComp` type-tests the list directly, and only at three or more does
  it consult `compsByType`, which nothing here builds. One or two, not "at most two": the fast path
  reads `comps[0]` whenever the count is below three, so an empty list throws
  `ArgumentOutOfRangeException` rather than answering null. Vanilla never builds an empty list,
  because `InitializeComps` leaves `comps` null when `def.comps` is empty, so leave the field null
  for a building with no comps. `Thing.Map` returns null without
  reading `Find` while `mapIndexOrState` is its initial -1. So a component standing on such a
  building runs anything that reads its own fields and its parent's comps, and stops at the first
  `parent.Map` test. `ImproveTargetTests` uses this for the stranded-mark decision, which is why that
  fix could be run rather than only read.
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
back), `MaterialCostField` (the material cost range and the two decisions that drive its text
field), `QualityBonuses` (the best case behind the skill warning), `ImproveSite` (the replacement for
`GenConstruct.CanConstruct`, whose one runnable guard is covered and whose five vanilla calls, split
between an access half and a permission half, are pinned by reading the compiled IL, and whose
ideoligion check runs for a worker with no ideoligion), `JobMemo` (the whole hand-over between
`HasJobOnThing` and `JobOnThing`: when to build, what to hold, what to hand out and when to forget),
`ImproveTarget` (whether a mark still has work ahead of it and whether a building can be offered
improvement at all, run as pure functions and again on a component standing on a hand-built
`Building`, with every call site pinned by reading the IL),
the work giver's declared surface, the shipped `PatchOperation` XML, the target quality field and
container allocation behaviour on `SimpleImproveComp`, the rule for when the Improve button counts
its buildings (`ImproveGroup.ShowsCount`), and `ReachabilityTests`, which fails on a private or
internal method nothing calls or a designator, work giver or job driver that no shipped def names.

`VanillaCancelTests` holds the two facts that put vanilla's own Cancel button on every marked
building: the mod's designation def does not opt out, and vanilla's default is to opt in. Issue #23
was filed and fixed without knowing that button was there, and in-game checks 8 and 9 depend on
telling it apart from the mod's own.

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

`GetBestCaseSkillRequirement` became reachable while being fixed, which is worth knowing because it
was previously the clearest example of a method nothing here could touch. It threw
`TypeInitializationException` on every call, from a null-map branch that read
`ModsConfig.IdeologyActive` to invent a role bonus. That branch is gone, so the no-map case runs
here; the with-pawns case runs through an `internal BestCaseRequirementFor` overload that takes the
pawns instead of a `Map`, which is the only reading the method now makes.

Two things about pawns in this project, both measured. `new Pawn()` constructs, which is more than
the rest of this list suggests and is what lets the quality clamp be tested with a pawn at all. But a
bare `Pawn` throws a `NullReferenceException` on `InspirationDef`, and the null is `health`, not
`mindState`: the property tests `Dead` first and `Pawn.Dead` is `health.Dead`, so it never reaches
the mind state. Naming `InspirationDefOf.Inspired_Creativity` throws a `TypeInitializationException`,
because a `DefOf` class's static constructor cannot run outside a game. So **neither of the two
quality modifiers the mod ships can be evaluated here**, and the tests register their own. What is covered is the machinery that reads modifiers and the arithmetic over
what they return, not the two shipped lambdas.

`ValidateAndFixLoadedData` is reachable as well, by reflection, and that was found by mutation rather
than by reading. It is private and the public route to it is `ExposeData` under
`Scribe.mode == LoadingVars`, which is unreachable, so it looked untestable and a mutation deleting
its clamp passed the suite. The method itself touches only the settings object, the preset table and
`MaterialCostField`, so invoking it directly is honest rather than a workaround. It is worth checking
this way round before writing something off: the barrier was the caller, not the method.

`MaterialCostFieldTests` also reads the mod's own nine shipped `Keyed` files and asserts each tooltip
names the bounds `MaterialCostField` declares. That is the only test here that pins shipped copy to
code, and it exists because this workspace has been burned four times by a description of something
the code does not do. It finds the repository root by walking up from `AppContext.BaseDirectory`, and
it fails rather than passes vacuously if it does not find nine files.

`WorkGiverSurfaceTests` is a different kind of test and the reason it exists is worth repeating. A
performance fix has no functional signature: re-adding the `PotentialWorkThingRequest` override would
restore the thirty-region scan that was this mod's most-reported defect, and every other test here
would still pass. Where the absence of a fix is invisible to behaviour, the declaration is what has
to be asserted.

`JobMemo` is the `WorkerSkill` split again, made for a regression rather than for coverage. The work
giver's `JobFor` reads `Find.TickManager` and cannot run here, and as first written (commit
`01b0bc2`, never released) it held a refusal between `HasJobOnThing` and `JobOnThing`. The float
menu never asks the second after a false first, so the next right-click on the same building in the
same paused tick got the null back with no fail reason written, and the menu showed no line at all.
The rules now live in a class that takes the tick as an argument and runs over plain objects: a
refusal is never held, a refusal for one building leaves every other building's job alone, a job is
handed out once, any change of pawn, `forced` value or tick forgets everything, and the builder is
handed the key's own pawn and `forced` value. The take-or-build step lives there too, as
`JobMemo.Answer`. While it stayed in `JobFor`, two one-word edits there, keeping a null in place of
the job just built and ignoring what `TryTake` handed back, passed the whole suite and brought issue
#5's double build back, which nothing in game can see either. `JobFor` is now one call.
`WorkGiverSurfaceTests` holds it to that call, and the constructor to handing the memo `BuildJob`
and the unreachable-material cache's `Clear`, by asserting both whole call sequences.

The same change split `ImproveSite.CanWorkOn` in two, because the work giver has to ask the
ideoligion before it hauls and the four access checks after. The seventeen-call vanilla list is
unchanged and each half is asserted as a slice of it, so a check moving between the halves fails as
well as one going missing. `CanWorkOn` is kept as the job driver's re-check and is asserted to call
exactly the two halves, access first.

Twenty-nine mutations were measured on 2026-09-18, first against the suite at 270 tests and again at
304 once the other three fixes of that day had landed, across the memo and the split, and
twenty-five are caught both times: `Keep` holding a null (1) or leaving an older job behind when
given one (2); `TryTake` not forgetting what it hands out (3); `Rekey` ignoring the pawn (4), the
`forced` value (5) or the tick (6), forgetting on every call (7), reporting a change without
forgetting (8) or forgetting without reporting it (9); `JobFor` skipping `TryTake` (10), not keeping
(11), keying on a constant tick (12) or never clearing the unreachable-material cache (13);
`BuildJob` losing the ideoligion test (14), asking it after the haul branch again (15), asking
`CanWorkOn` instead of `CanAccess` (16), or moving `CanAccess` above the haul branch (17) or above
the work type test (18); `CanWorkOn` dropping the permission half (19) or asking it first (20); the
permission check folded back into `CanAccess` (21); the `blueprintDef` guard moved below
`FirstBlockingThing` (22); the ideoligion fail-reason loop dropped (23); `IsBurning` dropped (24) or
moved into the permission half (25).

Four survived then, all arguments or control flow, and one of them is caught now. `JobFor` clearing
the unreachable-material cache on every call rather than on a rekey, a performance regression back
to issue #6's multiplier, moved into `JobMemo.Answer` with the rest of the hand-over, where it fails
`TheOwnersStateIsForgottenOnEveryKeyChangeAndOnlyThen`. Mutations 10 to 13 describe the `JobFor`
body as it was then, and each has a caught equivalent now: skipping `TryTake` and not keeping are
RC3 and RC2 in the review list below, never clearing the cache is the empty forget and the never
forgetting `Answer` there, and passing `Answer` a constant tick in place of `TicksGame` fails
`TheJobDecisionGoesThroughTheMemoAndNothingElse`, measured separately. Three still survive.
`JobFor` passing a constant `false` to `Answer` in place of `forced` would hand a right-click a job
the background scan built in the same tick and build every refusal without its reason, which checks
5, 10 and 14 would each show. `BuildJob` discarding or inverting the ideoligion's answer leaves
every call where it was, which is check 15.

For the removal of the dead code and the untranslated group tooltip, eighteen mutations were
measured on 2026-09-18 against the suite at 304 tests, and fifteen are caught: the tooltip going
back to an English suffix after `Translate()` (1), or keeping the new key and appending one anyway
(2); `SimpleImprove_TargetReserved` coming back as code without its key (3), as code with its key
(4), as a key alone (5), or as a key alone with a comment quoting it as a call, which is the
mutation that shows a comment cannot keep an orphan alive (6); either deleted gizmo method put back
(7, 8); both designators and their keys put back (9), and again with an XML comment naming both
classes, which shows a comment is not a registration (10); the IL walker no longer recording `ldstr`
(11); the reachability scan treating every type as compiler-generated (12); the new key missing from
Polish (13) or from all nine (14); and the Japanese sentence losing its `{0}` (15). Three survive.
The string scan skipping nested types and the def scan reading XML comments are equivalent today,
since no key lives only inside a lambda and no XML comment names a def-built class; mutations 6 and
10 show both turn loud once that data exists. The Russian count reverting to vanilla's
`{0} выделено` is wording, which is check 16 and the `localisation` agent. `VanillaCancelTests`
catches all three of its own: the def opting out of Cancel, the def being renamed, and the vanilla
default asserted the other way round, which is the proof its constructor really runs here.

Fixes here are checked by mutation rather than by inspection, and the fixtures say which mutations
they catch. For the work giver those are: re-adding the `ThingRequest`; deleting either override;
dropping the null test in `ScanTargets`; turning `ScanTargets` back into a lazy iterator; inverting,
widening or making `RepairNeeded` two-way; and `ScanTargets` ignoring its input. All nine fail as
test failures rather than as compile errors, which is a distinction worth keeping: an assertion on
`.Count` silently became a LINQ method group under the iterator mutation and had to be rewritten as
`Has.Count` to fail properly.

For the container, three more: reverting `GetDirectlyHeldThings` to the raw field, widening
`ShouldScribeContainer` back to a non-null test, and making `GetChildHolders` allocate.

For the material cost field and the Legendary clamp, twenty-five mutations were measured on
2026-09-18 against the suite at 189 tests and twenty-one are caught: the Legendary clamp reverting to
a literal 5, or going off by one in either direction; either `MaterialCostField` bound moving;
`Typing` rewriting the box, rewriting only in-range text, or clamping instead of deferring an out of
range value; `Unfocused` not clamping, keeping the multiplier that produced its text rather than
reading it back, or losing its NaN guard; `Clamp` losing its NaN guard; `ResetToDefaults` not
rebuilding the buffers, or writing the multiplier into them by hand again; `UpdateUIBuffers`
formatting in multiplier units; `ValidateAndFixLoadedData` not clamping, or not filling a missing
quality row; `SettleMaterialCostField` doing nothing; `SimpleImproveMod` no longer overriding
`WriteSettings`; and `About/About.xml` reverting to the range it used to advertise.

For the best case behind the skill warning, twelve mutations were measured on 2026-09-18 against the
suite at 220 tests and eleven are caught: the carried scan filtering on the wrong kind (1), dropping
its kind filter (2), or restoring the old behaviour of skipping a pawn whose attainable bonus is
nonzero (3); `BestCase` comparing the two totals instead of adding them (4), losing its zero floor
(5), summing every pawn instead of taking the best (6), or ignoring the pawns entirely (7);
`RequirementForBonus` losing its lower clamp (8); and the per-pawn requirement reading `Worth`
instead of `BonusFor` (9), ignoring the modifiers (10), or gaining a `Kind` filter of its own (11).

Four of those needed tests the first draft did not have, and the fourth was the one that mattered.
The lower clamp was never exercised with a pawn at all. Nothing distinguished `Worth` from
`BonusFor`, which are equal in every case a careless test registers. And **nothing pinned that the
per-pawn requirement counts carried bonuses**: two of the three loops over `PawnQualityModifiers`
filter on `Kind` and this one deliberately does not, so the filter could be copied into it and the
whole suite stayed green. Every other test registering a carried modifier was either asking the best
case or landing on a clamp that hid the difference. An adversarial review found that one; the other
three came out of the mutation runs.

Three of those were added because an adversarial review found the first draft of this suite could not
catch them, and the fourth (the reset test) was found by mutation. They are worth naming because each
is a way a test can look right and hold nothing:

- **`Typing` rewriting only in-range text survived.** Every keystroke test used whole percentages,
  and for those the rewritten text is the same text. It is caught now by typing `12.5`, where the
  intermediate `12.` parses, is in range, and would have its decimal point eaten.
- **The keystroke helper fed in prefixes rather than chaining the box.** `TypeAll` built each
  keystroke as `text.Substring(0, i)`, which assumes the very thing the fix provides. It appends to
  the buffer the previous call returned now, so it fails against the code that has the bug.
- **`HighestQualityIndex` off by one upward survived.** The bound only bites when a modifier is
  negative, and nothing passed a pawn at all. `new Pawn()` turns out to be constructible here, so a
  test registers a negative modifier and asserts the lookup does not fall off the end of the table.
- **`ResetToDefaults` doing nothing survived**, because the test reset a fresh instance, which
  already reads `100`. A test that asserts a default it was handed for free asserts nothing.

For the material return and the target quality move, measured on 2026-09-18 against the suite at 136
tests, eighteen more are caught: `OnDeSpawn` ignoring the gravship flag, the map, or whether
anything is staged, or answering unconditionally; `OnUnmark` ignoring the map;
`ShouldMigrateTargetQuality` dropping any one of its three conditions; `TakeTargetQuality` not
removing what it read; `DiscardUnclaimedTargetQualities` doing nothing, not being called from
`FinalizeInit`, or `FinalizeInit` no longer overriding; `SetMarkedForImprovementDirect` leaving the
target behind, or clearing it when marking rather than unmarking; the `TargetQuality` setter
becoming a no-op; and the `PostDeSpawn` override being removed.

Two mutations that were expected to be caught and are not, both because they are behaviour
preserving rather than because of a gap. Making `AttainableBonusTotal` add up every modifier's
`Worth` rather than only the attainable ones passes the suite, and no test can catch it: a carried
modifier is built through `PawnQualityModifier.Carried`, which is the only route to one and always
sets `Worth` to zero, so the two sums are equal by construction. The filter states the intent and
would start mattering the moment a carried modifier could declare a worth. Rewriting `OnUnmark` to
call `OnDeSpawn` with a hardcoded `false` passes the suite for the same reason, and
`TheTwoDecisionsDisagreeAboutAGravship` says so in the fixture rather than implying it catches
something it does not.

An earlier version of this paragraph also listed the second clamp in `GetBestCaseSkillRequirement`
reverting to a literal 5. That entry is gone because the code is: the per-pawn and best-case paths
share one `RequirementForBonus` now, so there is one clamp rather than two, and mutating it **is**
caught. A claim about an uncovered mutation has to be retired with the code it described, or it reads
as a live gap in something that no longer exists.

**Be precise about what that does not cover, because the list above makes it look wider than it is.**
Several method bodies are unreachable, and mutations inside them were measured to pass the suite
entire. Against the suite at 97 tests: dropping the `!` from `WorkGiver_Improve.ShouldSkip`,
replacing `PotentialWorkThingsGlobal`'s body with `ScanTargets(null)`, and deleting the
`PostSpawnSetup` designation repair. Against the suite at 136 tests, six more: emptying
`PostDeSpawn`'s body, emptying `ReturnStoredMaterialsWhileSpawned`'s body, deleting the target
quality migration call, hardcoding `true` for the mark argument it passes, deleting the
`Scribe_Values.Look` for `targetQuality`, and making the designation-cancel prefix stop returning
materials. Against the suite at 189 tests, three more, all inside `DoSettingsWindowContents`, which
needs IMGUI: deleting the `GUI.SetNextControlName` call so the focus test never matches, inverting
the focus test itself, and deleting the `UI.UnfocusCurrentControl()` call that notices a click
landing outside the field.

For the `CanConstruct` replacement, sixteen mutations were measured on 2026-09-18 against the suite
at 228 tests, and an adversarial review then found four more ways these tests could hold nothing,
which is why the suite is 229 and several of the assertions are wider than the mutation runs alone
would have made them. The four are worth naming, because three are shapes that will recur:

- **`Assume.That` deletes a test and reports success.** The three preconditions in
  `ABuildingWithNoBlueprintIsRefusedBeforeVanillaIsAsked` were `Assume`. NUnit turns a failed
  assumption into Inconclusive, the runner drops an inconclusive test from the totals rather than
  failing, and the run still prints `Passed!` with the count one lower. So the fixture's only
  executable test could stop running and say nothing. They are assertions now. **Do not use `Assume`
  for a premise whose failure means the test has stopped testing.**
- **A whitelist assertion is blind in both directions.** The order test used to filter the extracted
  call list down to six interesting names before comparing, so it pinned those six and could not see
  any other call arriving or leaving. It compares the whole sequence of calls into the game assembly
  now, seventeen of them, which is the mod's entire vanilla contact surface for this decision.
- **A positive control has to cover the ground the negative test guards.** The control proving the IL
  scan can see anything at all was anchored in `SimpleImprove.Core`, while the defect and both former
  call sites are in `SimpleImprove.Jobs`, one inside a hoisted lambda. A filter matching a subset of
  the mod rather than nothing would have left the control green. It now asserts both specifically.
- **The check that catches everything else was itself unchecked.** The branch-target check is the
  only thing that catches a misaligned walk that happens to end on the last byte, and deleting it was
  silent across the whole suite. It has its own test now, over hand-built bytes, because no real
  method can be deliberately malformed and `DynamicMethod.GetMethodBody` throws here.

Five more were measured after those fixes, making twenty-one, of which eighteen are caught: the
`blueprintDef` guard deleted (1) or moved below `FirstBlockingThing` (2); either call site calling
`GenConstruct.CanConstruct` again (3, 4); any one of the five checks dropped, `FirstBlockingThing`
(5), `CanTouchTargetFromValidCell` (6), `IsBurning` (7) or the whole Ideology branch (8); the
Ideology fail-reason loop dropped while the decision stays (9); `IsBurning` reordered to the front
(10); the work giver checking skill before site (11); a sixth vanilla check quietly added to
`CanWorkOn`, now either half of `ImproveSite` (18); and five against the IL reader itself,
mishandling the `switch` operand length (12), the mod-method scan matching no types (13), reaching
`SimpleImprove.Core` but not `SimpleImprove.Jobs` (19), skipping compiler-generated nested types and
so the job driver's lambda (20), and the guard test's own premise going stale (21).

Mutations 2 and 12 are worth naming because the first draft of these tests missed both, and each is a
way a test can look right and hold nothing:

- **Moving the `blueprintDef` guard below the vanilla call survived.** The test built its def with
  `FormatterServices.GetUninitializedObject`, which bypasses field initialisers, so `ThingDef.size`
  arrived as `(0, 0)` rather than its declared `IntVec2.One`. A thing that occupies no cells walks
  out of `FirstBlockingThing`'s loop before touching the null `Map`, so vanilla never got the chance
  to throw and the guard's position did not matter. The test sets the size now.
- **Breaking the IL walker's `switch` handling survived the walker's own integrity check.** Ending
  exactly on the last byte looked like proof of alignment and is not, for the jump-table reason
  above. The branch-target check is what catches it, and mutation 12 and "delete the branch-target
  check" are therefore not independent: the second hides the first. That is why the check now has a
  test of its own (17) rather than only being the reason another mutation fails.

  Note also that the mod does compile three jump tables today, `SimpleImproveSettings`
  `.GetPresetDisplayName` and the two `MakeNewToils` iterator state machines, so mutation 12 fails
  five tests rather than one. The work giver's `switch` over `WorkerSkill.FirstBlocker` is not among
  them: too few cases, and the compiler emits a comparison chain. The dense switch in
  `ImproveSiteTests` exists so the branch stays exercised by code this suite owns even if those three
  change shape, because whether a `switch` statement becomes a jump table is the compiler's decision
  and not the source's.

Three survived, all limits of reading IL rather than gaps that can be closed here, and two still do.
Hardcoding `forced` to `false` at the work giver's call site survives, because the call list is the
same call list; so does discarding the answer entirely, because the `if` around a call is not
something IL reading can see. Both are held by in-game checks below instead: discarding the answer
by check 4, where the improving line would be offered while somebody is on the chair, and hardcoding
`forced` by check 10, where the line would vanish rather than read "Reserved by". Both checks passed
on 2026-09-18, but on `d103316`, where that call site was still `CanWorkOn`. This change replaced it
with `CanAccess` and moved the ideoligion above the haul branch, so neither pass covers the code as
it stands, and both are owed again on the build that ships. The third, dropping `ldftn` from the
walker's call-shaped set, survived because no assertion depended on it: the job driver's lambda is
found by walking the generated nested type directly, which is the more robust route anyway. It is
caught now, by four tests, because the work giver's constructor hands the memo `BuildJob` and the
cache's `Clear` as delegates, and a delegate is created by `ldftn` rather than called.

For the stranded-mark fix, twenty mutations were measured on 2026-09-18, first against the suite at
288 tests and again at 304 once the other three fixes of that day had landed, and seventeen are
caught both times: the work giver (1), the job driver (2) or the haul driver (3) reading the bare
flag again; the giver's test moved below the haul branch (4); `ImproveTarget.IsOutstanding`
admitting the target itself (5) or treating any improvement as always outstanding (6);
`IsOutstandingFor` admitting a building with no quality (7); `HasOutstandingImprovement` ignoring
the flag (8); the setter's guard deleted (9) or moved after `AddDesignation` (10); `TryMarkFor`
losing its own check (11) or leaving a target behind when the setter refuses (12); one of the group
menu's two loops writing the target and the flag directly again (13); the stop rule after a success
admitting the target (14), or spelling the same comparison inline instead of asking `ImproveTarget`
(15), which is behaviour preserving and caught on purpose; and the gizmo (16) or the selection
filter (17) comparing against Legendary directly again. Three survive, all inversions of an `if`
that IL reading cannot see: around the property in the work giver, in the job driver, and around the
setter's guard. An inverted setter guard refuses every mark that has work ahead of it, so marking
anything in game shows it at once; the other two are what check 13 and check 6 are for.

Review measured five more in the same territory, all surviving at the time. Two are caught now,
because both call sites ask `ImproveTarget.CanBeOffered`, which takes no target: the gizmo (RA3) and
the selection filter (RA4) asking about Masterwork. Three still survive. The selection filter's test
inverted (RA6) admits only Legendary buildings, so it shows in any in-game check that selects one.
The haul driver's fail condition inverted (RA2) ends every delivery to a building with work ahead of
it, so check 13's delivery, and any improvement run with Require materials on, fails on sight. The setter asking `IsOutstandingFor(null)` in place of
`IsOutstandingFor(targetQuality)` (RA1) is covered by no check, and the harness cannot reach it,
because the setter's marking branch returns on a null map before it judges anything.
`TheSetterJudgesAMarkBeforeItAddsTheDesignation` pins only that the call is there and comes before
`AddDesignation`, so the setter's claim to hold for every caller rests on an argument nothing pins.
It matters only to a third party writing the public property, since `TryMarkFor` asks first with
the real target. Such a caller can also re-aim a building that is already marked through the public
`TargetQuality` setter, which checks nothing at all; the work giver then refuses a mark whose new
target is at or behind the building, so the work is lost but the materials are not.

The review fixes of 2026-09-19 were checked by thirty-three mutations against the suite at 317
tests, and twenty-seven are caught. In the memo: `Keep` clearing every building on one refusal
(RC1); `Answer` keeping a null in place of the job it built (RC2), ignoring what `TryTake` handed
back (RC3), forgetting on every call, never forgetting, forgetting after the build instead of
before it, or building with a constant `forced`; `JobFor` bypassing the memo; and the constructor
binding an empty forget in place of the cache clear. In the offer: the gizmo or the selection filter
asking about a constant quality or about Masterwork (RA3 and RA4, two each), and `CanBeOffered`
itself aiming at Masterwork. In the button: the count starting at three (RB1), the label or the
tooltip spelling its own count rule, the group key translated with no argument (RB2), the label's
suffix going back into code with or without the key, the German tooltip left in English (RB3), the
label key missing from Polish, and the deleted label method put back as `internal` (RB4), which the
private-only reachability scan used to let through. In the site: the ideoligion's null guard
inverted (RC4b) or deleted (RC4), and the ideoligion moved to the top of `BuildJob` (RC5) or above
the designation tests. Six survive. Four are named above: `JobFor` passing a constant `false`, the
selection filter inverted (RA6), the setter judging any improvement (RA1) and the haul driver
inverted (RA2). The other two are new. Passing the tooltip one less than the group's size (RB5) is
arithmetic, which IL reading does not see, and check 16 reads the number. Showing the ideoligion
reason with an empty list of names (RC7) sits in the refusal branch, which reads
`Find.IdeoManager`; it needs a building no ideoligion in the world may build, and no check covers it.

The numbered checks below are the ones this suite cannot make. Checks 1 to 12 were run in game on
2026-09-18 against the 1.1.0 build from `main` at `d103316` (RimWorld 1.6.4871, English), and all
twelve passed. Where a reading is worth keeping it is recorded with its check.

**Those passes do not all carry over to the build that ships.** The stranded-mark fix, the memo fix,
the ideoligion move and the review fixes all landed after that session and are not in the build it
read. Decompiling that installed DLL confirms it: it has no `HasOutstandingImprovement`, and its
work giver still calls `CanWorkOn`. Checks 1, 2, 3, 7, 11 and 12 exercise the settings window, whose
code none of that touched, so their passes stand. Checks 4, 5, 6 and 10 exercise the work giver and
the job driver, which it rewrote, and checks 8 and 9 exercise the gizmo's gate, which it rewrote in
an equivalent form. All six are owed again on the build that ships, together with checks 13 to 16,
which have never been run.

**Re-run on 2026-09-19 against the build that ships**, `7b8640b` installed by `build.sh`
(RimWorld 1.6.4871, dev mode, English unless stated). Checks 4, 5 and 10 passed again. Check 13
passed in all three halves: a stranded stool left an hour at speed 3 was never hauled to or worked,
a chair raised to Legendary mid-work stopped its worker with no "Improvement failed!" and kept its
wood until "Cancel improvement" dropped it, and a stool marked for Good and raised to Excellent sat
idle. Check 14 passed on three paused right-clicks of the cloth armchair. Check 16 passed in English
and Russian. The dev-mode startup warning about `ImproveSelection` was gone. Check 15 was not run:
it needs a non-classic ideoligion whose ritual seat is a pew or a kneel seat. Checks 6, 8 and 9
were not re-run as numbered checks; check 13's driver half exercised check 8's "Cancel improvement"
on a stranded building, and nothing on this build re-read check 9's merged button.

Checks 4, 5, 6, 8, 9 and 10 were also rewritten on 2026-09-18, because working out an exact
procedure for each showed that every one of the six, as first written, either predicted something
that cannot happen or could have passed for the wrong reason. Two of the recorded readings do not
follow the rewritten procedure. Check 4 was read with a colonist seated to eat rather than the
drafted colonist the procedure asks for, so its background half was never observed. Check 6 was read
on completion only, without the interruption.

**Inverting the focus test is the most important in-game check this suite cannot do.** It normalises
the box while the player is typing and leaves the raw text alone once they are out of it, which is
the reported defect back with the fix apparently in place, and every test of `Typing` and `Unfocused`
still passes. Three checks settle the whole control:

1. Click into the material cost field and type `100` one digit at a time. The box must read `1`,
   then `10`, then `100`, never rewriting itself.
2. Type `5` and click the Require-materials checkbox. The box must snap to `10`. This is the
   `UI.UnfocusCurrentControl()` call: Unity assigns keyboard focus on a mouse down inside a text
   field and never clears it on one outside, `GUI.DoControl` behind every button and checkbox does
   not touch it, and `Dialog_ModSettings` makes no focus call of its own, so without that call the
   only way out of this box is to click one of the skill boxes.
3. Type `1000`, close the window with the Close button, and reopen it. The box must read `500` and
   the setting must be 500%. That is `SimpleImproveMod.WriteSettings`, which is the commit point for
   every ordinary way out of the window, none of which moves keyboard focus.

**The `CanConstruct` replacement owes three in-game checks of its own**, because reading IL proves
the five calls are there and proves nothing about what is done with the answer. Two mutations
survived on exactly that.

4. With Require materials off, a marked dining chair must not be offered for improvement while
   somebody is on its tile. That is `FirstBlockingThing`, it was confirmed as wanted in game on
   2026-09-17, and it is the check a mutation discarding `ImproveSite.CanAccess`'s answer at the
   work giver's call site would fail. Two traps decide whether the reading means anything. While
   materials are owed, the right-click is the haul job, which the work giver offers before any
   access check runs, so with materials on it says nothing about who is on the chair. And a colonist
   seated to eat reserves the chair, so the background scan refuses at the reservation test and
   never reaches `FirstBlockingThing`; the right-click still does, but the background half of the
   reading is lost. Mark a wooden dining chair for any improvement, draft another colonist and
   right-click the chair's tile (a drafted right-click there walks straight onto it, with no menu),
   pause, and right-click the chair with an undrafted worker selected alone. There must be no line
   containing "improving" at all, absent rather than greyed, because `FirstBlockingThing` sets no
   reason, and once unpaused the worker must never take the job on their own. Move the drafted
   colonist off, let a tick pass, and "Prioritize improving wooden dining chair (normal)" must come
   back, or the greyed "Already improving wooden dining chair (normal)" if the worker took it in
   that tick, which is also a pass. Under the mutation the line is enabled while the tile is
   occupied, and taking it sends the worker over to turn away with no sparks and no bar; the next
   job search hands the same job out again, and the log shows a red "started 10 jobs in one tick".

   Passed 2026-09-18 on `d103316`, before the work giver was rewritten, so owed again. Read with a
   colonist seated to eat. With materials on the right-click read
   "Prioritize improving wooden dining chair (normal): Reserved by <the eater>", which is the haul
   job and proves nothing about occupancy. With materials off there was no improving line, which
   is the reading that counts, and empty chairs offered it.
5. Right-click a marked building a colonist cannot reach, and confirm the menu says what 1.0.8 said.
   There is no generic reason to look for. With Require materials on and the materials not yet
   delivered, the work giver answers with the haul job before any access check runs, and vanilla
   greys it as "Cannot improve wooden stool (normal): No path", or "...: Missing 25x wood" when no
   reachable, unforbidden wood exists. With materials off or delivered, the access checks refuse
   with no reason, so there is no improving line at all and often no menu; a roofing line on a
   freshly walled cell is unrelated. The practical setup is a wooden stool built in god mode and
   walled in on all eight sides before it is marked. The Ideology reason this check used to carry
   has moved to check 15, because the gate that produces it now runs before the haul job.

   Passed 2026-09-18 on `d103316`, before the work giver was rewritten, so owed again. With
   materials on, "Cannot improve cloth armchair (normal): Missing 88x cloth"
   and "Cannot improve wooden stool (normal): No path"; with materials off, no improving line.
6. Improve something to completion, and confirm the job driver's per-tick re-check is live. The
   driver asks `ImproveSite.CanWorkOn` on every tick through a `FailOn`, passing `forced: false`
   where the work giver passes the real value. This check used to single out a forced job, on the
   grounds that the two might disagree on its first tick. They cannot. `NormalMaxDanger` already
   returns `Deadly` while the pawn's current job is player-forced, and the worker reserves the
   building before any toil runs, which `CanReserve` accepts ahead of every test of other pawns'
   reservations. Vanilla's `JobDriver_ConstructFinishFrame` has the same shape. With Require
   materials off and a worker at Construction 10 (dev "Set Skill..."; from 8 there are no botches),
   set a wooden stool to Awful with dev "T: Set Quality...", mark it for any improvement and
   right-click it with the worker: sparks, the repair sound and a filling bar, then "Improved to
   <quality>!" and the mark gone. "Improvement failed! (awful)" is a bad roll rather than a failed
   check; the mark stays and it goes round again. Then mark a wooden dining chair, let the worker
   start on it, and draft another colonist onto its tile. The worker must stop at once, the chair
   must keep its part-done "Work left", and the worker must resume from there once the tile is
   clear. With dev "T: Toggle Job Logging" on the worker, the stop logs an `endConditions` line and
   then "condition=Incompletable". A first-tick failure is zero ticks of work: the worker arrives,
   shows no sparks and no bar, and turns to something else.

   Passed 2026-09-18 on `d103316`, before the job driver was rewritten, so owed again, and on
   completion only: a prioritised improve of a wooden end table, with materials on,
   hauled the wood and took the table from Normal to Good. With materials owed, the right-click
   order is the haul, so that run does not show whether the improve job itself was player-forced,
   which no longer matters. The recorded reading does not include the interruption.

**The 2026-09-18 issue sweep added six more, each a change reasoned from the source rather than
observed.** Nothing in this tree runs RimWorld, so these were the parts of that work that were still
a claim until the in-game session later the same day.

7. Open the settings window and look at it. The two columns used to be drawn 40px lower than the
   code intended, because `Listing.Begin` opens a GUI group and the layout added `inRect.y` on top
   of a coordinate that was already relative. The gap is a named constant of the same value now, so
   the window should be pixel-identical to what shipped. If anything has moved, the constant and
   the magic `80f` below it need rationalising together, which is the one part of #21 that was
   deliberately left for somebody who can see the result.
8. Mark a Normal building for any improvement, keep the game paused, and raise it to Legendary with
   dev "T: Set Quality..." then "Legendary" (open the debug actions menu and type quality). That is
   the practical way into this state. Vanilla never raises an existing building's quality and the
   mod's own loop clears the mark when it reaches Legendary, so the only other route is another
   mod. The Improve menu used to be a third: its options capture the selection when it opens and
   the game keeps running, so "Any improvement" could re-mark a building a colonist had just
   finished at Legendary. Marking refuses such a building now. Confirm that a button labelled
   exactly "Cancel improvement" appears, that its tooltip reads "This building is marked for
   improvement but can no longer be improved. Cancel the mark and return any materials that were
   delivered.", and that one mouse click on it clears the mark. Vanilla's "Cancel" (red X, "C" in
   the corner) is on every marked building, stranded or not, and was before this button existed,
   so seeing it proves nothing, and clicking it or pressing C clears the mark too. The pass
   condition is the mod's own button. Its C hotkey never fires, because vanilla's Cancel is drawn
   first and a hotkey binds only to the first button drawn with it.

   Passed 2026-09-18 on `d103316`, before the gizmo's gate was rewritten, so owed again: one
   "Cancel improvement" beside vanilla's "Cancel".
9. Select several stranded buildings at once (three stools, shift-clicked; a drag box also picks up
   trees and rocks) and confirm the bar holds exactly one "Cancel improvement", with no count, next
   to exactly one vanilla "Cancel", and that one mouse click on "Cancel improvement" clears every
   mark. Not "Cancel" and not the C key, which are vanilla's and clear them too. This is vanilla's
   `Command.GroupsWith` merging the buttons, which needs the label, the icon reference, the hotkey
   and the group key to match. It cannot show that the icon is a shared static, since fetching the
   texture per button returns the same object anyway. What it really guards is the label: any
   per-building text in it would split the merge into one button per building.

   Passed 2026-09-18 on `d103316`, before the gizmo's gate was rewritten, so owed again: three
   stranded stools showed one "Cancel improvement", and one click cleared
   all three.
10. Right-click a marked building another colonist is already improving, and confirm the option is
    offered enabled, reading "Prioritize improving wooden table (1x2) (normal): Reserved by <name>",
    and that taking it stops the other colonist and keeps the progress. An earlier version of this
    check expected a greyed reason naming the reservation, and that cannot happen. The right-click
    is the only caller that passes `forced: true`, and a forced `CanReserve` ignores other pawns'
    reservations altogether, so vanilla offers the job and appends ": Reserved by <name>" itself.
    The history it gave was wrong as well: the building's reservation test has honoured `forced`
    since at least 1.0.7, so the menu never mislabelled it, and the only reservation that ever
    produced a "Missing" reason on the menu was a material stack's, fixed in `77b2a01`. Use a
    table, which nobody can stop on, with Require materials off or the materials already in. While
    paused, order worker A to improve it, then right-click it once with worker B, still paused, and
    read the line. Unpause, pause again once A's bar has some fill, and take B's option: A's job
    must end at once, B must carry on from the same fill, and A must not come back to it. With the
    materials in or off, this is also the check that catches the work giver passing
    `forced: false` to `ImproveSite.CanAccess`: the line would be absent rather than read
    "Reserved by".

    Passed 2026-09-18 on `d103316`, before the work giver was rewritten, so owed again:
    "Prioritize improving ...: Reserved by <A>", and taking it handed the job
    over and kept the progress.
11. With a hand-edited Custom skill table, press a preset button and confirm the confirmation dialog
    appears and defaults to cancel, and that cancelling really does leave the table alone.
12. Type a percentage starting 0 to 4 into the material cost box, and confirm it is still typable.
    Nothing in this sweep touched `MaterialCostField`, but the settings window's layout and its
    focus handling did move, and that field's whole defect class was focus-dependent.

**Four more cover fixes made after that session, and none has been run yet.** Each came out of
working out the exact procedures for checks 4 to 10.

13. A building that is marked but can no longer be improved must be left alone. Strand a stool as
    in check 8, with Require materials on, wood available and nothing delivered to it, and do not
    cancel it. Unpause at speed 3 for about an in-game hour with a colonist free who has Improve
    ticked. Nobody may haul to it or work on it: no sparks, no bar, no "Improvement failed!" mote,
    and its wood line under "Contained resources" must stay at 0 / 25. Right-clicked by that
    colonist alone, it must not offer an enabled "Prioritize improving wooden stool (legendary)".
    Then the job driver's half. Mark a wooden dining chair, let the colonist deliver its wood and
    start work, pause while the bar is moving, raise the chair to Legendary with "T: Set
    Quality...", and unpause. The colonist must stop at once with no "Improvement failed!" mote,
    the delivered wood must still be listed, and clicking "Cancel improvement" must drop it beside
    the chair. Then the target-passed half: mark a Normal stool for Good, raise it to Excellent the
    same way, and confirm nobody works it and that its button still reads "Improve (good)", with
    "Cancel improvement" first in its menu. Before this fix the colony hauled the full cost in,
    rolled, showed "Improvement failed! (<quality>)", destroyed the materials and started again, for
    as long as the mark stood.
14. A greyed reason must survive a repeated right-click while paused. With Require materials on,
    mark a building whose material the colony has too little of (the cloth armchair from check 5
    will do) and pause. Right-click it with one colonist who has Improve ticked and read "Cannot
    improve cloth armchair (normal): Missing <n>x cloth". Close the menu and right-click again, and
    a third time, without unpausing. Every menu must carry the same greyed line. The work giver
    remembers its answers for the current tick and the tick does not move while paused, so before
    this fix a second right-click could reuse a remembered empty answer with no reason attached,
    and open with no improving line or no menu at all. Unpausing for a moment before each
    right-click was the workaround and is no longer needed.
15. A colonist whose ideoligion forbids a building must not be sent to haul for it, and must be told
    why on a right-click. This needs Ideology and a non-classic colony ideoligion whose ritual seat
    is a pew, kneel sheet or kneel pillow; those three, the slab bed and the slab double bed are the
    only improvable buildings an ideoligion can forbid. With Require materials on, build the
    colony's ritual seat in god mode on reachable open ground, mark it for any improvement, and make
    sure its material is available. Move colonist B to another world ideoligion with dev "Set Ideo",
    keep B undrafted with Improve ticked, and untick Improve for everyone else. Right-clicked by B
    alone, the seat must show a greyed "Cannot improve <seat>: Only <names>s can build", naming
    every ideoligion whose members could build it, the colony's own among them, and not an enabled
    "Prioritize improving <seat>", which was the haul order. Unpaused for an in-game hour, B must
    haul nothing to it. As a control, tick Improve for a member of the colony's ideoligion, who must
    haul to it and work it normally. If B's line is still enabled, the ideoligion picked can build
    the seat, through the same seat precept or an ideoligion diversity precept of "appreciated" or
    above; pick another.
16. The Improve button must count a multi-building selection in the game's language. Select three
    unmarked stools: the button must read "Improve (3)", and hovering it must read "Mark these items
    for quality improvement (3 affected)", with no raw key or `{0}` showing. Select two and the
    button must read "Improve (2)" and the tooltip "(2 affected)": two is where the count starts,
    and a rule that started it at three would pass both the other readings. Select one and the
    button must read "Improve" with no count and the tooltip the single-building sentence. If you
    can spare the reload, switch to Russian and select the three again: the button must read
    "Улучшить (3)" and the tooltip "Отметить эти предметы для улучшения качества (выделено: 3)",
    with no English word anywhere, where it used to end in "(3 items)" in all nine languages. In
    Chinese the button must read "改良（3）", the count in fullwidth parentheses with no space
    before them, where it used to read "改良 (3)".

Two things these checks cannot be replaced by, and it is worth saying which. A quiet log is not
evidence for #5: the desync it guards is reported by `Log.ErrorOnce` on a key shared with every work
giver in the game, so another mod may have consumed it before this one ever ran. And a frame that
looks right is not evidence for #19's cache: the failure is one frame long and needs the selection
changed mid-frame, which means dragging a new selection over an existing one and watching the
button's count rather than glancing at it.

The reflection tests hold the *declarations*, not the bodies, and no test can **run** the bodies
either: `Map` is unconstructible outside a running game, `DesignationManager` needs one, `Scribe` is
static game state, and Harmony cannot patch on this runtime at all.

An earlier version of this paragraph went on to say that nothing could give any assurance those
bodies still call the decisions they delegate to, and that only an in-game check closed it. That is
no longer true and the sentence has been retired with the gap it described. A body that cannot be
run can still be **read**: `ILCalls` walks the compiled IL and reports which methods a method calls
and in what order, which is enough to hold "the work giver still asks `WorkerSkill`" and "nothing
hands a completed building to `GenConstruct.CanConstruct`". It was added for issue #7, where the
whole defect was which vanilla method the mod was calling. Be exact about what it establishes,
because it is easy to read as more:

- It pins **which methods appear in a body and in what textual order**, and the assertion built on it
  decides how much of that is actually held. Asserting the whole sequence catches a call being
  deleted or added; filtering to a whitelist of interesting names first, which the order test used to
  do, catches only those names moving or going missing. Reordering two calls is caught either way. A
  call moving into a lambda is caught, if you ask the assembly rather than the method.
- It does **not** pin arguments. Changing `checkSkills: false` to `true` at a call site is invisible
  to it, which matters because that is the shape of the chair-bug fix.
- It does **not** pin control flow. Inverting the `if` around a call, or discarding the result,
  changes nothing it can see. Both were measured as surviving mutations.
- IL order is not execution order in a method with branches, so it can say "A appears before B",
  never "A runs before B".
- It also lists every string literal a body loads, as `WalkResult.Strings`. That is how
  `LanguageParityTests` tells a key the code uses from a comment that quotes one, which a search of
  the source cannot, and how the group tooltip and the work giver's fail reasons are pinned in full.
- It reads the **test** build, not the shipped assembly. The sources are compiled into
  `SimpleImprove.Tests.dll`, so tokens and offsets differ from `SimpleImprove.dll`. Measured on
  2026-09-18 across three methods: Debug and Release disagree about length, locals, `nop` count and
  every offset, and their extracted call sequences are identical. That holds only while nothing in
  `1.6/` sits inside a `#if`, and nothing does.

Two things make it trustworthy rather than merely clever, and both were put there after a mutation
showed the first draft was not. The walk throws rather than returning a short list, because a short
list reads as "this method does not call that" and passes. And it checks that every branch target
lands on an offset it saw an instruction begin at, because ending on the last byte is not enough:
a jump table is a run of small numbers, small numbers decode as short operand-free instructions, and
a walk that reads one as code can drift back into alignment and finish cleanly. Mutating the `switch`
operand length survived the end-of-body check and is caught by the branch-target check.

Quote coverage against the types listed under Covered today, never the repo: most of this mod needs
a spawned `Thing` on a `Map` and a whole-repo figure would be misleading.

The background is `docs/spikes/test-harness/README.md` in the workspace, which records what each
experiment established, including the two whose failure is the finding.
