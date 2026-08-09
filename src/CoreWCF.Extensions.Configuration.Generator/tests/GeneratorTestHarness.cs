// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CoreWCF.Extensions.Configuration.Generator.Tests
{
    /// <summary>What one generator run produced.</summary>
    internal sealed class GeneratorResult
    {
        internal GeneratorResult(
            ImmutableArray<Diagnostic> generatorDiagnostics,
            ImmutableArray<Diagnostic> compilationDiagnostics,
            IReadOnlyList<string> generatedSources)
        {
            GeneratorDiagnostics = generatorDiagnostics;
            CompilationDiagnostics = compilationDiagnostics;
            GeneratedSources = generatedSources;
        }

        internal ImmutableArray<Diagnostic> GeneratorDiagnostics { get; }

        internal ImmutableArray<Diagnostic> CompilationDiagnostics { get; }

        internal IReadOnlyList<string> GeneratedSources { get; }

        internal string SingleSource => GeneratedSources.Single();

        internal IEnumerable<string> DiagnosticIds => GeneratorDiagnostics.Select(d => d.Id);

        /// <summary>Errors from compiling the input together with everything the generator emitted.</summary>
        internal IEnumerable<Diagnostic> Errors =>
            CompilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);

        /// <summary>
        /// A generator that threw. Roslyn reports this as CS8785, a warning, so without looking for
        /// it a crashed generator presents as one that simply produced nothing.
        /// </summary>
        internal IEnumerable<Diagnostic> Crashes =>
            GeneratorDiagnostics.Where(d => d.Id == "CS8785");

        /// <summary>
        /// The errors rendered for an assertion message. Without this a failure reports only that a
        /// collection was non-empty, which says nothing about what the generator got wrong.
        /// </summary>
        internal string ErrorReport =>
            string.Join(Environment.NewLine, Errors.Concat(Crashes).Select(e => "  " + e.Id + ": " + e.GetMessage()));
    }

    /// <summary>
    /// Runs the generator over a source snippet and reports what it produced.
    /// </summary>
    /// <remarks>
    /// Drives <see cref="CSharpGeneratorDriver"/> directly, as the DataContractSerializer generator's
    /// tests do. Compiling the input together with the emitted output is the assertion that carries the
    /// most weight here: the whole point of the generated path is that it is ordinary code, so if it
    /// does not compile against the real binding types there is nothing left to be right about.
    /// </remarks>
    internal static class GeneratorTestHarness
    {
        internal static GeneratorDriver RunForSnapshot(string source)
        {
            Run(source, enabled: true, out GeneratorDriver driver);
            return driver;
        }

        internal static GeneratorResult Run(string source, bool enabled = true) =>
            Run(source, enabled, out _);

        private static GeneratorResult Run(string source, bool enabled, out GeneratorDriver driver)
        {
            CSharpParseOptions parseOptions = new(LanguageVersion.CSharp11);

            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName: "GeneratorTests",
                syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references: MetadataReferences,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            driver = CSharpGeneratorDriver.Create(
                generators: new[] { new ServiceModelConfigurationGenerator().AsSourceGenerator() },
                parseOptions: parseOptions,
                optionsProvider: new TestOptionsProvider(enabled));

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

            GeneratorDriverRunResult runResult = driver.GetRunResult();
            List<string> sources = runResult.Results
                .SelectMany(r => r.GeneratedSources)
                .Select(s => s.SourceText.ToString())
                .ToList();

            return new GeneratorResult(generatorDiagnostics, output.GetDiagnostics(), sources);
        }

        private static ImmutableArray<MetadataReference> MetadataReferences
        {
            get
            {
                // Everything already loaded into this test process, which is the whole framework plus
                // the CoreWCF assemblies the emitted code refers to.
                List<MetadataReference> references = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                    .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
                    .ToList();

                foreach (Assembly assembly in new[]
                {
                    typeof(CoreWCF.Extensions.Configuration.ServiceModelConfigurationContext).Assembly,
                    typeof(CoreWCF.Channels.Binding).Assembly,
                    typeof(CoreWCF.NetTcpBinding).Assembly,
                    typeof(CoreWCF.BasicHttpBinding).Assembly,
                    typeof(System.ComponentModel.TypeConverter).Assembly,
                    // XmlDictionaryReaderQuotas hangs off almost every binding, and the assembly it
                    // lives in is not loaded until something asks for it. Without this the symbol
                    // arrives as an error type and the whole ReaderQuotas branch of the graph is
                    // silently absent from the generated metadata.
                    typeof(System.Xml.XmlDictionaryReaderQuotas).Assembly,
                })
                {
                    if (!references.Any(r => string.Equals(r.Display, assembly.Location, StringComparison.OrdinalIgnoreCase)))
                    {
                        references.Add(MetadataReference.CreateFromFile(assembly.Location));
                    }
                }

                return references.ToImmutableArray();
            }
        }

        /// <summary>Supplies the build property the generator is gated on.</summary>
        private sealed class TestOptionsProvider : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptionsProvider
        {
            public TestOptionsProvider(bool enabled) => GlobalOptions = new Options(enabled);

            public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GlobalOptions { get; }

            public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

            public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

            private sealed class Options : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions
            {
                // Spelled out rather than referencing the generator's constant on purpose: this is the
                // name a consumer's build actually supplies, so a rename should break these tests rather
                // than silently sail through them and break consumers instead.
                private const string EnableProperty = "build_property.EnableCoreWCFConfigurationGenerator";

                private readonly bool _enabled;

                public Options(bool enabled) => _enabled = enabled;

                public override bool TryGetValue(string key, out string value)
                {
                    if (key == EnableProperty)
                    {
                        value = _enabled ? "true" : "false";
                        return true;
                    }

                    value = null;
                    return false;
                }
            }
        }
    }
}
