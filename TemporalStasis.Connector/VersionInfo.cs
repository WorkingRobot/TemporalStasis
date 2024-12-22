namespace TemporalStasis.Connector;

public readonly struct VersionInfo
{
    public readonly string BlowfishPhrase { get; init; }
    public readonly uint BlowfishVersion { get; init; }
    public readonly ushort LoginVersion { get; init; }
    public readonly string BootVersion { get; init; }
    public readonly string GameVersion { get; init; }
    public readonly string[] ExVersions { get; init; }

    public readonly FileReport GameExe { get; init; }
    public readonly FileReport[] BootHashes { get; init; }
}
