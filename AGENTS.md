# CommandPrompts

WPF desktop app (.NET 10, Windows) for managing and running multiple command-prompt sessions with persisted history. Two-project Clean Architecture solution (`CommandPrompts.slnx`): `src/CommandPrompts.Core` (models, abstractions, services — platform-neutral) and `src/CommandPrompts.App` (WPF UI referencing Core). No test project exists.

## Build & Run

```bash
dotnet build CommandPrompts.slnx          # build; EnforceCodeStyleInBuild is ON for Core
dotnet run --project src/CommandPrompts.App
```

- Requires .NET 10 SDK (`net10.0` / `net10.0-windows`). Only NuGet dependency: Microsoft.Xaml.Behaviors.Wpf.
- There are no tests or lint scripts — verification is a clean build (0 warnings/errors expected; `AnalysisLevel` is `latest-all`, so warnings surface on build).

## Conventions

- Naming: **no underscores anywhere** — private fields are camelCase (`private readonly IProcessExecutor executor;`), types/files PascalCase. Domain-driven names only (no `Helper`/`Manager` files).
- MVVM by hand: ViewModels inherit local `ObservableObject`; commands use local `RelayCommand` (no CommunityToolkit.Mvvm).
- DI without container: dependencies (`IProcessExecutor`, `ISessionRepository`) injected via constructor into ViewModels; Core defines interfaces in `Abstractions/`, implementations in `Services/`.
- JSON: source-generated serializer via `[JsonSerializable]` context in `Core/Json/AppJsonContext.cs` (AOT-compatible; Core sets `IsAotCompatible=true`). Add new model types there, not ad-hoc `JsonSerializer` reflection calls.
- Performance requirements from `resources/Project-Requirements.md` apply to all changes: minimal allocation, thread-safe, fully async/non-blocking, reflection-free where possible.
- Session history persists as JSON files in `History/` (one file per session).

## Pitfalls

- Solution file is `.slnx` (new format) — pass it explicitly to `dotnet` commands; older tooling may not parse it.
- Repo has no commits yet on `master` — never assume prior git history/branches exist.
- Never hand-edit `bin/`, `obj/`, or `History/*.json`.
- `resources/notes.md` is the user's scratch task list, not app documentation.
