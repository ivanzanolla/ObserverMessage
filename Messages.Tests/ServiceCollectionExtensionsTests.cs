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

            var dropped = await PublishTwoWhileFirstIsBlocked(provider.GetRequiredService<IMessageService>());

            Assert.Equal(new[] { 2 }, dropped);
        }

        [Fact]
        public async Task Options_apply_regardless_of_registration_order()
        {
            var services = new ServiceCollection();
            services.AddObserverMessage();                          // stands in for AddMessageHandler's internal call
            services.AddObserverMessage(o => o.BoundedCapacity = 1);
            using var provider = services.BuildServiceProvider();

            var dropped = await PublishTwoWhileFirstIsBlocked(provider.GetRequiredService<IMessageService>());

            Assert.Equal(new[] { 2 }, dropped);
        }

        [Fact]
        public async Task Later_option_callbacks_override_earlier_ones()
        {
            var services = new ServiceCollection();
            services.AddObserverMessage(o => o.BoundedCapacity = 5);
            services.AddObserverMessage(o => o.BoundedCapacity = 1);
            using var provider = services.BuildServiceProvider();

            var dropped = await PublishTwoWhileFirstIsBlocked(provider.GetRequiredService<IMessageService>());

            Assert.Equal(new[] { 2 }, dropped);
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

        // Blocks a handler on message 1, publishes message 2 and returns the numbers of the dropped messages.
        private static async Task<int[]> PublishTwoWhileFirstIsBlocked(IMessageService bus)
        {
            var gate = new SemaphoreSlim(0);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var dropped = new ConcurrentBag<int>();
            bus.MessageDropped += (_, e) => dropped.Add(((Ping)e.Message).N);
            bus.Subscribe<Ping>(_ => { started.TrySetResult(true); gate.Wait(); });

            bus.Publish(new Ping(1));
            await started.Task.WithTimeout(TimeSpan.FromSeconds(5));
            bus.Publish(new Ping(2));

            var result = dropped.ToArray();
            gate.Release(10);
            return result;
        }
    }
}
