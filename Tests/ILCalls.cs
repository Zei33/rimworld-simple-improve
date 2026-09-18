using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Thrown when the IL walk cannot make sense of a method body.
    /// </summary>
    /// <remarks>
    /// Loud on purpose. A walker that quietly gives up, or that decodes an operand byte as though it
    /// were an opcode and carries on, returns a short call list and every assertion over that list
    /// passes for the wrong reason. Every failure mode here has to be a thrown exception rather than
    /// a missing entry.
    /// </remarks>
    public class ILWalkException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ILWalkException"/> class.
        /// </summary>
        /// <param name="message">What went wrong, including the method and the byte offset.</param>
        public ILWalkException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// What one walk of a method body found.
    /// </summary>
    public sealed class WalkResult
    {
        /// <summary>
        /// Initializes an empty result, for a method with no body to read.
        /// </summary>
        public WalkResult() : this(new List<MethodBase>(), new HashSet<ushort>(), new List<string>())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WalkResult"/> class.
        /// </summary>
        /// <param name="calls">The methods called, in IL order.</param>
        /// <param name="opcodes">The distinct opcodes encountered.</param>
        /// <param name="strings">The string literals loaded, in IL order.</param>
        public WalkResult(IList<MethodBase> calls, ISet<ushort> opcodes, IList<string> strings)
        {
            Calls = calls;
            Opcodes = opcodes;
            Strings = strings;
        }

        /// <summary>
        /// Gets the methods the body calls, in the order they appear, including repeats.
        /// </summary>
        public IList<MethodBase> Calls { get; }

        /// <summary>
        /// Gets the distinct opcodes the walk decoded.
        /// </summary>
        /// <remarks>
        /// Reported so a test can assert that a body still has the shape it was written to have.
        /// The compiler decides whether a <c>switch</c> statement becomes an IL jump table or a
        /// chain of comparisons, and a test written to exercise the jump table stops exercising
        /// anything, silently, if that decision changes.
        /// </remarks>
        public ISet<ushort> Opcodes { get; }

        /// <summary>
        /// Gets the string literals the body loads, in the order they appear, including repeats.
        /// </summary>
        /// <remarks>
        /// Every <c>ldstr</c> operand. A <c>const string</c> is inlined at each use, so it appears here
        /// under every method that reads it rather than under its declaring type. What this cannot
        /// contain is a comment, which is the point of reading it: a check over source text cannot
        /// tell a key that is used from a comment that quotes it, and the compiler has already
        /// thrown the comments away.
        /// </remarks>
        public IList<string> Strings { get; }
    }

    /// <summary>
    /// Reads which methods a compiled method body calls, by walking its IL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This closes a gap <c>Tests/README.md</c> names explicitly. Most of this mod's method bodies
    /// need a spawned <c>Thing</c> on a <c>Map</c> and cannot be run here, so the suite covers the
    /// decisions those bodies delegate to and nothing holds them to still calling them. Deleting the
    /// <c>WorkerSkill.FirstBlocker</c> call from the work giver was a measured example: it passes the
    /// whole suite. The declared surface is reachable by reflection and the compiled body is
    /// reachable this way, which between them covers rather more than "the body cannot be tested"
    /// suggests.
    /// </para>
    /// <para>
    /// Be exact about what it establishes, because it is easy to read as more. It pins <em>which
    /// methods appear in the body and in what textual order</em>. It does not pin arguments: changing
    /// a literal <c>false</c> to <c>true</c> at a call site is invisible here, which matters because
    /// that is the shape of the chair-bug fix. It does not pin control flow either, so inverting the
    /// <c>if</c> around a call changes nothing it can see, and IL order is not execution order in a
    /// method with branches.
    /// </para>
    /// <para>
    /// It also reads the test build rather than the shipped assembly. The mod's sources are compiled
    /// into <c>SimpleImprove.Tests.dll</c>, so the tokens and byte offsets differ from
    /// <c>SimpleImprove.dll</c>; the call sequence does not. Measured on 2026-09-18 across
    /// <c>ImprovableDefs.Qualifies</c>, <c>DeclareCompOn</c> and <c>WorkGiver_Improve.JobOnThing</c>:
    /// Debug and Release disagree about length, locals, <c>nop</c> count and every offset, and their
    /// extracted call sequences are identical. That holds only while nothing in <c>1.6/</c> is inside
    /// a <c>#if</c>, and nothing is.
    /// </para>
    /// <para>
    /// One trap is worth knowing before writing an assertion. The C# compiler hoists a lambda, a
    /// local function, an iterator or an async body into a generated nested type, so the calls made
    /// there are not in the method that appears to make them. <c>JobDriver_Improve.MakeNewToils</c> is
    /// exactly that case. Ask <see cref="CalledAnywhereInTheMod"/> rather than <see cref="CalledBy"/>
    /// whenever the answer must not depend on where the compiler put the code.
    /// </para>
    /// </remarks>
    public static class ILCalls
    {
        private const ushort Call = 0x28;
        private const ushort CallVirtual = 0x6F;
        private const ushort NewObject = 0x73;
        private const ushort LoadFunction = 0xFE06;
        private const ushort LoadVirtualFunction = 0xFE07;
        private const ushort LoadString = 0x72;

        /// <summary>
        /// The <c>switch</c> opcode, whose operand is the only variable-length one in the set.
        /// </summary>
        public const ushort Switch = 0x45;

        private const BindingFlags Everything =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        private static readonly Dictionary<ushort, OperandType> OperandTypes = BuildOperandTable();

        /// <summary>
        /// Gets the methods a method body calls, in the order they appear in its IL.
        /// </summary>
        /// <param name="method">The method to read.</param>
        /// <returns>
        /// Every method named by a <c>call</c>, <c>callvirt</c>, <c>newobj</c>, <c>ldftn</c> or
        /// <c>ldvirtftn</c> instruction, in IL order, including repeats.
        /// </returns>
        /// <exception cref="ILWalkException">The body could not be walked to its end.</exception>
        /// <remarks>
        /// <c>calli</c> is deliberately not in that set: its operand is a signature token rather than
        /// a method token, and resolving it as a method throws.
        /// </remarks>
        public static IList<MethodBase> CalledBy(MethodBase method)
        {
            return Read(method).Calls;
        }

        /// <summary>
        /// Walks a method body and reports what it found.
        /// </summary>
        /// <param name="method">The method to read.</param>
        /// <returns>The walk's result, which is empty for a method with no body.</returns>
        /// <exception cref="ILWalkException">The body could not be walked to its end.</exception>
        public static WalkResult Read(MethodBase method)
        {
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }

            MethodBody body = method.GetMethodBody();
            byte[] il = body?.GetILAsByteArray();

            // Abstract, extern or a runtime-provided body. Nothing to read, and not a fault.
            return il == null ? new WalkResult() : Walk(il, method);
        }

        /// <summary>
        /// Walks a body of IL that has already been obtained.
        /// </summary>
        /// <param name="il">The instruction bytes.</param>
        /// <param name="method">The method the bytes came from, used to resolve tokens and to name failures.</param>
        /// <returns>The walk's result.</returns>
        /// <exception cref="ILWalkException">The bytes could not be walked to their end.</exception>
        /// <remarks>
        /// Separated from <see cref="Read"/> so that the integrity checks below can be tested against
        /// hand-built bytes. They are the only thing standing between a misaligned walk and a green
        /// suite, and a check that nothing exercises is a check that can be deleted silently.
        /// <c>DynamicMethod</c> is not an alternative here: <c>GetMethodBody</c> throws on one.
        /// </remarks>
        internal static WalkResult Walk(byte[] il, MethodBase method)
        {
            var calls = new List<MethodBase>();
            var opcodes = new HashSet<ushort>();
            var strings = new List<string>();

            Type[] typeArguments = method.DeclaringType != null && method.DeclaringType.IsGenericType
                ? method.DeclaringType.GetGenericArguments()
                : Type.EmptyTypes;
            Type[] methodArguments = method.IsGenericMethod || method.IsGenericMethodDefinition
                ? method.GetGenericArguments()
                : Type.EmptyTypes;

            // Where each instruction began, and where every branch claims to be going. Checked
            // against each other at the end, which is what turns a desynchronised walk into a
            // failure rather than a short call list. Landing exactly on the last byte is not enough
            // on its own: a jump table is bytes of small numbers, and small numbers decode as short
            // operand-free instructions, so a walk that reads one as code can drift back into
            // alignment and finish cleanly. Branch targets cannot survive that, because a target
            // that is real points at an instruction the walk also started on.
            var instructionStarts = new HashSet<int>();
            var branchTargets = new List<int>();

            var position = 0;
            while (position < il.Length)
            {
                int instructionStart = position;
                instructionStarts.Add(instructionStart);
                ushort opcode = il[position];
                position++;

                if (opcode == 0xFE)
                {
                    if (position >= il.Length)
                    {
                        throw new ILWalkException(
                            $"Two-byte opcode ran off the end of {Describe(method)} at {instructionStart}.");
                    }

                    opcode = (ushort)(0xFE00 | il[position]);
                    position++;
                }

                opcodes.Add(opcode);

                if (!OperandTypes.TryGetValue(opcode, out OperandType operandType))
                {
                    throw new ILWalkException(
                        $"Unknown opcode 0x{opcode:X4} at offset {instructionStart} in {Describe(method)}. "
                        + "The walk has desynchronised and every call read after this point is nonsense.");
                }

                if (IsCallShaped(opcode))
                {
                    if (position + 4 > il.Length)
                    {
                        throw new ILWalkException(
                            $"Call token ran off the end of {Describe(method)} at {instructionStart}.");
                    }

                    int token = BitConverter.ToInt32(il, position);
                    calls.Add(Resolve(method, token, typeArguments, methodArguments, instructionStart));
                }

                if (opcode == LoadString)
                {
                    if (position + 4 > il.Length)
                    {
                        throw new ILWalkException(
                            $"String token ran off the end of {Describe(method)} at {instructionStart}.");
                    }

                    strings.Add(ResolveString(method, BitConverter.ToInt32(il, position), instructionStart));
                }

                int operandLength = OperandLength(operandType, il, position, method, instructionStart);
                CollectBranchTargets(operandType, il, position, operandLength, branchTargets);
                position += operandLength;
            }

            if (position != il.Length)
            {
                throw new ILWalkException(
                    $"The walk of {Describe(method)} ended at {position} of {il.Length} bytes, so it read "
                    + "an operand as an opcode somewhere.");
            }

            foreach (int target in branchTargets)
            {
                if (!instructionStarts.Contains(target))
                {
                    throw new ILWalkException(
                        $"A branch in {Describe(method)} targets offset {target}, which the walk never "
                        + "saw an instruction begin at. The walk is misaligned even though it ended "
                        + "on the last byte.");
                }
            }

            return new WalkResult(calls, opcodes, strings);
        }

        /// <summary>
        /// Gets every string literal the mod's own compiled code loads, compiler-generated types
        /// included.
        /// </summary>
        /// <returns>The distinct literals, in no particular order.</returns>
        public static ISet<string> StringsAnywhereInTheMod()
        {
            var strings = new HashSet<string>(StringComparer.Ordinal);

            foreach (MethodBase method in AllModMethods())
            {
                strings.UnionWith(Read(method).Strings);
            }

            return strings;
        }

        /// <summary>
        /// Gets every method the mod's own compiled code contains, including compiler-generated ones.
        /// </summary>
        /// <returns>Every method and constructor declared by a type the mod ships.</returns>
        /// <remarks>
        /// The test types are excluded and the generated nested types are not. A lambda hoisted into
        /// <c>&lt;&gt;c</c> or a local function hoisted into <c>&lt;&gt;c__DisplayClass</c> is the
        /// mod's code and is exactly where a call can hide from a search of the method that appears
        /// to make it.
        /// </remarks>
        public static IEnumerable<MethodBase> AllModMethods()
        {
            Assembly assembly = typeof(SimpleImprove.Core.ImproveSite).Assembly;

            foreach (Type type in assembly.GetTypes())
            {
                string space = type.Namespace;
                if (space == null || !space.StartsWith("SimpleImprove", StringComparison.Ordinal))
                {
                    continue;
                }

                if (space.StartsWith("SimpleImprove.Tests", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (MethodInfo method in type.GetMethods(Everything))
                {
                    yield return method;
                }

                foreach (ConstructorInfo constructor in type.GetConstructors(Everything))
                {
                    yield return constructor;
                }
            }
        }

        /// <summary>
        /// Determines whether any of the mod's compiled code calls a method.
        /// </summary>
        /// <param name="declaringType">The type declaring the method being looked for.</param>
        /// <param name="name">The method name being looked for, matching every overload.</param>
        /// <returns>The methods that contain such a call, which is empty when nothing does.</returns>
        public static IList<MethodBase> CalledAnywhereInTheMod(Type declaringType, string name)
        {
            var callers = new List<MethodBase>();

            foreach (MethodBase method in AllModMethods())
            {
                foreach (MethodBase called in CalledBy(method))
                {
                    if (called.DeclaringType == declaringType && called.Name == name)
                    {
                        callers.Add(method);
                        break;
                    }
                }
            }

            return callers;
        }

        /// <summary>
        /// Renders a method for an assertion message.
        /// </summary>
        /// <param name="method">The method to name.</param>
        /// <returns>The declaring type and method name.</returns>
        public static string Describe(MethodBase method)
        {
            return method.DeclaringType == null
                ? method.Name
                : method.DeclaringType.FullName + "." + method.Name;
        }

        private static bool IsCallShaped(ushort opcode)
        {
            return opcode == Call
                   || opcode == CallVirtual
                   || opcode == NewObject
                   || opcode == LoadFunction
                   || opcode == LoadVirtualFunction;
        }

        private static MethodBase Resolve(
            MethodBase method, int token, Type[] typeArguments, Type[] methodArguments, int offset)
        {
            try
            {
                return method.Module.ResolveMethod(token, typeArguments, methodArguments);
            }
            catch (Exception exception)
            {
                throw new ILWalkException(
                    $"Could not resolve token 0x{token:X8} at offset {offset} in {Describe(method)}: "
                    + exception.Message);
            }
        }

        private static string ResolveString(MethodBase method, int token, int offset)
        {
            try
            {
                return method.Module.ResolveString(token);
            }
            catch (Exception exception)
            {
                throw new ILWalkException(
                    $"Could not resolve string token 0x{token:X8} at offset {offset} in {Describe(method)}: "
                    + exception.Message);
            }
        }

        /// <summary>
        /// Records where a branch instruction says it is going, in absolute offsets.
        /// </summary>
        /// <param name="operandType">The instruction's operand type.</param>
        /// <param name="il">The method body.</param>
        /// <param name="position">The offset of the operand.</param>
        /// <param name="operandLength">How long the operand is, so the next instruction can be found.</param>
        /// <param name="targets">The list to add to.</param>
        /// <remarks>
        /// Every branch offset in IL is relative to the instruction that follows the branch, not to
        /// the branch itself, which is why this needs the operand length. A switch carries one
        /// offset per case and they are all relative to the same place, the end of the whole table.
        /// </remarks>
        private static void CollectBranchTargets(
            OperandType operandType, byte[] il, int position, int operandLength, List<int> targets)
        {
            int next = position + operandLength;

            switch (operandType)
            {
                case OperandType.ShortInlineBrTarget:
                    targets.Add(next + (sbyte)il[position]);
                    return;

                case OperandType.InlineBrTarget:
                    targets.Add(next + BitConverter.ToInt32(il, position));
                    return;

                case OperandType.InlineSwitch:
                    int count = BitConverter.ToInt32(il, position);
                    for (var index = 0; index < count; index++)
                    {
                        targets.Add(next + BitConverter.ToInt32(il, position + 4 + (4 * index)));
                    }

                    return;

                default:
                    return;
            }
        }

        private static int OperandLength(
            OperandType operandType, byte[] il, int position, MethodBase method, int instructionStart)
        {
            switch (operandType)
            {
                case OperandType.InlineNone:
                    return 0;

                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    return 1;

                case OperandType.InlineVar:
                    return 2;

                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR:
                    return 4;

                case OperandType.InlineI8:
                case OperandType.InlineR:
                    return 8;

                case OperandType.InlineSwitch:
                    // Variable length, and the one operand a fixed-size table gets wrong. Four bytes
                    // of count followed by that many four-byte targets.
                    if (position + 4 > il.Length)
                    {
                        throw new ILWalkException(
                            $"A switch operand ran off the end of {Describe(method)} at {instructionStart}.");
                    }

                    return 4 + (4 * BitConverter.ToInt32(il, position));

                default:
                    throw new ILWalkException(
                        $"Unhandled operand type {operandType} at offset {instructionStart} in "
                        + Describe(method) + ".");
            }
        }

        private static Dictionary<ushort, OperandType> BuildOperandTable()
        {
            // Built by reflection rather than typed out, so it cannot disagree with the runtime about
            // an instruction length. OpCode.Value is an Int16, so the two-byte 0xFE set reads
            // negative and has to be cast unchecked to line up with 0xFE00 | second byte.
            var table = new Dictionary<ushort, OperandType>();

            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(OpCode))
                {
                    continue;
                }

                var opcode = (OpCode)field.GetValue(null);
                table[unchecked((ushort)opcode.Value)] = opcode.OperandType;
            }

            return table;
        }
    }
}
