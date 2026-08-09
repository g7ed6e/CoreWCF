// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace CoreWCF.Extensions.Configuration
{
    /// <summary>
    /// Probes the runtime for whether dynamic code is available.
    /// </summary>
    /// <remarks>
    /// <c>RuntimeFeature.IsDynamicCodeSupported</c> does not exist on netstandard2.0, so this reads the
    /// AppContext switch behind it instead - the same probe, spelled the same way, as
    /// <c>CoreWCF.Dispatcher.InvokerUtil</c> and <c>DispatchOperationRuntimeHelpers</c> in
    /// CoreWCF.Primitives. Absent means a runtime that never publishes it, which is a runtime that supports
    /// dynamic code.
    /// <para>
    /// The value is read once per process on first use. A test that sets the switch after something else
    /// has already read it gets the old answer and presents as a pass rather than a skip, so set it before
    /// the first configuration is read.
    /// </para>
    /// </remarks>
    internal static class RuntimeFeatureSwitches
    {
        private const string IsDynamicCodeSupportedSwitch =
            "System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported";

        private static readonly Lazy<bool> s_isDynamicCodeSupported = new Lazy<bool>(() =>
            !AppContext.TryGetSwitch(IsDynamicCodeSupportedSwitch, out bool supported) || supported);

        public static bool IsDynamicCodeSupported => s_isDynamicCodeSupported.Value;
    }
}
