// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace CoreWCF.DataContractSerialization.Generator;

public sealed partial class DataContractSerializerGenerator
{
    /// <summary>One data contract, reduced to what the emitter needs to write it.</summary>
    /// <remarks>
    /// A contract the generator cannot yet write is kept in the spec with
    /// <see cref="UnsupportedReason"/> set rather than dropped, so the emitter can record why in a
    /// comment. No serializer is emitted for it and <c>GetSerializer</c> returns null, which sends
    /// CoreWCF back to the reflection-based serializer - a correct outcome, not a failure.
    /// </remarks>
    internal sealed record ContractSpec(
        string FullyQualifiedName,
        string ContractName,
        string ContractNamespace,
        EquatableArray<MemberSpec> Members,
        string? UnsupportedReason,
        string? BaseContractFullyQualifiedName,
        bool IsRoot) : IEquatable<ContractSpec>
    {
        public bool IsSupported => UnsupportedReason is null;

        /// <summary>Same contract, newly declared unsupported.</summary>
        public ContractSpec WithUnsupportedReason(string reason) =>
            UnsupportedReason is null ? this with { UnsupportedReason = reason } : this;
    }

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
        MemberKind Kind,
        bool IsNullableValueType,
        string? NestedContractFullyQualifiedName,
        string? ChildNamespaceToDeclare) : IEquatable<MemberSpec>;

    /// <summary>
    /// How a member's value is written. Mirrors the primitive cases XmlWriterDelegator handles in
    /// dotnet/runtime, plus <see cref="Contract"/> for a member that is itself a data contract;
    /// anything not listed here makes its contract unsupported for now.
    /// </summary>
    internal enum MemberKind
    {
        Unsupported = 0,
        Boolean,
        Byte,
        SByte,
        Int16,
        UInt16,
        Int32,
        UInt32,
        Int64,
        UInt64,
        Single,
        Double,
        Decimal,
        Char,
        String,
        Guid,
        DateTime,
        TimeSpan,
        ByteArray,
        Contract
    }
}
