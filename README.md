# ObserverMessage

A small in-memory **publish/subscribe message bus** in C# (.NET 8).

Subscribers register with a handler and a `SystemName` filter; the bus delivers only
matching messages, on a background thread pool — never on the publisher's thread.

## What's inside

- `Messages/` — the bus library: `MessageService` (singleton), observable, observer,
  unsubscribe tokens, `BaseMessage` base class
- `ObserverMessage/` — console demo: two subscribers, 1000 published messages

## Run it

```bash
dotnet run --project ObserverMessage
```

## How it works

Built on the **Observer pattern** with three pieces:

- `MessageObservable` keeps observers in a `ConcurrentDictionary` and publishes with
  `Parallel.ForEach`
- `MessageObserver` wraps each handler in an `ActionBlock` (TPL Dataflow), so delivery
  happens off the publishing thread
- `Unsubscriber` is an `IDisposable` token that removes an observer from the dictionary

Written as an experiment in decoupled, thread-safe event handling.