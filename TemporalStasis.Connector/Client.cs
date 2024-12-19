using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using TemporalStasis.Connector.Serverbound;
using TemporalStasis.Encryption;
using TemporalStasis.Structs;

namespace TemporalStasis.Connector;

public sealed class Client(IPAddress host, int port) : IDisposable
{
    public TcpClient? TcpClient { get; private set; }
    public NetworkStream? Stream { get; private set; }
    public Brokefish? Brokefish { get; private set; }

    public event Func<PacketHeader, PacketSegment, IpcData, Task>? OnIpc;
    public event Func<PacketHeader, PacketSegment, Task>? OnNonIpc;

    private SemaphoreSlim SendSemaphore = new(1);

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        TcpClient = new();
        await TcpClient.ConnectAsync(host, port, ct).ConfigureAwait(false);
        Stream = TcpClient.GetStream();
    }

    public async Task RecieveTask(CancellationToken ct = default)
    {
        if (Stream == null)
            throw new InvalidOperationException("Client not connected");

        while (!ct.IsCancellationRequested && Stream.CanRead)
        {
            var header = await Stream.ReadStructAsync<PacketHeader>().ConfigureAwait(false);

            Console.WriteLine($"Received packet Compression {header.CompressionType} ConnectionType {header.ConnectionType}");
            for (var i = 0; i < header.Count; ++i)
            {
                var segment = new PacketSegment(Stream);

                if (segment.Header.SegmentType is SegmentType.EncryptedData or SegmentType.Ipc)
                    segment.Decrypt(Brokefish!);

                if (segment.Header.SegmentType == SegmentType.Ipc)
                {
                    var ipcData = new IpcData(segment);
                    if (OnIpc != null)
                        await OnIpc.Invoke(header, segment, ipcData).ConfigureAwait(false);
                }
                else
                {
                    if (OnNonIpc != null)
                        await OnNonIpc.Invoke(header, segment).ConfigureAwait(false);

                }

                Console.WriteLine($"Received segment {segment.Header.SegmentType}");
                Console.WriteLine($"Segment data: {Convert.ToHexString(segment.Data)}");
            }
        }
    }

    public async Task SendPacket(SendablePacket packet)
    {
        if (Stream == null)
            throw new InvalidOperationException("Client not connected");


        var segments = packet.Generate();
        var l = new List<byte>();
        foreach (var segment in segments)
            l.AddRange(segment);

        await SendSemaphore.WaitAsync();
        Stream.WriteBytes([.. l]);
        SendSemaphore.Release();
    }

    public async Task InitializeEncryption(string keyPhrase, uint key)
    {
        Brokefish = new BrokefishKey(keyPhrase, key, 7000).Create();

        var pkt = new SendablePacket()
        {
            ConnectionType = ConnectionType.None,
            Segments = [new SendablePacketSegment() {
                SegmentType = SegmentType.EncryptionInit,
                Payload = new EncryptionInitPacket(keyPhrase, key).Generate()
            }]
        };
        await SendPacket(pkt).ConfigureAwait(false);
    }

    public async Task SendPing(uint fingerprint, bool isPong = false)
    {
        await SendPacket(new SendablePacket()
        {
            ConnectionType = ConnectionType.None,
            Segments = [
                new SendablePacketSegment()
                {
                    SegmentType = isPong ? SegmentType.KeepAlivePong : SegmentType.KeepAlive,
                    Payload = new PingPacket(fingerprint).Generate()
                }
            ]
        });
    }

    public void Dispose()
    {
        Stream?.Dispose();
        TcpClient?.Dispose();
    }
}
