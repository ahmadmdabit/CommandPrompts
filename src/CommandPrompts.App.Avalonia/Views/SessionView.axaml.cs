using System.Collections.Specialized;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace CommandPrompts.App.Avalonia.Views;

public partial class SessionView : UserControl
{
    private INotifyCollectionChanged? subscribedCollection;
    private bool userScrolledAway;

    public SessionView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        OutputListBox.AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        SubscribeToOutputCollection();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        UnsubscribeFromOutputCollection();
    }

    private void SubscribeToOutputCollection()
    {
        if (ReferenceEquals(subscribedCollection, OutputListBox.ItemsSource))
            return;

        UnsubscribeFromOutputCollection();

        subscribedCollection = OutputListBox.ItemsSource as INotifyCollectionChanged;
        if (subscribedCollection is not null)
            subscribedCollection.CollectionChanged += OnOutputCollectionChanged;
    }

    private void UnsubscribeFromOutputCollection()
    {
        if (subscribedCollection is not null)
            subscribedCollection.CollectionChanged -= OnOutputCollectionChanged;

        subscribedCollection = null;
    }

    // Auto-scroll: follow new lines only while the user is already at (or near) the
    // bottom — scrolling up to read history must not be fought by the app.
    // Always recalculate from the current scroll position so scrolling back down
    // re-enables auto-scroll (the flag is otherwise sticky-disabled forever).
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var sb = OutputListBox.Scroll;
        if (sb is null)
            return;

        userScrolledAway = sb.Offset.Y + sb.Viewport.Height < sb.Extent.Height - 40;
    }

    private void OnOutputCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || userScrolledAway)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (OutputListBox.ItemCount > 0)
                OutputListBox.ScrollIntoView(OutputListBox.ItemCount - 1);
        });
    }
}
