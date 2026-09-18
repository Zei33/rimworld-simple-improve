using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Holds the nine shipped languages to the same key set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A key added to English alone is a shipped bug rather than a warning: the player sees a raw
    /// key name in the UI, and nothing in the game fails loudly enough to catch it. This mod has
    /// already shipped one whole family of that defect, the missing <c>DefInjected/WorkGiverDef</c>,
    /// which a player reported as a missing Chinese translation on 28 October 2025 and which looked
    /// wrong at first glance precisely because every Keyed key WAS present in all nine.
    /// </para>
    /// <para>
    /// So this fixture checks both halves: that the Keyed sets match each other, and that every key
    /// the C# actually asks for is one of them.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class LanguageParityTests
    {
        private static readonly string[] Languages =
        {
            "English", "ChineseSimplified", "French", "German", "Japanese",
            "Polish", "PortugueseBrazilian", "Russian", "Spanish"
        };

        [Test]
        public void AllNineLanguagesShipAKeyedFile()
        {
            foreach (string language in Languages)
            {
                Assert.That(
                    File.Exists(KeyedPath(language)), Is.True,
                    language + " has no Keyed file, so every string in it falls back to the key name.");
            }
        }

        [Test]
        public void EveryLanguageDeclaresExactlyTheSameKeys()
        {
            Dictionary<string, List<string>> byLanguage = Languages.ToDictionary(
                language => language,
                language => XDocument.Load(KeyedPath(language)).Root.Elements()
                    .Select(e => e.Name.LocalName).ToList());

            var english = new HashSet<string>(byLanguage["English"]);

            Assert.That(english.Count, Is.GreaterThan(0), "English declares no keys at all.");

            foreach (string language in Languages)
            {
                var keys = new HashSet<string>(byLanguage[language]);

                Assert.That(
                    byLanguage[language].Count, Is.EqualTo(keys.Count),
                    language + " declares a key twice: "
                    + string.Join(", ", byLanguage[language].GroupBy(k => k)
                        .Where(g => g.Count() > 1).Select(g => g.Key)));

                Assert.That(
                    english.Except(keys).ToList(), Is.Empty,
                    language + " is missing keys that English has, so those strings render as raw "
                    + "key names in that language.");

                Assert.That(
                    keys.Except(english).ToList(), Is.Empty,
                    language + " declares keys English does not, which are dead weight for "
                    + "translators and usually a rename that only landed in one file.");
            }
        }

        [Test]
        public void NoShippedStringIsEmpty()
        {
            foreach (string language in Languages)
            {
                List<string> empty = XDocument.Load(KeyedPath(language)).Root.Elements()
                    .Where(e => string.IsNullOrWhiteSpace(e.Value))
                    .Select(e => e.Name.LocalName)
                    .ToList();

                Assert.That(empty, Is.Empty, language + " ships empty strings: " + string.Join(", ", empty));
            }
        }

        [Test]
        public void EveryKeyTheCodeAsksForIsDeclaredInEveryLanguage()
        {
            // The forward direction only. Orphaned keys, of which this mod has a known backlog, are a
            // separate cleanup and would make this test fail for a reason that is not a player-facing
            // defect. A key the code asks for and no language declares IS player-facing.
            var declared = new HashSet<string>(
                XDocument.Load(KeyedPath("English")).Root.Elements().Select(e => e.Name.LocalName));

            // The literal must be followed by .Translate to count. Matching every "SimpleImprove_"
            // string instead is too wide and gives a false positive on the first run: the material
            // cost field's GUI control name is "SimpleImprove_MaterialCostField", which looks exactly
            // like a key and is one of GUIUtility.keyboardControl's names.
            //
            // The limit, stated rather than left to be found: a key reached through a variable or
            // built by concatenation is invisible here. This scan sees the direct form, which is the
            // form the whole mod uses.
            var asked = new HashSet<string>();
            var pattern = new Regex("\"(SimpleImprove_[A-Za-z0-9_]+)\"\\s*\\.\\s*Translate");

            foreach (string file in Directory.GetFiles(
                         Path.Combine(RepoRoot(), "1.6"), "*.cs", SearchOption.AllDirectories))
            {
                foreach (Match match in pattern.Matches(File.ReadAllText(file)))
                {
                    asked.Add(match.Groups[1].Value);
                }
            }

            Assert.That(asked.Count, Is.GreaterThan(0),
                "Found no SimpleImprove_ keys in the C# at all, so this test is scanning nothing.");

            Assert.That(
                asked.Except(declared).ToList(), Is.Empty,
                "The code asks for keys no language declares, which render as the raw key name.");
        }

        [Test]
        public void TheStrandedCancelButtonIsTranslatedEverywhere()
        {
            // Issue #23 added this key. It is named explicitly rather than left to the parity test
            // above, because that test would also pass if the key were absent from all nine at once.
            foreach (string language in Languages)
            {
                XElement key = XDocument.Load(KeyedPath(language)).Root
                    .Elements("SimpleImprove_CancelImprovementStranded").SingleOrDefault();

                Assert.That(key, Is.Not.Null,
                    language + " has no SimpleImprove_CancelImprovementStranded, so a building that "
                    + "is marked but no longer improvable shows a raw key name on its cancel button.");
                Assert.That(key.Value, Is.Not.Empty);
            }
        }

        private static string KeyedPath(string language)
        {
            return Path.Combine(RepoRoot(), "1.6", "Languages", language, "Keyed", "SimpleImprove_Keys.xml");
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
