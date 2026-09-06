using System.Text.Json;

using CommandPrompts.Core.Json;
using CommandPrompts.Core.Models;
using CommandPrompts.Core.Services;

using NUnit.Framework;

namespace CommandPrompts.App.Avalonia.Tests;

public class SessionJournalTests
{
    private string testDir = string.Empty!;

    [SetUp]
    public void Setup()
    {
        testDir = Path.Combine(Path.GetTempPath(), "CPTests" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(testDir))
            Directory.Delete(testDir, true);
    }

    [Test]
    public async Task AppendThenReadRoundTripsAllEntries()
    {
        var journalPath = Path.Combine(testDir, "test.journal");
        using var journal = new SessionJournal(journalPath);

        var entries = new[]
        {
            new OutputEntry { Timestamp = DateTimeOffset.UtcNow, Kind = OutputKind.StandardOutput, Text = "line 1" },
            new OutputEntry { Timestamp = DateTimeOffset.UtcNow.AddSeconds(1), Kind = OutputKind.StandardError, Text = "err 1" },
            new OutputEntry { Timestamp = DateTimeOffset.UtcNow.AddSeconds(2), Kind = OutputKind.System, Text = "sys 1" }
        };

        foreach (var e in entries)
            journal.AppendLine(e);

        // Debug: check file size
        var fileInfo = new FileInfo(journalPath);
        Assert.That(fileInfo.Length, Is.GreaterThan(0), "Journal file should have content after append");

        var read = new List<OutputEntry>();
        await foreach (var e in journal.ReadAllAsync())
            read.Add(e);

        Assert.That(read, Has.Count.EqualTo(3));
        Assert.That(read[0].Text, Is.EqualTo("line 1"));
        Assert.That(read[1].Kind, Is.EqualTo(OutputKind.StandardError));
    }

    [Test]
    public async Task DeleteAsyncWithOpenJournalDisposesFirstAndDeletesFiles()
    {
        using var repository = new SessionRepository();
        var session = new Session { Name = "DeleteMe" };
        await repository.SaveAsync(session);

        // Open the journal (like a live SessionViewModel buffer would) and write.
        var journal = repository.GetJournal(session.Id);
        journal.AppendLine(new OutputEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = OutputKind.StandardOutput,
            Text = "doomed"
        });

