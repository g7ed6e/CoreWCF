// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreWCF.Channels;
using CoreWCF.Configuration;
using Helpers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]
namespace CoreWCF.Primitives.Tests;

[Collection("TelemetryTests")]
public class TelemetryTests
{
    [Fact]
    public async Task Basic_Telemetry_Test()
    {
        string telemetryEchoAction = "http://tempuri.org/ISimpleTelemetryService/Echo";
        var startedActivities = new ConcurrentBag<Activity>();
        var stoppedActivities = new ConcurrentBag<Activity>();

        // ShouldListenTo must be set to its final predicate before AddActivityListener is called.
        // ActivitySource attaches a listener at AddActivityListener time (for existing sources) and at
        // ActivitySource construction time (for sources created later). In both cases ShouldListenTo is
        // evaluated only once per (listener, source) pair; mutating ShouldListenTo afterwards does NOT
        // retroactively attach the listener to an already-existing source. The static
        // WcfInstrumentationActivitySource.ActivitySource may be initialized by a concurrent test
        // (e.g. DispatchBuilderTests in the default xUnit collection) before this test reaches
        // AddActivityListener, so a "_ => false" predicate at that moment would permanently prevent
        // attachment regardless of subsequent updates. Filtering by the unique DisplayName below
        // ensures activities created by concurrent tests do not interfere with the assertions.
        using var listener = new ActivityListener
        {
            ShouldListenTo = activitySource => activitySource.Name == "CoreWCF.Primitives",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => startedActivities.Add(activity),
            ActivityStopped = activity => stoppedActivities.Add(activity)
        };

        string serviceAddress = "http://localhost/dummy";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServiceModelServices();
        IServer server = new MockServer();
        services.AddSingleton(server);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.RegisterApplicationLifetime();
        ServiceProvider serviceProvider = services.BuildServiceProvider();
        IServiceBuilder serviceBuilder = serviceProvider.GetRequiredService<IServiceBuilder>();
        serviceBuilder.BaseAddresses.Add(new Uri(serviceAddress));
        serviceBuilder.AddService<SimpleTelemetryService>();
        var binding = new CustomBinding("BindingName", "BindingNS");
        binding.Elements.Add(new MockTransportBindingElement());
        serviceBuilder.AddServiceEndpoint<SimpleTelemetryService, ISimpleTelemetryService>(binding, serviceAddress);
        await serviceBuilder.OpenAsync(TestContext.Current.CancellationToken);
        IDispatcherBuilder dispatcherBuilder = serviceProvider.GetRequiredService<IDispatcherBuilder>();
        System.Collections.Generic.List<IServiceDispatcher> dispatchers =
            dispatcherBuilder.BuildDispatchers(typeof(SimpleTelemetryService));
        Assert.Single(dispatchers);
        IServiceDispatcher serviceDispatcher = dispatchers[0];
        Assert.Equal("foo", serviceDispatcher.Binding.Scheme);
        Assert.Equal(serviceAddress, serviceDispatcher.BaseAddress.ToString());
        IChannel mockChannel = new MockReplyChannel(serviceProvider);
        IServiceChannelDispatcher dispatcher =
            await serviceDispatcher.CreateServiceChannelDispatcherAsync(mockChannel);
        var requestContext = TestRequestContext.Create(serviceAddress, telemetryEchoAction);

        ActivitySource.AddActivityListener(listener);
        await dispatcher.DispatchAsync(requestContext);
        Assert.True(await requestContext.WaitForReplyAsync(TestContext.Current.CancellationToken), "Dispatcher didn't send reply");
        requestContext.ValidateReply(telemetryEchoAction + "Response");

        // ActivityStarted/ActivityStopped callbacks run synchronously inside Activity.Start()/Stop(),
        // and CoreWCF stops the activity before sending the reply, so by the time WaitForReplyAsync
        // returns the activity should already be in the bags. Poll briefly with the test cancellation
        // token in case there is any small async window on a heavily loaded machine. If the activity
        // is genuinely missing this fails fast with an explicit message rather than a TaskCanceledException.
        using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        pollCts.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (!stoppedActivities.Any(a => a.DisplayName == telemetryEchoAction))
            {
                await Task.Delay(50, pollCts.Token);
            }
        }
        catch (OperationCanceledException) when (pollCts.IsCancellationRequested && !TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            Assert.Fail($"No Activity with DisplayName '{telemetryEchoAction}' was captured within 30s. " +
                $"Started count: {startedActivities.Count}, Stopped count: {stoppedActivities.Count}. " +
                "This usually indicates the ActivityListener was not attached to the CoreWCF.Primitives ActivitySource.");
        }

        var startedActivity = Assert.Single(startedActivities, a => a.DisplayName == telemetryEchoAction);
        var stoppedActivity = Assert.Single(stoppedActivities, a => a.DisplayName == telemetryEchoAction);

