using System.Runtime.InteropServices;

namespace TemporalStasis.Connector.Serverbound;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 24)]
public unsafe struct ServiceLoginPacket
{
    public uint RequestNumber;
    public uint Unknown4;
    public byte AccountIdx;
    public byte LoginParam1ProtoVersion;
    public ushort LoginParam2;
    public uint Unused12;
    public ulong AccountId;

    public readonly byte LoginParam1 => (byte)(LoginParam1ProtoVersion & 0xF);
    public readonly byte ProtoVersion => (byte)(LoginParam1ProtoVersion >> 4);

    public ServiceLoginPacket(uint reqNumber, byte accountIdx, ulong accountId)
    {
        RequestNumber = reqNumber;
        LoginParam1ProtoVersion = 1; // LoginParam1 = 1, ProtoVersion = 0
        AccountId = accountId;
    }

    public readonly byte[] Generate()
    {
        using var s = new MemoryStream();
        s.WriteStruct(this);
        return s.ToArray();
    }
}
