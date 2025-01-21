using DotMake.CommandLine;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Web;
using TemporalStasis.Connector.Login;
using SerCtx = TemporalStasis.Connector.ConnectorSerializerContext;

namespace TemporalStasis.Connector;

[CliCommand(Description = "Get a DC travel token")]
public class GetTokenCommand
{
    private static Task<int> Main(string[] args) =>
        Cli.RunAsync<GetTokenCommand>(args);

    [CliArgument(Required = true, Arity = CliArgumentArity.OneOrMore, Description = "The lobby endpoints to access. Accepts DNS names, IPs, and ports")]
    public required string[] LobbyHosts { get; set; }

    // Version Info

    [CliOption(Required = false, Description = "A json file to where all the version information is stored", ValidationRules = CliValidationRules.ExistingFile)]
    public FileInfo? VersionFile { get; set; }

    [CliOption(Required = false, Description = "A json url to where all the version information is stored", ValidationRules = CliValidationRules.LegalUrl)]
    public Uri? VersionUrl { get; set; }

    // Username/Password

    [CliOption(Required = false)]
    public string? Username { get; set; }

    [CliOption(Required = false)]
    public string? Password { get; set; }

    [CliOption(Name = "--free-trial")]
    public bool IsFreeTrial { get; set; } = false;

    // UID

    [CliOption(Required = false)]
    public string? UID { get; set; }

    [CliOption(Required = false)]
    public int? MaxExpansion { get; set; }

    // Caches

    [CliOption(Name = "--uid-cache-name", Required = false)]
    public string UIDCacheName { get; set; } = string.Empty;

    [CliOption(Name = "--uid-cache", Required = false)]
    public FileInfo? UIDCache { get; set; }

    [CliOption(Name = "--dc-token-cache", Required = false)]
    public FileInfo? DCTokenCache { get; set; }

    [CliOption(Name = "--uid-ttl")]
    public TimeSpan UIDTTL { get; set; } = TimeSpan.FromDays(1);

    [CliOption(Name = "--dc-token-ttl")]
    public TimeSpan DCTokenTTL { get; set; } = TimeSpan.FromDays(5);

    // Action Limiters

    [CliOption(Name = "--only-dc-token")]
    public bool OnlyDCToken { get; set; } = false;

    [CliOption(Name = "--only-uid-data")]
    public bool OnlyUIDData { get; set; } = false;
    
    // Misc

    [CliOption]
    public bool Verbose { get; set; } = false;

    public struct UIDCacheEntry
    {
        public LoginInfo LoginInfo { get; set; }
        public DateTime CreationDate { get; set; }
    }

    public struct DCTokenCacheEntry
    {
        public ulong CharacterId { get; set; }
        public ushort WorldId { get; set; }
        public uint DCToken { get; set; }
        public DateTime CreationDate { get; set; }
        public byte[] VersionInfoHash { get; set; }
    }

    public async Task RunAsync()
    {
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            cts.Cancel();
            eventArgs.Cancel = true;
        };

        VersionInfo versionInfo;
        if (VersionFile != null)
        {
            using var f = VersionFile.OpenRead();
            versionInfo = await JsonSerializer.DeserializeAsync(f, SerCtx.Default.VersionInfo, cancellationToken: cts.Token).ConfigureAwait(false);
        }
        else if (VersionUrl != null)
        {
            using var client = new HttpClient();
            versionInfo = await client.GetFromJsonAsync(VersionUrl, SerCtx.Default.VersionInfo, cts.Token).ConfigureAwait(false);
        }
        else
            throw new ArgumentException("Either version file or version url must be specified");

        var endpoints = await Task.WhenAll(LobbyHosts.Select(async (host) =>
        {
            var port = 54994;
            var portSplitIdx = host.LastIndexOf(':');
            if (portSplitIdx != -1)
            {
                port = int.Parse(host.AsSpan()[(portSplitIdx + 1)..]);
                host = host[..portSplitIdx];
            }

            if (!IPAddress.TryParse(host, out var address))
            {
                var t = await Dns.GetHostEntryAsync(host).ConfigureAwait(false);
                address = t.AddressList[0];
            }
            return new IPEndPoint(address, port);
        })).ConfigureAwait(false);

        LoginInfo? loginInfoValue = null;

        if (!string.IsNullOrEmpty(UID) && MaxExpansion.HasValue)
        {
            loginInfoValue = new()
            {
                UniqueId = UID,
                IsSteam = false,
                MaxExpansion = MaxExpansion.Value
            };
            await WriteUIDCacheEntryAsync(loginInfoValue.Value, cts.Token).ConfigureAwait(false);
        }

        if (!loginInfoValue.HasValue)
            loginInfoValue = await GetUIDCacheEntryAsync(cts.Token).ConfigureAwait(false);

