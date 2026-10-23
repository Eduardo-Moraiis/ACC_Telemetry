using System.Runtime.InteropServices;

namespace AccTelemetry.IO.Persistence;

public static class BinaryBlobHelper
{
    /// <summary>
    /// Converte um array de double[] diretamente para bytes brutos.
    /// </summary>
    public static byte[] ToBytes(double[]? values)
    {
        if (values == null || values.Length == 0)
            return [];

        // Converte a memória do double[] diretamente para Span de bytes
        var byteSpan = MemoryMarshal.AsBytes(values.AsSpan());
        return byteSpan.ToArray();
    }

    /// <summary>
    /// Reconverte os bytes brutos do SQLite de volta para double[] de forma instantânea.
    /// </summary>
    public static double[] ToDoubles(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return [];

        var doubleSpan = MemoryMarshal.Cast<byte, double>(bytes.AsSpan());
        return doubleSpan.ToArray();
    }
}