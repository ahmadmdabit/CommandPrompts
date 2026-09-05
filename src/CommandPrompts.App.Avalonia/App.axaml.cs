using System.Diagnostics;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Markup.Xaml;

[assembly: InternalsVisibleTo("CommandPrompts.App.Avalonia.Tests")]

namespace CommandPrompts.App.Avalonia;

internal sealed class App : Application
{
    private Logging.AvaloniaTraceListener? traceListener;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        traceListener = new Logging.AvaloniaTraceListener();
        Trace.Listeners.Add(traceListener);

        var mainWindow = new Views.MainWindow();
        mainWindow.Show();

        base.OnFrameworkInitializationCompleted();
    }
}
