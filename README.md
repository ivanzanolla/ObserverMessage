# ObserverMessage

A lightweight **in-process event bus** (publish/subscribe) for .NET, built on the Observer
pattern and TPL Dataflow. Targets netstandard2.0 and .NET 10.

Handlers run on the thread pool, never on the publisher's thread. Each subscription has its
own queue, so it receives messages one at a time and in publishing order, and a slow handler
does not hold up the others.

It works inside a single process and keeps messages in memory only: it is not a message
broker like RabbitMQ or Kafka.

## Install

```bash
dotnet add package ObserverMessage
```

## 1. Register the bus

The bus is meant to be **one per application**: register it once as a singleton and inject
`IMessageService` wherever you need it.

```csharp
// Program.cs
builder.Services.AddObserverMessage();

// or with options
builder.Services.AddObserverMessage(o => o.BoundedCapacity = 1000);
```

The container disposes the bus on shutdown, which stops every subscription.

## 2. Define the messages

A message is a class that derives from `BaseMessage` and passes its `SystemName`.

```csharp
using Messages;

public class OrderPlaced : BaseMessage
{
    public OrderPlaced() : base(nameof(OrderPlaced)) { }

    public int OrderId { get; init; }
    public decimal Total { get; init; }
}
```

## 3. Publish

```csharp
using Messages.Interfaces;

public class OrderService(IMessageService bus)
{
    public async Task PlaceAsync(Order order)
    {
        // ... save the order

        await bus.PublishAsync(new OrderPlaced { OrderId = order.Id, Total = order.Total });
    }
}
```

- `Publish` queues the message and returns at once.
- `PublishAsync` does the same, but when a subscription's queue is full (see
  [Bounded queues](#bounded-queues-backpressure)) it waits for space instead of dropping.

Publishing returns as soon as the message is queued: it does not wait for the handlers.

## 4. Subscribe

```csharp
// Route by type: the handler receives the concrete message, no casts.
int id = bus.Subscribe<OrderPlaced>(m => Console.WriteLine($"Order {m.OrderId}"));

// Async handler.
bus.Subscribe<OrderPlaced>(async m => await SendConfirmationAsync(m.OrderId));

// Async handler that receives a token, cancelled when this subscription stops.
bus.Subscribe<OrderPlaced>(async (m, ct) => await SendConfirmationAsync(m.OrderId, ct));

// Route by SystemName.
bus.Subscribe(m => Console.WriteLine($"Received {m.SystemName}"), nameof(OrderPlaced));

// Stop: messages still queued for this subscription are discarded.
bus.Unsubscribe(id);
```

### Subscribing in an application with DI

Write a handler class and register it. It can inject anything, including scoped services such as a `DbContext`:

```csharp
public class OrderEmailHandler(IEmailSender email) : IMessageHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced message, CancellationToken cancellationToken)
        => email.SendOrderConfirmationAsync(message.OrderId, cancellationToken);
}

// Program.cs
builder.Services.AddObserverMessage();
builder.Services.AddMessageHandler<OrderPlaced, OrderEmailHandler>();
```

Every message gets a new scope and a new handler instance, so scoped services are safe and are disposed
after each message. The `CancellationToken` is cancelled when the subscription stops, for example at
application shutdown. At shutdown, in-flight handlers may see already-disposed singletons; messages queued
after the container starts disposing are dropped silently. `UnsubscribeAll()` and `Dispose()` on the bus also
permanently remove the handler subscriptions registered through the container.

For a delegate subscription, subscribe from a long-lived component such as an `IHostedService` and
unsubscribe when it stops. The bus keeps a reference to every handler, so a short-lived object that
subscribes and never unsubscribes stays in memory. **Do not capture scoped services** in the delegate:
create a scope for each message instead.

```csharp
public class OrderEmailSubscriber(IMessageService bus, IServiceScopeFactory scopes) : IHostedService
{
    private int _subscriptionId;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscriptionId = bus.Subscribe<OrderPlaced>(async order =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            await email.SendOrderConfirmationAsync(order.OrderId);
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        bus.Unsubscribe(_subscriptionId);
        return Task.CompletedTask;
    }
}

builder.Services.AddHostedService<OrderEmailSubscriber>();
```

## Errors

A handler that throws does not stop its subscription: the exception is reported through
`HandlerError` (or written to `Trace` if nobody listens) and the next messages are delivered
as usual.

```csharp
public class BusErrorLogger(IMessageService bus, ILogger<BusErrorLogger> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        bus.HandlerError += OnError;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        bus.HandlerError -= OnError;
        return Task.CompletedTask;
    }

    private void OnError(object? sender, HandlerErrorEventArgs e) =>
        logger.LogError(e.Exception, "Handler {Handler} (subscription {Id}) failed on {Message}",
            e.HandlerType?.Name, e.SubscriptionId, e.Message.SystemName);
}
```

`HandlerType` is the handler class for subscriptions created by `AddMessageHandler`, and `null` for delegate and `IObserver` subscriptions (`MessageDropped` has it too).

## Bounded queues (backpressure)

Each subscription has its own queue, unbounded by default. If a handler is slower than the
publishers, its queue grows without limit and so does memory. `BoundedCapacity` sets the
maximum number of messages per subscription (the one being handled included):

```csharp
builder.Services.AddObserverMessage(o => o.BoundedCapacity = 1000);
```

When a subscription's queue is full:

- `Publish` drops the message for that subscription and raises `MessageDropped`;
- `await PublishAsync(...)` waits until there is space, slowing the publisher down.

```csharp
bus.MessageDropped += (_, e) =>
    logger.LogWarning("Queue full: subscription {Id} lost {Message}", e.SubscriptionId, e.Message.SystemName);
```

## Plain `IObserver<T>`

The bus is an `IObservable<IBaseMessage>`, so any `IObserver<IBaseMessage>` (for example
from System.Reactive) can subscribe to every message. Dispose the returned token to stop;
disposing the bus calls `OnCompleted`.

```csharp
IDisposable token = bus.Subscribe(myObserver);
// ...
token.Dispose();
```

## Without a DI container

For tests or small tools you can create the bus yourself. Keep a single instance and share it.

```csharp
using var bus = new MessageService(new MessageServiceOptions { BoundedCapacity = 100 });
```

## Repository

- `Messages/` — the library: `MessageService`, `MessageObservable` (the subject),
  `MessageObserver` (one per subscription, wraps an `ActionBlock`), `Unsubscriber`
  (the `IDisposable` token), `BaseMessage`, `AddObserverMessage()`
- `ObserverMessage/` — console demo: two subscribers, 1000 published messages
- `Messages.Tests/` — xUnit tests

```bash
dotnet run --project ObserverMessage
dotnet test
```

## Continuous integration and releases

The [CI workflow](.github/workflows/ci.yml) builds, tests and packs every pull request and
every push to `master`.

To publish a version on nuget.org, push a tag named after it:

```bash
git tag v0.1.0
git push origin v0.1.0
```

The tag sets the package version (`v0.1.0` → `0.1.0`); after build and tests pass, the package
is pushed to nuget.org. This needs a repository secret named `NUGET_API_KEY` holding an API
key created on nuget.org with push permission for the `ObserverMessage` package.

## License

Apache License 2.0 — see [LICENSE](LICENSE). If you use or redistribute this code, keep the [NOTICE](NOTICE) file with the attribution to the author.
