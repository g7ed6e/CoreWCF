// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using CoreWCF.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CoreWCF.Extensions.Configuration
{
    /// <summary>
    /// Wires a service model configuration section into CoreWCF's <see cref="ServiceModelOptions"/>.
    /// </summary>
    public static class ServiceModelConfigurationExtensions
    {
        /// <summary>
        /// Configures the services and endpoints declared by <paramref name="serviceModelSection"/>.
        /// </summary>
        /// <remarks>
        /// The counterpart of <c>AddServiceModelConfigurationManagerFile</c>, reading an
        /// <see cref="IConfiguration"/> source instead of a wcf.config file.
        /// </remarks>
        public static IServiceCollection AddServiceModelConfiguration(
            this IServiceCollection services,
            IConfiguration serviceModelSection)
            => services.AddServiceModelConfiguration(serviceModelSection, context: null);

        /// <summary>
        /// Configures the services and endpoints declared by <paramref name="serviceModelSection"/>, using
        /// <paramref name="context"/> for the types it names.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A context is what makes this work under trimming and NativeAOT: it holds the types the
        /// configuration may name, rooted with <c>typeof</c> by the source generator, together with the
        /// members and conversions hydrating them needs. Without one every type is found by
        /// <see cref="Type.GetType(string)"/> and hydrated by reflection, which is correct on a runtime
        /// that supports dynamic code and broken on one that does not.
        /// </para>
        /// <code>
        /// [ServiceModelConfigurable(typeof(NetTcpBinding), Name = "netTcp")]
        /// [ServiceModelConfigurable(typeof(EchoService))]
        /// [ServiceModelConfigurable(typeof(IEchoService))]
        /// public partial class MyServiceModel : ServiceModelConfigurationContext { }
        ///
        /// services.AddServiceModelConfiguration(configuration.GetSection("ServiceModel"), new MyServiceModel());
        /// </code>
        /// </remarks>
        public static IServiceCollection AddServiceModelConfiguration(
            this IServiceCollection services,
            IConfiguration serviceModelSection,
            ServiceModelConfigurationContext context)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (serviceModelSection == null)
            {
                throw new ArgumentNullException(nameof(serviceModelSection));
            }

            // A CoreWCF host registers the options infrastructure itself, but this extension should stand on its own.
            services.AddOptions();

            services.TryAddSingleton<ServiceModelTypeRegistry>();

            if (context != null)
            {
                services.TryAddSingleton(context);
            }

            // A context registered separately is honoured too, so a host that composes its registrations
            // elsewhere does not have to route them through this call.
            services.TryAddSingleton(provider => new ServiceModelConfigurationOptions
            {
                Context = context ?? provider.GetService<ServiceModelConfigurationContext>(),
            });

            // Built from the container's registry rather than its own, so names a host registers are visible to
            // binding discriminators and not just to service and contract names.
            services.TryAddSingleton(provider =>
            {
                var options = provider.GetRequiredService<ServiceModelConfigurationOptions>();
                return new BindingHydrator(new BindingHydratorOptions
                {
                    Registry = provider.GetRequiredService<ServiceModelTypeRegistry>(),
                    Context = options.Context,
                    RequireGeneratedMetadata = options.RequireGeneratedMetadata,
                });
            });

            services.TryAddSingleton<ServiceModelConfigurationReader>();

            services.AddSingleton<IConfigureOptions<ServiceModelOptions>>(provider =>
                new ConfigureServiceModelOptions(
                    provider.GetRequiredService<ServiceModelConfigurationReader>(),
                    serviceModelSection));

            return services;
        }

        private sealed class ConfigureServiceModelOptions : IConfigureOptions<ServiceModelOptions>
        {
            private readonly ServiceModelConfigurationReader _reader;
            private readonly IConfiguration _serviceModelSection;

            public ConfigureServiceModelOptions(ServiceModelConfigurationReader reader, IConfiguration serviceModelSection)
            {
                _reader = reader;
                _serviceModelSection = serviceModelSection;
            }

            public void Configure(ServiceModelOptions options)
            {
                foreach (ServiceEndpointDefinition definition in _reader.ReadEndpoints(_serviceModelSection))
                {
                    options.ConfigureService(
                        definition.ServiceType,
                        service => service.AddServiceEndpoint(
                            definition.Contract,
                            definition.Binding,
                            definition.Address,
                            definition.ListenUri));
                }
            }
        }
    }
}
