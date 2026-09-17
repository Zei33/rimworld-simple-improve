using RimWorld;
using Verse;

namespace SimpleImprove.Core
{
    /// <summary>
    /// Where a worker's Construction level came from.
    /// </summary>
    /// <remarks>
    /// Reported rather than inferred so a failing test names the branch that fired. The three values
    /// are exhaustive: a pawn either has a skill tracker, is a mechanoid judged on a fixed level, or
    /// cannot be judged at all.
    /// </remarks>
    public enum WorkerSkillSource
    {
        /// <summary>The pawn has neither a skill tracker nor a mechanoid's fixed level.</summary>
        None,

        /// <summary>Read from <c>Pawn.skills</c>, the ordinary humanlike case.</summary>
        SkillTracker,

        /// <summary>Read from <c>RaceProperties.mechFixedSkillLevel</c>.</summary>
        MechFixedLevel
    }

    /// <summary>
    /// Why a worker cannot be given improvement work, on skill grounds.
    /// </summary>
    /// <remarks>
    /// An enum rather than a bool so a failing test names the guard that fired, and so the two guards
    /// cannot be collapsed into one comparison. They are not interchangeable: one refuses a worker
    /// outright, the other refuses it for this particular target quality.
    /// </remarks>
    public enum ImproveSkillBlocker
    {
        /// <summary>Nothing on skill grounds stops this worker.</summary>
        None,

        /// <summary>The worker has no Construction level that can be read at all.</summary>
        NoConstructionSkill,

        /// <summary>The worker is readable but below the target quality's requirement.</summary>
        SkillTooLow
    }

