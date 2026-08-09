// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CoreWCF.Channels;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CoreWCF.Extensions.Configuration.Tests
{
    /// <summary>
    /// A context that lists nothing, so the generator never runs for it and every virtual keeps returning null.
    /// </summary>
    /// <remarks>
    /// This is not a mock. It is exactly what an application gets when it declares a context and forgets to list
    /// the type its configuration names, which is the mistake the strict mode exists to report.
    /// </remarks>
    public partial class EmptyConfigurationContext : ServiceModelConfigurationContext
    {
    }

    /// <summary>
    /// That the generated metadata is what answers, rather than the reflective path quietly picking up the slack.
    /// </summary>
    /// <remarks>
    /// <see cref="GeneratedMetadataEndToEndTests"/> runs the whole suite under
    /// <see cref="ServiceModelConfigurationOptions.RequireGeneratedMetadata"/> and passing means nothing unless
    /// that flag can actually fail. These are the pair that establishes it: the same configuration, hydrated by a
    /// context that lists the type and by one that does not.
    /// </remarks>
    public class GeneratedMetadataTests
    {
        private static IConfigurationSection Section(params (string Key, string Value)[] values)
        {
            var data = new Dictionary<string, string>();
            foreach ((string key, string value) in values)
            {
                data["Binding:" + key] = value;
            }

            return new ConfigurationBuilder().AddInMemoryCollection(data).Build().GetSection("Binding");
        }

        private static IConfigurationSection NetTcpSection() => Section(
            ("Type", "CoreWCF.NetTcpBinding, CoreWCF.NetTcp"),
            ("MaxReceivedMessageSize", "2097152"),
            ("ReceiveTimeout", "00:05:00"),
            ("Security:Mode", "Transport"),
            ("Security:Transport:ClientCredentialType", "Certificate"));

        [Fact]
        public void ListedType_HydratesWithoutReflection()
        {
            var hydrator = new BindingHydrator(new BindingHydratorOptions
            {
                Context = new EndToEndConfigurationContext(),
                RequireGeneratedMetadata = true,
            });

            var binding = Assert.IsType<NetTcpBinding>(hydrator.CreateBinding(NetTcpSection()));

            // Reached through generated members: a scalar on the binding, a converted TimeSpan, an enum on a
            // nested object the context never listed, and an enum one level below that.
            Assert.Equal(2097152, binding.MaxReceivedMessageSize);
            Assert.Equal(System.TimeSpan.FromMinutes(5), binding.ReceiveTimeout);
            Assert.Equal(SecurityMode.Transport, binding.Security.Mode);
            Assert.Equal(TcpClientCredentialType.Certificate, binding.Security.Transport.ClientCredentialType);
        }

        [Fact]
        public void UnlistedType_UnderStrictMode_ThrowsNamingTheAttribute()
        {
            var hydrator = new BindingHydrator(new BindingHydratorOptions
            {
                Context = new EmptyConfigurationContext(),
                RequireGeneratedMetadata = true,
            });

            BindingConfigurationException exception =
                Assert.Throws<BindingConfigurationException>(() => hydrator.CreateBinding(NetTcpSection()));

            // The message has to carry the fix, because the situation it reports is one the developer cannot
            // reproduce on the machine they are reading it on - it only arises where dynamic code is unavailable.
            Assert.Contains("ServiceModelConfigurable", exception.Message);
            Assert.Contains("NetTcpBinding", exception.Message);
        }

        [Fact]
        public void UnlistedType_WithoutStrictMode_StillHydratesReflectively()
        {
            var hydrator = new BindingHydrator(new BindingHydratorOptions
            {
                Context = new EmptyConfigurationContext(),
                RequireGeneratedMetadata = false,
            });

            // A context that covers nothing has to be no worse than no context at all: the fallback is what
            // makes adopting the generated path one type at a time possible.
            var binding = Assert.IsType<NetTcpBinding>(hydrator.CreateBinding(NetTcpSection()));

            Assert.Equal(2097152, binding.MaxReceivedMessageSize);
            Assert.Equal(SecurityMode.Transport, binding.Security.Mode);
        }

        [Fact]
        public void ContextResolvesTheNamesConfigurationUses()
        {
            var context = new EndToEndConfigurationContext();

            // The assembly qualified name a configuration file written against the reflective path already uses,
            // and the bare full name, both without loading anything by string.
            Assert.Equal(typeof(NetTcpBinding), context.ResolveType("CoreWCF.NetTcpBinding, CoreWCF.NetTcp"));
            Assert.Equal(typeof(NetTcpBinding), context.ResolveType("CoreWCF.NetTcpBinding"));
            Assert.Null(context.ResolveType("CoreWCF.NetHttpsBinding, CoreWCF.Http"));
        }

        [Fact]
        public void ContextCoversTheGraphBelowAListedBinding()
        {
            var context = new EndToEndConfigurationContext();

            // Nothing lists these; the property graph walk reached them.
            Assert.NotNull(context.GetConfiguredType(typeof(NetTcpSecurity)));
            Assert.NotNull(context.GetConfiguredType(typeof(TcpTransportSecurity)));
            Assert.NotNull(context.GetConfiguredType(typeof(System.Xml.XmlDictionaryReaderQuotas)));
        }

        [Fact]
        public void CollectionsAreAppendedThroughAClosedGeneric()
        {
            var hydrator = new BindingHydrator(new BindingHydratorOptions
            {
                Context = new EndToEndConfigurationContext(),
                RequireGeneratedMetadata = true,
            });

            // CustomBinding.Elements is the one place the reflective path constructs a generic type at run time,
            // which NativeAOT cannot do at all. Strict mode here means the generated closed cast is what ran.
            var binding = Assert.IsType<CustomBinding>(hydrator.CreateBinding(Section(
                ("Type", "CoreWCF.Channels.CustomBinding, CoreWCF.Primitives"),
                ("Elements:0:Type", "CoreWCF.Channels.TextMessageEncodingBindingElement, CoreWCF.Primitives"),
                ("Elements:0:MessageVersion", "Soap11"),
                ("Elements:1:Type", "CoreWCF.Channels.HttpTransportBindingElement, CoreWCF.Http"),
                ("Elements:1:MaxReceivedMessageSize", "1048576"))));

            Assert.Equal(2, binding.Elements.Count);
            var encoding = Assert.IsType<TextMessageEncodingBindingElement>(binding.Elements[0]);

            // MessageVersion has no TypeConverter; this value came from the generated vocabulary table.
            Assert.Equal(MessageVersion.Soap11, encoding.MessageVersion);
        }
    }
}
