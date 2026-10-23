using System.Buffers.Binary;
using System.Text;
using AccTelemetry.Domain.Models;

namespace AccTelemetry.IO.Motec;

/// <summary>
/// Parser de arquivos MoTeC .ld (telemetria do ACC / i2 Pro).
/// Layout baseado na engenharia reversa do formato (projeto open-source gotzl/ldparser).
/// Tudo little-endian.
/// </summary>
public static class LdParser
{
    // Offsets do cabeçalho
    private const int OffMarker = 0;
    private const int OffMetaPtr = 8;
    private const int OffDataPtr = 12;
    private const int OffEventPtr = 36;
    private const int OffSerial = 70;
    private const int OffDeviceType = 74;   // 8 bytes
    private const int OffDeviceVersion = 82;
    private const int OffNumChannels = 86;
    private const int OffDate = 94;         // 16 bytes
    private const int OffTime = 126;        // 16 bytes
    private const int OffDriver = 158;      // 64 bytes
    private const int OffVehicle = 222;     // 64 bytes
    private const int OffVenue = 350;       // 64 bytes
    private const int OffShortComment = 1572; // 64 bytes
    private const int OffEvent = 1762;      // 64 bytes
    private const int OffSession = 1826;    // 64 bytes
    private const int HeaderSize = 1890;

    // Bloco de evento: name[64] session[64] comment[1024] venue_ptr(u16)
    private const int EvSession = 64;
    private const int EvComment = 128;
    private const int EvVenuePtr = 1152;
    private const int EventSize = 1154;

    // Bloco de venue: name[64] pad[1034] vehicle_ptr(u16)
    private const int VenVehiclePtr = 1098;
    private const int VenueSize = 1100;

    // Bloco de veículo: id[64] pad[128] weight(u32) type[32] comment[32]
    private const int VehWeight = 192;
    private const int VehType = 196;
    private const int VehicleSize = 260;

    private const int ChannelSize = 124;

    private static readonly Encoding Latin1 = Encoding.Latin1;

    public static LdFile Parse(string path) => Parse(System.IO.File.ReadAllBytes(path));

    public static LdFile Parse(byte[] buf)
    {
        if (buf.Length < HeaderSize)
            throw new InvalidDataException("Arquivo menor que o cabeçalho .ld.");

        var marker = U32(buf, OffMarker);
        if (marker != 0x40)
            throw new InvalidDataException($"Marcador inesperado 0x{marker:X}; não parece um .ld válido.");

        var metaPtr = U32(buf, OffMetaPtr);
        var eventPtr = (int)U32(buf, OffEventPtr);

        // Campos do cabeçalho principal
        var driver = Str(buf, OffDriver, 64);
        var vehicle = Str(buf, OffVehicle, 64);
        var venue = Str(buf, OffVenue, 64);
        var eventName = Str(buf, OffEvent, 64);
        var session = Str(buf, OffSession, 64);
        var eventComment = "";
        var vehicleType = "";
        uint vehicleWeight = 0;

        // Cadeia evento -> venue -> veículo (o ACC costuma preencher aqui)
        if (eventPtr > 0 && eventPtr + EventSize <= buf.Length)
        {
            var evName = Str(buf, eventPtr, 64);
            var evSession = Str(buf, eventPtr + EvSession, 64);
            eventComment = Str(buf, eventPtr + EvComment, 1024);
            if (string.IsNullOrEmpty(eventName)) eventName = evName;
            if (string.IsNullOrEmpty(session)) session = evSession;

            var venuePtr = U16(buf, eventPtr + EvVenuePtr);
            if (venuePtr > 0 && venuePtr + VenueSize <= buf.Length)
            {
                var venName = Str(buf, venuePtr, 64);
                if (string.IsNullOrEmpty(venue)) venue = venName;

                var vehPtr = U16(buf, venuePtr + VenVehiclePtr);
                if (vehPtr > 0 && vehPtr + VehicleSize <= buf.Length)
                {
                    var vehId = Str(buf, vehPtr, 64);
                    if (string.IsNullOrEmpty(vehicle)) vehicle = vehId;
                    vehicleWeight = U32(buf, vehPtr + VehWeight);
                    vehicleType = Str(buf, vehPtr + VehType, 32);
                }
            }
        }

        var header = new LdHeader
        {
            Serial = U32(buf, OffSerial),
            DeviceType = Str(buf, OffDeviceType, 8),
            DeviceVersion = U16(buf, OffDeviceVersion),
            NumChannels = U32(buf, OffNumChannels),
            Date = Str(buf, OffDate, 16),
            Time = Str(buf, OffTime, 16),
            Driver = driver,
            Vehicle = vehicle,
            Venue = venue,
            ShortComment = Str(buf, OffShortComment, 64),
            Event = eventName,
            Session = session,
            EventComment = eventComment,
            VehicleType = vehicleType,
            VehicleWeight = vehicleWeight,
        };

        var channels = new List<LdChannel>();
        var seen = new HashSet<uint>();
        var addr = metaPtr;

        // Lista encadeada de canais; 'seen' protege contra ciclos em arquivo corrompido.
        while (addr != 0 && seen.Add(addr))
        {
            if (addr + ChannelSize > buf.Length)
                throw new InvalidDataException($"Ponteiro de canal fora do arquivo: 0x{addr:X}");

            channels.Add(ReadChannel(buf, (int)addr, out addr));
        }

        return new LdFile { Header = header, Channels = channels };
    }

