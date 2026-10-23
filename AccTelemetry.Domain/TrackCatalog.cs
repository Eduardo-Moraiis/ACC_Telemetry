namespace AccTelemetry.Domain;

public static class TrackCatalog
{
    // Dicionário com extensões oficiais aproximadas (em metros) das pistas do ACC
    private static readonly Dictionary<string, double> Tracks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["monza"] = 5793,
        ["spa"] = 7004,
        ["silverstone"] = 5891,
        ["nurburgring"] = 5148,
        ["nurburgring_24h"] = 25378,
        ["brands_hatch"] = 3916,
        ["brands_hatch_indy"] = 1944,
        ["zolder"] = 4010,
        ["misano"] = 4226,
        ["paul_ricard"] = 5822,
        ["hungaroring"] = 4381,
        ["barcelona"] = 4675,
        ["kyalami"] = 4529,
        ["mount_panorama"] = 6213,
        ["suzuka"] = 5807,
        ["laguna_seca"] = 3602,
        ["donington"] = 4020,
        ["oulton_park"] = 4307,
        ["snetterton"] = 4779,
        ["watkins_glen"] = 5430,
        ["cota"] = 5513,
        ["indianapolis"] = 3925,
        ["valencia"] = 4005,
        ["red_bull_ring"] = 4318
    };

    public static double? GetOfficialLength(string venue)
    {
        if (string.IsNullOrWhiteSpace(venue)) return null;

        // Procura por correspondência exata ou parcial (ex: "monza", "monza_2020")
        foreach (var (key, length) in Tracks)
        {
            if (venue.Contains(key, StringComparison.OrdinalIgnoreCase))
                return length;
        }

        return null;
    }
}