// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace CoreWCF.DataContractSerialization.Generator;

public sealed partial class DataContractSerializerGenerator
{
    /// <summary>One data contract, reduced to what the emitter needs to write it.</summary>
    internal sealed record ContractSpec(
        string FullyQualifiedName,
        string ContractName,
        string ContractNamespace,
        EquatableArray<MemberSpec> Members) : IEquatable<ContractSpec>;

    /// <summary>
    /// One <c>[DataMember]</c>, already resolved to its wire name and ordering key.
    /// </summary>
    /// <remarks>
    /// <paramref name="Order"/> is the raw attribute value: -1 when unspecified, and never negative
    /// otherwise because DataMemberAttribute's setter rejects negatives. Members therefore sort by
    /// Order ascending then by ordinal comparison of <paramref name="Name"/>, which puts every
    /// unordered member ahead of every ordered one - including Order = 0. See
    /// ClassDataContract.DataMemberComparer in dotnet/runtime.
    /// </remarks>
    internal sealed record MemberSpec(
        string Name,
        int Order,
        bool EmitDefaultValue,
        bool IsRequired,
        string MemberName,
        string TypeFullyQualifiedName) : IEquatable<MemberSpec>;
}
