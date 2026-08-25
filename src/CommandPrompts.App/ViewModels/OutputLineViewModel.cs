using System.Windows.Media;

using CommandPrompts.Core.Models;

namespace CommandPrompts.App.ViewModels;

public sealed class OutputLineViewModel
{
    private static readonly SolidColorBrush StdOutBrush =
        Freeze(new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)));
    private static readonly SolidColorBrush StdErrBrush =
        Freeze(new SolidColorBrush(Color.FromRgb(0xA0, 0x20, 0x60)));
    private static readonly SolidColorBrush SystemBrush =
        Freeze(new SolidColorBrush(Color.FromRgb(0x60, 0x8B, 0x4E)));

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

    public SolidColorBrush Foreground => Kind switch
    {
        OutputKind.StandardError => StdErrBrush,
        OutputKind.System => SystemBrush,
        _ => StdOutBrush
    };

    public string KindLabel => Kind switch
    {
        OutputKind.StandardError => "ERR",
        OutputKind.System => "SYS",
        _ => "OUT"
    };

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