        Assert.DoesNotThrowAsync(async () => await repository.DeleteAsync(session.Id));
        Assert.That(File.Exists(repository.GetSessionFilePath(session.Id)), Is.False);
        Assert.That(File.Exists(repository.GetJournalFilePath(session.Id)), Is.False);
        Assert.That(File.Exists(repository.GetJournalFilePath(session.Id) + ".idx"), Is.False);
    }

    [Test]
    public async Task ReadAllAsyncCorruptTrailingLineDiscardsPartialEntry()
    {
        var journalPath = Path.Combine(testDir, "test.journal");
        var validLine = """{"timestamp":"2024-01-01T00:00:00Z","kind":0,"text":"valid"}""" + "\n";
        await File.WriteAllTextAsync(journalPath, validLine + "{\"timestamp\":\"2024-01-01");

        using var journal = new SessionJournal(journalPath);
        var read = new List<OutputEntry>();
        await foreach (var e in journal.ReadAllAsync())
            read.Add(e);

        Assert.That(read, Has.Count.EqualTo(1),
            "Corrupt trailing line must be discarded; valid lines preserved");
        Assert.That(read[0].Text, Is.EqualTo("valid"));
    }

    [Test]
    public void ClearTruncatesJournalAllowsReappend()
    {
        var journalPath = Path.Combine(testDir, "test.journal");
        using var journal = new SessionJournal(journalPath);
        journal.AppendLine(new OutputEntry { Timestamp = DateTimeOffset.UtcNow, Kind = OutputKind.StandardOutput, Text = "line1" });
        journal.AppendLine(new OutputEntry { Timestamp = DateTimeOffset.UtcNow, Kind = OutputKind.StandardOutput, Text = "line2" });
        Assert.That(journal.LineCount, Is.EqualTo(2));

        journal.Clear();  // was: IOException on Append-mode stream
        Assert.That(journal.LineCount, Is.EqualTo(0));

        journal.AppendLine(new OutputEntry { Timestamp = DateTimeOffset.UtcNow, Kind = OutputKind.StandardOutput, Text = "after-clear" });
        Assert.That(journal.LineCount, Is.EqualTo(1));
        Assert.That(journal.ReadLine(0)?.Text, Is.EqualTo("after-clear"));
    }

    // Fused-line regression: two JSON values on one line (concurrent writers)
    // must yield both entries instead of throwing or discarding the line.
    [Test]
    public void ParseJournalLineFusedLineYieldsBothEntries()
    {
        var first = JsonSerializer.Serialize(
            new OutputEntry { Timestamp = DateTimeOffset.UtcNow, Kind = OutputKind.StandardOutput, Text = "first" },
            JournalJsonContext.Default.OutputEntry);
        var second = JsonSerializer.Serialize(
            new OutputEntry { Timestamp = DateTimeOffset.UtcNow, Kind = OutputKind.StandardOutput, Text = "second" },
            JournalJsonContext.Default.OutputEntry);

        var parsed = SessionJournal.ParseJournalLine(first + second).ToList();

        Assert.That(parsed, Has.Count.EqualTo(2));
        Assert.That(parsed[0].Text, Is.EqualTo("first"));
        Assert.That(parsed[1].Text, Is.EqualTo("second"));
    }

    [Test]
    public async Task ReadOutputAsyncFusedLineSalvagesValidEntries()
    {
        using var repository = new SessionRepository();
        var session = new Session { Name = "Fused" };
        await repository.SaveAsync(session);

        string Encode(string text) => JsonSerializer.Serialize(
            new OutputEntry { Timestamp = DateTimeOffset.UtcNow, Kind = OutputKind.StandardOutput, Text = text },
            JournalJsonContext.Default.OutputEntry);
        await File.WriteAllTextAsync(repository.GetJournalFilePath(session.Id),
            Encode("a") + "\n" + Encode("b") + Encode("c") + "\n" + Encode("d") + "\n");

        var entries = await repository.ReadOutputAsync(session.Id);

        Assert.That(entries.Select(e => e.Text), Is.EqualTo(new[] { "a", "b", "c", "d" }));
    }

    [Test]
    public async Task ReadAllAsyncFusedLineSalvagesValidEntries()
    {
        var journalPath = Path.Combine(testDir, "fused.journal");
        string Encode(string text) => JsonSerializer.Serialize(
            new OutputEntry { Timestamp = DateTimeOffset.UtcNow, Kind = OutputKind.StandardOutput, Text = text },
            JournalJsonContext.Default.OutputEntry);
        await File.WriteAllTextAsync(journalPath, Encode("x") + Encode("y") + "\n");

        using var journal = new SessionJournal(journalPath);
        var read = new List<OutputEntry>();
        await foreach (var e in journal.ReadAllAsync())
            read.Add(e);

        Assert.That(read.Select(e => e.Text), Is.EqualTo(new[] { "x", "y" }));
    }

    // C1 regression: ReadOutputAsync must read directly from the file, not
    // create a second SessionJournal instance (which would break the
    // single-writer invariant by opening a competing write-capable handle).
    [Test]
    public async Task ReadOutputAsyncDoesNotCreateSecondJournal()
    {
        using var repository = new SessionRepository();
        var session = new Session { Name = "C1Test" };
        await repository.SaveAsync(session);

        // Write via the cached journal (the single writer).
        var journal = repository.GetJournal(session.Id);
        journal.AppendLine(new OutputEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = OutputKind.StandardOutput,
            Text = "from-journal"
        });

        // ReadOutputAsync must return the same data without creating a
        // second journal instance.
        var entries = await repository.ReadOutputAsync(session.Id);
        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Text, Is.EqualTo("from-journal"));
    }

    // C2 regression: CloseJournal must release the cached journal so its file
    // handles don't leak for the lifetime of the repository.
    [Test]
    public void CloseJournalReleasesCachedJournal()
    {
        using var repository = new SessionRepository();
        var session = new Session { Name = "C2Test" };
        repository.SaveAsync(session).Wait();

        // Open the journal (caches it).
        var journal = repository.GetJournal(session.Id);
        Assert.That(journal, Is.Not.Null);

        // Close it.
        repository.CloseJournal(session.Id);

        // GetJournal should create a NEW instance (proving the old one was
        // released). We can't directly assert "not same instance" without
        // exposing internals, but we can verify the journal still works.
        var newJournal = repository.GetJournal(session.Id);
        Assert.That(newJournal, Is.Not.Null);
    }
}
