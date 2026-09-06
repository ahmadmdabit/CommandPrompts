using CommandPrompts.Core.Models;
using CommandPrompts.Core.Services;

namespace CommandPrompts.Core.Abstractions;

public interface ISessionRepository
{
    Task<IReadOnlyList<Session>> LoadAllAsync(CancellationToken ct = default);
    Task SaveAsync(Session session, CancellationToken ct = default);
    Task DeleteAsync(Guid sessionId, CancellationToken ct = default);
    string GetSessionFilePath(Guid sessionId);
    Task AppendOutputAsync(Guid sessionId, OutputEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<OutputEntry>> ReadOutputAsync(Guid sessionId, CancellationToken ct = default);
    string GetJournalFilePath(Guid sessionId);
    SessionJournal GetJournal(Guid sessionId);
    /// <summary>Closes the cached journal for a session, releasing its file handles.
    /// Call when a session VM is deactivated or a tab is closed (C2).</summary>
    void CloseJournal(Guid sessionId);
}
