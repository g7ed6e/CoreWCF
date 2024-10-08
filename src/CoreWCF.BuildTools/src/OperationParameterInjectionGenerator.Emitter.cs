﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CoreWCF.BuildTools;

public sealed partial class OperationParameterInjectionGenerator
{
    private record struct MessageProperty(
        string PropertyName,
        string PropertyTypeFullName,
        string OutputVarName);

    private record struct KeyedService(TypedConstant ServiceKey);

    private sealed class Emitter
    {
        private readonly StringBuilder _builder;
        private readonly OperationParameterInjectionSourceGenerationContext _sourceGenerationContext;
        private readonly SourceGenerationSpec _generationSpec;

        public Emitter(in OperationParameterInjectionSourceGenerationContext sourceGenerationContext, in SourceGenerationSpec generationSpec)
        {
            _sourceGenerationContext = sourceGenerationContext;
            _generationSpec = generationSpec;
            _builder = new StringBuilder();
        }

        public void Emit()
        {
            _builder.Clear();
            _builder.AppendLine($@"// <auto-generated>
// Generated by the CoreWCF.BuildTools.OperationParameterInjectionGenerator source generator. DO NOT EDIT!
// </auto-generated>
#nullable disable
using System;
using Microsoft.Extensions.DependencyInjection;");

            foreach (var operationContractSpec in _generationSpec.OperationContractSpecs)
            {
                EmitOperationContract(operationContractSpec);
            }

            if (_generationSpec.OperationContractSpecs.Length > 0)
            {
                _builder.AppendLine("#nullable restore");
                _sourceGenerationContext.AddSource("OperationParameterInjection.g.cs", SourceText.From(_builder.ToString(), Encoding.UTF8, SourceHashAlgorithm.Sha256));
            }
        }

        private void EmitOperationContract(OperationContractSpec operationContractSpec)
        {
            Dictionary<IParameterSymbol, string> messagePropertyNames = new(SymbolEqualityComparer.Default);
            Dictionary<IParameterSymbol, KeyedService> keyedServices = new(SymbolEqualityComparer.Default);
            List<MessageProperty> messageProperties = new();

            foreach (var parameter in operationContractSpec.UserProvidedOperationContractImplementation!.Parameters)
            {
                if (operationContractSpec.MissingOperationContract.Parameters.Any(p => p.IsMatchingParameter(parameter)))
                {
                    continue;
                }

                foreach (AttributeData attribute in parameter.GetAttributes())
                {
                    if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass,
                            _generationSpec.CoreWCFInjectedSymbol))
                    {
                        if (attribute.NamedArguments.Length > 1)
                        {
                            _sourceGenerationContext.ReportDiagnostic(DiagnosticDescriptors.OperationParameterInjectionGenerator_01XX.RaiseEitherPropertyNameOrServiceKeyShouldBeSpecified(parameter.Locations[0]));
                            return;
                        }

                        foreach (var namedArgument in attribute.NamedArguments)
                        {
                            if (namedArgument.Key == "PropertyName")
                            {
                                if (namedArgument.Value.IsNull)
                                {
                                    _sourceGenerationContext.ReportDiagnostic(
                                        DiagnosticDescriptors.OperationParameterInjectionGenerator_01XX
                                            .RaisePropertyNameCannotBeNullOrEmptyError(parameter.Locations[0]));
                                    return;
                                }
                                var propertyName = namedArgument.Value.Value!.ToString();
                                if (propertyName is "")
                                {
                                    _sourceGenerationContext.ReportDiagnostic(
                                        DiagnosticDescriptors.OperationParameterInjectionGenerator_01XX
                                            .RaisePropertyNameCannotBeNullOrEmptyError(parameter.Locations[0]));
                                    return;
                                }
                                var messagePropertyVariableName = propertyName.ToMessagePropertyVariableName();
                                messagePropertyNames[parameter] = messagePropertyVariableName;
                                messageProperties.Add(new MessageProperty(propertyName, parameter.Type.ToDisplayString(),
                                    messagePropertyVariableName));
                            }
                            else if (namedArgument.Key == "ServiceKey")
                            {
                                keyedServices[parameter] = new KeyedService(namedArgument.Value);
                            }
                        }
                    }
                    else if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass,
                                 _generationSpec.MicrosoftExtensionsDependencyInjectionFromKeyedServicesSymbol))
                    {
                        keyedServices[parameter] = new KeyedService(attribute.ConstructorArguments.First());
                    }
                }
            }

            Dictionary<ITypeSymbol, string> dependencyNames = new(SymbolEqualityComparer.Default);
            var dependencies = operationContractSpec.UserProvidedOperationContractImplementation!.Parameters.Where(x => !operationContractSpec.MissingOperationContract!.Parameters.Any(p =>
                p.IsMatchingParameter(x))).ToList();

            bool shouldGenerateAsyncAwait = SymbolEqualityComparer.Default.Equals(operationContractSpec.MissingOperationContract!.ReturnType, _generationSpec.TaskSymbol)
                                            || (operationContractSpec.MissingOperationContract.ReturnType is INamedTypeSymbol symbol &&
                                                SymbolEqualityComparer.Default.Equals(symbol.ConstructedFrom, _generationSpec.GenericTaskSymbol));

            string @async = shouldGenerateAsyncAwait
                ? "async "
                : string.Empty;

            string @await = shouldGenerateAsyncAwait
                ? "await "
                : string.Empty;

            string @return = (operationContractSpec.MissingOperationContract.ReturnsVoid || SymbolEqualityComparer.Default.Equals(operationContractSpec.MissingOperationContract.ReturnType, _generationSpec.TaskSymbol)) ?
                string.Empty
                : "return ";

            string GetAccessibilityModifier(Accessibility accessibility) => accessibility switch
            {
                Accessibility.Private => "private ",
                Accessibility.Protected => "protected ",
                Accessibility.Public => "public ",
                _ => "internal "
            };

            bool isServiceContractImplInGlobalNamespace = operationContractSpec.ServiceContractImplementation!.ContainingNamespace
                .IsGlobalNamespace;

            string returnType = operationContractSpec.MissingOperationContract.ReturnsVoid
                ? "void"
                : $"{operationContractSpec.MissingOperationContract.ReturnType}";

            string parameters = string.Join(", ", operationContractSpec.MissingOperationContract.Parameters
                .Select(static p => p.RefKind switch
                {
                    RefKind.Ref => $"ref {p.Type} {p.Name}",
                    RefKind.Out => $"out {p.Type} {p.Name}",
                    _ => $"{p.Type} {p.Name}",
                }));

            var indentor = new Indentor();

            if (!isServiceContractImplInGlobalNamespace)
            {
                _builder.AppendLine($@"namespace {operationContractSpec.ServiceContractImplementation!.ContainingNamespace}
{{");
                indentor.Increment();
            }

            Stack<INamedTypeSymbol> classes = new();
            INamedTypeSymbol containingType = operationContractSpec.ServiceContractImplementation;
            while (containingType != null)
            {
                classes.Push(containingType);
                containingType = containingType.ContainingType;
            }

            while (classes.Count > 0)
            {
                containingType = classes.Pop();
                _builder.AppendLine($@"{indentor}{GetAccessibilityModifier(containingType.DeclaredAccessibility)}partial class {containingType.Name}");
                _builder.AppendLine($@"{indentor}{{");
                indentor.Increment();
            }

            foreach (AttributeData attributeData in operationContractSpec.UserProvidedOperationContractImplementation.GetAttributes())
            {
                _builder.Append($"{indentor}[{attributeData.AttributeClass}(");
                _builder.Append(string.Join(", ", attributeData.ConstructorArguments.Select(x => x.ToSafeCSharpString()).Union(attributeData.NamedArguments.Select(x => $@"{x.Key} = {x.Value.ToSafeCSharpString()}") )));
                _builder.Append(")]");
                _builder.AppendLine();
            }

            _builder.AppendLine($@"{indentor}public {@async}{returnType} {operationContractSpec.MissingOperationContract.Name}({parameters})");
            _builder.AppendLine($@"{indentor}{{");
            indentor.Increment();
            _builder.AppendLine($@"{indentor}var serviceProvider = CoreWCF.OperationContext.Current.InstanceContext.Extensions.Find<IServiceProvider>();");
            _builder.AppendLine($@"{indentor}if (serviceProvider == null) throw new InvalidOperationException(""Missing IServiceProvider in InstanceContext extensions"");");

            int objectIndex = 0;
            if (dependencies.Any(x => SymbolEqualityComparer.Default.Equals(x.Type, operationContractSpec.HttpContextSymbol)
                                      || SymbolEqualityComparer.Default.Equals(x.Type, operationContractSpec.HttpRequestSymbol)
                                      || SymbolEqualityComparer.Default.Equals(x.Type, operationContractSpec.HttpResponseSymbol)))
            {
                _builder.AppendLine($@"{indentor}var httpContext = (CoreWCF.OperationContext.Current.RequestContext.RequestMessage.Properties.TryGetValue(""Microsoft.AspNetCore.Http.HttpContext"", out var o{objectIndex})");
                indentor.Increment();
                _builder.AppendLine($@"{indentor}&& o{objectIndex} is Microsoft.AspNetCore.Http.HttpContext p{objectIndex})");
                _builder.AppendLine($@"{indentor}? p{objectIndex}");
                _builder.AppendLine($@"{indentor}: null;");
                indentor.Decrement();
                _builder.AppendLine($@"{indentor}if (httpContext == null) throw new InvalidOperationException(""Missing HttpContext in RequestMessage properties"");");
                objectIndex++;
            }

            foreach ((string propertyName, string propertyTypeFullName, string outputVar) in messageProperties)
            {
                _builder.AppendLine($@"{indentor}var {outputVar} = (CoreWCF.OperationContext.Current.IncomingMessageProperties.TryGetValue(""{propertyName}"", out var o{objectIndex})");
                indentor.Increment();
                _builder.AppendLine($@"{indentor}&& o{objectIndex} is {propertyTypeFullName} p{objectIndex})");
                _builder.AppendLine($@"{indentor}? p{objectIndex}");
                _builder.AppendLine($@"{indentor}: null;");
                indentor.Decrement();
                objectIndex++;
            }

            _builder.AppendLine($@"{indentor}if (CoreWCF.OperationContext.Current.InstanceContext.IsSingleton)");
            _builder.AppendLine($@"{indentor}{{");
            indentor.Increment();
            _builder.AppendLine($@"{indentor}using (var scope = serviceProvider.CreateScope())");
            _builder.AppendLine($@"{indentor}{{");
            indentor.Increment();

            string dependencyNamePrefix = "d";
            string serviceProviderName = "scope.ServiceProvider";

            AppendResolveDependencies();
            AppendInvokeUserProvidedImplementation();

            if (operationContractSpec.MissingOperationContract.ReturnsVoid || SymbolEqualityComparer.Default.Equals(operationContractSpec.MissingOperationContract.ReturnType, _generationSpec.TaskSymbol))
            {
                _builder.AppendLine($@"{indentor}return;");
            }

            indentor.Decrement();
            _builder.AppendLine($@"{indentor}}}");
            indentor.Decrement();
            _builder.AppendLine($@"{indentor}}}");

            dependencyNamePrefix = "e";
            serviceProviderName = "serviceProvider";

            AppendResolveDependencies();
            AppendInvokeUserProvidedImplementation();

            while (indentor.Level > 0)
            {
                indentor.Decrement();
                _builder.AppendLine($@"{indentor}}}");
            }

            void AppendResolveDependencies()
            {
                for (int i = 0; i < dependencies.Count; i++)
                {
                    dependencyNames[dependencies[i].Type] = $"{dependencyNamePrefix}{i}";
                    if (SymbolEqualityComparer.Default.Equals(operationContractSpec.HttpContextSymbol, dependencies[i].Type))
                    {
                        _builder.AppendLine($@"{indentor}var {dependencyNamePrefix}{i} = httpContext;");
                    }
                    else if (SymbolEqualityComparer.Default.Equals(operationContractSpec.HttpRequestSymbol, dependencies[i].Type))
                    {
                        _builder.AppendLine($@"{indentor}var {dependencyNamePrefix}{i} = httpContext.Request;");
                    }
                    else if (SymbolEqualityComparer.Default.Equals(operationContractSpec.HttpResponseSymbol, dependencies[i].Type))
                    {
                        _builder.AppendLine($@"{indentor}var {dependencyNamePrefix}{i} = httpContext.Response;");
                    }
                    else if (messagePropertyNames.TryGetValue(dependencies[i], out string messagePropertyVariableName))
                    {
                        _builder.AppendLine($@"{indentor}var {dependencyNamePrefix}{i} = {messagePropertyVariableName};");
                    }
                    else if (keyedServices.TryGetValue(dependencies[i], out var keyedService))
                    {
                        _builder.AppendLine($@"{indentor}var {dependencyNamePrefix}{i} = {serviceProviderName}.GetKeyedService<{dependencies[i].Type}>({keyedService.ServiceKey.ToSafeCSharpString()});");
                    }
                    else
                    {
                        _builder.AppendLine($@"{indentor}var {dependencyNamePrefix}{i} = {serviceProviderName}.GetService<{dependencies[i].Type}>();");
                    }
                }
            }

            void AppendInvokeUserProvidedImplementation()
            {
                _builder.Append($"{indentor}{@return}{@await}{operationContractSpec.UserProvidedOperationContractImplementation.Name}(");
                for (int i = 0,j = 0; i < operationContractSpec.UserProvidedOperationContractImplementation.Parameters.Length; i++)
                {
                    IParameterSymbol parameter = operationContractSpec.UserProvidedOperationContractImplementation.Parameters[i];
                    if (i != 0)
                    {
                        _builder.Append(", ");
                    }

                    if (parameter.GetOneAttributeOf(_generationSpec.CoreWCFInjectedSymbol,
                            _generationSpec.MicrosoftAspNetCoreMvcFromServicesSymbol,
                            _generationSpec.MicrosoftExtensionsDependencyInjectionFromKeyedServicesSymbol) is not null)
                    {
                        _builder.Append(dependencyNames[parameter.Type]);
                    }
                    else
                    {
                        IParameterSymbol originalParameter = operationContractSpec.MissingOperationContract.Parameters[j];
                        _builder.Append(originalParameter.RefKind switch
                        {
                            RefKind.Ref => $"ref {originalParameter.Name}",
                            RefKind.Out => $"out {originalParameter.Name}",
                            _ => originalParameter.Name,
                        });
                        j++;
                    }
                }
                _builder.AppendLine(");");
            }
        }
    }
}
