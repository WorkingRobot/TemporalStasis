using System.Runtime.InteropServices;
using System.Text;

namespace TemporalStasis.Connector.Clientbound;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 528)]
public unsafe struct DistWorldInfoPacket
{
    public uint RequestNumber;
    public uint Unknown4;
    public ushort ListData;
    public ushort ListOffset;
    public ushort Count;
    public ushort OptionParam;
    public uint Padding;
    public uint OptionArg;
    public unsafe fixed byte WorldData[504];

    public readonly bool HasMore => (ListData & 1) == 0;
    public Span<World> Worlds
    {
        get
        {
            fixed (byte* p = WorldData)
            {
                return new Span<World>(p, 6);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 84)]
    public unsafe struct World
    {
        public ushort Id;
        public ushort Idx;
        public byte Param1;
        public byte Padding1;
        public byte Padding2;
        public byte Padding3;
        public uint Stat1;
        public uint Stat2;
        public uint Mode;
        public unsafe fixed byte NameData[64];

        public string Name
        {
            get
            {
                fixed (byte* p = NameData)
                {
                    return Marshal.PtrToStringUTF8((nint)p, 64);
                }
            }
        }
    }
}
