namespace AccTelemetry.Domain.Models;

public record SessionSummaryDto(
    Guid Id,
    string Track,
    string Car,
    string Driver,
    DateTime CreatedAt,
    int TotalLaps,
    TimeSpan? BestLapTime
);

public record LapSummaryDto(
    Guid Id,
    Guid SessionId,
    int LapNumber,
    TimeSpan LapTime,
    double LapDistanceMeters,
    bool IsValidLap,
    bool IsOutLap,
    bool IsInLap,
    string InvalidationReason
);