// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading.Tasks;
using Confluent.Kafka;
using Contracts;
using CoreWCF.Channels;
using CoreWCF.Configuration;
using CoreWCF.Description;
using CoreWCF.Dispatcher;
using CoreWCF.Kafka.Tests.Helpers;
using CoreWCF.Queue.Common.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace CoreWCF.Kafka.Tests;

public class CustomSerializationTests : IntegrationTest
{
    //private const string MessageTemplate =
    //    @"<s:Envelope xmlns:s=""http://www.w3.org/2003/05/soap-envelope"" xmlns:a=""http://www.w3.org/2005/08/addressing"">"
    //    + @"<s:Header><a:Action s:mustUnderstand=""1"">http://tempuri.org/ITestContract/Create</a:Action></s:Header>"
    //    + @"<s:Body><Create xmlns=""http://tempuri.org/""><name>{0}</name></Create></s:Body>"
    //    + @"</s:Envelope>";
    private const string PersonMessageTemplate = """
{
    "lastName": "Doe",
    "firstName": "John",
}
""";

    private const string CityMessageTemplate = """
{
    "name": "Paris",
    "country": "France"
}
""";

    public CustomSerializationTests(ITestOutputHelper output)
        : base(output)
    {

    }

    [LinuxWhenCIOnlyFact]
    public async Task KafkaProducerTest()
    {
        IWebHost host = ServiceHelper.CreateWebHostBuilder<Startup>(Output, ConsumerGroup, Topic).Build();
        Person person;
        City city;
        using (host)
        {
            await host.StartAsync();
            var resolver = new DependencyResolverHelper(host);
            var testService = resolver.GetService<CustomMessageFormatService>();
            testService.CountdownEvent.Reset(1);

            using var producer = new ProducerBuilder<Null, string>(new ProducerConfig
                {
                    BootstrapServers = "localhost:9092",
                    Acks = Acks.All
                })
                .SetKeySerializer(Serializers.Null)
                .SetValueSerializer(Serializers.Utf8)
                .Build();

            // string name = Guid.NewGuid().ToString();
            // string value = string.Format(MessageTemplate, name);
            var result = await producer.ProduceAsync(Topic, new Message<Null, string> { Value = PersonMessageTemplate });

            Assert.True(result.Status == PersistenceStatus.Persisted);
            Assert.Equal(0, producer.Flush(TimeSpan.FromSeconds(3)));

            Assert.True(testService.CountdownEvent.Wait(TimeSpan.FromSeconds(10)));
            person = testService.Person;
        }

        await AssertEx.RetryAsync(() => Assert.Equal(0, KafkaEx.GetConsumerLag(Output, ConsumerGroup, Topic)));

        Assert.Equal("Doe", person.LastName);
        Assert.Equal("John", person.FirstName);
    }

    //[LinuxWhenCIOnlyFact]
    // public async Task KafkaClientBindingTest()
    // {
    //     IWebHost host = ServiceHelper.CreateWebHostBuilder<Startup>(Output, ConsumerGroup, Topic).Build();
    //     using (host)
    //     {
    //         await host.StartAsync();
    //         var resolver = new DependencyResolverHelper(host);
    //         var testService = resolver.GetService<TestService>();
    //         testService.CountdownEvent.Reset(1);
    //
    //         CoreWCF.ServiceModel.Channels.KafkaBinding kafkaBinding = new();
    //         //System.ServiceModel.Channels.CustomBinding binding = new(kafkaBinding);
    //         //binding.Elements.Remove<MessageEncodingBindingElement>();
    //         //binding.Elements.Insert(0, new System.ServiceModel.Channels.ByteStreamMessageEncodingBindingElement());
    //         var factory = new System.ServiceModel.ChannelFactory<ITestContract>(kafkaBinding,
    //             new System.ServiceModel.EndpointAddress(new Uri($"net.kafka://localhost:9092/{Topic}")));
    //         factory.Endpoint.EndpointBehaviors.Add(new MyEndpointBehavior());
    //         ITestContract channel = factory.CreateChannel();
    //
    //
    //         string name = Guid.NewGuid().ToString();
    //         await channel.CreateAsync(name);
    //
    //         Assert.True(testService.CountdownEvent.Wait(TimeSpan.FromSeconds(10)));
    //         Assert.Contains(name, testService.Names);
    //     }
    //
    //     await AssertEx.RetryAsync(() =>Assert.Equal(0, KafkaEx.GetConsumerLag(Output, ConsumerGroup, Topic)));
    // }

