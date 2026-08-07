// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoreWCF.Channels;

namespace CoreWCF.Extensions.Configuration
{
    /// <summary>
    /// Maps the discriminator names used in configuration ("NetTcpBinding", "TextMessageEncodingBindingElement")
    /// onto the CLR types they identify.
    /// </summary>
    /// <remarks>
    /// Configuration is a flat, untyped key/value store, so a polymorphic value such as a <see cref="Binding"/> or a
    /// <see cref="BindingElement"/> cannot be created without an explicit type discriminator. This registry is the
    /// lookup behind that discriminator.
    /// </remarks>
    public sealed class BindingTypeRegistry
    {
        private readonly Dictionary<string, Type> _types = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Creates a registry populated from the CoreWCF assemblies referenced by this package.
        /// </summary>
        public static BindingTypeRegistry CreateDefault()
        {
            var registry = new BindingTypeRegistry();
            registry.AddFrom(typeof(Binding).Assembly);           // CoreWCF.Primitives
            registry.AddFrom(typeof(BasicHttpBinding).Assembly);  // CoreWCF.Http
            registry.AddFrom(typeof(NetTcpBinding).Assembly);     // CoreWCF.NetTcp
            return registry;
        }

        /// <summary>
        /// Registers every public, concrete, default-constructible <see cref="Binding"/> and
        /// <see cref="BindingElement"/> declared by <paramref name="assembly"/>.
        /// </summary>
        public BindingTypeRegistry AddFrom(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            foreach (Type type in assembly.GetExportedTypes())
            {
                if (IsRegisterable(type))
                {
                    Add(type);
                }
            }

            return this;
        }

        /// <summary>
        /// Registers a single type under its name and, for a "…Binding"/"…BindingElement" name, its short alias.
        /// </summary>
        public BindingTypeRegistry Add(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            _types[type.Name] = type;

            // "NetTcpBinding" is also reachable as "NetTcp", which is how <bindings> names them in wcf.config.
            string alias = StripSuffix(type.Name, "BindingElement") ?? StripSuffix(type.Name, "Binding");
            if (alias != null && !_types.ContainsKey(alias))
            {
                _types[alias] = type;
            }

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
                // Fall back to an assembly qualified name so types outside the registered assemblies stay reachable.
                type = Type.GetType(name, throwOnError: false, ignoreCase: true);
            }

            if (type == null)
            {
                throw new BindingConfigurationException(
                    $"'{name}' does not name a known {baseType.Name}. Known names: {KnownNames(baseType)}.");
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

        private string KnownNames(Type baseType) => string.Join(
            ", ",
            _types.Where(pair => baseType.IsAssignableFrom(pair.Value))
                  .Select(pair => pair.Key)
                  .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

        private static bool IsRegisterable(Type type) =>
            !type.IsAbstract &&
            (typeof(Binding).IsAssignableFrom(type) || typeof(BindingElement).IsAssignableFrom(type)) &&
            type.GetConstructor(Type.EmptyTypes) != null;

        private static string StripSuffix(string name, string suffix) =>
            name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal)
                ? name.Substring(0, name.Length - suffix.Length)
                : null;
    }
}
