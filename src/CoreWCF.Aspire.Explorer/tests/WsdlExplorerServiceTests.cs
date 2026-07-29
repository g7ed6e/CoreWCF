// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CoreWCF.Aspire.Explorer.Model;
using CoreWCF.Aspire.Explorer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CoreWCF.Aspire.Explorer.Tests;

public class WsdlExplorerServiceTests : IClassFixture<EchoServiceFixture>
{
    private readonly EchoServiceFixture _fixture;

    public WsdlExplorerServiceTests(EchoServiceFixture fixture) => _fixture = fixture;

    private CoreWcfServiceDescriptor Descriptor => new()
    {
        Name = "Echo",
        Url = _fixture.BaseAddress,
        Path = "/echo",
    };

    [Fact]
    public async Task LoadAsync_ParsesContractAndOperations()
    {
        using var http = new HttpClient();
        var service = new WsdlExplorerService(http, NullLogger<WsdlExplorerService>.Instance);

        var model = await service.LoadAsync(Descriptor);

        var contract = Assert.Single(model.Contracts, c => c.Name == "IEchoService");
        var echo = Assert.Single(contract.Operations, o => o.Name == "Echo");

        Assert.Contains("IEchoService/Echo", echo.SoapAction);
        Assert.Equal(SoapVersion.Soap11, echo.SoapVersion);
        Assert.Contains("Envelope", echo.SampleRequestEnvelope);
        Assert.Contains("Echo", echo.SampleRequestEnvelope);
    }

    [Fact]
    public async Task LoadAsync_ExtractsSimpleParameters()
    {
        using var http = new HttpClient();
        var service = new WsdlExplorerService(http, NullLogger<WsdlExplorerService>.Instance);

        var model = await service.LoadAsync(Descriptor);
        var echo = model.Contracts.SelectMany(c => c.Operations).Single(o => o.Name == "Echo");

        Assert.True(echo.CanUseFormattedRequest);
        var parameter = Assert.Single(echo.RequestParameters);
        Assert.Equal("text", parameter.Name);
        Assert.Equal("string", parameter.TypeName);
        Assert.True(parameter.IsSimple);
    }

    [Fact]
    public async Task Invoke_UsingFormattedParameters_RoundTrips()
    {
        using var http = new HttpClient();
        var explorer = new WsdlExplorerService(http, NullLogger<WsdlExplorerService>.Instance);
        var model = await explorer.LoadAsync(Descriptor);
        var echo = model.Contracts.SelectMany(c => c.Operations).Single(o => o.Name == "Echo");

        echo.RequestParameters.Single().Value = "Bonjour";
        var envelope = SoapRequestBuilder.BuildEnvelope(echo);

        using var invokeHttp = new HttpClient();
        var invoker = new SoapInvoker(invokeHttp);
        var result = await invoker.InvokeAsync(Descriptor.EndpointAddress, echo, envelope);

        Assert.True(result.IsSuccess, $"Expected success but got {result.StatusCode}: {result.Body}");
        Assert.Contains("You said: Bonjour", result.Body);

        var parsed = SoapResponseParser.Parse(result.Body);
        Assert.False(parsed.IsFault);
        Assert.Contains(parsed.Rows, r => r.Value.Contains("You said: Bonjour"));
    }

    [Fact]
    public async Task Invoke_RoundTripsThroughTheService()
    {
        using var http = new HttpClient();
        var explorer = new WsdlExplorerService(http, NullLogger<WsdlExplorerService>.Instance);
        var model = await explorer.LoadAsync(Descriptor);
        var echo = model.Contracts.SelectMany(c => c.Operations).Single(o => o.Name == "Echo");

        using var invokeHttp = new HttpClient();
        var invoker = new SoapInvoker(invokeHttp);

        var result = await invoker.InvokeAsync(Descriptor.EndpointAddress, echo, echo.SampleRequestEnvelope);

        Assert.True(result.IsSuccess, $"Expected success but got {result.StatusCode}: {result.Body}");
        Assert.Contains("You said:", result.Body);
    }
}
