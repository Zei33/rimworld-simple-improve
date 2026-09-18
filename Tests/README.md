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
back), `MaterialCostField` (the material cost range and the two decisions that drive its text
field), `QualityBonuses` (the best case behind the skill warning), `ImproveSite` (the replacement for
`GenConstruct.CanConstruct`, whose one runnable guard is covered and whose five vanilla calls are
pinned by reading the compiled IL), the work giver's declared surface, the shipped `PatchOperation`
XML, and the target quality field and container allocation behaviour on `SimpleImproveComp`.

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
`CanWorkOn` (18); and five against the IL reader itself, mishandling the `switch` operand length
(12), the mod-method scan matching no types (13), reaching `SimpleImprove.Core` but not
`SimpleImprove.Jobs` (19), skipping compiler-generated nested types and so the job driver's lambda
(20), and the guard test's own premise going stale (21).

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

Three survive, and all three are limits of reading IL rather than gaps that can be closed here.
Hardcoding `forced` to `false` at the work giver's call site survives, because the call list is the
same call list; so does discarding the answer entirely, because the `if` around a call is not
something IL reading can see. Both join the in-game checks below. The third, dropping `ldftn` from
the walker's call-shaped set, survives because no assertion depends on it: the job driver's lambda is
found by walking the generated nested type directly, which is the more robust route anyway.

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

4. Sit a pawn on a marked dining chair and confirm it still cannot be improved while they sit there.
   That is `FirstBlockingThing`, it was confirmed as wanted in game on 2026-09-17, and it is the
   check a mutation that discards `CanWorkOn`'s result would silently remove.
5. Right-click a marked building with a pawn who cannot reach it, and confirm the float menu says
   what it said before. The three access checks set no fail reason, so the correct result is the
   generic one, and an Ideology colony that forbids the building should still name the ideoligions
   that would allow it.
6. Improve something to completion. The job driver re-checks on every tick through the same method,
   and it passes `forced: false` where the work giver passes the real value, so a forced job is the
   one worth watching: it must not fail on the first tick of work the giver just handed out.

**The 2026-09-18 issue sweep added six more, and every one of them is a change that was reasoned
from the source rather than observed.** Nothing in this tree runs RimWorld, so these are the parts
of that work that are still a claim.

7. Open the settings window and look at it. The two columns used to be drawn 40px lower than the
   code intended, because `Listing.Begin` opens a GUI group and the layout added `inRect.y` on top
   of a coordinate that was already relative. The gap is a named constant of the same value now, so
   the window should be pixel-identical to what shipped. If anything has moved, the constant and
   the magic `80f` below it need rationalising together, which is the one part of #21 that was
   deliberately left for somebody who can see the result.
8. Mark a building, then raise its quality to Legendary by some other means while the mark is live,
   and confirm a cancel button appears on it. That is the only genuinely reachable state of the four
   #23 described; the other three need dev tools or cannot occur at all.
9. Select several such stranded buildings at once and confirm they show ONE cancel button that
   clears all of them. That is vanilla's own `Command.GroupsWith` merging them, which only happens
   while the label, the icon reference, the hotkey and the group key all match, so it is the check
   that the shared static icon is really shared.
10. Right-click a marked building that another pawn has already reserved, and confirm the greyed
    reason names the reservation rather than missing materials. That message was wrong for as long
    as the reservation test sat inside the material loop.
11. With a hand-edited Custom skill table, press a preset button and confirm the confirmation dialog
    appears and defaults to cancel, and that cancelling really does leave the table alone.
12. Type a percentage starting 0 to 4 into the material cost box, and confirm it is still typable.
    Nothing in this sweep touched `MaterialCostField`, but the settings window's layout and its
    focus handling did move, and that field's whole defect class was focus-dependent.

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

Quote coverage against those nine types, never the repo: most of this mod needs a spawned `Thing` on
a `Map` and a whole-repo figure would be misleading.

The background is `docs/spikes/test-harness/README.md` in the workspace, which records what each
experiment established, including the two whose failure is the finding.
