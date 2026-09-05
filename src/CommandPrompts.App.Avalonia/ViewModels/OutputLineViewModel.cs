using Avalonia.Media;

using CommandPrompts.Core.Models;

namespace CommandPrompts.App.Avalonia.ViewModels;

public sealed class OutputLineViewModel
{
    // WPF-parity console colors: OUT #CCCCCC, ERR #A02060, SYS #608B3E.
    private static readonly IBrush StdOutBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
    private static readonly IBrush StdErrBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0x20, 0x60));
    private static readonly IBrush SystemBrush = new SolidColorBrush(Color.FromRgb(0x60, 0x8B, 0x3E));

    public OutputLineViewModel(OutputKind kind, string text, DateTimeOffset timestamp)
    {
        Kind = kind;
        Text = text;
        Timestamp = timestamp;
    }

    public OutputKind Kind { get; }
    public string Text { get; }
    public DateTimeOffset Timestamp { get; }

    public string TimestampFormatted => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");

    public string KindLabel => Kind switch
    {
        OutputKind.StandardOutput => "[OUT]",
        OutputKind.StandardError => "[ERR]",
        _ => "[SYS]"
    };

    public IBrush Foreground => Kind switch
    {
        OutputKind.StandardError => StdErrBrush,
        OutputKind.System => SystemBrush,
        _ => StdOutBrush
    };
}
