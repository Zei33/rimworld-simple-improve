using System.Collections.Generic;
using Verse;

namespace SimpleImprove.Core
{
    /// <summary>
    /// What has to happen to bring a building's improvement designation back into agreement with the
    /// flag stored on its component.
    /// </summary>
    public enum ImproveDesignationRepair
    {
        /// <summary>The two agree, or the disagreement is one this mod deliberately leaves alone.</summary>
        None,

        /// <summary>The component says marked and this map carries no designation, so one is missing.</summary>
        AddDesignation,
    }

    /// <summary>
    /// The improvement designation as a record of what is marked, rather than as an icon.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Marking is stored twice: as <c>isMarkedForImprovement</c> on the component, and as a
    /// <c>Designation_Improve</c> in the map's <c>DesignationManager</c>. Until the work giver was
    /// made designation-driven only the first mattered to whether work happened, and the second was
    /// the icon. Now the designation decides whether the giver runs at all, so the two disagreeing
    /// is the difference between a building being improved and being silently ignored.
    /// </para>
    /// <para>
    /// Both members here take plain values rather than reading the game, because everything they
    /// decide over (<c>Thing.Spawned</c>, <c>Thing.Map</c>, <c>DesignationManager</c>) needs a live
    /// map. <see cref="ScanTargets"/> is the exception that proves it: a <c>Designation</c> over a
    /// <c>Thing</c> can be built outside the game, so that projection is exercised directly.
    /// </para>
    /// </remarks>
    public static class ImproveDesignations
    {
        /// <summary>
        /// Decides whether a building's designation needs restoring as it spawns.
        /// </summary>
        /// <param name="markedInComponent">The flag stored on the building's component.</param>
        /// <param name="designationOnThisMap">Whether this map already carries the designation for it.</param>
        /// <returns>The repair to apply, or <see cref="ImproveDesignationRepair.None"/>.</returns>
        /// <remarks>
        /// <para>
        /// The designation does not follow a building to another map, and nothing in the game puts it
        /// back. <c>Thing.DeSpawn</c> never touches the designation manager, and
        /// <c>Building.DeSpawn</c> only asks <c>Notify_BuildingDespawned</c> to clear designations
        /// whose def sets <c>removeIfBuildingDespawned</c>, which vanilla sets on Plan, Mine and
        /// MineVein alone. So the designation stays behind in the old map's manager, keyed by the
        /// same <c>Thing</c>, while the component and its flag travel with the building.
        /// </para>
        /// <para>
        /// One thing does that: an Odyssey gravship jump. <c>GravshipUtility.GenerateGravship</c>
        /// despawns every gravship building and only then sweeps the substructure cells, and the sweep
        /// finds things through <c>thingGrid.ThingsListAt</c>, which the despawn has already emptied,
        /// so the designation is never swept. The <c>Gravship</c> itself holds its things in a plain
        /// <c>Dictionary</c> rather than a <c>ThingOwner</c>, and carries only the three floor
        /// designations across, so nothing restores it on the far side either.
        /// </para>
        /// <para>
        /// Minifying is <em>not</em> a second case, which is worth stating because it looks like one.
        /// <c>MinifyUtility.MakeMinified</c> puts the building in the <c>MinifiedThing</c>'s
        /// <c>ThingOwner</c>, and <c>ThingOwner.NotifyAdded</c> calls
        /// <c>RemoveAllDesignationsOn</c> on <em>every</em> map whenever the holder is an enclosing
        /// container, which a <c>MinifiedThing</c> is (<c>IsEnclosingContainer</c> excludes only
        /// carry trackers, corpses, maps, caravans, trader trackers and trade ships). That goes
        /// through <c>RemoveDesignation</c>, so it fires <c>Notify_Removing</c>, which this mod
        /// prefixes to clear the flag too. Uninstalling a marked building therefore loses the mark on
        /// both sides and stays consistent. That is pre-existing behaviour and not something this
        /// repair should undo: the player uninstalled it.
        /// </para>
        /// <para>
        /// Left alone that would be a regression rather than an existing bug: the old work giver read
        /// the component flag, so improvement carried on after a jump with only the icon missing.
        /// Restoring the designation on spawn also repairs saves that already carry the divergence,
        /// because the designation manager is scribed and indexed before any thing is respawned.
        /// </para>
        /// <para>
        /// The reverse, a designation with the flag clear, is deliberately not repaired here. It needs
        /// two moves between maps to reach, it costs a stray icon and a one-element scan rather than
        /// lost work, and removing a designation fires <c>Notify_Removing</c>, which this mod prefixes
        /// to drop staged materials. Spawning items from inside another thing's <c>SpawnSetup</c> is
        /// more risk than that case is worth. The return type is an enum rather than a bool so adding
        /// that case later is an extra branch rather than a reinterpretation of this one.
        /// </para>
        /// </remarks>
        public static ImproveDesignationRepair RepairNeeded(bool markedInComponent, bool designationOnThisMap)
        {
            return markedInComponent && !designationOnThisMap
                ? ImproveDesignationRepair.AddDesignation
                : ImproveDesignationRepair.None;
        }

