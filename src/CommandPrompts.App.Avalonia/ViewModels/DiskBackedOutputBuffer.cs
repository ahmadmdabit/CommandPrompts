using System.Collections;
using System.Collections.Specialized;

using CommandPrompts.Core.Models;
using CommandPrompts.Core.Services;

namespace CommandPrompts.App.Avalonia.ViewModels;

/// <summary>
/// <para>
/// Disk-backed output buffer with a sliding memory window.
/// Keeps the most recent N lines in memory; older lines are read from disk on demand.
/// Binds to Avalonia ItemsControl for UI virtualization.
/// O(1) random access via the journal's line-offset index.
/// </para>
/// <para>
/// Thread safety: all mutations are serialized by <c>locker</c>. <c>CollectionChanged</c>
/// is raised outside the lock so subscribers see a consistent snapshot (the memory
/// window and total line count are updated atomically under the lock). All public
/// callers invoke on the UI thread (Dispatcher), so there is no concurrent access in
/// practice. If this is ever called from a non-UI thread, subscribers must be aware
/// that the event fires after the lock releases — a subscriber reading under the lock
/// will see consistent state.
/// </para>
/// </summary>
public sealed class DiskBackedOutputBuffer : IList<OutputLineViewModel>, System.Collections.IList, INotifyCollectionChanged, IDisposable
{
    private readonly LinkedList<OutputLineViewModel> memoryWindow = new();
    private readonly int memoryWindowSize;
    private readonly SessionJournal journal;
    private long totalLines;
    private readonly Lock locker = new();

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public DiskBackedOutputBuffer(SessionJournal journal, int memoryWindowSize = 1000)
    {
        this.memoryWindowSize = memoryWindowSize;
        this.journal = journal;
        totalLines = this.journal.LineCount;
    }

    public int Count
    {
        get { lock (locker) return (int)totalLines; }
    }

    public OutputLineViewModel this[int index]
    {
        get
        {
            lock (locker)
            {
                if (index < 0 || index >= totalLines)
                    throw new ArgumentOutOfRangeException(nameof(index));
                var memoryStart = (int)(totalLines - memoryWindow.Count);
                if (index >= memoryStart)
                    return memoryWindow.ElementAt(index - memoryStart);
            }
            var entry = journal.ReadLine(index);
            if (entry is null)
                return new OutputLineViewModel(OutputKind.System, "[disk read failed — journal locked]", DateTimeOffset.MinValue);
            return new OutputLineViewModel(entry.Kind, entry.Text, entry.Timestamp);
        }
        set => throw new NotSupportedException();
    }

    public bool IsReadOnly => true;

    public void Add(OutputLineViewModel item)
    {
        lock (locker)
        {
            journal.AppendLine(new OutputEntry
            {
                Timestamp = item.Timestamp,
                Kind = item.Kind,
                Text = item.Text
            });
            memoryWindow.AddLast(item);
            totalLines++;
            while (memoryWindow.Count > memoryWindowSize)
                memoryWindow.RemoveFirst();
        }
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, item, (int)totalLines - 1));
    }

    public void Clear()
    {
        lock (locker)
        {
            memoryWindow.Clear();
            totalLines = 0;
            journal.Clear();
        }
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }

    public bool Contains(OutputLineViewModel item)
    {
        lock (locker) return memoryWindow.Contains(item);
    }

    public void CopyTo(OutputLineViewModel[] array, int arrayIndex)
    {
        for (int i = 0; i < Count; i++)
            array[arrayIndex + i] = this[i];
    }

    public int IndexOf(OutputLineViewModel item)
    {
        lock (locker)
        {
            var memoryStart = (int)(totalLines - memoryWindow.Count);
            var idx = 0;
            foreach (var node in memoryWindow)
            {
                if (EqualityComparer<OutputLineViewModel>.Default.Equals(node, item))
                    return memoryStart + idx;
                idx++;
            }
            return -1;
        }
    }

    public void Insert(int index, OutputLineViewModel item) => throw new NotSupportedException();

    public bool Remove(OutputLineViewModel item) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();

    public IEnumerator<OutputLineViewModel> GetEnumerator()
    {
        lock (locker) return memoryWindow.ToList().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // Non-generic IList (required by Avalonia ItemsControl alongside
    // INotifyCollectionChanged — otherwise the ItemsSource binding fails
    // with "Collection implements INotifyCollectionChanged but not IList").
    bool System.Collections.IList.IsFixedSize => true;
    bool System.Collections.ICollection.IsSynchronized => false;
    object System.Collections.ICollection.SyncRoot => this;
    int System.Collections.IList.Add(object? value) => throw new NotSupportedException();
    bool System.Collections.IList.Contains(object? value) => value is OutputLineViewModel vm && Contains(vm);
    int System.Collections.IList.IndexOf(object? value) => value is OutputLineViewModel vm ? IndexOf(vm) : -1;
    void System.Collections.IList.Insert(int index, object? value) => throw new NotSupportedException();
    void System.Collections.IList.Remove(object? value) => throw new NotSupportedException();
    void System.Collections.IList.RemoveAt(int index) => throw new NotSupportedException();
    void System.Collections.ICollection.CopyTo(Array array, int index)
    {
        for (int i = 0; i < Count; i++)
            array.SetValue(this[i], index + i);
    }
    object? System.Collections.IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    public void Dispose()
    {
        // Journal is owned by the repository (shared single writer) — do not dispose here.
    }
}
