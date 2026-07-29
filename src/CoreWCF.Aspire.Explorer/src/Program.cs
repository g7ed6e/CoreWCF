// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CoreWCF.Aspire.Explorer.Services;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddFluentUIComponents();

builder.Services.AddSingleton<ServiceCatalog>();
builder.Services.AddHttpClient<WsdlExplorerService>();
builder.Services.AddHttpClient<SoapInvoker>();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");
app.MapHealthChecks("/health");

app.Run();

/// <summary>Marker type so the test host (WebApplicationFactory) can reference this entry point.</summary>
public partial class Program;
