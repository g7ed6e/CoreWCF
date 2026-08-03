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
    .WithCoreWcfService(echoService, metadataPath: "/echo", name: "Echo service")
    .WithCoreWcfService(echoService, metadataPath: "/inventory", name: "Inventory service");

builder.Build().Run();
