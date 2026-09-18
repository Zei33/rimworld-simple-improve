using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using SimpleImprove.Core;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Covers the material cost percentage field: its range, and the two decisions that drive it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The control itself is drawn in <c>DoSettingsWindowContents</c>, which needs IMGUI and cannot
    /// run here, so what is covered is the arithmetic and not the wiring. The one thing only an
    /// in-game check can confirm is that the focus test is asking the right question, which is
    /// whether <c>GUI.GetNameOfFocusedControl</c> matches the name the field is drawn under.
    /// <c>Tests/README.md</c> lists it.
    /// </para>
    /// <para>
    /// What is covered is the whole of the reported defect. Every test naming a keystroke walks the
    /// string one character at a time, because the bug was not in what any single value parsed to,
    /// it was in the box being rewritten underneath the player between one keystroke and the next.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class MaterialCostFieldTests
    {
        /// <summary>
        /// Types a percentage one character at a time into an empty box, the way a player does.
        /// </summary>
        /// <remarks>
        /// Each keystroke is appended to <em>the box as the last call left it</em>, not to a prefix
        /// of the intended text. That is the whole point: the defect was the box being rewritten
        /// underneath the player between one keystroke and the next, so a loop that fed in the
        /// prefixes would pass against the code that has the bug. An earlier version of this helper
        /// did exactly that and had to be corrected.
        /// </remarks>
        private static MaterialCostEdit TypeAll(string text, float startingMultiplier)
        {
            var edit = new MaterialCostEdit(startingMultiplier, string.Empty);

            foreach (var character in text)
            {
                edit = MaterialCostField.Typing(edit.Buffer + character, edit.Multiplier);
            }

            return edit;
        }

        [Test]
        public void TheDefaultPercentageCanBeTyped()
        {
            // The defect, exactly as reported. Reaching 100% means passing through "1", which reads
            // as 1%. The old field clamped that to the minimum and replaced the box with "5", so the
            // next keystroke built on a 5 and 100 was unreachable. So were 200, 300 and 400.
            var edit = TypeAll("100", 3.0f);

            Assert.That(edit.Buffer, Is.EqualTo("100"), "The box must hold what was typed into it.");
            Assert.That(edit.Multiplier, Is.EqualTo(MaterialCostField.DefaultMultiplier));
        }

        [TestCase("10", 0.1f)]
        [TestCase("20", 0.2f)]
        [TestCase("100", 1.0f)]
        [TestCase("200", 2.0f)]
        [TestCase("300", 3.0f)]
        [TestCase("400", 4.0f)]
        [TestCase("500", 5.0f)]
        public void EveryPercentageInRangeCanBeTyped(string typed, float expected)
        {
            var edit = TypeAll(typed, 1.0f);

            Assert.That(edit.Buffer, Is.EqualTo(typed));
            Assert.That(edit.Multiplier, Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void TypingNeverRewritesTheBox()
        {
            // The invariant, stated against the values that break it. Each of these is out of range
            // on its own, and the old code replaced all five with the minimum as they were typed.
            foreach (var typed in new[] { "", "0", "1", "2", "3", "4", "9999", "-5", "abc", "1." })
            {
                Assert.That(
                    MaterialCostField.Typing(typed, 1.0f).Buffer, Is.EqualTo(typed),
                    "The box was rewritten while the player was typing " + typed);
            }
        }

        [TestCase("")]
        [TestCase("abc")]
        [TestCase("-")]
        [TestCase(".")]
        public void TextThatIsNotANumberLeavesTheSettingAlone(string typed)
        {
            Assert.That(MaterialCostField.Typing(typed, 2.5f).Multiplier, Is.EqualTo(2.5f));
        }

        [TestCase("0")]
        [TestCase("1")]
        [TestCase("9")]
        [TestCase("501")]
        [TestCase("100000")]
        [TestCase("-100")]
        public void AnOutOfRangePercentageLeavesTheSettingAloneWhileItIsBeingTyped(string typed)
        {
            // Deliberately not clamped here. Nothing in the running game can tell, because
            // Dialog_ModSettings force-pauses it, so the reason is the player: a half-typed 3 on the
            // way to 300 should not change their setting, and since the box is no longer rewritten
            // there would be nothing on screen to say that it had. Unfocused applies it, clamped,
            // when they leave the field or close the window.
            Assert.That(MaterialCostField.Typing(typed, 2.5f).Multiplier, Is.EqualTo(2.5f));
        }

        [Test]
        public void TheBoundsThemselvesAreInRange()
        {
            // An exclusive comparison here would make the advertised minimum or maximum the one
            // value the player cannot type, which is the defect again in miniature. Exact equality
            // is the point: dividing a whole number of percent by 100f is correctly rounded, so the
            // range test in Typing has to hold without a tolerance or the bounds fall outside it.
            Assert.That(
                MaterialCostField.Typing("10", 1.0f).Multiplier,
                Is.EqualTo(MaterialCostField.MinimumMultiplier));
            Assert.That(
                MaterialCostField.Typing("500", 1.0f).Multiplier,
                Is.EqualTo(MaterialCostField.MaximumMultiplier));
        }

        [Test]
        public void LeavingTheFieldSettlesAnUnderTypedValueOnTheMinimum()
        {
            // What the player sees if they type 1 and click away: the box says 10, which is the
            // minimum they asked to go below. Nothing stopped them typing it, and nothing silently
            // discarded it either.
            var edit = MaterialCostField.Unfocused("1", 1.0f);

            Assert.That(edit.Multiplier, Is.EqualTo(MaterialCostField.MinimumMultiplier));
            Assert.That(edit.Buffer, Is.EqualTo("10"));
        }

        [Test]
        public void LeavingTheFieldSettlesAnOverTypedValueOnTheMaximum()
        {
            var edit = MaterialCostField.Unfocused("99999", 1.0f);

            Assert.That(edit.Multiplier, Is.EqualTo(MaterialCostField.MaximumMultiplier));
            Assert.That(edit.Buffer, Is.EqualTo("500"));
        }

        [Test]
        public void LeavingTheFieldKeepsAValueThatIsAlreadyInRange()
        {
            var edit = MaterialCostField.Unfocused("250", 1.0f);

            Assert.That(edit.Multiplier, Is.EqualTo(2.5f).Within(0.0001f));
            Assert.That(edit.Buffer, Is.EqualTo("250"));
        }

        [TestCase("")]
        [TestCase("abc")]
        public void LeavingAnUnreadableFieldRestoresTheSetting(string typed)
        {
            // Clearing the box and clicking away has to put the real setting back rather than leave
            // an empty field that the next keystroke would parse from nothing.
            var edit = MaterialCostField.Unfocused(typed, 2.5f);

            Assert.That(edit.Multiplier, Is.EqualTo(2.5f));
            Assert.That(edit.Buffer, Is.EqualTo("250"));
        }

        [Test]
        public void TheWholeRangeSurvivesBeingWrittenOutAndReadBack()
        {
            // Every percentage the field can hold has to format to text that parses back to the same
            // multiplier, because that round trip is what Unfocused and UpdateUIBuffers both rely on.
            for (var percentage = 10; percentage <= 500; percentage++)
            {
                var multiplier = percentage / 100f;
                var edit = MaterialCostField.Unfocused(MaterialCostField.Format(multiplier), 1.0f);

                Assert.That(edit.Multiplier, Is.EqualTo(multiplier).Within(0.0001f),
                    percentage + "% did not survive the round trip.");
            }
        }

        [Test]
        public void FormattingUsesTheUnitsTheFieldDisplays()
        {
            // The units slip that caused two of the three defects in this feature: ResetToDefaults
            // wrote the multiplier 1.0 into a box showing percentages, and the old clamp maximum of
            // 1000.0f reads like somebody meaning 1000% and writing it the same wrong way.
            Assert.That(MaterialCostField.Format(MaterialCostField.DefaultMultiplier), Is.EqualTo("100"));
            Assert.That(MaterialCostField.Format(MaterialCostField.MinimumMultiplier), Is.EqualTo("10"));
            Assert.That(MaterialCostField.Format(MaterialCostField.MaximumMultiplier), Is.EqualTo("500"));
        }

        [Test]
        public void ANonsenseMultiplierFromASaveBecomesTheDefault()
        {
            // Mathf.Clamp passes NaN through both of its comparisons, so without this a save holding
            // one would reach Mathf.CeilToInt in the material cost calculation.
            Assert.That(MaterialCostField.Clamp(float.NaN), Is.EqualTo(MaterialCostField.DefaultMultiplier));
            Assert.That(MaterialCostField.Clamp(float.PositiveInfinity), Is.EqualTo(MaterialCostField.MaximumMultiplier));
            Assert.That(MaterialCostField.Clamp(float.NegativeInfinity), Is.EqualTo(MaterialCostField.MinimumMultiplier));
        }

        [Test]
        public void TheShippedTooltipNamesTheRangeTheCodeEnforces()
        {
            // The third defect in this feature, which nobody had filed: the tooltip promised 10% to
            // 500% in all nine languages while the clamp allowed 5% to 100000%. This workspace has
            // now been burned four times by shipped copy naming something the code does not do, so
            // the copy is pinned to the constants rather than to a reviewer noticing.
            var minimum = MaterialCostField.Format(MaterialCostField.MinimumMultiplier) + "%";
            var maximum = MaterialCostField.Format(MaterialCostField.MaximumMultiplier) + "%";
            var standard = MaterialCostField.Format(MaterialCostField.DefaultMultiplier) + "%";

            var files = Directory.GetFiles(
                Path.Combine(RepoRoot(), "1.6", "Languages"),
                "SimpleImprove_Keys.xml",
                SearchOption.AllDirectories);

            Assert.That(files.Length, Is.EqualTo(9),
                "Expected the nine shipped languages, found " + files.Length + ".");

            foreach (var file in files)
            {
                var language = Directory.GetParent(file).Parent.Name;
                var tooltip = XDocument.Load(file).Root
                    .Elements("SimpleImprove_MaterialCostMultiplierTooltip")
                    .Single()
                    .Value;

                Assert.That(tooltip, Does.Contain(minimum), language + " does not name the minimum.");
                Assert.That(tooltip, Does.Contain(maximum), language + " does not name the maximum.");
                Assert.That(tooltip, Does.Contain(standard), language + " does not name the default.");
            }
        }

        [Test]
        public void ADecimalPercentageCanBeTyped()
        {
            // The box has to survive a keystroke that parses but is not what the player means yet.
            // "12." parses as 12, which is in range, so a Typing that rewrote in-range text would
            // eat the decimal point and make 12.5% untypable. That mutation passes every other test
            // in this file, because for whole percentages the rewritten text is the same text.
            var edit = TypeAll("125", 1.0f);
            edit = MaterialCostField.Typing(edit.Buffer.Insert(2, "."), edit.Multiplier);

            Assert.That(edit.Buffer, Is.EqualTo("12.5"));
            Assert.That(edit.Multiplier, Is.EqualTo(0.125f).Within(0.0001f));
        }

        [Test]
        public void ADecimalPercentageSettlesOnAWholeOneWhenTheFieldIsLeft()
        {
            // And then it has to stop being a decimal, because the box only shows whole percentages.
            var edit = MaterialCostField.Unfocused("12.5", 1.0f);

            Assert.That(edit.Buffer, Is.EqualTo("13"));
            Assert.That(edit.Multiplier, Is.EqualTo(0.13f).Within(0.0001f),
                "The stored setting has to be the one the box is showing, not the one that produced it.");
        }

        [TestCase("12.5")]
        [TestCase("100.5")]
        [TestCase("1")]
        [TestCase("")]
        [TestCase("abc")]
        [TestCase("99999")]
        public void NormalisingTheSameFieldTwiceChangesNothingTheSecondTime(string typed)
        {
            // It runs on every frame the field is not focused, so anything that moved under
            // repetition would drift while the window sat open. The decimal cases are why the
            // multiplier is read back off the formatted text rather than kept.
            var once = MaterialCostField.Unfocused(typed, 3.0f);
            var twice = MaterialCostField.Unfocused(once.Buffer, once.Multiplier);

            Assert.That(twice.Multiplier, Is.EqualTo(once.Multiplier));
            Assert.That(twice.Buffer, Is.EqualTo(once.Buffer));
        }

        [Test]
        public void TypingNonsenseThatParsesLeavesTheSettingAlone()
        {
            // float.TryParse accepts the culture's NaN and infinity symbols, and Widgets.TextField
            // accepts any characters at all, so both are typable into this box.
            Assert.That(MaterialCostField.Typing("NaN", 2.5f).Multiplier, Is.EqualTo(2.5f));
            Assert.That(MaterialCostField.Typing("Infinity", 2.5f).Multiplier, Is.EqualTo(2.5f));
        }

        [Test]
        public void LeavingTheFieldOnNonsenseThatParsesRestoresTheSetting()
        {
            // NaN must not reach the setting: Mathf.Clamp passes it through both comparisons and it
            // would go on to Mathf.CeilToInt in the material cost calculation. Restoring what was
            // there is the honest answer, rather than the default, which would be a silent reset.
            var edit = MaterialCostField.Unfocused("NaN", 2.5f);

            Assert.That(edit.Multiplier, Is.EqualTo(2.5f));
            Assert.That(edit.Buffer, Is.EqualTo("250"));
        }

        [Test]
        public void LeavingTheFieldOnAnInfinityGivesTheMaximum()
        {
            // Unlike NaN this one has a right answer, and the ordinary clamp already gives it.
            Assert.That(
                MaterialCostField.Unfocused("Infinity", 2.5f).Multiplier,
                Is.EqualTo(MaterialCostField.MaximumMultiplier));
        }

        [Test]
        public void EveryShippedDescriptionNamesTheRangeTheCodeEnforces()
        {
            // The same pin as the tooltip test, over the copy that is not a translation key.
            // About.xml ships inside the mod folder and is what the in-game mod list shows; the
            // nine Workshop files are what gets pasted into the Steam store pages by hand. The
            // About.xml line was missed by the first draft of this change and found by review, so
            // this test exists because inspection has already failed once on exactly this file.
            var minimum = MaterialCostField.Format(MaterialCostField.MinimumMultiplier) + "%";
            var maximum = MaterialCostField.Format(MaterialCostField.MaximumMultiplier) + "%";
            var root = RepoRoot();

            var files = new List<string> { Path.Combine(root, "About", "About.xml") };
            files.AddRange(Directory.GetFiles(Path.Combine(root, "Workshop"), "*.md"));

            Assert.That(files.Count, Is.EqualTo(10),
                "Expected About.xml and the nine store descriptions, found " + files.Count + ".");

            foreach (var file in files)
            {
                // Select on either bound, then require both on the one line. The selector used to
                // read `l.Contains(minimum) || l.Contains("5%")`, whose second clause was a leftover
                // from the range change in #15 and matched 25%, 35%, 45% and 65% as substrings. A
                // bullet reading "25% faster with a skilled pawn" sitting above the range bullet was
                // therefore picked instead, and both assertions below then failed on it reporting
                // that the file "does not name the minimum", which points at the wrong line.
                //
                // Selecting on either bound rather than on the minimum alone is the stricter of the
                // two repairs the issue offered, and it buys one real case: copy that splits
                // "10% to 500%" across two bullets is now caught, because the bullet naming 10% is
                // selected and fails the maximum assertion. Selecting on the minimum alone would
                // have passed that, and selecting on lines containing both would have made the two
                // assertions below tautological and left only the blunt "names no range" message.
                //
                // Residual, stated rather than left to be rediscovered: these are still substring
                // tests, so "110%" contains "10%" and "1500%" contains "500%". That is tolerated
                // because the alternative is a regex, and this selector is deliberately mirrored by
                // `required_strings` in Workshop/src/mod.json, whose rule engine evaluates plain
                // containment. Keeping both halves expressible in the weaker of the two languages is
                // what makes them agree by construction rather than by coincidence. Changing
                // MinimumMultiplier or MaximumMultiplier means editing that file in the same commit.
                var line = File.ReadAllLines(file)
                    .FirstOrDefault(l => l.Contains(minimum) || l.Contains(maximum));

                Assert.That(line, Is.Not.Null, Path.GetFileName(file) + " names no material cost range.");
                Assert.That(line, Does.Contain(minimum), Path.GetFileName(file) + " does not name the minimum.");
                Assert.That(line, Does.Contain(maximum), Path.GetFileName(file) + " does not name the maximum.");
            }
        }

        /// <summary>
        /// Walks up from the test assembly to the repository root, which is the directory holding
        /// the shipped <c>1.6</c> folder.
        /// </summary>
        private static string RepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "1.6", "Languages")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException(
                "Could not find the repository root above " + AppContext.BaseDirectory + ".");
        }
    }
}
