using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using TemporalStasis.Encryption;

namespace TemporalStasis.Connector;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 0x2C)]
public unsafe struct BrokefishKey
{
    public uint Magic;

    public uint Key;

    public uint Version;

    public unsafe fixed byte KeyPhrase[32];

    public BrokefishKey(string keyPhrase, uint key, uint version)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(keyPhrase.Length, 32);

        Magic = 0x12345678;

        Key = key;
        Version = version;

        fixed (byte* p = KeyPhrase)
        {
            var span = new Span<byte>(p, 32);
            Encoding.ASCII.GetBytes(keyPhrase, span);
        }
    }

    public readonly Brokefish Create()
    {
        using var s = new MemoryStream();
        s.WriteStruct(this);
        var hash = MD5.HashData(s.ToArray());
        Console.WriteLine($"KEY: {Convert.ToHexString(hash)}");
        return new(hash);
    }
}
