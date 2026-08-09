// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CoreWCF.Extensions.Configuration.Tests
{
    /// <summary>
    /// Every type the end to end configuration names, listed once.
    /// </summary>
    /// <remarks>
    /// This is the whole ceremony the generated path asks of an application, and it is deliberately short: the
    /// bindings and elements the configuration file names, plus the service and its contract. Nothing here
    /// mentions <c>NetTcpSecurity</c>, <c>XmlDictionaryReaderQuotas</c> or <c>MessageVersion</c>, all of which
    /// the same configuration sets - the generator walks the property graph and finds them.
    /// </remarks>
    [ServiceModelConfigurable(typeof(BasicHttpBinding))]
    [ServiceModelConfigurable(typeof(NetHttpBinding))]
    [ServiceModelConfigurable(typeof(WSHttpBinding))]
    [ServiceModelConfigurable(typeof(NetTcpBinding))]
    [ServiceModelConfigurable(typeof(Channels.CustomBinding))]
    [ServiceModelConfigurable(typeof(Channels.TextMessageEncodingBindingElement))]
    [ServiceModelConfigurable(typeof(Channels.HttpTransportBindingElement))]
    [ServiceModelConfigurable(typeof(EchoService))]
    [ServiceModelConfigurable(typeof(IEchoService))]
    public partial class EndToEndConfigurationContext : ServiceModelConfigurationContext
    {
    }

    /// <summary>
    /// The end to end suite hydrating reflectively, which is what an application gets with no context at all.
    /// </summary>
    public class ReflectiveEndToEndTests : EndToEndTests
    {
        protected override ServiceModelConfigurationOptions Options => null;
    }

    /// <summary>
    /// The same suite hydrating from generated metadata, with the reflective path forbidden.
    /// </summary>
    /// <remarks>
    /// <see cref="ServiceModelConfigurationOptions.RequireGeneratedMetadata"/> is what makes this worth running.
    /// Without it a gap in the generator would be invisible: the reflective path would answer, every test would
    /// pass, and the claim that a configured host needs no reflection would go unchecked until someone published
    /// one with NativeAOT. With it, a type the context does not cover throws and names itself.
    /// <para>
    /// It is set explicitly rather than left to default, because these tests run on a runtime that does support
    /// dynamic code - the default would be false and the run would prove nothing.
    /// </para>
    /// </remarks>
    public class GeneratedMetadataEndToEndTests : EndToEndTests
    {
        protected override ServiceModelConfigurationOptions Options => new ServiceModelConfigurationOptions
        {
            Context = new EndToEndConfigurationContext(),
            RequireGeneratedMetadata = true,
        };
    }
}
