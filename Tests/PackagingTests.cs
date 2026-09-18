using System.IO;
using System.Linq;
using NUnit.Framework;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Holds the staging step in <c>build.sh</c> to an allow-list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What this can and cannot do is worth stating, because it reads a shell script as text and
    /// that is a weak kind of test. It cannot run <c>build.sh</c>: the script ends by deleting and
    /// replacing the installed mod folder, so running it from a test would wipe whatever is in the
    /// game directory. It therefore checks the one thing that distinguishes the fixed shape from
    /// the broken one, and would not notice a staging step that is wrong in some new way.
    /// </para>
    /// <para>
    /// It is here at all because the defect it guards has no signature. Shipping the C# source tree
    /// to subscribers breaks nothing, loads nothing and logs nothing; it is simply 25 files and
    /// about 285 KB of every download, and it survived a year of releases unnoticed for exactly
    /// that reason.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class PackagingTests
    {
        [Test]
        public void BuildDoesNotStageTheWholeSourceFolder()
        {
            string script = Commands();

            Assert.That(
                script, Does.Not.Contain("cp -r 1.6 release"),
                "build.sh copies 1.6 wholesale again, which puts every C# source file, the vendored "
                + "Harmony reference and the two developer READMEs into every subscriber's mod "
                + "folder. Stage the folders the game reads by name instead.");
        }

        [Test]
        public void BuildStagesEachShippedFolderByName()
        {
            string script = Commands();

            foreach (string folder in new[] { "1.6/Defs", "1.6/Languages", "1.6/Textures", "1.6/Patches" })
            {
                Assert.That(
                    script, Does.Contain(folder),
                    "build.sh no longer stages " + folder + ", so that folder is missing from the "
                    + "shipped mod entirely. This fails loudly in game rather than quietly, but it "
                    + "fails after upload rather than before.");
            }

            Assert.That(
                script, Does.Contain("SimpleImprove.dll"),
                "build.sh no longer names the mod's own assembly, so either nothing ships or "
                + "everything in Assemblies does.");
        }

        [Test]
        public void NoSourceFolderIsStagedWholesale()
        {
            // The folders that hold only C# and must never appear in a cp target. Patches is
            // deliberately absent from this list: it holds both the XML PatchOperations the game
            // reads and the C# Harmony patches it does not, so it is staged by glob rather than
            // wholesale, which the test above covers.
            string script = Commands();

            foreach (string folder in new[] { "1.6/Core", "1.6/Jobs", "1.6/Utils" })
            {
                Assert.That(
                    script.Contains("cp -r " + folder), Is.False,
                    "build.sh stages " + folder + ", which is C# source and is not read by the game.");
            }
        }

        [Test]
        public void EveryFolderHoldingShippedContentIsAccountedFor()
        {
            // The positive control for the two tests above. They assert that named folders appear
            // and that source folders do not, which both pass trivially if a NEW content folder is
            // added and simply forgotten. This walks what is actually on disk and insists each
            // top-level folder under 1.6 is either staged by name or is one of the known
            // not-shipped ones, so adding 1.6/Sounds fails here until somebody decides about it.
            string root = RepoRoot();
            string script = Commands();

            var neverShipped = new[] { "Core", "Jobs", "Utils", "Libraries", "Assemblies" };

            foreach (string directory in Directory.GetDirectories(Path.Combine(root, "1.6")))
            {
                string name = Path.GetFileName(directory);

                if (neverShipped.Contains(name))
                {
                    continue;
                }

                Assert.That(
                    script, Does.Contain("1.6/" + name),
                    "1.6/" + name + " exists but build.sh never mentions it, so it is not shipped. "
                    + "Either stage it or add it to this test's not-shipped list with a reason.");
            }
        }

        /// <summary>
        /// Reads <c>build.sh</c> with its comment lines removed.
        /// </summary>
        /// <returns>Only the lines the shell actually executes.</returns>
        /// <remarks>
        /// Stripping comments is not tidiness, it is required. The first version of this fixture
        /// read the whole file and failed immediately, because the comment explaining why
        /// `cp -r 1.6 release` was replaced contains that exact command. A test that reads a script
        /// as prose cannot tell an instruction from a description of one.
        /// </remarks>
        private static string Commands()
        {
            return string.Join(
                "\n",
                File.ReadAllLines(Path.Combine(RepoRoot(), "build.sh"))
                    .Where(line => !line.TrimStart().StartsWith("#")));
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
