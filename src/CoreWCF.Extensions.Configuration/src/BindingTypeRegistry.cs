// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using CoreWCF.Channels;

namespace CoreWCF.Extensions.Configuration
{
    /// <summary>
    /// Resolves the type names used in configuration to name a <see cref="Binding"/> or a
    /// <see cref="BindingElement"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Configuration is a flat, untyped key/value store, so a polymorphic value such as a <see cref="Binding"/>
    /// cannot be created without an explicit type discriminator. A discriminator is an
    /// <see href="https://learn.microsoft.com/dotnet/api/system.type.assemblyqualifiedname">assembly qualified
    /// name</see>: <c>"CoreWCF.NetTcpBinding, CoreWCF.NetTcp"</c>.
    /// </para>
    /// <para>
    /// Short names are not accepted, and the reason is in this repository rather than in theory. CoreWCF ships
    /// client and server halves of the queue transports side by side, and they are deliberate homonyms:
    /// <c>CoreWCF.Channels.KafkaBinding</c> in <c>CoreWCF.Kafka</c> against
    /// <c>CoreWCF.ServiceModel.Channels.KafkaBinding</c> in <c>CoreWCF.Kafka.Client</c>, and the same for
    /// <c>RabbitMqBinding</c> and <c>RabbitMqTransportBindingElement</c>. Resolving <c>"KafkaBinding"</c> by short
    /// name picks whichever assembly was scanned last. Namespaces already disambiguate the two - client types live
    /// under <c>CoreWCF.ServiceModel.*</c>, mirroring <c>System.ServiceModel.*</c> - so the full name is the
    /// smallest unambiguous key, and it stays unambiguous when the client half of this feature lands next to the
    /// server half in the same configuration file.
    /// </para>
    /// <para>
    /// Requiring the assembly as well is what makes resolution deterministic. Transports load lazily, so a name
    /// resolved by searching the loaded assemblies would work or fail depending on what the application happened to
    /// touch first. An assembly qualified name loads its assembly instead of waiting for something else to, so a
    /// configuration that works on one machine works on all of them. This package therefore references
    /// <c>CoreWCF.Primitives</c> alone, and no transport.
    /// </para>
    /// <para>
    /// <see cref="Add"/> registers a type under a name of the host's choosing, for configuration that would rather
    /// not repeat an assembly qualified name.
    /// </para>
    /// </remarks>
    public sealed class BindingTypeRegistry
    {
        private readonly Dictionary<string, Type> _types = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Registers <paramref name="type"/> under its full name, so configuration can name it without the
        /// assembly.
        /// </summary>
        public BindingTypeRegistry Add(Type type)
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
        public BindingTypeRegistry Add(string name, Type type)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A name is required.", nameof(name));
            }

            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (_types.TryGetValue(name, out Type existing) && existing != type)
            {
                throw new BindingConfigurationException(
                    $"'{name}' is already registered for '{existing.AssemblyQualifiedName}' and cannot be " +
                    $"re-registered for '{type.AssemblyQualifiedName}'.");
            }

            _types[name] = type;
            return this;
        }

        /// <summary>
        /// Resolves <paramref name="name"/> to a concrete type assignable to <paramref name="baseType"/>.
        /// </summary>
        public Type Resolve(Type baseType, string name)
        {
            if (baseType == null)
            {
                throw new ArgumentNullException(nameof(baseType));
            }

            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A type name is required.", nameof(name));
            }

            if (!_types.TryGetValue(name, out Type type))
            {
                type = Type.GetType(name, throwOnError: false, ignoreCase: false);
            }

            if (type == null)
            {
                throw new BindingConfigurationException(
                    $"'{name}' did not resolve to a type. Name the {baseType.Name} with an assembly qualified " +
                    $"name, for example \"CoreWCF.NetTcpBinding, CoreWCF.NetTcp\"{RegisteredNames(baseType)}.");
            }

            if (!baseType.IsAssignableFrom(type))
            {
                throw new BindingConfigurationException(
                    $"'{name}' resolves to '{type.FullName}', which is not a {baseType.Name}.");
            }

            if (type.IsAbstract)
            {
                throw new BindingConfigurationException($"'{name}' resolves to abstract type '{type.FullName}'.");
            }

            return type;
        }

        /// <summary>
        /// Resolves <paramref name="name"/> to a concrete <see cref="Binding"/> type.
        /// </summary>
        public Type ResolveBinding(string name) => Resolve(typeof(Binding), name);

        /// <summary>
        /// Resolves <paramref name="name"/> to a concrete <see cref="BindingElement"/> type.
        /// </summary>
        public Type ResolveBindingElement(string name) => Resolve(typeof(BindingElement), name);

        private string RegisteredNames(Type baseType)
        {
            string[] names = _types
                .Where(pair => baseType.IsAssignableFrom(pair.Value))
                .Select(pair => pair.Key)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return names.Length == 0
                ? string.Empty
                : $", or one of the registered names: {string.Join(", ", names)}";
        }
    }
}
