using AccTelemetry.Domain.Models;

namespace AccTelemetry.IO.Motec;

public static class LdLapExtensions
{
    /// <summary>
    /// Retorna as amostras do canal entre startSec (inclusive) e endSec (exclusive),
    /// em segundos desde o início do log.
    /// </summary>
    public static double[] Slice(this LdChannel channel, double startSec, double endSec)
    {
        if (channel.Frequency <= 0 || channel.Data.Length == 0 || endSec <= startSec)
            return [];

        var first = Math.Clamp((int)Math.Ceiling(startSec * channel.Frequency), 0, channel.Data.Length);
        var last = Math.Clamp((int)Math.Ceiling(endSec * channel.Frequency), 0, channel.Data.Length);

        return last > first ? channel.Data[first..last] : [];
    }

    /// <summary>
    /// Fatia um canal por volta usando as beacons do LDX.
    /// A volta N vai da beacon N-1 até a beacon N (a beacon 0 é a saída dos boxes).
    /// </summary>
    public static IEnumerable<(LapBeacon Lap, double[] Samples)> SliceByLap(
        this LdChannel channel, IReadOnlyList<LapBeacon> beacons)
    {
        for (var i = 1; i < beacons.Count; i++)
            yield return (beacons[i],
                channel.Slice(beacons[i - 1].TimestampSeconds, beacons[i].TimestampSeconds));
    }
}