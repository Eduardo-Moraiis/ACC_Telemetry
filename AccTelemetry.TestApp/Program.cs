using AccTelemetry.Domain.Models;
using AccTelemetry.IO.Motec;
using AccTelemetry.IO.Persistence;
using System.Diagnostics;

Console.WriteLine("==================================================");
Console.WriteLine(" ACC Telemetry Reader - Validação Fases 2 e 3     ");
Console.WriteLine("==================================================\n");

string motecFolder = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
    "Assetto Corsa Competizione",
    "MoTeC"
);

if (!Directory.Exists(motecFolder))
{
    Console.WriteLine($"Pasta do MoTeC não encontrada em: {motecFolder}");
    return;
}

var latestLdxFile = new DirectoryInfo(motecFolder)
    .GetFiles("*.ldx")
    .OrderByDescending(f => f.LastWriteTime)
    .FirstOrDefault();

if (latestLdxFile == null)
{
    Console.WriteLine("Nenhum arquivo .ldx encontrado.");
    return;
}

var ldPath = Path.ChangeExtension(latestLdxFile.FullName, ".ld");
var ldxParser = new LdxParser();
var beacons = ldxParser.ParseBeacons(latestLdxFile.FullName);
var ldFile = LdParser.Parse(ldPath);

// Fase 2: Normalização
var session = SessionNormalizer.BuildSession(ldFile, beacons);
Console.WriteLine($"Sessão processada: {session.Track} | {session.Car} | {session.Laps.Count} voltas");

// ----------------------------------------------------------------------
// Fase 3: Persistência no SQLite
// ----------------------------------------------------------------------
Console.WriteLine("\n[Fase 3] Inicializando banco SQLite (telemetry.db)...");
var repo = new SqliteTelemetryRepository("telemetry.db");
await repo.InitializeAsync();

var sw = Stopwatch.StartNew();
await repo.SaveSessionAsync(session);
sw.Stop();
Console.WriteLine($"✔ Sessão gravada no SQLite com sucesso em {sw.ElapsedMilliseconds} ms!");

// Teste de Leitura: Buscar lista de sessões cadastradas
var sessionsInDb = await repo.GetSessionsAsync();
Console.WriteLine($"\nTotal de sessões no banco: {sessionsInDb.Count}");
foreach (var s in sessionsInDb)
{
    Console.WriteLine($"  - Pista: {s.Track} | Carro: {s.Car} | Voltas: {s.TotalLaps} | Melhor: {s.BestLapTime:mm\\:ss\\.fff}");
}

// Teste de Leitura dos BLOBs: Puxar a telemetria da volta 3 do banco
var bestLap = session.Laps.FirstOrDefault(l => l.IsValidLap);
if (bestLap != null)
{
    sw.Restart();
    var loadedTelemetry = await repo.GetLapTelemetryAsync(bestLap.Id);
    sw.Stop();

    Console.WriteLine($"\n✔ Telemetria da Volta {bestLap.LapNumber} recuperada do SQLite em {sw.Elapsed.TotalMilliseconds:F2} ms!");
    Console.WriteLine($"  - Total de amostras carregadas: {loadedTelemetry?.SampleCount}");
    Console.WriteLine($"  - V.Max lida do BLOB: {loadedTelemetry?.Speed.Max():F1} km/h");
    Console.WriteLine($"  - Distância final lida do BLOB: {loadedTelemetry?.Distance[^1]:F1} m");
}

Console.WriteLine("\n[Fases 2 e 3 Concluídas!] Pressione qualquer tecla para sair...");
Console.ReadKey();