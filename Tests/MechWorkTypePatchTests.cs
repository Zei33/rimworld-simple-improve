using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using NUnit.Framework;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Checks the shipped <c>PatchOperation</c> XML against the mod's own defs and against the
    /// installed game.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A def patch has no compiler and fails quietly in both directions. An xpath that matches
    /// nothing is a no-op, and this mod deliberately silences even the red error that would
    /// otherwise name it, because the alternative is every player without Biotech seeing one. A
    /// defName in the value that does not match the work type the mod ships would apply cleanly and
    /// give the mech a work type that does not exist.
    /// </para>
    /// <para>
    /// So the two things worth pinning are that the value matches the mod's own
    /// <c>WorkTypeDef</c>, and that the xpath still selects a node in the game as installed. The
    /// second is the one that catches a RimWorld update: these tests run against
    /// <c>$RimWorldDir/Data</c>, so a renamed or restructured <c>Mech_Constructoid</c> fails here
    /// rather than being discovered by a player whose constructoid quietly stopped improving.
    /// </para>
    /// <para>
    /// Applying the patch is not simulated. That needs <c>LoadedModManager</c> and a running game.
    /// What is checked is that the xpath selects the node the operation intends to append to, which
    /// is the part that breaks.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class MechWorkTypePatchTests
    {
        private const string ImprovingWorkType = "WorkType_Improving";

        [Test]
        public void TheModShipsTheWorkTypeThePatchesName()
        {
            // If the WorkTypeDef is ever renamed, every patch below silently stops meaning anything.
            var workTypes = ModXml("1.6/Defs/WorkTypeDefs/WorkTypes_Improve.xml")
                .Root.Elements("WorkTypeDef")
                .Select(d => (string)d.Element("defName"))
                .ToList();

            Assert.That(workTypes, Does.Contain(ImprovingWorkType));
        }

        [TestCase("1.6/Patches/MechWorkTypes.xml")]
        [TestCase("1.6/Patches/ProjectRimFactoryDrones.xml")]
        public void EveryPatchAppendsTheWorkTypeTheModActuallyShips(string patchPath)
        {
            foreach (var operation in Operations(patchPath))
            {
                var appended = operation.Element("value").Elements("li").Select(li => li.Value.Trim());

                Assert.That(appended, Is.EqualTo(new[] { ImprovingWorkType }),
                    patchPath + " appends something other than the mod's own work type.");
            }
        }

        [TestCase("1.6/Patches/MechWorkTypes.xml")]
        [TestCase("1.6/Patches/ProjectRimFactoryDrones.xml")]
        public void EveryPatchIsSilencedByAChildElementRatherThanAnAttribute(string patchPath)
        {
            // DirectXmlToObject maps only child NODES onto fields, so success="Always" written as an
            // XML attribute is read by nothing and the operation would log a red error at the end of
            // def loading for every player who does not have the mod or DLC it targets.
            foreach (var operation in Operations(patchPath))
            {
                Assert.That(operation.Attribute("success"), Is.Null,
                    patchPath + " sets success as an attribute, which is silently ignored.");
                Assert.That((string)operation.Element("success"), Is.EqualTo("Always"),
                    patchPath + " is missing <success>Always</success>.");
            }
        }

        [TestCase("1.6/Patches/MechWorkTypes.xml")]
        [TestCase("1.6/Patches/ProjectRimFactoryDrones.xml")]
        public void NoPatchReliesOnMayRequire(string patchPath)
        {
            // ModContentPack.LoadPatches deserialises every <Operation> unconditionally and reads no
            // MayRequire attribute. One here would look like a DLC gate and do nothing at all.
            foreach (var operation in Operations(patchPath))
            {
                Assert.That(operation.Attribute("MayRequire"), Is.Null,
                    patchPath + " carries MayRequire on an Operation, where it is not honoured.");
                Assert.That(operation.Attribute("MayRequireAnyOf"), Is.Null,
                    patchPath + " carries MayRequireAnyOf on an Operation, where it is not honoured.");
            }
        }

        [Test]
        public void TheMechPatchTargetsTheListNodeRatherThanTheDef()
        {
            // PatchOperationAdd appends each child of <value> to every node the xpath selects, so the
            // xpath has to end at the list itself. Pointed at the ThingDef it would append a stray
            // <li> directly under the def.
            var xpath = (string)Operations("1.6/Patches/MechWorkTypes.xml").Single().Element("xpath");

            Assert.That(xpath, Does.EndWith("/race/mechEnabledWorkTypes"));
        }

        [Test]
        public void TheMechPatchXpathStillSelectsANodeInTheInstalledGame()
        {
            // The test that catches a RimWorld update. Run against the shipped Biotech XML, not
            // against a fixture, so a renamed def or a moved mechEnabledWorkTypes fails here.
            var xpath = (string)Operations("1.6/Patches/MechWorkTypes.xml").Single().Element("xpath");
            var races = GameXml("Biotech/Defs/ThingDefs_Races/Races_Mechanoids_Light.xml");

            // The shipped file's root is <Defs>, which is what the patch xpath is written against.
            var selected = races.XPathSelectElements(xpath).ToList();

            Assert.That(selected, Is.Not.Empty,
                "The Mech_Constructoid xpath no longer selects anything in the installed game. "
                + "Either the def moved, or mechEnabledWorkTypes is no longer declared on it directly. "
                + "Remember patches run before XmlInheritance.Resolve, so an inherited list will not match.");
            Assert.That(selected.Count, Is.EqualTo(1));
        }

        [Test]
        public void TheConstructoidStillShipsConstructionAndNotImprovingAlready()
        {
            // Pins the premise of the whole issue. If a future Biotech gave the constructoid the work
            // type some other way, or took Construction off it, the patch would need rethinking.
            var xpath = (string)Operations("1.6/Patches/MechWorkTypes.xml").Single().Element("xpath");
            var list = GameXml("Biotech/Defs/ThingDefs_Races/Races_Mechanoids_Light.xml")
                .XPathSelectElements(xpath).Single();

            var shipped = list.Elements("li").Select(li => li.Value.Trim()).ToList();

            Assert.That(shipped, Does.Contain("Construction"),
                "Mech_Constructoid no longer ships Construction, so it may no longer be the right mech.");
            Assert.That(shipped, Does.Not.Contain(ImprovingWorkType),
                "The game now ships the improving work type on the constructoid, so this patch is redundant.");
        }

        private static XElement[] Operations(string patchPath)
        {
            var operations = ModXml(patchPath).Root.Elements("Operation").ToArray();

            Assert.That(operations, Is.Not.Empty, patchPath + " declares no Operation.");
            return operations;
        }

        private static XDocument ModXml(string repoRelativePath)
        {
            return XDocument.Load(Path.Combine(RepoRoot(), repoRelativePath));
        }

        private static XDocument GameXml(string dataRelativePath)
        {
            var rimWorldDir = Environment.GetEnvironmentVariable("RimWorldDir");
            if (string.IsNullOrEmpty(rimWorldDir))
            {
                throw new InvalidOperationException(
                    "RimWorldDir is not set. These tests read the game's shipped Defs to confirm the "
                    + "patch xpaths still select something.");
            }

            var full = Path.Combine(rimWorldDir, "Data", dataRelativePath);

            // Biotech is a paid DLC. The mod ships <success>Always</success> precisely so a player
            // without it sees nothing, so the suite must not assume more than the mod does. Skipping
            // is right here and failing would be wrong: on a machine with no Biotech there is nothing
            // to check and nothing broken.
            Assume.That(File.Exists(full), Is.True,
                "Skipped: " + dataRelativePath + " is not installed, so the Biotech defs this checks "
                + "against are unavailable. The patch is a no-op without Biotech anyway.");

            return XDocument.Load(full);
        }

        /// <summary>
        /// Walks up from the test assembly to the repository root.
        /// </summary>
        /// <remarks>
        /// The assembly runs out of <c>Tests/bin/Debug/net472</c>, and the solution file is the
        /// marker rather than a fixed number of parent steps, so this keeps working if the output
        /// path changes.
        /// </remarks>
        private static string RepoRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "rimworld-simple-improve.sln")))
            {
                directory = directory.Parent;
            }

            Assert.That(directory, Is.Not.Null, "Could not find the repository root from the test directory.");
            return directory.FullName;
        }
    }
}
