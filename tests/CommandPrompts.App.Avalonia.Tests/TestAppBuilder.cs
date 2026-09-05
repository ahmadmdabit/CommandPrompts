using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(CommandPrompts.App.Avalonia.Tests.TestAppBuilder))]

namespace CommandPrompts.App.Avalonia.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<CommandPrompts.App.Avalonia.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
