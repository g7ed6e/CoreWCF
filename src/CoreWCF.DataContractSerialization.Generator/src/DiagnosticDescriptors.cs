// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.CodeAnalysis;

namespace CoreWCF.DataContractSerialization.Generator;

/// <summary>
/// Diagnostics reported by <see cref="DataContractSerializerGenerator"/>.
/// </summary>
/// <remarks>
/// Uses the COREWCF_04XX band; 01XX-03XX belong to CoreWCF.BuildTools. Every id here must also
/// appear in AnalyzerReleases.Unshipped.md or RS2000/RS2008 fail the build.
/// <para>
/// These fire only for input that is definitely wrong - a context that cannot be generated into, or
/// a type that is not a data contract at all. A contract the generator merely does not support yet
/// is silently left to the reflection-based serializer, because the generated path is an
/// optimization and falling back is a correct outcome, not an error.
/// </para>
/// </remarks>
internal static class DiagnosticDescriptors
{
    private const string Category = nameof(DataContractSerializerGenerator);

    internal static readonly DiagnosticDescriptor ContextMustBePartial = new(
        id: "COREWCF_0400",
        title: "DataContractSerializerContext must be partial",
        messageFormat: "Class '{0}' carries [DataContractSerializable] but is not partial, so no serializers can be generated into it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ContextMustDeriveFromBase = new(
        id: "COREWCF_0401",
        title: "DataContractSerializerContext must derive from DataContractSerializerContext",
        messageFormat: "Class '{0}' carries [DataContractSerializable] but does not derive from CoreWCF.DataContractSerialization.DataContractSerializerContext",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor TypeIsNotADataContract = new(
        id: "COREWCF_0402",
        title: "Type is not a data contract",
        messageFormat: "Type '{0}' is listed in [DataContractSerializable] but has no [DataContract] attribute; only explicit data contracts are supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
