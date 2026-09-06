# CommandPrompts

Avalonia desktop app (.NET 10, Windows) for managing and running multiple command-prompt sessions with persisted history. Two-project Clean Architecture solution (`CommandPrompts.slnx`; the legacy WPF project was removed):

- `src/CommandPrompts.Core` — models, abstractions, services (platform-neutral)
- `src/CommandPrompts.App.Avalonia` — Avalonia 12 presentation (active)
- `tests/CommandPrompts.App.Avalonia.Tests` — headless NUnit tests

## Build & Run

```bash
dotnet build CommandPrompts.slnx
dotnet run --project src/CommandPrompts.App.Avalonia
```

- Requires .NET 10 SDK (`net10.0`).
- NuGet dependencies (Avalonia): `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `CommunityToolkit.Mvvm`, `ZLogger`.
- Code style is enforced at build time for Core (`EnforceCodeStyleInBuild`); `AnalysisLevel=latest-all` surfaces all analyzer warnings.

## Tests

```bash
dotnet test tests/CommandPrompts.App.Avalonia.Tests
```

Uses `Avalonia.Headless.NUnit` with `PerTest` isolation. Tests must be `async Task` awaiting `ExecuteAsync` for deterministic dispatcher pumping.

## Conventions

- Naming: **no underscores anywhere** — private fields are camelCase, types/files PascalCase. Domain-driven names only (no `Helper`/`Manager` files).
- MVVM via `CommunityToolkit.Mvvm` source generation: `[ObservableProperty]`, `[RelayCommand]`, `[NotifyPropertyChangedFor]`.
- DI without container: dependencies (`IProcessExecutor`, `ISessionRepository`) injected via constructor; view-layer services attached post-construction via `AttachServices`.
- JSON: source-generated serializer via `[JsonSerializable]` context in `Core/Json/AppJsonContext.cs`. Add new model types there, not ad-hoc reflection calls.
- Performance requirements from `resources/Project-Requirements.md` apply to all changes: minimal allocation, thread-safe, fully async/non-blocking, reflection-free where possible.
- Session history persists as JSON files in `%LOCALAPPDATA%\CommandPrompts\History` (one file per session, write-once temp file per save, atomic replace).
- Output journal: one append-only `.journal` + `.journal.idx` per session, single cached writer owned by the repository — new readers must use the shared parser (`SessionJournal.ParseJournalLine`), never open a second `SessionJournal`; VMs release handles via `CloseJournal` on dispose.
- Env profiles live in `%LOCALAPPDATA%\CommandPrompts\Profiles.json`; session VMs track profile vars and user vars separately and rebuild on change (switching profiles replaces, never accumulates).
- ViewModels are `sealed`; collections bound to `ItemsControl` must implement non-generic `System.Collections.IList`.

## Avalonia-Specific Conventions

- **Compiled bindings**: Use `x:DataType` on all `DataTemplate`, `UserControl`, and `Window` roots. Wrong property paths fail at build time (AVLN2000).
- **UI-thread marshaling**: Use `Dispatcher.UIThread.InvokeAsync` for UI-thread result collection; `Dispatcher.UIThread.Post` for fire-and-forget updates.
- **Theme**: Brushes live in `Themes/AppTheme.axaml` (merged via `ResourceInclude`); control styles live in `Themes/AppStyles.axaml` (merged via `StyleInclude`). `<Style>` cannot live inside a `<ResourceDictionary>`.
- **ScrollViewer**: Must have a constrained parent (`Grid`, `MaxHeight`, or `DockPanel`). Never inside `StackPanel`.
- **WrapPanel**: Does not virtualize — acceptable for small collections (sessions). For large collections, prefer `ItemsRepeater` with `UniformGridLayout`.

## Pitfalls

- Solution file is `.slnx` (new format) — pass it explicitly to `dotnet` commands.
- Never hand-edit `bin/`, `obj/`, or `History/*.json`.
- `resources/notes.md` is the user's scratch task list, not app documentation.
- Avalonia's `TextBox` does not expose `VerticalScrollBarVisibility` — use a `ScrollViewer` wrapper.
- ContextMenu bindings in popups require `#Root.DataContext.Property` (named Window) — `$parent` selectors resolve to null in popup overlays.
- `DataTransfer` (Avalonia 12 clipboard API) must be disposed after use.
- Headless tests use `TraceListener` + `LogToTrace` — framework logs go to test output, not app logs.
- `FileMode.Append` forbids `SetLength(0)` — truncatable append streams need `OpenOrCreate` + `Seek(End)`.
- Auto-scroll follow flags must be recalculated from current scroll position on every `ScrollChanged`, not gated on `ExtentDelta`.
- Analyzer triage policy: DTO collection setters stay (STJ deserialization requires them — reject CA2227/CA1002); no blanket `ConfigureAwait(false)` in VM paths (UI-thread affinity); intentional general catches (process kill, temp cleanup) stay documented.