    private class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<CustomMessageFormatService>();
            services.AddServiceModelServices();
            services.AddQueueTransport();
            services.AddSingleton<IServiceBehavior, MyServiceBehavior>();
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            app.UseServiceModel(services =>
            {
                var topicNameAccessor = app.ApplicationServices.GetService<TopicNameAccessor>();
                var consumerGroupAccessor = app.ApplicationServices.GetService<ConsumerGroupAccessor>();
                services.AddService<CustomMessageFormatService>();
                var kafkaBinding = new KafkaBinding
                {
                    AutoOffsetReset = AutoOffsetReset.Earliest,
                    DeliverySemantics = KafkaDeliverySemantics.AtMostOnce,
                    GroupId = consumerGroupAccessor.Invoke()
                };
                KafkaTransportBindingElement transport = kafkaBinding.CreateBindingElements().Find<KafkaTransportBindingElement>();
                MessageEncodingBindingElement encoding = new ByteStreamMessageEncodingBindingElement();
                CustomBinding binding = new(encoding, transport);
                services.AddServiceEndpoint<CustomMessageFormatService, ICustomMessageFormatService>(binding, $"net.kafka://localhost:9092/{topicNameAccessor.Invoke()}");
            });
        }
    }

    private class MyServiceBehavior : IServiceBehavior
    {
        public void Validate(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase)
        {

        }

        public void AddBindingParameters(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase,
            Collection<ServiceEndpoint> endpoints,
            BindingParameterCollection bindingParameters)
        {

        }

        public void ApplyDispatchBehavior(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase)
        {
            foreach (ServiceEndpoint endpoint in serviceDescription.Endpoints)
            {
                //endpoint.EndpointBehaviors.Add(new MyEndpointBehavior());
                foreach (OperationDescription operation in endpoint.Contract.Operations)
                {
                    operation.OperationBehaviors.Add(new MyDispatchBehavior());
                }
            }
        }
    }

    // private class MyEndpointBehavior : IEndpointBehavior
    // {
    //     private class MyAllowAllMessageFilter : MessageFilter
    //     {
    //         public override bool Match(Message message)
    //         {
    //             // var reader = message.GetReaderAtBodyContents();
    //             // var bytes = reader.ReadContentAsBase64();
    //             return true;
    //         }
    //
    //         public override bool Match(MessageBuffer buffer)
    //         {
    //             return true;
    //         }
    //     }
    //
    //     public void AddBindingParameters(ServiceEndpoint endpoint, BindingParameterCollection bindingParameters)
    //     {
    //
    //     }
    //
    //     public void ApplyClientBehavior(ServiceEndpoint endpoint, ClientRuntime clientRuntime)
    //     {
    //
    //     }
    //
    //     public void ApplyDispatchBehavior(ServiceEndpoint endpoint, EndpointDispatcher endpointDispatcher)
    //     {
    //         endpointDispatcher.AddressFilter = new MyAllowAllMessageFilter();
    //         endpointDispatcher.ContractFilter = new MyAllowAllMessageFilter();
    //     }
    //
    //     public void Validate(ServiceEndpoint endpoint)
    //     {
    //
    //     }
    // }

    private class MyDispatchBehavior : IOperationBehavior
    {
        public void AddBindingParameters(OperationDescription operationDescription,
            BindingParameterCollection bindingParameters)
        {

        }

        public void ApplyClientBehavior(OperationDescription operationDescription, ClientOperation clientOperation)
        {

        }

        public void ApplyDispatchBehavior(OperationDescription operationDescription,
            DispatchOperation dispatchOperation)
        {
            dispatchOperation.Formatter = new MyDispatchFormatter();
        }

        public void Validate(OperationDescription operationDescription)
        {

        }
    }

    private class MyDispatchFormatter : IDispatchMessageFormatter
    {
        public void DeserializeRequest(Message message, object[] parameters)
        {
            var reader = message.GetReaderAtBodyContents();
            var bytes = reader.ReadContentAsBase64();
            parameters[0] = JsonSerializer.Deserialize<Contracts.Person>(bytes);
        }

        public Message SerializeReply(MessageVersion messageVersion, object[] parameters, object result)
        {
            return null;
        }
    }
}
