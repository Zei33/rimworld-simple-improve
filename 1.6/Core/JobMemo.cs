using System;
using System.Collections.Generic;

namespace SimpleImprove.Core
{
    /// <summary>
    /// Holds the jobs a work giver has decided on and not yet handed out, for one pawn, one
    /// <c>forced</c> value and one game tick at a time, and decides when to build a new one.
    /// </summary>
    /// <typeparam name="TAsker">Who is asking. <c>Pawn</c> in the game.</typeparam>
    /// <typeparam name="TSubject">What a decision is about. <c>Thing</c> in the game.</typeparam>
    /// <typeparam name="TAnswer">The decision. <c>Job</c> in the game.</typeparam>
    /// <remarks>
    /// <para>
    /// It exists so that <c>WorkGiver_Improve.HasJobOnThing</c> and <c>JobOnThing</c> answer from one
    /// computation. <c>JobGiver_Work</c> uses the first as its scan validator and calls the second on
    /// the winner with nothing in between that could re-check it, so the two must agree, and the way
    /// they are made to agree is that the second is handed exactly what the first built.
    /// </para>
    /// <para>
    /// Everything that decides that hand-over lives here rather than in the work giver, so that it
    /// can be run outside the game: when to forget, what to hold, what to hand out, and when to
    /// build. <see cref="Answer"/> is the whole of it and is the one method the work giver calls. The
    /// work giver itself cannot be run: it reads <c>Find.TickManager</c> and needs a spawned pawn. An
    /// earlier version kept the take-or-build step in the work giver, and two one-word edits there
    /// (keeping a null in place of the job just built, or ignoring what the take handed back) passed
    /// the whole suite while bringing back issue #5's double evaluation.
    /// </para>
    /// <para>
    /// The class is generic only so that a test can hold it over plain objects. The game uses it over
    /// <c>Pawn</c>, <c>Thing</c> and <c>Job</c>, and the method bodies are the same bodies either way;
    /// the one thing a test does not share with the game is how subjects compare, since <c>Thing</c>
    /// implements <c>IEquatable&lt;Thing&gt;</c> on its ID and def while a test object compares by
    /// reference.
    /// </para>
    /// </remarks>
    public sealed class JobMemo<TAsker, TSubject, TAnswer>
        where TAsker : class
        where TAnswer : class
    {
        /// <summary>
        /// Decisions waiting to be handed out, by subject, under the current key.
        /// </summary>
        private readonly Dictionary<TSubject, TAnswer> waiting = new Dictionary<TSubject, TAnswer>();

        /// <summary>
        /// Builds a decision when none is held, for the asker and <c>forced</c> value of the current key.
        /// </summary>
        private readonly Func<TAsker, TSubject, bool, TAnswer> build;

        /// <summary>
        /// Drops whatever state the owner keeps under the same key, called whenever the key changes.
        /// </summary>
        private readonly Action forget;

        /// <summary>The pawn the held decisions were made for, or <c>null</c> before the first key.</summary>
        private TAsker asker;

        /// <summary>The <c>forced</c> value the held decisions were made under.</summary>
        private bool forced;

        /// <summary>The game tick the held decisions were made on, or -1 before the first key.</summary>
        private int tick = -1;

        /// <summary>
        /// Initializes a new instance of the <see cref="JobMemo{TAsker, TSubject, TAnswer}"/> class.
        /// </summary>
        /// <param name="build">
        /// Builds a decision from scratch. It is handed the asker and <c>forced</c> value the memo is
        /// keyed on, never ones a caller passes separately, so a decision cannot be built for one key
        /// and held under another.
        /// </param>
        /// <param name="forget">
        /// Drops the owner's own state kept under the same key. Called on every key change, before
        /// anything is built under the new key.
        /// </param>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        public JobMemo(Func<TAsker, TSubject, bool, TAnswer> build, Action forget)
        {
            this.build = build ?? throw new ArgumentNullException(nameof(build));
            this.forget = forget ?? throw new ArgumentNullException(nameof(forget));
        }

        /// <summary>
        /// Answers the job question for one subject: the decision held for it under this key, or a
        /// new one built now.
        /// </summary>
        /// <param name="asker">The pawn asking. Compared by reference.</param>
        /// <param name="forced">Whether the player is prioritising this by hand.</param>
        /// <param name="tick">The current game tick.</param>
        /// <param name="subject">The subject asked about.</param>
        /// <returns>The decision, or <c>null</c> when there is no job.</returns>
        /// <remarks>
        /// <para>
        /// This is the hand-over the memo exists for. The call that builds a job holds it, and the
        /// next call about the same subject under the same key takes it instead of building again.
        /// For the subject a caller goes on to use, that next call comes straight away:
        /// <c>JobGiver_Work</c> asks <c>JobOnThing</c> about the winner as soon as its scan picks one,
        /// and <c>FloatMenuOptionProvider_WorkGivers</c> asks it in the same expression as
        /// <c>HasJobOnThing</c>. Jobs built for candidates that lost are never handed to anybody, so
        /// they are never pooled, and they fall away at the next key change.
        /// </para>
        /// <para>
        /// A job that is handed out is never stale in a way anybody sees, because it is taken by the
        /// call straight after the one that built it. That call does not build, so it writes no
        /// <c>JobFailReason</c>, and that is correct: a take only ever serves a job, and a job
        /// carries no reason to write. A refusal is never held (see <see cref="Keep"/>), so every
        /// refusal is built by the call that returns it and writes its own reason.
        /// </para>
        /// </remarks>
        public TAnswer Answer(TAsker asker, bool forced, int tick, TSubject subject)
        {
            if (Rekey(asker, forced, tick))
            {
                forget();
            }

            if (TryTake(subject, out TAnswer held))
            {
                return held;
            }

            TAnswer built = build(this.asker, subject, this.forced);
            Keep(subject, built);
            return built;
        }

        /// <summary>
        /// Points the memo at one pawn, <c>forced</c> value and tick, forgetting everything it held
        /// if any of the three differs from the last call.
        /// </summary>
        /// <param name="asker">The pawn asking. Compared by reference.</param>
        /// <param name="forced">Whether the player is prioritising this by hand.</param>
        /// <param name="tick">The current game tick.</param>
        /// <returns>
        /// <c>true</c> when the key changed and the memo forgot, which is when <see cref="Answer"/>
        /// tells the owner to drop its own state under the same key.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The pawn is part of the key because the object that owns this memo is shared by every pawn
        /// in the game: <c>WorkGiverDef.Worker</c> constructs one <c>WorkGiver</c> per def and caches it
        /// in an <c>[Unsaved]</c> field. It is compared by reference, which is what the work giver's
        /// own inequality test on two <c>Pawn</c> values did, since <c>Pawn</c> declares no operator.
        /// </para>
        /// <para>
        /// The tick bounds how long a decision can be held. It does not make one true while held,
        /// because the tick does not move while the game is paused, and the float menu, the one
        /// caller that passes <c>forced: true</c>, runs exactly then. That is why
        /// <see cref="Keep"/> refuses the one kind of decision whose staleness a player can see.
        /// </para>
        /// </remarks>
        public bool Rekey(TAsker asker, bool forced, int tick)
        {
            if (ReferenceEquals(this.asker, asker) && this.forced == forced && this.tick == tick)
            {
                return false;
            }

            waiting.Clear();
            this.asker = asker;
            this.forced = forced;
            this.tick = tick;
            return true;
        }

        /// <summary>
        /// Hands out the decision held for a subject, forgetting it in the same step.
        /// </summary>
        /// <param name="subject">The subject asked about.</param>
        /// <param name="answer">The held decision, or the type default when there is none.</param>
        /// <returns><c>true</c> when a decision was held and has now been handed out.</returns>
        /// <remarks>
        /// Forgetting on the way out is a correctness requirement, not tidiness. <c>JobMaker.MakeJob</c>
        /// hands out pooled <c>Job</c> objects from <c>SimplePool&lt;Job&gt;</c>, and
        /// <c>Pawn_JobTracker</c> returns them to that pool when it declines or finishes one. A job
        /// still reachable from here after it was handed out could be served a second time after the
        /// pool had given the same object to somebody else.
        /// </remarks>
        public bool TryTake(TSubject subject, out TAnswer answer)
        {
            if (!waiting.TryGetValue(subject, out answer))
            {
                return false;
            }

            waiting.Remove(subject);
            return true;
        }

        /// <summary>
        /// Holds a decision for the next ask about the same subject under the current key. A null
        /// decision, meaning "no job", is not held, and replaces anything that was.
        /// </summary>
        /// <param name="subject">The subject the decision is about.</param>
        /// <param name="answer">The decision, or <c>null</c> for none.</param>
        /// <remarks>
        /// <para>
        /// Refusing null is the fix for a regression the memo brought in when it was first written,
        /// before any release carried it. A refusal is only useful to the player with its reason,
        /// and the reason is not part of the decision: it is <c>JobFailReason</c>, a static the
        /// building of the decision writes as a side effect.
        /// <c>FloatMenuOptionProvider_WorkGivers</c> clears that static, then calls
        /// <c>HasJobOnThing</c>, and calls <c>JobOnThing</c> only if the answer was true. So a held
        /// null was never collected, and the next right-click on the same building in the same
        /// paused tick was handed it back without the reason being written. The provider reads a
        /// null job with no reason as nothing to say and drops the line, so a greyed "Cannot
        /// improve" entry could be missing from the next right-click, about one time in two
        /// depending on how often the open menu had re-asked, until the game ticked.
        /// </para>
        /// <para>
        /// Remembering the reason alongside the null was the other way out, and it is worse on both
        /// counts that matter. It copies vanilla state this mod does not own, which is three statics
        /// in <c>JobFailReason</c> today and would silently become an incomplete copy if a fourth
        /// were added. And it would replay a stale reason: while the game is paused the player can
        /// change the Work tab, the mod settings or a mark between two right-clicks, and a replayed
        /// reason describes the colony as it was. Building a refusal again is cheap: most return from
        /// an early test, and the one expensive part, the material search, has its own cache in the
        /// work giver.
        /// </para>
        /// <para>
        /// Nothing is lost by holding no nulls for the background scan either. <c>JobGiver_Work</c>
        /// only ever calls <c>JobOnThing</c> on a candidate its validator accepted, so a held null is
        /// read only if the same pawn scans twice in one tick, and then it is better rebuilt.
        /// </para>
        /// <para>
        /// A null drops whatever was held for its own subject and nothing else. <see cref="Answer"/>
        /// only keeps after a miss, so nothing is held for that subject at that point today, but the
        /// latest decision is the one that stands, and a job left behind by an older one would be
        /// handed out as if it were current. Every other subject's decision has to survive: the
        /// background scan validates a closer candidate after the eventual winner, and a refusal
        /// there that dropped the winner's job would send <c>JobOnThing</c> back to building it.
        /// </para>
        /// </remarks>
        public void Keep(TSubject subject, TAnswer answer)
        {
            if (answer == null)
            {
                waiting.Remove(subject);
                return;
            }

            waiting[subject] = answer;
        }
    }
}
