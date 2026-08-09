// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreWCF.Extensions.Configuration
{
    /// <summary>
    /// Names a host chooses for the types its service model configuration section mentions: bindings,
    /// binding elements, service implementations and contracts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A name that is not registered here falls to a
    /// <see href="https://learn.microsoft.com/dotnet/api/system.type.assemblyqualifiedname">assembly
    /// qualified name</see>: <c>"CoreWCF.NetTcpBinding, CoreWCF.NetTcp"</c>. Short names are not accepted
    /// there, and neither are bare full names.
    /// </para>
    /// <para>
    /// The reason is in this repository rather than in theory. CoreWCF ships client and server halves of
    /// the queue transports side by side as deliberate homonyms: <c>CoreWCF.Channels.KafkaBinding</c> in
    /// <c>CoreWCF.Kafka</c> against <c>CoreWCF.ServiceModel.Channels.KafkaBinding</c> in
    /// <c>CoreWCF.Kafka.Client</c>, and the same for <c>RabbitMqBinding</c> and
    /// <c>RabbitMqTransportBindingElement</c>. Resolving <c>"KafkaBinding"</c> by short name picks whichever
    /// assembly was scanned last. Namespaces already disambiguate the two - client types live under
    /// <c>CoreWCF.ServiceModel.*</c>, mirroring <c>System.ServiceModel.*</c> - so the full name is the
    /// smallest unambiguous key.
    /// </para>
    /// <para>
    /// Naming the assembly as well is what makes that fallback deterministic. An assembly that is not
    /// loaded yet cannot be searched, so a name resolved by scanning loaded assemblies answers according to
    /// whatever the application happened to touch first: right on the machine where the configuration was
    /// written, wrong elsewhere. Transports load lazily, and so does a class library holding service
    /// implementations. An assembly qualified name loads its assembly instead of waiting for something
    /// else to.
    /// </para>
    /// <para>
    /// What it cannot do is survive trimming: a type named only by a string is a type nothing references,
    /// and the trimmer removes it. Registering here with <c>typeof</c> is the answer to that, and
    /// <see cref="ServiceModelConfigurableAttribute"/> is the same answer generated rather than written -
    /// see <see cref="ServiceModelConfigurationContext"/>.
    /// </para>
    /// </remarks>
    public sealed class ServiceModelTypeRegistry
    {
        private readonly Dictionary<string, Type> _registered = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Registers <typeparamref name="T"/> under its full name, so configuration can name it without the
        /// assembly.
        /// </summary>
        public ServiceModelTypeRegistry Add<T>() => Add(typeof(T));

        /// <summary>
        /// Registers <typeparamref name="T"/> under <paramref name="name"/>.
        /// </summary>
        public ServiceModelTypeRegistry Add<T>(string name) => Add(name, typeof(T));

        /// <summary>
        /// Registers <paramref name="type"/> under its full name, so configuration can name it without the
        /// assembly.
        /// </summary>
        public ServiceModelTypeRegistry Add(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            return Add(type.FullName, type);
        }

        /// <summary>
        /// Registers <paramref name="type"/> under <paramref name="name"/>.
        /// </summary>
        public ServiceModelTypeRegistry Add(string name, Type type)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A name is required.", nameof(name));
            }

            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (_registered.TryGetValue(name, out Type existing) && existing != type)
            {
                throw new BindingConfigurationException(
                    $"'{name}' is already registered for '{existing.AssemblyQualifiedName}' and cannot be " +
                    $"re-registered for '{type.AssemblyQualifiedName}'.");
            }

            _registered[name] = type;
            return this;
        }

        /// <summary>
        /// Looks <paramref name="name"/> up among the registered names.
        /// </summary>
        public bool TryGetType(string name, out Type type) => _registered.TryGetValue(name, out type);

        /// <summary>
        /// The registered names, ordered, for inclusion in an error message.
        /// </summary>
        public IEnumerable<string> Names => _registered.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
    }
}
