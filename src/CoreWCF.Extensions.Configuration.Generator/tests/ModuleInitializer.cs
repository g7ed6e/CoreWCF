// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using VerifyTests;

namespace CoreWCF.Extensions.Configuration.Generator.Tests
{
    internal static class ModuleInitializer
    {
        /// <summary>
        /// Teaches Verify how to render a <c>GeneratorDriver</c> run.
        /// </summary>
        /// <remarks>
        /// Snapshots live in a folder of their own rather than beside the test file: each is hundreds
        /// of lines of generated code, and interleaved with the source they would bury it.
        /// <para>
        /// One snapshot serves every target framework. The generator's output is a function of the
        /// types it is given rather than of the runtime the test host happens to be on, and having all
        /// of them compared against the same file is what re-checks that claim on every run.
        /// </para>
        /// </remarks>
        [ModuleInitializer]
        internal static void Initialize()
        {
            VerifySourceGenerators.Initialize();
        }
    }
}
