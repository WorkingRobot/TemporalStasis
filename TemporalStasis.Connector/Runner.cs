using System.Net;
using System.Web;
using TemporalStasis.Connector.Clientbound;
using TemporalStasis.Connector.Login;
using TemporalStasis.Connector.Serverbound;
using TemporalStasis.Structs;

namespace TemporalStasis.Connector;

internal sealed class Runner : IDisposable
{
    private Client Client { get; }

    const bool IsLocal = false;

    const string Username = "nice try";
    const string Password = "nice try";
    const string? Otp = null;
    const bool IsFreeTrial = true;
    const bool IsSteam = false;

    const string BlowfishPhrase = "06c67fb0bb427f0e2f12433b9172f12e";
    const uint BlowfishVersion = 7000;
    const ushort LoginVersion = 7000;

    static readonly string[] ExVersions = ["2024.11.19.0000.0000", "2024.11.19.0000.0000", "2024.12.07.0000.0000", "2024.12.07.0000.0000", "2024.12.07.0000.0000"];
    const string GameVersion = "2024.12.07.0000.0000";
    const string BootVersion = "2024.11.01.0001.0001";

    static readonly FileReport GameExe =
        new("ffxiv_dx11.exe", 48641808, Convert.FromHexString("1c4d47684f5f25e8d17367ec9b38e582d1744262"));
    static readonly FileReport[] BootHashes = [
        new("ffxivboot.exe", 1089296, Convert.FromHexString("a351529a185333aaaa8e91f138efc3729ec7900d")),
        new("ffxivboot64.exe", 1282320, Convert.FromHexString("4a1f28c1d29a83e0070d4506366211115e89c12d")),
        new("ffxivlauncher64.exe", 13871376, Convert.FromHexString("17c033e02bd74e575472dbcdbbb4d341b41189d0")),
        new("ffxivupdater64.exe", 1338128, Convert.FromHexString("165380fcf21f5b610085b3fc66f91f3c0513eaf8")),
    ];

    private LoginClient.LoginResult LoginData { get; set; }
    private string PatchUniqueId { get; set; }

    static async Task Main(string[] args)
    {
        var aether = await Dns.GetHostEntryAsync("neolobby03.ffxiv.com").ConfigureAwait(false);
        var addr = IsLocal ? IPAddress.Loopback : aether.AddressList[0];

        await new Runner(addr, IsLocal ? 44994 : 54994).RunAsync().ConfigureAwait(false);
    }

    public Runner(IPAddress lobbyHost, int lobbyPort)
    {
        Client = new(lobbyHost, lobbyPort);
    }

    public async Task RunAsync()
    {
        using var loginClient = new LoginClient();
        var loginToken = await loginClient.GetLoginTokenAsync(IsFreeTrial).ConfigureAwait(false);
        LoginData = await loginClient.LoginOAuth(loginToken, Username, Password, Otp).ConfigureAwait(false);
        var report = LoginClient.GenerateVersionReport(BootVersion, BootHashes, ExVersions.AsSpan()[..LoginData.MaxExpansion]);
        PatchUniqueId = await loginClient.GetUniqueIdAsync(LoginData, GameVersion, report).ConfigureAwait(false);
        Console.WriteLine($"UID: {PatchUniqueId}");

        var cts = new CancellationTokenSource();
        //Console.CancelKeyPress += (sender, eventArgs) =>
        //{
        //    cts.Cancel();
        //    eventArgs.Cancel = true;
        //};

        Client.OnIpc += OnIpc;
        Client.OnNonIpc += OnNonIpc;

        WaitingForPing = true;
        CancelToken = cts.Token;
        await Client.ConnectAsync().ConfigureAwait(false);
        await Client.RecieveTask(cts.Token).ConfigureAwait(false);
    }

    private async Task OnIpc(PacketHeader header, PacketSegment segment, IpcData ipc)
    {
        // Send 5 LoginEx
        // Receive 12 LoginReply

        // Send 3 ServiceLogin
        // (Receive 2 if bad data)
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

    uint Fingerprint;
    uint RequestNumber;

    bool WaitingForPing;
    CancellationToken CancelToken;

    bool WaitingForLoginReply;
    List<LoginReplyPacket.Account>? ServiceAccounts;

    bool WaitingForDistInfo;
    List<DistWorldInfoPacket.World>? Worlds;
    List<XiCharacterInfoPacket.Character>? XiCharacters;
    List<DistRetainerInfoPacket.Retainer>? Retainers;
    List<ServiceLoginReplyPacket.Character>? Characters;

    private async Task OnNonIpc(PacketHeader header, PacketSegment segment)
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
                                new LoginExPacket(++RequestNumber, LoginVersion, PatchUniqueId, IsSteam,
                                    GameExe, ExVersions[..LoginData.MaxExpansion]
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
                await Client.InitializeEncryption(BlowfishPhrase, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), BlowfishVersion).ConfigureAwait(false);
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

    public void Dispose()
    {
        Client.Dispose();
    }
}
