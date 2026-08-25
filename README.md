# CommandPrompts

A Windows desktop application for defining, running, and persisting multiple command-prompt sessions. Each session captures a command, arguments, working directory, and environment variables; output is streamed live and run history is stored locally as JSON.

Built on .NET 10 and WPF. No test suite.

## Architecture

Two-project solution following Clean Architecture:

```
src/
  CommandPrompts.Core/    Platform-neutral domain and services
  CommandPrompts.App/     WPF presentation (MVVM)
```

**CommandPrompts.Core** has no WPF dependency (`net10.0`, `IsAotCompatible=true`):

- `Abstractions/` — `IProcessExecutor`, `ISessionRepository`. The App layer depends only on these interfaces.
- `Models/` — `Session`, `ExecutionRecord`, `OutputEntry`, `EnvironmentVariable`.
- `Services/` — implementations: `ProcessExecutor` (async process lifecycle via `TaskCompletionSource` + event-based exit, non-blocking line pumping with cancellation support), `SessionRepository` (file-per-session JSON persistence under `History/`, guarded by a `Lock`), `DebouncedAction` (UI-thread-safe save debouncing).

Dependency direction is strictly one-way: App → Core abstractions → implementations. Swapping the repository or executor requires no change to ViewModels or models.

**CommandPrompts.App** uses hand-rolled MVVM — a local `ObservableObject` base and `RelayCommand`, no external MVVM framework. The only NuGet dependency is Microsoft.Xaml.Behaviors.Wpf. Dependencies are injected through ViewModel constructors; there is no service locator or container.

## Engineering Constraints

All code targets the requirements in `resources/Project-Requirements.md`:

- Minimal CPU / memory / IO consumption
- Zero allocation where practical
- Reflection-free serialization (source-generated JSON)
- Thread safety
- Fully async, non-blocking execution

Concrete expressions of these constraints:

- **Source-generated JSON**: all serialization goes through `[JsonSerializable]` types in `Core/Json/AppJsonContext.cs`. New model types must be registered there. No reflection-based `JsonSerializer` calls.
- **AOT compatibility**: Core sets `IsAotCompatible`; avoid APIs requiring runtime code generation.
- **Async throughout**: process execution is event-driven with `CancellationToken` propagation; file IO is asynchronous.
- **Explicit locking**: shared state in services is protected by `System.Threading.Lock`, not legacy monitor patterns.

## Build

Requires the .NET 10 SDK.

```bash
dotnet build CommandPrompts.slnx
dotnet run --project src/CommandPrompts.App
```

The solution file is `.slnx` format. Code style is enforced at build time for the Core project (`EnforceCodeStyleInBuild`); both projects compile with `AnalysisLevel=latest-all` and nullable reference types enabled.

## Conventions

- Naming: camelCase private fields, PascalCase types; no underscores anywhere.
- Sealed classes by default; interfaces defined in Core before implementation.
- ViewModels are `sealed`, implement `IDisposable` when they own processes or timers.

## License

See [LICENSE](LICENSE).
