// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using CoreWCF.Configuration;
using Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace CoreWCF.Http.Tests
{
    public class ServiceWithCtorDepsTests
    {
        private readonly ITestOutputHelper _output;

        internal interface ICtorDeps { }
        internal class CtorDeps : ICtorDeps { }

        [ServiceContract(SessionMode = SessionMode.Allowed)]
        [System.ServiceModel.ServiceContract(SessionMode = System.ServiceModel.SessionMode.Allowed)]
        
        internal interface IServiceWithCtorDeps
        {
            [OperationContract]
            [System.ServiceModel.OperationContract]
            int GetCallCount();
        }

        internal class ServiceWithCtorDeps : IServiceWithCtorDeps
        {
            private readonly ICtorDeps _ctorDeps;
            private int _callCount = 0;
         
            public ServiceWithCtorDeps(ICtorDeps ctorDeps)
            {
                _ctorDeps = ctorDeps;
            }

            public int GetCallCount()
            {
                _callCount++;
                return _callCount;
            }
        }

        [CoreWCF.ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
        internal class SingleServiceWithCtorDeps : ServiceWithCtorDeps
        {
            public SingleServiceWithCtorDeps(ICtorDeps ctorDeps) : base(ctorDeps)
            {
            }
        }

        [CoreWCF.ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession)]
        internal class PerSessionServiceWithCtorDeps : ServiceWithCtorDeps
        {
            public PerSessionServiceWithCtorDeps(ICtorDeps ctorDeps) : base(ctorDeps)
            {
            }
        }

        [CoreWCF.ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall)]
        internal class PerCallServiceWithCtorDeps : ServiceWithCtorDeps
        {
            public PerCallServiceWithCtorDeps(ICtorDeps ctorDeps) : base(ctorDeps)
            {
            }
        }

        internal abstract class Startup<TService> where TService : ServiceWithCtorDeps
        {
            public void ConfigureServices(IServiceCollection services)
            {
                services.AddServiceModelServices();
                OnRegisterService(services);
            }

            public void Configure(IApplicationBuilder app, IHostingEnvironment env)
            {
                app.UseServiceModel(builder =>
                {
                    builder.AddService<TService>();
                    builder.AddServiceEndpoint<TService, IServiceWithCtorDeps>(new BasicHttpBinding(), $"/BasicWcfService/{GetType().Name}.svc");
                });
            }

            protected abstract void OnRegisterService(IServiceCollection services);
        }

        internal abstract class StartupSingleton<TService> : Startup<TService> where TService : ServiceWithCtorDeps
        {
            protected override void OnRegisterService(IServiceCollection services)
            {
                services.AddSingleton<ICtorDeps, CtorDeps>();
                services.AddSingleton(OnProvideInstance);
            }

            protected abstract TService OnProvideInstance(IServiceProvider provider);
        }

        internal class StartupSingletonWithSingleInstance : StartupSingleton<SingleServiceWithCtorDeps>
        {
            protected override SingleServiceWithCtorDeps OnProvideInstance(IServiceProvider provider) => new SingleServiceWithCtorDeps(new CtorDeps());
        }

        //internal class StartupSingletonWithPerSessionInstance : StartupSingleton<PerSessionServiceWithCtorDeps>
        //{
        //    protected override PerSessionServiceWithCtorDeps OnProvideInstance(IServiceProvider provider) => new PerSessionServiceWithCtorDeps(new CtorDeps());
        //}

        internal class StartupSingletonWithPerCallMode : StartupSingleton<PerCallServiceWithCtorDeps>
        {
            protected override PerCallServiceWithCtorDeps OnProvideInstance(IServiceProvider provider) => new PerCallServiceWithCtorDeps(new CtorDeps());
        }

        internal abstract class StartupScoped<TService> : Startup<TService> where TService : ServiceWithCtorDeps
        {
            protected override void OnRegisterService(IServiceCollection services)
            {
                services.AddScoped<ICtorDeps, CtorDeps>();
                services.AddScoped(OnProvideInstance);
            }

            protected abstract TService OnProvideInstance(IServiceProvider provider);
        }

        internal class StartupScopedWithSingleMode : StartupScoped<SingleServiceWithCtorDeps>
        {
            protected override SingleServiceWithCtorDeps OnProvideInstance(IServiceProvider provider) => new SingleServiceWithCtorDeps(new CtorDeps());
        }

        //internal class StartupScopedWithPerSessionMode : StartupScoped<PerSessionServiceWithCtorDeps>
        //{
        //    protected override PerSessionServiceWithCtorDeps OnProvideInstance(IServiceProvider provider) => new PerSessionServiceWithCtorDeps(new CtorDeps());
        //}

        internal class StartupScopedWithPerCallMode : StartupScoped<PerCallServiceWithCtorDeps>
        {
            protected override PerCallServiceWithCtorDeps OnProvideInstance(IServiceProvider provider) => new PerCallServiceWithCtorDeps(new CtorDeps());
        }

        internal abstract class StartupTransient<TService> : Startup<TService> where TService : ServiceWithCtorDeps
        {
            protected override void OnRegisterService(IServiceCollection services)
            {
                services.AddScoped<ICtorDeps, CtorDeps>();
                services.AddTransient(OnProvideInstance);
            }

            protected abstract TService OnProvideInstance(IServiceProvider provider);
        }

        internal class StartupTransientWithSingleMode : StartupTransient<SingleServiceWithCtorDeps>
        {
            protected override SingleServiceWithCtorDeps OnProvideInstance(IServiceProvider provider) => new SingleServiceWithCtorDeps(new CtorDeps());
        }

        internal class StartupTransientWithPerSessionMode : StartupTransient<PerSessionServiceWithCtorDeps>
        {
            protected override PerSessionServiceWithCtorDeps OnProvideInstance(IServiceProvider provider) => new PerSessionServiceWithCtorDeps(new CtorDeps());
        }

        internal class StartupTransientWithPerCallMode : StartupTransient<PerCallServiceWithCtorDeps>
        {
            protected override PerCallServiceWithCtorDeps OnProvideInstance(IServiceProvider provider) => new PerCallServiceWithCtorDeps(new CtorDeps());
        }

        public ServiceWithCtorDepsTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public static IEnumerable<object[]> GetSingletonTestsVariations()
        {
            yield return new object[] { typeof(StartupSingletonWithPerCallMode), 1, 2, 3 }; // Singleton + PerCall => Behaves as Single
            // yield return new object[] { typeof(StartupSingletonWithPerSessionInstance), 1, 2, 1 };
            yield return new object[] { typeof(StartupSingletonWithSingleInstance), 1, 2, 3 }; // Singleton + Single => Behaves as Single
        }

        [Theory]
        [MemberData(nameof(GetSingletonTestsVariations))]
        public void SingletonLifetimeOverridesInstanceContextModeTests(Type startupType, int a, int b, int c)
        {
            using var host = ServiceHelper
                .CreateWebHostBuilder(_output, startupType)
                .Build();

            host.Start();
            System.ServiceModel.BasicHttpBinding binding = ClientHelper.GetBufferedModeBinding();
            var endpointUri = new Uri($"http://localhost:8080/BasicWcfService/{startupType.Name}.svc");
            var factory = new System.ServiceModel.ChannelFactory<IServiceWithCtorDeps>(binding,
                new System.ServiceModel.EndpointAddress(endpointUri));

            var client1 = factory.CreateChannel();
            int actualA = client1.GetCallCount();
            int actualB = client1.GetCallCount();

            var client2 = factory.CreateChannel();
            int actualC = client2.GetCallCount();

            Assert.Equal(a, actualA);
            Assert.Equal(b, actualB);
            Assert.Equal(c, actualC);
        }

        public static IEnumerable<object[]> GetTransientTestsVariations()
        {
            yield return new object[] { typeof(StartupTransientWithPerCallMode), 1, 1, 1 };
            yield return new object[] { typeof(StartupTransientWithPerSessionMode), 1, 1, 1 };// Core
            yield return new object[] { typeof(StartupTransientWithSingleMode), 1, 2, 3 };
        }

        [Theory]
        [MemberData(nameof(GetTransientTestsVariations))]
        public void TransientLefetimeTests(Type startupType, int a, int b, int c)
        {
            using var host = ServiceHelper
                .CreateWebHostBuilder(_output, startupType)
                .Build();

            host.Start();
            System.ServiceModel.BasicHttpBinding binding = ClientHelper.GetBufferedModeBinding();
            var endpointUri = new Uri($"http://localhost:8080/BasicWcfService/{startupType.Name}.svc");
            var factory = new System.ServiceModel.ChannelFactory<IServiceWithCtorDeps>(binding,
                new System.ServiceModel.EndpointAddress(endpointUri));

            var client1 = factory.CreateChannel();
            int actualA = client1.GetCallCount();
            int actualB = client1.GetCallCount();

            var client2 = factory.CreateChannel();
            int actualC = client2.GetCallCount();

            Assert.Equal(a, actualA);
            Assert.Equal(b, actualB);
            Assert.Equal(c, actualC);
        }

        public static IEnumerable<object[]> GetScopedTestsVariations()
        {
            yield return new object[] { typeof(StartupScopedWithPerCallMode), 1, 2, 3 };// Scoped + PerCall => Single 1, 2, 3 => Behaves as Single.
            // yield return new object[] { typeof(StartupScopedWithPerSessionMode), 1, 2, 1 };// Scoped + PerSession => Single 1, 2, 3
            yield return new object[] { typeof(StartupScopedWithSingleMode), 1, 2, 3 };// Scoped + Single => Single 1, 2, 3
        }

        [Theory]
        [MemberData(nameof(GetScopedTestsVariations))]
        public void ScopedLifetimeTests(Type startupType, int a, int b, int c)
        {

            using var host = ServiceHelper
                .CreateWebHostBuilder(_output, startupType)
                .Build();

            host.Start();
            System.ServiceModel.BasicHttpBinding binding = ClientHelper.GetBufferedModeBinding();
            var endpointUri = new Uri($"http://localhost:8080/BasicWcfService/{startupType.Name}.svc");
            var factory = new System.ServiceModel.ChannelFactory<IServiceWithCtorDeps>(binding,
                new System.ServiceModel.EndpointAddress(endpointUri));

            var client1 = factory.CreateChannel();

            int actualA = client1.GetCallCount();
            int actualB = client1.GetCallCount();

            var client2 = factory.CreateChannel();
            int actualC = client2.GetCallCount();

            Assert.Equal(a, actualA);
            Assert.Equal(b, actualB);
            Assert.Equal(c, actualC);
        }
    }
}
