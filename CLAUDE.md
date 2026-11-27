# ObserverMessage — notes for Claude

In-process publish/subscribe event bus for .NET, published on nuget.org as `ObserverMessage`.
The README is the user guide; read it before changing public behavior.

## Layout

- `ObserverMessage/` — the library, namespace `ObserverMessage`, targets `netstandard2.0;net10.0`.
  `MessageService` is the public bus, `MessageObservable`/`MessageObserver` are internal
  (one `ActionBlock` per subscription), DI extensions live in `ServiceCollectionExtensions.cs`
  (namespace `Microsoft.Extensions.DependencyInjection`).
- `ObserverMessage.Tests/` — xUnit, `net10.0` (+ `net48` on Windows or with `-p:IncludeNetFramework=true`).
- `ObserverMessage.Demo/` — console demo, not packed.

## Build and test

```bash
dotnet build
DOTNET_ROLL_FORWARD=Major dotnet test
dotnet build ObserverMessage.Tests -p:IncludeNetFramework=true   # compile-check net48 on macOS
dotnet pack ObserverMessage -c Release -o artifacts
```

`DOTNET_ROLL_FORWARD=Major` is needed on this Mac: only the .NET 11 runtime is installed, the tests target net10.0.

## Code rules

- The library must compile for **netstandard2.0**: no `Task.WaitAsync`, no newer BCL APIs without a fallback.
  Tests use the `WithTimeout` helper instead of `WaitAsync`.
- Keep dependencies to `Microsoft.Extensions.DependencyInjection.Abstractions` and
  `System.Threading.Tasks.Dataflow`.
- A handler exception must never stop its subscription: it goes to `HandlerError`.
  Cancellation while stopping and `ObjectDisposedException` at container shutdown are not errors.
- Tests must be deterministic: to wait for something to happen, wait on a signal (`TaskCompletionSource`,
  `SemaphoreSlim`, `WithTimeout`); use a short `Task.Delay` only to check that something did *not* happen.
- Public API changes go in `CHANGELOG.md` under an `## [Unreleased]` section (Keep a Changelog, SemVer;
  0.x minor versions may break).

## Git and releases

- `master` is protected: work on a branch, open a PR, CI (`build`, `test-netframework`) must be green.
  Ivan merges PRs from GitHub; do not merge or push to `master` yourself.
- Release: date the CHANGELOG entry, merge, then Ivan pushes a tag `vX.Y.Z`. The tag sets the package
  version and the `publish` job pushes to nuget.org with Trusted Publishing (OIDC via `NuGet/login@v1`,
  secret `NUGET_USER`). There is no API key; never add one. Do not create or push tags unless asked.
- License Apache-2.0: keep `LICENSE` and `NOTICE` (attribution to Ivan Zanolla).
