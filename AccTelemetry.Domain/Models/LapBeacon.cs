using System;
using System.Collections.Generic;
using System.Text;

namespace AccTelemetry.Domain.Models;

public record LapBeacon(
    int LapNumber,
    double TimestampSeconds,
    TimeSpan LapTime
);
