// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using CoreWCF.Channels;

namespace CoreWCF.Extensions.Configuration
{
    /// <summary>
    /// Turns the type names a configuration section uses into types, preferring the sources a trimmer can
    /// follow.
    /// </summary>
    /// <remarks>
    /// Three sources, in order, and the order is the whole design. A name the host registered with
    /// <c>typeof</c> comes first, then one the generated <see cref="ServiceModelConfigurationContext"/>
    /// lists - also <c>typeof</c>, just written by the generator - and only then
    /// <see cref="Type.GetType(string)"/>, which is the one that cannot survive trimming. Under
    /// <see cref="ServiceModelConfigurationOptions.RequireGeneratedMetadata"/> the third source is not
    /// consulted at all and the miss becomes an error naming the attribute that fixes it.
    /// </remarks>
    internal sealed class ServiceModelTypeResolver
    {
        private readonly ServiceModelTypeRegistry _registry;
        private readonly ServiceModelConfigurationContext _context;
        private readonly bool _requireGeneratedMetadata;
        private readonly Dictionary<string, Type> _resolved = new Dictionary<string, Type>(StringComparer.Ordinal);

        public ServiceModelTypeResolver(
            ServiceModelTypeRegistry registry,
            ServiceModelConfigurationContext context,
            bool requireGeneratedMetadata)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _context = context;
            _requireGeneratedMetadata = requireGeneratedMetadata;
        }

        public ServiceModelTypeRegistry Registry => _registry;

        public ServiceModelConfigurationContext Context => _context;

        public bool RequireGeneratedMetadata => _requireGeneratedMetadata;

        /// <summary>
        /// Resolves <paramref name="name"/> to a type.
        /// </summary>
        public Type Resolve(string name, string configurationPath)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new BindingConfigurationException(
                    $"A type name is required (configuration path '{configurationPath}').");
            }

            if (_registry.TryGetType(name, out Type type))
            {
                return type;
            }

            type = _context?.ResolveType(name);
            if (type != null)
            {
                return type;
            }

            if (_resolved.TryGetValue(name, out type))
            {
                return type;
            }

            if (_requireGeneratedMetadata)
            {
                throw new BindingConfigurationException(
                    $"'{name}' is not listed by the {ContextName()} and cannot be resolved by name because " +
                    $"this runtime does not support dynamic code (configuration path '{configurationPath}'). " +
                    $"Add [ServiceModelConfigurable(typeof({ShortName(name)}))] to it{RegisteredNames()}.");
            }

            type = ReflectionFallback.ResolveType(name);

            if (type == null)
            {
                throw new BindingConfigurationException(
                    $"'{name}' did not resolve to a type (configuration path '{configurationPath}'). Types are " +
                    $"named by assembly qualified name, for example " +
                    $"\"CoreWCF.NetTcpBinding, CoreWCF.NetTcp\"{RegisteredNames()}.");
            }

            _resolved[name] = type;
            return type;
        }

        /// <summary>
        /// Resolves <paramref name="name"/> to a type assignable to <paramref name="baseType"/>.
        /// </summary>
        public Type Resolve(Type baseType, string name, string configurationPath)
        {
            if (baseType == null)
            {
                throw new ArgumentNullException(nameof(baseType));
            }

            Type type = Resolve(name, configurationPath);

            if (!baseType.IsAssignableFrom(type))
            {
                throw new BindingConfigurationException(
                    $"'{name}' resolves to '{type.FullName}', which is not a {baseType.Name} " +
                    $"(configuration path '{configurationPath}').");
            }

            return type;
        }

        /// <summary>
        /// Resolves <paramref name="name"/> to a <see cref="Binding"/> type.
        /// </summary>
        public Type ResolveBinding(string name, string configurationPath) =>
            Resolve(typeof(Binding), name, configurationPath);

        /// <summary>
        /// Resolves <paramref name="name"/> to a <see cref="BindingElement"/> type.
        /// </summary>
        public Type ResolveBindingElement(string name, string configurationPath) =>
            Resolve(typeof(BindingElement), name, configurationPath);

        private string ContextName() =>
            _context == null ? "service model configuration context (none was supplied)" : _context.GetType().Name;

        /// <summary>
        /// The bare type name inside an assembly qualified name, so the suggested attribute reads as
        /// something that could be typed.
        /// </summary>
        private static string ShortName(string name)
        {
            int comma = name.IndexOf(',');
            return comma < 0 ? name : name.Substring(0, comma).Trim();
        }

        private string RegisteredNames()
        {
            string[] names = _registry.Names.ToArray();
            if (names.Length == 0)
            {
                return string.Empty;
            }

            return $", or use one of the registered names: {string.Join(", ", names)}";
        }
    }
}
