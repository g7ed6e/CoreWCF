// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CoreWCF;
using CoreWCF.Channels;
using CoreWCF.Configuration;
using CoreWCF.Description;
using CoreWcfSampleService;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddServiceModelServices();
builder.Services.AddServiceModelMetadata();
// Make the WSDL report the request's host/port so it is correct behind the Aspire proxy.
builder.Services.AddSingleton<IServiceBehavior, UseRequestHeadersForMetadataAddressBehavior>();

var app = builder.Build();

((IApplicationBuilder)app).UseServiceModel(serviceBuilder =>
{
    serviceBuilder.AddService<EchoService>();
    serviceBuilder.AddServiceEndpoint<EchoService, IEchoService>(
        new BasicHttpBinding(BasicHttpSecurityMode.None), "/echo");

    serviceBuilder.AddService<InventoryService>();
    serviceBuilder.AddServiceEndpoint<InventoryService, IInventoryService>(
        new BasicHttpBinding(BasicHttpSecurityMode.None), "/inventory");

    var metadata = app.Services.GetRequiredService<ServiceMetadataBehavior>();
    metadata.HttpGetEnabled = true;
});

app.Run();
