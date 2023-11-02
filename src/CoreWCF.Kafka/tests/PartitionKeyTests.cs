// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.ServiceModel.Channels;
using System.Threading.Tasks;
using Confluent.Kafka;
using Contracts;
using CoreWCF.Channels;
using CoreWCF.Configuration;
using CoreWCF.Kafka.Tests.Helpers;
using CoreWCF.Queue.Common.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using BindingContext = CoreWCF.Channels.BindingContext;
using BindingElement = CoreWCF.Channels.BindingElement;
using BufferManager = CoreWCF.Channels.BufferManager;
using CustomBinding = CoreWCF.Channels.CustomBinding;
using Message = CoreWCF.Channels.Message;
using MessageEncoder = CoreWCF.Channels.MessageEncoder;
using MessageEncoderFactory = CoreWCF.Channels.MessageEncoderFactory;
using MessageEncodingBindingElement = CoreWCF.Channels.MessageEncodingBindingElement;
using MessageVersion = CoreWCF.Channels.MessageVersion;
using TransportBindingElement = CoreWCF.Channels.TransportBindingElement;

namespace CoreWCF.Kafka.Tests;

public class PartitionKeyTests : IntegrationTest
{
    private const string MessageTemplate =
        @"<s:Envelope xmlns:s=""http://www.w3.org/2003/05/soap-envelope"" xmlns:a=""http://www.w3.org/2005/08/addressing"">"
        + @"<s:Header><a:Action s:mustUnderstand=""1"">http://tempuri.org/ITestContract/Create</a:Action></s:Header>"
        + @"<s:Body><Create xmlns=""http://tempuri.org/""><name>{0}</name></Create></s:Body>"
        + @"</s:Envelope>";

    public PartitionKeyTests(ITestOutputHelper output)
        : base(output)
    {

    }

    [LinuxWhenCIOnlyFact]
    public async Task KafkaProducerTest()
    {
        IWebHost host = ServiceHelper.CreateWebHostBuilder<Startup>(Output, ConsumerGroup, Topic).Build();
        using (host)
        {
            await host.StartAsync();
            var resolver = new DependencyResolverHelper(host);
            var testService = resolver.GetService<TestService>();
            testService.CountdownEvent.Reset(1);
            using var producer = new ProducerBuilder<string, string>(new ProducerConfig
                {
                    BootstrapServers = "localhost:9092",
                    Acks = Acks.All
                })
                .SetKeySerializer(Serializers.Utf8)
                .SetValueSerializer(Serializers.Utf8)
                .Build();

            string name = Guid.NewGuid().ToString();
            string partitionKey = Guid.NewGuid().ToString();
            string value = string.Format(MessageTemplate, name);
            var result = await producer.ProduceAsync(Topic, new Message<string, string>
            {
                Key = partitionKey,
                Value = value
            });

            Assert.True(result.Status == PersistenceStatus.Persisted);
            Assert.Equal(0, producer.Flush(TimeSpan.FromSeconds(3)));

            Assert.True(testService.CountdownEvent.Wait(TimeSpan.FromSeconds(10)));
            Assert.Contains(name, testService.Names);
        }

        await AssertEx.RetryAsync(() => Assert.Equal(0, KafkaEx.GetConsumerLag(Output, ConsumerGroup, Topic)));
    }

    [LinuxWhenCIOnlyFact]
    public async Task KafkaClientBindingTest()
    {
        IWebHost host = ServiceHelper.CreateWebHostBuilder<Startup>(Output, ConsumerGroup, Topic).Build();
        using (host)
        {
            await host.StartAsync();
            var resolver = new DependencyResolverHelper(host);
            var testService = resolver.GetService<TestService>();
            testService.CountdownEvent.Reset(1);

            ServiceModel.Channels.KafkaBinding kafkaBinding = new();
            var factory = new System.ServiceModel.ChannelFactory<ITestContract>(kafkaBinding,
                new System.ServiceModel.EndpointAddress(new Uri($"net.kafka://localhost:9092/{Topic}")));
            ITestContract channel = factory.CreateChannel();

            string name = Guid.NewGuid().ToString();
            await channel.CreateAsync(name);

            Assert.True(testService.CountdownEvent.Wait(TimeSpan.FromSeconds(10)));
            Assert.Contains(name, testService.Names);
        }

        await AssertEx.RetryAsync(() =>Assert.Equal(0, KafkaEx.GetConsumerLag(Output, ConsumerGroup, Topic)));
    }

