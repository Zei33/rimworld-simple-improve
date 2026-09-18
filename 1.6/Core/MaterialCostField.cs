using UnityEngine;

namespace SimpleImprove.Core
{
    /// <summary>
    /// The state of the material cost field after one frame of editing: the multiplier to store and
    /// the text to show in the box.
    /// </summary>
    public readonly struct MaterialCostEdit
    {
        /// <summary>
        /// Initialises a new <see cref="MaterialCostEdit"/>.
        /// </summary>
        /// <param name="multiplier">The material cost multiplier to store.</param>
        /// <param name="buffer">The text the field should show.</param>
        public MaterialCostEdit(float multiplier, string buffer)
        {
            Multiplier = multiplier;
            Buffer = buffer;
        }

        /// <summary>
        /// The material cost multiplier to store. Always inside the supported range.
        /// </summary>
        public float Multiplier { get; }

        /// <summary>
        /// The text the field should show, which is not always what the multiplier formats to.
        /// </summary>
        public string Buffer { get; }
    }

    /// <summary>
    /// The supported range of the material cost setting, and the two decisions that drive its text
    /// field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every number describing this setting now comes from here. Before this existed the range was
    /// written out in four places that disagreed: the clamp in the settings window and the matching
    /// clamp in <c>ValidateAndFixLoadedData</c> both said 5% to 100000%, the field's own doc comment
    /// said 0.1 to 5.0, the shipped tooltip said 10% to 500% in all nine languages, and the store
    /// description said 5% and upwards. Three of the four agreed that the intended band was the one
    /// the tooltip promises, so that is the one kept, and the clamp is the outlier that moved. A
    /// maximum written as <c>1000.0f</c> is the same units slip as a <c>ResetToDefaults</c> that put
    /// the multiplier <c>1.0</c> into a buffer showing percentages: somebody meaning 1000% and
    /// writing it in multiplier units.
    /// </para>
    /// <para>
    /// Narrowing the clamp is a live change and not a small one for everybody.
    /// <c>ValidateAndFixLoadedData</c> clamps on load, so a colony that had set something outside
    /// 10% to 500% is brought back inside it the next time the settings load, silently. The old
    /// field made the low end hard to reach, because it rewrote its own text on every keystroke and
    /// so only passed values whose every prefix cleared the old 5% minimum, but it made the high end
    /// easy: the buffer started at <c>100</c> and one more <c>0</c> gave 1000%, which the old clamp
    /// accepted. Anyone who did that drops to 500% here. It is a changelog line, not a footnote.
    /// </para>
    /// <para>
    /// The two decisions exist separately because the field behaves differently depending on whether
    /// the player is in it. That is the whole of the fix for the untypable field, and it cannot be
    /// tested where it is used: <c>DoSettingsWindowContents</c> needs IMGUI, which the test harness
    /// cannot reach. What it decides over is a string and a float, which it can.
    /// </para>
    /// </remarks>
    public static class MaterialCostField
    {
        /// <summary>
        /// The cheapest improvement the setting allows, as a fraction of the normal build cost.
        /// </summary>
        public const float MinimumMultiplier = 0.1f;

        /// <summary>
        /// The most expensive improvement the setting allows, as a fraction of the normal build cost.
        /// </summary>
        public const float MaximumMultiplier = 5.0f;

        /// <summary>
        /// The default, which is to charge exactly what the building cost to put up.
        /// </summary>
        public const float DefaultMultiplier = 1.0f;

        /// <summary>
        /// Formats a multiplier as the whole percentage the field displays.
        /// </summary>
        /// <param name="multiplier">The multiplier to format.</param>
        /// <returns>The percentage, with no decimal part and no unit.</returns>
        public static string Format(float multiplier)
        {
            return (multiplier * 100f).ToString("F0");
        }

        /// <summary>
        /// Brings a multiplier inside the supported range.
        /// </summary>
        /// <param name="multiplier">The multiplier to bound.</param>
        /// <returns>The multiplier, or the nearer bound if it was outside them.</returns>
        /// <remarks>
        /// <c>Mathf.Clamp</c> passes NaN straight through both of its comparisons, and a NaN
        /// multiplier would go on to reach <c>Mathf.CeilToInt</c> in the material cost calculation,
        /// so this is where it stops. Both routes to one are real: a save can hold it, because
        /// <c>Scribe_Values</c> reads back whatever text the XML holds, and so can the settings box,
        /// because <c>Widgets.TextField</c> accepts any characters and <c>float.TryParse</c> accepts
        /// the culture's NaN symbol. Infinity is left to the ordinary clamp, which answers it with
        /// the maximum, and that is the right answer for it.
        /// </remarks>
        public static float Clamp(float multiplier)
        {
            return float.IsNaN(multiplier)
                ? DefaultMultiplier
                : Mathf.Clamp(multiplier, MinimumMultiplier, MaximumMultiplier);
        }

