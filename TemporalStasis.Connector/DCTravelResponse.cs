using System.Text.Json.Serialization;

namespace TemporalStasis.Connector;

public readonly struct DCTravelResponse
{
    [JsonPropertyName("error")]
    public readonly string? Error { get; init; }
    [JsonPropertyName("result")]
    public readonly DCTravelResult Result { get; init; }
}

public readonly struct DCTravelResult
{
    [JsonPropertyName("return_code")]
    public readonly string Code { get; init; }
    [JsonPropertyName("return_status")]
    public readonly string Status { get; init; }
    [JsonPropertyName("return_errcode")]
    public readonly string ErrCode { get; init; }

    // Data
}
