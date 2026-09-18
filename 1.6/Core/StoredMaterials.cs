namespace SimpleImprove.Core
{
    /// <summary>
    /// What to do with the materials staged inside a building that is being despawned or unmarked.
    /// </summary>
    public enum MaterialReturn
    {
        /// <summary>
        /// Leave the materials where they are, inside the component's container.
        /// </summary>
        Keep,

        /// <summary>
        /// Drop the materials onto the map at the building's position.
        /// </summary>
        DropOnMap
    }

    /// <summary>
    /// Decides when the materials hauled towards an improvement are returned to the colony.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These two functions are the whole of the decision. Both call sites that act on them,
    /// <see cref="SimpleImproveComp.PostDeSpawn"/> and
    /// <see cref="SimpleImproveComp.ReturnStoredMaterialsWhileSpawned"/>, need a spawned
    /// <c>Thing</c> on a <c>Map</c> and so cannot be executed in the test harness. The readings they
    /// decide over are booleans and can be.
    /// </para>
    /// <para>
    /// Naming the decision is worth the file because the mod used to have three places that
    /// disagreed about who returns the materials and when. The designation-cancel prefix dropped
    /// into a map that was already null, which is one red error per stack on every deconstruct of a
    /// marked building. The <c>IsMarkedForImprovement</c> setter dropped into a possibly-null map.
    /// <c>PostDestroy</c> dropped correctly, but only under <c>DestroyMode.Deconstruct</c>, so every
    /// other way of destroying a building ate the materials. And a fourth case, despawning without
    /// destroying, had no owner at all and left the materials inside a component that nothing on the
    /// map could see.
    /// </para>
    /// </remarks>
    public static class StoredMaterials
    {
        /// <summary>
        /// Decides what to do when the building leaves the map.
        /// </summary>
        /// <param name="holdsMaterials">Whether the container currently holds anything.</param>
        /// <param name="haveMap">Whether a map was handed to the despawn hook.</param>
        /// <param name="beingTransportedOnGravship">
        /// <c>Verse.Thing.BeingTransportedOnGravship</c> for the building being despawned.
        /// </param>
        /// <returns>Whether to drop the materials onto the map being left.</returns>
        /// <remarks>
        /// <para>
        /// This is the single owner. <c>Verse.Thing.Destroy</c> reaches
        /// <c>if (Spawned) DeSpawn(mode);</c> before <c>RemoveAllReservationsAndDesignationsOnThis</c>
        /// and before the comps' <c>PostDestroy</c> loop, so the despawn hook runs first on the
        /// destroy path as well as on a plain despawn. <c>MinifyUtility.MakeMinified</c> likewise
        /// calls <c>DeSpawnOrDeselect</c> as its first statement, eight lines before it assigns
        /// <c>MinifiedThing.InnerThing</c>, and it is that assignment which reaches
        /// <c>ThingOwner.NotifyAdded</c> and its <c>RemoveAllDesignationsOn</c>. So on every path the
        /// container is already empty by the time anything else could try to drop it, which is what
        /// lets the other sites stop trying.
        /// </para>
        /// <para>
        /// The drop itself is the standard vanilla idiom rather than an invention. Seven shipped
        /// components do the same call from the same hook, among them <c>CompThingContainer</c>,
        /// <c>CompTransporter</c>, <c>CompGenepackContainer</c> and <c>CompBiosculpterPod</c>. The
        /// cell is free by then: <c>Thing.DeSpawn</c> has deregistered the building from the thing
        /// grid before the comps are notified.
        /// </para>
        /// <para>
        /// <strong>The guard here is the gravship itself, not the mode that gravships happen to
        /// use, and that is deliberate.</strong> All seven vanilla comps gate their release on
        /// <c>mode != DestroyMode.WillReplace</c>. That mode means two unrelated things: an Odyssey
        /// gravship is taking the building away, and something is being built in the building's
        /// place, which is how <c>GenSpawn</c> wipes what it is about to cover and how
        /// <c>SmoothableWallUtility</c> swaps a wall for its smoothed version. For a container of
        /// someone else's belongings, refusing to release is right in both. For materials the colony
        /// staged against one specific building it is right only in the first. A marked wall that a
        /// pawn smooths, or a marked building minified out of the way to make room, is gone for
        /// good, and its materials would go with it: destroyed with the old thing, or sealed inside
        /// a minified building whose mark has just been cleared by that same
        /// <c>RemoveAllDesignationsOn</c>, which is the stranding this fix exists to end.
        /// </para>
        /// <para>
        /// Vanilla does not treat the mode as a sufficient gravship test either, which is the
        /// precedent worth knowing about. <c>CompTransporter.PostDeSpawn</c> is the one holding
        /// things the colony put there on purpose, and it opens with an early
        /// <c>if (parent.BeingTransportedOnGravship || ...) return;</c> before it reaches its own
        /// <c>mode != DestroyMode.WillReplace</c> drop. It reads the flag for the same reason this
        /// does, and it is the closest vanilla analogue to a building holding a half-delivered load.
        /// </para>
        /// <para>
        /// The flag is reliable here. <c>Thing.PreSwapMap</c> sets
        /// <c>beingTransportedOnGravship</c>, and <c>GravshipUtility.GenerateGravship</c> runs it
        /// over every thing in a loop that completes before the despawn loop starts, so it is true
        /// during the jump and nothing else sets it. Keeping the materials is also the only correct
        /// answer: the building arrives on the far side with this component and its contents intact,
        /// so a drop would scatter them across a planet the colony has just left.
        /// </para>
        /// <para>
        /// The map test is defensive rather than reachable from vanilla. <c>ThingWithComps.DeSpawn</c>
        /// runs the comps loop whether or not <c>base.DeSpawn</c> did anything, so despawning an
        /// already-despawned building, which only another mod or a patch can arrange, hands this a
        /// null. Vanilla's seven do not test it and let <c>GenDrop</c> absorb it at the cost of a red
        /// error per stack, which is the exact failure this commit is removing from somewhere else.
        /// </para>
        /// </remarks>
        public static MaterialReturn OnDeSpawn(bool holdsMaterials, bool haveMap, bool beingTransportedOnGravship)
        {
            return holdsMaterials && haveMap && !beingTransportedOnGravship
                ? MaterialReturn.DropOnMap
                : MaterialReturn.Keep;
        }

        /// <summary>
        /// Decides what to do when the mark is cleared while the building is still standing.
        /// </summary>
        /// <param name="holdsMaterials">Whether the container currently holds anything.</param>
        /// <param name="haveMap">Whether the building still has a map to drop onto.</param>
        /// <returns>Whether to drop the materials where the building stands.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="OnDeSpawn"/> covers everything that leaves the map, but a plain cancel does not
        /// leave the map. Vanilla's <c>Designator_Cancel</c> on a marked building goes straight to
        /// <c>RemoveAllDesignationsOn</c> with no despawn anywhere in the path, and so do the mod's
        /// own cancel gizmo and cancel designator. Nothing else would return the materials in those
        /// cases, so this stays.
        /// </para>
        /// <para>
        /// The map test is what stops the red error this replaces. The designation-cancel prefix is
        /// reached with a despawned target from four separate directions: any <c>Thing.Destroy</c> of
        /// a marked building, because <c>RemoveAllReservationsAndDesignationsOnThis</c> runs for every
        /// <c>DestroyMode</c> and runs after the despawn; the pawn-executed uninstall through
        /// <c>JobDriver_RemoveBuilding</c>, which removes designations after <c>FinishedRemoving</c>
        /// has despawned; minification, through <c>ThingOwner.NotifyAdded</c>; and map removal,
        /// through <c>Thing.Notify_MyMapRemoved</c>. On all four <see cref="OnDeSpawn"/> has already
        /// returned the materials, so <paramref name="holdsMaterials"/> is false as well, but the map
        /// test is the one that is true by construction rather than by argument.
        /// </para>
        /// <para>
        /// The <c>IsMarkedForImprovement</c> setter needs the same guard for a reason of its own.
        /// Both quality float menus build their options as closures over a captured component and
        /// revalidate nothing when clicked, and the game ticks while a float menu is open, so a
        /// building destroyed between opening the menu and clicking an option reaches the setter with
        /// no map.
        /// </para>
        /// </remarks>
        public static MaterialReturn OnUnmark(bool holdsMaterials, bool haveMap)
        {
            return holdsMaterials && haveMap
                ? MaterialReturn.DropOnMap
                : MaterialReturn.Keep;
        }
    }
}
