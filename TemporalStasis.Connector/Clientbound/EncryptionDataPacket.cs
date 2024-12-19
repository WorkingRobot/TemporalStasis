using System.Runtime.InteropServices;

namespace TemporalStasis.Connector.Clientbound;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 640)]
public unsafe struct EncryptionDataPacket
{
    [FieldOffset(0)]
    public uint Fingerprint;
}
