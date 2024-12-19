using System.Runtime.CompilerServices;
using TemporalStasis.Structs;

namespace TemporalStasis.Connector;

public record SendablePacketSegment
{
    public uint SourceActor { get; init; }
    public uint TargetActor { get; init; }
    public required SegmentType SegmentType { get; init; }
    public required byte[] Payload { get; init; }

    public byte[] Generate()
    {
        using var s = new MemoryStream();
        var header = new PacketSegmentHeader()
        {
            Size = (uint)(Unsafe.SizeOf<PacketSegmentHeader>() + Payload.Length),
            SourceActor = SourceActor,
            TargetActor = TargetActor,
            SegmentType = SegmentType
        };
        s.WriteStruct(header);
        s.Write(Payload);
        return s.ToArray();
    }
}
