namespace TemporalStasis.Structs;

public enum SegmentType : ushort {
    SessionInit = 1,
    Ipc = 3,
    KeepAlive = 7,
    KeepAlivePong = 8,
    EncryptionInit = 9,
    EncryptedData = 10
}
