using System.Net;
using TemporalStasis.Compression;
using TemporalStasis.Intercept;
using TemporalStasis.Proxy;
using TemporalStasis.Structs;

// Connect to Aether with these launch arguments: DEV.LobbyHost04=127.0.0.1 DEV.LobbyPort04=44994
var aether = await Dns.GetHostEntryAsync("neolobby02.ffxiv.com");
var addr = aether.AddressList[0];
var lobbyProxy = new LobbyProxy(addr, 54994, IPAddress.Loopback, 44994);

var oodle = new OodleLibraryFactory("oodle-network-shared.dll");

var zoneProxy = new ZoneProxy(oodle, IPAddress.Loopback, 44992);
lobbyProxy.ZoneProxy = zoneProxy;

void LobbyIpcClientboundPacket(int id, ref IpcInterceptedPacket packet, ref bool dropped, ConnectionType type)
{
    Console.WriteLine($"CB Lobby IPC: {packet.IpcHeader.Opcode}; {packet.IpcHeader.ServerId}; {packet.IpcHeader.Timestamp}; {packet.IpcHeader.Unknown0}; {packet.IpcHeader.Unknown4}; {packet.IpcHeader.Unknown12}; {packet.Data.Length}");
    if (packet.IpcHeader.Opcode == 2) {
        var playersInQueue = BitConverter.ToUInt16(packet.Data.AsSpan()[12..14]);
        Console.WriteLine("Lobby queue status received: " + playersInQueue);
    }
}


lobbyProxy.OnRawServerboundPacket += (int id, ref RawInterceptedPacket packet, ref bool dropped, ConnectionType type) => {
    Console.WriteLine($"SB: SegType {packet.SegmentHeader.SegmentType} Size {packet.Data.Length}");
    Console.WriteLine(Convert.ToHexString(packet.Data));
};
lobbyProxy.OnRawClientboundPacket += (int id, ref RawInterceptedPacket packet, ref bool dropped, ConnectionType type) => {
    Console.WriteLine($"CB: SegType {packet.SegmentHeader.SegmentType} Size {packet.Data.Length}");
    Console.WriteLine(Convert.ToHexString(packet.Data));
};
lobbyProxy.OnIpcClientboundPacket += LobbyIpcClientboundPacket;
lobbyProxy.OnIpcServerboundPacket += LobbyProxy_OnIpcServerboundPacket;

void LobbyProxy_OnIpcServerboundPacket(int id, ref IpcInterceptedPacket packet, ref bool dropped, ConnectionType type)
{
    Console.WriteLine($"SB Lobby IPC: {packet.IpcHeader.Opcode}; {packet.IpcHeader.ServerId}; {packet.IpcHeader.Timestamp}; {packet.IpcHeader.Unknown0}; {packet.IpcHeader.Unknown4}; {packet.IpcHeader.Unknown12}; {packet.Data.Length}");
    if (packet.IpcHeader.Opcode == 5)
        Console.WriteLine($"loginex: {Convert.ToHexString(packet.Data)}");
}


await Task.WhenAll(lobbyProxy.StartAsync(), zoneProxy.StartAsync());
