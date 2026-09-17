using System.Collections.Generic;
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

        /// <summary>
        /// The priority the mod gives a colony mech for improvement work when it switches it on.
        /// </summary>
        /// <remarks>
        /// Three is what <c>Pawn_WorkSettings.EnableAndInitialize</c> assigns to every work type it
        /// switches on, so a corrected mech looks exactly like one gestated after the mod was added.
        /// </remarks>
        public const int DefaultMechPriority = 3;

        /// <summary>
        /// Decides whether the mod should switch improvement work on for a colony mech itself.
        /// </summary>
        /// <param name="isColonyMech">Whether the pawn is a colony mech.</param>
        /// <param name="workTypeIsDisabled">Whether the work type is disabled for the pawn.</param>
        /// <param name="currentPriority">The pawn's stored priority for the work type.</param>
        /// <returns><c>true</c> if the priority should be raised.</returns>
        /// <remarks>
        /// <para>
        /// Without this the def patch is inert for exactly the players who reported the problem. A
        /// mech that existed before the work type did has priority 0 stored against it, because
        /// <c>Pawn_WorkSettings.ExposeData</c> called <c>Disable</c> on it every load while it was
        /// still on the disabled list, and <c>DefMap.ExposeData</c> pads an unseen def with 0
        /// regardless. Adding the work type to <c>mechEnabledWorkTypes</c> stops it being disabled;
        /// it does not raise the stored number. Nothing re-runs <c>EnableAndInitialize</c> for a pawn
        /// that already has settings, and <c>GetPriority</c>'s "treat any non-zero as 3" shortcut is
        /// gated on <c>RaceProps.Humanlike</c>, so a mech is held to the stored 0 exactly.
        /// </para>
        /// <para>
        /// The player cannot fix it either. RimWorld's only per-work-type priority UI is the Work
        /// tab, and <c>MainTabWindow_PawnTable.Pawns</c> is <c>mapPawns.FreeColonists</c>, which
        /// filters on <c>RaceProps.Humanlike</c> and so never lists a mech. Biotech's own Mechs tab
        /// offers a work mode and no per-work-type column.
        /// </para>
        /// <para>
        /// Raising it is therefore not overriding a player's choice, because there is no way for a
        /// player to have made that choice. That asymmetry is the whole justification, and it is why
        /// this only ever touches mechs and only ever raises from zero. If RimWorld or another mod
        /// ever gives mechs a per-work-type control, this becomes wrong and should be reconsidered.
        /// </para>
        /// <para>
        /// The disabled test is not optional: <c>Pawn_WorkSettings.SetPriority</c> logs a red error
        /// and refuses when asked for a non-zero priority on a disabled work type, so calling it
        /// unguarded would spam the log once per mech for anyone whose mech still cannot do it.
        /// </para>
        /// </remarks>
        public static bool ShouldEnableForMech(bool isColonyMech, bool workTypeIsDisabled, int currentPriority)
        {
            return isColonyMech && !workTypeIsDisabled && currentPriority == 0;
        }

        /// <summary>
        /// Lists every spawned pawn on a map that could hold the Improving work type.
        /// </summary>
        /// <param name="map">The map to look at.</param>
        /// <returns>Player colonists and colony mechs, or an empty list when there is no map.</returns>
        /// <remarks>
        /// <para>
        /// Colonists alone are not the answer, and this is easy to get wrong because the obvious
        /// property looks like it covers everyone. <c>MapPawns.FreeColonistsSpawned</c> is
        /// <c>FreeHumanlikesSpawnedOfFaction</c>, which filters on <c>RaceProps.Humanlike</c>, so it
        /// silently excludes colony mechs. So do <c>FreeColonists</c>,
        /// <c>FreeColonistsAndPrisoners</c> and every colonist list on <c>PawnsFinder</c>. There is
        /// no vanilla property that returns colonists and mechs together, so vanilla code that wants
        /// both writes the predicate inline.
        /// </para>
        /// <para>
        /// Once a constructoid can improve, a warning that counted only colonists would tell a
        /// mechanitor colony that nothing can reach the target quality while a constructoid was
        /// standing there able to do it.
        /// </para>
        /// <para>
        /// The mechs are narrowed to <c>IsColonyMechPlayerControlled</c> rather than taken as
        /// <c>SpawnedColonyMechs</c> gives them. That property filters on <c>IsColonyMech</c>, which
        /// does not require an overseer, and a mech with no overseer can never take the job:
        /// <c>ThinkNode_ConditionalWorkMode.Satisfied</c> returns false when <c>GetOverseer()</c> is
        /// null, so it never reaches <c>JobGiver_Work</c> at all. Counting it here would suppress the
        /// warning on the strength of a worker that cannot work.
        /// </para>
        /// <para>
        /// Both getters return a shared buffer that they clear on access. They are currently two
        /// different buffers, so reading one does not empty the other, but the result is copied here
        /// rather than concatenated lazily so that this does not quietly depend on that.
        /// </para>
        /// </remarks>
        public static List<Pawn> PotentialOnMap(Map map)
        {
            var workers = new List<Pawn>();
            if (map == null || map.mapPawns == null)
            {
                return workers;
            }

            workers.AddRange(map.mapPawns.FreeColonistsSpawned);

            foreach (var mech in map.mapPawns.SpawnedColonyMechs)
            {
                if (mech != null && mech.IsColonyMechPlayerControlled)
                {
                    workers.Add(mech);
                }
            }

            return workers;
        }
    }
}
