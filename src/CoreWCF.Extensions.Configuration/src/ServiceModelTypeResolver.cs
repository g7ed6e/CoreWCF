// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace CoreWCF.Extensions.Configuration
{
    /// <summary>
    /// Resolves the service and contract type names that appear in configuration.
    /// </summary>
    /// <remarks>
    /// <see cref="Type.GetType(string)"/> only searches the calling assembly and the core library unless the name is
    /// assembly qualified, and service and contract types live in the application's own assemblies. So an
    /// assembly qualified name is honoured first, and a plain full name is then looked up across the loaded
    /// assemblies.
    /// </remarks>
    public class ServiceModelTypeResolver
    {
        private readonly Dictionary<string, Type> _cache = new Dictionary<string, Type>(StringComparer.Ordinal);

        /// <summary>
        /// Resolves <paramref name="typeName"/>, which may be an assembly qualified name or a namespace qualified
        /// full name.
        /// </summary>
        public Type Resolve(string typeName, string configurationPath)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                throw new BindingConfigurationException($"A type name is required (configuration path '{configurationPath}').");
            }

            if (_cache.TryGetValue(typeName, out Type cached))
            {
                return cached;
            }

            Type type = Type.GetType(typeName, throwOnError: false) ?? SearchLoadedAssemblies(typeName);

            if (type == null)
            {
                throw new BindingConfigurationException(
                    $"'{typeName}' did not resolve to a type in any loaded assembly " +
                    $"(configuration path '{configurationPath}').");
            }

            _cache[typeName] = type;
            return type;
        }

        private static Type SearchLoadedAssemblies(string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                {
                    continue;
                }

                Type type = assembly.GetType(typeName, throwOnError: false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
