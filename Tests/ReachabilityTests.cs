using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using NUnit.Framework;
using RimWorld;
using SimpleImprove.Core;
using Verse;
using Verse.AI;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Holds the mod to containing no code that nothing can reach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Dead code here was not harmless. Two designators that nothing registered sat in the source from
    /// the initial commit, and two gizmo methods nothing called from 1.0.5, until all four were deleted
    /// on 2026-09-18. The designators kept ten translated strings alive in nine files, and the
    /// documentation described a designator tool that no player could ever open. Deleting them is
    /// only half the fix; these tests are what stops the same shape coming back.
    /// </para>
    /// <para>
    /// Both tests read what the compiler or the def loader will actually see. A search of the source
    /// text for a method name would find its own declaration, and a search of the XML text would find
    /// a class named in a comment, and both of those count as a use when they are not one.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class ReachabilityTests
    {
        [Test]
        public void EveryPrivateOrInternalMethodInTheModIsCalledFromSomewhere()
        {
            // A private or internal method can only be reached from inside the mod, so if no
            // compiled body calls it, loads it as a delegate or constructs it, nothing ever runs it.
            // GetImproveGizmoLabel and ShowQualityTargetFloatMenu on SimpleImproveComp were exactly
            // that, left behind when the gizmo moved to the group path.
            //
            // Internal is included because private alone could be walked round without noticing:
            // putting GetImproveGizmoLabel back as internal passed the private-only version of this
            // test, and the string literals inside it kept its three keys alive as far as
            // LanguageParityTests could see. Calls from the test assembly's own namespace do not
            // count, since AllModMethods excludes it, so an internal method that only a test calls is
            // reported here too, which is right: it is dead in the game. Public and protected
            // members are left out because another mod or the game can reach them.
            //
            // Two kinds of private member are called implicitly and are excluded by kind rather than
            // by name. A static constructor runs when the type is first touched. Every member of a
            // compiler-generated type (an iterator's state machine, a lambda's closure class) is
            // reached through the interface or the delegate the enclosing method creates, and that
            // creation is itself a call this walk sees. An explicit interface implementation is
            // likewise called through the interface, which is why its name carries a dot.
            var called = new HashSet<MethodBase>();

            foreach (MethodBase method in ILCalls.AllModMethods())
            {
                foreach (MethodBase target in ILCalls.CalledBy(method))
                {
                    called.Add(target is MethodInfo info && info.IsGenericMethod
                        ? info.GetGenericMethodDefinition()
                        : target);
                }
            }

            List<MethodBase> examined = ILCalls.AllModMethods()
                .Where(method => method.IsPrivate || method.IsAssembly || method.IsFamilyAndAssembly)
                .Where(method => !(method.IsConstructor && method.IsStatic))
                .Where(method => method.IsConstructor || !method.Name.Contains("."))
                .Where(method => !IsCompilerGenerated(method.DeclaringType))
                .ToList();

            // The positive control. The assertion below passes on an empty list, so the list has to
            // be shown to hold the territory it claims to cover: private methods on
            // SimpleImproveComp, where the two dead ones lived, and in SimpleImprove.Jobs, and
            // internal ones on SimpleImproveComp. The first one named is reached only from a lambda
            // the compiler hoists out of CreateGroupGizmo, so it also shows that calls from generated
            // types are being counted. BuildJob is reached only as a delegate the constructor hands
            // the memo, so it shows that ldftn counts as a use.
            List<string> examinedNames = examined.Select(ILCalls.Describe).ToList();

            Assert.That(examinedNames, Does.Contain("SimpleImprove.Core.SimpleImproveComp.ShowGroupQualityTargetFloatMenu"),
                "The scan no longer examines SimpleImproveComp's private methods, so it cannot see dead ones there.");
            Assert.That(examinedNames, Does.Contain("SimpleImprove.Jobs.WorkGiver_Improve.BuildJob"),
                "The scan no longer examines private methods in SimpleImprove.Jobs.");
            Assert.That(examinedNames, Does.Contain("SimpleImprove.Core.SimpleImproveComp.TryMarkFor"),
                "The scan no longer examines internal methods, so an internal dead one would pass.");

            Assert.That(
                examined.Where(method => !called.Contains(method)).Select(ILCalls.Describe).ToList(),
                Is.Empty,
                "These private or internal methods are never called by anything in the mod. Delete "
                + "them, or find the call that was meant to reach them.");
        }

        [Test]
        public void EveryClassTheGameOnlyBuildsFromADefIsNamedByAShippedDef()
        {
            // RimWorld never discovers a Designator, a WorkGiver or a JobDriver by reflection. Each is
            // constructed only because a def names its class: a DesignationCategoryDef for a
            // designator, WorkGiverDef.giverClass, JobDef.driverClass. One that no shipped def names
            // is dead however complete it looks. Designator_MarkForImprovement and
            // Designator_CancelImprovement were exactly that for every version of the mod.
            //
            // The XML is parsed and only element text is read. A class named inside an XML comment is
            // not a registration, and a text search would count it as one.
            Type[] defBuilt = { typeof(Designator), typeof(WorkGiver), typeof(JobDriver) };

            List<Type> declared = typeof(ImproveSite).Assembly.GetTypes()
                .Where(type => type.Namespace != null
                               && type.Namespace.StartsWith("SimpleImprove", StringComparison.Ordinal)
                               && !type.Namespace.StartsWith("SimpleImprove.Tests", StringComparison.Ordinal))
                .Where(type => !type.IsAbstract && defBuilt.Any(baseType => baseType.IsAssignableFrom(type)))
                .ToList();

            var named = new HashSet<string>(StringComparer.Ordinal);
            string root = Path.Combine(RepoRoot(), "1.6");

            foreach (string folder in new[] { "Defs", "Patches" })
            {
                foreach (string file in Directory.GetFiles(Path.Combine(root, folder), "*.xml", SearchOption.AllDirectories))
                {
                    foreach (XElement element in XDocument.Load(file).Descendants().Where(e => !e.HasElements))
                    {
                        named.Add(element.Value.Trim());
                    }
                }
            }

            // The positive control: the three classes the mod does register are found by the type
            // scan and found in the XML. Without it an empty type list, or a parse that read nothing,
            // would pass the assertion below.
            List<string> declaredNames = declared.Select(type => type.FullName).ToList();

            foreach (string registered in new[]
                     {
                         "SimpleImprove.Jobs.WorkGiver_Improve",
                         "SimpleImprove.Jobs.JobDriver_Improve",
                         "SimpleImprove.Jobs.JobDriver_HaulToImprove"
                     })
            {
                Assert.That(declaredNames, Does.Contain(registered), "The type scan did not find " + registered + ".");
                Assert.That(named, Does.Contain(registered), "The def scan did not find " + registered + " named in any def.");
            }

            Assert.That(
                declaredNames.Where(name => !named.Contains(name)).ToList(),
                Is.Empty,
                "These classes can only be built from a def and no shipped def names them, so the "
                + "game never constructs them.");
        }

        private static bool IsCompilerGenerated(Type type)
        {
            for (Type current = type; current != null; current = current.DeclaringType)
            {
                if (current.IsDefined(typeof(CompilerGeneratedAttribute), false))
                {
                    return true;
                }
            }

            return false;
        }

        private static string RepoRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null
                   && !File.Exists(Path.Combine(directory.FullName, "rimworld-simple-improve.sln")))
            {
                directory = directory.Parent;
            }

            Assert.That(directory, Is.Not.Null, "Could not find the repository root.");
            return directory.FullName;
        }
    }
}
