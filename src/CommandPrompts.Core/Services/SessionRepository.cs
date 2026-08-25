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
    private bool disposed;

    public SessionRepository()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        historyDir = Path.Combine(basePath, AppDataSubfolder, "History");
        Directory.CreateDirectory(historyDir);
    }

    public string GetSessionFilePath(Guid sessionId)
        => Path.Combine(historyDir, $"{sessionId:N}.json");

    public async Task<IReadOnlyList<Session>> LoadAllAsync(CancellationToken ct = default)
    {
        var files = Directory.GetFiles(historyDir, "*.json");
        if (files.Length == 0)
            return [];

        var sessions = new List<Session>(files.Length);

        // Parallel deserialize for large histories
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

        // Serialize to a unique temp file in the same dir (same volume => atomic move),
        // then move into place atomically under the ioLock so concurrent saves on the
        // same session file don't collide on a shared .tmp path.
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

                // Atomic replace
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }
    }

    public async Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
    {
        var path = GetSessionFilePath(sessionId);

        lock (ioLock)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(SessionRepository));

            if (File.Exists(path))
                File.Delete(path);
        }

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
            return null; // corrupted file — skip
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
    }
}
