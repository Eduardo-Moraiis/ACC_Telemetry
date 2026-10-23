using AccTelemetry.Domain.Models;

namespace AccTelemetry.Domain.Repositories;

public interface ITelemetryRepository
{
    Task InitializeAsync();
    Task SaveSessionAsync(Session session);
    Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync();
    Task<IReadOnlyList<LapSummaryDto>> GetLapsBySessionAsync(Guid sessionId);
    Task<LapTelemetry?> GetLapTelemetryAsync(Guid lapId);
}