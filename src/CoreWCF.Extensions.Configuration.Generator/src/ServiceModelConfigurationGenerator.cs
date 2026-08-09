// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CoreWCF.Extensions.Configuration.Generator;

/// <summary>
/// Generates the type map and hydration metadata that let a service model configuration section be
/// read without reflection.
/// </summary>
/// <remarks>
/// <para>
/// Declaring bindings and endpoints in <c>IConfiguration</c> means naming types in strings, and a
/// type named only in a string is a type nothing references: the trimmer removes it, and
/// <c>Type.GetType</c> then finds nothing. Hydrating one means <c>Activator.CreateInstance</c> and
/// <c>PropertyInfo.SetValue</c> over members the trimmer removed for the same reason, and reaching
/// a collection's <c>Add</c> through <c>MakeGenericType</c>, which NativeAOT cannot do at all.
/// </para>
/// <para>
/// This generator answers all of that the only way it can be answered: by turning the strings into
/// <c>typeof</c> and the reflection into ordinary compiled code, for the types a user lists.
/// </para>
/// <para>
/// The gate is a build property rather than the presence of the attribute, so that a multi-targeted
/// project compiles everywhere: the base class's members return null, and where nothing was
/// generated the runtime falls back to reflection with no conditional compilation in user code.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed partial class ServiceModelConfigurationGenerator : IIncrementalGenerator
{
    internal const string EnableProperty = "build_property.EnableCoreWCFConfigurationGenerator";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<bool> enabled = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
                options.GlobalOptions.TryGetValue(EnableProperty, out string? value)
                && string.Equals(value, "true", System.StringComparison.OrdinalIgnoreCase));

        // Parsing happens inside the transform, so what leaves it is a plain value Roslyn can compare
        // and cache. CoreWCF.BuildTools' generators instead carry syntax nodes and symbols into
        // RegisterSourceOutput, which means they re-run on every keystroke.
        IncrementalValuesProvider<ContextSpec> contexts = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Parser.ConfigurableAttributeName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (syntaxContext, cancellationToken) => Parser.Parse(syntaxContext, cancellationToken));

        context.RegisterSourceOutput(
            enabled.Combine(contexts.Collect()),
            static (productionContext, source) => Execute(source.Left, source.Right, productionContext));
    }

    private static void Execute(bool enabled, ImmutableArray<ContextSpec> contexts, SourceProductionContext context)
    {
        if (!enabled || contexts.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (ContextSpec spec in contexts)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            foreach (DiagnosticInfo diagnostic in spec.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
            }

            if (spec.IsSuppressed)
            {
                continue;
            }

            context.AddSource(Emitter.HintName(spec), Emitter.Emit(spec));
        }
    }
}