        if (!loginInfoValue.HasValue && !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
        {
            using var loginClient = new LoginClient();
            var loginToken = await loginClient.GetLoginTokenAsync(IsFreeTrial).ConfigureAwait(false);
            var loginData = await loginClient.LoginOAuth(loginToken, Username, Password, null).ConfigureAwait(false);
            var report = LoginClient.GenerateVersionReport(versionInfo.BootVersion, versionInfo.BootHashes, versionInfo.ExVersions.AsSpan()[..loginData.MaxExpansion]);
            var uniqueId = await loginClient.GetUniqueIdAsync(loginData, versionInfo.GameVersion, report).ConfigureAwait(false);
            loginInfoValue = new()
            {
                UniqueId = uniqueId,
                IsSteam = false,
                MaxExpansion = loginData.MaxExpansion
            };
            await WriteUIDCacheEntryAsync(loginInfoValue.Value, cts.Token).ConfigureAwait(false);
        }

        var loginInfo = loginInfoValue ??
            throw new ArgumentException("Either username/password or uid/max expansion must be specified");

        if (OnlyUIDData)
        {
            Console.WriteLine(loginInfo.UniqueId);
            Console.WriteLine(loginInfo.MaxExpansion);
            return;
        }

        if (Verbose)
        {
            Console.WriteLine($"UID: {loginInfo.UniqueId}");
            Console.WriteLine();
        }

        var runnerTasks = endpoints.Select(e => ExecuteRunner(e, versionInfo, loginInfo, cts.Token));
        await Task.WhenAll(runnerTasks).ConfigureAwait(false);
    }

    private async Task ExecuteRunner(IPEndPoint endpoint, VersionInfo versionInfo, LoginInfo loginInfo, CancellationToken token)
    {
        var dcTokenData = await GetDCTokenCacheEntryAsync(endpoint, versionInfo, token).ConfigureAwait(false);

        if (!dcTokenData.HasValue)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(token);

            using var runner = new Runner(endpoint, versionInfo, loginInfo, cts.Token);

            if (Verbose)
            {
                runner.OnLogin += () =>
                {
                    Console.WriteLine("Worlds:");
                    foreach (var world in runner.Worlds)
                        Console.WriteLine($"World {world.Id}: {world.Name}");
                    Console.WriteLine();

                    Console.WriteLine("FFXI Characters:");
                    foreach (var character in runner.XiCharacters)
                        Console.WriteLine($"XiChar {character.Id:X8}: {character.Name} (World {character.WorldParam})");
                    Console.WriteLine();

                    Console.WriteLine("Retainers:");
                    foreach (var retainer in runner.Retainers)
                        Console.WriteLine($"Retainer {retainer.Id:X16} (Owner {retainer.OwnerId:X16}): {retainer.Name}");
                    Console.WriteLine();

                    Console.WriteLine("Characters:");
                    foreach (var character in runner.Characters)
                    {
                        Console.WriteLine($"Character {character.CharacterId:X16} (Player {character.PlayerId:X16}): {character.Name}");
                        Console.WriteLine($"  At {character.WorldName} ({character.WorldId})");
                        Console.WriteLine($"  Home {character.HomeWorldName} ({character.HomeWorldId})");
                        Console.WriteLine($"  JSON: {character.Json}");
                    }
                    Console.WriteLine();

                    return Task.CompletedTask;
                };
            }

            runner.OnLogin += () =>
            {
                if (runner.Characters.Count == 0)
                    throw new ArgumentException("No active accounts");
                var character = runner.Characters[0];

                _ = Task.Run(async () =>
                {
                    var token = await runner.GetDCTravelToken(character).ConfigureAwait(false);
                    await WriteDCTokenCacheEntryAsync(endpoint, versionInfo, character.CharacterId, character.WorldId, token, cts.Token).ConfigureAwait(false);

                    dcTokenData = (character.CharacterId, character.WorldId, token);
                }).ContinueWith(t => cts.Cancel());

                return Task.CompletedTask;
            };

            try
            {
                await runner.RunAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                if (!cts.IsCancellationRequested)
                    throw;
            }
        }

        if (!dcTokenData.HasValue)
            return;
        var (characterId, worldId, dcToken) = dcTokenData.Value;

        if (OnlyDCToken)
        {
            Console.WriteLine(dcToken);
            Console.WriteLine(worldId);
            Console.WriteLine(characterId);
            return;
        }

        if (Verbose)
        {
            Console.WriteLine($"DC Travel Token: {dcToken:X8}");
            Console.WriteLine($"World Id: {worldId}");
            Console.WriteLine($"Character Id: {characterId:X16}");
        }

