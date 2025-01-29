using System.Runtime.InteropServices;

namespace TemporalStasis.Connector.Clientbound;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 536)]
public unsafe struct LoginErrorPacket
{
    public uint RequestNumber;
    public uint Unknown4;
    public ushort ErrorCode;
    public ushort Padding;
    public uint ErrorParam;
    public ushort ErrorSheetRow;
    public ushort MessageSize;
    public fixed byte MessageData[516];

    public string Message
    {
        get
        {
            fixed (byte* p = MessageData)
            {
                return Marshal.PtrToStringUTF8((nint)p, MessageSize);
            }
        }
    }
}
