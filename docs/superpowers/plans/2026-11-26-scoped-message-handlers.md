# Scoped Message Handlers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let applications that use dependency injection handle messages with handler classes that receive scoped services (a `DbContext`, for example) safely, without writing scope code themselves.

**Architecture:** A handler is a class implementing `IMessageHandler<TMessage>`, registered with `services.AddMessageHandler<TMessage, THandler>()`. When the container builds the bus, it creates one subscription per registered handler. For each message, the subscription opens a new async scope, resolves the handler from that scope, awaits `HandleAsync`, then disposes the scope. This is the same model as MediatR notification handlers and MassTransit consumers. Each handler keeps its own queue, so the existing guarantees (order, isolation, `HandlerError`, `BoundedCapacity`) apply unchanged. Handlers also receive a `CancellationToken` that is cancelled when the subscription stops (Unsubscribe or bus disposal at shutdown).

**Tech Stack:** C#, netstandard2.0 + net10.0, TPL Dataflow, Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2, xUnit.

**Spec:** No separate spec document. The requirements come from the design discussion of 2026-11-26 and are summarised in Goal, Architecture and Global Constraints.

## Global Constraints

- Library targets stay `netstandard2.0;net10.0`; no new package dependencies (`CreateAsyncScope` is already in DI.Abstractions 8.0.2 for netstandard2.0).
- Tests must compile for `net48` too: use `WithTimeout(...)` from `Messages.Tests/TaskTimeoutExtensions.cs`, never `Task.WaitAsync`.
- Existing public API keeps working: the current `Subscribe` overloads, `Publish`, `PublishAsync`, `HandlerError`, `MessageDropped` must not change behavior.
- Run tests with: `DOTNET_ROLL_FORWARD=Major dotnet test` (the machine has the .NET 11 runtime only). All 16 existing tests must stay green.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. A handler whose scoped dependency is `IAsyncDisposable` only (like EF Core's `DbContext` in async code) must be disposed after each message, not throw on scope disposal → test `Scoped_async_disposable_dependency_is_disposed_after_each_message` (Task 2).
2. A handler whose dependency is not registered must surface the failure through `HandlerError`, not fail silently → test `Unresolvable_handler_raises_HandlerError` (Task 2).
3. The same handler registered twice (for example by two modules) must run once per message → test `Registering_same_handler_twice_runs_it_once` (Task 2).
4. `AddMessageHandler` called before `AddObserverMessage(o => ...)` must not lose the options → test `Options_apply_regardless_of_registration_order` (Task 1).
5. Shutdown while a handler is running must cancel the handler's token → test `Disposing_the_container_cancels_running_handlers` (Task 2).

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `Messages/MessageObserver.cs` | Modify | Handler delegate becomes `Func<IBaseMessage, CancellationToken, Task>`; passes `_cts.Token` |
| `Messages/MessageObservable.cs` | Modify | `Add(...)` takes the token-aware delegate |
| `Messages/MessageService.cs` | Modify | Existing overloads adapt to the new delegate; new public token-aware overload |
| `Messages/Interfaces/IMessageService.cs` | Modify | Declares the token-aware overload |
| `Messages/Interfaces/IMessageHandler.cs` | Create | Public handler contract |
| `Messages/MessageHandlerRegistration.cs` | Create | Internal: one registration per handler type, subscribes it with a scope per message |
| `Messages/ServiceCollectionExtensions.cs` | Modify | `AddObserverMessage` accumulates option callbacks; new `AddMessageHandler` |
| `Messages.Tests/ServiceCollectionExtensionsTests.cs` | Modify | Option-ordering test |
| `Messages.Tests/MessageHandlerTests.cs` | Create | Handler behavior tests |
| `README.md` | Modify | Replaces the hosted-service example with handler classes |

---

### Task 1: Order-independent options

Today the second `AddObserverMessage(configure)` call is ignored (`TryAddSingleton`). Task 2 needs `AddMessageHandler` to call `AddObserverMessage()` itself, so option callbacks must accumulate instead.

**Files:**
- Modify: `Messages/ServiceCollectionExtensions.cs`
- Test: `Messages.Tests/ServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Produces: `AddObserverMessage(this IServiceCollection, Action<MessageServiceOptions> configure = null)`, same signature. Every call registers its callback; the bus factory applies all callbacks in registration order. `IMessageService` stays registered once.
- Produces (internal): `sealed class ObserverMessageConfigurator { public ObserverMessageConfigurator(Action<MessageServiceOptions> configure); public void Apply(MessageServiceOptions options); }`, registered as singleton instances. Task 2 relies on `AddObserverMessage()` being safe to call any number of times.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task Options_apply_regardless_of_registration_order()
{
    var services = new ServiceCollection();
    services.AddObserverMessage();                          // stands in for AddMessageHandler's internal call
    services.AddObserverMessage(o => o.BoundedCapacity = 1);
    using var provider = services.BuildServiceProvider();
    var bus = provider.GetRequiredService<IMessageService>();
    // Same body as AddObserverMessage_applies_options: one blocked handler,
    // publish 1 then 2 → MessageDropped reports exactly { 2 }.
}

[Fact]
public void Later_option_callbacks_override_earlier_ones()
{
    // AddObserverMessage(o => o.BoundedCapacity = 5) then (o => o.BoundedCapacity = 1)
    // → resolved bus drops the 2nd message while the 1st is blocked (capacity 1 wins).
}
```

Keep `AddObserverMessage_registers_a_single_shared_bus` unchanged: it must still pass.

- [ ] **Step 2: Run to verify they fail**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test --filter "FullyQualifiedName~ServiceCollectionExtensionsTests"`
Expected: `Options_apply_regardless_of_registration_order` FAILS (nothing dropped, since the second call's options are ignored).

- [ ] **Step 3: Implement**

`AddObserverMessage` adds `new ObserverMessageConfigurator(configure)` when `configure != null`, and `TryAddSingleton<IMessageService>(sp => ...)` builds options by applying `sp.GetServices<ObserverMessageConfigurator>()` in order.

- [ ] **Step 4: Run all tests**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test`
Expected: PASS, 18 tests.

- [ ] **Step 5: Commit**

```bash
git add Messages/ServiceCollectionExtensions.cs Messages.Tests/ServiceCollectionExtensionsTests.cs
git commit -m "Make AddObserverMessage option callbacks accumulate"
```

---

### Task 2: Handler classes resolved in a scope per message

**Files:**
- Create: `Messages/Interfaces/IMessageHandler.cs`, `Messages/MessageHandlerRegistration.cs`, `Messages.Tests/MessageHandlerTests.cs`
- Modify: `Messages/MessageObserver.cs`, `Messages/MessageObservable.cs`, `Messages/MessageService.cs`, `Messages/Interfaces/IMessageService.cs`, `Messages/ServiceCollectionExtensions.cs`, `README.md`

**Interfaces:**
- Consumes: `AddObserverMessage()` from Task 1 (safe to call repeatedly).
- Produces (public):
  - `namespace Messages.Interfaces { public interface IMessageHandler<in TMessage> where TMessage : IBaseMessage { Task HandleAsync(TMessage message, CancellationToken cancellationToken); } }`
  - `IMessageService.Subscribe<TMessage>(Func<TMessage, CancellationToken, Task> handler) where TMessage : IBaseMessage` returning `int`. The token is cancelled when that subscription stops.
  - `IServiceCollection AddMessageHandler<TMessage, THandler>(this IServiceCollection services) where TMessage : IBaseMessage where THandler : class, IMessageHandler<TMessage>`, in `ObserverMessageServiceCollectionExtensions`.
- Produces (internal):
  - `MessageObserver` / `MessageObservable.Add` take `Func<IBaseMessage, CancellationToken, Task>`; `MessageObserver.InvokeAsync` passes `_cts.Token`. Existing public overloads wrap their delegates and ignore the token.
  - `abstract class MessageHandlerRegistration { public abstract int SubscribeTo(IMessageService bus, IServiceScopeFactory scopes); }` and `sealed class MessageHandlerRegistration<TMessage, THandler> : MessageHandlerRegistration`.

- [ ] **Step 1: Write the failing tests** in `Messages.Tests/MessageHandlerTests.cs`

Test doubles in the same file:
- `ScopeProbe` (scoped): `Guid Id = Guid.NewGuid()`, `bool Disposed`, implements `IDisposable`.
- `AsyncOnlyResource` (scoped): implements `IAsyncDisposable` only, sets `Disposed` in `DisposeAsync`.
- `RecordingHandler : IMessageHandler<Ping>`: takes `ScopeProbe`, `AsyncOnlyResource` and a singleton `HandlerLog` (a `ConcurrentQueue<(int N, Guid ScopeId, ScopeProbe Probe, AsyncOnlyResource Resource)>` plus a `SemaphoreSlim` released for each entry).
- `ThrowingHandler : IMessageHandler<Ping>`: throws `InvalidOperationException("boom")` when `N == 1`, otherwise logs.
- `NeedsMissingService : IMessageHandler<Ping>`: constructor takes an unregistered `IMissing`.
- `BlockingHandler : IMessageHandler<Ping>`: records the token in `HandlerLog`, then `await Task.Delay(-1, token)` (use `-1`, not `Timeout.Infinite`: test classes declare a `Timeout` field that would shadow `System.Threading.Timeout`).

Each test builds `new ServiceCollection().AddObserverMessage().AddSingleton<HandlerLog>().AddScoped<ScopeProbe>().AddScoped<AsyncOnlyResource>()`, adds its handler, builds the provider and resolves `IMessageService`.

```csharp
[Fact] public async Task Each_message_gets_its_own_scope()
// RecordingHandler; publish Ping(1), Ping(2); wait for 2 log entries (WithTimeout 5 s)
// Assert: the two ScopeIds differ.

[Fact] public async Task Scoped_dependency_is_disposed_after_each_message()
// RecordingHandler; publish Ping(1); wait for the entry, then wait until Probe.Disposed is true
// (poll ≤ 5 s) → Assert.True(entry.Probe.Disposed).

[Fact] public async Task Scoped_async_disposable_dependency_is_disposed_after_each_message()
// as above, checks entry.Resource.Disposed and that HandlerError was never raised.

[Fact] public async Task Handler_exception_raises_HandlerError_and_keeps_handling()
// ThrowingHandler; publish Ping(1), Ping(2)
// Assert: one HandlerError with Exception.Message == "boom"; the log contains N == 2.

[Fact] public async Task Unresolvable_handler_raises_HandlerError()
// NeedsMissingService; publish Ping(1)
// Assert: HandlerError raised within 5 s with an InvalidOperationException.

[Fact] public async Task Registering_same_handler_twice_runs_it_once()
// AddMessageHandler<Ping, RecordingHandler>() twice; publish Ping(1); wait for the entry, then 200 ms
// Assert: exactly one log entry.

[Fact] public async Task Handler_only_receives_its_message_type()
// RecordingHandler; publish Pong then Ping(3); Assert: the log contains only N == 3.

[Fact] public async Task Disposing_the_container_cancels_running_handlers()
// BlockingHandler; publish Ping(1); wait until the token is recorded; provider.Dispose()
// Assert: recorded token.IsCancellationRequested == true.

[Fact] public async Task Token_aware_Subscribe_cancels_on_Unsubscribe()
// No DI: new MessageService(); id = Subscribe<Ping>(async (p, ct) => { record ct; await Task.Delay(-1, ct); })
// publish; wait for ct; Unsubscribe(id) → ct.IsCancellationRequested == true.
```

`Ping` and `Pong` already exist in `MessageServiceTests.cs`.

- [ ] **Step 2: Run to verify they fail**

Run: `DOTNET_ROLL_FORWARD=Major dotnet build Messages.Tests`
Expected: build FAILS: `IMessageHandler<>`, `AddMessageHandler` and the token-aware `Subscribe` do not exist.

- [ ] **Step 3: Thread the token through the internals**

Change `MessageObserver`'s handler type and `MessageObservable.Add` as listed in Interfaces. Wrap the existing `MessageService` overloads (`(m, _) => handler(m)`) and add the public `Subscribe<TMessage>(Func<TMessage, CancellationToken, Task>)` to `MessageService` and `IMessageService`. Run `DOTNET_ROLL_FORWARD=Major dotnet test --filter "FullyQualifiedName~MessageServiceTests"`: the 13 existing tests still pass.

- [ ] **Step 4: Add `IMessageHandler<TMessage>` and the registration**

`MessageHandlerRegistration<TMessage, THandler>.SubscribeTo` calls `bus.Subscribe<TMessage>(async (message, ct) => { ... })`, whose body:

```csharp
var scope = scopes.CreateAsyncScope();
await using (scope.ConfigureAwait(false))
{
    var handler = scope.ServiceProvider.GetRequiredService<THandler>();
    await handler.HandleAsync(message, ct).ConfigureAwait(false);
}
```

Use `CreateAsyncScope`, not `CreateScope`: a scope holding an `IAsyncDisposable`-only service throws on synchronous disposal (Review Focus 1). Resolution errors propagate to the existing `HandlerError` path (Review Focus 2).

`AddMessageHandler<TMessage, THandler>`:
1. calls `services.AddObserverMessage()`;
2. `services.TryAddScoped<THandler>()`;
3. skips the rest if a `MessageHandlerRegistration<TMessage, THandler>` descriptor already exists (Review Focus 3);
4. `services.AddSingleton<MessageHandlerRegistration>(new MessageHandlerRegistration<TMessage, THandler>())`.

In the `AddObserverMessage` factory, after creating the bus, call `SubscribeTo(bus, sp.GetRequiredService<IServiceScopeFactory>())` for every `sp.GetServices<MessageHandlerRegistration>()`.

- [ ] **Step 5: Run all tests**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test`
Expected: PASS, 27 tests.
Run: `dotnet build Messages.Tests -p:IncludeNetFramework=true`
Expected: 0 errors (net48 compiles; it runs in CI on Windows).

- [ ] **Step 6: Update `README.md`**

In "Subscribing in an application with DI", make handler classes the recommended way:

```csharp
public class OrderEmailHandler(IEmailSender email) : IMessageHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced message, CancellationToken cancellationToken)
        => email.SendOrderConfirmationAsync(message.OrderId, cancellationToken);
}

builder.Services.AddObserverMessage();
builder.Services.AddMessageHandler<OrderPlaced, OrderEmailHandler>();
```

Explain in two sentences: a new scope per message, so scoped services are safe and disposed after each message; the token is cancelled at shutdown. Keep the `IHostedService` example as the option for delegate subscriptions, shortened. Add the token-aware overload to "4. Subscribe".

- [ ] **Step 7: Commit**

```bash
git add Messages Messages.Tests README.md
git commit -m "Add scoped message handlers resolved per message"
```

---

## Out of scope (YAGNI)

- Assembly scanning (`AddMessageHandlersFromAssembly`): explicit registration is enough for now.
- Resolving the handler as `IEnumerable<IMessageHandler<T>>` in a single subscription: one subscription per handler keeps per-handler isolation and ordering.
- Retries, pipelines and middleware around handlers.
