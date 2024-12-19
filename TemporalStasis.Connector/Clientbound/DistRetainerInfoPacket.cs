using System.Runtime.InteropServices;

namespace TemporalStasis.Connector.Clientbound;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 536)]
public unsafe struct DistRetainerInfoPacket
{
    public uint RequestNumber;
    public uint Unknown4;
    public byte ListData;
    public byte Count;
    public ushort OptionParam;
    public uint OptionArg;
    public ushort ContractedCount;
    public ushort ActiveCharacterCount;
    public ushort TotalSlotCount;
    public ushort FreeSlotCount;
    public ushort TotalRetainerCount;
    public ushort ActiveRetainerCount;
    public uint Padding;
    public unsafe fixed byte AccountData[504];

    public readonly bool HasMore => (ListData & 1) == 0;
    public Span<Retainer> Retainers
    {
        get
        {
            fixed (byte* p = AccountData)
            {
                return new Span<Retainer>(p, 9);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 56)]
    public unsafe struct Retainer
    {
        public ulong Id;
        public ulong OwnerId;
        public byte SlotId;
        public byte Param1;
        public ushort Status;
        public uint Param2;
        public unsafe fixed byte NameData[32];

        public string Name
        {
            get
            {
                fixed (byte* p = NameData)
                {
                    return Marshal.PtrToStringUTF8((nint)p, 32);
                }
            }
        }
    }
}
