using System.Text;
using System.Text.Json;

using CommandPrompts.Core.Abstractions;
using CommandPrompts.Core.Json;
using CommandPrompts.Core.Models;

namespace CommandPrompts.Core.Services;

public sealed class SessionRepository : ISessionRepository, IDisposable
{
    private readonly string historyDir;
    private const string AppDataSubfolder = "CommandPrompts";
    private readonly object ioLock = new();
    private readonly object journalLock = new();
    private readonly Dictionary<Guid, SessionJournal> journals = new();
    private bool disposed;

    public SessionRepository()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        historyDir = Path.Combine(basePath, AppDataSubfolder, "History");
        Directory.CreateDirectory(historyDir);
    }

    public string GetSessionFilePath(Guid sessionId)
        => Path.Combine(historyDir, $"{sessionId:N}.json");

    public string GetJournalFilePath(Guid sessionId)
        => Path.Combine(historyDir, $"{sessionId:N}.journal");

    public async Task<IReadOnlyList<Session>> LoadAllAsync(CancellationToken ct = default)
    {
        var files = Directory.GetFiles(historyDir, "*.json");
        if (files.Length == 0)
            return [];

        var sessions = new List<Session>(files.Length);

        var tasks = new Task<Session?>[files.Length];
        for (var i = 0; i < files.Length; i++)
        {
            var file = files[i];
            tasks[i] = DeserializeFileAsync(file, ct);
        }

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (var s in results)
        {
            if (s is not null)
                sessions.Add(s);
        }

        sessions.Sort(static (a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
        return sessions;
    }

    public async Task SaveAsync(Session session, CancellationToken ct = default)
    {
        var path = GetSessionFilePath(session.Id);

        var tempPath = Path.Combine(historyDir, $"{session.Id:N}.{Guid.NewGuid():N}.tmp");

        lock (ioLock)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(SessionRepository));

            try
            {
                using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, bufferSize: 4096, useAsync: false))
                {
                    JsonSerializer.Serialize(fs, session,
                        AppJsonContext.Default.Session);
                }

                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }
    }

    public Task AppendOutputAsync(Guid sessionId, OutputEntry entry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        GetJournal(sessionId).AppendLine(entry);
        return Task.CompletedTask;
    }

    /// <summary>Shared append-only journal for a session (single writer).</summary>
    public SessionJournal GetJournal(Guid sessionId)
    {
        lock (journalLock)
        {
            if (!journals.TryGetValue(sessionId, out var journal))
            {
                var path = GetJournalFilePath(sessionId);
                journal = new SessionJournal(path);
                journals[sessionId] = journal;
            }
            return journal;
        }
    }

    /// <summary>Closes the cached journal for a session, releasing its file handles.
    /// Call when a session VM is deactivated or a tab is closed (C2).</summary>
    public void CloseJournal(Guid sessionId)
    {
        lock (journalLock)
        {
            if (journals.TryGetValue(sessionId, out var journal))
            {
                journal.Dispose();
                journals.Remove(sessionId);
            }
        }
    }

    public async Task<IReadOnlyList<OutputEntry>> ReadOutputAsync(Guid sessionId, CancellationToken ct = default)
    {
        // Read directly from the file — do NOT create a second SessionJournal
        // instance. The cached journal owns the write handle + offset index;
        // a second instance would open a competing write-capable handle and
        // break the single-writer invariant (C1).
        var path = GetJournalFilePath(sessionId);
        if (!File.Exists(path))
            return [];

        var entries = new List<OutputEntry>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
            ct.ThrowIfCancellationRequested();
            // Shared salvage parser: a fused line yields each healthy entry
            // instead of discarding the whole line (or throwing).
            entries.AddRange(SessionJournal.ParseJournalLine(line));
        }
        return entries;
    }

    public async Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
    {
        // Dispose the cached journal FIRST: its writer/index handles keep the
        // files open, and File.Delete throws sharing-violation while held.
        lock (journalLock)
        {
            if (journals.TryGetValue(sessionId, out var journal))
            {
                journal.Dispose();
                journals.Remove(sessionId);
            }
        }

        var path = GetSessionFilePath(sessionId);

        lock (ioLock)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(SessionRepository));

            if (File.Exists(path))
                File.Delete(path);
        }

        var journalPath = GetJournalFilePath(sessionId);
        if (File.Exists(journalPath))
            File.Delete(journalPath);

        var indexPath = journalPath + ".idx";
        if (File.Exists(indexPath))
            File.Delete(indexPath);

        await Task.CompletedTask;
    }

    private static async Task<Session?> DeserializeFileAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, bufferSize: 4096, useAsync: true);
            return await JsonSerializer.DeserializeAsync(fs,
                AppJsonContext.Default.Session, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        lock (ioLock)
        {
            disposed = true;
        }
        lock (journalLock)
        {
            foreach (var journal in journals.Values)
                journal.Dispose();
            journals.Clear();
        }
    }
}
