namespace AccTelemetry.Domain.Models;

public sealed class Session
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Track { get; init; } = "";
    public string Car { get; init; } = "";
    public string Driver { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public IReadOnlyList<Lap> Laps { get; init; } = [];
}