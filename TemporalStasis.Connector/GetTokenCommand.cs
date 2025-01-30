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
    private static async Task<int> Main(string[] args)
    {
        try
        {
            return await Cli.RunAsync<GetTokenCommand>(args).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Error(e);
            return 1;
        }
    }

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

    [CliOption(Description = "Implies --verbose")]
    public bool Debug { get; set; } = false;

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
        Log.IsDebugEnabled = Debug;
        Log.IsVerboseEnabled = Debug || Verbose;

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

        if (OnlyUIDData)
        {
            var loginInfo = await GetLoginInfoAsync(versionInfo, useCache: true, cts.Token);

            Log.Output(loginInfo.UniqueId);
            Log.Output(loginInfo.MaxExpansion);
            return;
        }

        var runnerTasks = endpoints.Select(e => ExecuteRunner(e, versionInfo, useUidCache: true, useTokenCache: true, cts.Token));
        await Task.WhenAll(runnerTasks).ConfigureAwait(false);
    }

    private SemaphoreSlim LoginInfoLock { get; } = new(1);
    private async Task<LoginInfo> GetLoginInfoAsync(VersionInfo versionInfo, bool useCache, CancellationToken token)
    {
        await LoginInfoLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrEmpty(UID) && MaxExpansion.HasValue)
            {
                Log.Verbose("Using explicit UID");
                var ret = new LoginInfo()
                {
                    UniqueId = UID,
                    IsSteam = false,
                    MaxExpansion = MaxExpansion.Value
                };
                await WriteUIDCacheEntryAsync(ret, token).ConfigureAwait(false);
                return ret;
            }

            if (useCache)
            {
                Log.Verbose("Checking for UID");
                var ret = await GetUIDCacheEntryAsync(token).ConfigureAwait(false);

                if (ret.HasValue)
                    return ret.Value;
            }
            else
                Log.Verbose("Skipping UID cache");

            if (!string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
            {
                Log.Verbose("Logging in with username/password");
                using var loginClient = new LoginClient();
                var loginToken = await loginClient.GetLoginTokenAsync(IsFreeTrial).ConfigureAwait(false);
                var loginData = await loginClient.LoginOAuth(loginToken, Username, Password, null).ConfigureAwait(false);
                var report = LoginClient.GenerateVersionReport(versionInfo.BootVersion, versionInfo.BootHashes, versionInfo.ExVersions.AsSpan()[..loginData.MaxExpansion]);
                var uniqueId = await loginClient.GetUniqueIdAsync(loginData, versionInfo.GameVersion, report).ConfigureAwait(false);
                var ret = new LoginInfo()
                {
                    UniqueId = uniqueId,
                    IsSteam = false,
                    MaxExpansion = loginData.MaxExpansion
                };
                await WriteUIDCacheEntryAsync(ret, token).ConfigureAwait(false);
                return ret;
            }
        }
        finally
        {
            LoginInfoLock.Release();
        }

        throw new ArgumentException("Either username/password or uid/max expansion must be specified");
    }

    private async Task ExecuteRunner(IPEndPoint endpoint, VersionInfo versionInfo, bool useUidCache, bool useTokenCache, CancellationToken token)
    {
        var loginInfo = await GetLoginInfoAsync(versionInfo, useUidCache, token).ConfigureAwait(false);
        Log.Verbose($"UID: {loginInfo.UniqueId}");

        var dcTokenData = useTokenCache ? await GetDCTokenCacheEntryAsync(endpoint, versionInfo, token).ConfigureAwait(false) : null;

        if (!dcTokenData.HasValue)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(token);

            using var runner = new Runner(endpoint, versionInfo, loginInfo, cts.Token);

            runner.OnLogin += () =>
            {
                Log.Verbose("Worlds:");
                foreach (var world in runner.Worlds)
                    Log.Verbose($"World {world.Id}: {world.Name}");
                Log.Verbose();

                Log.Verbose("FFXI Characters:");
                foreach (var character in runner.XiCharacters)
                    Log.Verbose($"XiChar {character.Id:X8}: {character.Name} (World {character.WorldParam})");
                Log.Verbose();

                Log.Verbose("Retainers:");
                foreach (var retainer in runner.Retainers)
                    Log.Verbose($"Retainer {retainer.Id:X16} (Owner {retainer.OwnerId:X16}): {retainer.Name}");
                Log.Verbose();

                Log.Verbose("Characters:");
                foreach (var character in runner.Characters)
                {
                    Log.Verbose($"Character {character.CharacterId:X16} (Player {character.PlayerId:X16}): {character.Name}");
                    Log.Verbose($"  At {character.WorldName} ({character.WorldId})");
                    Log.Verbose($"  Home {character.HomeWorldName} ({character.HomeWorldId})");
                    Log.Verbose($"  JSON: {character.Json}");
                }
                Log.Verbose();

                return Task.CompletedTask;
            };

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
            catch (LoginErrorException e)
            {
                if (useUidCache && e.ErrorCode == 5006)
                {
                    Log.Warn($"Login error, retrying without cache");
                    Log.Warn(e.Message);

                    await ExecuteRunner(endpoint, versionInfo, useUidCache: false, useTokenCache: false, token).ConfigureAwait(false);
                    return;
                }
                throw;
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
            Log.Output(dcToken);
            Log.Output(worldId);
            Log.Output(characterId);
            return;
        }

        Log.Verbose($"DC Travel Token: {dcToken:X8}");
        Log.Verbose($"World Id: {worldId}");
        Log.Verbose($"Character Id: {characterId:X16}");

        var (errorCode, resp) = await ExecuteDCToken(dcToken, worldId, characterId, token).ConfigureAwait(false);
        if (!errorCode.HasValue)
        {
            Log.Verbose();
            Log.Verbose("Travel Response:");
            Log.Output(resp);
        }
        else
        {
            if (useTokenCache && errorCode == 101) // PARAM_ERROR
            {
                Log.Verbose();
                Log.Warn("Recieved PARAM_ERROR, retrying without cache");

                await ExecuteRunner(endpoint, versionInfo, useUidCache, useTokenCache: false, token).ConfigureAwait(false);
                return;
            }

            Log.Verbose();
            Log.Verbose("Travel Error:");
            Log.Output(resp);
        }
    }

    private static async Task<(int?, string)> ExecuteDCToken(uint dcToken, ushort worldId, ulong characterId, CancellationToken token)
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
        var resp = await ret.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        var json_resp = JsonSerializer.Deserialize(resp, SerCtx.Default.DCTravelResponse);
        if (json_resp.Error != null)
            return (int.Parse(json_resp.Result.ErrCode), resp);
        return (null, resp);
    }


    private static Dictionary<string, UIDCacheEntry> BackupUIDCache = [];
    private async Task<LoginInfo?> GetUIDCacheEntryAsync(CancellationToken token)
    {
        Dictionary<string, UIDCacheEntry> entries;

        if (!(UIDCache?.Exists ?? false))
            entries = BackupUIDCache;
        else
        {
            using var c = UIDCache.OpenRead();
            if (c.Length == 0)
                return null;
            try
            {
                entries = await JsonSerializer.DeserializeAsync(c, SerCtx.Default.DictionaryStringUIDCacheEntry, cancellationToken: token).ConfigureAwait(false) ?? [];
            }
            catch (JsonException e)
            {
                Log.Warn($"Failed to parse UIDCache while reading: {e.Message}");
                return null;
            }
        }
        if (!entries.TryGetValue(UIDCacheName, out var entry))
            return null;
        if (entry.CreationDate + UIDTTL <= DateTime.UtcNow)
            return null;
        return entry.LoginInfo;
    }

    private async Task WriteUIDCacheEntryAsync(LoginInfo loginInfo, CancellationToken token)
    {
        if (UIDCache == null)
        {
            BackupUIDCache[UIDCacheName] = new() { LoginInfo = loginInfo, CreationDate = DateTime.UtcNow };
            return;
        }

        using var c = UIDCache.Open(FileMode.OpenOrCreate, FileAccess.ReadWrite);
        Dictionary<string, UIDCacheEntry> entries = [];
        if (c.Length != 0)
        {
            try
            {
                entries = await JsonSerializer.DeserializeAsync(c, SerCtx.Default.DictionaryStringUIDCacheEntry, cancellationToken: token).ConfigureAwait(false) ?? [];
            }
            catch (JsonException e)
            {
                Log.Warn($"Failed to parse UIDCache while writing: {e.Message}");
            }
        }
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
            if (c.Length == 0)
                return null;
            Dictionary<string, DCTokenCacheEntry>? entries;
            try
            {
                entries = await JsonSerializer.DeserializeAsync(c, SerCtx.Default.DictionaryStringDCTokenCacheEntry, cancellationToken: token).ConfigureAwait(false);
            }
            catch (JsonException e)
            {
                Log.Warn($"Failed to parse DCTokenCache while reading: {e.Message}");
                return null;
            }
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
            Dictionary<string, DCTokenCacheEntry> entries = [];
            if (c.Length != 0)
            {
                try
                {
                    entries = await JsonSerializer.DeserializeAsync(c, SerCtx.Default.DictionaryStringDCTokenCacheEntry, cancellationToken: token).ConfigureAwait(false) ?? [];
                }
                catch (JsonException e)
                {
                    Log.Warn($"Failed to parse DCTokenCache while writing: {e.Message}");
                }
            }
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
