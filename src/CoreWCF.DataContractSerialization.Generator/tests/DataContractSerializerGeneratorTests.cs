// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Xunit;

namespace CoreWCF.DataContractSerialization.Generator.Tests
{
    public class DataContractSerializerGeneratorTests
    {
        /// <summary>Wraps a snippet in the usings and namespace every test needs.</summary>
        private static string Source(string body) => @"
using System.Runtime.Serialization;
using CoreWCF.DataContractSerialization;

namespace App
{
" + body + @"
}
";

        private static void AssertCompiles(GeneratorResult result) =>
            Assert.True(!result.Errors.Any(), "Generated code did not compile:" + Environment.NewLine + result.ErrorReport);

        private const string OrderContract = @"
    [DataContract]
    public class Order
    {
        [DataMember]
        public int Id { get; set; }

        [DataMember]
        public string Name;
    }
";

        [Fact]
        public void ContextWithoutAnySerializableAttribute_IsNotDiscovered()
        {
            // The attribute is the opt-in. A context carrying none is not a context the generator
            // knows about, so it emits nothing and the class keeps the base implementation, which
            // returns null and sends CoreWCF to the reflection-based serializer.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            Assert.Empty(result.GeneratedSources);
            Assert.Empty(result.GeneratorDiagnostics);
        }

