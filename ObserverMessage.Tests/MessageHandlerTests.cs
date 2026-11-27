using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ObserverMessage.Tests
{
    public sealed class ScopeProbe : IDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    public sealed class AsyncOnlyResource : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return default;
        }
    }

    public sealed class HandlerLog
    {
        public ConcurrentQueue<(int N, Guid ScopeId, ScopeProbe Probe, AsyncOnlyResource Resource)> Entries { get; } =
            new ConcurrentQueue<(int, Guid, ScopeProbe, AsyncOnlyResource)>();

        public ConcurrentQueue<CancellationToken> Tokens { get; } = new ConcurrentQueue<CancellationToken>();

        public SemaphoreSlim Signal { get; } = new SemaphoreSlim(0);

        /// <summary>Released when a <see cref="BlockingHandler"/> invocation has ended.</summary>
        public SemaphoreSlim Finished { get; } = new SemaphoreSlim(0);

        public void Add(int n, ScopeProbe probe, AsyncOnlyResource resource)
        {
            Entries.Enqueue((n, probe.Id, probe, resource));
            Signal.Release();
        }
    }

    public sealed class RecordingHandler : IMessageHandler<Ping>
    {
        private readonly ScopeProbe _probe;
        private readonly AsyncOnlyResource _resource;
        private readonly HandlerLog _log;

        public RecordingHandler(ScopeProbe probe, AsyncOnlyResource resource, HandlerLog log)
        {
            _probe = probe;
            _resource = resource;
            _log = log;
        }

        public Task HandleAsync(Ping message, CancellationToken cancellationToken)
        {
            _log.Add(message.N, _probe, _resource);
            return Task.CompletedTask;
        }
    }

    public sealed class ThrowingHandler : IMessageHandler<Ping>
    {
        private readonly ScopeProbe _probe;
        private readonly AsyncOnlyResource _resource;
        private readonly HandlerLog _log;

        public ThrowingHandler(ScopeProbe probe, AsyncOnlyResource resource, HandlerLog log)
        {
            _probe = probe;
            _resource = resource;
            _log = log;
        }

        public Task HandleAsync(Ping message, CancellationToken cancellationToken)
        {
            if (message.N == 1) throw new InvalidOperationException("boom");
            _log.Add(message.N, _probe, _resource);
            return Task.CompletedTask;
        }
    }

    public interface IMissing
    {
    }

    public sealed class NeedsMissingService : IMessageHandler<Ping>
    {
        public NeedsMissingService(IMissing missing)
        {
        }

        public Task HandleAsync(Ping message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class BlockingHandler : IMessageHandler<Ping>
    {
        private readonly HandlerLog _log;

        public BlockingHandler(HandlerLog log) => _log = log;

        public async Task HandleAsync(Ping message, CancellationToken cancellationToken)
        {
            _log.Tokens.Enqueue(cancellationToken);
            _log.Signal.Release();
            try
            {
                await Task.Delay(-1, cancellationToken);
            }
            finally
            {
                _log.Finished.Release();
            }
        }
    }

    /// <summary>A singleton that publishes from its own Dispose, i.e. while the container is shutting down.</summary>
    public sealed class ShutdownPublisher : IDisposable
    {
        private readonly IMessageService _bus;

        public ShutdownPublisher(IMessageService bus) => _bus = bus;

        public void Dispose()
        {
            _bus.Publish(new Ping(1));
            // The bus is bounded to one message: the queue counts the invocation in progress, so this
            // completes only once message 1 has finished, i.e. it ran while the container was disposing.
            _bus.PublishAsync(new Ping(2)).WithTimeout(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
    }

    public sealed class MessageHandlerTests : IDisposable
    {
        private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

        private readonly List<IDisposable> _disposables = new List<IDisposable>();

        public void Dispose()
        {
            foreach (var disposable in _disposables) disposable.Dispose();
        }

        private (ServiceProvider Provider, IMessageService Bus, HandlerLog Log) Build(
            Action<IServiceCollection> addHandlers,
            Action<MessageServiceOptions> configure = null)
        {
            var services = new ServiceCollection()
                .AddObserverMessage(configure)
                .AddSingleton<HandlerLog>()
                .AddScoped<ScopeProbe>()
                .AddScoped<AsyncOnlyResource>();
            addHandlers(services);

            var provider = services.BuildServiceProvider();
            var log = provider.GetRequiredService<HandlerLog>();
            _disposables.Add(provider);
            _disposables.Add(log.Signal);
            _disposables.Add(log.Finished);
            return (provider, provider.GetRequiredService<IMessageService>(), log);
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + Limit;
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition was not met in time.");
                await Task.Delay(10);
            }
        }

        [Fact]
        public async Task Each_message_gets_its_own_scope()
        {
            var (_, bus, log) = Build(s => s.AddMessageHandler<Ping, RecordingHandler>());

            bus.Publish(new Ping(1));
            bus.Publish(new Ping(2));
            await log.Signal.WaitAsync().WithTimeout(Limit);
            await log.Signal.WaitAsync().WithTimeout(Limit);

            var scopeIds = log.Entries.Select(e => e.ScopeId).ToList();
            Assert.Equal(2, scopeIds.Count);
            Assert.NotEqual(scopeIds[0], scopeIds[1]);
        }

        [Fact]
        public async Task Scoped_dependency_is_disposed_after_each_message()
        {
            var (_, bus, log) = Build(s => s.AddMessageHandler<Ping, RecordingHandler>());

            bus.Publish(new Ping(1));
            await log.Signal.WaitAsync().WithTimeout(Limit);
            log.Entries.TryPeek(out var entry);
            await WaitUntil(() => entry.Probe.Disposed);

            Assert.True(entry.Probe.Disposed);
        }

        [Fact]
        public async Task Scoped_async_disposable_dependency_is_disposed_after_each_message()
        {
            var (_, bus, log) = Build(s => s.AddMessageHandler<Ping, RecordingHandler>());
            var errors = new ConcurrentQueue<HandlerErrorEventArgs>();
            bus.HandlerError += (_, e) => errors.Enqueue(e);

            bus.Publish(new Ping(1));
            await log.Signal.WaitAsync().WithTimeout(Limit);
            log.Entries.TryPeek(out var entry);
            await WaitUntil(() => entry.Resource.Disposed);

            Assert.True(entry.Resource.Disposed);
            Assert.Empty(errors);
        }

        [Fact]
        public async Task Handler_exception_raises_HandlerError_and_keeps_handling()
        {
            var (_, bus, log) = Build(s => s.AddMessageHandler<Ping, ThrowingHandler>());
            var errors = new ConcurrentQueue<HandlerErrorEventArgs>();
            bus.HandlerError += (_, e) => errors.Enqueue(e);

            bus.Publish(new Ping(1));
            bus.Publish(new Ping(2));
            await log.Signal.WaitAsync().WithTimeout(Limit);

            var error = Assert.Single(errors);
            Assert.Equal("boom", error.Exception.Message);
            Assert.Contains(log.Entries, e => e.N == 2);
        }

        [Fact]
        public async Task Unresolvable_handler_raises_HandlerError()
        {
            var (_, bus, _) = Build(s => s.AddMessageHandler<Ping, NeedsMissingService>());
            var raised = new TaskCompletionSource<HandlerErrorEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            bus.HandlerError += (_, e) => raised.TrySetResult(e);

            bus.Publish(new Ping(1));
            var error = await raised.Task.WithTimeout(Limit);

            Assert.IsType<InvalidOperationException>(error.Exception);
        }

        [Fact]
        public async Task Registering_same_handler_twice_runs_it_once()
        {
            var services = new ServiceCollection();
            services.AddMessageHandler<Ping, RecordingHandler>();
            services.AddMessageHandler<Ping, RecordingHandler>();
            Assert.Single(services, d =>
                d.ServiceType == typeof(MessageHandlerRegistration)
                && d.ImplementationInstance is MessageHandlerRegistration<Ping, RecordingHandler>);

            var (_, bus, log) = Build(s =>
            {
                s.AddMessageHandler<Ping, RecordingHandler>();
                s.AddMessageHandler<Ping, RecordingHandler>();
            });

            bus.Publish(new Ping(1));
            await log.Signal.WaitAsync().WithTimeout(Limit);
            await Task.Delay(200);

            Assert.Single(log.Entries);
        }

        [Fact]
        public async Task Handler_only_receives_its_message_type()
        {
            var (_, bus, log) = Build(s => s.AddMessageHandler<Ping, RecordingHandler>());

            var errors = new ConcurrentQueue<HandlerErrorEventArgs>();
            bus.HandlerError += (_, e) => errors.Enqueue(e);

            bus.Publish(new Pong());
            bus.Publish(new Ping(3));
            await log.Signal.WaitAsync().WithTimeout(Limit);

            // Messages are processed in order, so a Pong that got through the filter would
            // already have failed (InvalidCastException into HandlerError) before Ping(3) ran.
            Assert.Empty(errors);
            Assert.Equal(new[] { 3 }, log.Entries.Select(e => e.N));
        }

        [Fact]
        public async Task Disposing_the_container_cancels_running_handlers()
        {
            var (provider, bus, log) = Build(s => s.AddMessageHandler<Ping, BlockingHandler>());
            var errors = new ConcurrentQueue<HandlerErrorEventArgs>();
            bus.HandlerError += (_, e) => errors.Enqueue(e);

            bus.Publish(new Ping(1));
            await log.Signal.WaitAsync().WithTimeout(Limit);
            log.Tokens.TryPeek(out var token);

            provider.Dispose();
            await log.Finished.WaitAsync().WithTimeout(Limit);
            await Task.Delay(200); // let the bus finish the cancelled invocation

            Assert.True(token.IsCancellationRequested);
            Assert.Empty(errors); // cancellation at shutdown is not a handler failure
        }

        [Fact]
        public void Messages_queued_while_the_container_is_disposing_are_dropped_silently()
        {
            var (provider, bus, log) = Build(s =>
            {
                s.AddMessageHandler<Ping, RecordingHandler>();
                s.AddSingleton<ShutdownPublisher>();
            }, o => o.BoundedCapacity = 1);
            var errors = new ConcurrentQueue<HandlerErrorEventArgs>();
            bus.HandlerError += (_, e) => errors.Enqueue(e);
            // Resolved after the bus, so the container disposes it first, while the bus still runs.
            provider.GetRequiredService<ShutdownPublisher>();

            provider.Dispose(); // ShutdownPublisher.Dispose publishes and waits until the queued invocation has finished

            Assert.Empty(errors); // a container that is gone is a clean stop, not a handler failure
            Assert.Empty(log.Entries);
        }

        [Fact]
        public async Task Token_aware_Subscribe_cancels_on_Unsubscribe()
        {
            using var bus = new MessageService();
            var recorded = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var errors = new ConcurrentQueue<HandlerErrorEventArgs>();
            bus.HandlerError += (_, e) => errors.Enqueue(e);
            var id = bus.Subscribe<Ping>(async (_, ct) =>
            {
                recorded.TrySetResult(ct);
                try
                {
                    await Task.Delay(-1, ct);
                }
                finally
                {
                    finished.TrySetResult(true);
                }
            });

            bus.Publish(new Ping(1));
            var token = await recorded.Task.WithTimeout(Limit);

            Assert.True(bus.Unsubscribe(id));
            await finished.Task.WithTimeout(Limit);
            await Task.Delay(200); // let the bus finish the cancelled invocation

            Assert.True(token.IsCancellationRequested);
            Assert.Empty(errors); // cancellation on Unsubscribe is not a handler failure
        }
    }
}
