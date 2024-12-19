using System.Runtime.InteropServices;

namespace TemporalStasis.Connector.Serverbound;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 8)]
public struct PingPacket(uint fingerprint)
{
    [FieldOffset(0)]
    public uint Fingerprint = fingerprint;

    [FieldOffset(4)]
    public uint Timestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public readonly byte[] Generate()
    {
        using var s = new MemoryStream();
        s.WriteStruct(this);
        return s.ToArray();
    }
}
