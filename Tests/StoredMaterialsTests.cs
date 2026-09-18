using NUnit.Framework;
using SimpleImprove.Core;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Covers the two decisions that say when staged improvement materials come back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both call sites need a spawned <c>Thing</c> on a <c>Map</c> and neither can be executed here,
    /// which is exactly why the decisions were lifted out of them. What is covered is the decision;
    /// what is not is that the call sites still ask. Only an in-game check closes that half, and
    /// <c>Tests/README.md</c> says so rather than implying otherwise.
    /// </para>
    /// <para>
    /// Mutations these catch, measured rather than assumed: inverting or dropping the gravship test,
    /// dropping the map test from either function, dropping the "is there anything staged" test, and
    /// returning one answer unconditionally. One they do not catch, and it is worth knowing which
    /// way round that is: rewriting <c>OnUnmark</c> to delegate to <c>OnDeSpawn</c> with a hardcoded
    /// <c>false</c> passes the suite, because it is behaviour preserving. The collapse that would
    /// actually break something is <c>OnDeSpawn</c> losing the gravship test, and that is caught.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class StoredMaterialsTests
    {
        [Test]
        public void MaterialsComeBackWhenTheBuildingLeavesTheMap()
        {
            // Deconstruct, uninstall, minify, fire, or a wall being smoothed over. Before this
            // existed only deconstruct returned anything, and only from PostDestroy.
            Assert.That(
                StoredMaterials.OnDeSpawn(holdsMaterials: true, haveMap: true, beingTransportedOnGravship: false),
                Is.EqualTo(MaterialReturn.DropOnMap));
        }

        [Test]
        public void AGravshipJumpKeepsTheMaterialsAboard()
        {
            // The one case where keeping them is right. GravshipUtility.GenerateGravship despawns
            // every thing on the ship before swapping maps, so dropping here would scatter a
            // colony's staged materials across the planet it is leaving, with no way back. The
            // building arrives on the far side with this component and its contents intact.
            //
            // The flag is tested rather than mode != DestroyMode.WillReplace, which is what the
            // seven vanilla comps that release contents from PostDeSpawn gate on. WillReplace also
            // means something is being built in this thing's place, and there the materials have to
            // come back. CompTransporter is the vanilla precedent for testing the flag directly.
            Assert.That(
                StoredMaterials.OnDeSpawn(holdsMaterials: true, haveMap: true, beingTransportedOnGravship: true),
                Is.EqualTo(MaterialReturn.Keep));
        }

        [Test]
        public void ADespawnWithNoMapDropsNothing()
        {
            // ThingWithComps.DeSpawn runs the comps loop whether or not base.DeSpawn did anything,
            // so despawning an already-despawned building hands the hook a null. Vanilla's seven
            // equivalent components do not test this and pay a red error per stack for it.
            Assert.That(
                StoredMaterials.OnDeSpawn(holdsMaterials: true, haveMap: false, beingTransportedOnGravship: false),
                Is.EqualTo(MaterialReturn.Keep));
        }

        [Test]
        public void AnEmptyBuildingDropsNothingOnDeSpawn()
        {
            // Which is almost every despawn: the component is on every quality building def, and
            // the overwhelming majority have never been marked at all.
            Assert.That(
                StoredMaterials.OnDeSpawn(holdsMaterials: false, haveMap: true, beingTransportedOnGravship: false),
                Is.EqualTo(MaterialReturn.Keep));
        }

        [TestCase(false, false, false, MaterialReturn.Keep)]
        [TestCase(false, false, true, MaterialReturn.Keep)]
        [TestCase(false, true, false, MaterialReturn.Keep)]
        [TestCase(false, true, true, MaterialReturn.Keep)]
        [TestCase(true, false, false, MaterialReturn.Keep)]
        [TestCase(true, false, true, MaterialReturn.Keep)]
        [TestCase(true, true, false, MaterialReturn.DropOnMap)]
        [TestCase(true, true, true, MaterialReturn.Keep)]
        public void TheDespawnDecisionInFull(
            bool holdsMaterials, bool haveMap, bool beingTransportedOnGravship, MaterialReturn expected)
        {
            Assert.That(
                StoredMaterials.OnDeSpawn(holdsMaterials, haveMap, beingTransportedOnGravship),
                Is.EqualTo(expected));
        }

        [Test]
        public void MaterialsComeBackWhenTheMarkIsClearedOnAStandingBuilding()
        {
            // Cancelling an improvement does not despawn anything, so PostDeSpawn never sees it and
            // this is the only owner. Vanilla's Designator_Cancel reaches the same place.
            Assert.That(
                StoredMaterials.OnUnmark(holdsMaterials: true, haveMap: true),
                Is.EqualTo(MaterialReturn.DropOnMap));
        }

        [Test]
        public void ClearingTheMarkOnADespawnedBuildingDropsNothing()
        {
            // The defect this replaces. The designation-cancel prefix is reached with the target
            // already despawned from four directions, the commonest being any Thing.Destroy of a
            // marked building, because RemoveAllReservationsAndDesignationsOnThis runs for every
            // DestroyMode and runs after the despawn. Dropping there produced one red
            // "Dropped <thing> in a null map." per staged stack and returned nothing.
            Assert.That(
                StoredMaterials.OnUnmark(holdsMaterials: true, haveMap: false),
                Is.EqualTo(MaterialReturn.Keep));
        }

        [TestCase(false, false, MaterialReturn.Keep)]
        [TestCase(false, true, MaterialReturn.Keep)]
        [TestCase(true, false, MaterialReturn.Keep)]
        [TestCase(true, true, MaterialReturn.DropOnMap)]
        public void TheUnmarkDecisionInFull(bool holdsMaterials, bool haveMap, MaterialReturn expected)
        {
            Assert.That(StoredMaterials.OnUnmark(holdsMaterials, haveMap), Is.EqualTo(expected));
        }

        [Test]
        public void TheTwoDecisionsDisagreeAboutAGravship()
        {
            // The same staged materials, the same non-null map, two different answers. Worth
            // stating, because the two functions look alike enough that a reader may take them for
            // duplicates. What this does not do is stop them being merged: rewriting OnUnmark to
            // call OnDeSpawn with a hardcoded false is behaviour preserving and this still passes.
            // The mutation that matters is OnDeSpawn losing the gravship test, and
            // AGravshipJumpKeepsTheMaterialsAboard is what catches that one.
            Assert.That(
                StoredMaterials.OnDeSpawn(holdsMaterials: true, haveMap: true, beingTransportedOnGravship: true),
                Is.Not.EqualTo(StoredMaterials.OnUnmark(holdsMaterials: true, haveMap: true)));
        }
    }
}
