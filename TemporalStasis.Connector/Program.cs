using System.Net;
using System.Web;
using TemporalStasis.Connector.Clientbound;
using TemporalStasis.Connector.Serverbound;
using TemporalStasis.Structs;

namespace TemporalStasis.Connector;

internal static class Program
{
    static Client Client;

    const bool IsLocal = false;

    static async Task Main(string[] args)
    {
        var aether = await Dns.GetHostEntryAsync("neolobby06.ffxiv.com");
        var addr = IsLocal ? IPAddress.Loopback : aether.AddressList[0];

        var cts = new CancellationTokenSource();
        //Console.CancelKeyPress += (sender, eventArgs) =>
        //{
        //    cts.Cancel();
        //    eventArgs.Cancel = true;
        //};

        using var client = new Client(addr, IsLocal ? 44994 : 54994);
        Client = client;

        client.OnIpc += OnIpc;
        client.OnNonIpc += OnNonIpc;

        WaitingForPing = true;
        CancelToken = cts.Token;
        await client.ConnectAsync().ConfigureAwait(false);
        await client.RecieveTask(cts.Token).ConfigureAwait(false);
    }

    static async Task OnIpc(PacketHeader header, PacketSegment segment, IpcData ipc)
    {
        Console.WriteLine($"Ipc Data Recieved: {ipc.Header.Opcode}; {ipc.Header.ServerId}; {ipc.Header.Timestamp}; {ipc.Header.Unknown0}; {ipc.Header.Unknown4}; {ipc.Header.Unknown12}; {ipc.Data.Length}");
        Console.WriteLine(Convert.ToHexString(ipc.Data));

        // Send 5 LoginEx
        // Receive 12 LoginReply

        // Send 3 ServiceLogin
        // Receive 21 DistWorldInfo
        // Receive 21 DistWorldInfo
        // Receive 22 XiCharacterInfo
        // Receive 23 DistRetainerInfo
        // Receive 13 (big) ServiceLoginReply

        // DC Travel
        // Send 11 CharaMake
        // Receive 14 CharaMakeReply


        if (ipc.Header.Opcode == 12)
        {
            var data = ipc.Deserialize<LoginReplyPacket>();
            if (WaitingForLoginReply)
            {
                ServiceAccounts!.AddRange(data.Accounts[..data.Count]);

                if (!data.HasMore)
                {
                    WaitingForLoginReply = false;
                    if (ServiceAccounts!.Count == 0)
                        throw new ArgumentException("No active accounts");

                    byte accountIdx = 0;
                    await Client.SendPacket(new SendablePacket()
                    {
                        ConnectionType = ConnectionType.None,
                        SendMagic = true,
                        SendTimestamp = true,
                        Segments = [
                            new SendablePacketSegment()
                    {
                        SegmentType = SegmentType.Ipc,
                        SourceActor = Fingerprint,
                        TargetActor = Fingerprint,
                        Payload = new SendableIpcPacket()
                        {
                            Opcode = 3,
                            Payload = new ServiceLoginPacket(++RequestNumber, accountIdx, ServiceAccounts[accountIdx].Id).Generate()
                        }.Generate(Client.Brokefish!)
                    }
                        ]
                    });
                    WaitingForDistInfo = true;
                    Worlds = [];
                    XiCharacters = [];
                    Retainers = [];
                    Characters = [];
                }
            }
        }
        else if (ipc.Header.Opcode == 21)
        {
            var data = ipc.Deserialize<DistWorldInfoPacket>();
            if (WaitingForDistInfo)
                Worlds!.AddRange(data.Worlds[..data.Count]);
        }
        else if (ipc.Header.Opcode == 22)
        {
            var data = ipc.Deserialize<XiCharacterInfoPacket>();
            if (WaitingForDistInfo)
                XiCharacters!.AddRangeIf(data.Characters[..data.Count], c => c.Id != 0);
        }
        else if (ipc.Header.Opcode == 23)
        {
            var data = ipc.Deserialize<DistRetainerInfoPacket>();
            if (WaitingForDistInfo)
                Retainers!.AddRangeIf(data.Retainers[..data.Count], c => c.Id != 0);
        }
        else if (ipc.Header.Opcode == 13)
        {
            var data = ipc.Deserialize<ServiceLoginReplyPacket>();
            if (WaitingForDistInfo)
            {
                Characters!.AddRangeIf(data.Characters[..data.Count], c => c.CharacterId != 0);

                if (!data.HasMore)
                {
                    WaitingForDistInfo = false;

                    foreach (var world in Worlds!)
                        Console.WriteLine($"World {world.Id}: {world.Name}");

                    foreach (var character in XiCharacters!)
                        Console.WriteLine($"XiChar {character.Id:X8}: {character.Name} (World {character.WorldParam})");

                    foreach (var retainer in Retainers!)
                        Console.WriteLine($"Retainer {retainer.Id:X16} (Owner {retainer.OwnerId:X16}): {retainer.Name}");

                    foreach (var character in Characters!)
                    {
                        Console.WriteLine($"Character {character.PlayerId:X16} (Character {character.CharacterId:X16}): {character.Name}");
                        Console.WriteLine($"At {character.WorldName} ({character.WorldId})");
                        Console.WriteLine($"Home {character.HomeWorldName} ({character.HomeWorldId})");
                        Console.WriteLine($"JSON: {character.Json}");
                    }

                    if (Characters!.Count == 0)
                        throw new ArgumentException("No active accounts");

                    // DC Travel
                    Console.WriteLine($"Character {Characters[0].Name} DC Travel Token");
                    await Client.SendPacket(new SendablePacket()
                    {
                        ConnectionType = ConnectionType.None,
                        SendMagic = true,
                        SendTimestamp = true,
                        Segments = [
                            new SendablePacketSegment()
                            {
                                SegmentType = SegmentType.Ipc,
                                SourceActor = Fingerprint,
                                TargetActor = Fingerprint,
                                Payload = new SendableIpcPacket()
                                {
                                    Opcode = 11,
                                    Payload =
                                        CharaMakePacket.GetDCTravelToken(++RequestNumber, Characters[0].CharacterId, Characters[0].Index).Generate()
                                }.Generate(Client.Brokefish!)
                            }
                        ]
                    });
                }
            }
        }
        else if (ipc.Header.Opcode == 14)
        {
            var data = ipc.Deserialize<CharaMakeReplyPacket>();
            if (data.Operation == CharaMakePacket.OperationType.DatacenterToken)
            {
                Console.WriteLine($"DC Travel Token: {data.DatacenterToken:X8}");
                using var http = new HttpClient();
                http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "FFXIV CLIENT");
                http.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/json ; charset=UTF-8");

                var uri = new UriBuilder("https://dctravel.ffxiv.com/worlds");
                var qs = HttpUtility.ParseQueryString(string.Empty);
                qs.Add("token", data.DatacenterToken.ToString());
                qs.Add("worldId", Characters![0].WorldId.ToString());
                qs.Add("characterId", Characters![0].CharacterId.ToString());

                uri.Query = qs.ToString();

                using var request = new HttpRequestMessage(HttpMethod.Get, uri.Uri)
                {
                    Version = HttpVersion.Version11,
                    VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
                    Content = new StringContent("\r\n")
                };
                request.Content.Headers.Clear();
                request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json ; charset=UTF-8");

                var ret = (await http.SendAsync(request).ConfigureAwait(false)).EnsureSuccessStatusCode();
                Console.WriteLine(await ret.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
        }
    }

    static uint Fingerprint;
    static uint RequestNumber;

    static bool WaitingForPing;
    static CancellationToken CancelToken;

    static bool WaitingForLoginReply;
    static List<LoginReplyPacket.Account>? ServiceAccounts;

    static bool WaitingForDistInfo;
    static List<DistWorldInfoPacket.World>? Worlds;
    static List<XiCharacterInfoPacket.Character>? XiCharacters;
    static List<DistRetainerInfoPacket.Retainer>? Retainers;
    static List<ServiceLoginReplyPacket.Character>? Characters;

    static async Task OnNonIpc(PacketHeader header, PacketSegment segment)
    {
        if (segment.Header.SegmentType == SegmentType.EncryptedData)
        {
            var pkt = segment.Deserialize<EncryptionDataPacket>();
            Fingerprint = pkt.Fingerprint;

            await Client.SendPacket(new SendablePacket()
            {
                ConnectionType = ConnectionType.None,
                SendMagic = true,
                SendTimestamp = true,
                Segments = [
                    new SendablePacketSegment()
                    {
                        SegmentType = SegmentType.Ipc,
                        SourceActor = Fingerprint,
                        TargetActor = Fingerprint,
                        Payload = new SendableIpcPacket()
                        {
                            Opcode = 5,
                            Payload =
                                new LoginExPacket(++RequestNumber, 7000, "nice try", 48641808,
                                    Convert.FromHexString("1c4d47684f5f25e8d17367ec9b38e582d1744262"),
                                    ["2024.11.19.0000","2024.11.19.0000","2024.12.07.0000","2024.12.07.0000","2024.12.07.0000"]
                                ).Generate()
                        }.Generate(Client.Brokefish!)
                    }
                ]
            });
            WaitingForLoginReply = true;
            ServiceAccounts = [];

            await Client.SendPing(Fingerprint, true);
        }
        if (header.ConnectionType == ConnectionType.None && segment.Header.SegmentType == SegmentType.KeepAlive)
        {
            if (WaitingForPing)
            {
                WaitingForPing = false;

                Console.WriteLine($"Initializing encryption {Convert.ToHexString(segment.Data)}");
                var key = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var phrase = "06c67fb0bb427f0e2f12433b9172f12e";
                await Client.InitializeEncryption(phrase, key).ConfigureAwait(false);
            }

            _ = Task.Run(async () =>
            {
                while (true)
                {
                    CancelToken.ThrowIfCancellationRequested();
                    await Task.Delay(10 * 1000, CancelToken).ConfigureAwait(false);
                    await Client.SendPing(Fingerprint).ConfigureAwait(false);
                }
            }, CancelToken);
        }
    }

    private static void AddRangeIf<T>(this List<T> me, ReadOnlySpan<T> span, Predicate<T> predicate)
    {
        foreach(ref readonly var i in span)
        {
            if (predicate(i))
                me.Add(i);
        }
    }
}
