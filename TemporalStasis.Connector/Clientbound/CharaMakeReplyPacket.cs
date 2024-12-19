using System.Runtime.InteropServices;
using TemporalStasis.Connector.Serverbound;

namespace TemporalStasis.Connector.Clientbound;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 2568)]
public unsafe struct CharaMakeReplyPacket
{
    public uint RequestNumber;
    public uint Unknown4;
    public byte ListData;
    public byte Count;
    public CharaMakePacket.OperationType Operation;
    public byte OptionParam;
    public uint OptionArg;

    public unsafe fixed byte Padding[32];

    public ulong Unknown; // PlayerId? (all zeros though)
    public ulong CharacterId;
    public ulong Unknown2;
    public ushort VistingWorldId;
    public unsafe fixed byte Padding2[14];
    public uint DatacenterToken;
}
