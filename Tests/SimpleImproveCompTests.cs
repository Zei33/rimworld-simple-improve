using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RimWorld;
using SimpleImprove.Core;
using Verse;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Covers the parts of <see cref="SimpleImproveComp"/> that do not need a spawned parent.
    /// </summary>
    /// <remarks>
    /// Almost all of this component needs a <c>Thing</c> on a <c>Map</c>, so the reachable surface
    /// is small. What is here matters out of proportion to its size: it is the container's
    /// allocation behaviour, which the holder-tree change made load bearing.
    /// </remarks>
    [TestFixture]
    public class SimpleImproveCompTests
    {
        [Test]
        public void GetDirectlyHeldThingsNeverReportsNull()
        {
            // This assertion was the other way round until 2026-09-18, and the null it demanded threw
            // on ordinary mouse-over.
            //
            // Declaring the component on the defs puts every quality building into
            // ThingRequestGroup.ThingHolder, and almost every vanilla traversal that reaches a child
            // holder null-checks the result. Exactly one does not:
            // ContainingSelectionUtility.SelectableContainedThings walks ThingWithComps.AllComps and
            // does foreach (Thing t in (IEnumerable<Thing>)holder.GetDirectlyHeldThings()) with no
            // guard. A null cast to IEnumerable<Thing> is still null, so the foreach throws. It is
            // reached from GenUI.ThingsUnderMouse and Selector.SelectableObjectsUnderMouse, i.e. from
            // hovering, selecting or right-clicking any improvable building with nothing staged in it.
            //
            // The cost that motivated the null is real and is handled in PostExposeData instead,
            // which scribes the container only when it holds something.
            var comp = new SimpleImproveComp();

            Assert.That(comp.GetDirectlyHeldThings(), Is.Not.Null);
        }

        [Test]
        public void GetDirectlyHeldThingsIsTheSameContainerTheModHaulsInto()
        {
            // If these ever diverged, materials would be staged in one container and read from
            // another, and the mod would report an empty building it had just filled.
            var comp = new SimpleImproveComp();

            Assert.That(comp.GetDirectlyHeldThings(), Is.SameAs(comp.GetMaterialContainer()));
        }

        [Test]
        public void AnEmptyContainerIsNotWrittenIntoTheSave()
        {
            // The half of the crash fix that has no other observable. Now that
            // GetDirectlyHeldThings allocates on demand, every quality building a traversal walks
            // past ends up with an empty container, so a scribe guard testing for non-null would put
            // an empty node into every save for every quality building on the map. That is silent,
            // cumulative, and exactly the cost the old null return existed to avoid.
            var comp = new SimpleImproveComp();

            Assert.That(SimpleImproveComp.ShouldScribeContainer(comp.GetMaterialContainer()), Is.False);
        }

        [Test]
        public void AnAbsentContainerIsNotWrittenIntoTheSave()
        {
            Assert.That(SimpleImproveComp.ShouldScribeContainer(null), Is.False);
        }

        [Test]
        public void GetMaterialContainerCreatesTheContainerOnDemand()
        {
            var comp = new SimpleImproveComp();

            var container = comp.GetMaterialContainer();

            Assert.That(container, Is.Not.Null);
            Assert.That(container, Is.InstanceOf<ThingOwner>());
        }

        [Test]
        public void GetMaterialContainerReturnsTheSameContainerEveryTime()
        {
            var comp = new SimpleImproveComp();

            Assert.That(comp.GetMaterialContainer(), Is.SameAs(comp.GetMaterialContainer()));
        }

        [Test]
        public void TheContainerIsOwnedByTheComponentSoTheHolderTreeCanResolveALocation()
        {
            // Built with the component as owner rather than null, which is what lets
            // ThingOwnerUtility.GetRootMap and GetRootPosition walk up to the parent thing. Those
            // two have an explicit branch for a holder that is a ThingComp.
            var comp = new SimpleImproveComp();

            Assert.That(comp.GetMaterialContainer().Owner, Is.SameAs(comp));
        }

        [Test]
        public void GetChildHoldersDoesNotAllocateAContainer()
        {
            // Still worth pinning now that GetDirectlyHeldThings does allocate. GetChildHolders is
            // called by ThingOwnerUtility.AppendThingHoldersFromThings while walking the holder tree,
            // it has no unguarded caller forcing its hand, and it reports nothing for an empty
            // container anyway, so there is no reason for it to build one.
            //
            // Read through the field rather than through GetDirectlyHeldThings, which would create
            // the very container this is checking for the absence of.
            var comp = new SimpleImproveComp();
            var children = new System.Collections.Generic.List<IThingHolder>();

            comp.GetChildHolders(children);

            Assert.That(MaterialContainerField(comp), Is.Null);
            Assert.That(children, Is.Empty);
        }

        private static object MaterialContainerField(SimpleImproveComp comp)
        {
            return typeof(SimpleImproveComp)
                .GetField("materialContainer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(comp);
        }

        [Test]
        public void ANewComponentIsNotMarkedAndHasNoWork()
        {
            var comp = new SimpleImproveComp();

            Assert.That(comp.IsMarkedForImprovement, Is.False);
            Assert.That(comp.WorkDone, Is.EqualTo(0f));
        }

        [Test]
        public void ANewComponentHasNoTargetQuality()
        {
            // Null is "any improvement is acceptable", which is a real setting and not an absence.
            Assert.That(new SimpleImproveComp().TargetQuality, Is.Null);
        }

        [Test]
        public void TargetQualityRoundTripsWithoutAMap()
        {
            // The defect this replaces, and the reason it can be tested at all now. The target used
            // to live in a map component reached through parent?.Map?.GetComponent, so the getter
            // returned null and the setter silently discarded the write for any building that was
            // not standing on a map. That includes every building during loading, because
            // Thing.ExposeData forces mapIndexOrState to -1 and things only spawn later in
            // Map.FinalizeLoading, which is why the value could not be scribed from the component.
            var comp = new SimpleImproveComp();

            comp.TargetQuality = QualityCategory.Masterwork;

            Assert.That(comp.TargetQuality, Is.EqualTo(QualityCategory.Masterwork));
        }

        [Test]
        public void AwfulIsStoredRatherThanReadingAsNoTarget()
        {
            // QualityCategory is byte backed and Awful is zero, so this is the case a non-nullable
            // field could not express. Nothing in the UI offers Awful as a target today, but the
            // field is the thing being tested, not the menu in front of it.
            var comp = new SimpleImproveComp();

            comp.TargetQuality = QualityCategory.Awful;

            Assert.That(comp.TargetQuality, Is.Not.Null);
            Assert.That(comp.TargetQuality, Is.EqualTo(QualityCategory.Awful));
        }

        [Test]
        public void ClearingTheMarkDirectlyAlsoClearsTheTarget()
        {
            // The invariant is that an unmarked building has no target. Without it a stale target
            // outlives the mark: it can still show in the inspect pane while the building holds
            // materials, and re-marking would silently re-aim at a quality the player last saw
            // cancelled. This is the path vanilla's Designator_Cancel takes, through the mod's
            // Notify_Removing prefix.
            var comp = new SimpleImproveComp();
            comp.TargetQuality = QualityCategory.Legendary;
            comp.SetMarkedForImprovementDirect(true);

            comp.SetMarkedForImprovementDirect(false);

            Assert.That(comp.TargetQuality, Is.Null);
        }

        [Test]
        public void SettingTheMarkDirectlyLeavesTheTargetAlone()
        {
            // The gizmos set a target and then mark, in that order, so clearing on the way up would
            // throw away the thing the player just chose.
            var comp = new SimpleImproveComp();
            comp.TargetQuality = QualityCategory.Good;

            comp.SetMarkedForImprovementDirect(true);

            Assert.That(comp.TargetQuality, Is.EqualTo(QualityCategory.Good));
        }

        [Test]
        public void TheComponentDeclaresPostDeSpawn()
        {
            // A declaration test rather than a behaviour one, for the same reason
            // WorkGiverSurfaceTests exists: the fix has no functional signature this harness can
            // reach. PostDeSpawn needs a spawned Thing on a Map, so deleting the override is
            // invisible to every other test here, and its absence is precisely the defect. Before
            // it existed, uninstalling a marked building left its hauled materials inside a
            // component that nothing on the map could see.
            var declared = typeof(SimpleImproveComp).GetMethod(
                "PostDeSpawn",
                new[] { typeof(Map), typeof(DestroyMode) });

            Assert.That(declared, Is.Not.Null,
                "SimpleImproveComp no longer has a PostDeSpawn(Map, DestroyMode) at all.");
            Assert.That(declared.DeclaringType, Is.EqualTo(typeof(SimpleImproveComp)),
                "SimpleImproveComp stopped overriding PostDeSpawn, which strands hauled materials "
                + "in the component on every uninstall. That is issue #11.");
        }

        [Test]
        public void TheMaterialCostIsNotHandedOutFromAField()
        {
            // GetTotalMaterialCost used to clear and refill a cachedMaterialsNeeded field and return
            // it, so every caller held a live alias to state the next call emptied. It was never
            // saving an allocation, since the method builds a fresh ThingDefCountClass per entry per
            // call regardless, and CompInspectStringExtra took the list and then made a call that
            // re-entered and cleared it one line before iterating.
            //
            // Neither method can be run here: both read SimpleImproveMod.Settings, which needs the
            // game. The field's absence is the durable part of the fix and is what this asserts.
            Assert.That(
                typeof(SimpleImproveComp).GetField(
                    "cachedMaterialsNeeded",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static),
                Is.Null,
                "cachedMaterialsNeeded is back, so GetTotalMaterialCost is handing callers an alias "
                + "to state its next call clears.");
        }

        [Test]
        public void TheNeededCountIsComputedRatherThanRead()
        {
            // ThingCountNeeded read the cache without ever populating it, so it answered with
            // whatever the last call on this component instance had left behind. Its only consumer,
            // JobDriver_HaulToImprove, sizes the deposit with it one line before the transfer that
            // would have repopulated it, so on a cold component the count was 0, nothing moved, and
            // the pawn walked away still carrying.
            Assert.That(
                CallNamesIn("ThingCountNeeded"),
                Does.Contain("SimpleImprove.Core.SimpleImproveComp.GetTotalMaterialCost"),
                "ThingCountNeeded no longer computes the cost it compares against.");
        }

        [Test]
        public void WorkToBuildIsFlooredSoNothingDividesByZero()
        {
            // JobDriver_Improve divides by this twice, and one of those divisions decides whether the
            // improvement FAILS. At zero, speed / 0 is positive infinity, Mathf.Pow(chance, infinity)
            // is zero, so the fail roll is Rand.Value < 1f and always true: the pawn destroys the
            // staged materials every tick it works and the work giver re-issues the job, while the
            // progress bar reads 0/0 as NaN.
            //
            // Vanilla does the same division unfloored in JobDriver_ConstructFinishFrame and
            // Frame.PercentComplete, so an auditor diffing against the game will want to remove this.
            // The floor lives in the property rather than at the call sites so that WorkLeft and the
            // inspect string agree with the divisors.
            Assert.That(
                CallNamesIn("get_WorkToBuild"),
                Does.Contain("UnityEngine.Mathf.Max"),
                "WorkToBuild is unfloored again. A modded def, a modded stuff stat factor, or a "
                + "custom scenario's ScenPart_StatFactor at 0% all reach zero, and zero turns every "
                + "improvement attempt into a guaranteed failure that destroys the materials.");
        }

        [Test]
        public void MarkingChecksForAnExistingDesignationBeforeAddingOne()
        {
            // Two faults in one branch. The `?.` swallowed a null map and then set the flag anyway,
            // leaving the component marked with no designation, and since the work giver became
            // designation-driven that building is invisible to it, so the mark silently did nothing.
            // And DesignationManager.AddDesignation logs a red error and returns on a double add,
            // which a save written by 1.0.5 or 1.0.6 can trigger by re-marking a building that
            // already carries a stale designation.
            //
            // IL order is not execution order, so this pins that both calls are present and that the
            // test appears before the add, which is as much as reading a body can say.
            var calls = CallNamesIn("set_IsMarkedForImprovement");

            int test = calls.IndexOf("Verse.DesignationManager.DesignationOn");
            int add = calls.IndexOf("Verse.DesignationManager.AddDesignation");

            Assert.That(test, Is.GreaterThanOrEqualTo(0),
                "The marking path no longer checks for an existing designation, so re-marking a "
                + "building that carries a stale one logs a red error every time.");
            Assert.That(add, Is.GreaterThanOrEqualTo(0),
                "The marking path no longer adds a designation at all.");
            Assert.That(test, Is.LessThan(add));
        }

        private static List<string> CallNamesIn(string methodName)
        {
            MethodInfo method = typeof(SimpleImproveComp).GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly);

            Assert.That(method, Is.Not.Null,
                "SimpleImproveComp has no " + methodName + " to read.");

            return ILCalls.CalledBy(method).Select(ILCalls.Describe).ToList();
        }

        [Test]
        public void TheGroupTooltipIsBuiltFromTranslatedKeysAlone()
        {
            // The tooltip for a group of two or more used to append the literal English " (N items)"
            // after Translate(), so every language showed the English word. The method cannot be run
            // here: its caller needs Find.Selector and Translate needs a loaded language. Its compiled
            // body can be read, and every string it loads is listed below in full, so an untranslated
            // suffix coming back is one more literal and fails. Narrowing the list to the keys first
            // would pass with the suffix still in it.
            MethodInfo method = GizmoMethod("GetGroupGizmoDesc");

            Assert.That(
                ILCalls.Read(method).Strings,
                Is.EqualTo(new List<string> { "SimpleImprove_GizmoTooltip", "SimpleImprove_GizmoTooltipGroup" }),
                "The group tooltip loads a string that is not one of its two keys, which is what an "
                + "English suffix outside Translate() looks like.");

            // The strings alone do not hold the count, and the count is the one thing the group
            // sentence adds. Translating the group key with no argument shows a raw {0} in every
            // language and loads exactly the same two strings, so the calls are pinned as well, with
            // each Translate named by its parameters: the group key has to go through the overload
            // that takes one argument, and that argument has to be converted from the group's size.
            // The branch between the two sentences is ImproveGroup.ShowsCount, which runs in
            // TheCountStartsAtTwoBuildings. What this cannot see is arithmetic on the count, such as
            // passing one less; in-game check 16 reads the number.
            Assert.That(
                ILCalls.CalledBy(method).Select(Signature).ToList(),
                Is.EqualTo(new List<string>
                {
                    "SimpleImprove.Core.ImproveGroup.get_ShowsCount()",
                    "Verse.Translator.Translate(String)",
                    "Verse.TaggedString.op_Implicit(TaggedString)",
                    "SimpleImprove.Core.ImproveGroup.get_Comps()",
                    "System.Collections.Generic.List`1.get_Count()",
                    "Verse.NamedArgument.op_Implicit(Int32)",
                    "Verse.TranslatorFormattedStringExtensions.Translate(String, NamedArgument)",
                    "Verse.TaggedString.op_Implicit(TaggedString)",
                }));
        }

        [Test]
        public void TheGroupLabelIsBuiltFromTranslatedKeysAlone()
        {
            // The label's count used to be a literal " (N)" joined after Translate(). It carried no
            // word, but it fixed the punctuation for every language, so Chinese and Japanese labels
            // that close in fullwidth parentheses gained an ASCII pair after a space. Every string
            // the method loads is listed in full, so the format string of that suffix coming back
            // (" ({0})") is one more literal and fails here.
            MethodInfo method = GizmoMethod("GetGroupGizmoLabel");

            Assert.That(
                ILCalls.Read(method).Strings,
                Is.EqualTo(new List<string>
                {
                    "SimpleImprove_GizmoLabel",
                    "SimpleImprove_GizmoLabelWithTarget",
                    "SimpleImprove_GizmoLabelAny",
                    "SimpleImprove_GizmoLabelCount",
                }));

            // And the count is read through the same rule the tooltip uses, so the two cannot start
            // counting at different sizes.
            Assert.That(
                ILCalls.CalledBy(method).Select(ILCalls.Describe).ToList(),
                Does.Contain("SimpleImprove.Core.ImproveGroup.get_ShowsCount"));
        }

        [Test]
        public void TheCountStartsAtTwoBuildings()
        {
            // One building is described in the singular with no count; a pair is already a group.
            // The tooltip used to test count == 1 and the label count > 1, two spellings of one rule
            // that could be edited apart, and changing the first to count <= 2 passed the suite
            // while describing a pair of buildings as one.
            Assert.That(GroupOf(1).ShowsCount, Is.False);
            Assert.That(GroupOf(2).ShowsCount, Is.True);
            Assert.That(GroupOf(3).ShowsCount, Is.True);
        }

        private static ImproveGroup GroupOf(int size)
        {
            var group = new ImproveGroup();

            for (int i = 0; i < size; i++)
            {
                group.Comps.Add(new SimpleImproveComp());
            }

            Assert.That(group.Comps, Has.Count.EqualTo(size));
            return group;
        }

        private static MethodInfo GizmoMethod(string name)
        {
            MethodInfo method = typeof(SimpleImproveComp).GetMethod(
                name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            Assert.That(method, Is.Not.Null, "SimpleImproveComp has no " + name + " to read.");
            return method;
        }

        /// <summary>
        /// Names a called method with its parameter types, so that two overloads read differently.
        /// </summary>
        /// <param name="method">The method to name.</param>
        /// <returns>The declaring type, the method name and its parameter type names.</returns>
        /// <remarks>
        /// The declaring type is named by its generic definition and the parameters by their short
        /// names, so that nothing in the result spells out an assembly version and the list does not
        /// need editing on every RimWorld patch release.
        /// </remarks>
        private static string Signature(MethodBase method)
        {
            System.Type type = method.DeclaringType;

            if (type != null && type.IsGenericType)
            {
                type = type.GetGenericTypeDefinition();
            }

            string parameters = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name));
            return (type == null ? method.Name : type.FullName + "." + method.Name) + "(" + parameters + ")";
        }

        [Test]
        public void TheComponentIsNotSealed()
        {
            // ThingWithComps.GetComp<T> short-circuits to null for a sealed type parameter that its
            // compsByType dictionary does not contain. Nothing depends on that today, which is
            // exactly why it is worth a test rather than only a comment.
            Assert.That(typeof(SimpleImproveComp).IsSealed, Is.False);
        }
    }
}
