using System.Runtime.CompilerServices;
using TemporalStasis.Structs;

namespace TemporalStasis.Connector;

public record struct IpcData
{
    public IpcHeader Header;
    public byte[] Data;

    public IpcData(PacketSegment segment)
    {
        Header = segment.Data.ReadStruct<IpcHeader>();
        Data = segment.Data[Unsafe.SizeOf<IpcHeader>()..];
    }

    public readonly T Deserialize<T>() where T : struct
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(Unsafe.SizeOf<T>(), Data.Length, nameof(T));
        return StructExtensions.ReadStruct<T>(Data);
    }
}
