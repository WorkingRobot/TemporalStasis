using System.Net;
using TemporalStasis.Connector.Clientbound;
using TemporalStasis.Connector.Serverbound;
using TemporalStasis.Structs;

namespace TemporalStasis.Connector;

internal sealed class Runner : IDisposable
{
    public event Func<Task> OnLogin
    {
        add
        {
            lock (onLoginLock)
                onLogin.Add(value);
        }
        remove
        {
            lock (onLoginLock)
                onLogin.Remove(value);
        }
    }
    private readonly object onLoginLock = new();
    private readonly List<Func<Task>> onLogin = [];
    private Task InvokeOnLogin()
    {
        Func<Task>[] t;
        lock (onLoginLock)
            t = [.. onLogin];
        return Task.WhenAll(t.Select(a => a()));
    }

    public event Func<CharaMakeReplyPacket, Task> OnCharaMakeReply
    {
        add
        {
            lock (onCharaMakeReplyLock)
                onCharaMakeReply.Add(value);
        }
        remove
        {
            lock (onCharaMakeReplyLock)
                onCharaMakeReply.Remove(value);
        }
    }
    private readonly object onCharaMakeReplyLock = new();
    private readonly List<Func<CharaMakeReplyPacket, Task>> onCharaMakeReply = [];
    private Task InvokeOnCharaMakeReply(CharaMakeReplyPacket a)
    {
        Func<CharaMakeReplyPacket, Task>[] t;
        lock (onCharaMakeReplyLock)
            t = [.. onCharaMakeReply];
        return Task.WhenAll(t.Select(f => f(a)));
    }

    private Client Client { get; }
    private VersionInfo VersionInfo { get; }
    private LoginInfo LoginInfo { get; }
    private CancellationToken Token { get; }

    enum WaitingState
    {
        WaitingForPing,
        WaitingForLoginReply,
        WaitingForDistInfo,
        LoggedIn
    }

    private uint? Fingerprint { get; set; }
    private uint RequestNumber { get; set; }

    private WaitingState State { get; set; }

    private List<LoginReplyPacket.Account>? ServiceAccounts { get; set; }
    private List<DistWorldInfoPacket.World>? WorldsList { get; set; }
    private List<XiCharacterInfoPacket.Character>? XiCharactersList { get; set; }
    private List<DistRetainerInfoPacket.Retainer>? RetainersList { get; set; }
    private List<ServiceLoginReplyPacket.Character>? CharactersList { get; set; }

    public IReadOnlyList<DistWorldInfoPacket.World> Worlds =>
        State == WaitingState.LoggedIn ?
            WorldsList! :
            throw new InvalidOperationException("Not logged in yet");

    public IReadOnlyList<XiCharacterInfoPacket.Character> XiCharacters =>
        State == WaitingState.LoggedIn ?
            XiCharactersList! :
            throw new InvalidOperationException("Not logged in yet");

    public IReadOnlyList<DistRetainerInfoPacket.Retainer> Retainers =>
        State == WaitingState.LoggedIn ?
            RetainersList! :
            throw new InvalidOperationException("Not logged in yet");

    public IReadOnlyList<ServiceLoginReplyPacket.Character> Characters =>
        State == WaitingState.LoggedIn ?
            CharactersList! :
            throw new InvalidOperationException("Not logged in yet");

    public Runner(IPEndPoint lobbyEndpoint, VersionInfo versionInfo, LoginInfo loginInfo, CancellationToken token)
    {
        Client = new(lobbyEndpoint);
        Client.OnIpc += OnIpc;
        Client.OnNonIpc += OnNonIpc;
        VersionInfo = versionInfo;
        LoginInfo = loginInfo;
        Token = token;
    }

    public async Task RunAsync()
    {
        State = WaitingState.WaitingForPing;
        Fingerprint = null;
        RequestNumber = 0;
        await Client.ConnectAsync().ConfigureAwait(false);
        await Client.RecieveTask(Token).ConfigureAwait(false);
    }

