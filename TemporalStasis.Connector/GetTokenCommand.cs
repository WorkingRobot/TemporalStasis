using DotMake.CommandLine;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using TemporalStasis.Connector.Login;

namespace TemporalStasis.Connector;

[CliCommand(Description = "Get a DC travel token")]
public class GetTokenCommand
{
    private static Task<int> Main(string[] args) =>
        Cli.RunAsync<GetTokenCommand>(args);

    [CliArgument(Required = true, Description = "The lobby endpoint to access. Accepts DNS names and IPs")]
    public required string LobbyHost { get; set; }

    [CliArgument(Required = false, Description = "The lobby point to use")]
    public int LobbyPort { get; set; } = 54994;

    [CliOption(Required = false, Description = "A json file to where all the version information is stored")]
    public FileInfo? VersionFile { get; set; }

    [CliOption(Required = false, Description = "A json url to where all the version information is stored", ValidationRules = CliValidationRules.LegalUrl)]
    public Uri? VersionUrl { get; set; }

    [CliOption(Required = false)]
    public string? Username { get; set; }

    [CliOption(Required = false)]
    public string? Password { get; set; }

    [CliOption(Required = false)]
    public string? UID { get; set; }

    [CliOption(Required = false)]
    public int? MaxExpansion { get; set; }

    [CliOption(Name = "--free-trial")]
    public bool IsFreeTrial { get; set; } = false;

    [CliOption(Name = "--only-dc-token")]
    public bool OnlyDCToken { get; set; } = false;

    [CliOption(Name = "--only-uid-data")]
    public bool OnlyUIDData { get; set; } = false;

    [CliOption]
    public bool Verbose { get; set; } = false;

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
            versionInfo = await JsonSerializer.DeserializeAsync<VersionInfo>(f, cancellationToken: cts.Token).ConfigureAwait(false);
        }
        else if (VersionUrl != null)
        {
            using var client = new HttpClient();
            versionInfo = await client.GetFromJsonAsync<VersionInfo>(VersionUrl, cts.Token).ConfigureAwait(false);
        }
        else
            throw new ArgumentException("Either version file or version url must be specified");

        if (!IPAddress.TryParse(LobbyHost, out var lobbyAddress))
        {
            var t = await Dns.GetHostEntryAsync(LobbyHost).ConfigureAwait(false);
            lobbyAddress = t.AddressList[0];
        }

        LoginInfo loginInfo;
        if (!string.IsNullOrEmpty(UID) && MaxExpansion.HasValue)
        {
            loginInfo = new()
            {
                UniqueId = UID,
                IsSteam = false,
                MaxExpansion = MaxExpansion.Value
            };
        }
        else if (!string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
        {
            using var loginClient = new LoginClient();
            var loginToken = await loginClient.GetLoginTokenAsync(IsFreeTrial).ConfigureAwait(false);
            var loginData = await loginClient.LoginOAuth(loginToken, Username, Password, null).ConfigureAwait(false);
            var report = LoginClient.GenerateVersionReport(versionInfo.BootVersion, versionInfo.BootHashes, versionInfo.ExVersions.AsSpan()[..loginData.MaxExpansion]);
            var uniqueId = await loginClient.GetUniqueIdAsync(loginData, versionInfo.GameVersion, report).ConfigureAwait(false);
            loginInfo = new()
            {
                UniqueId = uniqueId,
                IsSteam = false,
                MaxExpansion = loginData.MaxExpansion
            };
        }
        else
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

        using var runner = new Runner(new(lobbyAddress, LobbyPort), versionInfo, loginInfo, cts.Token);

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
                if (OnlyDCToken)
                {
                    Console.WriteLine(token);
                    Console.WriteLine(character.WorldId);
                    Console.WriteLine(character.CharacterId);
                    return;
                }

                if (Verbose)
                {
                    Console.WriteLine($"DC Travel Token: {token:X8}");
                    Console.WriteLine($"World Id: {character.WorldId}");
                    Console.WriteLine($"Character Id: {character.CharacterId:X16}");
                }

                using var http = new HttpClient();
                http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "FFXIV CLIENT");

                var uri = new UriBuilder("https://dctravel.ffxiv.com/worlds");
                var qs = HttpUtility.ParseQueryString(string.Empty);
                qs.Add("token", token.ToString());
                qs.Add("worldId", character.WorldId.ToString());
                qs.Add("characterId", character.CharacterId.ToString());
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
                if (Verbose)
                {
                    Console.WriteLine();
                    Console.WriteLine("Travel Response:");
                }
                Console.WriteLine(await ret.Content.ReadAsStringAsync().ConfigureAwait(false));
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
}
