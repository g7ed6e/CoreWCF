# CoreWCF.Aspire.Hosting

A [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) hosting integration that adds a **SOAP
service explorer** to the Aspire dashboard, so the CoreWCF services orchestrated by your AppHost can be
browsed and invoked directly from the developer control plane (DCP).

The explorer is a companion web application (`CoreWCF.Aspire.Explorer`) that, for every registered
service, fetches its WSDL (`?singleWsdl`), lists its contracts and operations, and lets you edit a
pre-filled SOAP envelope and invoke the operation — a lightweight WCF Test Client / SoapUI, embedded in
your Aspire run.

## Usage

In your Aspire AppHost:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Your CoreWCF service(s), with metadata (WSDL) enabled.
var echo = builder.AddProject<Projects.MyCoreWcfService>("echo-service");

// Add the explorer and point it at the services to explore.
builder.AddCoreWcfExplorer("wcf-explorer")
       .WithCoreWcfService(echo, metadataPath: "/echo", name: "Echo service");

builder.Build().Run();
```

`AddCoreWcfExplorer` runs the explorer from its published container image. Open the **SOAP Explorer**
URL shown on the explorer resource in the dashboard.

### Requirements on the CoreWCF service

The service must expose metadata over HTTP GET so the explorer can read the WSDL:

```csharp
builder.Services.AddServiceModelServices();
builder.Services.AddServiceModelMetadata();
builder.Services.AddSingleton<IServiceBehavior, UseRequestHeadersForMetadataAddressBehavior>();
// ...
var metadata = app.Services.GetRequiredService<ServiceMetadataBehavior>();
metadata.HttpGetEnabled = true; // or HttpsGetEnabled
```

## API

- `IDistributedApplicationBuilder.AddCoreWcfExplorer(name = "wcf-explorer", port?, imageTag?)` — adds the
  explorer resource.
- `IResourceBuilder<TExplorer>.WithCoreWcfService(service, metadataPath = "/", name?, endpointName?)` —
  registers a CoreWCF service (any resource exposing an endpoint) with the explorer. The service's
  endpoint URL and metadata path are injected into the explorer via `CoreWcf:Services` configuration and
  the service is added as a reference the explorer waits for.

## Running the sample

See `samples/` for a runnable AppHost that hosts an Echo service and the explorer (run here as a project,
so no container image is required for local development):

```bash
dotnet run --project samples/CoreWcfSample.AppHost
```
