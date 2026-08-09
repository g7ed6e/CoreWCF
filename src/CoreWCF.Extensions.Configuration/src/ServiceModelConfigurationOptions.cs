// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace CoreWCF.Extensions.Configuration
{
    /// <summary>
    /// Options controlling how a service model configuration section is read.
    /// </summary>
    public sealed class ServiceModelConfigurationOptions
    {
        private bool? _requireGeneratedMetadata;

        /// <summary>
        /// The context supplying generated, trim-safe hydration metadata. When null, every type is resolved
        /// and hydrated by reflection.
        /// </summary>
        public ServiceModelConfigurationContext Context { get; set; }

        /// <summary>
        /// Whether a type the <see cref="Context"/> does not cover is an error rather than something to
        /// hydrate reflectively.
        /// </summary>
        /// <remarks>
        /// Defaults to true exactly when the runtime does not support dynamic code, which is the case that
        /// matters: under NativeAOT the reflective path is the broken one, so falling back to it silently
        /// produces a host that starts and then misbehaves. Failing instead, with a message naming the
        /// <c>[ServiceModelConfigurable]</c> line to add, turns that into something actionable. Set it
        /// explicitly to exercise the same strictness on a runtime that does support dynamic code.
        /// </remarks>
        public bool RequireGeneratedMetadata
        {
            get => _requireGeneratedMetadata ?? !RuntimeFeatureSwitches.IsDynamicCodeSupported;
            set => _requireGeneratedMetadata = value;
        }
    }
}