    /// <summary>
    /// How skilled a worker is for the purposes of improving a building, lifted off the pawn so the
    /// rules that use it can be tested.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Pawn.skills</c> is null for every pawn whose race is not humanlike.
    /// <c>PawnComponentsUtility.CreateInitialComponents</c> assigns it only inside
    /// <c>if (pawn.RaceProps.Humanlike)</c>, and <c>Humanlike</c> is <c>intelligence >= Humanlike</c>,
    /// while every mechanoid ships <c>ToolUser</c>. So it is null on mechs, on animals, and on any
    /// modded drone race that is not humanlike.
    /// </para>
    /// <para>
    /// The branch order deliberately matches <c>QualityUtility.GenerateQualityCreatedByPawn</c>,
    /// which reads <c>pawn.RaceProps.IsMechanoid ? pawn.RaceProps.mechFixedSkillLevel :
    /// pawn.skills.GetSkill(relevantSkill).Level</c>. That is the call
    /// <see cref="SimpleImproveComp.CompleteImprovement"/> ends at, so the level this mod gates on has
    /// to be the same number the quality roll will use. Gating on
    /// <c>GenConstruct.CanConstruct</c>'s <c>IsColonyMech</c> instead would agree for colony mechs and
    /// disagree for anything else.
    /// </para>
    /// <para>
    /// <see cref="IsKnown"/> is the load-bearing part. <c>GenerateQualityCreatedByPawn</c> has no null
    /// guard on its non-mechanoid branch, so handing it a skill-less non-mechanoid pawn throws inside
    /// vanilla. Refusing the job while <see cref="IsKnown"/> is false is what keeps that unreachable,
    /// and it has to be checked whether or not a target quality is set.
    /// </para>
    /// </remarks>
    public readonly struct WorkerSkill
    {
        /// <summary>
        /// The level reported when the worker has no skill model at all.
        /// </summary>
        /// <remarks>
        /// Negative rather than zero on purpose. "No tracker" is not "skill 0": a pawn that cannot be
        /// scored must be refused, not merely held to the lowest requirement, because the quality roll
        /// at the end of the job would throw.
        /// </remarks>
        public const int NoLevel = -1;

        private readonly int level;

        private readonly WorkerSkillSource source;

        private WorkerSkill(int level, WorkerSkillSource source)
        {
            this.level = level;
            this.source = source;
        }

        /// <summary>
        /// Gets the Construction level the worker should be judged on, or <see cref="NoLevel"/>.
        /// </summary>
        public int Level => level;

        /// <summary>
        /// Gets where <see cref="Level"/> came from.
        /// </summary>
        public WorkerSkillSource Source => source;

        /// <summary>
        /// Gets whether the worker can be judged on a Construction level at all.
        /// </summary>
        public bool IsKnown => source != WorkerSkillSource.None;

        /// <summary>
        /// Determines whether the worker meets a skill requirement.
        /// </summary>
        /// <param name="requiredLevel">The Construction level the improvement asks for.</param>
        /// <returns><c>true</c> only if the worker can be judged and reaches the requirement.</returns>
        /// <remarks>
        /// An unknown skill fails every requirement, including a requirement of zero. That is what
        /// stops a skill-less non-mechanoid reaching the quality roll through the "any improvement"
        /// path, where no requirement is checked.
        /// </remarks>
        public bool Meets(int requiredLevel) => IsKnown && level >= requiredLevel;

        /// <summary>
        /// Decides whether a worker's skill stops it improving a building.
        /// </summary>
        /// <param name="skill">The worker's skill model.</param>
        /// <param name="requiredLevel">
        /// The Construction level the target quality asks for, or <c>null</c> when the building is
        /// marked for any improvement at all and no particular quality is being aimed at.
        /// </param>
        /// <returns>The first thing that stops the worker, or <see cref="ImproveSkillBlocker.None"/>.</returns>
        /// <remarks>
        /// The order is load bearing and is why this is a function rather than two inline tests. An
        /// unreadable skill is refused <em>before</em> and <em>independently of</em> the requirement,
        /// so a null requirement still refuses it. Testing the requirement first, or only when one
        /// exists, would let a skill-less non-mechanoid through the "any improvement" path and on into
        /// <c>QualityUtility.GenerateQualityCreatedByPawn</c>, which throws on exactly that pawn.
        /// </remarks>
        public static ImproveSkillBlocker FirstBlocker(WorkerSkill skill, int? requiredLevel)
        {
            if (!skill.IsKnown)
            {
                return ImproveSkillBlocker.NoConstructionSkill;
            }

            if (requiredLevel.HasValue && !skill.Meets(requiredLevel.Value))
            {
                return ImproveSkillBlocker.SkillTooLow;
            }

            return ImproveSkillBlocker.None;
        }

        /// <summary>
        /// Reads the skill model off a pawn.
        /// </summary>
        /// <param name="pawn">The pawn to read.</param>
        /// <returns>The worker's skill model.</returns>
        /// <remarks>
        /// The one place in the mod that touches <c>Pawn.skills</c>, <c>RaceProps.IsMechanoid</c> or
        /// <c>mechFixedSkillLevel</c>. None of those are reachable from the test project, so keeping
        /// them here and nowhere else is what makes everything below this line testable.
        /// </remarks>
        public static WorkerSkill Of(Pawn pawn)
        {
            if (pawn == null)
            {
                return Unknown();
            }

            // RaceProps is def.race, which is non-null for anything that is a Pawn at all, so it is
            // read bare. Nothing is decided here: the four readings go straight to From, which is
            // where the branch order lives and is the only part a test can see.
            return From(
                pawn.RaceProps.IsMechanoid,
                pawn.RaceProps.mechFixedSkillLevel,
                pawn.skills != null,
                pawn.skills != null ? pawn.skills.GetSkill(SkillDefOf.Construction).Level : 0);
        }

        /// <summary>
        /// Builds a skill model from readings already taken off a pawn.
        /// </summary>
        /// <param name="isMechanoid">The pawn's <c>RaceProps.IsMechanoid</c>.</param>
        /// <param name="mechFixedSkillLevel">The pawn's <c>RaceProps.mechFixedSkillLevel</c>.</param>
        /// <param name="hasSkillTracker">Whether <c>Pawn.skills</c> is non-null.</param>
        /// <param name="trackedConstructionLevel">
        /// The tracked Construction level, ignored when <paramref name="hasSkillTracker"/> is false.
        /// </param>
        /// <returns>The worker's skill model.</returns>
        /// <remarks>
        /// The mechanoid test comes first deliberately, and the order is the whole point of this being
        /// a separate method rather than four lines inside <see cref="Of"/>. It matches
        /// <c>QualityUtility.GenerateQualityCreatedByPawn</c>, which reads
        /// <c>pawn.RaceProps.IsMechanoid ? mechFixedSkillLevel : pawn.skills.GetSkill(...).Level</c>.
        /// Reversing it would judge a hypothetical mechanoid that also carried a skill tracker on the
        /// tracker, while the quality roll at the end of the job judged it on
        /// <c>mechFixedSkillLevel</c>, so the requirement the player configured and the quality they
        /// got would be computed from two different numbers.
        /// </remarks>
        public static WorkerSkill From(
            bool isMechanoid, int mechFixedSkillLevel, bool hasSkillTracker, int trackedConstructionLevel)
        {
            if (isMechanoid)
            {
                return OfMechanoid(mechFixedSkillLevel);
            }

            if (hasSkillTracker)
            {
                return OfSkillTracker(trackedConstructionLevel);
            }

            return Unknown();
        }

        /// <summary>
        /// Builds the skill model of a pawn with an ordinary skill tracker.
        /// </summary>
        /// <param name="constructionLevel">The pawn's Construction level.</param>
        /// <returns>The worker's skill model.</returns>
        public static WorkerSkill OfSkillTracker(int constructionLevel)
            => new WorkerSkill(constructionLevel, WorkerSkillSource.SkillTracker);

        /// <summary>
        /// Builds the skill model of a mechanoid, which has no tracker and one fixed level.
        /// </summary>
        /// <param name="mechFixedSkillLevel">The race's <c>mechFixedSkillLevel</c>.</param>
        /// <returns>The worker's skill model.</returns>
        public static WorkerSkill OfMechanoid(int mechFixedSkillLevel)
            => new WorkerSkill(mechFixedSkillLevel, WorkerSkillSource.MechFixedLevel);

        /// <summary>
        /// Builds the skill model of a pawn that cannot be judged on Construction at all.
        /// </summary>
        /// <returns>The worker's skill model.</returns>
        public static WorkerSkill Unknown() => new WorkerSkill(NoLevel, WorkerSkillSource.None);
    }
}