        await ExecuteDCToken(dcToken, worldId, characterId, token).ConfigureAwait(false);
    }

    private async Task ExecuteDCToken(uint dcToken, ushort worldId, ulong characterId, CancellationToken token)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "FFXIV CLIENT");

        var uri = new UriBuilder("https://dctravel.ffxiv.com/worlds");
        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs.Add("token", dcToken.ToString());
        qs.Add("worldId", worldId.ToString());
        qs.Add("characterId", characterId.ToString());
        uri.Query = qs.ToString();

        using var request = new HttpRequestMessage(HttpMethod.Get, uri.Uri)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Content = new StringContent("\r\n")
        };
        request.Content.Headers.Clear();
        request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json ; charset=UTF-8");

        var ret = (await http.SendAsync(request, token).ConfigureAwait(false)).EnsureSuccessStatusCode();
        if (Verbose)
        {
            Console.WriteLine();
            Console.WriteLine("Travel Response:");
        }
        Console.WriteLine(await ret.Content.ReadAsStringAsync(token).ConfigureAwait(false));
    }

    private async Task<LoginInfo?> GetUIDCacheEntryAsync(CancellationToken token)
    {
        if (!(UIDCache?.Exists ?? false))
            return null;
        using var c = UIDCache.OpenRead();
        var entries = c.Length == 0 ? null : await JsonSerializer.DeserializeAsync(c, SerCtx.Default.DictionaryStringUIDCacheEntry, cancellationToken: token).ConfigureAwait(false);
        if (entries == null)
            return null;
        if (!entries.TryGetValue(UIDCacheName, out var entry))
            return null;
        if (entry.CreationDate + UIDTTL <= DateTime.UtcNow)
            return null;
        return entry.LoginInfo;
    }

    private async Task WriteUIDCacheEntryAsync(LoginInfo loginInfo, CancellationToken token)
    {
        if (UIDCache == null)
            return;
        using var c = UIDCache.Open(FileMode.OpenOrCreate, FileAccess.ReadWrite);
        var entries = c.Length == 0 ? [] : await JsonSerializer.DeserializeAsync(c, SerCtx.Default.DictionaryStringUIDCacheEntry, cancellationToken: token).ConfigureAwait(false) ?? [];
        entries[UIDCacheName] = new() { LoginInfo = loginInfo, CreationDate = DateTime.UtcNow };
        c.SetLength(0);
        await JsonSerializer.SerializeAsync(c, entries, SerCtx.Default.DictionaryStringUIDCacheEntry, cancellationToken: token).ConfigureAwait(false);
    }

    private SemaphoreSlim DCTokenCacheLock { get; } = new(1);
    private async Task<(ulong CharacterId, ushort WorldId, uint DCToken)?> GetDCTokenCacheEntryAsync(IPEndPoint endpoint, VersionInfo versionInfo, CancellationToken token)
    {
        if (!(DCTokenCache?.Exists ?? false))
            return null;
        
        var versionHash = SHA1.HashData(JsonSerializer.SerializeToUtf8Bytes(versionInfo, SerCtx.Default.VersionInfo));

        await DCTokenCacheLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var c = DCTokenCache.OpenRead();
            var entries = c.Length == 0 ? null : await JsonSerializer.DeserializeAsync(c, SerCtx.Default.DictionaryStringDCTokenCacheEntry, cancellationToken: token).ConfigureAwait(false);
            if (entries == null)
                return null;
            if (!entries.TryGetValue(endpoint.ToString(), out var entry))
                return null;
            if (!entry.VersionInfoHash.AsSpan().SequenceEqual(versionHash))
                return null;
            if (entry.CreationDate + DCTokenTTL <= DateTime.UtcNow)
                return null;
            return (entry.CharacterId, entry.WorldId, entry.DCToken);
        }
        finally
        {
            DCTokenCacheLock.Release();
        }
    }

    private async Task WriteDCTokenCacheEntryAsync(IPEndPoint endpoint, VersionInfo versionInfo, ulong characterId, ushort worldId, uint dcToken, CancellationToken token)
    {
        if (DCTokenCache == null)
            return;

        var versionHash = SHA1.HashData(JsonSerializer.SerializeToUtf8Bytes(versionInfo, SerCtx.Default.VersionInfo));

        await DCTokenCacheLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var c = DCTokenCache.Open(FileMode.OpenOrCreate, FileAccess.ReadWrite);
            var entries = c.Length == 0 ? [] : await JsonSerializer.DeserializeAsync(c, SerCtx.Default.DictionaryStringDCTokenCacheEntry, cancellationToken: token).ConfigureAwait(false) ?? [];
            entries[endpoint.ToString()] = new() { CharacterId = characterId, WorldId = worldId, DCToken = dcToken, CreationDate = DateTime.UtcNow, VersionInfoHash = versionHash };
            c.SetLength(0);
            await JsonSerializer.SerializeAsync(c, entries, SerCtx.Default.DictionaryStringDCTokenCacheEntry).ConfigureAwait(false);
        }
        finally
        {
            DCTokenCacheLock.Release();
        }
    }
}
