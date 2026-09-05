# CommandPrompts

A Windows desktop application for defining, running, and persisting multiple command-prompt sessions. Each session captures a command, arguments, working directory, and environment variables; output is streamed live and run history is stored locally as JSON.

Built on .NET 10 and **Avalonia 12** (migrated from WPF). Includes a headless NUnit test suite.

## Architecture

Three-project solution following Clean Architecture:

```
src/
  CommandPrompts.Core/           Platform-neutral domain and services
  CommandPrompts.App/            WPF presentation (MVVM) — legacy, retained for reference
  CommandPrompts.App.Avalonia/   Avalonia 12 presentation (MVVM) — active
tests/
  CommandPrompts.App.Avalonia.Tests/  Headless NUnit tests (Avalonia.Headless)
```

### CommandPrompts.Core

No UI framework dependency (`net10.0`, `IsAotCompatible=true`):

- `Abstractions/` — `IProcessExecutor`, `ISessionRepository`. The App layer depends only on these interfaces.
- `Models/` — `Session`, `ExecutionRecord`, `OutputEntry`, `EnvironmentVariable`.
- `Services/` — implementations:
  - `ProcessExecutor` — async process lifecycle via `TaskCompletionSource` + event-based exit; returns the real process `ExitCode`, validates command/working-directory before start, observes cancellation and `Win32Exception` on terminate.
  - `SessionRepository` — file-per-session JSON persistence under `%LOCALAPPDATA%\CommandPrompts\History`; each save writes a uniquely-named temp file and holds the `ioLock` across serialize+write+move for atomic, collision-free replacement; implements `IDisposable`; skips corrupt JSON files at load time.
  - `DebouncedAction` — UI-thread-safe save debouncing with a `FlushAsync` for deterministic final writes.

Dependency direction is strictly one-way: App → Core abstractions → implementations.

### CommandPrompts.App.Avalonia

The active UI layer, using **Avalonia 12** with `CommunityToolkit.Mvvm` source generation:

- **ViewModels** — `MainViewModel` (session lifecycle, Dispatcher marshaling for UI-thread collection updates), `SessionViewModel` (run/terminate/clear/copy/multi-select), `OutputLineViewModel` (immutable, color-coded console output).
- **Views** — `MainWindow` (toolbar + horizontal WrapPanel session strip + detail panel), `SessionView` (config panel + virtualized console output with auto-scroll), `ConfirmDialog`.
- **Services** — `IDialogService`, `IClipboardService` (Avalonia 12 `DataTransfer` API), `IProcessLauncher` — injected via `AttachServices` after construction (no DI container).
- **Themes** — `AppTheme.axaml` (13-color dark console palette + brushes, merged via `ResourceInclude`), `AppStyles.axaml` (control styles, merged via `StyleInclude`).
- **Logging** — `AvaloniaTraceListener` bridges Avalonia `LogToTrace` → ZLogger rolling file under `%LOCALAPPDATA%\CommandPrompts\logs`.

### CommandPrompts.App

The original WPF implementation, retained for reference. Uses hand-rolled MVVM with a local `ObservableObject` base and `RelayCommand`. Not actively developed.

## Engineering Constraints

All code targets the requirements in `resources/Project-Requirements.md`:

- Minimal CPU / memory / IO consumption
- Zero allocation where practical
- Reflection-free serialization (source-generated JSON)
- Thread safety
- Fully async, non-blocking execution

Concrete expressions:

- **Source-generated JSON**: all serialization goes through `[JsonSerializable]` types in `Core/Json/AppJsonContext.cs`. New model types must be registered there. No reflection-based `JsonSerializer` calls.
- **AOT compatibility**: Core sets `IsAotCompatible`; avoid APIs requiring runtime code generation.
- **Async throughout**: process execution is event-driven with `CancellationToken` propagation; file IO is asynchronous.
- **Explicit locking**: shared state in services is protected by `System.Threading.Lock`, not legacy monitor patterns.
- **Compiled bindings**: Avalonia uses `x:DataType` on all `DataTemplate` roots and `UserControl`/`Window` roots. Wrong property paths fail at build time (AVLN2000), not runtime.

## Build & Run

Requires the .NET 10 SDK.

```bash
dotnet build CommandPrompts.slnx
dotnet run --project src/CommandPrompts.App.Avalonia
```

The solution file is `.slnx` format. Code style is enforced at build time for the Core project (`EnforceCodeStyleInBuild`); both projects compile with `AnalysisLevel=latest-all` and nullable reference types enabled.

### NuGet Dependencies

- **Core**: none beyond BCL.
- **Avalonia**: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `CommunityToolkit.Mvvm`, `ZLogger`.
- **Tests**: `Avalonia.Headless.NUnit`, `NUnit`.

## Tests

```bash
dotnet test tests/CommandPrompts.App.Avalonia.Tests
```

Uses `Avalonia.Headless.NUnit` with `PerTest` isolation. Tests are `async Task` awaiting `ExecuteAsync` for deterministic dispatcher pumping. Coverage: load persistence, view binding, run loop exit codes, history replay, idempotent loads.

## Conventions

- Naming: camelCase private fields, PascalCase types; no underscores anywhere.
- Sealed classes by default; interfaces defined in Core before implementation.
- ViewModels are `sealed`, implement `IDisposable` when they own processes or timers.
- MVVM via `CommunityToolkit.Mvvm` source generation: `[ObservableProperty]`, `[RelayCommand]`, `[NotifyPropertyChangedFor]`.
- DI without container: dependencies injected via constructor; view-layer services attached post-construction.
- JSON: source-generated serializer via `[JsonSerializable]` context in `Core/Json/AppJsonContext.cs`.
- Avalonia threading: `Dispatcher.UIThread.InvokeAsync` for UI-thread result marshaling; `Dispatcher.UIThread.Post` for fire-and-forget UI updates from background callbacks.
- Session history persists as JSON files in `%LOCALAPPDATA%\CommandPrompts\History` (one file per session, write-once temp file per save, atomic replace).

## Pitfalls

- Solution file is `.slnx` (new format) — pass it explicitly to `dotnet` commands; older tooling may not parse it.
- Never hand-edit `bin/`, `obj/`, or `History/*.json`.
- `resources/notes.md` is the user's scratch task list, not app documentation.
- Avalonia `<Style>` elements cannot live directly inside a merged `<ResourceDictionary>` — they require a separate `<Styles>` file included via `StyleInclude`.
- `WrapPanel` in `ItemsControl.ItemsPanel` does not virtualize — acceptable for small collections (sessions), but use `ItemsRepeater` with `UniformGridLayout` for large data sets.
- `ScrollViewer` must have a constrained parent (`Grid`, `MaxHeight`); placing it inside infinite-height panels disables overflow detection.
- `VerticalScrollBarVisibility` is a WPF property — Avalonia's `TextBox` does not expose it; use `ScrollViewer` wrappers instead.
- Headless tests must `await ExecuteAsync` (not `RunJobs`) for posted dispatcher work to complete.

## License

See [LICENSE](LICENSE).
