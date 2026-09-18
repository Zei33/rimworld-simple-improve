using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace SimpleImprove.Core
{
    /// <summary>
    /// Decides whether a pawn can work on a building that is already standing, which is the question
    /// <c>GenConstruct.CanConstruct</c> was being asked and is not designed to answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because of the argument shape rather than the answer. Every one of vanilla's seven
    /// <c>CanConstruct</c> call sites passes a <c>Blueprint</c> or a <c>Frame</c>, the only two
    /// implementors of <c>IConstructible</c>, and both always carry a non-null
    /// <c>def.entityDefToBuild</c>. This mod passed a completed <c>Building</c>, which carries none.
    /// A third-party postfix that reads <c>t.def.entityDefToBuild</c> is reading a field every real
    /// caller fills in, so it throws here and nowhere else, and the exception surfaces as the whole
    /// improve work giver dying rather than as a fault in the patch. Two players reported exactly
    /// that a month apart, on 18 September and 19 October 2025, with the same third-party mod in the
    /// stack and the same postfix named in both traces.
    /// </para>
    /// <para>
    /// A null <c>jobForReservation</c> is <em>not</em> the unusual part, which matters because it
    /// rules out the cheap fix. Two of the seven vanilla call sites pass null,
    /// <c>JobDriver_ConstructFinishFrame</c> and <c>WorkGiver_ConstructFinishFrames</c>, so a postfix
    /// that failed on a null there would break vanilla construction too. Passing a job def would have
    /// changed the shape without touching what differs.
    /// </para>
    /// <para>
    /// Nothing here is reimplemented. With <c>checkSkills: false</c> and a completed building, exactly
    /// five of <c>CanConstruct</c>'s checks are reachable, and all five are public vanilla methods
    /// called below in vanilla's own order. The skill block is the one this mod bypasses on purpose,
    /// and the whole trailing <c>t.def.IsBlueprint || t.def.IsFrame</c> block, the attachment test and
    /// the two history events, is dead code for a building that already exists. So this is the same
    /// five questions asked of the same five methods, just not routed through the one function other
    /// mods patch on the assumption that its argument is something being built.
    /// </para>
    /// <para>
    /// What it gives up is real and was the actual decision: a third-party postfix that adds a
    /// legitimate construction restriction no longer applies to improvement. That is defensible
    /// because such a postfix is answering "may this pawn bring this into existence", and improvement
    /// is asked about something that already exists and already stands in the colony. The one
    /// restriction that is wanted, Ideology's, is kept by calling it directly.
    /// </para>
    /// <para>
    /// Most of those postfixes were never answering for a completed building anyway, but not all, and
    /// the exception is worth naming rather than generalising away. Of the postfixes on this method
    /// that could be inspected on 2026-09-18, Vanilla Expanded Framework's three all bail on a null
    /// <c>entityDefToBuild</c>, Alpha Genes' compares it against a def and so is false for the very
    /// building it exists to gate, and the one in both player reports throws. Humanoid Alien Races is
    /// the exception: its postfix is
    /// <c>RaceRestrictionSettings.CanBuild(t.def.entityDefToBuild ?? t.def, p.def)</c>, which gives a
    /// correct answer for a finished building, so a race forbidden to construct something was until
    /// now also forbidden to improve it and no longer is. That is a real loss for those players, it
    /// is invisible in game, and it belongs in the release notes.
    /// </para>
    /// </remarks>
    public static class ImproveSite
    {
        /// <summary>
        /// Determines whether a worker can start or continue improvement work on a building.
        /// </summary>
        /// <param name="target">The building being improved. Must be spawned.</param>
        /// <param name="worker">The pawn that would do the work.</param>
        /// <param name="forced">Whether the player is prioritising this by hand.</param>
        /// <returns><c>true</c> when nothing about the building or the pawn's access to it refuses the work.</returns>
        /// <remarks>
        /// <para>
        /// The order is vanilla's and is kept deliberately, because it is visible to the player. Only
        /// the Ideology branch sets a <c>JobFailReason</c>, so the order decides whether a building
        /// that is both blocked and forbidden reports "only members of X can build" or reports
        /// nothing. Reordering to put the two cheap tests first would be faster and would change that.
        /// </para>
        /// <para>
        /// Only the Ideology branch sets a reason, so the other four set none here either. That is
        /// vanilla's behaviour rather than an omission, and matching it is what keeps this change
        /// invisible to every player who does not have a mod patching <c>CanConstruct</c>.
        /// </para>
        /// </remarks>
        public static bool CanWorkOn(Thing target, Pawn worker, bool forced)
        {
            // Not a defensive habit: without it the very next call throws inside vanilla.
            // FirstBlockingThing reaches BlocksConstruction for every other thing sharing a cell with
            // the target, and that opens `ThingDef thingDef = BlueprintDefOf(constructible);` before
            // dereferencing `thingDef.entityDefToBuild`. BlueprintDefOf returns `def.blueprintDef` for
            // anything that is neither a blueprint nor a frame, so a building whose def has no
            // blueprint hands it null. The `t == constructible` early return means the building
            // meeting itself is safe; anything else in the cell is not, including the worker pawn,
            // because BlocksConstruction is evaluated before the `!= pawnToIgnore` test. A plant with
            // low harvest work is the only occupant that returns before the dereference.
            //
            // Nothing in vanilla holds that shut. What holds it shut is this mod: ImprovableDefs
            // .Qualifies refuses a def with no blueprintDef, and both marking paths re-check it
            // (SimpleImproveComp.CompGetGizmosExtra and the selection filter beside it). None of
            // those covers a third party declaring SimpleImproveComp on such a def in XML and marking
            // it through the public setter, which is the one route left. Refusing here is cheaper
            // than relying on three guards in other files staying correct.
            if (target.def.blueprintDef == null)
            {
                return false;
            }

            if (GenConstruct.FirstBlockingThing(target, worker) != null)
            {
                return false;
            }

            if (!GenConstruct.CanTouchTargetFromValidCell(target, worker))
            {
                return false;
            }

            // The null-jobForReservation arm of CanConstruct, which is the arm this mod was already
            // taking. forced widens the danger threshold and lets the pawn take a reservation another
            // pawn holds, exactly as it did before.
            if (!worker.CanReserveAndReach(
                    target,
                    PathEndMode.Touch,
                    forced ? Danger.Deadly : worker.NormalMaxDanger(),
                    1,
                    -1,
                    null,
                    forced))
            {
                return false;
            }

            if (target.IsBurning())
            {
                return false;
            }

            // Kept rather than dropped, and it is the one restriction here that is about permission
            // rather than access. Ideo is null for anything not humanlike, so a colony mech reaches
            // this with no ideoligion at all and the guard is load bearing rather than tidy:
            // PawnComponentsUtility builds the tracker only inside `if (pawn.RaceProps.Humanlike)`,
            // and every mechanoid ships ToolUser.
            //
            // MembersCanBuild is safe with a completed building where the rest of the construction
            // system is not. It opens `thing.def.entityDefToBuild ?? thing.def`, so vanilla did think
            // about a thing that is not being built when it wrote this one.
            if (worker.Ideo != null && !worker.Ideo.MembersCanBuild(target))
            {
                // A local list rather than vanilla's shared static buffer. Vanilla clears
                // tmpIdeoMemberNames on entry to CanConstruct and leaves it populated on exit; a list
                // built inside the branch that uses it cannot be read by anything else, and this
                // branch ends the job anyway, so the allocation is on a path that is already over.
                var memberNames = new List<string>();

                foreach (Ideo ideo in Find.IdeoManager.IdeosListForReading)
                {
                    if (ideo.MembersCanBuild(target))
                    {
                        memberNames.Add(ideo.memberName);
                    }
                }

                // Vanilla writes this as .Any() on the same list. No ideoligion being able to build
                // it means no useful reason to give, and saying nothing is what vanilla does.
                if (memberNames.Count > 0)
                {
                    JobFailReason.Is("OnlyMembersCanBuild".Translate(memberNames.ToCommaList(useAnd: true)));
                }

                return false;
            }

            return true;
        }
    }
}
