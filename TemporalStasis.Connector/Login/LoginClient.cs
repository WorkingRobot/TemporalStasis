using System.Text;
using System.Web;
using System.Net;
using System.Security.Cryptography;

namespace TemporalStasis.Connector.Login;

internal sealed class LoginClient : IDisposable
{
    private HttpClient Client { get; }
    private HttpClient PatchClient { get; }

    public LoginClient()
    {
        Client = new(new HttpClientHandler()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            UseCookies = false
        });

        var hashString = Environment.MachineName + Environment.UserName + Environment.OSVersion +
                         Environment.ProcessorCount;
        var sha1 = SHA1.HashData(Encoding.Unicode.GetBytes(hashString))[..4];
        var checksum = (byte)-sha1.Sum(x => x);
        var computerId = $"{checksum:x2}{Convert.ToHexString(sha1).ToLowerInvariant()}";

        Client.DefaultRequestHeaders.AddWithoutValidation("User-Agent", $"SQEXAuthor/2.0.0(Windows 6.2; ja-jp; {computerId})");
        Client.DefaultRequestHeaders.AddWithoutValidation("Accept-Encoding", "gzip, deflate");
        Client.DefaultRequestHeaders.AddWithoutValidation("Accept-Language", "en-us");
        Client.DefaultRequestHeaders.AddWithoutValidation("Origin", "https://launcher.finalfantasyxiv.com");
        Client.DefaultRequestHeaders.AddWithoutValidation("Referer", $"https://launcher.finalfantasyxiv.com/v620/index.html?rc_lang=en_us&time={DateTime.UtcNow:yyyy-MM-dd-HH-mm}");

        PatchClient = new(new HttpClientHandler()
        {
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
        });
        PatchClient.DefaultRequestHeaders.AddWithoutValidation("User-Agent", "FFXIV PATCH CLIENT");
    }

    public readonly record struct LoginToken(Uri Referer, string StoredToken);

    // https://github.com/WorkingRobot/L4-cpp/blob/e8c669464a8b756608019d6c0ee1b94fb40ab65b/plugins/ffxiv/web/LauncherClient.cpp#L123
    public async Task<LoginToken> GetLoginTokenAsync(bool isFreeTrial)
    {
        var uri = new UriBuilder("https://ffxiv-login.square-enix.com/oauth/ffxivarr/login/top");
        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs.Add("lng", "en");
        qs.Add("rgn", "3");
        qs.Add("isft", isFreeTrial ? "1" : "0");
        qs.Add("cssmode", "1");
        qs.Add("isnew", "1");
        qs.Add("launchver", "3");

        uri.Query = qs.ToString();

        using var request = new HttpRequestMessage(HttpMethod.Get, uri.Uri);

        request.Headers.AddWithoutValidation("Accept", "image/gif, image/jpeg, image/pjpeg, application/x-ms-application, application/xaml+xml, application/x-ms-xbap, */*");
        request.Headers.AddWithoutValidation("Cookie", "_rsid=\"\"");

        var resp = await Client.SendAsync(request).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var str = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (str.Contains("window.external.user(\"restartup\");"))
            throw new Exception("No Steam account is linked");

        return new(uri.Uri, ScrapeData(str, "name=\"_STORED_\" value=\"", "\"").ToString());
    }

    private static ReadOnlySpan<char> ScrapeData(ReadOnlySpan<char> span, ReadOnlySpan<char> begin, ReadOnlySpan<char> end)
    {
        var beginPos = span.IndexOf(begin);
        ArgumentOutOfRangeException.ThrowIfNegative(beginPos);

        span = span[(beginPos + begin.Length)..];

        var endPos = span.IndexOf(end);
        ArgumentOutOfRangeException.ThrowIfNegative(endPos);

        return span[..endPos];
    }

    public readonly record struct LoginResult(string SessionId, int Region, bool TermsAccepted, bool Playable, int MaxExpansion);

    public async Task<LoginResult> LoginOAuth(LoginToken token, string username, string password, string? otp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://ffxiv-login.square-enix.com/oauth/ffxivarr/login/login.send");

        request.Headers.AddWithoutValidation("Accept", "image/gif, image/jpeg, image/pjpeg, application/x-ms-application, application/xaml+xml, application/x-ms-xbap, */*");
        request.Headers.AddWithoutValidation("Referer", token.Referer.ToString());
        request.Headers.AddWithoutValidation("Cache-Control", "no-cache");
        request.Headers.AddWithoutValidation("Cookie", "_rsid=\"\"");

        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>()
        {
            { "_STORED_", token.StoredToken },
            { "sqexid", username },
            { "password", password },
            { "otppw", otp ?? string.Empty },
        });

        var resp = await Client.SendAsync(request).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var str = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

        var prms = ScrapeData(str, "window.external.user(\"login=auth,", "\");").ToString().Split(',');
        if (prms[0] == "ok")
            return new(prms[2], int.Parse(prms[6]), prms[4] != "0", prms[10] != "0", int.Parse(prms[14]));
        else if (prms[1] == "err")
            throw new InvalidOperationException(prms[2]);
        else
            throw new InvalidOperationException($"Unknown error during login: {string.Join(',', prms)}");
    }

    public async Task<string> GetUniqueIdAsync(LoginResult loginData, string gameVersion, string versionReport)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://patch-gamever.ffxiv.com/http/win32/ffxivneo_release_game/{gameVersion}/{loginData.SessionId}");

        request.Headers.AddWithoutValidation("X-Hash-Check", "enabled");

        request.Content = new StringContent(versionReport);
        request.Content.Headers.Clear();

        var resp = await PatchClient.SendAsync(request).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var str = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!string.IsNullOrEmpty(str))
            throw new InvalidOperationException("Invalid version report; game needs patching");

        return resp.Headers.GetValues("X-Patch-Unique-Id").First();
    }

    public static string GenerateVersionReport(string bootVersion, FileReport[] bootFiles, ReadOnlySpan<string> exVersions)
    {
        var sb = new StringBuilder();
        sb.Append(bootVersion);
        sb.Append('=');
        var i = 0;
        foreach (var file in bootFiles)
        {
            sb.Append(file.Report);
            if (++i != bootFiles.Length)
                sb.Append(',');
        }
        sb.Append('\n');
        i = 0;
        foreach(var exVersion in exVersions)
        {
            sb.Append("ex");
            sb.Append(++i);
            sb.Append('\t');
            sb.Append(exVersion);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        Client.Dispose();
        PatchClient.Dispose();
    }
}
