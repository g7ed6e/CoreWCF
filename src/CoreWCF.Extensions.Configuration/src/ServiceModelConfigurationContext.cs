// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace CoreWCF.Extensions.Configuration
{
    /// <summary>
    /// Base class for a user-declared partial class that the source generator fills in with one
    /// <see cref="ConfiguredType"/> per <see cref="ServiceModelConfigurableAttribute"/>, plus the name to
    /// type map that replaces <see cref="Type.GetType(string)"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// [ServiceModelConfigurable(typeof(NetTcpBinding), Name = "netTcp")]
    /// [ServiceModelConfigurable(typeof(EchoService))]
    /// public partial class MyServiceModel : ServiceModelConfigurationContext { }
    ///
    /// services.AddServiceModelConfiguration(configuration.GetSection("ServiceModel"), new MyServiceModel());
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// Both members are deliberately <c>virtual</c> returning <c>null</c> rather than <c>abstract</c>. The
    /// generator is gated to target frameworks whose default language version supports the code it emits,
    /// so on .NET Framework and netstandard it never runs and a user's partial class is never completed.
    /// An abstract member would leave that class uncompilable; returning null instead means the same source
    /// compiles everywhere and simply contributes nothing where generation did not happen, falling back to
    /// the reflection based path with no conditional compilation in user code.
    /// </para>
    /// <para>
    /// That is the same shape as <c>DataContractSerializerContext</c>, and for the same reason: the
    /// generated path is how this feature survives trimming, not a second implementation of it.
    /// </para>
    /// </remarks>
    public abstract class ServiceModelConfigurationContext
    {
        /// <summary>
        /// Resolves a name configuration used - an assembly qualified name, or a short name given by
        /// <see cref="ServiceModelConfigurableAttribute.Name"/> - to a type this context lists, or
        /// <see langword="null"/> when it lists no such name.
        /// </summary>
        public virtual Type ResolveType(string name) => null;

        /// <summary>
        /// Returns the generated hydration metadata for <paramref name="type"/>, or <see langword="null"/>
        /// when this context has none.
        /// </summary>
        public virtual ConfiguredType GetConfiguredType(Type type) => null;
    }
}
