using Messages;
using Messages.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Messages.Tests
{
    public class Ping : BaseMessage
    {
        public const string Name = "Ping";
        public Ping(int n = 0) : base(Name) { N = n; }
        public int N { get; }
    }

    public class Pong : BaseMessage
    {
        public Pong() : base("Pong") { }
    }

    public class MessageServiceTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        [Fact]
        public async Task Handler_that_throws_keeps_receiving_and_error_is_reported()
        {
            using var bus = new MessageService();
            var errors = new ConcurrentBag<HandlerErrorEventArgs>();
            bus.HandlerError += (_, e) => errors.Add(e);

            var received = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            int id = bus.Subscribe<Ping>(p =>
            {
                if (p.N == 1) throw new InvalidOperationException("boom");
                received.TrySetResult(p.N);
            });

            bus.Publish(new Ping(1));
            bus.Publish(new Ping(2));

            Assert.Equal(2, await received.Task.WithTimeout(Timeout));
            var error = Assert.Single(errors);
            Assert.Equal(id, error.SubscriptionId);
            Assert.Equal("boom", error.Exception.Message);
        }

        [Fact]
        public async Task Concurrent_subscriptions_get_unique_ids_and_all_receive()
        {
            using var bus = new MessageService();
            const int count = 500;
            using var allReceived = new CountdownEvent(count);

            var ids = new ConcurrentBag<int>();
            Parallel.For(0, count, _ => ids.Add(bus.Subscribe(_ => allReceived.Signal(), Ping.Name)));

            Assert.Equal(count, ids.Distinct().Count());

            bus.Publish(new Ping());
            Assert.True(await Task.Run(() => allReceived.Wait(Timeout)));
        }

        [Fact]
        public async Task Unsubscribe_discards_queued_messages()
        {
            using var bus = new MessageService();
            var gate = new SemaphoreSlim(0);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;

            int id = bus.Subscribe<Ping>(_ =>
            {
                Interlocked.Increment(ref calls);
                started.TrySetResult(true);
                gate.Wait();
            });

            bus.Publish(new Ping(1));
            bus.Publish(new Ping(2));
            bus.Publish(new Ping(3));
            await started.Task.WithTimeout(Timeout);

            Assert.True(bus.Unsubscribe(id));
            Assert.False(bus.Unsubscribe(id));

            gate.Release(3);
            await Task.Delay(200);
            Assert.Equal(1, Volatile.Read(ref calls));
        }

        [Fact]
        public async Task UnsubscribeAll_stops_every_subscription()
        {
            using var bus = new MessageService();
            int calls = 0;
            int a = bus.Subscribe<Ping>(_ => Interlocked.Increment(ref calls));
            int b = bus.Subscribe(_ => Interlocked.Increment(ref calls), Ping.Name);

            bus.UnsubscribeAll();
            bus.Publish(new Ping());
            await Task.Delay(200);

            Assert.Equal(0, Volatile.Read(ref calls));
            Assert.False(bus.Unsubscribe(a));
            Assert.False(bus.Unsubscribe(b));
        }

        [Fact]
        public async Task Typed_subscription_receives_only_its_type()
        {
            using var bus = new MessageService();
            var errors = new ConcurrentBag<HandlerErrorEventArgs>();
            bus.HandlerError += (_, e) => errors.Add(e);
            var calls = new ConcurrentQueue<int>();
            var pings = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            bus.Subscribe<Ping>(p => { calls.Enqueue(p.N); pings.TrySetResult(true); });

            bus.Publish(new Pong());
            bus.Publish(new Ping(7));
            await pings.Task.WithTimeout(Timeout);
            await Task.Delay(100);

            Assert.Equal(new[] { 7 }, calls.ToArray());
            Assert.Empty(errors);
        }

        [Fact]
        public async Task SystemName_subscription_ignores_other_names()
        {
            using var bus = new MessageService();
            var received = new ConcurrentQueue<string>();
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            bus.Subscribe(m => { received.Enqueue(m.SystemName); done.TrySetResult(true); }, Ping.Name);

            bus.Publish(new Pong());
            bus.Publish(new Ping());
            await done.Task.WithTimeout(Timeout);
            await Task.Delay(100);

            Assert.Equal(new[] { Ping.Name }, received.ToArray());
        }

        [Fact]
        public async Task Messages_arrive_in_publishing_order()
        {
            using var bus = new MessageService();
            const int count = 1000;
            var received = new ConcurrentQueue<int>();
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            bus.Subscribe<Ping>(p =>
            {
                received.Enqueue(p.N);
                if (p.N == count - 1) done.TrySetResult(true);
            });

            for (int i = 0; i < count; i++) bus.Publish(new Ping(i));
            await done.Task.WithTimeout(Timeout);

            Assert.Equal(Enumerable.Range(0, count), received);
        }

        [Fact]
        public async Task Async_handler_is_awaited()
        {
            using var bus = new MessageService();
            var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            bus.Subscribe<Ping>(async p =>
            {
                await Task.Delay(10);
                done.TrySetResult(p.N);
            });

            bus.Publish(new Ping(3));
            Assert.Equal(3, await done.Task.WithTimeout(Timeout));
        }

        [Fact]
        public async Task Publish_on_full_bounded_queue_raises_MessageDropped()
        {
            using var bus = new MessageService(new MessageServiceOptions { BoundedCapacity = 1 });
            var gate = new SemaphoreSlim(0);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var dropped = new ConcurrentBag<int>();
            bus.MessageDropped += (_, e) => dropped.Add(((Ping)e.Message).N);

            bus.Subscribe<Ping>(_ => { started.TrySetResult(true); gate.Wait(); });

            bus.Publish(new Ping(1));
            await started.Task.WithTimeout(Timeout);
            bus.Publish(new Ping(2));
            bus.Publish(new Ping(3));

            Assert.Equal(new[] { 2, 3 }, dropped.OrderBy(n => n));
            gate.Release(10);
        }

        [Fact]
        public async Task PublishAsync_waits_for_space_in_bounded_queue()
        {
            using var bus = new MessageService(new MessageServiceOptions { BoundedCapacity = 1 });
            var gate = new SemaphoreSlim(0);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bus.Subscribe<Ping>(_ => { started.TrySetResult(true); gate.Wait(); });

            await bus.PublishAsync(new Ping(1));
            await started.Task.WithTimeout(Timeout);

            var second = bus.PublishAsync(new Ping(2));
            await Task.Delay(100);
            Assert.False(second.IsCompleted);

            gate.Release();
            await second.WithTimeout(Timeout);
            gate.Release();
        }

        [Fact]
        public void BoundedCapacity_rejects_invalid_values()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MessageServiceOptions { BoundedCapacity = 0 });
        }

        [Fact]
        public async Task Plain_IObserver_receives_and_stops_on_dispose()
        {
            var bus = new MessageService();
            var observer = new RecordingObserver();

            var token = bus.Subscribe(observer);
            bus.Publish(new Ping(1));
            await observer.FirstMessage.Task.WithTimeout(Timeout);

            token.Dispose();
            bus.Publish(new Ping(2));
            await Task.Delay(100);
            Assert.Equal(1, observer.Count);
            Assert.False(observer.Completed);
        }

        [Fact]
        public void Disposing_the_service_completes_plain_observers()
        {
            var bus = new MessageService();
            var observer = new RecordingObserver();
            bus.Subscribe(observer);

            bus.Dispose();

            Assert.True(observer.Completed);
        }

        private sealed class RecordingObserver : IObserver<IBaseMessage>
        {
            private int _count;
            public int Count => Volatile.Read(ref _count);
            public bool Completed { get; private set; }
            public TaskCompletionSource<bool> FirstMessage { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public void OnNext(IBaseMessage value)
            {
                Interlocked.Increment(ref _count);
                FirstMessage.TrySetResult(true);
            }

            public void OnError(Exception error) { }
            public void OnCompleted() => Completed = true;
        }
    }
}
