using System.Runtime.InteropServices;

namespace TemporalStasis.Connector.Clientbound;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 656)]
public unsafe struct LoginReplyPacket
{
    public uint RequestNumber;
    public uint Unknown4;
    public byte ListData;
    public byte Count;
    public byte RegionId;
    public byte OptionParam;
    public uint OptionArg;
    public unsafe fixed byte AccountData[640];

    public readonly bool HasMore => (ListData & 1) != 0;
    public readonly byte ListOffset => (byte)(ListData >> 1);
    public Span<Account> Accounts
    {
        get
        {
            fixed (byte* p = AccountData)
            {
                return new Span<Account>(p, 8);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 80)]
    public unsafe struct Account
    {
        public ulong Id;
        public byte Index;
        public byte Param;
        public ushort Status;
        public unsafe fixed byte Name[64];
    }
}
