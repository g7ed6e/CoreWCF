// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using Xunit;

namespace CoreWCF.Extensions.Configuration.Generator.Tests
{
    /// <summary>
    /// What the generator emits, and what it refuses to.
    /// </summary>
    /// <remarks>
    /// Every test compiles the input together with the generated output against the real CoreWCF
    /// binding assemblies. That is the assertion doing most of the work: the generated path exists to
    /// replace reflection with ordinary code, so code that does not compile has failed at the only
    /// thing it was for.
    /// </remarks>
    public class ServiceModelConfigurationGeneratorTests
    {
        [Fact]
        public void DisabledByBuildProperty_EmitsNothing()
        {
            GeneratorResult result = GeneratorTestHarness.Run(Source(@"
    [ServiceModelConfigurable(typeof(global::CoreWCF.NetTcpBinding))]
    public partial class MyContext : ServiceModelConfigurationContext { }
"), enabled: false);

            Assert.Empty(result.GeneratedSources);
        }

        [Fact]
        public void Binding_EmitsTypeMapAndMetadata()
        {
            GeneratorResult result = Run(@"
    [ServiceModelConfigurable(typeof(global::CoreWCF.NetTcpBinding), Name = ""netTcp"")]
    public partial class MyContext : ServiceModelConfigurationContext { }
");

            Assert.Empty(result.Errors);

            // The three spellings a configuration file may use, all resolving through typeof.
            Assert.Contains("{ \"netTcp\", typeof(global::CoreWCF.NetTcpBinding) }", result.SingleSource);
            Assert.Contains("{ \"CoreWCF.NetTcpBinding\", typeof(global::CoreWCF.NetTcpBinding) }", result.SingleSource);
            Assert.Contains("{ \"CoreWCF.NetTcpBinding, CoreWCF.NetTcp\", typeof(global::CoreWCF.NetTcpBinding) }", result.SingleSource);

            Assert.Contains("create: static () => new global::CoreWCF.NetTcpBinding()", result.SingleSource);
        }

        [Fact]
        public void SettableProperty_EmitsCastAndAssignment()
        {
            GeneratorResult result = Run(@"
    [ServiceModelConfigurable(typeof(global::CoreWCF.NetTcpBinding))]
    public partial class MyContext : ServiceModelConfigurationContext { }
");

            Assert.Empty(result.Errors);
            Assert.Contains(
                "static (o, v) => ((global::CoreWCF.NetTcpBinding)o).MaxReceivedMessageSize = (long)v",
                result.SingleSource);
        }

        [Fact]
        public void NestedPropertyType_IsReachedWithoutBeingListed()
        {
            GeneratorResult result = Run(@"
    [ServiceModelConfigurable(typeof(global::CoreWCF.NetTcpBinding))]
    public partial class MyContext : ServiceModelConfigurationContext { }
");

            Assert.Empty(result.Errors);

            // Listing the binding is meant to be enough: the walk finds what hydrating it touches.
            Assert.Contains("typeof(global::CoreWCF.NetTcpSecurity)", result.SingleSource);
            Assert.Contains("typeof(global::CoreWCF.TcpTransportSecurity)", result.SingleSource);
            Assert.Contains("typeof(global::System.Xml.XmlDictionaryReaderQuotas)", result.SingleSource);
        }

        [Fact]
        public void CollectionProperty_EmitsClosedGenericAdd()
        {
            GeneratorResult result = Run(@"
    [ServiceModelConfigurable(typeof(global::CoreWCF.Channels.CustomBinding))]
    [ServiceModelConfigurable(typeof(global::CoreWCF.Channels.HttpTransportBindingElement))]
    public partial class MyContext : ServiceModelConfigurationContext { }
");

            Assert.Empty(result.Errors);

            // The call NativeAOT cannot make reflectively - MakeGenericType over ICollection<T> - as a
            // cast the compiler closed.
            Assert.Contains(
                "((global::System.Collections.Generic.ICollection<global::CoreWCF.Channels.BindingElement>)c)" +
                ".Add((global::CoreWCF.Channels.BindingElement)i)",
                result.SingleSource);
        }

        [Fact]
        public void VocabularyType_EmitsItsWellKnownValues()
        {
            GeneratorResult result = Run(@"
    [ServiceModelConfigurable(typeof(global::CoreWCF.Channels.CustomBinding))]
    [ServiceModelConfigurable(typeof(global::CoreWCF.Channels.TextMessageEncodingBindingElement))]
    public partial class MyContext : ServiceModelConfigurationContext { }
");

            Assert.Empty(result.Errors);

            // MessageVersion has no TypeConverter at all; its values are public static members on
            // itself, which is what makes a hand written converter per type unnecessary.
            Assert.Contains("{ \"Soap12WSAddressing10\", global::CoreWCF.Channels.MessageVersion.Soap12WSAddressing10 }", result.SingleSource);
            Assert.Contains("{ \"None\", global::CoreWCF.Channels.MessageVersion.None }", result.SingleSource);
        }

        [Fact]
        public void ServiceAndContract_AreRootedRatherThanWalked()
        {
            GeneratorResult result = Run(@"
    [global::CoreWCF.ServiceContract]
    public interface IEcho { [global::CoreWCF.OperationContract] string Echo(string value); }

    public class EchoService : IEcho { public string Echo(string value) => value; }

    [ServiceModelConfigurable(typeof(EchoService))]
    [ServiceModelConfigurable(typeof(IEcho))]
    public partial class MyContext : ServiceModelConfigurationContext { }
");

            Assert.Empty(result.Errors);

            // A service is never hydrated, only handed to ConfigureService as a Type - so it is rooted
            // rather than walked. TypeLoader reflects over the contract, and without the dependency the
            // interface arrives trimmed to zero operations.
            Assert.Contains(
                "DynamicDependency(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All, typeof(global::App.EchoService))",
                result.SingleSource);
            Assert.Contains("typeof(global::App.IEcho))", result.SingleSource);
            Assert.DoesNotContain("Describe_App_EchoService", result.SingleSource);
        }

        [Fact]
        public void NonPartialContext_IsAnError()
        {
            GeneratorResult result = Run(@"
    [ServiceModelConfigurable(typeof(global::CoreWCF.NetTcpBinding))]
    public class MyContext : ServiceModelConfigurationContext { }
");

            Assert.Contains("COREWCF_0600", result.DiagnosticIds);
            Assert.Empty(result.GeneratedSources);
        }

        [Fact]
        public void ContextNotDerivingFromBase_IsAnError()
        {
            GeneratorResult result = Run(@"
    [ServiceModelConfigurable(typeof(global::CoreWCF.NetTcpBinding))]
    public partial class MyContext { }
");

            Assert.Contains("COREWCF_0601", result.DiagnosticIds);
            Assert.Empty(result.GeneratedSources);
        }

        [Fact]
        public void TwoTypesClaimingOneName_IsAnError()
        {
            GeneratorResult result = Run(@"
    [ServiceModelConfigurable(typeof(global::CoreWCF.NetTcpBinding), Name = ""shared"")]
    [ServiceModelConfigurable(typeof(global::CoreWCF.BasicHttpBinding), Name = ""shared"")]
    public partial class MyContext : ServiceModelConfigurationContext { }
");

            // The homonym problem the package exists to be deterministic about, answered at compile
            // time rather than by whichever assembly loaded first.
            Assert.Contains("COREWCF_0602", result.DiagnosticIds);
        }

        [Fact]
        public void CustomBindingWithoutAnyElement_SaysSo()
        {
            GeneratorResult result = Run(@"
    [ServiceModelConfigurable(typeof(global::CoreWCF.Channels.CustomBinding))]
    public partial class MyContext : ServiceModelConfigurationContext { }
");

            // Generated metadata for a CustomBinding whose Elements nothing can fill is metadata that
            // hydrates nothing.
            Assert.Contains("COREWCF_0607", result.DiagnosticIds);
        }

        [Fact]
        public void ListedBindingWithNoParameterlessConstructor_SaysSo()
        {
            GeneratorResult result = Run(@"
    public class NeedsAnArgument : global::CoreWCF.Channels.Binding
    {
        public NeedsAnArgument(int unused) { }
        public override string Scheme => ""needs"";
        public override global::CoreWCF.Channels.BindingElementCollection CreateBindingElements() => new();
    }

    [ServiceModelConfigurable(typeof(NeedsAnArgument))]
    public partial class MyContext : ServiceModelConfigurationContext { }
");

            Assert.Empty(result.Errors);
            Assert.Contains("create: null", result.SingleSource);
        }

        private static GeneratorResult Run(string body)
        {
            GeneratorResult result = GeneratorTestHarness.Run(Source(body));
            Assert.True(!result.Errors.Any() && !result.Crashes.Any(), result.ErrorReport);
            return result;
        }

        private static string Source(string body) => @"
using CoreWCF.Extensions.Configuration;

namespace App
{
" + body + @"
}
";
        }
}