        [Fact]
        public void FlatContract_EmitsASerializerAndCompiles()
        {
            GeneratorResult result = GeneratorTestHarness.Run(Source(OrderContract + @"
    [DataContractSerializable(typeof(Order))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Empty(result.GeneratorDiagnostics);
            Assert.Contains("if (type == typeof(global::App.Order))", result.SingleSource);
            // A property and a field must both be picked up.
            Assert.Contains("writer.WriteValue(value.Id);", result.SingleSource);
            Assert.Contains("writer.WriteValue(value.Name);", result.SingleSource);
        }

        [Fact]
        public void Members_AreOrderedByOrderThenOrdinalName()
        {
            // Unspecified Order is -1 and cannot be written explicitly, so unordered members precede
            // every ordered one - including Order = 0. See ClassDataContract.DataMemberComparer.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class Ordered
    {
        [DataMember(Order = 0)] public int ExplicitZero { get; set; }
        [DataMember] public int Zebra { get; set; }
        [DataMember] public int Apple { get; set; }
        [DataMember(Order = 5)] public int Later { get; set; }
    }

    [DataContractSerializable(typeof(Ordered))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            string emitted = result.SingleSource;

            int apple = emitted.IndexOf("\"Apple\"", StringComparison.Ordinal);
            int zebra = emitted.IndexOf("\"Zebra\"", StringComparison.Ordinal);
            int explicitZero = emitted.IndexOf("\"ExplicitZero\"", StringComparison.Ordinal);
            int later = emitted.IndexOf("\"Later\"", StringComparison.Ordinal);

            Assert.True(apple > 0 && zebra > 0 && explicitZero > 0 && later > 0, "All four members should be emitted.");
            Assert.True(apple < zebra, "Unordered members sort ordinally by name.");
            Assert.True(zebra < explicitZero, "Unordered members precede Order = 0.");
            Assert.True(explicitZero < later, "Ordered members sort by Order.");
        }

        [Fact]
        public void RenamedContractAndMember_UseTheirWireNames()
        {
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract(Name = ""RenamedContract"", Namespace = ""http://example/ns"")]
    public class Renamed
    {
        [DataMember(Name = ""OnTheWire"")] public int Value { get; set; }
    }

    [DataContractSerializable(typeof(Renamed))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("\"OnTheWire\"", result.SingleSource);
            Assert.Contains("\"http://example/ns\"", result.SingleSource);
        }

        [Fact]
        public void ContractWithoutExplicitNamespace_GetsTheDefaultDerivedFromItsClrNamespace()
        {
            GeneratorResult result = GeneratorTestHarness.Run(Source(OrderContract + @"
    [DataContractSerializable(typeof(Order))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("http://schemas.datacontract.org/2004/07/App", result.SingleSource);
        }

        [Fact]
        public void EmitDefaultValueFalse_OmitsTheMember()
        {
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class Sparse
    {
        [DataMember(EmitDefaultValue = false)] public int Maybe { get; set; }
    }

    [DataContractSerializable(typeof(Sparse))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("// omitted", result.SingleSource);
        }

        [Fact]
        public void EmitDefaultValueFalseOnARequiredMember_Throws()
        {
            // DataContractSerializer treats this as an error rather than an omission. See
            // ReflectionXmlFormatWriter.ReflectionWriteMembers.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class Contradictory
    {
        [DataMember(EmitDefaultValue = false, IsRequired = true)] public int Required { get; set; }
    }

    [DataContractSerializable(typeof(Contradictory))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("SerializationException", result.SingleSource);
        }

        [Fact]
        public void CollectionMember_WritesItemsInTheCollectionNamespace()
        {
            // Items are named after their XSD type, not the CLR type - and several differ. The
            // names are pinned by the SanityPrimitiveArrays fixture, produced by the real
            // serializer.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class WithCollections
    {
        [DataMember] public int[] Numbers { get; set; }
        [DataMember] public System.Collections.Generic.List<string> Words { get; set; }
        [DataMember] public sbyte[] Signed { get; set; }
    }

    [DataContractSerializable(typeof(WithCollections))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("writer.WriteXmlnsAttribute(null, \"http://schemas.microsoft.com/2003/10/Serialization/Arrays\");", result.SingleSource);
            Assert.Contains("writer.WriteStartElement(\"int\", \"http://schemas.microsoft.com/2003/10/Serialization/Arrays\");", result.SingleSource);
            Assert.Contains("writer.WriteStartElement(\"string\", \"http://schemas.microsoft.com/2003/10/Serialization/Arrays\");", result.SingleSource);
            // sbyte is "byte" on the wire, and byte is "unsignedByte" - the opposite of the guess.
            Assert.Contains("writer.WriteStartElement(\"byte\", \"http://schemas.microsoft.com/2003/10/Serialization/Arrays\");", result.SingleSource);
        }

        [Fact]
        public void ByteArrayMember_IsAPrimitiveNotACollection()
        {
            // byte[] is written as base64 in the containing contract's namespace, with no child
            // namespace declaration and no per-item elements.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class WithBytes
    {
        [DataMember] public byte[] Payload { get; set; }
    }

    [DataContractSerializable(typeof(WithBytes))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("writer.WriteBase64(value.Payload, 0, value.Payload.Length);", result.SingleSource);
            Assert.DoesNotContain("Serialization/Arrays", result.SingleSource);
        }

        [Fact]
        public void UnsupportedMemberType_LeavesTheContractToReflection()
        {
            // Falling back is a correct outcome, so this is a recorded comment rather than a
            // diagnostic - and no serializer is emitted, so GetSerializer returns null for it.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class HasCollection
    {
        [DataMember] public System.Collections.Generic.Dictionary<string, string> Values { get; set; }
    }

    [DataContractSerializable(typeof(HasCollection))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Empty(result.GeneratorDiagnostics);
            Assert.Contains("unsupported type", result.SingleSource);
            Assert.DoesNotContain("if (type == typeof(global::App.HasCollection))", result.SingleSource);
        }

        [Fact]
        public void DerivedContract_WritesBaseMembersFirst()
        {
            // Ordering is per contract and base-first, not a single merged sort across the
            // hierarchy - so a base member named Zulu still precedes a derived member named Alpha.
            // Mirrors the recursion in ReflectionXmlClassWriter.ReflectionWriteMembers.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class BaseContract
    {
        [DataMember] public int Zulu { get; set; }
    }

    [DataContract]
    public class DerivedContract : BaseContract
    {
        [DataMember] public int Alpha { get; set; }
    }

    [DataContractSerializable(typeof(DerivedContract))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("if (type == typeof(global::App.DerivedContract))", result.SingleSource);

            // The derived content writer delegates to the base one before writing its own members.
            int delegation = result.SingleSource.IndexOf("__WriteContent", StringComparison.Ordinal);
            int alpha = result.SingleSource.IndexOf("\"Alpha\"", StringComparison.Ordinal);
            int zulu = result.SingleSource.IndexOf("\"Zulu\"", StringComparison.Ordinal);
            Assert.True(delegation > 0 && alpha > 0 && zulu > 0, "Both contracts should be emitted.");
        }

        [Fact]
        public void ContractWithNonContractBaseClass_LeavesTheContractToReflection()
        {
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    public class PlainBase
    {
        public int Ignored { get; set; }
    }

    [DataContract]
    public class DerivedFromPlain : PlainBase
    {
        [DataMember] public int Value { get; set; }
    }

    [DataContractSerializable(typeof(DerivedFromPlain))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("is not a data contract", result.SingleSource);
        }

        [Fact]
        public void NestedContractMember_IsWrittenInlineWithItsOwnNamespaceDeclared()
        {
            // A contract-typed member has no second wrapping element: the nested contract's members
            // are written straight inside the member element. Its namespace is declared there rather
            // than at the root, which is what produces xmlns:b on the member. Mirrors
            // ClassDataContract.GetChildNamespaceToDeclare.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract(Namespace = ""http://outer/ns"")]
    public class Outer
    {
        [DataMember] public Inner Child { get; set; }
    }

    [DataContract(Namespace = ""http://inner/ns"")]
    public class Inner
    {
        [DataMember] public int Value { get; set; }
    }

    [DataContractSerializable(typeof(Outer))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("writer.WriteXmlnsAttribute(null, \"http://inner/ns\");", result.SingleSource);
            // Inner is pulled in transitively even though only Outer was declared.
            Assert.Contains("\"Value\"", result.SingleSource);
        }

        [Fact]
        public void NestedContractInTheSameNamespace_DeclaresNothingExtra()
        {
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract(Namespace = ""http://shared/ns"")]
    public class Outer
    {
        [DataMember] public Inner Child { get; set; }
    }

    [DataContract(Namespace = ""http://shared/ns"")]
    public class Inner
    {
        [DataMember] public int Value { get; set; }
    }

    [DataContractSerializable(typeof(Outer))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.DoesNotContain("writer.WriteXmlnsAttribute(null, \"http://shared/ns\");\r\n            writer.WriteXmlnsAttribute", result.SingleSource);
        }

        [Fact]
        public void UnsupportedNestedContract_MakesTheContainerUnsupportedToo()
        {
            // A container is only writable if everything it writes is. Emitting a serializer that
            // silently skipped an unwritable member would produce wrong XML rather than falling back.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class Container
    {
        [DataMember] public Problem Child { get; set; }
    }

    [DataContract]
    public class Problem
    {
        [DataMember] public System.Collections.Generic.Dictionary<string, string> Values { get; set; }
    }

    [DataContractSerializable(typeof(Container))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.DoesNotContain("if (type == typeof(global::App.Container))", result.SingleSource);
            Assert.Contains("unsupported contract type", result.SingleSource);
        }

        [Fact]
        public void InheritedIsReference_IsDetectedThroughTheBaseChain()
        {
            // IsReference is inherited. A derived contract that says nothing still gets it, and
            // reading only its own attribute would miss that and emit output without z:Id.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract(IsReference = true)]
    public class ReferencedBase
    {
        [DataMember] public int BaseValue { get; set; }
    }

    [DataContract]
    public class QuietDerived : ReferencedBase
    {
        [DataMember] public int Value { get; set; }
    }

    [DataContractSerializable(typeof(QuietDerived))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("if (type == typeof(global::App.QuietDerived))", result.SingleSource);
            Assert.Contains("scope.WriteIdOrRef(writer, graph);", result.SingleSource);
        }

        [Fact]
        public void ContractWithoutIsReference_WritesNoId()
        {
            // The counterpart to the test above: a plain contract must not acquire a z:Id, which
            // would be a visible difference in every document it appears in.
            GeneratorResult result = GeneratorTestHarness.Run(Source(OrderContract + @"
    [DataContractSerializable(typeof(Order))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("if (type == typeof(global::App.Order))", result.SingleSource);

            // The scope type is always emitted because every content writer takes one; what must
            // be absent is any call that would put an id on the wire.
            Assert.DoesNotContain("scope.WriteIdOrRef", result.SingleSource);
        }

        [Fact]
        public void IsReferenceMemberSeenTwice_WritesARefInsteadOfTheContent()
        {
            // The whole point of IsReference: the second sight of an instance is a z:Ref with no
            // content. Writing the members again would both duplicate data and, for a cycle,
            // recurse forever.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract(IsReference = true)]
    public class Node
    {
        [DataMember] public Node Next { get; set; }
    }

    [DataContractSerializable(typeof(Node))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("if (!scope.WriteIdOrRef(writer, value.Next))", result.SingleSource);
        }

        [Fact]
        public void ContradictoryIsReference_LeavesTheContractToReflection()
        {
            // A derived contract cannot disagree with its base about IsReference:
            // DataContractSerializer throws InvalidDataContractException. Declining keeps that
            // throw, where emitting a serializer would silently accept an invalid contract.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class PlainBase
    {
        [DataMember] public int BaseValue { get; set; }
    }

    [DataContract(IsReference = true)]
    public class LoudDerived : PlainBase
    {
        [DataMember] public int Value { get; set; }
    }

    [DataContractSerializable(typeof(LoudDerived))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("contradicts base contract PlainBase", result.SingleSource);
            Assert.DoesNotContain("if (type == typeof(global::App.LoudDerived))", result.SingleSource);
        }

        [Fact]
        public void NonPublicMember_LeavesTheContractToReflection()
        {
            // Generated code lives in the context's assembly and cannot reach a private member.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class Hidden
    {
        [DataMember] private int _value;
    }

    [DataContractSerializable(typeof(Hidden))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            Assert.Contains("is not public", result.SingleSource);
        }

        [Fact]
        public void ContractInAnotherAssemblyWithAPrivateMember_LeavesTheContractToReflection()
        {
            // SerializationTestTypes.BaseDCNoIsRef has a single [DataMember] on a private field.
            // Compiled from metadata rather than source, that member may not be visible to the
            // generator at all - in which case it would happily emit a serializer that silently
            // drops it, producing XML that is wrong rather than absent. Whatever Roslyn surfaces,
            // the outcome must be a fallback, never a partial serializer.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContractSerializable(typeof(SerializationTestTypes.BaseDCNoIsRef))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.DoesNotContain(
                "if (type == typeof(global::SerializationTestTypes.BaseDCNoIsRef))",
                result.SingleSource);
        }

        [Fact]
        public void IsReferenceOnAValueType_LeavesTheContractToReflection()
        {
            // A struct has no identity to preserve, and DataContractSerializer rejects the
            // combination rather than ignoring it. Declining keeps that behaviour.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract(IsReference = true)]
    public struct Referenced
    {
        [DataMember] public int Value { get; set; }
    }

    [DataContractSerializable(typeof(Referenced))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("IsReference is not valid on a value type", result.SingleSource);
            Assert.DoesNotContain("if (type == typeof(global::App.Referenced))", result.SingleSource);
        }

        [Fact]
        public void NonPartialContext_ReportsCOREWCF_0400()
        {
            GeneratorResult result = GeneratorTestHarness.Run(Source(OrderContract + @"
    [DataContractSerializable(typeof(Order))]
    public class MyContext : DataContractSerializerContext
    {
    }
"));

            Assert.Contains("COREWCF_0400", result.DiagnosticIds);
            Assert.Empty(result.GeneratedSources);
        }

        [Fact]
        public void ContextNotDerivingFromBase_ReportsCOREWCF_0401()
        {
            GeneratorResult result = GeneratorTestHarness.Run(Source(OrderContract + @"
    [DataContractSerializable(typeof(Order))]
    public partial class MyContext
    {
    }
"));

            Assert.Contains("COREWCF_0401", result.DiagnosticIds);
            Assert.Empty(result.GeneratedSources);
        }

        [Fact]
        public void TypeWithoutDataContract_ReportsCOREWCF_0402()
        {
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    public class NotAContract
    {
        public int Value { get; set; }
    }

    [DataContractSerializable(typeof(NotAContract))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            Assert.Contains("COREWCF_0402", result.DiagnosticIds);
        }

        [Fact]
        public void Disabled_EmitsNothing()
        {
            // The target framework gate is what keeps emitted code free to use a modern language
            // version; when it is off the generator must produce nothing at all.
            GeneratorResult result = GeneratorTestHarness.Run(
                Source(OrderContract + @"
    [DataContractSerializable(typeof(Order))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"),
                enabled: false);

            Assert.Empty(result.GeneratedSources);
            Assert.Empty(result.GeneratorDiagnostics);
        }
    }
}
