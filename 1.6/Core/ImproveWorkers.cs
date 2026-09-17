using Verse;

namespace SimpleImprove.Core
{
    /// <summary>
    /// Questions about which pawns can be given improvement work.
    /// </summary>
    /// <remarks>
    /// Everything here reads live game state and so has no automated coverage. It exists to keep the
    /// readings in one place rather than repeated at each call site, which is how the three copies of
    /// the work-settings guard came to disagree about whether a null was possible at all.
    /// </remarks>
    public static class ImproveWorkers
    {
        /// <summary>
        /// Determines whether a pawn currently has the Improving work type switched on.
        /// </summary>
        /// <param name="pawn">The pawn to test.</param>
        /// <returns><c>true</c> if the pawn has usable work settings with Improving active.</returns>
        /// <remarks>
        /// <para>
        /// <c>Pawn.workSettings</c> is null on a dead pawn, and on any non-humanlike that is not a
        /// player mechanoid. It is <em>not</em> null on a colony mech:
        /// <c>PawnComponentsUtility.AddAndRemoveDynamicComponents</c> constructs one for every
        /// player-faction mechanoid carrying a <c>CompOverseerSubject</c>, which every vanilla mech
        /// does.
        /// </para>
        /// <para>
        /// Constructing is not initialising, and the two happen in different places.
        /// <c>AddAndRemoveDynamicComponents</c> only does <c>new Pawn_WorkSettings(pawn)</c>;
        /// <c>EnableAndInitialize</c> is called separately, from <c>PawnGenerator</c> and from
        /// <c>Pawn.SetFaction</c>. That gap is exactly what the <c>EverWork</c> half guards, so it is
        /// not redundant with the null test. <c>WorkIsActive</c> does not throw on a non-null but
        /// uninitialised <c>Pawn_WorkSettings</c>: <c>ConfirmInitializedDebug</c> logs an error and
        /// then silently rewrites the pawn's entire priority table. A side effect the player cannot
        /// see is worse than an exception. This is the guard <c>JobGiver_Work.GetPriority</c> opens
        /// the whole work loop with.
        /// </para>
        /// </remarks>
        public static bool IsAssignedToImproving(Pawn pawn)
        {
            return pawn != null
                && pawn.workSettings != null
                && pawn.workSettings.EverWork
                && pawn.workSettings.WorkIsActive(SimpleImproveDefOf.WorkType_Improving);
        }
    }
}