        Assert.Equal("CoreWCF.Primitives.IncomingRequest", startedActivity.OperationName);
        Assert.Equal("CoreWCF.Primitives.IncomingRequest", stoppedActivity.OperationName);
        Assert.Equal(telemetryEchoAction, startedActivity.DisplayName);
        Assert.Equal(telemetryEchoAction, stoppedActivity.DisplayName);
        Assert.Equal(ActivityKind.Server, startedActivity.Kind);
        Assert.Equal(ActivityKind.Server, stoppedActivity.Kind);
        Assert.Equal(startedActivity.RootId, stoppedActivity.RootId);
        Assert.Equal(startedActivity.SpanId, stoppedActivity.SpanId);
        Assert.Equal(startedActivity.TraceId, stoppedActivity.TraceId);

        var startedTags = startedActivity.Tags.ToList();
        var stoppedTags = stoppedActivity.Tags.ToList();
        Assert.Equal(7, startedTags.Count);
        Assert.Equal(7, stoppedTags.Count);

        Assert.Equal("rpc.system", startedTags[0].Key);
        Assert.Equal("dotnet_wcf", startedTags[0].Value);

        Assert.Equal("rpc.method", startedTags[1].Key);
        Assert.Equal(telemetryEchoAction, startedTags[1].Value);

        Assert.Equal("soap.message_version", startedTags[2].Key);
        Assert.Equal("Soap11 (http://schemas.xmlsoap.org/soap/envelope/) AddressingNone (http://schemas.microsoft.com/ws/2005/05/addressing/none)", startedTags[2].Value);

        Assert.Equal("server.address", startedTags[3].Key);
        Assert.Equal("localhost", startedTags[3].Value);

        Assert.Equal("wcf.channel.scheme", startedTags[4].Key);
        Assert.Equal("http", startedTags[4].Value);

        Assert.Equal("wcf.channel.path", startedTags[5].Key);
        Assert.Equal("/dummy", startedTags[5].Value);

        Assert.Equal("soap.reply_action", startedTags[6].Key);
        Assert.Equal(telemetryEchoAction + "Response", startedTags[6].Value);

