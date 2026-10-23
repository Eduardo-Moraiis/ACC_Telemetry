namespace AccTelemetry.Domain.Models;

public sealed class LapTelemetry
{
    public int SampleCount => Time.Length;

    /// <summary>Tempo decorrido na volta em segundos (0.0 até LapTime.TotalSeconds).</summary>
    public double[] Time { get; init; } = [];

    /// <summary>Eixo X principal: metros percorridos desde a linha de largada (0.0 até ~5800m).</summary>
    public double[] Distance { get; init; } = [];

    /// <summary>Velocidade em km/h.</summary>
    public double[] Speed { get; init; } = [];

    /// <summary>Acelerador de 0 a 100%.</summary>
    public double[] Throttle { get; init; } = [];

    /// <summary>Freio de 0 a 100%.</summary>
    public double[] Brake { get; init; } = [];

    /// <summary>Marcha engatada (-1 Ré, 0 Neutro, 1 a 6 marchas à frente).</summary>
    public double[] Gear { get; init; } = [];

    /// <summary>Ângulo de esterço do volante em graus.</summary>
    public double[] SteerAngle { get; init; } = [];

    /// <summary>Rotação do veículo</summary>
    public double[] YawRate { get; init; } = [];
}