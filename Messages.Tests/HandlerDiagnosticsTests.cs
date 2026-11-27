using Messages.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Messages.Tests
{
    public sealed class DiagnosticsProbe : IDisposable
    {
        public SemaphoreSlim Gate { get; } = new SemaphoreSlim(0);

        public TaskCompletionSource<bool> Started { get; } =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose() => Gate.Dispose();
    }

    public sealed class FailingDiagnosticsHandler : IMessageHandler<Ping>
    {
        public Task HandleAsync(Ping message, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }

    public sealed class BlockedDiagnosticsHandler : IMessageHandler<Ping>
    {
        private readonly DiagnosticsProbe _probe;

        public BlockedDiagnosticsHandler(DiagnosticsProbe probe) => _probe = probe;

        public async Task HandleAsync(Ping message, CancellationToken cancellationToken)
        {
            _probe.Started.TrySetResult(true);
            await _probe.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public class HandlerDiagnosticsTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        [Fact]
        public async Task Failing_DI_handler_reports_its_HandlerType()
        {
            var services = new ServiceCollection();
            services.AddMessageHandler<Ping, FailingDiagnosticsHandler>();
            using var provider = services.BuildServiceProvider();
            var bus = provider.GetRequiredService<IMessageService>();

            var error = new TaskCompletionSource<HandlerErrorEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            bus.HandlerError += (_, e) => error.TrySetResult(e);

            bus.Publish(new Ping(1));

            var args = await error.Task.WithTimeout(Timeout);
            Assert.Equal(typeof(FailingDiagnosticsHandler), args.HandlerType);
        }

        [Fact]
        public async Task Failing_delegate_subscription_reports_null_HandlerType()
        {
            using var bus = new MessageService();
            var error = new TaskCompletionSource<HandlerErrorEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            bus.HandlerError += (_, e) => error.TrySetResult(e);

            bus.Subscribe<Ping>(_ => throw new InvalidOperationException("boom"));
            bus.Publish(new Ping(1));

            var args = await error.Task.WithTimeout(Timeout);
            Assert.Null(args.HandlerType);
        }

        [Fact]
        public async Task Dropped_message_for_DI_handler_reports_its_HandlerType()
        {
            using var probe = new DiagnosticsProbe();
            var services = new ServiceCollection();
            services.AddSingleton(probe);
            services.AddObserverMessage(o => o.BoundedCapacity = 1);
            services.AddMessageHandler<Ping, BlockedDiagnosticsHandler>();
            using var provider = services.BuildServiceProvider();
            var bus = provider.GetRequiredService<IMessageService>();

            var dropped = new TaskCompletionSource<MessageDroppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            bus.MessageDropped += (_, e) => dropped.TrySetResult(e);

            bus.Publish(new Ping(1));
            await probe.Started.Task.WithTimeout(Timeout);
            bus.Publish(new Ping(2));

            var args = await dropped.Task.WithTimeout(Timeout);
            Assert.Equal(typeof(BlockedDiagnosticsHandler), args.HandlerType);
        }
    }
}
