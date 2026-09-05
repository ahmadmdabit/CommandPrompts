using Avalonia.Controls;

namespace CommandPrompts.App.Avalonia.Services;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, bool cancelVisible = true)
    {
        InitializeComponent();

        Title = title;
        MessageText.Text = message;
        CancelButton.IsVisible = cancelVisible;

        OkButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }
}
