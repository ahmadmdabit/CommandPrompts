using CommandPrompts.Core.Models;

namespace CommandPrompts.Core.Abstractions;

public interface ISessionRepository
{
    Task<IReadOnlyList<Session>> LoadAllAsync(CancellationToken ct = default);
    Task SaveAsync(Session session, CancellationToken ct = default);
    Task DeleteAsync(Guid sessionId, CancellationToken ct = default);
    string GetSessionFilePath(Guid sessionId);
}
