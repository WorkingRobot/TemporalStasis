using System.Runtime.InteropServices;
using System.Text;

namespace TemporalStasis.Connector.Serverbound;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 616)]
public unsafe struct EncryptionInitPacket
{
    [FieldOffset(36)]
    public unsafe fixed byte KeyPhrase[32];

    [FieldOffset(100)]
    public uint Key;

    public EncryptionInitPacket(string keyPhrase, uint key)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(keyPhrase.Length, 32);

        fixed (byte* p = KeyPhrase)
        {
            var span = new Span<byte>(p, 32);
            Encoding.ASCII.GetBytes(keyPhrase, span);
        }

        Key = key;
    }

    public readonly byte[] Generate()
    {
        using var s = new MemoryStream();
        s.WriteStruct(this);
        return s.ToArray();
    }
}
