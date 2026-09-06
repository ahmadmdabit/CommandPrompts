using Avalonia.Controls;

using CommandPrompts.App.Avalonia.ViewModels;

namespace CommandPrompts.App.Avalonia.Services;

public partial class ProfileEditorDialog : Window
{
    // This empty ctor for design time only
    public ProfileEditorDialog() : this(null)
    {
    }

    public ProfileEditorDialog(ProfileEditorViewModel? viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        CloseButton.Click += (_, _) => Close();
    }
}
