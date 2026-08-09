// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CoreWCF.Extensions.Configuration.Generator;

public sealed partial class ServiceModelConfigurationGenerator
{
    /// <summary>
    /// Reads a context declaration and the types it lists into a <see cref="ContextSpec"/>.
    /// </summary>
    internal static class Parser
    {
        internal const string ConfigurableAttributeName = "CoreWCF.Extensions.Configuration.ServiceModelConfigurableAttribute";

        private const string ContextBaseName = "CoreWCF.Extensions.Configuration.ServiceModelConfigurationContext";
        private const string BindingName = "CoreWCF.Channels.Binding";
        private const string BindingElementName = "CoreWCF.Channels.BindingElement";
        private const string CustomBindingName = "CoreWCF.Channels.CustomBinding";
        private const string DynamicDependencyName = "System.Diagnostics.CodeAnalysis.DynamicDependencyAttribute";
        private const string TypeConverterAttributeName = "System.ComponentModel.TypeConverterAttribute";
        private const string TypeConverterName = "System.ComponentModel.TypeConverter";

        /// <summary>
        /// How far the property graph is walked below a listed type.
        /// </summary>
        /// <remarks>
        /// A binding reaches its security, transport security and quota objects within three or four
        /// levels; eight is chosen to be comfortably past that while still bounding a graph that turns
        /// out to be unexpectedly deep. Hitting it is reported rather than silently accepted.
        /// </remarks>
        private const int MaxGraphDepth = 8;

