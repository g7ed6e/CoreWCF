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
            Assert.Contains("ContractNamespace = \"http://example/ns\"", result.SingleSource);
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
        public void UnsupportedMemberType_LeavesTheContractToReflection()
        {
            // Falling back is a correct outcome, so this is a recorded comment rather than a
            // diagnostic - and no serializer is emitted, so GetSerializer returns null for it.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class HasCollection
    {
        [DataMember] public System.Collections.Generic.List<int> Values { get; set; }
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
        public void ContractWithBaseClass_LeavesTheContractToReflection()
        {
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract]
    public class BaseContract
    {
        [DataMember] public int BaseValue { get; set; }
    }

    [DataContract]
    public class DerivedContract : BaseContract
    {
        [DataMember] public int DerivedValue { get; set; }
    }

    [DataContractSerializable(typeof(DerivedContract))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("inheritance is not supported yet", result.SingleSource);
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
        public void IsReferenceContract_LeavesTheContractToReflection()
        {
            // IsReference makes the serializer emit z:Id and z:Ref for object identity, which this
            // slice does not implement. Emitting a serializer that ignored it would produce output
            // that looks plausible and is wrong.
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [DataContract(IsReference = true)]
    public class Referenced
    {
        [DataMember] public int Value { get; set; }
    }

    [DataContractSerializable(typeof(Referenced))]
    public partial class MyContext : DataContractSerializerContext
    {
    }
"));

            AssertCompiles(result);
            Assert.Contains("IsReference", result.SingleSource);
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
