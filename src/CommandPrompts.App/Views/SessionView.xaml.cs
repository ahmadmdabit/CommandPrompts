using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using CommandPrompts.App.ViewModels;

namespace CommandPrompts.App.Views;

public partial class SessionView : UserControl
{
    private INotifyCollectionChanged? outputCollection;
    private CommandBinding? copyCommandBinding;
    private ScrollViewer? outputScrollViewer;

    public SessionView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        SubscribeToOutputCollection();

        if (copyCommandBinding is null)
        {
            copyCommandBinding = new CommandBinding(
                ApplicationCommands.Copy,
                OnCopyExecuted,
                OnCopyCanExecute);

            CommandBindings.Add(copyCommandBinding);
        }

        outputScrollViewer ??= FindVisualChild<ScrollViewer>(OutputListBox);
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        UnsubscribeFromOutputCollection();

        if (copyCommandBinding is not null)
        {
            CommandBindings.Remove(copyCommandBinding);
            copyCommandBinding = null;
        }

        outputScrollViewer = null;
    }

    private void OnOutputCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && OutputListBox.Items.Count > 0)
        {
            // Scroll to the last item
            OutputListBox.ScrollIntoView(OutputListBox.Items[^1]);

            // Ensure scroll to bottom by accessing the ScrollViewer
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                OutputListBox.ScrollIntoView(OutputListBox.Items[^1]);

                outputScrollViewer ??= FindVisualChild<ScrollViewer>(OutputListBox);
                outputScrollViewer?.ScrollToBottom();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
                return descendant;
        }
        return null;
    }

    private void OnCopyCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = OutputListBox.SelectedItems.Count > 0;
    }

    private void OnCopyExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (OutputListBox.SelectedItems.Count == 0)
            return;

        var lines = OutputListBox.SelectedItems
            .Cast<OutputLineViewModel>()
            .OrderBy(l => l.Timestamp)
            .Select(l => l.Text);

        TrySetClipboardText(string.Join(System.Environment.NewLine, lines));
    }

    private void SubscribeToOutputCollection()
    {
        if (ReferenceEquals(outputCollection, OutputListBox.ItemsSource))
            return;

        UnsubscribeFromOutputCollection();

        outputCollection = OutputListBox.ItemsSource as INotifyCollectionChanged;
        if (outputCollection is not null)
            outputCollection.CollectionChanged += OnOutputCollectionChanged;
    }

    private void UnsubscribeFromOutputCollection()
    {
        if (outputCollection is not null)
            outputCollection.CollectionChanged -= OnOutputCollectionChanged;

        outputCollection = null;
    }

    private static void TrySetClipboardText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException ex)
        {
            MessageBox.Show(
                $"Unable to access the clipboard: {ex.Message}",
                "Clipboard Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
