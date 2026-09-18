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
    /// called below in vanilla's own order: four about access in <see cref="CanAccess"/>, and the
    /// fifth, the ideoligion's permission, in <see cref="IdeoligionAllows"/>. The skill block is the
    /// one this mod bypasses on purpose, and the whole trailing <c>t.def.IsBlueprint || t.def.IsFrame</c>
    /// block, the attachment test and the two history events, is dead code for a building that already
    /// exists. So this is the same five questions asked of the same five methods, just not routed
    /// through the one function other mods patch on the assumption that its argument is something
    /// being built.
    /// </para>
    /// <para>
    /// The five are split in two because the work giver needs to ask them at two different points.
    /// Permission has to come before any materials are hauled, or a colonist whose ideoligion forbids
    /// the building carries the whole cost to something it can never work on. Access has to come
    /// after, because the four access checks write no reason of their own: for a building the pawn
    /// cannot reach or work at, the readings players see ("No path", "Missing") come from the haul
    /// branch answering first. The job driver asks all five together through
    /// <see cref="CanWorkOn"/>, in vanilla's order.
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
        /// Determines whether a worker can start or continue improvement work on a building: every
        /// access check and the ideoligion's permission, in vanilla's order.
        /// </summary>
        /// <param name="target">The building being improved. Must be spawned.</param>
        /// <param name="worker">The pawn that would do the work.</param>
        /// <param name="forced">Whether the player is prioritising this by hand.</param>
        /// <returns><c>true</c> when nothing about the building or the pawn's access to it refuses the work.</returns>
        /// <remarks>
        /// <para>
        /// This is the job driver's re-check, run on every tick of the work. It asks exactly what the
        /// work giver asks, both halves and nothing else, so that it cannot abandon on the first tick
        /// a job the giver has just handed out, and cannot keep a pawn working on something the giver
        /// would now refuse. The work giver does not call it: it asks the same two halves itself,
        /// permission before the haul and access after it, which is the one place the order is
        /// visible to a player.
        /// </para>
        /// <para>
        /// Access comes first here because that is vanilla's order, and here the order is not visible
        /// to anyone: nothing reads a fail reason set inside a job driver's fail condition.
        /// </para>
        /// </remarks>
        public static bool CanWorkOn(Thing target, Pawn worker, bool forced)
        {
            return CanAccess(target, worker, forced) && IdeoligionAllows(target, worker);
        }

        /// <summary>
        /// Determines whether a worker can get to a building and work on it where it stands.
        /// </summary>
        /// <param name="target">The building being improved. Must be spawned.</param>
        /// <param name="worker">The pawn that would do the work.</param>
        /// <param name="forced">Whether the player is prioritising this by hand.</param>
        /// <returns><c>true</c> when nothing blocks, hides, holds or burns the building for this pawn.</returns>
        /// <remarks>
        /// <para>
        /// The first four of <c>CanConstruct</c>'s five reachable checks, in vanilla's order, and none
        /// of them sets a <c>JobFailReason</c>. That is vanilla's behaviour rather than an omission,
        /// and matching it is what keeps this change invisible to every player who does not have a
        /// mod patching <c>CanConstruct</c>.
        /// </para>
        /// <para>
        /// Because none of them sets a reason, where the work giver asks them decides what the player
        /// is told. It asks them after the haul branch and after the work type test, so a building
        /// the pawn cannot reach still reads "No path" (the haul job is returned, and the float menu
        /// provider finds the building unreachable) or "Missing" rather than nothing at all.
        /// </para>
        /// </remarks>
        public static bool CanAccess(Thing target, Pawn worker, bool forced)
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
            //
            // IdeoligionAllows needs no such guard, which is what lets the work giver ask it first.
            // MembersCanBuild opens `thing.def.entityDefToBuild ?? thing.def` and never touches a
            // blueprint.
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

            return true;
        }

        /// <summary>
        /// Determines whether the worker's ideoligion lets its members work on a building, naming the
        /// ideoligions that would when it does not.
        /// </summary>
        /// <param name="target">The building being improved.</param>
        /// <param name="worker">The pawn that would do the work.</param>
        /// <returns><c>true</c> when the worker has no ideoligion or its ideoligion allows the building.</returns>
        /// <remarks>
        /// <para>
        /// The one check of the five that is about permission rather than access, and the only one
        /// that sets a <c>JobFailReason</c>. It is kept rather than dropped because it is the one
        /// restriction a player's ideoligion imposes, and it is asked on its own because it is the
        /// one refusal that can never clear: a blocker moves and a path opens, but a pawn whose
        /// ideoligion forbids a pew will never improve that pew. The work giver therefore asks it
        /// before anything is hauled, which is also where vanilla's own delivery givers ask it,
        /// inside the <c>CanConstruct</c> call they make before offering a delivery.
        /// </para>
        /// <para>
        /// Asking it first does change one thing a player can see, and it is the better answer. A
        /// building that is both forbidden and blocked, or both forbidden and out of reach, used to
        /// report nothing, or "No path", or offered a haul; it now reports which ideoligions could
        /// build it. The permanent reason wins over the passing one.
        /// </para>
        /// </remarks>
        public static bool IdeoligionAllows(Thing target, Pawn worker)
        {
            // Ideo is null for anything not humanlike, so a colony mech reaches this with no
            // ideoligion at all and the guard is load bearing rather than tidy:
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