    private class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<TestService>();
            services.AddServiceModelServices();
            services.AddQueueTransport();
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            app.UseServiceModel(services =>
            {
                var topicNameAccessor = app.ApplicationServices.GetService<TopicNameAccessor>();
                var consumerGroupAccessor = app.ApplicationServices.GetService<ConsumerGroupAccessor>();
                services.AddService<TestService>();
                KafkaBinding kafkaBinding = new KafkaBinding
                {
                    AutoOffsetReset = AutoOffsetReset.Earliest,
                    DeliverySemantics = KafkaDeliverySemantics.AtMostOnce,
                    GroupId = consumerGroupAccessor.Invoke()
                };
                var bindingElements = kafkaBinding.CreateBindingElements();
                var transport = bindingElements.Find<TransportBindingElement>();
                var innerEncoding = bindingElements.Find<MessageEncodingBindingElement>();
                var encoding = new MyMessageEncodingBindingElement(innerEncoding);
                var binding = new CustomBinding(encoding, transport);
                services.AddServiceEndpoint<TestService, ITestContract>(binding, $"net.kafka://localhost:9092/{topicNameAccessor.Invoke()}");
            });
        }
    }

    private class MyMessageEncodingBindingElement : MessageEncodingBindingElement
    {
        private readonly MessageEncodingBindingElement _inner;

        public MyMessageEncodingBindingElement(MessageEncodingBindingElement inner)
        {
            _inner = inner;
        }

        public override BindingElement Clone() => new MyMessageEncodingBindingElement(_inner);

        public override MessageVersion MessageVersion
        {
            get => _inner.MessageVersion;
            set => _inner.MessageVersion = value;
        }

        public override MessageEncoderFactory CreateMessageEncoderFactory()
        {
            return new MyMessageEncoderFactory(_inner.CreateMessageEncoderFactory());
        }
    }

    private class MyMessageEncoderFactory : MessageEncoderFactory
    {
        private readonly MessageEncoderFactory _inner;

        public MyMessageEncoderFactory(MessageEncoderFactory inner)
        {
            _inner = inner;
        }

        public override MessageEncoder Encoder => new MyMessageEncoder(_inner.Encoder);
        public override MessageVersion MessageVersion => _inner.MessageVersion;
    }

    private class MyMessageEncoder : MessageEncoder
    {
        private readonly MessageEncoder _inner;

        public MyMessageEncoder(MessageEncoder inner)
        {
            _inner = inner;
        }

        public override string ContentType => _inner.ContentType;
        public override string MediaType => _inner.MediaType;
        public override MessageVersion MessageVersion => _inner.MessageVersion;

        public override async Task<Message> ReadMessageAsync(Stream stream, int maxSizeOfHeaders, string contentType)
        {
            var message = await _inner.ReadMessageAsync(stream, maxSizeOfHeaders, contentType);
            // Assert.Contains(message.Properties, x => x.Key == "KafkaPartitionKey");
            return message;
        }

        public override Message ReadMessage(ArraySegment<byte> buffer, BufferManager bufferManager, string contentType)
        {
            return _inner.ReadMessage(buffer, bufferManager, contentType);
        }

        public override Task WriteMessageAsync(Message message, Stream stream)
        {
            return _inner.WriteMessageAsync(message, stream);
        }

        public override ArraySegment<byte> WriteMessage(Message message, int maxMessageSize,
            BufferManager bufferManager, int messageOffset)
        {
            return _inner.WriteMessage(message, maxMessageSize, bufferManager, messageOffset);
        }
    }
}
