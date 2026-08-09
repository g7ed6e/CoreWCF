// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CoreWCF.Extensions.Configuration
{
    /// <summary>
    /// Options controlling how <see cref="BindingHydrator"/> reads configuration.
    /// </summary>
    public sealed class BindingHydratorOptions
    {
        private bool? _requireGeneratedMetadata;

        /// <summary>
        /// Names the host chose for the types configuration mentions. Empty by default: nothing is
        /// discovered, so a type is named by its assembly qualified name unless the host registered a
        /// shorter one or the <see cref="Context"/> lists it. See <see cref="ServiceModelTypeRegistry"/>.
        /// </summary>
        public ServiceModelTypeRegistry Registry { get; set; } = new ServiceModelTypeRegistry();

        /// <summary>
        /// The generated context supplying trim-safe hydration metadata, or null to hydrate reflectively.
        /// See <see cref="ServiceModelConfigurationContext"/>.
        /// </summary>
        public ServiceModelConfigurationContext Context { get; set; }

        /// <summary>
        /// Whether a type the <see cref="Context"/> does not cover is an error rather than something to
        /// hydrate reflectively. Defaults to true exactly when the runtime does not support dynamic code.
        /// See <see cref="ServiceModelConfigurationOptions.RequireGeneratedMetadata"/>.
        /// </summary>
        public bool RequireGeneratedMetadata
        {
            get => _requireGeneratedMetadata ?? !RuntimeFeatureSwitches.IsDynamicCodeSupported;
            set => _requireGeneratedMetadata = value;
        }

        /// <summary>
        /// The key naming the concrete type of a polymorphic value. Defaults to <c>Type</c>.
        /// </summary>
        public string DiscriminatorKey { get; set; } = "Type";
    }
}
