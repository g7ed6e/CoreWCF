// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CoreWCF.DataContractSerialization.Generator;

public sealed partial class DataContractSerializerGenerator
{
    /// <summary>
    /// Turns a serializer context declaration into a <see cref="ContextSpec"/>.
    /// </summary>
    /// <remarks>
    /// The only place that touches symbols. Everything it produces is plain data, so the emitter -
    /// and Roslyn's incremental cache - never see a <c>Compilation</c>.
    /// </remarks>
    internal static class Parser
    {
        internal const string ContextBaseTypeName = "CoreWCF.DataContractSerialization.DataContractSerializerContext";
        internal const string SerializableAttributeName = "CoreWCF.DataContractSerialization.DataContractSerializableAttribute";
        internal const string DataContractAttributeName = "System.Runtime.Serialization.DataContractAttribute";
        internal const string DataMemberAttributeName = "System.Runtime.Serialization.DataMemberAttribute";

        /// <summary>The namespace a contract gets when it does not name one itself.</summary>
        private const string DefaultNamespacePrefix = "http://schemas.datacontract.org/2004/07/";

        internal static ContextSpec Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
        {
            if (context.TargetSymbol is not INamedTypeSymbol contextType)
            {
                return ContextSpec.Failed(EquatableArray<DiagnosticInfo>.Empty);
            }

            List<DiagnosticInfo> diagnostics = new();

            if (!IsPartial(context.TargetNode))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.ContextMustBePartial, contextType, contextType.Name));
            }

            if (!DerivesFromContextBase(contextType))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.ContextMustDeriveFromBase, contextType, contextType.Name));
            }

            if (diagnostics.Count > 0)
            {
                return ContextSpec.Failed(new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
            }

            // Contracts reachable through members are pulled in automatically, so a user lists the
            // types they serialize rather than the closure of everything those types touch. Only
            // the explicitly declared ones become GetSerializer entries; the rest exist so their
            // content can be written inline by whatever refers to them.
            Dictionary<string, ContractSpec> contracts = new(StringComparer.Ordinal);
            Queue<(INamedTypeSymbol Type, bool IsRoot)> pending = new();
            HashSet<string> queued = new(StringComparer.Ordinal);

            foreach (AttributeData attribute in context.Attributes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attribute.ConstructorArguments.Length != 1
                    || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol contractType)
                {
                    continue;
                }

                if (GetAttribute(contractType, DataContractAttributeName) is null)
                {
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.TypeIsNotADataContract, contractType, contractType.ToDisplayString()));
                    continue;
                }

                Enqueue(contractType, isRoot: true);
            }

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                (INamedTypeSymbol type, bool isRoot) = pending.Dequeue();
                ContractSpec spec = ParseContract(type, contextType.ContainingAssembly, isRoot, out List<INamedTypeSymbol> referenced, cancellationToken);
                contracts[spec.FullyQualifiedName] = spec;

                // Symbols are followed here and never stored: putting one in the spec would root a
                // Compilation and defeat the incremental caching this design exists to preserve.
                foreach (INamedTypeSymbol reference in referenced)
                {
                    Enqueue(reference, isRoot: false);
                }
            }

            PropagateUnsupported(contracts);

            string? containingNamespace = contextType.ContainingNamespace.IsGlobalNamespace
                ? null
                : contextType.ContainingNamespace.ToDisplayString();

            // Sorted so the emitted file is byte-stable across builds: dictionary order is an
            // implementation detail, and generated source that shuffles is generated source that
            // shows up as a spurious diff.
            ContractSpec[] ordered = contracts.Values
                .OrderBy(c => c.FullyQualifiedName, StringComparer.Ordinal)
                .ToArray();

            return new ContextSpec(
                containingNamespace,
                contextType.Name,
                HintNameFor(contextType),
                new EquatableArray<ContractSpec>(ordered),
                new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));

            void Enqueue(INamedTypeSymbol type, bool isRoot)
            {
                string key = type.ToDisplayString(FullyQualifiedFormat);
                if (isRoot)
                {
                    // A type may be reached as a member before it is declared; declaring it wins,
                    // because that is what makes it available from GetSerializer.
                    if (contracts.TryGetValue(key, out ContractSpec existing) && !existing.IsRoot)
                    {
                        contracts[key] = existing with { IsRoot = true };
                        return;
                    }
                }

                if (queued.Add(key))
                {
                    pending.Enqueue((type, isRoot));
                }
                else if (isRoot && contracts.TryGetValue(key, out ContractSpec found) && !found.IsRoot)
                {
                    contracts[key] = found with { IsRoot = true };
                }
            }
        }

        /// <summary>
        /// A contract whose nested contract or base contract cannot be written cannot be written
        /// either. Repeats until nothing changes, since the dependency can be several deep.
        /// </summary>
        private static void PropagateUnsupported(Dictionary<string, ContractSpec> contracts)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;

                foreach (string key in contracts.Keys.ToArray())
                {
                    ContractSpec spec = contracts[key];
                    if (!spec.IsSupported)
                    {
                        continue;
                    }

                    string? blocked = FirstUnsupportedDependency(spec, contracts);
                    if (blocked is not null)
                    {
                        contracts[key] = spec.WithUnsupportedReason(blocked);
                        changed = true;
                    }
                }
            }
        }

        private static string? FirstUnsupportedDependency(ContractSpec spec, Dictionary<string, ContractSpec> contracts)
        {
            if (spec.BaseContractFullyQualifiedName is string baseName
                && contracts.TryGetValue(baseName, out ContractSpec baseSpec)
                && !baseSpec.IsSupported)
            {
                return "base contract " + baseName + " is not supported (" + baseSpec.UnsupportedReason + ")";
            }

            foreach (MemberSpec member in spec.Members)
            {
                if (member.NestedContractFullyQualifiedName is string nested
                    && contracts.TryGetValue(nested, out ContractSpec nestedSpec)
                    && !nestedSpec.IsSupported)
                {
                    return "member '" + member.MemberName + "' has unsupported contract type " + nested +
                           " (" + nestedSpec.UnsupportedReason + ")";
                }
            }

            return null;
        }

        private static ContractSpec ParseContract(INamedTypeSymbol contractType, IAssemblySymbol contextAssembly, bool isRoot, out List<INamedTypeSymbol> referenced, CancellationToken cancellationToken)
        {
            referenced = new List<INamedTypeSymbol>();
            AttributeData dataContract = GetAttribute(contractType, DataContractAttributeName)!;

            string contractName = GetNamedArgument(dataContract, "Name") ?? contractType.Name;
            string contractNamespace = GetNamedArgument(dataContract, "Namespace") ?? DefaultNamespaceFor(contractType);

            string? unsupportedReason = null;

            // IsReference makes the serializer emit z:Id and z:Ref to preserve object identity.
            // It is inherited, so a derived contract that says nothing still gets it from its base -
            // reading only this type's attribute would miss that and emit plausible, wrong output.
            if (InheritsIsReference(contractType))
            {
                unsupportedReason = "IsReference is not supported yet";
            }

            // A contract from another assembly is only safe if every one of its data members is
            // visible from here. Non-public members of a metadata type may not be surfaced at all,
            // in which case they would be silently dropped - producing wrong XML rather than
            // falling back. Since their absence is indistinguishable from their not existing, the
            // only sound answer is to decline the whole contract.
            if (unsupportedReason is null
                && !SymbolEqualityComparer.Default.Equals(contractType.ContainingAssembly, contextAssembly))
            {
                unsupportedReason = "contract is declared in another assembly (" +
                                    contractType.ContainingAssembly.Name +
                                    "), where non-public data members may not be visible to the generator";
            }

            INamedTypeSymbol? baseContract = contractType.BaseType is INamedTypeSymbol candidate
                && GetAttribute(candidate, DataContractAttributeName) is not null
                    ? candidate
                    : null;

            if (baseContract is not null)
            {
                referenced.Add(baseContract);
            }

            // A base class that is not itself a data contract contributes no members but is also
            // not something this generator can reason about, so decline.
            if (unsupportedReason is null
                && baseContract is null
                && contractType.BaseType is { SpecialType: not SpecialType.System_Object } other
                && other.SpecialType != SpecialType.System_ValueType)
            {
                unsupportedReason = "base type " + other.Name + " is not a data contract";
            }

            List<MemberSpec> members = new();
            foreach (ISymbol member in contractType.GetMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (member.IsStatic || GetAttribute(member, DataMemberAttributeName) is not AttributeData dataMember)
                {
                    continue;
                }

                // A property that overrides a base [DataMember] does not add a second element: the
                // base contract already contributes it, and the override only changes which getter
                // runs. Emitting it again would duplicate the member on the wire.
                if (OverridesADataMember(member))
                {
                    continue;
                }

                ITypeSymbol? memberType = member switch
                {
                    IPropertySymbol property => property.Type,
                    IFieldSymbol field => field.Type,
                    _ => null
                };

                if (memberType is null)
                {
                    continue;
                }

                // Generated code lives in the context's assembly, so it can only reach members the
                // context can see. Anything less visible keeps its contract on the reflection path.
                if (member.DeclaredAccessibility != Accessibility.Public)
                {
                    unsupportedReason ??= "member '" + member.Name + "' is not public";
                }

                MemberKind kind = ClassifyMember(memberType, out bool isNullableValueType, out INamedTypeSymbol? nestedContract);
                if (kind == MemberKind.Unsupported)
                {
                    unsupportedReason ??= "member '" + member.Name + "' has unsupported type '" +
                                          memberType.ToDisplayString() + "'";
                }

                if (nestedContract is not null)
                {
                    referenced.Add(nestedContract);
                }

                members.Add(new MemberSpec(
                    Name: GetNamedArgument(dataMember, "Name") ?? member.Name,
                    Order: GetNamedArgumentInt32(dataMember, "Order") ?? -1,
                    EmitDefaultValue: GetNamedArgumentBoolean(dataMember, "EmitDefaultValue") ?? true,
                    IsRequired: GetNamedArgumentBoolean(dataMember, "IsRequired") ?? false,
                    MemberName: member.Name,
                    Kind: kind,
                    IsNullableValueType: isNullableValueType,
                    NestedContractFullyQualifiedName: nestedContract?.ToDisplayString(FullyQualifiedFormat),
                    ChildNamespaceToDeclare: ChildNamespaceToDeclare(memberType, contractNamespace)));
            }

            // Order ascending, then ordinal by contract name. Unspecified Order is -1 and cannot be
            // written explicitly, so unordered members always precede ordered ones - including
            // Order = 0. Mirrors ClassDataContract.DataMemberComparer in dotnet/runtime.
            members.Sort(static (x, y) =>
            {
                int byOrder = x.Order.CompareTo(y.Order);
                return byOrder != 0 ? byOrder : string.CompareOrdinal(x.Name, y.Name);
            });

            return new ContractSpec(
                contractType.ToDisplayString(FullyQualifiedFormat),
                contractName,
                contractNamespace,
                new EquatableArray<MemberSpec>(members.ToArray()),
                unsupportedReason,
                baseContract?.ToDisplayString(FullyQualifiedFormat),
                isRoot);
        }

        /// <summary>
        /// Whether this member overrides one that is already a data member of a base contract.
        /// </summary>
        private static bool OverridesADataMember(ISymbol member)
        {
            for (IPropertySymbol? property = (member as IPropertySymbol)?.OverriddenProperty;
                 property is not null;
                 property = property.OverriddenProperty)
            {
                if (GetAttribute(property, DataMemberAttributeName) is not null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a type or anything in its hierarchy declares <c>[KnownType]</c>, meaning a member
        /// of that type may hold a derived instance.
        /// </summary>
        /// <remarks>
        /// Without a known type there is nothing to be polymorphic with: DataContractSerializer
        /// itself throws on an unexpected runtime type rather than guessing, so declared and runtime
        /// type must agree. With one, the real serializer emits an i:type attribute and the derived
        /// contract's members, which this slice does not do - and writing the declared type's
        /// members instead would produce wrong XML rather than falling back.
        /// </remarks>
        private static bool DeclaresKnownTypes(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            {
                foreach (AttributeData attribute in current.GetAttributes())
                {
                    string? name = attribute.AttributeClass?.ToDisplayString();
                    if (name is "System.Runtime.Serialization.KnownTypeAttribute"
                        or "CoreWCF.ServiceKnownTypeAttribute"
                        or "System.ServiceModel.ServiceKnownTypeAttribute")
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Whether this contract or any of its bases sets <c>IsReference</c>.</summary>
        private static bool InheritsIsReference(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            {
                if (GetAttribute(current, DataContractAttributeName) is AttributeData contract
                    && GetNamedArgumentBoolean(contract, "IsReference") == true)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The namespace to declare on a member element, or null for none.
        /// </summary>
        /// <remarks>
        /// Mirrors ClassDataContract.GetChildNamespaceToDeclare: built-in contracts, enums and
        /// IXmlSerializable declare nothing, and otherwise the child's namespace is declared only
        /// when it differs from the containing contract's. This is what puts xmlns:b on the member
        /// element rather than the root.
        /// </remarks>
        private static string? ChildNamespaceToDeclare(ITypeSymbol memberType, string containingNamespace)
        {
            ITypeSymbol type = UnwrapNullable(memberType);

            if (type.TypeKind == TypeKind.Enum || GetAttribute(type, DataContractAttributeName) is null)
            {
                return null;
            }

            string ns = GetAttribute(type, DataContractAttributeName) is AttributeData contract
                ? GetNamedArgument(contract, "Namespace") ?? DefaultNamespaceFor((INamedTypeSymbol)type)
                : string.Empty;

            return ns.Length > 0 && ns != containingNamespace ? ns : null;
        }

        private static ITypeSymbol UnwrapNullable(ITypeSymbol type) =>
            type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            && nullable.TypeArguments.Length == 1
                ? nullable.TypeArguments[0]
                : type;

        /// <summary>
        /// The contract namespace a type gets when its [DataContract] does not name one:
        /// the CLR namespace appended to a fixed prefix. An empty CLR namespace yields the prefix
        /// with no trailing segment.
        /// </summary>
        private static string DefaultNamespaceFor(INamedTypeSymbol type) =>
            type.ContainingNamespace.IsGlobalNamespace
                ? DefaultNamespacePrefix
                : DefaultNamespacePrefix + type.ContainingNamespace.ToDisplayString();

        /// <summary>
        /// Maps a member's CLR type onto how its value is written, or
        /// <see cref="MemberKind.Unsupported"/> when this generator cannot write it yet.
        /// </summary>
        private static MemberKind ClassifyMember(ITypeSymbol type, out bool isNullableValueType, out INamedTypeSymbol? nestedContract)
        {
            isNullableValueType = false;
            nestedContract = null;

            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                && nullable.TypeArguments.Length == 1)
            {
                isNullableValueType = true;
                type = nullable.TypeArguments[0];
            }

            if (type is IArrayTypeSymbol { Rank: 1 } array
                && array.ElementType.SpecialType == SpecialType.System_Byte)
            {
                return isNullableValueType ? MemberKind.Unsupported : MemberKind.ByteArray;
            }

            MemberKind kind = type.SpecialType switch
            {
                SpecialType.System_Boolean => MemberKind.Boolean,
                SpecialType.System_Byte => MemberKind.Byte,
                SpecialType.System_SByte => MemberKind.SByte,
                SpecialType.System_Int16 => MemberKind.Int16,
                SpecialType.System_UInt16 => MemberKind.UInt16,
                SpecialType.System_Int32 => MemberKind.Int32,
                SpecialType.System_UInt32 => MemberKind.UInt32,
                SpecialType.System_Int64 => MemberKind.Int64,
                SpecialType.System_UInt64 => MemberKind.UInt64,
                SpecialType.System_Single => MemberKind.Single,
                SpecialType.System_Double => MemberKind.Double,
                SpecialType.System_Decimal => MemberKind.Decimal,
                SpecialType.System_Char => MemberKind.Char,
                SpecialType.System_String => MemberKind.String,
                SpecialType.System_DateTime => MemberKind.DateTime,
                _ => MemberKind.Unsupported
            };

            if (kind != MemberKind.Unsupported)
            {
                if (isNullableValueType && !type.IsValueType)
                {
                    isNullableValueType = false;
                }

                return kind;
            }

            switch (type.ToDisplayString())
            {
                case "System.Guid":
                    return MemberKind.Guid;
                case "System.TimeSpan":
                    return MemberKind.TimeSpan;
            }

            // A member that is itself a data contract is written inline by that contract's content
            // writer. Structs are excluded for now: a nullable struct contract would need the
            // nullable unwrapping and the nil handling to compose, which is untested.
            if (type is INamedTypeSymbol named
                && named.TypeKind == TypeKind.Class
                && GetAttribute(named, DataContractAttributeName) is not null)
            {
                // A member whose declared type admits derived instances needs an i:type attribute
                // and the derived contract's members, which this slice does not emit.
                if (DeclaresKnownTypes(named))
                {
                    return MemberKind.Unsupported;
                }

                nestedContract = named;
                return MemberKind.Contract;
            }

            return MemberKind.Unsupported;
        }

        private static bool IsPartial(SyntaxNode node) =>
            node is TypeDeclarationSyntax declaration
            && declaration.Modifiers.Any(SyntaxKind.PartialKeyword);

        private static bool DerivesFromContextBase(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
            {
                if (current.ToDisplayString() == ContextBaseTypeName)
                {
                    return true;
                }
            }

            return false;
        }

        private static AttributeData? GetAttribute(ISymbol symbol, string attributeMetadataName) =>
            symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == attributeMetadataName);

        private static string? GetNamedArgument(AttributeData attribute, string name) =>
            attribute.NamedArguments.FirstOrDefault(a => a.Key == name).Value.Value as string;

        private static bool? GetNamedArgumentBoolean(AttributeData attribute, string name) =>
            attribute.NamedArguments.FirstOrDefault(a => a.Key == name).Value.Value as bool?;

        private static int? GetNamedArgumentInt32(AttributeData attribute, string name) =>
            attribute.NamedArguments.FirstOrDefault(a => a.Key == name).Value.Value as int?;

        /// <summary>
        /// A file name unique per context. Nested and generic types would otherwise collide, so the
        /// characters that are illegal or ambiguous in a hint name are folded to underscores.
        /// </summary>
        private static string HintNameFor(INamedTypeSymbol contextType)
        {
            string full = contextType.ToDisplayString();
            char[] buffer = full.ToCharArray();
            for (int i = 0; i < buffer.Length; i++)
            {
                char c = buffer[i];
                if (c is '<' or '>' or ',' or ' ' or '`' or '+' or ':' or '/' or '\\')
                {
                    buffer[i] = '_';
                }
            }

            return new string(buffer) + ".DataContractSerializers.g.cs";
        }

        internal static readonly SymbolDisplayFormat FullyQualifiedFormat =
            SymbolDisplayFormat.FullyQualifiedFormat.RemoveMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
    }
}
