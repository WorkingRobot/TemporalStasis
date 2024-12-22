using System.Net;
using System.Web;
using TemporalStasis.Connector.Login;

namespace TemporalStasis.Connector;
internal static class Program
{
    const bool IsLocal = false;

    const string Username = "nice try";
    const string Password = "nice try";
    const string? Otp = null;
    const bool IsFreeTrial = true;
    const bool IsSteam = false;

    private static readonly VersionInfo VersionInfo = new()
    {
        BlowfishPhrase = "06c67fb0bb427f0e2f12433b9172f12e",
        BlowfishVersion = 7000,
        LoginVersion = 7000,

        BootVersion = "2024.11.01.0001.0001",
        GameVersion = "2024.12.07.0000.0000",
        ExVersions = ["2024.11.19.0000.0000", "2024.11.19.0000.0000", "2024.12.07.0000.0000", "2024.12.07.0000.0000", "2024.12.07.0000.0000"],

        GameExe = new("ffxiv_dx11.exe", 48641808, Convert.FromHexString("1c4d47684f5f25e8d17367ec9b38e582d1744262")),
        BootHashes = [
            new("ffxivboot.exe", 1089296, Convert.FromHexString("a351529a185333aaaa8e91f138efc3729ec7900d")),
            new("ffxivboot64.exe", 1282320, Convert.FromHexString("4a1f28c1d29a83e0070d4506366211115e89c12d")),
            new("ffxivlauncher64.exe", 13871376, Convert.FromHexString("17c033e02bd74e575472dbcdbbb4d341b41189d0")),
            new("ffxivupdater64.exe", 1338128, Convert.FromHexString("165380fcf21f5b610085b3fc66f91f3c0513eaf8")),
        ]
    };

    private static async Task Main(string[] args)
    {
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            cts.Cancel();
            eventArgs.Cancel = true;
        };

        IPEndPoint lobbyEndpoint;
        {
            var aether = await Dns.GetHostEntryAsync("neolobby05.ffxiv.com").ConfigureAwait(false);
            var addr = IsLocal ? IPAddress.Loopback : aether.AddressList[0];
            lobbyEndpoint = new(addr, IsLocal ? 44994 : 54994);
        }

        LoginInfo loginInfo;
        {
            using var loginClient = new LoginClient();
            var loginToken = await loginClient.GetLoginTokenAsync(IsFreeTrial).ConfigureAwait(false);
            var loginData = await loginClient.LoginOAuth(loginToken, Username, Password, Otp).ConfigureAwait(false);
            var report = LoginClient.GenerateVersionReport(VersionInfo.BootVersion, VersionInfo.BootHashes, VersionInfo.ExVersions.AsSpan()[..loginData.MaxExpansion]);
            var uniqueId = await loginClient.GetUniqueIdAsync(loginData, VersionInfo.GameVersion, report).ConfigureAwait(false);
            loginInfo = new()
            {
                UniqueId = uniqueId,
                IsSteam = false,
                MaxExpansion = loginData.MaxExpansion
            };
        }
        Console.WriteLine($"UID: {loginInfo.UniqueId}");
        Console.WriteLine();

        using var runner = new Runner(lobbyEndpoint, VersionInfo, loginInfo, cts.Token);

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

        runner.OnLogin += () =>
        {
            if (runner.Characters.Count == 0)
                throw new ArgumentException("No active accounts");
            var character = runner.Characters[0];

            _ = Task.Run(async () =>
            {
                var token = await runner.GetDCTravelToken(character).ConfigureAwait(false);
                Console.WriteLine($"DC Travel Token: {token:X8}");
                Console.WriteLine($"World Id: {character.WorldId}");
                Console.WriteLine($"Character Id: {character.CharacterId:X16}");

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
                Console.WriteLine();
                Console.WriteLine("Travel Response:");
                Console.WriteLine(await ret.Content.ReadAsStringAsync().ConfigureAwait(false));
                Console.WriteLine();
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