        /// <summary>
        /// Turns the map's improvement designations into the set the work giver scans.
        /// </summary>
        /// <param name="designations">The map's designations of the improvement def.</param>
        /// <returns>The buildings to scan, as a list.</returns>
        /// <remarks>
        /// <para>
        /// A <c>List</c> rather than the <c>yield return</c> iterator the eleven vanilla work givers
        /// use, for two reasons that both come from how the set is consumed.
        /// </para>
        /// <para>
        /// <c>GenClosest.ClosestThing_Global</c> takes the set as a non-generic <c>IEnumerable</c> and
        /// then type-tests for four typed lists, <c>IList&lt;Thing&gt;</c> among them, to get a count
        /// and an indexed loop. A <c>List&lt;Thing&gt;</c> takes that path. A lazy iterator does not,
        /// and neither would a <c>HashSet&lt;Thing&gt;</c>, which implements no non-generic
        /// <c>ICollection</c> and no <c>IList</c> at all.
        /// </para>
        /// <para>
        /// Second, the source is <c>DesignationManager.SpawnedDesignationsOfDef</c>, which yields over
        /// the live list for that def, so a lazy projection would hold an enumerator open across the
        /// whole scan and throw if anything added or removed a designation meanwhile. Nothing
        /// currently does: the only two removal sites in this mod are the <c>IsMarkedForImprovement</c>
        /// setter and <c>ClearImprovementAndFinish</c>, and neither is reachable from the scan
        /// validator, which only reads. So this forecloses a class of hazard rather than fixing a live
        /// one, and it costs one list. Worth saying plainly, because the reverse claim (that the
        /// validator can cancel an improvement mid-scan) was written here first and is not true.
        /// </para>
        /// <para>
        /// The null test is not defensive clutter. <c>SpawnedDesignationsOfDef</c> admits any
        /// designation of the def whose target is <em>not</em> a thing (its filter is
        /// <c>!target.HasThing || target.Thing.Map == map</c>), so a cell-targeted one would arrive
        /// here with a null <c>Thing</c>. A null in the search set is not caught anywhere useful:
        /// <c>JobGiver_Work</c> wraps the scan in a <c>try</c> that calls <c>Log.Error</c>, not
        /// <c>Log.ErrorOnce</c>, so it would repeat for every pawn on every job search.
        /// </para>
        /// <para>
        /// Nothing else is filtered here. The designation is the definition of what is marked, and the
        /// set the work giver scans is also what the right-click menu tests against, so narrowing it
        /// would silently remove options the player can currently use.
        /// </para>
        /// </remarks>
        public static List<Thing> ScanTargets(IEnumerable<Designation> designations)
        {
            var targets = new List<Thing>();
            if (designations == null)
            {
                return targets;
            }

            foreach (var designation in designations)
            {
                var thing = designation?.target.Thing;
                if (thing != null)
                {
                    targets.Add(thing);
                }
            }

            return targets;
        }
    }
}
