using System.Net;
using System.Net.Sockets;
using TemporalStasis.Connector.Serverbound;
using TemporalStasis.Encryption;
using TemporalStasis.Structs;

namespace TemporalStasis.Connector;

public sealed class Client(IPEndPoint lobbyEndpoint) : IDisposable
{
    public TcpClient? TcpClient { get; private set; }
    public NetworkStream? Stream { get; private set; }
    public Brokefish? Brokefish { get; private set; }

    public event Func<PacketHeader, PacketSegment, IpcData, Task> OnIpc
    {
        add
        {
            lock (onIpcLock)
                onIpc.Add(value);
        }
        remove
        {
            lock (onIpcLock)
                onIpc.Remove(value);
        }
    }
    private readonly object onIpcLock = new();
    private readonly List<Func<PacketHeader, PacketSegment, IpcData, Task>> onIpc = [];
    private Task InvokeOnIpc(PacketHeader a, PacketSegment b, IpcData c)
    {
        Func<PacketHeader, PacketSegment, IpcData, Task>[] t;
        lock (onIpcLock)
            t = [.. onIpc];
        return Task.WhenAll(t.Select(f => f(a, b, c)));
    }

    public event Func<PacketHeader, PacketSegment, Task> OnNonIpc
    {
        add
        {
            lock (onNonIpcLock)
                onNonIpc.Add(value);
        }
        remove
        {
            lock (onNonIpcLock)
                onNonIpc.Remove(value);
        }
    }
    private readonly object onNonIpcLock = new();
    private readonly List<Func<PacketHeader, PacketSegment, Task>> onNonIpc = [];
    private Task InvokeOnNonIpc(PacketHeader a, PacketSegment b)
    {
        Func<PacketHeader, PacketSegment, Task>[] t;
        lock (onNonIpcLock)
            t = [.. onNonIpc];
        return Task.WhenAll(t.Select(f => f(a, b)));
    }

    private SemaphoreSlim SendSemaphore { get; } = new(1);

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        TcpClient = new();
        await TcpClient.ConnectAsync(lobbyEndpoint, ct).ConfigureAwait(false);
        Stream = TcpClient.GetStream();
    }

    public async Task RecieveTask(CancellationToken ct = default)
    {
        if (Stream == null)
            throw new InvalidOperationException("Client not connected");

        while (!ct.IsCancellationRequested && Stream.CanRead)
        {
            var header = await Stream.ReadStructAsync<PacketHeader>(ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested)
                return;
            for (var i = 0; i < header.Count; ++i)
            {
                var segment = new PacketSegment(Stream);

                if (segment.Header.SegmentType is SegmentType.EncryptedData or SegmentType.Ipc)
                    segment.Decrypt(Brokefish!);

                if (segment.Header.SegmentType == SegmentType.Ipc)
                    await InvokeOnIpc(header, segment, new IpcData(segment)).ConfigureAwait(false);
                else
                    await InvokeOnNonIpc(header, segment).ConfigureAwait(false);
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

    public async Task InitializeEncryption(string keyPhrase, uint key, uint keyVersion)
    {
        Brokefish = new BrokefishKey(keyPhrase, key, keyVersion).Create();

        var pkt = new SendablePacket()
        {
            ConnectionType = ConnectionType.Lobby,
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
