// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using CoreWCF.Extensions.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CoreWCF.Extensions.Configuration.Tests
{
    [ServiceContract]
    public interface IEchoService
    {
        [OperationContract]
        string Echo(string value);
    }

    [ServiceContract]
    public interface IInventoryService
    {
        [OperationContract]
        int Count(string sku);
    }

    public class EchoService : IEchoService
    {
        public string Echo(string value) => value;
    }

    public class ServiceModelConfigurationReaderTests
    {
        private const string ServiceTypeName = "CoreWCF.Extensions.Configuration.Tests.EchoService";
        private const string EchoContractName = "CoreWCF.Extensions.Configuration.Tests.IEchoService";
        private const string InventoryContractName = "CoreWCF.Extensions.Configuration.Tests.IInventoryService";

        private static IConfiguration Configure(Dictionary<string, string> data) =>
            new ConfigurationBuilder().AddInMemoryCollection(data).Build().GetSection("ServiceModel");

        [Fact]
        public void ReadsEndpointsAgainstNamedBindings()
        {
            IConfiguration section = Configure(new Dictionary<string, string>
            {
                ["ServiceModel:Bindings:internal:Type"] = "NetTcpBinding",
                ["ServiceModel:Bindings:internal:MaxReceivedMessageSize"] = "2097152",
                ["ServiceModel:Bindings:public:Type"] = "BasicHttpBinding",
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Contract"] = EchoContractName,
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Binding"] = "internal",
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Address"] = "net.tcp://localhost:8089/echo",
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:1:Contract"] = InventoryContractName,
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:1:Binding"] = "public",
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:1:Address"] = "http://localhost:8080/inventory",
            });

            IReadOnlyList<ServiceEndpointDefinition> endpoints =
                new ServiceModelConfigurationReader().ReadEndpoints(section);

            Assert.Equal(2, endpoints.Count);

            ServiceEndpointDefinition tcp = endpoints[0];
            Assert.Equal(typeof(EchoService), tcp.ServiceType);
            Assert.Equal(typeof(IEchoService), tcp.Contract);
            Assert.Equal(new Uri("net.tcp://localhost:8089/echo"), tcp.Address);
            var netTcp = Assert.IsType<NetTcpBinding>(tcp.Binding);
            Assert.Equal(2097152, netTcp.MaxReceivedMessageSize);
            Assert.Null(tcp.ListenUri);

            ServiceEndpointDefinition http = endpoints[1];
            Assert.Equal(typeof(IInventoryService), http.Contract);
            Assert.IsType<BasicHttpBinding>(http.Binding);
        }

        [Fact]
        public void NamedBinding_IsSharedByEveryEndpointReferencingIt()
        {
            IConfiguration section = Configure(new Dictionary<string, string>
            {
                ["ServiceModel:Bindings:shared:Type"] = "NetTcpBinding",
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Contract"] = EchoContractName,
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Binding"] = "shared",
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Address"] = "net.tcp://localhost:8089/a",
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:1:Contract"] = InventoryContractName,
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:1:Binding"] = "shared",
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:1:Address"] = "net.tcp://localhost:8089/b",
            });

            IReadOnlyList<ServiceEndpointDefinition> endpoints =
                new ServiceModelConfigurationReader().ReadEndpoints(section);

            Assert.Same(endpoints[0].Binding, endpoints[1].Binding);
            Assert.Equal("shared", endpoints[0].Binding.Name);
        }

        [Fact]
        public void InlineBinding_IsHydratedInPlace()
        {
            IConfiguration section = Configure(new Dictionary<string, string>
            {
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Contract"] = EchoContractName,
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Binding:Type"] = "NetTcpBinding",
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Binding:MaxBufferSize"] = "16384",
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Address"] = "net.tcp://localhost:8089/echo",
            });

            ServiceEndpointDefinition endpoint =
                new ServiceModelConfigurationReader().ReadEndpoints(section).Single();

            var binding = Assert.IsType<NetTcpBinding>(endpoint.Binding);
            Assert.Equal(16384, binding.MaxBufferSize);
        }

        [Fact]
        public void ListenUri_IsOptional()
        {
            IConfiguration section = Configure(new Dictionary<string, string>
            {
                ["ServiceModel:Bindings:internal:Type"] = "NetTcpBinding",
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Contract"] = EchoContractName,
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Binding"] = "internal",
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Address"] = "net.tcp://contoso/echo",
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:ListenUri"] = "net.tcp://localhost:8089/echo",
            });

            ServiceEndpointDefinition endpoint =
                new ServiceModelConfigurationReader().ReadEndpoints(section).Single();

            Assert.Equal(new Uri("net.tcp://contoso/echo"), endpoint.Address);
            Assert.Equal(new Uri("net.tcp://localhost:8089/echo"), endpoint.ListenUri);
        }

        [Fact]
        public void UnknownBindingName_IsReported()
        {
            IConfiguration section = Configure(new Dictionary<string, string>
            {
                ["ServiceModel:Bindings:internal:Type"] = "NetTcpBinding",
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Contract"] = EchoContractName,
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Binding"] = "intrenal",
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Address"] = "net.tcp://localhost:8089/echo",
            });

            BindingConfigurationException exception = Assert.Throws<BindingConfigurationException>(
                () => new ServiceModelConfigurationReader().ReadEndpoints(section));

            Assert.Contains("intrenal", exception.Message);
            Assert.Contains("Binding", exception.Message);
        }

        [Fact]
        public void UnknownContractType_IsReported()
        {
            IConfiguration section = Configure(new Dictionary<string, string>
            {
                ["ServiceModel:Bindings:internal:Type"] = "NetTcpBinding",
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Contract"] = "Contoso.INotHere",
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Binding"] = "internal",
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Address"] = "net.tcp://localhost:8089/echo",
            });

            BindingConfigurationException exception = Assert.Throws<BindingConfigurationException>(
                () => new ServiceModelConfigurationReader().ReadEndpoints(section));

            Assert.Contains("Contoso.INotHere", exception.Message);
        }

        [Fact]
        public void MissingAddress_IsReported()
        {
            IConfiguration section = Configure(new Dictionary<string, string>
            {
                ["ServiceModel:Bindings:internal:Type"] = "NetTcpBinding",
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Contract"] = EchoContractName,
                [$"ServiceModel:Services:{ServiceTypeName}:Endpoints:0:Binding"] = "internal",
            });

            BindingConfigurationException exception = Assert.Throws<BindingConfigurationException>(
                () => new ServiceModelConfigurationReader().ReadEndpoints(section));

            Assert.Contains("address is required", exception.Message);
        }

        [Fact]
        public void ServiceWithoutEndpoints_IsReported()
        {
            IConfiguration section = Configure(new Dictionary<string, string>
            {
                [$"ServiceModel:Services:{ServiceTypeName}:Comment"] = "oops",
            });

            BindingConfigurationException exception = Assert.Throws<BindingConfigurationException>(
                () => new ServiceModelConfigurationReader().ReadEndpoints(section));

            Assert.Contains("no endpoints", exception.Message);
        }
    }
}
