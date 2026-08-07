// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CoreWCF.Extensions.Configuration
{
    /// <summary>
    /// Options controlling how <see cref="BindingHydrator"/> reads configuration.
    /// </summary>
    public sealed class BindingHydratorOptions
    {
        /// <summary>
        /// The types reachable by name from configuration. Defaults to the CoreWCF bindings and binding elements.
        /// </summary>
        public BindingTypeRegistry Registry { get; set; } = BindingTypeRegistry.CreateDefault();

        /// <summary>
        /// The key naming the concrete type of a polymorphic value. Defaults to <c>Type</c>.
        /// </summary>
        public string DiscriminatorKey { get; set; } = "Type";
    }
}