    private static LdChannel ReadChannel(byte[] buf, int a, out uint next)
    {
        next = U32(buf, a + 4);
        var dataPtr = (int)U32(buf, a + 8);
        var nData = (int)U32(buf, a + 12);
        var dtypeA = U16(buf, a + 18);
        var dtype = U16(buf, a + 20);
        var freq = U16(buf, a + 22);
        var shift = I16(buf, a + 24);
        var mul = I16(buf, a + 26);
        var scale = I16(buf, a + 28);
        var dec = I16(buf, a + 30);

        // 'dtype' costuma ser o nº de bytes (2/4), mas alguns arquivos usam índice (1/3).
        var nBytes = dtype switch { 1 => 2, 3 => 4, _ => dtype };
        var isFloat = dtypeA == 0x07;

        if (nBytes is not (2 or 4) || (!isFloat && dtypeA is not (0 or 3 or 5)))
            throw new NotSupportedException(
                $"Tipo de dado não suportado (dtype_a=0x{dtypeA:X}, dtype={dtype}) no canal '{Str(buf, a + 32, 32)}'.");

        if (dataPtr < 0 || (long)dataPtr + (long)nData * nBytes > buf.Length)
            throw new InvalidDataException($"Dados do canal '{Str(buf, a + 32, 32)}' fora do arquivo.");

        var data = new double[nData];
        var span = buf.AsSpan();

        if (isFloat)
        {
            for (var i = 0; i < nData; i++)
            {
                var off = dataPtr + i * nBytes;
                data[i] = nBytes == 2
                    ? (double)BinaryPrimitives.ReadHalfLittleEndian(span[off..])
                    : BinaryPrimitives.ReadSingleLittleEndian(span[off..]);
            }
        }
        else
        {
            var s = scale == 0 ? 1.0 : scale;
            var m = mul == 0 ? 1.0 : mul;
            var pow = Math.Pow(10, -dec);
            for (var i = 0; i < nData; i++)
            {
                var off = dataPtr + i * nBytes;
                double raw = nBytes == 2
                    ? BinaryPrimitives.ReadInt16LittleEndian(span[off..])
                    : BinaryPrimitives.ReadInt32LittleEndian(span[off..]);
                data[i] = (raw / s * pow + shift) * m;
            }
        }

        return new LdChannel
        {
            Name = Str(buf, a + 32, 32),
            ShortName = Str(buf, a + 64, 8),
            Unit = Str(buf, a + 72, 12),
            Frequency = freq,
            Data = data,
        };
    }

    private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    private static ushort U16(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));
    private static short I16(byte[] b, int o) => BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(o));

    private static string Str(byte[] b, int o, int len)
    {
        var span = b.AsSpan(o, len);
        var z = span.IndexOf((byte)0);
        if (z >= 0) span = span[..z];
        return Latin1.GetString(span).Trim();
    }
}