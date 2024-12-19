using System.Runtime.InteropServices;

namespace TemporalStasis.Connector;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 496)]
public unsafe struct XiCharacterInfoPacket
{
    public uint RequestNumber;
    public uint Unknown4;
    public byte ListData;
    public byte Count;
    public ushort OptionParam;
    public uint OptionArg;
    public unsafe fixed byte AccountData[480];

    public readonly bool HasMore => (ListData & 1) == 0;
    public Span<Character> Characters
    {
        get
        {
            fixed (byte* p = AccountData)
            {
                return new Span<Character>(p, 12);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 40)]
    public unsafe struct Character
    {
        public uint Id;
        public byte Index;
        public byte WorldParam;
        public ushort Status;
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
