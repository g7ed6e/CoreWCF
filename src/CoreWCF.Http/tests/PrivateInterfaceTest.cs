// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ServiceModel;
using CoreWCF.Configuration;
using Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace BasicHttp
{
    public class PrivateInterfaceTest
    {
        private readonly ITestOutputHelper _output;

        public PrivateInterfaceTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void BasicHttpRequestReplyEchoString()
        {
            IWebHost host = ServiceHelper.CreateWebHostBuilder<Startup>(_output).Build();
            using (host)
            {
                host.Start();
                System.ServiceModel.BasicHttpBinding httpBinding = ClientHelper.GetBufferedModeBinding();
                var factory = new System.ServiceModel.ChannelFactory<IAzertyService>(httpBinding,
                    new System.ServiceModel.EndpointAddress(
                        new Uri($"http://localhost:{host.GetHttpPort()}/BasicWcfService/basichttp.svc")));
                IAzertyService channel = factory.CreateChannel();
                string result = channel.String();
                Assert.Equal("azerty", result);
            }
        }

        [ServiceContract]
        private interface IAzertyService
        {
            [OperationContract]
            string String();
        }

        private class AzertyService : IAzertyService
        {
            public string String() => "azerty";
        }

        internal class Startup
        {
            public void ConfigureServices(IServiceCollection services)
            {
                services.AddServiceModelServices();
            }

            public void Configure(IApplicationBuilder app)
            {
                app.UseServiceModel(builder =>
                {
                    builder.AddService<AzertyService>();
                    builder.AddServiceEndpoint<AzertyService, IAzertyService>(
                        new CoreWCF.BasicHttpBinding(), "/BasicWcfService/basichttp.svc");
                });
            }
        }
    }
}

