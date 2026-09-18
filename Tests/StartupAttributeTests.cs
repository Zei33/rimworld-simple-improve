using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Verse;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Runs the game's own dev-mode check for a missing <c>[StaticConstructorOnStartup]</c> over the
    /// mod's types.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>StaticConstructorOnStartupUtility.ReportProbablyMissingAttributes</c> runs twice at startup
    /// whenever dev mode is on, and warns about every type that lacks the attribute and has a static
    /// field whose type, or array element type, is a <c>Texture</c>, <c>Material</c>,
    /// <c>Shader</c>, <c>Graphic</c>, <c>GameObject</c> or <c>MaterialPropertyBlock</c>. It is a
    /// heuristic and does not care whether the field is filled lazily on the main thread, which is
    /// how <see cref="Core.ImproveSelection"/> earned a warning in the 1.1.0 build while being safe.
    /// A modder with dev mode on reads that yellow line as a bug report, so the check is reproduced
    /// here rather than left to be noticed in a log.
    /// </para>
    /// <para>
    /// The predicate is copied from the game's, field for field, rather than simplified. A stricter
    /// or looser version would pass or fail on types the game treats the other way.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class StartupAttributeTests
    {
        private const BindingFlags StaticFields =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void NoModTypeWouldDrawTheMissingAttributeWarning()
        {
            List<string> offenders = ModTypes()
                .Where(t => !t.IsDefined(typeof(StaticConstructorOnStartup), true))
                .Select(t => new { Type = t, Field = t.GetFields(StaticFields).FirstOrDefault(IsAssetField) })
                .Where(x => x.Field != null)
                .Select(x => x.Type.FullName + "." + x.Field.Name)
                .ToList();

            Assert.That(offenders, Is.Empty,
                "These would log \"probably needs a StaticConstructorOnStartup attribute\" in dev mode.");
        }

        [Test]
        public void TheScanFindsTheFieldsThatDrewTheWarning()
        {
            // The positive control. The test above passes vacuously if the scan sees no types or
            // misreads the field types, so this asserts it finds the two texture fields that
            // actually drew the warning, in the type that actually drew it.
            List<string> assetFields = typeof(Core.ImproveSelection)
                .GetFields(StaticFields)
                .Where(IsAssetField)
                .Select(f => f.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            Assert.That(assetFields, Is.EqualTo(new[] { "cancelIcon", "icon" }));
            Assert.That(ModTypes(), Does.Contain(typeof(Core.ImproveSelection)));
        }

        /// <summary>
        /// The game's own test, from <c>ReportProbablyMissingAttributes</c>.
        /// </summary>
        private static bool IsAssetField(FieldInfo field)
        {
            Type type = field.FieldType;
            if (type.IsArray)
            {
                type = type.GetElementType();
            }

            return typeof(Texture).IsAssignableFrom(type)
                || typeof(Material).IsAssignableFrom(type)
                || typeof(Shader).IsAssignableFrom(type)
                || typeof(Graphic).IsAssignableFrom(type)
                || typeof(GameObject).IsAssignableFrom(type)
                || typeof(MaterialPropertyBlock).IsAssignableFrom(type);
        }

        /// <summary>
        /// Every type compiled from the mod's own sources, nested and generated ones included,
        /// because <c>GenTypes.AllTypes</c> includes them too.
        /// </summary>
        private static List<Type> ModTypes()
        {
            List<Type> types = typeof(SimpleImproveMod).Assembly.GetTypes()
                .Where(t => t.Namespace != null
                    && (t.Namespace == "SimpleImprove" || t.Namespace.StartsWith("SimpleImprove.", StringComparison.Ordinal))
                    && !t.Namespace.StartsWith("SimpleImprove.Tests", StringComparison.Ordinal))
                .ToList();

            Assert.That(types, Has.Count.GreaterThan(20), "The scan found too few of the mod's types to mean anything.");
            return types;
        }
    }
}
