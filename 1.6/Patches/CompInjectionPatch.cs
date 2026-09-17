using HarmonyLib;
using RimWorld;
using Verse;
using SimpleImprove.Core;

namespace SimpleImprove.Patches
{
    /// <summary>
    /// Declares <see cref="SimpleImproveComp"/> on the improvable defs, once per play-data load.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>RimWorld.DefGenerator.GenerateImpliedDefs_PostResolve</c> is the hook because of when it
    /// runs and how often. Inside <c>Verse.PlayDataLoader.DoPlayLoad</c> it comes after
    /// <c>GenerateImpliedDefs_PreResolve</c>, which is what populates <c>ThingDef.blueprintDef</c>,
    /// and after <c>DefDatabase&lt;ThingDef&gt;.ResolveAllReferences</c>, so every def is in its
    /// final shape. It also runs before <c>ErrorCheckAllDefs</c>, so the declared component is
    /// included in the config error pass rather than skipping it.
    /// </para>
    /// <para>
    /// A <c>[StaticConstructorOnStartup]</c> class will not do, and this is the whole reason the
    /// patch exists. <c>StaticConstructorOnStartupUtility.CallAll</c> runs every type through
    /// <c>RuntimeHelpers.RunClassConstructor</c>, which does nothing for a type whose static
    /// constructor has already run. Changing language calls
    /// <c>LanguageDatabase.SelectLanguage</c>, whose body is
    /// <c>PlayDataLoader.ClearAllPlayData()</c> followed by <c>PlayDataLoader.LoadAllPlayData()</c>,
    /// and that rebuilds every <c>ThingDef</c> from XML. The static constructor would not run a
    /// second time, so the freshly built defs would carry no improvement component and the mod
    /// would silently do nothing until the game was restarted. Dev mode's "reload defs" has the same
    /// shape: it shallow-copies newly parsed defs over the existing ones, which replaces the comps
    /// list wholesale. Both call this method, so both are covered.
    /// </para>
    /// <para>
    /// The patch is applied by <c>PatchAll()</c> from the <c>Mod</c> constructor, which runs during
    /// <c>LoadedModManager.LoadAllActiveMods</c>, well before the first call to the patched method.
    /// The constructor itself is only ever run once, because <c>CreateModClasses</c> skips a type
    /// already in <c>runningModClasses</c>, but Harmony patches the method rather than the mod, so
    /// the postfix keeps firing across later reloads.
    /// </para>
    /// </remarks>
    [HarmonyPatch(typeof(DefGenerator), nameof(DefGenerator.GenerateImpliedDefs_PostResolve), new[] { typeof(bool) })]
    public static class CompInjectionPatch
    {
        /// <summary>
        /// Declares the improvement component on every def that qualifies.
        /// </summary>
        public static void Postfix()
        {
            var declared = ImprovableDefs.DeclareCompOn(DefDatabase<ThingDef>.AllDefsListForReading);
            Log.Message($"[SimpleImprove] Declared the improvement component on {declared} building defs.");
        }
    }

    /// <summary>
    /// A second pass over the defs, after every mod's static constructor has run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Vanilla declares quality in XML, so <see cref="CompInjectionPatch"/> sees all 43 of the
    /// game's quality buildings. A mod that instead adds a quality component from its own
    /// <c>[StaticConstructorOnStartup]</c> class is invisible to it, because
    /// <c>StaticConstructorOnStartupUtility.CallAll</c> runs at <c>PlayDataLoader.DoPlayLoad</c>
    /// line 346, well after <c>GenerateImpliedDefs_PostResolve</c> at line 267. Those buildings
    /// would silently never be improvable.
    /// </para>
    /// <para>
    /// Patching <c>CallAll</c> rather than adding another <c>[StaticConstructorOnStartup]</c> class
    /// is deliberate: a postfix is guaranteed to run after every static constructor, whereas being
    /// one of them would put this in a race. <c>GenTypes.AllTypesWithAttribute</c> is built with
    /// PLINQ and is not ordered, so the order static constructors run in is not load order and
    /// cannot be influenced by <c>loadAfter</c>.
    /// </para>
    /// <para>
    /// Both hooks are needed and neither is redundant. <c>CallAll</c> is not reached by dev mode's
    /// reload-defs path, which <see cref="CompInjectionPatch"/> covers, and
    /// <c>GenerateImpliedDefs_PostResolve</c> runs too early to see C# additions, which this
    /// covers. <see cref="ImprovableDefs.DeclareCompOn"/> is idempotent, so running twice on the
    /// same defs adds nothing the second time.
    /// </para>
    /// </remarks>
    [HarmonyPatch(typeof(StaticConstructorOnStartupUtility), nameof(StaticConstructorOnStartupUtility.CallAll))]
    public static class LateCompInjectionPatch
    {
        /// <summary>
        /// Picks up any def that gained a quality component from another mod's startup code.
        /// </summary>
        public static void Postfix()
        {
            var declared = ImprovableDefs.DeclareCompOn(DefDatabase<ThingDef>.AllDefsListForReading);
            if (declared > 0)
            {
                Log.Message($"[SimpleImprove] Declared the improvement component on {declared} further building defs, added by another mod at startup.");
            }
        }
    }
}