        /// <summary>
        /// Decides the setting and the field text while the player is typing in the field.
        /// </summary>
        /// <param name="typed">The text currently in the box.</param>
        /// <param name="currentMultiplier">The multiplier the setting holds now.</param>
        /// <returns>What to store and what to show.</returns>
        /// <remarks>
        /// <para>
        /// <strong>The text is handed straight back, never rewritten.</strong> That is the defect
        /// this fixes. The old code clamped whatever parsed and wrote the clamped value back into
        /// the box on the same keystroke, so a player reaching for 100% typed <c>1</c>, had it read
        /// as 1%, clamped to the minimum and replaced with <c>5</c>, and built every further
        /// keystroke on that <c>5</c>. Every value starting with a digit below the minimum was
        /// unreachable, which included the default. Vanilla's <c>Widgets.TextFieldNumeric</c> has
        /// the same trap in <c>ResolveParseNow</c> and gets away with it only because its float
        /// fields nearly all have a minimum of zero, so swapping to it would not have helped.
        /// </para>
        /// <para>
        /// The setting still follows the text as it is typed, but only while the text is already
        /// inside the range. A half-typed <c>3</c> on the way to <c>300</c> reads as 3%, which is
        /// out of range, and leaves the stored setting alone rather than dropping it to the minimum
        /// for as long as the player takes to finish the number. Nothing in the running game can
        /// observe the difference, because <c>Dialog_ModSettings</c> sets <c>forcePause</c> and the
        /// tick manager is stopped for as long as the window is open. The reason is the player: an
        /// unfinished number should not change their setting, and since the box is no longer
        /// rewritten there would be nothing on screen to say that it had. An out of range value is
        /// not discarded, only deferred: <see cref="Unfocused"/> applies it, clamped, when the
        /// player leaves the field or closes the window.
        /// </para>
        /// </remarks>
        public static MaterialCostEdit Typing(string typed, float currentMultiplier)
        {
            if (float.TryParse(typed, out var percentage))
            {
                var multiplier = percentage / 100f;

                // Equality against the clamp is the range test. It is exact for the values that can
                // be typed here, because dividing a whole number of percent by 100f is correctly
                // rounded, and it rejects NaN for free.
                if (Clamp(multiplier) == multiplier)
                {
                    return new MaterialCostEdit(multiplier, typed);
                }
            }

            return new MaterialCostEdit(currentMultiplier, typed);
        }

        /// <summary>
        /// Decides the setting and the field text when the player is not in the field.
        /// </summary>
        /// <param name="typed">The text currently in the box.</param>
        /// <param name="currentMultiplier">The multiplier the setting holds now.</param>
        /// <returns>What to store and what to show.</returns>
        /// <remarks>
        /// <para>
        /// This is where the clamping and the rewriting the old code did on every keystroke belongs.
        /// A player who asked for 1% sees the field settle on 10% once they leave it, which tells
        /// them the bound exists without ever having stopped them typing. Text that does not parse
        /// at all, including an empty box, restores the stored setting instead of losing it.
        /// </para>
        /// <para>
        /// It runs on every frame the field is not focused, not only on the frame focus is lost, so
        /// it has to be a fixed point or the setting would drift while the window sat open. That is
        /// why the multiplier is read back off the formatted text rather than kept from the value
        /// that produced it: the field shows whole percentages, so 12.5% has to settle as 13% or as
        /// 12.5%, and storing one while displaying the other is the mismatch this whole commit is
        /// about.
        /// </para>
        /// <para>
        /// It is also the commit point. <c>SimpleImproveMod.WriteSettings</c> calls through to it
        /// when the settings window closes, because Unity never moves keyboard focus off a text
        /// field when the player clicks a button, presses Escape or clicks outside the window, and
        /// RimWorld's window code does not either. Without that call the only way to leave this
        /// field would be to click one of the skill boxes.
        /// </para>
        /// </remarks>
        public static MaterialCostEdit Unfocused(string typed, float currentMultiplier)
        {
            var multiplier = float.TryParse(typed, out var percentage) && !float.IsNaN(percentage)
                ? Clamp(percentage / 100f)
                : Clamp(currentMultiplier);
            var buffer = Format(multiplier);

            return new MaterialCostEdit(
                float.TryParse(buffer, out var whole) ? Clamp(whole / 100f) : multiplier,
                buffer);
        }
    }
}
