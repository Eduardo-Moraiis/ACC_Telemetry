namespace AccTelemetry.Domain.Models;

public sealed class LdHeader
{
    public uint Serial { get; init; }
    public string DeviceType { get; init; } = "";
    public ushort DeviceVersion { get; init; }
    public uint NumChannels { get; init; }
    public string Date { get; init; } = "";
    public string Time { get; init; } = "";
    public string Driver { get; init; } = "";
    public string Vehicle { get; init; } = "";
    public string Venue { get; init; } = "";
    public string ShortComment { get; init; } = "";
    public string Event { get; init; } = "";
    public string Session { get; init; } = "";

    // Vindos da cadeia evento -> venue -> veículo
    public string EventComment { get; init; } = "";
    public string VehicleType { get; init; } = "";
    public uint VehicleWeight { get; init; }
}

public sealed class LdChannel
{
    public string Name { get; init; } = "";
    public string ShortName { get; init; } = "";
    public string Unit { get; init; } = "";
    public int Frequency { get; init; }
    public double[] Data { get; init; } = Array.Empty<double>();

    /// <summary>Tempo (s) da amostra i.</summary>
    public double TimeAt(int i) => Frequency > 0 ? (double)i / Frequency : 0;
}

public sealed class LdFile
{
    public LdHeader Header { get; init; } = new();
    public IReadOnlyList<LdChannel> Channels { get; init; } = Array.Empty<LdChannel>();

    public LdChannel? Find(string name) =>
        Channels.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}