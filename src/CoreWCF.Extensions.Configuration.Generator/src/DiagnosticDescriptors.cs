// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.CodeAnalysis;

namespace CoreWCF.Extensions.Configuration.Generator;

/// <summary>
/// Diagnostics reported by <see cref="ServiceModelConfigurationGenerator"/>.
/// </summary>
/// <remarks>
/// <para>
/// Uses the COREWCF_06XX band. 01XX-03XX belong to CoreWCF.BuildTools, 04XX to the
/// DataContractSerializer generator, and 05XX is left clear for the service description generator.
/// Every id here must also appear in AnalyzerReleases.Unshipped.md or RS2000/RS2008 fail the build.
/// </para>
/// <para>
/// 0600-0602 fire for input that is definitely wrong: a context that cannot be generated into, or a
/// name that would resolve to two types. The rest report a fallback to the reflection based path,
/// and are warnings rather than errors because a fallback is correct behaviour on a runtime that
/// supports dynamic code - failing the build would make adopting this one type at a time impossible.
/// </para>
/// <para>
/// They are reported at all for the reason the DataContractSerializer generator settled next door: a
/// silent fallback is a build that looks clean and throws at run time, because under NativeAOT the
/// reflective path is the broken one rather than merely the slower one.
/// </para>
/// </remarks>
internal static class DiagnosticDescriptors
{
    private const string Category = nameof(ServiceModelConfigurationGenerator);

    internal static readonly DiagnosticDescriptor ContextMustBePartial = new(
        id: "COREWCF_0600",
        title: "ServiceModelConfigurationContext must be partial",
        messageFormat: "Class '{0}' carries [ServiceModelConfigurable] but is not partial, so no configuration metadata can be generated into it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ContextMustDeriveFromBase = new(
        id: "COREWCF_0601",
        title: "ServiceModelConfigurationContext must derive from ServiceModelConfigurationContext",
        messageFormat: "Class '{0}' carries [ServiceModelConfigurable] but does not derive from CoreWCF.Extensions.Configuration.ServiceModelConfigurationContext",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Two entries claiming one name.
    /// </summary>
    /// <remarks>
    /// This is the homonym problem the package exists to be deterministic about, moved to compile
    /// time. CoreWCF ships client and server halves of the queue transports as deliberate homonyms -
    /// CoreWCF.Channels.KafkaBinding against CoreWCF.ServiceModel.Channels.KafkaBinding - and every
    /// listed type is registered under its bare full name as well as its assembly qualified one. Two
    /// types sharing a name is therefore possible, and answering it by load order is what this whole
    /// design refuses to do. Erroring names both types and asks for a Name instead.
    /// </remarks>
    internal static readonly DiagnosticDescriptor DuplicateConfigurationName = new(
        id: "COREWCF_0602",
        title: "Two types claim the same configuration name",
        messageFormat: "'{0}' would resolve to both '{1}' and '{2}'; give one of them a distinct [ServiceModelConfigurable(..., Name = \"...\")]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor TypeNotAccessible = new(
        id: "COREWCF_0603",
        title: "Listed type is not accessible from the generated context",
        messageFormat: "'{0}' is not accessible from '{1}', so it will be resolved and hydrated by reflection; make it public, or declare the context in its assembly",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor NoParameterlessConstructor = new(
        id: "COREWCF_0604",
        title: "Listed type cannot be created from configuration",
        messageFormat: "'{0}' has no accessible parameterless constructor, so configuration cannot create one",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// The property graph walk stopped before it reached everything.
    /// </summary>
    /// <remarks>
    /// Reported on the listed type rather than on every type below the cut. A graph deep enough to
    /// hit the cap produces a cascade of consequences, and burying the one line anybody can act on -
    /// list the nested type yourself - under the rest is not a service.
    /// </remarks>
    internal static readonly DiagnosticDescriptor GraphTruncated = new(
        id: "COREWCF_0605",
        title: "Property graph truncated",
        messageFormat: "The property graph of '{0}' was truncated at '{1}', which will be hydrated by reflection; add [ServiceModelConfigurable(typeof({2}))] to reach it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor NoCompileTimeConversion = new(
        id: "COREWCF_0606",
        title: "No compile-time conversion from a configuration string",
        messageFormat: "'{0}' has no TypeConverter and no public static members of its own type, so a configured value for it falls back to TypeDescriptor",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// A CustomBinding with nothing to put in it.
    /// </summary>
    /// <remarks>
    /// CustomBinding.Elements holds abstract BindingElements, and which concrete ones a configuration
    /// will name is the one thing the graph walk cannot infer. Without at least one listed, a
    /// CustomBinding is generated metadata that hydrates nothing.
    /// </remarks>
    internal static readonly DiagnosticDescriptor CustomBindingWithoutElements = new(
        id: "COREWCF_0607",
        title: "CustomBinding is listed but no binding element is",
        messageFormat: "'{0}' lists CustomBinding but no concrete BindingElement, so the elements it is built from will be resolved and hydrated by reflection",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
