// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

var builder = DistributedApplication.CreateBuilder(args);

// The CoreWCF service to explore. It exposes an Echo operation at /echo with metadata (WSDL) enabled.
var echoService = builder.AddProject<Projects.CoreWcfSampleService>("echo-service");

// The SOAP explorer, run here as a project (no container image needed for local development), wired to
// the service above. In production use `builder.AddCoreWcfExplorer("wcf-explorer")` instead, which runs
// the published explorer container image; both expose the same WithCoreWcfService wiring.
builder.AddProject<Projects.CoreWCF_Aspire_Explorer>("wcf-explorer")
    .WithCoreWcfService(echoService, metadataPath: "/echo", name: "Echo service");

builder.Build().Run();
