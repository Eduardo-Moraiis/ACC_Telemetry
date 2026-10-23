using AccTelemetry.Domain.Models;
using AccTelemetry.Domain.Repositories;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AccTelemetry.IO.Persistence;

public sealed class SqliteTelemetryRepository : ITelemetryRepository
{
    private readonly string _connectionString;

    public SqliteTelemetryRepository(string dbPath = "telemetry.db")
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var pragmaCmd = conn.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON;";
        await pragmaCmd.ExecuteNonQueryAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Sessions (Id TEXT PRIMARY KEY, Track TEXT NOT NULL, Car TEXT NOT NULL, Driver TEXT NOT NULL, CreatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Laps (Id TEXT PRIMARY KEY, SessionId TEXT NOT NULL, LapNumber INTEGER NOT NULL, LapTimeMs REAL NOT NULL, LapDistanceMeters REAL NOT NULL, IsValidLap INTEGER NOT NULL, IsOutLap INTEGER NOT NULL, IsInLap INTEGER NOT NULL, InvalidationReason TEXT, FOREIGN KEY (SessionId) REFERENCES Sessions(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS LapTelemetry (LapId TEXT PRIMARY KEY, SampleCount INTEGER NOT NULL, Time BLOB NOT NULL, Distance BLOB NOT NULL, Speed BLOB NOT NULL, Throttle BLOB NOT NULL, Brake BLOB NOT NULL, Gear BLOB NOT NULL, SteerAngle BLOB NOT NULL, FOREIGN KEY (LapId) REFERENCES Laps(Id) ON DELETE CASCADE);
        ";
        await cmd.ExecuteNonQueryAsync();

        // MIGRATION SEAMLESS: Adiciona a coluna YawRate se ela não existir
        try
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE LapTelemetry ADD COLUMN YawRate BLOB;";
            await alterCmd.ExecuteNonQueryAsync();
        }
        catch { /* Coluna já existe, segue o jogo */ }
    }

    public async Task SaveSessionAsync(Session session)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        using var sessionCmd = conn.CreateCommand();
        sessionCmd.Transaction = tx;
        sessionCmd.CommandText = "INSERT OR REPLACE INTO Sessions (Id, Track, Car, Driver, CreatedAt) VALUES (@Id, @Track, @Car, @Driver, @CreatedAt);";
        sessionCmd.Parameters.AddWithValue("@Id", session.Id.ToString());
        sessionCmd.Parameters.AddWithValue("@Track", session.Track);
        sessionCmd.Parameters.AddWithValue("@Car", session.Car);
        sessionCmd.Parameters.AddWithValue("@Driver", session.Driver);
        sessionCmd.Parameters.AddWithValue("@CreatedAt", session.CreatedAt.ToString("o"));
        await sessionCmd.ExecuteNonQueryAsync();

        using var lapCmd = conn.CreateCommand();
        lapCmd.Transaction = tx;
        lapCmd.CommandText = "INSERT OR REPLACE INTO Laps (Id, SessionId, LapNumber, LapTimeMs, LapDistanceMeters, IsValidLap, IsOutLap, IsInLap, InvalidationReason) VALUES (@Id, @SessionId, @LapNumber, @LapTimeMs, @LapDistanceMeters, @IsValidLap, @IsOutLap, @IsInLap, @InvalidationReason);";
        var pLapId = lapCmd.Parameters.Add("@Id", SqliteType.Text);
        var pSessionId = lapCmd.Parameters.Add("@SessionId", SqliteType.Text);
        var pLapNumber = lapCmd.Parameters.Add("@LapNumber", SqliteType.Integer);
        var pLapTimeMs = lapCmd.Parameters.Add("@LapTimeMs", SqliteType.Real);
        var pLapDist = lapCmd.Parameters.Add("@LapDistanceMeters", SqliteType.Real);
        var pIsValid = lapCmd.Parameters.Add("@IsValidLap", SqliteType.Integer);
        var pIsOut = lapCmd.Parameters.Add("@IsOutLap", SqliteType.Integer);
        var pIsIn = lapCmd.Parameters.Add("@IsInLap", SqliteType.Integer);
        var pReason = lapCmd.Parameters.Add("@InvalidationReason", SqliteType.Text);

        using var telemCmd = conn.CreateCommand();
        telemCmd.Transaction = tx;
        telemCmd.CommandText = "INSERT OR REPLACE INTO LapTelemetry (LapId, SampleCount, Time, Distance, Speed, Throttle, Brake, Gear, SteerAngle, YawRate) VALUES (@LapId, @SampleCount, @Time, @Distance, @Speed, @Throttle, @Brake, @Gear, @SteerAngle, @YawRate);";
        var pTId = telemCmd.Parameters.Add("@LapId", SqliteType.Text);
        var pTCount = telemCmd.Parameters.Add("@SampleCount", SqliteType.Integer);
        var pTTime = telemCmd.Parameters.Add("@Time", SqliteType.Blob);
        var pTDist = telemCmd.Parameters.Add("@Distance", SqliteType.Blob);
        var pTSpeed = telemCmd.Parameters.Add("@Speed", SqliteType.Blob);
        var pTThrottle = telemCmd.Parameters.Add("@Throttle", SqliteType.Blob);
        var pTBrake = telemCmd.Parameters.Add("@Brake", SqliteType.Blob);
        var pTGear = telemCmd.Parameters.Add("@Gear", SqliteType.Blob);
        var pTSteer = telemCmd.Parameters.Add("@SteerAngle", SqliteType.Blob);
        var pTYaw = telemCmd.Parameters.Add("@YawRate", SqliteType.Blob);

        foreach (var lap in session.Laps)
        {
            pLapId.Value = lap.Id.ToString();
            pSessionId.Value = session.Id.ToString();
            pLapNumber.Value = lap.LapNumber;
            pLapTimeMs.Value = lap.LapTime.TotalMilliseconds;
            pLapDist.Value = lap.LapDistanceMeters;
            pIsValid.Value = lap.IsValidLap ? 1 : 0;
            pIsOut.Value = lap.IsOutLap ? 1 : 0;
            pIsIn.Value = lap.IsInLap ? 1 : 0;
            pReason.Value = lap.InvalidationReason ?? "";
            await lapCmd.ExecuteNonQueryAsync();

            pTId.Value = lap.Id.ToString();
            pTCount.Value = lap.Telemetry.SampleCount;
            pTTime.Value = BinaryBlobHelper.ToBytes(lap.Telemetry.Time);
            pTDist.Value = BinaryBlobHelper.ToBytes(lap.Telemetry.Distance);
            pTSpeed.Value = BinaryBlobHelper.ToBytes(lap.Telemetry.Speed);
            pTThrottle.Value = BinaryBlobHelper.ToBytes(lap.Telemetry.Throttle);
            pTBrake.Value = BinaryBlobHelper.ToBytes(lap.Telemetry.Brake);
            pTGear.Value = BinaryBlobHelper.ToBytes(lap.Telemetry.Gear);
            pTSteer.Value = BinaryBlobHelper.ToBytes(lap.Telemetry.SteerAngle);
            pTYaw.Value = BinaryBlobHelper.ToBytes(lap.Telemetry.YawRate);
            await telemCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    public async Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT s.Id, s.Track, s.Car, s.Driver, s.CreatedAt, COUNT(l.Id), MIN(CASE WHEN l.IsValidLap = 1 THEN l.LapTimeMs ELSE NULL END) FROM Sessions s LEFT JOIN Laps l ON l.SessionId = s.Id GROUP BY s.Id ORDER BY s.CreatedAt DESC;";

        var list = new List<SessionSummaryDto>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var bestMs = reader.IsDBNull(6) ? (double?)null : reader.GetDouble(6);
            list.Add(new SessionSummaryDto(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), DateTime.Parse(reader.GetString(4)), reader.GetInt32(5), bestMs.HasValue ? TimeSpan.FromMilliseconds(bestMs.Value) : null));
        }
        return list;
    }

    public async Task<IReadOnlyList<LapSummaryDto>> GetLapsBySessionAsync(Guid sessionId)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, SessionId, LapNumber, LapTimeMs, LapDistanceMeters, IsValidLap, IsOutLap, IsInLap, InvalidationReason FROM Laps WHERE SessionId = @SessionId ORDER BY LapNumber ASC;";
        cmd.Parameters.AddWithValue("@SessionId", sessionId.ToString());

        var laps = new List<LapSummaryDto>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            laps.Add(new LapSummaryDto(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetInt32(2), TimeSpan.FromMilliseconds(reader.GetDouble(3)), reader.GetDouble(4), reader.GetInt32(5) == 1, reader.GetInt32(6) == 1, reader.GetInt32(7) == 1, reader.IsDBNull(8) ? "" : reader.GetString(8)));
        }
        return laps;
    }

    public async Task<LapTelemetry?> GetLapTelemetryAsync(Guid lapId)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Time, Distance, Speed, Throttle, Brake, Gear, SteerAngle, YawRate FROM LapTelemetry WHERE LapId = @LapId;";
        cmd.Parameters.AddWithValue("@LapId", lapId.ToString());

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new LapTelemetry
        {
            Time = BinaryBlobHelper.ToDoubles((byte[])reader["Time"]),
            Distance = BinaryBlobHelper.ToDoubles((byte[])reader["Distance"]),
            Speed = BinaryBlobHelper.ToDoubles((byte[])reader["Speed"]),
            Throttle = BinaryBlobHelper.ToDoubles((byte[])reader["Throttle"]),
            Brake = BinaryBlobHelper.ToDoubles((byte[])reader["Brake"]),
            Gear = BinaryBlobHelper.ToDoubles((byte[])reader["Gear"]),
            SteerAngle = BinaryBlobHelper.ToDoubles((byte[])reader["SteerAngle"]),
            YawRate = reader.IsDBNull(7) ? [] : BinaryBlobHelper.ToDoubles((byte[])reader["YawRate"])
        };
    }
}