    public async Task<uint> GetDCTravelToken(ServiceLoginReplyPacket.Character character)
    {
        var reqNum = ++RequestNumber;
        TaskCompletionSource<uint> completionSource = new();
        Task HandleReply(CharaMakeReplyPacket p)
        {
            if (p.RequestNumber != reqNum)
                return Task.CompletedTask;

            try
            {
                if (p.Operation != CharaMakePacket.OperationType.DatacenterToken)
                    throw new InvalidOperationException("Expected DC token response");

                completionSource.SetResult(p.DatacenterToken);
            }
            catch (Exception e)
            {
                completionSource.SetException(e);
            }
            finally
            {
                OnCharaMakeReply -= HandleReply;
            }
            return completionSource.Task;
        }
        OnCharaMakeReply += HandleReply;

        await Client.SendPacket(new SendablePacket()
        {
            ConnectionType = ConnectionType.None,
            SendMagic = true,
            SendTimestamp = true,
            Segments = [
                new SendablePacketSegment()
                {
                    SegmentType = SegmentType.Ipc,
                    SourceActor = Fingerprint!.Value,
                    TargetActor = Fingerprint!.Value,
                    Payload = new SendableIpcPacket()
                    {
                        Opcode = 11,
                        Payload =
                            CharaMakePacket.GetDCTravelToken(reqNum, character.CharacterId, character.Index).Generate()
                    }.Generate(Client.Brokefish!)
                }
            ]
        });
        Log.Debug("Sent CharaMake DCTravelToken");
        return await completionSource.Task.ConfigureAwait(false);
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

        var opName = ipc.Header.Opcode switch
        {
            2 => "LoginError",
            12 => "LoginReply",
            21 => "DistWorldInfo",
            22 => "XiCharacterInfo",
            23 => "DistRetainerInfo",
            13 => "ServiceLoginReply",
            14 => "CharaMakeReply",
            var c => $"Unknown ({c})"
        };
        Log.Debug($"Received IPC {opName}");

        if (ipc.Header.Opcode == 2 && State >= WaitingState.WaitingForLoginReply)
        {
            var data = ipc.Deserialize<LoginErrorPacket>();
            throw new InvalidDataException($"Login error; Code: {data.ErrorCode}; Param: {data.ErrorParam}; Row: {data.ErrorSheetRow}; {data.Message}");
        }
        else if (ipc.Header.Opcode == 12 && State >= WaitingState.WaitingForLoginReply)
        {
            var data = ipc.Deserialize<LoginReplyPacket>();
            ServiceAccounts!.AddRange(data.Accounts[..data.Count]);

            if (!data.HasMore)
            {
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
                            SourceActor = Fingerprint!.Value,
                            TargetActor = Fingerprint!.Value,
                            Payload = new SendableIpcPacket()
                            {
                                Opcode = 3,
                                Payload = new ServiceLoginPacket(++RequestNumber, accountIdx, ServiceAccounts[accountIdx].Id).Generate()
                            }.Generate(Client.Brokefish!)
                        }
                    ]
                });
                Log.Debug("Sent ServiceLogin");
                State = WaitingState.WaitingForDistInfo;
                WorldsList = [];
                XiCharactersList = [];
                RetainersList = [];
                CharactersList = [];
            }
        }
        else if (ipc.Header.Opcode == 21 && State >= WaitingState.WaitingForDistInfo)
        {
            var data = ipc.Deserialize<DistWorldInfoPacket>();
            WorldsList!.AddRange(data.Worlds[..data.Count]);
        }
        else if (ipc.Header.Opcode == 22 && State >= WaitingState.WaitingForDistInfo)
        {
            var data = ipc.Deserialize<XiCharacterInfoPacket>();
            XiCharactersList!.AddRangeIf(data.Characters[..data.Count], c => c.Id != 0);
        }
        else if (ipc.Header.Opcode == 23 && State >= WaitingState.WaitingForDistInfo)
        {
            var data = ipc.Deserialize<DistRetainerInfoPacket>();
            RetainersList!.AddRangeIf(data.Retainers[..data.Count], c => c.Id != 0);
        }
        else if (ipc.Header.Opcode == 13 && State >= WaitingState.WaitingForDistInfo)
        {
            var data = ipc.Deserialize<ServiceLoginReplyPacket>();
            CharactersList!.AddRangeIf(data.Characters[..data.Count], c => c.CharacterId != 0);

            if (!data.HasMore)
            {
                State = WaitingState.LoggedIn;
                await InvokeOnLogin().ConfigureAwait(false);
            }
        }
        else if (ipc.Header.Opcode == 14)
        {
            var data = ipc.Deserialize<CharaMakeReplyPacket>();
            await InvokeOnCharaMakeReply(data).ConfigureAwait(false);
        }
    }

    private async Task OnNonIpc(PacketHeader header, PacketSegment segment)
    {
        Log.Debug($"Received non-IPC {segment.Header.SegmentType}");

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
                        SourceActor = Fingerprint!.Value,
                        TargetActor = Fingerprint!.Value,
                        Payload = new SendableIpcPacket()
                        {
                            Opcode = 5,
                            Payload =
                                new LoginExPacket(++RequestNumber, VersionInfo.LoginVersion, LoginInfo.UniqueId, LoginInfo.IsSteam,
                                    VersionInfo.GameExe, VersionInfo.ExVersions[..LoginInfo.MaxExpansion]
                                ).Generate()
                        }.Generate(Client.Brokefish!)
                    }
                ]
            });
            Log.Debug("Sent LoginEx");
            State = WaitingState.WaitingForLoginReply;
            ServiceAccounts = [];

            await Client.SendPing(Fingerprint!.Value, true);
        }
        if (header.ConnectionType == ConnectionType.None &&
            segment.Header.SegmentType == SegmentType.KeepAlive &&
            State == WaitingState.WaitingForPing)
        {
            Log.Debug($"Initializing encryption {Convert.ToHexString(segment.Data)}");
            await Client.InitializeEncryption(VersionInfo.BlowfishPhrase, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), VersionInfo.BlowfishVersion).ConfigureAwait(false);

            _ = Task.Run(async () =>
            {
                while (true)
                {
                    Token.ThrowIfCancellationRequested();
                    await Task.Delay(10 * 1000, Token).ConfigureAwait(false);
                    await Client.SendPing(Fingerprint!.Value).ConfigureAwait(false);
                }
            }, Token);
        }
    }

    public void Dispose()
    {
        Client.Dispose();
    }
}
