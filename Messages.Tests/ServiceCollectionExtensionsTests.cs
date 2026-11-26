using Messages.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Messages.Tests
{
    public class ServiceCollectionExtensionsTests
    {
        [Fact]
        public void AddObserverMessage_registers_a_single_shared_bus()
        {
            var services = new ServiceCollection();
            services.AddObserverMessage();
            services.AddObserverMessage();

            Assert.Single(services, d => d.ServiceType == typeof(IMessageService));
            Assert.Equal(ServiceLifetime.Singleton, services.Single(d => d.ServiceType == typeof(IMessageService)).Lifetime);

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            Assert.Same(provider.GetRequiredService<IMessageService>(), scope.ServiceProvider.GetRequiredService<IMessageService>());
        }

        [Fact]
        public async Task AddObserverMessage_applies_options()
        {
            var services = new ServiceCollection();
            services.AddObserverMessage(o => o.BoundedCapacity = 1);
            using var provider = services.BuildServiceProvider();
            var bus = provider.GetRequiredService<IMessageService>();

            var gate = new SemaphoreSlim(0);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var dropped = new ConcurrentBag<int>();
            bus.MessageDropped += (_, e) => dropped.Add(((Ping)e.Message).N);
            bus.Subscribe<Ping>(_ => { started.TrySetResult(true); gate.Wait(); });

            bus.Publish(new Ping(1));
            await started.Task.WithTimeout(TimeSpan.FromSeconds(5));
            bus.Publish(new Ping(2));

            Assert.Equal(new[] { 2 }, dropped.ToArray());
            gate.Release(10);
        }

        [Fact]
        public void Disposing_the_container_stops_the_bus()
        {
            var services = new ServiceCollection();
            services.AddObserverMessage();
            var provider = services.BuildServiceProvider();
            var bus = provider.GetRequiredService<IMessageService>();
            int id = bus.Subscribe<Ping>(_ => { });

            provider.Dispose();

            Assert.False(bus.Unsubscribe(id));
        }
    }
}
