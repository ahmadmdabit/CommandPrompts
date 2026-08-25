using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using CommandPrompts.App.ViewModels;

namespace CommandPrompts.App.Views;

public partial class SessionView : UserControl
{
    public SessionView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (OutputListBox.ItemsSource is INotifyCollectionChanged ncc)
            ncc.CollectionChanged += OnOutputCollectionChanged;

        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Copy,
            OnCopyExecuted,
            OnCopyCanExecute));
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (OutputListBox.ItemsSource is INotifyCollectionChanged ncc)
            ncc.CollectionChanged -= OnOutputCollectionChanged;
    }

    private void OnOutputCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && OutputListBox.Items.Count > 0)
        {
            // Scroll to the last item
            OutputListBox.ScrollIntoView(OutputListBox.Items[^1]);

            // Force scroll to bottom by accessing the ScrollViewer
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var scrollViewer = FindVisualChild<System.Windows.Controls.ScrollViewer>(OutputListBox);
                if (scrollViewer != null)
                {
                    scrollViewer.ScrollToBottom();
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
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

        Clipboard.SetText(string.Join(Environment.NewLine, lines));
    }
}
