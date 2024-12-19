using System.Runtime.CompilerServices;
using TemporalStasis.Structs;

namespace TemporalStasis.Connector;

public record SendablePacket
{
    public bool SendMagic { get; set; }

    public bool SendTimestamp { get; set; }

    public required ConnectionType ConnectionType { get; set; }

    public required List<SendablePacketSegment> Segments { get; set; }

    private const ulong HEADER_1 = 0xE2465DFF41A05252;
    private const ulong HEADER_2 = 0x75C4997B4D642A7F;

    public List<byte[]> Generate()
    {
        var segments = Segments.Select(s => s.Generate());
        var segmentSizes = segments.Sum(s => s.Length);

        using var s = new MemoryStream();
        var header = new PacketHeader()
        {
            Unknown0 = SendMagic ? HEADER_1 : 0,
            Unknown8 = SendMagic ? HEADER_2 : 0,
            Timestamp = SendTimestamp ? (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : 0,
            Size = (uint)(Unsafe.SizeOf<PacketHeader>() + segmentSizes),
            ConnectionType = ConnectionType,
            Count = (ushort)Segments.Count,
            Unknown20 = 1,
            CompressionType = CompressionType.None,
            UncompressedSize = 0//(uint)(Unsafe.SizeOf<PacketHeader>() + segmentSizes)
        };
        s.WriteStruct(header);
        return [s.ToArray(), .. segments];
    }
}
