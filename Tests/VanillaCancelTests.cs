using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using Verse;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Pins the premise that vanilla's own Cancel button is on every building marked for improvement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Issue #23 was filed, and fixed, on the belief that a marked building which could no longer be
    /// improved had no button to cancel the mark. It always had one. <c>Designator_Cancel</c> is a
    /// reverse designator, and its <c>CanDesignateThing</c> accepts any thing carrying a designation
    /// whose def leaves <c>designateCancelable</c> true, so vanilla draws its own "Cancel" on the
    /// gizmo bar of every marked building. The comment in <c>SimpleImproveComp.CompGetGizmosExtra</c>
    /// and in-game checks 8 and 9 in <c>Tests/README.md</c> rest on that, and so does the C key
    /// clearing a stranded mark, because the mod's own button never gets the key.
    /// </para>
    /// <para>
    /// It holds for two reasons, and each can break without the other. The mod's def does not set
    /// the field, and vanilla's default for it is true. An edit to the def breaks the first; a game
    /// update could break the second with the XML exactly as it is, which is why both are asserted.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class VanillaCancelTests
    {
        [Test]
        public void VanillaDefaultsADesignationToCancelable()
        {
            // A real constructor, not FormatterServices.GetUninitializedObject: the default under
            // test is a field initialiser, and bypassing initialisers would read false and fail for
            // the wrong reason.
            Assert.That(new DesignationDef().designateCancelable, Is.True,
                "Vanilla no longer defaults designateCancelable to true, so a DesignationDef that "
                + "omits it is no longer cleared by vanilla's Cancel button.");
        }

        [Test]
        public void TheImproveDesignationDoesNotOptOutOfVanillaCancel()
        {
            // Single rather than FirstOrDefault, so a renamed or moved def fails here instead of
            // passing with nothing checked.
            XElement def = XDocument
                .Load(Path.Combine(RepoRoot(), "1.6", "Defs", "DesignationDefs", "Designations_Improve.xml"))
                .Root.Elements("DesignationDef")
                .Single(element => (string)element.Element("defName") == "Designation_Improve");

            XElement field = def.Element("designateCancelable");

            Assert.That(
                field == null || string.Equals(field.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase),
                Is.True,
                "Designation_Improve opts out of vanilla's Cancel, so a stranded building would be left "
                + "with only the mod's own button, and the C key would stop clearing the mark.");
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
