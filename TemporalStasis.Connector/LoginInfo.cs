namespace TemporalStasis.Connector;

public readonly struct LoginInfo
{
    public readonly string UniqueId { get; init; }
    public readonly bool IsSteam { get; init; }
    public readonly int MaxExpansion { get; init; }
}
