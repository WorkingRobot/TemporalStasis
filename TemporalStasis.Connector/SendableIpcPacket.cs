using TemporalStasis.Encryption;
using TemporalStasis.Structs;

namespace TemporalStasis.Connector;

public record SendableIpcPacket
{
    public required ushort Opcode { get; init; }
    public required byte[] Payload { get; init; }

    public byte[] Generate(Brokefish brokefish)
    {
        using var s = new MemoryStream();
        var header = new IpcHeader()
        {
            Unknown0 = 20,
            Opcode = Opcode,
            Timestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        s.WriteStruct(header);
        s.Write(Payload);
        var ret = s.ToArray();

        //Console.WriteLine($"Sending IPC Packet: {Convert.ToHexString(ret)}");

        brokefish.EncipherPadded(ret);
        return ret;
    }
}
