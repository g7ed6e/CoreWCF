// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CoreWcfExplorer.IntegrationTests;

/// <summary>
/// Starts the AppHost once for the whole test class. Starting it means DCP really launches the
/// explorer container and the CoreWCF service process, so the cost is paid once rather than per test.
/// </summary>
public sealed class ExplorerAppHostFixture : IAsyncLifetime
{
    /// <summary>Matches the resource name the AppHost passes to <c>AddCoreWcfExplorer</c>.</summary>
    public const string ExplorerResourceName = "wcf-explorer";

    /// <summary>Generous: a cold run pulls the aspnet base image and starts a container.</summary>
    private static readonly TimeSpan s_startupTimeout = TimeSpan.FromMinutes(5);

    private DistributedApplication? _app;

    /// <summary>An HttpClient addressed at the explorer's endpoint, via Aspire's service discovery.</summary>
    public HttpClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.CoreWcfExplorer_IntegrationTests_AppHost>();

        _app = await builder.BuildAsync();

        var notifications = _app.Services.GetRequiredService<ResourceNotificationService>();

        using var cts = new CancellationTokenSource(s_startupTimeout);
        await _app.StartAsync(cts.Token);

        // Healthy rather than merely Running. The resource only turns healthy once the probe that
        // AddCoreWcfExplorer attaches - WithHttpHealthCheck("/health") - has actually answered from
        // inside the container, so this single wait covers "the image boots" and "the health wiring
        // is real" at the same time.
        await notifications.WaitForResourceHealthyAsync(ExplorerResourceName, cts.Token);

        Client = _app.CreateHttpClient(ExplorerResourceName, CoreWcfExplorerEndpointName);
    }

    /// <summary>The endpoint name AddCoreWcfExplorer gives the explorer's HTTP endpoint.</summary>
    private const string CoreWcfExplorerEndpointName = "http";

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}

[CollectionDefinition(nameof(ExplorerAppHostCollection))]
public sealed class ExplorerAppHostCollection : ICollectionFixture<ExplorerAppHostFixture>;
