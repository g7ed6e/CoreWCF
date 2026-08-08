// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CoreWCF.DataContractSerialization.TestCorpus.Sanity;

namespace CoreWCF.DataContractSerialization.Tests
{
    /// <summary>
    /// The corpus types the source generator emits serializers for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Declared here rather than in the corpus project on purpose. Attaching the generator to the
    /// corpus would couple it to the oracle: generated code that failed to compile would break the
    /// corpus build and fail every reflection-based test too - the very tests that prove the
    /// generator wrong. Keeping it here means a generator bug fails only the generated tests.
    /// </para>
    /// <para>
    /// The cost is that generated code cannot reach a non-public <c>[DataMember]</c> across the
    /// assembly boundary. Exactly one corpus type has one today
    /// (<c>SerializationTestTypes.BaseDCNoIsRef._data</c>), and it is out of the first slice. When
    /// an in-slice case needs private access this moves into the corpus and the trade reverses.
    /// </para>
    /// <para>
    /// This list grows as the generator learns to emit more shapes; it is not the coverage report.
    /// The skip reasons in <c>GeneratedGoldenRecordTests</c> output are.
    /// </para>
    /// </remarks>
    [DataContractSerializable(typeof(SanityPrimitives))]
    [DataContractSerializable(typeof(SanityMemberAttributes))]
    public partial class GeneratedCorpusContext : DataContractSerializerContext
    {
    }
}