        /// <summary>
        /// A type's name as the leaf and opaque sets spell it.
        /// </summary>
        /// <remarks>
        /// Not <c>ToDisplayString()</c>, which renders <c>System.Boolean</c> as <c>bool</c> - so a set
        /// keyed on framework names silently matches nothing and every primitive gets walked as though
        /// it were a binding.
        /// </remarks>
        private static readonly SymbolDisplayFormat s_frameworkName = new(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

        /// <summary>
        /// The types a configuration value is converted to without any generated help, because
        /// <see cref="ConfigurationValueConverter"/> converts them by hand. Walking into one would
        /// generate metadata nothing reads.
        /// </summary>
        private static readonly ImmutableHashSet<string> s_leafTypes = ImmutableHashSet.Create(
            "System.String", "System.Boolean", "System.Char", "System.SByte", "System.Byte",
            "System.Int16", "System.UInt16", "System.Int32", "System.UInt32", "System.Int64",
            "System.UInt64", "System.Single", "System.Double", "System.Decimal", "System.TimeSpan",
            "System.DateTime", "System.DateTimeOffset", "System.Guid", "System.Uri", "System.Text.Encoding");

        /// <summary>
        /// Types the walk never descends into whatever their shape, because configuration cannot
        /// meaningfully name their contents.
        /// </summary>
        private static readonly ImmutableHashSet<string> s_opaqueTypes = ImmutableHashSet.Create(
            "System.Object", "System.Type", "System.Reflection.Assembly", "System.Xml.XmlQualifiedName",
            "System.Net.IPAddress", "System.Security.Cryptography.X509Certificates.X509Certificate2",
            // Reached from EnvelopeVersion.DictionaryNamespace and its like. An interned XML string is
            // not something a configuration file sets, so walking it only adds metadata nothing reads.
            "System.Xml.XmlDictionaryString");

        internal static ContextSpec Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
        {
            var contextSymbol = (INamedTypeSymbol)context.TargetSymbol;
            var declaration = (ClassDeclarationSyntax)context.TargetNode;
            Compilation compilation = context.SemanticModel.Compilation;

            var diagnostics = new List<DiagnosticInfo>();
            bool suppressed = false;

            if (!declaration.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)))
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.ContextMustBePartial, contextSymbol, contextSymbol.Name));
                suppressed = true;
            }

            INamedTypeSymbol? contextBase = compilation.GetTypeByMetadataName(ContextBaseName);
            if (contextBase is null || !DerivesFrom(contextSymbol, contextBase))
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.ContextMustDeriveFromBase, contextSymbol, contextSymbol.Name));
                suppressed = true;
            }

            var listed = new List<(INamedTypeSymbol Type, string? Name)>();
            foreach (AttributeData attribute in context.Attributes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attribute.ConstructorArguments.Length != 1 ||
                    attribute.ConstructorArguments[0].Value is not INamedTypeSymbol listedType)
                {
                    continue;
                }

                string? name = attribute.NamedArguments
                    .FirstOrDefault(a => a.Key == "Name").Value.Value as string;

                listed.Add((listedType, string.IsNullOrEmpty(name) ? null : name));
            }

            var walker = new GraphWalker(compilation, contextSymbol, diagnostics);
            var names = new Dictionary<string, (string Expression, string TypeDisplay)>(System.StringComparer.OrdinalIgnoreCase);
            var rooted = new List<string>();

            INamedTypeSymbol? binding = compilation.GetTypeByMetadataName(BindingName);
            INamedTypeSymbol? bindingElement = compilation.GetTypeByMetadataName(BindingElementName);
            bool listsCustomBinding = false;
            bool listsBindingElement = false;

            foreach ((INamedTypeSymbol type, string? name) in listed)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsAccessible(compilation, contextSymbol, type))
                {
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.TypeNotAccessible, type, type.ToDisplayString(), contextSymbol.Name));
                    continue;
                }

                AddName(names, diagnostics, type, ToExpression(type), type.ToDisplayString(), contextSymbol);
                AddName(names, diagnostics, type, ToExpression(type), AssemblyQualifiedName(type), contextSymbol);
                if (name is not null)
                {
                    AddName(names, diagnostics, type, ToExpression(type), name, contextSymbol);
                }

                if (type.ToDisplayString() == CustomBindingName)
                {
                    listsCustomBinding = true;
                }

                if (bindingElement is not null && DerivesFrom(type, bindingElement))
                {
                    listsBindingElement = true;
                }

                // Only the two things this configuration model hydrates get a property graph walked.
                // A service implementation or a contract interface is never hydrated - it is a Type
                // handed to ServiceModelOptions.ConfigureService - so all it needs is to be rooted, and
                // walking a service class's properties would generate metadata nobody reads.
                bool isHydrated =
                    (binding is not null && DerivesFrom(type, binding)) ||
                    (bindingElement is not null && DerivesFrom(type, bindingElement));

                if (isHydrated)
                {
                    walker.Walk(type, type, depth: 0, cancellationToken);
                }
                else
                {
                    rooted.Add(ToExpression(type));
                }
            }

            if (listsCustomBinding && !listsBindingElement)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.CustomBindingWithoutElements, contextSymbol, contextSymbol.Name));
            }

            bool supportsDynamicDependency = compilation.GetTypeByMetadataName(DynamicDependencyName) is not null;

            return new ContextSpec(
                Namespace: contextSymbol.ContainingNamespace.IsGlobalNamespace ? null : contextSymbol.ContainingNamespace.ToDisplayString(),
                ContextName: contextSymbol.Name,
                ContextAccessibility: contextSymbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
                Names: new EquatableArray<NameSpec>(names
                    .OrderBy(pair => pair.Key, System.StringComparer.Ordinal)
                    .Select(pair => new NameSpec(pair.Key, pair.Value.Expression))
                    .ToArray()),
                Types: new EquatableArray<ConfiguredTypeSpec>(walker.Types),
                RootedTypes: new EquatableArray<string>(rooted.Distinct().OrderBy(t => t, System.StringComparer.Ordinal).ToArray()),
                SupportsDynamicDependency: supportsDynamicDependency,
                Diagnostics: new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()),
                IsSuppressed: suppressed);
        }

        private static void AddName(
            Dictionary<string, (string Expression, string TypeDisplay)> names,
            List<DiagnosticInfo> diagnostics,
            INamedTypeSymbol type,
            string expression,
            string name,
            INamedTypeSymbol contextSymbol)
        {
            if (names.TryGetValue(name, out (string Expression, string TypeDisplay) existing))
            {
                if (existing.Expression != expression)
                {
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.DuplicateConfigurationName,
                        type,
                        name,
                        existing.TypeDisplay,
                        type.ToDisplayString()));
                }

                return;
            }

            names[name] = (expression, type.ToDisplayString());
        }

        /// <summary>
        /// Walks a listed type's settable property graph, collecting metadata for everything reachable.
        /// </summary>
        /// <remarks>
        /// Listing <c>NetTcpBinding</c> should not also mean listing <c>NetTcpSecurity</c>,
        /// <c>TcpTransportSecurity</c>, <c>MessageSecurityOverTcp</c> and
        /// <c>XmlDictionaryReaderQuotas</c>. The user lists what a configuration file names; the walk
        /// finds what hydrating it touches.
        /// </remarks>
        private sealed class GraphWalker
        {
            private readonly Compilation _compilation;
            private readonly INamedTypeSymbol _contextSymbol;
            private readonly List<DiagnosticInfo> _diagnostics;
            private readonly Dictionary<string, ConfiguredTypeSpec> _types = new(System.StringComparer.Ordinal);
            private readonly HashSet<string> _visiting = new(System.StringComparer.Ordinal);
            private readonly INamedTypeSymbol? _typeConverterAttribute;
            private readonly INamedTypeSymbol? _typeConverter;

            public GraphWalker(Compilation compilation, INamedTypeSymbol contextSymbol, List<DiagnosticInfo> diagnostics)
            {
                _compilation = compilation;
                _contextSymbol = contextSymbol;
                _diagnostics = diagnostics;
                _typeConverterAttribute = compilation.GetTypeByMetadataName(TypeConverterAttributeName);
                _typeConverter = compilation.GetTypeByMetadataName(TypeConverterName);
            }

            public ConfiguredTypeSpec[] Types => _types.Values
                .OrderBy(t => t.TypeExpression, System.StringComparer.Ordinal)
                .ToArray();

            public void Walk(INamedTypeSymbol root, INamedTypeSymbol type, int depth, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string key = FrameworkName(type);
                if (_types.ContainsKey(key) || _visiting.Contains(key))
                {
                    return;
                }

                if (depth > MaxGraphDepth)
                {
                    _diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.GraphTruncated, root, root.ToDisplayString(), key, type.Name));
                    return;
                }

                _visiting.Add(key);

                var members = new List<MemberSpec>();
                var nested = new List<INamedTypeSymbol>();

                foreach (IPropertySymbol property in EnumerateProperties(type))
                {
                    bool canRead = property.GetMethod is { DeclaredAccessibility: Accessibility.Public };
                    bool canWrite = property.SetMethod is { DeclaredAccessibility: Accessibility.Public };

                    if (!canRead && !canWrite)
                    {
                        continue;
                    }

                    // A member whose type the generated code cannot name, or cannot box, is one this
                    // context has no metadata for. Leaving it out is what sends it to reflection; naming
                    // it anyway would emit a cast that does not compile.
                    if (property.Type.IsRefLikeType || !IsAccessible(_compilation, _contextSymbol, property.Type))
                    {
                        continue;
                    }

                    members.Add(new MemberSpec(
                        property.Name,
                        ToExpression(property.Type),
                        canRead,
                        canWrite));

                    if (property.Type is INamedTypeSymbol memberType && ShouldDescend(memberType))
                    {
                        nested.Add(memberType);
                    }
                }

                _types[key] = new ConfiguredTypeSpec(
                    TypeExpression: ToExpression(type),
                    MethodSuffix: MethodSuffix(key),
                    CanCreate: HasAccessibleParameterlessConstructor(type),
                    Members: new EquatableArray<MemberSpec>(members.ToArray()),
                    Vocabulary: new EquatableArray<VocabularySpec>(Vocabulary(type)),
                    ConverterTypeExpression: ConverterExpression(type),
                    CollectionItemTypeExpression: CollectionItemExpression(type));

                _visiting.Remove(key);

                foreach (INamedTypeSymbol memberType in nested)
                {
                    Walk(root, memberType, depth + 1, cancellationToken);
                }

                // The element type of a collection is walked as well: CustomBinding.Elements is a
                // BindingElementCollection whose items the configuration names one by one, and each of
                // those concrete element types is listed separately. Descending here covers the abstract
                // base's own properties.
                if (CollectionItem(type) is INamedTypeSymbol itemType && ShouldDescend(itemType))
                {
                    Walk(root, itemType, depth + 1, cancellationToken);
                }
            }

            /// <summary>
            /// Public instance properties, most derived first, so a property re-declared with
            /// <c>new</c> shadows the one it hides rather than colliding with it.
            /// </summary>
            private static IEnumerable<IPropertySymbol> EnumerateProperties(INamedTypeSymbol type)
            {
                var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

                for (INamedTypeSymbol? current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
                {
                    foreach (IPropertySymbol property in current.GetMembers().OfType<IPropertySymbol>())
                    {
                        if (property.IsStatic || property.IsIndexer || property.DeclaredAccessibility != Accessibility.Public)
                        {
                            continue;
                        }

                        if (seen.Add(property.Name))
                        {
                            yield return property;
                        }
                    }
                }
            }

            private bool ShouldDescend(INamedTypeSymbol type)
            {
                if (type.TypeKind is TypeKind.Enum or TypeKind.Delegate or TypeKind.Interface or TypeKind.TypeParameter)
                {
                    return false;
                }

                if (type.IsGenericType && type.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
                {
                    return false;
                }

                string name = FrameworkName(type);
                if (s_leafTypes.Contains(name) || s_opaqueTypes.Contains(name))
                {
                    return false;
                }

                // Metadata for a type the generated code cannot name is metadata that will not compile.
                // Falling back for it is correct; saying so is what COREWCF_0603 is for, and it is
                // reported for a listed type rather than for every nested one, which would be noise.
                return IsAccessible(_compilation, _contextSymbol, type);
            }

            private bool HasAccessibleParameterlessConstructor(INamedTypeSymbol type) =>
                !type.IsAbstract &&
                type.InstanceConstructors.Any(c =>
                    c.Parameters.Length == 0 &&
                    c.ContainingAssembly is not null &&
                    _compilation.IsSymbolAccessibleWithin(c, _contextSymbol));

            /// <summary>
            /// The public static members of <paramref name="type"/> whose own type is
            /// <paramref name="type"/>, which is how the types with no TypeConverter spell their
            /// well-known values.
            /// </summary>
            private VocabularySpec[] Vocabulary(INamedTypeSymbol type)
            {
                var values = new List<VocabularySpec>();

                for (INamedTypeSymbol? current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
                {
                    foreach (ISymbol member in current.GetMembers())
                    {
                        if (!member.IsStatic || member.DeclaredAccessibility != Accessibility.Public)
                        {
                            continue;
                        }

                        ITypeSymbol? memberType = member switch
                        {
                            IPropertySymbol { GetMethod.DeclaredAccessibility: Accessibility.Public } property => property.Type,
                            IFieldSymbol field => field.Type,
                            _ => null,
                        };

                        if (memberType is null || !IsAssignableTo(memberType, type))
                        {
                            continue;
                        }

                        if (!values.Any(v => v.Name == member.Name))
                        {
                            values.Add(new VocabularySpec(member.Name, $"{ToExpression(current)}.{member.Name}"));
                        }
                    }
                }

                return values.OrderBy(v => v.Name, System.StringComparer.Ordinal).ToArray();
            }

            /// <summary>
            /// The type's own <c>[TypeConverter]</c>, when it names a converter the generated code can
            /// construct - which is what replaces <c>TypeDescriptor.GetConverter</c>.
            /// </summary>
            private string? ConverterExpression(INamedTypeSymbol type)
            {
                if (_typeConverterAttribute is null || _typeConverter is null)
                {
                    return null;
                }

                foreach (AttributeData attribute in type.GetAttributes())
                {
                    if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, _typeConverterAttribute) ||
                        attribute.ConstructorArguments.Length != 1 ||
                        attribute.ConstructorArguments[0].Value is not INamedTypeSymbol converter)
                    {
                        continue;
                    }

                    if (DerivesFrom(converter, _typeConverter) &&
                        HasAccessibleParameterlessConstructor(converter))
                    {
                        return ToExpression(converter);
                    }
                }

                return null;
            }

            private string? CollectionItemExpression(INamedTypeSymbol type) =>
                CollectionItem(type) is { } item ? ToExpression(item) : null;

            /// <summary>
            /// The element type when <paramref name="type"/> is a collection configuration can append
            /// to. Arrays are excluded deliberately: they satisfy <c>ICollection&lt;T&gt;</c> and then
            /// throw from <c>Add</c>, so treating one as a collection only moves the failure.
            /// </summary>
            private static INamedTypeSymbol? CollectionItem(INamedTypeSymbol type)
            {
                if (type.SpecialType == SpecialType.System_String || type.TypeKind == TypeKind.Array)
                {
                    return null;
                }

                foreach (INamedTypeSymbol contract in type.AllInterfaces)
                {
                    if (contract.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_ICollection_T)
                    {
                        return contract.TypeArguments[0] as INamedTypeSymbol;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Whether generated code can name <paramref name="type"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="Compilation.IsSymbolAccessibleWithin"/> throws rather than answering for a symbol
        /// with no containing assembly - an array, a pointer, an error type - so the shape has to be
        /// established before the question is asked. An array is answered by its element type, because
        /// naming <c>T[]</c> is naming <c>T</c>; a constructed generic by its arguments, for the same
        /// reason.
        /// </remarks>
        private static bool IsAccessible(Compilation compilation, INamedTypeSymbol within, ITypeSymbol? type) => type switch
        {
            null => false,
            IArrayTypeSymbol array => IsAccessible(compilation, within, array.ElementType),
            IPointerTypeSymbol => false,
            IDynamicTypeSymbol => true,
            INamedTypeSymbol named =>
                named.TypeKind != TypeKind.Error
                && named.ContainingAssembly is not null
                && compilation.IsSymbolAccessibleWithin(named, within)
                && named.TypeArguments.All(argument => IsAccessible(compilation, within, argument)),
            _ => false,
        };

        private static bool IsAssignableTo(ITypeSymbol from, INamedTypeSymbol to)
        {
            for (ITypeSymbol? current = from; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, to))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
        {
            for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                {
                    return true;
                }
            }

            return false;
        }

        internal static string ToExpression(ITypeSymbol type) =>
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        private static string FrameworkName(ITypeSymbol type) => type.ToDisplayString(s_frameworkName);

        /// <summary>
        /// The assembly qualified name a configuration file would use for a type, which is the spelling
        /// that works with no context at all and therefore has to keep working with one.
        /// </summary>
        private static string AssemblyQualifiedName(INamedTypeSymbol type) =>
            $"{type.ToDisplayString()}, {type.ContainingAssembly.Name}";

        /// <summary>
        /// A type's display name reduced to something usable in a method name.
        /// </summary>
        private static string MethodSuffix(string display)
        {
            var builder = new System.Text.StringBuilder(display.Length);
            foreach (char c in display)
            {
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            return builder.ToString();
        }
    }
}
