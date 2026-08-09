// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;
using static VerifyXunit.Verifier;

namespace CoreWCF.Extensions.Configuration.Generator.Tests
{
    /// <summary>
    /// The generated code itself, checked in so it can be read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The assertions next door pin one line at a time, which is right for stating a rule and wrong
    /// for seeing the shape of the whole thing. The question a reviewer of this generator actually
    /// has is "what does a context look like now, and what changed" - a diff of the emitter cannot
    /// answer it, and a snapshot can.
    /// </para>
    /// <para>
    /// A snapshot is a record, not an assertion: it says that output changed and by how much, not
    /// that output is correct. What says it is correct is that the emitted code compiles against the
    /// real binding types, which every test here and next door checks.
    /// </para>
    /// <para>
    /// Three cases rather than one per feature. Each snapshot carries the whole context, so they
    /// overlap heavily; these are the structurally distinct shapes - a binding with a nested property
    /// graph, a CustomBinding with a polymorphic element list, and a service with a contract, which
    /// is rooted rather than hydrated.
    /// </para>
    /// </remarks>
    public class GeneratedSourceSnapshotTests
    {
        [Fact]
        public Task Binding()
        {
            GeneratorDriver driver = GeneratorTestHarness.RunForSnapshot(Source(@"
    [ServiceModelConfigurable(typeof(global::CoreWCF.NetTcpBinding), Name = ""netTcp"")]
    public partial class MyContext : ServiceModelConfigurationContext { }
"));

            return Verify(driver).UseDirectory("Snapshots");
        }

        [Fact]
        public Task CustomBindingWithElements()
        {
            GeneratorDriver driver = GeneratorTestHarness.RunForSnapshot(Source(@"
    [ServiceModelConfigurable(typeof(global::CoreWCF.Channels.CustomBinding), Name = ""custom"")]
    [ServiceModelConfigurable(typeof(global::CoreWCF.Channels.TextMessageEncodingBindingElement), Name = ""textEncoding"")]
    [ServiceModelConfigurable(typeof(global::CoreWCF.Channels.HttpTransportBindingElement), Name = ""httpTransport"")]
    public partial class MyContext : ServiceModelConfigurationContext { }
"));

            return Verify(driver).UseDirectory("Snapshots");
        }

        [Fact]
        public Task ServiceAndContract()
        {
            GeneratorDriver driver = GeneratorTestHarness.RunForSnapshot(Source(@"
    [global::CoreWCF.ServiceContract]
    public interface IEcho { [global::CoreWCF.OperationContract] string Echo(string value); }

    public class EchoService : IEcho { public string Echo(string value) => value; }

    [ServiceModelConfigurable(typeof(EchoService), Name = ""echo"")]
    [ServiceModelConfigurable(typeof(IEcho))]
    public partial class MyContext : ServiceModelConfigurationContext { }
"));

            return Verify(driver).UseDirectory("Snapshots");
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
