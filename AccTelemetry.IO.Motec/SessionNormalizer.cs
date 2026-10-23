using AccTelemetry.Domain;
using AccTelemetry.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AccTelemetry.IO.Motec;

public static class SessionNormalizer
{
    public static Session BuildSession(LdFile ld, IReadOnlyList<LapBeacon> beacons)
    {
        var speedChan = ld.Find("SPEED");
        var throttleChan = ld.Find("THROTTLE");
        var brakeChan = ld.Find("BRAKE");
        var gearChan = ld.Find("GEAR");
        var steerChan = ld.Find("STEERANGLE");
        var rotyChan = ld.Find("ROTY"); // Captura o Giroscópio

        var rawLaps = new List<(int LapNum, TimeSpan LapTime, double[] Dist, LapTelemetry Telemetry)>();

        for (int i = 1; i < beacons.Count; i++)
        {
            var prevBeacon = beacons[i - 1];
            var curBeacon = beacons[i];

            double startSec = prevBeacon.TimestampSeconds;
            double endSec = curBeacon.TimestampSeconds;
            double lapDuration = endSec - startSec;

            var rawSpeedMs = speedChan?.Slice(startSec, endSec) ?? [];
            var throttle = throttleChan?.Slice(startSec, endSec) ?? [];
            var brake = brakeChan?.Slice(startSec, endSec) ?? [];
            var steer = steerChan?.Slice(startSec, endSec) ?? [];
            var rawRotY = rotyChan?.Slice(startSec, endSec) ?? [];

            if (rawSpeedMs.Length == 0) continue;

            int sampleCount = rawSpeedMs.Length;
            double dt = sampleCount > 1 ? lapDuration / (sampleCount - 1) : 0;

            var speedKmh = new double[sampleCount];
            for (int s = 0; s < sampleCount; s++) speedKmh[s] = rawSpeedMs[s] * 3.6;

            var timeSlice = new double[sampleCount];
            for (int s = 0; s < sampleCount; s++) timeSlice[s] = s * dt;

            var distance = IntegrateDistance(rawSpeedMs, dt);

            var rawGear = gearChan?.Slice(startSec, endSec) ?? [];
            var gearAligned = ResampleStep(rawGear, sampleCount);
            var rotyAligned = ResampleLinear(rawRotY, sampleCount);

            var telemetry = new LapTelemetry
            {
                Time = timeSlice,
                Distance = distance,
                Speed = speedKmh,
                Throttle = throttle,
                Brake = brake,
                Gear = gearAligned,
                SteerAngle = steer,
                YawRate = rotyAligned
            };

            rawLaps.Add((i, curBeacon.LapTime, distance, telemetry));
        }

        double? officialLength = TrackCatalog.GetOfficialLength(ld.Header.Venue);
        double referenceDistance = officialLength ?? CalculateMedianDistance(rawLaps.Select(l => l.Dist.Length > 0 ? l.Dist[^1] : 0));

        var finalLaps = new List<Lap>();
        for (int i = 0; i < rawLaps.Count; i++)
        {
            var (lapNum, lapTime, dist, telemetry) = rawLaps[i];
            double lapDist = dist.Length > 0 ? dist[^1] : 0;
            bool isOutLap = lapNum == 1;
            bool isInLap = false;
            bool isValid = true;
            string reason = "OK";

            if (isOutLap)
            {
                isValid = false; reason = "Out-Lap";
            }
            else if (referenceDistance > 0)
            {
                double diffPercent = Math.Abs(lapDist - referenceDistance) / referenceDistance;
                if (diffPercent > 0.03)
                {
                    isValid = false;
                    if (lapDist < referenceDistance * 0.90) { isInLap = true; reason = "In-Lap"; }
                    else reason = $"Distância anormal";
                }
            }

            finalLaps.Add(new Lap
            {
                LapNumber = lapNum,
                LapTime = lapTime,
                LapDistanceMeters = lapDist,
                IsOutLap = isOutLap,
                IsInLap = isInLap,
                IsValidLap = isValid,
                InvalidationReason = reason,
                Telemetry = telemetry
            });
        }

        return new Session
        {
            Track = ld.Header.Venue,
            Car = ld.Header.Vehicle,
            Driver = ld.Header.Driver,
            CreatedAt = DateTime.Now,
            Laps = finalLaps
        };
    }

    private static double CalculateMedianDistance(IEnumerable<double> distances)
    {
        var list = distances.Where(d => d > 500).OrderBy(d => d).ToList();
        if (list.Count == 0) return 0;
        return list.Count % 2 != 0 ? list[list.Count / 2] : (list[list.Count / 2 - 1] + list[list.Count / 2]) / 2.0;
    }

    private static double[] IntegrateDistance(double[] speedMs, double dt)
    {
        var dist = new double[speedMs.Length];
        for (int i = 1; i < speedMs.Length; i++) dist[i] = dist[i - 1] + (((speedMs[i] + speedMs[i - 1]) / 2.0) * dt);
        return dist;
    }

    private static double[] ResampleStep(double[] src, int targetCount)
    {
        if (src.Length == 0) return new double[targetCount];
        var dst = new double[targetCount];
        for (int i = 0; i < targetCount; i++) dst[i] = src[Math.Clamp((int)Math.Round((double)i / (targetCount - 1) * (src.Length - 1)), 0, src.Length - 1)];
        return dst;
    }

    private static double[] ResampleLinear(double[] src, int targetCount)
    {
        if (src.Length == 0) return new double[targetCount];
        var dst = new double[targetCount];
        for (int i = 0; i < targetCount; i++)
        {
            double t = (double)i / (targetCount - 1) * (src.Length - 1);
            int i0 = (int)Math.Floor(t);
            int i1 = Math.Min(i0 + 1, src.Length - 1);
            dst[i] = src[i0] + (src[i1] - src[i0]) * (t - i0);
        }
        return dst;
    }
}