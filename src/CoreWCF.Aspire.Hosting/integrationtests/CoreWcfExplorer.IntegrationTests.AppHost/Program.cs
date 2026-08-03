// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

var builder = DistributedApplication.CreateBuilder(args);

// The service whose metadata the explorer will read. Same one the sample uses.
var echoService = builder.AddProject<Projects.CoreWcfSampleService>("echo-service");

// Which explorer image to run. CI publishes the image under a per-run tag and passes it in via
// CoreWcfExplorer__ImageTag; with nothing set this falls back to the package default, which is what
// a consumer of the published package gets.
var imageTag = builder.Configuration["CoreWcfExplorer:ImageTag"];

builder.AddCoreWcfExplorer("wcf-explorer", imageTag: string.IsNullOrWhiteSpace(imageTag) ? null : imageTag)
    // Aspire 9.x addresses a host process from a container as host.docker.internal, and before the
    // container tunnel arrived in Aspire 13.3 nothing made that name resolve on Linux. Docker Desktop
    // supplies it, so this is invisible on a developer machine; on a Linux CI runner the explorer fails
    // with "Failed to load WSDL: Name or service not known (host.docker.internal:<port>)".
    //
    // Deliberately here and not in AddCoreWcfExplorer: a consumer on a current Aspire gets the tunnel
    // and needs none of this, and a hosting integration is the wrong place for a container-runtime
    // flag. This AppHost is pinned to the 9.5.2 support floor by our own choice, so it compensates
    // itself - which is also what a Linux user on pre-13.3 Aspire has to do.
    .WithContainerRuntimeArgs("--add-host", "host.docker.internal:host-gateway")
    .WithCoreWcfService(echoService, metadataPath: "/echo", name: "Echo service")
    .WithCoreWcfService(echoService, metadataPath: "/inventory", name: "Inventory service");

builder.Build().Run();
