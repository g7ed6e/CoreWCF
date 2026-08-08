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

            List<ContractSpec> contracts = new();
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

                contracts.Add(ParseContract(contractType, cancellationToken));
            }

            string? containingNamespace = contextType.ContainingNamespace.IsGlobalNamespace
                ? null
                : contextType.ContainingNamespace.ToDisplayString();

            return new ContextSpec(
                containingNamespace,
                contextType.Name,
                HintNameFor(contextType),
                new EquatableArray<ContractSpec>(contracts.ToArray()),
                new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
        }

        private static ContractSpec ParseContract(INamedTypeSymbol contractType, CancellationToken cancellationToken)
        {
            AttributeData dataContract = GetAttribute(contractType, DataContractAttributeName)!;

            string contractName = GetNamedArgument(dataContract, "Name") ?? contractType.Name;
            string contractNamespace = GetNamedArgument(dataContract, "Namespace") ?? DefaultNamespaceFor(contractType);

            string? unsupportedReason = null;

            // The first slice writes flat contracts only. A contract with a base class, or with a
            // member this generator cannot yet write, is left to the reflection-based serializer.
            if (contractType.BaseType is { SpecialType: not SpecialType.System_Object } baseType
                && baseType.SpecialType != SpecialType.System_ValueType)
            {
                unsupportedReason = "inheritance is not supported yet (base type " + baseType.Name + ")";
            }

            List<MemberSpec> members = new();
            foreach (ISymbol member in contractType.GetMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (member.IsStatic || GetAttribute(member, DataMemberAttributeName) is not AttributeData dataMember)
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

                MemberKind kind = ClassifyMember(memberType, out bool isNullableValueType);
                if (kind == MemberKind.Unsupported)
                {
                    unsupportedReason ??= "member '" + member.Name + "' has unsupported type '" +
                                          memberType.ToDisplayString() + "'";
                }

                members.Add(new MemberSpec(
                    Name: GetNamedArgument(dataMember, "Name") ?? member.Name,
                    Order: GetNamedArgumentInt32(dataMember, "Order") ?? -1,
                    EmitDefaultValue: GetNamedArgumentBoolean(dataMember, "EmitDefaultValue") ?? true,
                    IsRequired: GetNamedArgumentBoolean(dataMember, "IsRequired") ?? false,
                    MemberName: member.Name,
                    Kind: kind,
                    IsNullableValueType: isNullableValueType));
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
                unsupportedReason);
        }

        /// <summary>
        /// Maps a member's CLR type onto how its value is written, or
        /// <see cref="MemberKind.Unsupported"/> when this generator cannot write it yet.
        /// </summary>
        private static MemberKind ClassifyMember(ITypeSymbol type, out bool isNullableValueType)
        {
            isNullableValueType = false;

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
                // A nullable reference type is just the reference type; only value types wrap.
                if (isNullableValueType && !type.IsValueType)
                {
                    isNullableValueType = false;
                }

                return kind;
            }

            return type.ToDisplayString() switch
            {
                "System.Guid" => MemberKind.Guid,
                "System.TimeSpan" => MemberKind.TimeSpan,
                _ => MemberKind.Unsupported
            };
        }

        /// <summary>
        /// The contract namespace a type gets when its [DataContract] does not name one:
        /// the CLR namespace appended to a fixed prefix. An empty CLR namespace yields the prefix
        /// with no trailing segment.
        /// </summary>
        private static string DefaultNamespaceFor(INamedTypeSymbol type) =>
            type.ContainingNamespace.IsGlobalNamespace
                ? DefaultNamespacePrefix
                : DefaultNamespacePrefix + type.ContainingNamespace.ToDisplayString();

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
