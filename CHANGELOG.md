# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow
[Semantic Versioning](https://semver.org/). While the version is 0.x, minor versions may
contain breaking changes.

## [0.1.0]

First NuGet release. Compared with the original, unpackaged code (`.NET 8`, `MessageService.Instance`):

### Breaking changes

- Namespaces `Messages` and `Messages.Interfaces` are now a single `ObserverMessage`
  namespace, and the assembly is `ObserverMessage.dll`.
- `MessageService.Instance` removed: register the bus with `services.AddObserverMessage()`
  or create it with `new MessageService()`.
- `UnSuscribe(int)` / `UnSuscribe()` renamed to `Unsubscribe(int)` (now returns `bool`) and
  `UnsubscribeAll()`.
- The public `IMessageObserver<T>` interface was removed.
- `IMessageService` gained members (typed, async and token-aware `Subscribe` overloads,
  `PublishAsync`, `HandlerError`, `MessageDropped`); custom implementations must add them.

### Added

- Targets **netstandard2.0** and **net10.0**.
- `Subscribe<TMessage>(...)`: routing by message type, no casts in handlers.
- Async handlers (`Func<T, Task>`) and token-aware handlers (`Func<T, CancellationToken, Task>`);
  the token is cancelled when the subscription stops.
- `IMessageHandler<TMessage>` and `services.AddMessageHandler<TMessage, THandler>()`: handler
  classes resolved in a new DI scope for every message, so scoped services (such as a
  `DbContext`) are safe.
- `services.AddObserverMessage(o => ...)`, a singleton registration whose option callbacks
  accumulate in registration order.
- `MessageServiceOptions.BoundedCapacity`: per-subscription queue limit. `Publish` drops and
  raises `MessageDropped`; `PublishAsync` waits for space.
- `HandlerError` and `MessageDropped` events; their `HandlerType` names the handler class for
  subscriptions created by `AddMessageHandler`.
- `MessageService` is an `IObservable<IBaseMessage>`: any `IObserver<IBaseMessage>` can subscribe.

### Fixed

- A handler that threw stopped receiving messages without any error (its `ActionBlock`
  faulted). Errors are now reported through `HandlerError` and the subscription keeps working.
- Subscription ids could be duplicated under concurrent `Subscribe` calls.
- Unsubscribed tokens were never released; `Unsubscribe` is now O(1).
- `Unsubscribe` did not stop delivery of already-queued messages.
- Stopping a subscription or disposing the container no longer reports cancellation or
  `ObjectDisposedException` as handler errors.

[0.1.0]: https://github.com/ivanzanolla/ObserverMessage/releases/tag/v0.1.0
