using Verse;

namespace SimpleImprove.Core
{
    /// <summary>
    /// Component properties class for SimpleImprove functionality.
    /// This class defines the component that will be attached to things with quality to enable improvement functionality.
    /// </summary>
    public class CompProperties_SimpleImprove : CompProperties
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CompProperties_SimpleImprove"/> class.
        /// Sets the component class to <see cref="SimpleImproveComp"/>.
        /// </summary>
        public CompProperties_SimpleImprove()
        {
            compClass = typeof(SimpleImproveComp);
        }
    }
}