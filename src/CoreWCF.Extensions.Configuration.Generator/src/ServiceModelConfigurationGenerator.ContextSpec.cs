// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CoreWCF.Extensions.Configuration.Generator;

/// <summary>
/// One user-declared context, reduced to values.
/// </summary>
/// <remarks>
/// Nothing here is an <c>ISymbol</c>. A symbol roots its whole <c>Compilation</c>, so putting one in
/// the model both defeats incremental caching and keeps compilations alive. Everything the emitter
/// needs is a string or a bool by the time parsing is done.
/// </remarks>
internal sealed record ContextSpec(
    string? Namespace,
    string ContextName,
    string ContextAccessibility,
    EquatableArray<NameSpec> Names,
    EquatableArray<ConfiguredTypeSpec> Types,
    EquatableArray<string> RootedTypes,
    bool SupportsDynamicDependency,
    EquatableArray<DiagnosticInfo> Diagnostics,
    bool IsSuppressed);

/// <summary>A name configuration may use, and the type it resolves to.</summary>
internal sealed record NameSpec(string Name, string TypeExpression);

/// <summary>
/// The metadata for one participating type: how to create it, what may be set on it, how to convert
/// a string to it, and how to append to it when it is a collection.
/// </summary>
internal sealed record ConfiguredTypeSpec(
    string TypeExpression,
    string MethodSuffix,
    bool CanCreate,
    EquatableArray<MemberSpec> Members,
    EquatableArray<VocabularySpec> Vocabulary,
    string? ConverterTypeExpression,
    string? CollectionItemTypeExpression);

/// <summary>
/// A settable or readable property, reached by a cast rather than by a PropertyInfo.
/// </summary>
/// <remarks>
/// The cast targets the type being described rather than the type declaring the property. A public
/// type may inherit public properties from an internal base, which generated code cannot name; and
/// casting to the most derived type is also what makes a property re-declared with <c>new</c> resolve
/// to the one that shadows rather than the one shadowed.
/// </remarks>
internal sealed record MemberSpec(
    string Name,
    string MemberTypeExpression,
    bool CanRead,
    bool CanWrite);

/// <summary>
/// One well-known value exposed as a public static member of its own type, as
/// <c>MessageVersion.Soap12WSAddressing10</c> is.
/// </summary>
/// <remarks>
/// These are what makes a hand written converter per type unnecessary for <c>MessageVersion</c>,
/// <c>EnvelopeVersion</c>, <c>SecurityAlgorithmSuite</c> and <c>MessageSecurityVersion</c>. The
/// generator enumerates them from the symbol, so a type added later is covered without anyone
/// writing anything.
/// </remarks>
internal sealed record VocabularySpec(string Name, string Expression);
