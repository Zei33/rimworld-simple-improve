using RimWorld;
using Verse;

namespace SimpleImprove.Core
{
    /// <summary>
    /// Static class containing references to all definition objects used by the SimpleImprove mod.
    /// This class provides compile-time safe access to XML-defined game objects.
    /// </summary>
    [DefOf]
    public static class SimpleImproveDefOf
    {
        /// <summary>
        /// The designation definition used to mark items for improvement.
        /// </summary>
        public static DesignationDef Designation_Improve;
        
        /// <summary>
        /// The job definition for hauling materials to items marked for improvement.
        /// </summary>
        public static JobDef Job_HaulToImprove;
        
        /// <summary>
        /// The job definition for performing the actual improvement work on items.
        /// </summary>
        public static JobDef Job_Improve;
        
        /// <summary>
        /// The work type definition that governs which pawns can perform improvement tasks.
        /// </summary>
        public static WorkTypeDef WorkType_Improving;

        /// <summary>
        /// Static constructor to ensure all definitions are properly initialized.
        /// </summary>
        static SimpleImproveDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(SimpleImproveDefOf));
        }
    }
}