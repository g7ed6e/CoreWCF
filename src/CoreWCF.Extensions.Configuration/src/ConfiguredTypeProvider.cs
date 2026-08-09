// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace CoreWCF.Extensions.Configuration
{
    /// <summary>
    /// Supplies the <see cref="ConfiguredType"/> the binder needs for a type, from the generated context
    /// when it lists that type and from reflection when it does not.
    /// </summary>
    /// <remarks>
    /// The binder never learns which of the two it got, so there is exactly one traversal rather than a
    /// generated one and a reflective one that have to be kept agreeing. What changes between them is only
    /// how a member is reached.
    /// </remarks>
    internal sealed class ConfiguredTypeProvider
    {
        private readonly ServiceModelConfigurationContext _context;
        private readonly bool _requireGeneratedMetadata;
        private readonly Dictionary<Type, ConfiguredType> _cache = new Dictionary<Type, ConfiguredType>();

        public ConfiguredTypeProvider(ServiceModelConfigurationContext context, bool requireGeneratedMetadata)
        {
            _context = context;
            _requireGeneratedMetadata = requireGeneratedMetadata;
        }

        public ServiceModelConfigurationContext Context => _context;

        public bool RequireGeneratedMetadata => _requireGeneratedMetadata;

        public ConfiguredType Get(Type type, string configurationPath)
        {
            if (_cache.TryGetValue(type, out ConfiguredType configured))
            {
                return configured;
            }

            configured = _context?.GetConfiguredType(type);

            if (configured == null)
            {
                if (_requireGeneratedMetadata)
                {
                    throw new BindingConfigurationException(
                        $"'{type.FullName}' is not listed by the service model configuration context, so it " +
                        $"cannot be hydrated on a runtime that does not support dynamic code " +
                        $"(configuration path '{configurationPath}'). Add " +
                        $"[ServiceModelConfigurable(typeof({type.Name}))] to it.");
                }

                configured = ReflectionFallback.Describe(type);
            }

            _cache[type] = configured;
            return configured;
        }
    }
}
