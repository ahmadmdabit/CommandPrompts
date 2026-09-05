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
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentDelta.Y == 0)
            return;

        var sb = OutputListBox.Scroll;
        userScrolledAway = sb is not null
            && sb.Offset.Y + sb.Viewport.Height < sb.Extent.Height - 40;
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
