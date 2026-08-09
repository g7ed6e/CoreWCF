// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace CoreWCF.Extensions.Configuration.Generator.Tests
{
    /// <summary>
    /// The only test compiled on net472.
    /// </summary>
    /// <remarks>
    /// The generator emits C# 11, and is gated off for .NETFramework precisely because that target
    /// cannot compile it. Running the generator tests there would exercise nothing that ships, so the
    /// whole test file set is excluded and this stands in - a test project with no tests fails the
    /// run rather than skipping it.
    /// </remarks>
    public class DummyTest
    {
        [Fact]
        public void GeneratorIsNotExercisedOnNetFramework() => Assert.True(true);
    }
}
