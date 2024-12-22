using System.Runtime.InteropServices;
using System.Text;

namespace TemporalStasis.Connector.Serverbound;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 1144)]
public unsafe struct LoginExPacket
{
    public uint RequestNumber;
    public uint Unknown4;
    public ushort Unknown8;
    public ushort LobbyLoginMode;
    public ushort Unknown12;
    public ushort Unknown14;
    public ushort Unknown16;
    public unsafe fixed byte SessionId[64];
    public unsafe fixed byte VersionData[192];

    public LoginExPacket(uint reqNumber, ushort version, string sessionId, bool isSteam, FileReport executable, string[] exVersions)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(sessionId.Length, 32);

        RequestNumber = reqNumber;
        Unknown4 = 0; // ClientTimeValue
        Unknown8 = 1000; // ClientLangCode
        LobbyLoginMode = version;
        Unknown12 = 4944;
        Unknown14 = 18; // PlatformType (?)
        Unknown16 = (ushort)(isSteam ? 1 : 0); // PlatformMode

        fixed (byte* p = SessionId)
        {
            var span = new Span<byte>(p, 64);
            Encoding.ASCII.GetBytes(sessionId, span);
        }

        var s = new StringBuilder();
        s.Append(executable.Report);
        foreach (var ver in exVersions)
        {
            s.Append('+');
            s.Append(ver[..15]); // Truncate the "part" section
        }
        fixed (byte* p = VersionData)
        {
            var span = new Span<byte>(p, 192);
            Encoding.ASCII.GetBytes(s.ToString(), span);
        }
    }

    public readonly byte[] Generate()
    {
        using var s = new MemoryStream();
        s.WriteStruct(this);
        return s.ToArray();
    }
}
