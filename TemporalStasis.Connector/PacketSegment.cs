using System.Runtime.CompilerServices;
using TemporalStasis.Encryption;
using TemporalStasis.Structs;

namespace TemporalStasis.Connector;

public record struct PacketSegment
{
    public PacketSegmentHeader Header;
    public byte[] Data;

    public PacketSegment(Stream stream)
    {
        Header = stream.ReadStruct<PacketSegmentHeader>();
        Data = stream.ReadBytes((int)(Header.Size - Unsafe.SizeOf<PacketSegmentHeader>()));
    }

    public void Decrypt(Brokefish brokefish) =>
        brokefish.DecipherPadded(Data);

    public readonly T Deserialize<T>() where T : struct
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(Unsafe.SizeOf<T>(), Data.Length, nameof(T));
        return StructExtensions.ReadStruct<T>(Data);
    }
}
