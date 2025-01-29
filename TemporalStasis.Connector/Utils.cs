using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using static TemporalStasis.Connector.GetTokenCommand;

namespace TemporalStasis.Connector;

internal static class Utils
{
    public static void AddWithoutValidation(this HttpHeaders headers, string key, string value)
    {
        var res = headers.TryAddWithoutValidation(key, value);

        if (!res)
            throw new ArgumentException("Could not add header", nameof(key));
    }

    public static void AddRangeIf<T>(this List<T> me, ReadOnlySpan<T> span, Predicate<T> predicate)
    {
        foreach (ref readonly var i in span)
        {
            if (predicate(i))
                me.Add(i);
        }
    }
}

public readonly record struct FileReport(string FileName, long FileSize, byte[] Sha1Hash)
{
    [JsonIgnore]
    public string Report =>
        $"{FileName}/{FileSize}/{Convert.ToHexString(Sha1Hash).ToLowerInvariant()}";
}

[JsonSerializable(typeof(VersionInfo))]
[JsonSerializable(typeof(Dictionary<string, UIDCacheEntry>))]
[JsonSerializable(typeof(Dictionary<string, DCTokenCacheEntry>))]
[JsonSerializable(typeof(DCTravelResponse))]
public partial class ConnectorSerializerContext : JsonSerializerContext
{

}
