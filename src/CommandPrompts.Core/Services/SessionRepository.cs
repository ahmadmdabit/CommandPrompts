using System.Text.Json;

using CommandPrompts.Core.Abstractions;
using CommandPrompts.Core.Json;
using CommandPrompts.Core.Models;

namespace CommandPrompts.Core.Services;

public sealed class SessionRepository : ISessionRepository
{
    private readonly string historyDir;
    private readonly Lock ioLock = new();

    public SessionRepository()
    {
        historyDir = Path.Combine(AppContext.BaseDirectory, "History");
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

        // Serialize to pooled buffer first, then write atomically
        var tempPath = path + ".tmp";

        await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(fs, session,
                AppJsonContext.Default.Session, ct).ConfigureAwait(false);
        }

        // Atomic replace
        lock (ioLock)
        {
            File.Move(tempPath, path, overwrite: true);
        }
    }

    public Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
    {
        var path = GetSessionFilePath(sessionId);
        lock (ioLock)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        return Task.CompletedTask;
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
}
