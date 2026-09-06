using CommandPrompts.App.Avalonia.ViewModels;
using CommandPrompts.Core.Models;
using CommandPrompts.Core.Services;

using NUnit.Framework;

namespace CommandPrompts.App.Avalonia.Tests;

public class DiskBackedBufferTests
{
    private string testDir = string.Empty!;

    [SetUp]
    public void Setup()
    {
        testDir = Path.Combine(Path.GetTempPath(), "CPBuffer" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(testDir))
            Directory.Delete(testDir, true);
    }

    [Test]
    public void AddWithinWindowKeepsInMemory()
    {
        using var journal = new SessionJournal(Path.Combine(testDir, "log.journal"));
        var buffer = new DiskBackedOutputBuffer(journal, memoryWindowSize: 100);
        buffer.Add(new OutputLineViewModel(OutputKind.StandardOutput, "line", DateTimeOffset.UtcNow));

        Assert.That(buffer.Count, Is.EqualTo(1));
        Assert.That(buffer[0].Text, Is.EqualTo("line"));
        buffer.Dispose();
    }

    [Test]
    public void AddExceedingWindowArchivesToDisk()
    {
        using var journal = new SessionJournal(Path.Combine(testDir, "log.journal"));
        var buffer = new DiskBackedOutputBuffer(journal, memoryWindowSize: 10);
        for (int i = 0; i < 20; i++)
            buffer.Add(new OutputLineViewModel(OutputKind.StandardOutput, $"line {i}", DateTimeOffset.UtcNow));

        Assert.That(buffer.Count, Is.EqualTo(20));
        Assert.That(buffer[0].Text, Is.EqualTo("line 0"));
        Assert.That(buffer[19].Text, Is.EqualTo("line 19"));
        buffer.Dispose();
    }
}
