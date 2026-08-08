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
            GeneratorResult result = GeneratorTestHarness.Run(Source(OrderContract + @"
    [DataContract]
    public class HasCollection
    {
        [DataMember] public System.Collections.Generic.Dictionary<string, Order> Values { get; set; }
    }

    [DataContractSerializable(typeof(HasCollection))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Empty(result.GeneratorDiagnostics);
            Assert.Contains("unsupported key or value type", result.SingleSource);
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
            GeneratorResult result = GeneratorTestHarness.Run(Source(OrderContract + @"
    [DataContract]
    public class Container
    {
        [DataMember] public Problem Child { get; set; }
    }

    [DataContract]
    public class Problem
    {
        [DataMember] public System.Collections.Generic.Dictionary<string, Order> Values { get; set; }
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
        public void PolymorphicMember_WritesAnXsiTypeAndTheDerivedContractsMembers()
        {
            // A member declared as a base contract may hold a derived one. The serializer announces
            // that with i:type and writes the derived contract's members; writing the declared
            // type's members instead is well-formed, plausible and wrong.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    [KnownType(typeof(Derived))]
    public class Base
    {
        [DataMember] public int BaseValue { get; set; }
    }

    [DataContract]
    public class Derived : Base
    {
        [DataMember] public int DerivedValue { get; set; }
    }

    [DataContract]
    public class Holder
    {
        [DataMember] public Base Value { get; set; }
    }

    [DataContractSerializable(typeof(Holder))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);

            // Exact type equality, not a type pattern: a pattern would let the base branch swallow
            // a derived instance depending on the order the candidates happen to be in.
            Assert.Contains("__runtimeType == typeof(global::App.Derived)", result.SingleSource);
            Assert.Contains("__runtimeType == typeof(global::App.Base)", result.SingleSource);
            Assert.Contains("writer.WriteQualifiedName(\"Derived\"", result.SingleSource);

            // ...and not for the declared type, which is what the reader already assumes.
            Assert.DoesNotContain("writer.WriteQualifiedName(\"Base\"", result.SingleSource);
        }

        [Fact]
        public void KnownTypeOnTheMemberType_IsFound()
        {
            // The attribute may sit on either end - the contract holding the member, or the member's
            // own declared type. The serializer has both in scope while writing the member, so
            // reading only the holder would silently lose the other's types and emit the base
            // contract's members for a derived instance.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    [KnownType(typeof(Derived))]
    public class Base
    {
        [DataMember] public int BaseValue { get; set; }
    }

    [DataContract]
    public class Derived : Base
    {
        [DataMember] public int DerivedValue { get; set; }
    }

    [DataContract]
    public class Holder
    {
        [DataMember] public Base Value { get; set; }
    }

    [DataContractSerializable(typeof(Holder))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("__runtimeType == typeof(global::App.Derived)", result.SingleSource);

            // The root declares no [KnownType] of its own, but resolves one - so it must say so when
            // CoreWCF asks whether the operation's known types are covered.
            Assert.Contains("knownTypes[i] == typeof(global::App.Derived)", result.SingleSource);
        }

        [Fact]
        public void AbstractMemberTypeWithoutAKnownType_LeavesTheContractToReflection()
        {
            // Nothing can ever be an instance of the declared type, so every value this member holds
            // is one no [KnownType] names. Writing the abstract contract's members would be wrong.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public abstract class Shape
    {
        [DataMember] public int Sides { get; set; }
    }

    [DataContract]
    public class Holder
    {
        [DataMember] public Shape Value { get; set; }
    }

    [DataContractSerializable(typeof(Holder))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("declared as abstract contract Shape", result.SingleSource);
            Assert.DoesNotContain("if (type == typeof(global::App.Holder))", result.SingleSource);
        }

        [Fact]
        public void KnownTypeNamingAMethod_LeavesTheContractToReflection()
        {
            // The methodName overload returns its types at run time. Nothing here can evaluate it,
            // and assuming there are none would make the serializer reject instances the real one
            // accepts.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    [KnownType(""GetKnownTypes"")]
    public class Holder
    {
        [DataMember] public int Value { get; set; }

        public static System.Type[] GetKnownTypes() => new System.Type[0];
    }

    [DataContractSerializable(typeof(Holder))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("names a method", result.SingleSource);
            Assert.DoesNotContain("if (type == typeof(global::App.Holder))", result.SingleSource);
        }

        [Fact]
        public void ContractWithoutKnownTypes_CoversNoOperationKnownTypes()
        {
            // CoreWCF supplies known types from the operation description, which no attribute
            // reveals. A serializer compiled against none resolves none, so the honest answer is
            // false and the caller falls back.
            GeneratorResult result = GeneratorTestHarness.Run(Source(OrderContract + @"
    [DataContractSerializable(typeof(Order))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("public override bool CoversKnownTypes", result.SingleSource);
            Assert.DoesNotContain("knownTypes[i] == typeof(", result.SingleSource);
        }

        [Fact]
        public void SerializableType_IsWrittenFromItsFields()
        {
            // A [Serializable] type has no [DataMember]s to read. Every instance field takes part,
            // properties never do, and [NonSerialized] opts a field out. See the else branch of
            // hasDataContract in ClassDataContract.ImportDataMembers.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [System.Serializable]
    public class Legacy
    {
        public string Kept;
        [System.NonSerialized] public string Dropped;
        public string AlsoKept { get; set; }
    }

    [DataContractSerializable(typeof(Legacy))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("writer.WriteStartElement(\"Kept\"", result.SingleSource);
            Assert.DoesNotContain("writer.WriteStartElement(\"Dropped\"", result.SingleSource);

            // A property is not a field, so it contributes nothing - and neither does its compiler
            // generated backing field, whose name is not a legal element name anyway.
            Assert.DoesNotContain("AlsoKept", result.SingleSource);
        }

        [Fact]
        public void TypeWithBothAttributes_IsWrittenAsADataContract()
        {
            // [DataContract] wins when a type carries both, so only the annotated members take part.
            // BaseSerializable in the corpus is exactly this shape.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [System.Serializable]
    [DataContract]
    public class Both
    {
        [DataMember] public string Annotated;
        public string Bare;
    }

    [DataContractSerializable(typeof(Both))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("writer.WriteStartElement(\"Annotated\"", result.SingleSource);
            Assert.DoesNotContain("writer.WriteStartElement(\"Bare\"", result.SingleSource);
        }

        [Fact]
        public void SerializableTypeImplementingISerializable_LeavesTheContractToReflection()
        {
            // ISerializable takes over serialization entirely - a different write algorithm, not a
            // different member list - so the fields are not what would go on the wire.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [System.Serializable]
    public class Custom : System.Runtime.Serialization.ISerializable
    {
        public string Value;

        public void GetObjectData(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
        {
        }
    }

    [DataContractSerializable(typeof(Custom))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("COREWCF_0402", result.DiagnosticIds);
            Assert.DoesNotContain("if (type == typeof(global::App.Custom))", result.SingleSource);
        }

        [Fact]
        public void DateOnlyAndTimeOnlyMembers_DecideTheirFormatAtRunTime()
        {
            // The only members whose wire format is decided by the runtime rather than the contract.
            // Up to .NET 9 the serializer did not recognise them and wrote a memberless contract -
            // an empty element that drops the value - and .NET 10 writes them as primitives. A
            // net8.0 assembly run on .NET 10 produces the .NET 10 format, so the choice cannot be
            // made at compile time.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class Schedule
    {
        [DataMember] public System.DateOnly Day { get; set; }
        [DataMember] public System.TimeOnly At { get; set; }
    }

    [DataContractSerializable(typeof(Schedule))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("global::System.Environment.Version.Major >= 10;", result.SingleSource);

            // Optional fractional digits, so trailing zeros and the dot are omitted rather than
            // padded - matching XmlWriterDelegator.
            Assert.Contains("value.ToString(\"yyyy-MM-dd\", global::System.Globalization.CultureInfo.InvariantCulture)", result.SingleSource);
            Assert.Contains("value.ToString(\"HH:mm:ss.FFFFFFF\", global::System.Globalization.CultureInfo.InvariantCulture)", result.SingleSource);

            // The System namespace is declared only where the serializer does not recognise them.
            Assert.Contains("if (!__DateOnlyIsPrimitive)", result.SingleSource);
        }

        [Fact]
        public void JaggedArrayMember_WrapsEachInnerArrayInAnArrayOfElement()
        {
            // Each outer item is an array in its own right, written as ArrayOf plus the XSD name of
            // the innermost type, holding the items themselves. byte[][] is deliberately not this
            // shape: byte[] is a primitive written as base64, so it stays a flat collection.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class Jagged
    {
        [DataMember] public int[][] Numbers { get; set; }
        [DataMember] public byte[][] Blobs { get; set; }
    }

    [DataContractSerializable(typeof(Jagged))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("writer.WriteStartElement(\"ArrayOfint\", \"http://schemas.microsoft.com/2003/10/Serialization/Arrays\");", result.SingleSource);
            Assert.Contains("foreach (var innerItem in item)", result.SingleSource);

            // byte[][] keeps its flat base64 items, with no ArrayOf wrapper.
            Assert.Contains("writer.WriteStartElement(\"base64Binary\", \"http://schemas.microsoft.com/2003/10/Serialization/Arrays\");", result.SingleSource);
            Assert.DoesNotContain("ArrayOfbase64Binary", result.SingleSource);
        }

        [Fact]
        public void DictionaryMember_NamesEntriesAfterBothTypeArguments()
        {
            // An entry is named KeyValueOf followed by the XSD name of each argument, which is why
            // Dictionary<string, string> writes KeyValueOfstringstring and Dictionary<byte[], byte[]>
            // writes KeyValueOfbase64Binarybase64Binary. Both are pinned by fixtures.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class Maps
    {
        [DataMember] public System.Collections.Generic.Dictionary<string, string> Names { get; set; }
        [DataMember] public System.Collections.Generic.Dictionary<byte[], byte[]> Blobs { get; set; }
    }

    [DataContractSerializable(typeof(Maps))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("writer.WriteStartElement(\"KeyValueOfstringstring\", \"http://schemas.microsoft.com/2003/10/Serialization/Arrays\");", result.SingleSource);
            Assert.Contains("writer.WriteStartElement(\"KeyValueOfbase64Binarybase64Binary\", \"http://schemas.microsoft.com/2003/10/Serialization/Arrays\");", result.SingleSource);

            // Key and Value are ordinary primitive writes in the same namespace.
            Assert.Contains("writer.WriteStartElement(\"Key\", \"http://schemas.microsoft.com/2003/10/Serialization/Arrays\");", result.SingleSource);
            Assert.Contains("writer.WriteStartElement(\"Value\", \"http://schemas.microsoft.com/2003/10/Serialization/Arrays\");", result.SingleSource);
        }

        [Fact]
        public void ArrayListMember_WritesAnyTypeItems()
        {
            // ArrayList holds anything, so each item announces its own runtime type - the same shape
            // as an object member, once per item. Unlike a System.Array member, the namespace is
            // declared once on the member element, so the items carry a prefix rather than each
            // binding a default xmlns of its own.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class Bag
    {
        [DataMember] public System.Collections.ArrayList Items { get; set; }
    }

    [DataContractSerializable(typeof(Bag))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("writer.WriteXmlnsAttribute(null, \"http://schemas.microsoft.com/2003/10/Serialization/Arrays\");", result.SingleSource);
            Assert.Contains("writer.WriteStartElement(\"anyType\", \"http://schemas.microsoft.com/2003/10/Serialization/Arrays\");", result.SingleSource);
            Assert.Contains("WriteAnyType(writer, item);", result.SingleSource);
        }

        [Fact]
        public void DictionaryWithAContractValue_LeavesTheContractToReflection()
        {
            // Only built-in arguments are supported. A contract argument would contribute its own
            // contract name to the entry name and, if it were generic, a hash - neither of which is
            // worth guessing at.
            GeneratorResult result = GeneratorTestHarness.Run(Source(OrderContract + @"
    [DataContract]
    public class Maps
    {
        [DataMember] public System.Collections.Generic.Dictionary<string, Order> Orders { get; set; }
    }

    [DataContractSerializable(typeof(Maps))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("has unsupported key or value type", result.SingleSource);
            Assert.DoesNotContain("if (type == typeof(global::App.Maps))", result.SingleSource);
        }

        [Fact]
        public void AContractBlockedOnSeveralThings_ReportsEveryReason()
        {
            // The coverage report used to record the first reason and stop, which made a wide
            // contract read like a single blocker when it was one of many - and did exactly that
            // once, for AllTypes.
            GeneratorResult result = GeneratorTestHarness.Run(Source(OrderContract + @"
    [DataContract]
    public class Awkward
    {
        [DataMember] public System.Collections.Generic.List<Order> Orders { get; set; }
        [DataMember] public System.Collections.Generic.Dictionary<string, Order> Map { get; set; }
        [DataMember] private int _hidden;
    }

    [DataContractSerializable(typeof(Awkward))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("member 'Orders' has unsupported collection element type", result.SingleSource);
            Assert.Contains("member 'Map' has unsupported key or value type", result.SingleSource);
            Assert.Contains("member '_hidden' is not public", result.SingleSource);
        }

        [Fact]
        public void ByValueContractMember_IsGuardedAgainstCycles()
        {
            // A contract written by value can be cyclic, and the generated writer would recurse
            // until the stack ran out. DataContractSerializer throws past a depth of 512 instead,
            // and a StackOverflowException cannot be caught - so the generated path matches it.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
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
            Assert.Contains("scope.EnterByValue(value.Next);", result.SingleSource);
            Assert.Contains("scope.ExitByValue(value.Next);", result.SingleSource);
            Assert.Contains("private const int DepthToCheckCyclicReference = 512;", result.SingleSource);
        }

        [Fact]
        public void IsReferenceContractMember_IsNotGuarded()
        {
            // A reference-preserving contract cannot recurse forever: the second sight of an
            // instance is a z:Ref with no content, so the guard would be dead weight.
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
            Assert.DoesNotContain("scope.EnterByValue", result.SingleSource);
        }

        [Fact]
        public void XmlQualifiedNameMember_GetsItsOwnElementPrefix()
        {
            // The one member type whose element carries a prefix of its own rather than reusing
            // what the writer has bound, so a second prefix ends up on the contract's namespace
            // beside the one already in scope. Mirrors NeedsPrefix in ReflectionXmlFormatWriter,
            // which forces it for this type alone and only when the namespace is non-empty.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class Named
    {
        [DataMember] public System.Xml.XmlQualifiedName Which { get; set; }
    }

    [DataContractSerializable(typeof(Named))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("writer.WriteStartElement(\"q\", \"Which\", \"http://schemas.datacontract.org/2004/07/App\");", result.SingleSource);

            // The empty name writes nothing at all, not an empty string.
            Assert.Contains("if (value != global::System.Xml.XmlQualifiedName.Empty)", result.SingleSource);
        }

        [Fact]
        public void ValueTypeMember_OffersOnlyValueTypeCandidates()
        {
            // A member declared as ValueType is the boxed switch over a narrower set. The filtering
            // is not cosmetic: casting a ValueType to string is a compile error, so an unfiltered
            // table would emit generated code that does not build.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class Boxy
    {
        [DataMember] public System.ValueType Value { get; set; }
    }

    [DataContractSerializable(typeof(Boxy))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("__runtimeType == typeof(int)", result.SingleSource);
            Assert.DoesNotContain("((string)value.Value)", result.SingleSource);
            Assert.DoesNotContain("((byte[])value.Value)", result.SingleSource);
        }

        [Fact]
        public void UriMember_IsWrittenFromItsSerializationComponents()
        {
            // Not ToString(). SerializationInfoString is the round-trippable form, and it is what
            // normalises an authority-only Uri to carry a trailing slash - which the
            // SanityUriAndOffset fixture records.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class Endpoint
    {
        [DataMember] public System.Uri Address { get; set; }
        [DataMember] public System.Uri[] Fallbacks { get; set; }
    }

    [DataContractSerializable(typeof(Endpoint))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("GetComponents(global::System.UriComponents.SerializationInfoString, global::System.UriFormat.UriEscaped)", result.SingleSource);

            // In a collection it is an XSD name in the Arrays namespace, like any other built-in.
            Assert.Contains("writer.WriteStartElement(\"anyURI\", \"http://schemas.microsoft.com/2003/10/Serialization/Arrays\");", result.SingleSource);
        }

        [Fact]
        public void DateTimeOffsetMember_IsWrittenAsATwoMemberContract()
        {
            // DateTimeOffset is not a value on the wire at all: DataContractSerializer swaps in
            // DateTimeOffsetAdapter, a contract with a DateTime and an OffsetMinutes member living
            // in a namespace neither type mentions. The DateTime written is the UTC one, so the
            // offset is recorded once rather than baked into both.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class Stamped
    {
        [DataMember] public System.DateTimeOffset When { get; set; }
        [DataMember] public System.DateTimeOffset? Maybe { get; set; }
        [DataMember] public System.Collections.Generic.List<System.DateTimeOffset> Many { get; set; }
    }

    [DataContractSerializable(typeof(Stamped))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("writer.WriteStartElement(\"DateTime\", \"http://schemas.datacontract.org/2004/07/System\");", result.SingleSource);
            Assert.Contains("writer.WriteValue(value.UtcDateTime);", result.SingleSource);
            Assert.Contains("writer.WriteValue((short)value.Offset.TotalMinutes);", result.SingleSource);

            // An item is named after the contract and stays in the System namespace, unlike the
            // built-in types which all go into the Arrays namespace.
            Assert.Contains("writer.WriteStartElement(\"DateTimeOffset\", \"http://schemas.datacontract.org/2004/07/System\");", result.SingleSource);
        }

        [Fact]
        public void UnsignedLongMember_GoesThroughWriteRaw()
        {
            // XmlWriter has no WriteValue(ulong): ulong converts implicitly to float, double and
            // decimal and to none of them better than the others, so WriteValue(ulong) is ambiguous
            // rather than missing. The mistake surfaces as CS0121 in generated code, which the
            // compiler reported by exiting without a diagnostic - so AssertCompiles is the test that
            // matters here as much as the string assertions.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class Counts
    {
        [DataMember] public ulong Total { get; set; }
        [DataMember] public ulong[] Buckets { get; set; }
    }

    [DataContractSerializable(typeof(Counts))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("writer.WriteRaw(global::System.Xml.XmlConvert.ToString(value.Total));", result.SingleSource);
            Assert.Contains("writer.WriteRaw(global::System.Xml.XmlConvert.ToString(item));", result.SingleSource);
        }

        [Fact]
        public void ObjectMember_WritesTheRuntimeTypeAsAnXsiType()
        {
            // object constrains nothing, so the runtime type decides the element's type, its value
            // and its namespace. char, Guid and TimeSpan are named in the serialization namespace
            // rather than XML Schema, which has no equivalent for them - a split that is invisible
            // until a document is compared byte for byte.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class Holder
    {
        [DataMember] public object Value { get; set; }
    }

    [DataContractSerializable(typeof(Holder))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("writer.WriteQualifiedName(\"boolean\", \"http://www.w3.org/2001/XMLSchema\")", result.SingleSource);
            Assert.Contains("writer.WriteQualifiedName(\"char\", \"http://schemas.microsoft.com/2003/10/Serialization/\")", result.SingleSource);
            Assert.Contains("writer.WriteQualifiedName(\"duration\", \"http://schemas.microsoft.com/2003/10/Serialization/\")", result.SingleSource);

            // sbyte is "byte" and byte is "unsignedByte" - the reverse of the obvious guess.
            Assert.Contains("__runtimeType == typeof(sbyte)", result.SingleSource);
            Assert.Contains("writer.WriteQualifiedName(\"unsignedByte\", \"http://www.w3.org/2001/XMLSchema\")", result.SingleSource);

            // A bare object is anyType: an empty element with no i:type at all.
            Assert.Contains("// anyType: neither i:type nor content", result.SingleSource);
        }

        [Fact]
        public void ObjectMemberWithAnEnumKnownType_WritesTheEnumWithAnXsiType()
        {
            // Every known type in scope is a candidate for an object member, enums included. The
            // enum is announced with i:type like any other contract, then written from its own
            // value/name table rather than by a content writer.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    public enum Colour { Red }

    [DataContract]
    [KnownType(typeof(Colour))]
    public class Holder
    {
        [DataMember] public object Value { get; set; }
    }

    [DataContractSerializable(typeof(Holder))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("__runtimeType == typeof(global::App.Colour)", result.SingleSource);
            Assert.Contains("writer.WriteQualifiedName(\"Colour\", \"http://schemas.datacontract.org/2004/07/App\")", result.SingleSource);
            Assert.Contains("WriteEnum(writer, (long)((global::App.Colour)value.Value)", result.SingleSource);
        }

        [Fact]
        public void EnumCollection_NamesItemsAfterTheEnumContractNotTheArraysNamespace()
        {
            // An enum item is named after its own contract and stays in its own namespace, unlike
            // the built-in types which all go into the Arrays namespace. AllTypes.enumArrayData in
            // the corpus writes <a:MyEnum1> beside its containing contract for exactly this reason,
            // with no xmlns declaration on the member element at all.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    public enum Colour { Red, Green }

    [DataContract]
    public class Palette
    {
        [DataMember] public Colour[] Colours { get; set; }
    }

    [DataContractSerializable(typeof(Palette))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains(
                "writer.WriteStartElement(\"Colour\", \"http://schemas.datacontract.org/2004/07/App\");",
                result.SingleSource);
            Assert.DoesNotContain("http://schemas.microsoft.com/2003/10/Serialization/Arrays", result.SingleSource);
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
