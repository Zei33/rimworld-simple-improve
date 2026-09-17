# Tests

```sh
export FrameworkPathOverride=/opt/homebrew/opt/mono/lib/mono/4.7.2-api
dotnet test Tests/SimpleImprove.Tests.csproj
```

net472 NUnit, running against the real `Assembly-CSharp.dll` from the installed game rather than a
stub. `RimWorldDir` must be set; the workspace `.claude/settings.json` and `~/.zshrc` both export it.

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
  a `Map` and in most cases `Find.Selector`. None of that is reachable.

Covered today: `ImprovableDefs` (the decision the save/load fix rests on),
`SimpleImproveSettings` (skill table, presets, modifier registration),
`SimpleImproveMapComponent` (the target quality store), and the container allocation behaviour on
`SimpleImproveComp`. That last one is small and matters more than its size: `GetDirectlyHeldThings`
must report null until something is hauled, because declaring the component on the defs puts every
quality building into `ThingRequestGroup.ThingHolder` and vanilla traversals call it on all of them.

Quote coverage against those four types, never the repo: most of this mod needs a spawned `Thing` on
a `Map` and a whole-repo figure would be misleading.

The background is `docs/spikes/test-harness/README.md` in the workspace, which records what each
experiment established, including the two whose failure is the finding.
