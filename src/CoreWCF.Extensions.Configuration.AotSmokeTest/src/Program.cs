// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using CoreWCF;
using CoreWCF.Configuration;
using CoreWCF.Description;
using CoreWCF.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CoreWCF.Extensions.Configuration.AotSmokeTest;

[ServiceContract]
public interface IEchoService
{
    [OperationContract]
    string Echo(string value);
}

public class EchoService : IEchoService
{
    public string Echo(string value) => value;
}

/// <summary>
/// Every type the configuration below names.
/// </summary>
/// <remarks>
/// The list is the whole point of the exercise: under Native AOT nothing else roots these types, and a
/// name resolved from a string reaches a type the compiler removed. Deleting one attribute here is the
/// way to watch this fail.
/// </remarks>
[ServiceModelConfigurable(typeof(BasicHttpBinding), Name = "basicHttp")]
[ServiceModelConfigurable(typeof(Channels.CustomBinding), Name = "custom")]
[ServiceModelConfigurable(typeof(Channels.TextMessageEncodingBindingElement), Name = "textEncoding")]
[ServiceModelConfigurable(typeof(Channels.HttpTransportBindingElement), Name = "httpTransport")]
[ServiceModelConfigurable(typeof(EchoService), Name = "echo")]
[ServiceModelConfigurable(typeof(IEchoService), Name = "IEcho")]
public partial class SmokeTestConfiguration : ServiceModelConfigurationContext
{
}

/// <summary>
/// Builds a CoreWCF host described only by configuration, under Native AOT, and reports what it got.
/// </summary>
/// <remarks>
/// <para>
/// Scoped to host startup: it builds the application, drains the configured endpoints into CoreWCF's
/// service model and asserts they arrived, then stops. It deliberately does not call the service.
/// </para>
/// <para>
/// That boundary is not caution, it is what this package is responsible for. Answering a SOAP call
/// under Native AOT needs a serializer, and the reflection based DataContractSerializer is not one:
/// the feat/aot-datacontractserializer work found that it silently writes a truncated document
/// before the contract types are rooted and throws NullReferenceException once they are. Calling
/// through here would be measuring that gap rather than this one. The call-through version belongs
/// in this file once that branch lands.
/// </para>
/// </remarks>
public static class Program
{
    private const string ServiceName = "echo";
    private const string ContractName = "IEcho";

    public static int Main()
    {
        var settings = new Dictionary<string, string?>
        {
            // Named by the short names the context registered. The assembly qualified spelling resolves
            // through the same table; this is the one worth exercising because it is the one that has no
            // hope at all without a context.
            ["ServiceModel:Bindings:internal:Type"] = "basicHttp",
            ["ServiceModel:Bindings:internal:MaxReceivedMessageSize"] = "1048576",
            ["ServiceModel:Bindings:internal:TextEncoding"] = "utf-8",

            // The shape nothing else can express: an ordered list of polymorphic elements, each named by
            // a discriminator, appended through a collection whose Add the reflective path can only reach
            // by constructing ICollection<T> at run time - which is the one call Native AOT cannot make.
            ["ServiceModel:Bindings:custom:Type"] = "custom",
            ["ServiceModel:Bindings:custom:Elements:0:Type"] = "textEncoding",
            ["ServiceModel:Bindings:custom:Elements:0:MessageVersion"] = "Soap11",
            ["ServiceModel:Bindings:custom:Elements:0:WriteEncoding"] = "utf-8",
            ["ServiceModel:Bindings:custom:Elements:1:Type"] = "httpTransport",
            ["ServiceModel:Bindings:custom:Elements:1:MaxReceivedMessageSize"] = "1048576",

            [$"ServiceModel:Services:{ServiceName}:Endpoints:0:Contract"] = ContractName,
            [$"ServiceModel:Services:{ServiceName}:Endpoints:0:Binding"] = "internal",
            [$"ServiceModel:Services:{ServiceName}:Endpoints:0:Address"] = "/echo/basic.svc",

            [$"ServiceModel:Services:{ServiceName}:Endpoints:1:Contract"] = ContractName,
            [$"ServiceModel:Services:{ServiceName}:Endpoints:1:Binding"] = "custom",
            [$"ServiceModel:Services:{ServiceName}:Endpoints:1:Address"] = "/echo/custom.svc",
        };

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);

        builder.Services.AddServiceModelServices();

        // Strict rather than left to default. The default would already be true here - this runs with
        // dynamic code unavailable - but saying so means a regression that reintroduces the JIT is
        // reported as a failure rather than passing on the reflective path.
        builder.Services.AddSingleton(new ServiceModelConfigurationOptions
        {
            Context = new SmokeTestConfiguration(),
            RequireGeneratedMetadata = true,
        });

        builder.Services.AddServiceModelConfiguration(builder.Configuration.GetSection("ServiceModel"));

        WebApplication app = builder.Build();
        app.UseServiceModel();

        return Report(app);
    }

    /// <summary>
    /// Drains the configured endpoints into the service model and reports what arrived.
    /// </summary>
    /// <remarks>
    /// <c>UseServiceModel</c> is what applies <c>ServiceModelOptions</c>, so the endpoints exist only
    /// after it has run. Reading them back off <see cref="IServiceBuilder"/> is what distinguishes "the
    /// host started" from "the configuration was understood" - a host with no endpoints starts perfectly
    /// well.
    /// </remarks>
    private static int Report(WebApplication app)
    {
        Console.WriteLine($"IsDynamicCodeSupported: {System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}");

        var serviceBuilder = app.Services.GetRequiredService<IServiceBuilder>();
        Type[] services = serviceBuilder.Services.ToArray();

        foreach (Type service in services)
        {
            Console.WriteLine($"service: {service.FullName}");
        }

        if (services.Length != 1 || services[0] != typeof(EchoService))
        {
            Console.WriteLine("FAIL: the configured service did not reach the service model.");
            return 1;
        }

        // The contract has to have arrived with its operations. That is the assertion the
        // [DynamicDependency] the generator emits exists for: without it TypeLoader reflects over an
        // interface the compiler trimmed to nothing, and the description comes back empty.
        ContractDescription contract = ContractDescription.GetContract<EchoService>(typeof(IEchoService));
        Console.WriteLine($"contract: {contract.Name}, operations: {contract.Operations.Count}");

        if (contract.Operations.Count != 1)
        {
            Console.WriteLine("FAIL: the contract arrived with no operations, so it was trimmed.");
            return 1;
        }

        Console.WriteLine("PASS");
        return 0;
    }
}