        Assert.Equivalent(startedTags[1], stoppedTags[1]);
        Assert.Equivalent(startedTags[2], stoppedTags[2]);
        Assert.Equivalent(startedTags[3], stoppedTags[3]);
        Assert.Equivalent(startedTags[4], stoppedTags[4]);
        Assert.Equivalent(startedTags[5], stoppedTags[5]);
        Assert.Equivalent(startedTags[6], stoppedTags[6]);
    }

    // Regression test for https://github.com/CoreWCF/CoreWCF/issues/1677
    // Prior to the fix, the per-request Activity was stored in a single instance field
    // (ImmutableDispatchRuntime._activity) shared by every request flowing through the
    // endpoint. Under concurrency, a later request overwrote the field before an earlier
    // request reached the code that stops the Activity, so the earlier Activity was never
    // stopped. Those never-stopped Activity instances accumulated on the Gen2 heap and
    // eventually exhausted memory. This test dispatches several requests concurrently and
    // asserts that every started Activity is also stopped.
    [Fact]
    public async Task Concurrent_Requests_Do_Not_Leak_Activities()
    {
        // Kept below the default MaxConcurrentCalls floor (16 * ProcessorCount, i.e. 16 even on a
        // single-core runner) so all requests can be in flight simultaneously without any being
        // held back by the concurrency throttle, which would deadlock against the gate below.
        const int RequestCount = 10;
        string echoAction = "http://tempuri.org/IConcurrentTelemetryService/Echo";
        var startedActivities = new ConcurrentBag<Activity>();
        var stoppedActivities = new ConcurrentBag<Activity>();

        // Filter by the unique DisplayName (the SOAP action) so activities created by other
        // tests sharing the static CoreWCF.Primitives ActivitySource cannot skew the counts.
        // See the detailed note in Basic_Telemetry_Test about ActivityListener attachment timing.
        using var listener = new ActivityListener
        {
            ShouldListenTo = activitySource => activitySource.Name == "CoreWCF.Primitives",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => { if (activity.DisplayName == echoAction) startedActivities.Add(activity); },
            ActivityStopped = activity => { if (activity.DisplayName == echoAction) stoppedActivities.Add(activity); }
        };

        string serviceAddress = "http://localhost/dummy";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServiceModelServices();
        IServer server = new MockServer();
        services.AddSingleton(server);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.RegisterApplicationLifetime();
        ServiceProvider serviceProvider = services.BuildServiceProvider();
        IServiceBuilder serviceBuilder = serviceProvider.GetRequiredService<IServiceBuilder>();
        serviceBuilder.BaseAddresses.Add(new Uri(serviceAddress));
        serviceBuilder.AddService<ConcurrentTelemetryService>();
        var binding = new CustomBinding("BindingName", "BindingNS");
        binding.Elements.Add(new MockTransportBindingElement());
        serviceBuilder.AddServiceEndpoint<ConcurrentTelemetryService, IConcurrentTelemetryService>(binding, serviceAddress);
        await serviceBuilder.OpenAsync(TestContext.Current.CancellationToken);
        IDispatcherBuilder dispatcherBuilder = serviceProvider.GetRequiredService<IDispatcherBuilder>();
        List<IServiceDispatcher> dispatchers = dispatcherBuilder.BuildDispatchers(typeof(ConcurrentTelemetryService));
        IServiceDispatcher serviceDispatcher = Assert.Single(dispatchers);

        // Gate: hold every request inside the service operation (i.e. after its Activity has
        // been created) until all RequestCount requests have arrived. This forces all the
        // Activities to be alive simultaneously, deterministically exercising the shared-field
        // race instead of relying on lucky timing.
        using var allRequestsEntered = new CountdownEvent(RequestCount);
        using var releaseGate = new ManualResetEventSlim(false);
        ConcurrentTelemetryService.OnOperationEntered = allRequestsEntered;
        ConcurrentTelemetryService.ReleaseGate = releaseGate;

        ActivitySource.AddActivityListener(listener);

        var contexts = new List<TestRequestContext>(RequestCount);
        var dispatchTasks = new List<Task>(RequestCount);
        for (int i = 0; i < RequestCount; i++)
        {
            IChannel mockChannel = new MockReplyChannel(serviceProvider);
            IServiceChannelDispatcher dispatcher = await serviceDispatcher.CreateServiceChannelDispatcherAsync(mockChannel);
            var requestContext = TestRequestContext.Create(serviceAddress, echoAction);
            contexts.Add(requestContext);
            // Run the pipeline on the thread pool: the operation blocks on the gate below, so
            // dispatching inline would stall this loop before every request is in flight.
            dispatchTasks.Add(Task.Run(() => dispatcher.DispatchAsync(requestContext)));
        }

        Assert.True(allRequestsEntered.Wait(TimeSpan.FromSeconds(30)),
            $"Only {RequestCount - allRequestsEntered.CurrentCount} of {RequestCount} requests reached the service operation.");

        // All Activities have now been created (and, with the bug, all but one overwritten in
        // the shared field). Let the operations complete so each runs the reply path that stops
        // the Activity.
        releaseGate.Set();

        foreach (var requestContext in contexts)
        {
            Assert.True(await requestContext.WaitForReplyAsync(TestContext.Current.CancellationToken), "Dispatcher didn't send reply");
        }
        await Task.WhenAll(dispatchTasks);

        // The Activity is stopped synchronously (before the reply is sent), so by the time every
        // reply has arrived every Activity that is going to be stopped already has been.
        var startedIds = startedActivities.Select(a => a.SpanId).ToList();
        var stoppedIds = stoppedActivities.Select(a => a.SpanId).ToHashSet();

        Assert.Equal(RequestCount, startedIds.Count);
        Assert.Equal(RequestCount, startedIds.Distinct().Count());

        // Every started Activity must also have been stopped. With the shared-field bug the
        // overwritten Activities are never passed to Stop(), so they are missing here.
        var leaked = startedIds.Where(id => !stoppedIds.Contains(id)).ToList();
        Assert.True(leaked.Count == 0,
            $"{leaked.Count} of {RequestCount} started Activities were never stopped (leaked). " +
            $"Started: {startedIds.Count}, distinct stopped: {stoppedIds.Count}.");
    }

    [CoreWCF.ServiceContract]
    [System.ServiceModel.ServiceContract]
    public interface ISimpleTelemetryService
    {
        [CoreWCF.OperationContract]
        [System.ServiceModel.OperationContract]
        string Echo(string echo);
    }

    internal class SimpleTelemetryService : ISimpleTelemetryService
    {
        public string Echo(string echo)
        {
            return echo;
        }
    }

    [CoreWCF.ServiceContract]
    [System.ServiceModel.ServiceContract]
    public interface IConcurrentTelemetryService
    {
        [CoreWCF.OperationContract]
        [System.ServiceModel.OperationContract]
        string Echo(string echo);
    }

    [CoreWCF.ServiceBehavior(ConcurrencyMode = CoreWCF.ConcurrencyMode.Multiple, InstanceContextMode = CoreWCF.InstanceContextMode.Single)]
    internal class ConcurrentTelemetryService : IConcurrentTelemetryService
    {
        internal static CountdownEvent OnOperationEntered;
        internal static ManualResetEventSlim ReleaseGate;

        public string Echo(string echo)
        {
            // Signal that this request has reached the operation (its Activity already exists),
            // then wait until the test releases all requests at once.
            OnOperationEntered.Signal();
            ReleaseGate.Wait(TimeSpan.FromSeconds(30));
            return echo;
        }
    }
}
