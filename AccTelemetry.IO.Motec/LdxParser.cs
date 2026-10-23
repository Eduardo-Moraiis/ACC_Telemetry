using AccTelemetry.Domain.Models;
using System.Globalization;
using System.Xml.Linq;

namespace AccTelemetry.IO.Motec;

public class LdxParser
{
    public List<LapBeacon> ParseBeacons(string ldxFilePath)
    {
        if (!File.Exists(ldxFilePath))
            throw new FileNotFoundException($"Arquivo LDX não encontrado: {ldxFilePath}");

        var doc = XDocument.Load(ldxFilePath);

        var beaconGroup = doc.Descendants("MarkerGroup")
            .FirstOrDefault(g => (string?)g.Attribute("Name") == "Beacons");

        if (beaconGroup == null)
            return [];

        var timestamps = beaconGroup.Elements("Marker")
            .Select(m => (string?)m.Attribute("Time"))
            .Where(t => !string.IsNullOrEmpty(t))
            .Select(t => double.Parse(t!, CultureInfo.InvariantCulture) / 1_000_000.0)
            .OrderBy(t => t)
            .ToList();

        var laps = new List<LapBeacon>();

        for (int i = 0; i < timestamps.Count; i++)
        {
            double currentSec = timestamps[i];
            TimeSpan lapDuration = TimeSpan.Zero;

            if (i > 0)
            {
                double prevSec = timestamps[i - 1];
                lapDuration = TimeSpan.FromSeconds(currentSec - prevSec);
            }

            laps.Add(new LapBeacon(
                LapNumber: i,
                TimestampSeconds: currentSec,
                LapTime: lapDuration
            ));
        }

        return laps;
    }
}