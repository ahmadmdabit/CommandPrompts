using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using CommandPrompts.Core.Json;
using CommandPrompts.Core.Models;

namespace CommandPrompts.Core.Services;

/// <summary>
/// Append-only output journal. Each entry is one newline-delimited JSON line.
/// Failure-safe: a kill mid-write loses at most the trailing partial line.
/// Maintains a line-offset index for O(1) random access to any line.
/// </summary>
public sealed class SessionJournal : IDisposable
{
    private readonly string path;
    private readonly string indexPath;
    private readonly StreamWriter writer;
    private readonly FileStream indexStream;
    private readonly Lock locker = new();
    private bool disposed;
    private long lineCount;

    public SessionJournal(string path)
    {
        this.path = path;
        indexPath = path + ".idx";
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // OpenOrCreate (not Append): Append mode forbids SetLength(0), which
        // Clear() requires. Seek-to-end preserves append semantics on the writer.
        var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        stream.Seek(0, SeekOrigin.End);
        writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

        indexStream = new FileStream(indexPath, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        lineCount = indexStream.Length / sizeof(long);
    }

    public long LineCount
    {
        get { lock (locker) return lineCount; }
    }

    public void AppendLine(OutputEntry entry)
    {
        var json = JsonSerializer.Serialize(entry, JournalJsonContext.Default.OutputEntry);
        lock (locker)
        {
            if (disposed) return;
            // Flush first so BaseStream.Position reflects all prior bytes;
            // otherwise the recorded offset lags behind buffered data.
            writer.Flush();
            var offset = writer.BaseStream.Position;
            writer.Write(json);
            writer.Write('\n');
            writer.Flush();
            // Append the byte offset of this line to the index
            indexStream.Seek(0, SeekOrigin.End);
            indexStream.Write(BitConverter.GetBytes(offset), 0, sizeof(long));
            indexStream.Flush();
            lineCount++;
        }
    }

    /// <summary>Reads a specific line by index in O(1) via the offset index.</summary>
    public OutputEntry? ReadLine(long index)
    {
        lock (locker)
        {
            if (disposed || index < 0 || index >= lineCount)
                return null;
            try
            {
                indexStream.Seek(index * sizeof(long), SeekOrigin.Begin);
                var offsetBytes = new byte[sizeof(long)];
                indexStream.ReadExactly(offsetBytes, 0, sizeof(long));
                var offset = BitConverter.ToInt64(offsetBytes, 0);
                // Dedicated read stream — the writer's stream is write-only.
                using var readStream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                readStream.Seek(offset, SeekOrigin.Begin);
                using var reader = new StreamReader(readStream, Encoding.UTF8);
                var line = reader.ReadLine();
                if (line is null) return null;
                try
                {
                    return JsonSerializer.Deserialize(line, JournalJsonContext.Default.OutputEntry);
                }
                catch (JsonException)
                {
                    // Stale/corrupt offset — treat as missing.
                    return null;
                }
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    public async IAsyncEnumerable<OutputEntry> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!File.Exists(path))
            yield break;

        using var reader = new StreamReader(
            new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete),
            Encoding.UTF8);
        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var entry in ParseJournalLine(line))
                yield return entry;
        }
    }

    /// <summary>
    /// Parses one journal line into zero or more entries. A healthy line holds
    /// exactly one JSON value; a fused line (two writers interleaved, legacy
    /// file) holds several. Each top-level object is decoded independently so
    /// one corrupt segment never discards its healthy neighbors.
    /// </summary>
    public static IEnumerable<OutputEntry> ParseJournalLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            yield break;
        foreach (var segment in SplitTopLevelObjects(line))
        {
            OutputEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize(segment, JournalJsonContext.Default.OutputEntry);
            }
            catch (JsonException)
            {
                // Undecodable segment (e.g. trailing partial line after a failure) — skip.
                continue;
            }
            if (entry is not null)
                yield return entry;
        }
    }

    /// <summary>
    /// Splits a line into its top-level JSON object segments, brace-matching
    /// with string/escape awareness. Never throws.
    /// </summary>
    private static IEnumerable<string> SplitTopLevelObjects(string line)
    {
        var depth = 0;
        var inString = false;
        var escape = false;
        var start = -1;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inString)
            {
                if (escape)
                    escape = false;
                else if (c == '\\')
                    escape = true;
                else if (c == '"')
                    inString = false;
            }
            else if (c == '"')
            {
                inString = true;
            }
            else if (c == '{')
            {
                if (depth == 0)
                    start = i;
                depth++;
            }
            else if (c == '}' && depth > 0)
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    yield return line.Substring(start, i - start + 1);
                    start = -1;
                }
            }
        }
    }

    public void Clear()
    {
        lock (locker)
        {
            if (disposed) return;
            writer.Flush();
            writer.BaseStream.SetLength(0);
            // Reset the position: after truncation it would point past the new
            // end, corrupting the next recorded offset. Buffer is empty after
            // Flush, so repositioning the base stream is safe.
            writer.BaseStream.Position = 0;
            indexStream.SetLength(0);
            indexStream.Seek(0, SeekOrigin.Begin);
            lineCount = 0;
        }
    }

    public void Dispose()
    {
        lock (locker)
        {
            if (disposed) return;
            disposed = true;
            writer.Flush();
            writer.Dispose();
            indexStream.Dispose();
        }
    }
}
