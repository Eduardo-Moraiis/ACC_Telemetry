namespace AccTelemetry.Domain.Models;

public sealed class Lap
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int LapNumber { get; init; }
    public TimeSpan LapTime { get; init; }
    public double LapDistanceMeters { get; init; }

    public bool IsOutLap { get; init; }
    public bool IsInLap { get; init; }
    public bool IsValidLap { get; init; }
    public string InvalidationReason { get; init; } = string.Empty;

    public LapTelemetry Telemetry { get; init; } = new();
}