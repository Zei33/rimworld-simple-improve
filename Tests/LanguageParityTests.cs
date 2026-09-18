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
            // The forward direction. A key the code asks for and no language declares renders as
            // its raw name, which is a player-facing defect. The reverse direction is the next test.
            var declared = new HashSet<string>(
                XDocument.Load(KeyedPath("English")).Root.Elements().Select(e => e.Name.LocalName));

            HashSet<string> asked = KeysAskedForInSource();

            Assert.That(asked.Count, Is.GreaterThan(0),
                "Found no SimpleImprove_ keys in the C# at all, so this test is scanning nothing.");

            Assert.That(
                asked.Except(declared).ToList(), Is.Empty,
                "The code asks for keys no language declares, which render as the raw key name.");
        }

        [Test]
        public void EveryDeclaredKeyIsStillUsedByTheCompiledMod()
        {
            // The reverse direction. A key no code asks for is dead text in nine files, and it goes
            // on being translated and reviewed. Eleven of them were removed at once: the ten that
            // only the two unregistered designators used, and SimpleImprove_TargetReserved, which
            // was set only on a path where CanReserve ignores other pawns' reservations and so
            // could never describe one.
            //
            // This reads the compiled IL rather than the source, and that is required, not a
            // preference. A text scan cannot tell a key the code uses from a comment that quotes it
            // in the same form, and in this direction that failure is silent: a comment explaining
            // why a key was removed would count as a use and hide the orphan it describes. The
            // compiler drops comments, so every string literal left in the assembly is code.
            List<string> declared = XDocument.Load(KeyedPath("English")).Root.Elements()
                .Select(e => e.Name.LocalName).ToList();

            ISet<string> literals = ILCalls.StringsAnywhereInTheMod();

            Assert.That(declared, Is.Not.Empty, "English declares no keys, so there is nothing to check.");

            // Checked first so that a scan fault is reported as one. The IL scan has to see every key
            // the source scan sees. If it reached less of the mod, keys used only in the part it
            // missed would be reported below as unused, and the message would send somebody to
            // delete strings the game still shows.
            List<string> missedByTheScan = KeysAskedForInSource().Where(key => !literals.Contains(key)).ToList();

            Assert.That(
                missedByTheScan, Is.Empty,
                "The IL scan does not see keys the source asks for, so it is not reading the whole mod. "
                + "If one of these appears only in a comment, that comment quotes it as a call and the "
                + "source scan is the one that is wrong.");

            Assert.That(
                declared.Where(key => !literals.Contains(key)).ToList(), Is.Empty,
                "These keys are declared but nothing in the compiled mod asks for them. Remove them "
                + "from all nine Keyed files, or find the call that was meant to use them.");
        }

        [Test]
        public void EveryTranslationCarriesTheSamePlaceholdersAsEnglish()
        {
            // A translation that drops or renumbers a placeholder still loads, still has its key,
            // and passes every test above; the player just sees a sentence with the number missing
            // or the wrong number in it. The group tooltip's count is the newest example: it is the
            // only thing that sentence adds over the single-building one.
            Dictionary<string, Dictionary<string, string>> byLanguage = Languages.ToDictionary(
                language => language,
                language => XDocument.Load(KeyedPath(language)).Root.Elements()
                    .ToDictionary(e => e.Name.LocalName, e => e.Value));

            var placeholder = new Regex("\\{[0-9]+\\}");
            var mismatches = new List<string>();

            foreach (KeyValuePair<string, string> english in byLanguage["English"])
            {
                List<string> expected = placeholder.Matches(english.Value).Cast<Match>()
                    .Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal).ToList();

                foreach (string language in Languages)
                {
                    if (!byLanguage[language].TryGetValue(english.Key, out string translated))
                    {
                        continue;
                    }

                    List<string> found = placeholder.Matches(translated).Cast<Match>()
                        .Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal).ToList();

                    if (!found.SequenceEqual(expected))
                    {
                        mismatches.Add(language + "/" + english.Key);
                    }
                }
            }

            // The control: English has to have some placeholders for this to be testing anything.
            Assert.That(
                byLanguage["English"].Values.Count(value => placeholder.IsMatch(value)), Is.GreaterThan(5),
                "Almost no English string carries a placeholder, so this test is checking nothing.");

            Assert.That(mismatches, Is.Empty,
                "These translations do not carry the same {n} placeholders as the English.");
        }

        [Test]
        public void NoTranslationIsLeftInEnglish()
        {
            // A key copied into a language and never translated passes every test above: it is
            // declared, it is not empty, and its placeholders match the English because they are the
            // English. The player sees an English sentence in the middle of their language, which is
            // the defect the group tooltip was until 2026-09-18. Setting the German group tooltip to
            // the English sentence passed the whole suite.
            //
            // Three keys are allowed to match, each for a reason that holds in every language, and
            // anything else identical to English fails. SettingsCategory is the mod's name, which is
            // not translated. LabelColon is punctuation, which differs only where the typography does:
            // French sets a space before a colon, and Chinese and Japanese use a fullwidth one.
            // GizmoLabelCount is a format with no words in it, and Chinese and Japanese alone differ
            // because their labels close in fullwidth parentheses.
            var mayMatchEnglish = new HashSet<string>
            {
                "SimpleImprove_SettingsCategory",
                "SimpleImprove_LabelColon",
                "SimpleImprove_GizmoLabelCount",
            };

            Dictionary<string, Dictionary<string, string>> byLanguage = Languages.ToDictionary(
                language => language,
                language => XDocument.Load(KeyedPath(language)).Root.Elements()
                    .ToDictionary(e => e.Name.LocalName, e => e.Value));

            Dictionary<string, string> english = byLanguage["English"];

            // A stale exemption is an unexamined one: if an allowed key is renamed, the new name
            // is not exempt and this fails, but the old name would sit here exempting nothing.
            Assert.That(mayMatchEnglish.Where(key => !english.ContainsKey(key)).ToList(), Is.Empty,
                "An exempted key is no longer declared, so the exemption is stale.");

            var untranslated = new List<string>();
            int compared = 0;

            foreach (string language in Languages.Where(l => l != "English"))
            {
                foreach (KeyValuePair<string, string> entry in byLanguage[language])
                {
                    if (mayMatchEnglish.Contains(entry.Key) || !english.TryGetValue(entry.Key, out string source))
                    {
                        continue;
                    }

                    compared++;

                    if (entry.Value == source)
                    {
                        untranslated.Add(language + "/" + entry.Key);
                    }
                }
            }

            // The control: eight languages times every key but three. A comparison that matched
            // nothing, such as one reading the wrong file, would find no untranslated string.
            Assert.That(compared, Is.EqualTo(8 * (english.Count - mayMatchEnglish.Count)),
                "The comparison did not cover every key in every translation.");

            Assert.That(untranslated, Is.Empty, "These translations are identical to the English.");
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

        [Test]
        public void EveryLanguageShipsTheSameDefInjectedDirectoriesAndKeys()
        {
            // The Keyed files have always been in parity, which is exactly why the missing
            // DefInjected/WorkGiverDef went unnoticed for so long: a player reported a missing
            // Chinese translation on 28 October 2025 and every Keyed key WAS present in all nine, so
            // the report looked wrong. The injections are the other half and need the same guard.
            Dictionary<string, Dictionary<string, List<string>>> byLanguage = Languages.ToDictionary(
                language => language, language => InjectionsFor(language));

            Dictionary<string, List<string>> english = byLanguage["English"];

            Assert.That(english.Keys, Is.Not.Empty, "English injects nothing at all.");

            foreach (string language in Languages)
            {
                Assert.That(
                    byLanguage[language].Keys, Is.EquivalentTo(english.Keys),
                    language + " does not inject the same def types as English. Missing: "
                    + string.Join(", ", english.Keys.Except(byLanguage[language].Keys))
                    + "; extra: " + string.Join(", ", byLanguage[language].Keys.Except(english.Keys)));

                foreach (string defType in english.Keys)
                {
                    if (!byLanguage[language].ContainsKey(defType))
                    {
                        continue;
                    }

                    Assert.That(
                        byLanguage[language][defType], Is.EquivalentTo(english[defType]),
                        language + "/" + defType + " does not inject the same fields as English, so "
                        + "those fields render in English in that language.");
                }
            }
        }

        [Test]
        public void TheWorkGiverIsInjectedInEveryLanguageAndNamesTheShippedDef()
        {
            // Issue #17. WorkGiverDef.verb and .gerund carry [MustTranslate] and .label inherits it
            // from Def, and the mod's def XML sets all three in English, so without an injection the
            // improve entry renders part-English everywhere.
            //
            // The defName is checked against the shipped def rather than hardcoded twice, because a
            // wrong defName here fails SOFTLY: DefInjectionPackage appends it to loadErrors and all
            // that reaches the player is one aggregate yellow warning telling them to generate a
            // translation report. Nothing goes red, and the mod keeps rendering English.
            string defName = XDocument
                .Load(Path.Combine(RepoRoot(), "1.6/Defs/WorkGiverDefs/WorkGivers_Improve.xml"))
                .Root.Elements("WorkGiverDef").Select(d => (string)d.Element("defName")).Single();

            var expected = new[] { defName + ".label", defName + ".gerund", defName + ".verb" };

            foreach (string language in Languages)
            {
                Dictionary<string, List<string>> injections = InjectionsFor(language);

                Assert.That(
                    injections.ContainsKey("WorkGiverDef"), Is.True,
                    language + " has no DefInjected/WorkGiverDef, so the improve entry in its float "
                    + "menu and work tab renders in English.");

                Assert.That(injections["WorkGiverDef"], Is.EquivalentTo(expected));
            }
        }

        [Test]
        public void NoTwoLanguagesShareAnInjectedValue()
        {
            // A translation that is byte-identical to the English is usually a file copied and not
            // translated. The three WorkGiverDef fields are the ones this test can speak for: every
            // language has a distinct word for improving, and Chinese and Japanese, which do share
            // the character pair, differ on the label and on the particle.
            Dictionary<string, Dictionary<string, string>> values = Languages.ToDictionary(
                language => language, language => InjectedValues(language, "WorkGiverDef"));

            foreach (string language in Languages.Where(l => l != "English"))
            {
                Assert.That(
                    values[language].Values.SequenceEqual(values["English"].Values), Is.False,
                    language + "/WorkGiverDef is identical to English, which means the file was "
                    + "copied rather than translated.");
            }
        }

        /// <summary>
        /// Finds every key the mod's C# source passes to <c>Translate</c>.
        /// </summary>
        /// <returns>The keys, each once.</returns>
        /// <remarks>
        /// The literal must be followed by <c>.Translate</c> to count. Matching every
        /// <c>SimpleImprove_</c> string instead is too wide and gives a false positive on the first
        /// run: the material cost field's GUI control name is <c>SimpleImprove_MaterialCostField</c>,
        /// which looks exactly like a key and is one of <c>GUIUtility.keyboardControl</c>'s names.
        /// The limit, stated rather than left to be found: a key reached through a variable or built
        /// by concatenation is invisible here. This scan sees the direct form, which is the form the
        /// whole mod uses. It reads source text, comments included, so it is used only where a
        /// comment could make it fail loudly, never where one could make it pass.
        /// </remarks>
        private static HashSet<string> KeysAskedForInSource()
        {
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

            return asked;
        }

        private static Dictionary<string, List<string>> InjectionsFor(string language)
        {
            string root = Path.Combine(RepoRoot(), "1.6", "Languages", language, "DefInjected");
            var found = new Dictionary<string, List<string>>();

            if (!Directory.Exists(root))
            {
                return found;
            }

            foreach (string directory in Directory.GetDirectories(root))
            {
                var keys = new List<string>();

                foreach (string file in Directory.GetFiles(directory, "*.xml", SearchOption.AllDirectories))
                {
                    keys.AddRange(XDocument.Load(file).Root.Elements().Select(e => e.Name.LocalName));
                }

                found[Path.GetFileName(directory)] = keys;
            }

            return found;
        }

        private static Dictionary<string, string> InjectedValues(string language, string defType)
        {
            string directory = Path.Combine(
                RepoRoot(), "1.6", "Languages", language, "DefInjected", defType);

            var values = new Dictionary<string, string>();

            foreach (string file in Directory.GetFiles(directory, "*.xml", SearchOption.AllDirectories))
            {
                foreach (XElement element in XDocument.Load(file).Root.Elements())
                {
                    values[element.Name.LocalName] = element.Value;
                }
            }

            return values;
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
