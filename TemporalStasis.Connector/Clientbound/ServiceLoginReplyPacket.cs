using System.Runtime.InteropServices;

namespace TemporalStasis.Connector.Clientbound;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 2472)]
public unsafe struct ServiceLoginReplyPacket
{
    public uint RequestNumber;
    public uint Unknown4;
    public byte ListData;
    public byte Count;
    public ushort OptionParam;
    public uint OptionArg;
    public unsafe fixed byte Padding[28]; // BillingParam(?)

    // LobbySubscriptionInfo (these fields are only populated in the last packet, where !HasMore)
    public byte Unknown6;
    public byte VeteranRank;
    public ushort Unknown7;
    public uint DaysSubscribed;
    public uint DaysRemaining;
    public uint DaysUntilNextVeteranRank;
    public ushort MaxCharacterCount;
    public ushort MaxCharacterList;
    public uint EntitledExpansion;

    public unsafe fixed byte Padding2[12];
    public unsafe fixed byte AccountData[1184 * 2];
    // 24 extra footer bytes of 00

    public readonly bool HasMore => (ListData & 1) == 0;
    public readonly byte ListOffset => (byte)(ListData >> 1);
    public Span<Character> Characters
    {
        get
        {
            fixed (byte* p = AccountData)
            {
                return new Span<Character>(p, 2);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 1184)]
    public unsafe struct Character
    {
        public ulong PlayerId;
        public ulong CharacterId;
        public byte Index;
        public byte Param;
        public ushort Status;
        public uint Param2;
        public ushort WorldId;
        public ushort HomeWorldId;
        public uint LastBackupTimestamp;
        public ushort Unk; // PlatformId/PlatformMode? May be similar to some field in LoginExPacket
        public unsafe fixed byte Padding[10];
        public unsafe fixed byte NameData[32];
        public unsafe fixed byte WorldNameData[32];
        public unsafe fixed byte HomeWorldNameData[32];
        public unsafe fixed byte JsonData[1024];
        public unsafe fixed byte SettingsHash[20]; // SHA1

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

        public string WorldName
        {
            get
            {
                fixed (byte* p = WorldNameData)
                {
                    return Marshal.PtrToStringUTF8((nint)p, 32);
                }
            }
        }

        public string HomeWorldName
        {
            get
            {
                fixed (byte* p = HomeWorldNameData)
                {
                    return Marshal.PtrToStringUTF8((nint)p, 32);
                }
            }
        }

        public string Json
        {
            get
            {
                fixed (byte* p = JsonData)
                {
                    return Marshal.PtrToStringUTF8((nint)p, 1024);
                }
            }
        }
    }
}
