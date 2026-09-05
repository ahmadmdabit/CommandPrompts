using System.Diagnostics;

using Avalonia.Controls;

using CommandPrompts.App.Avalonia.Services;
using CommandPrompts.App.Avalonia.ViewModels;

namespace CommandPrompts.App.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is not MainViewModel vm)
            return;

        // Wire view-layer services that need an owner window.
        vm.AttachServices(
            new DialogService(this),
            new ClipboardService(this),
            new ProcessLauncherService());

        // Fire the initial load; exceptions must be observable, never swallowed.
        _ = RunInitialLoadAsync(vm);
    }

    private static async Task RunInitialLoadAsync(MainViewModel vm)
    {
        try
        {
            await vm.LoadedCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Initial session load failed: {ex}");
            Trace.TraceError($"Initial session load failed: {ex}");
        }
    }
}
