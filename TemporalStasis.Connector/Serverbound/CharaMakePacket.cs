using System.Runtime.InteropServices;

namespace TemporalStasis.Connector.Serverbound;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 496)]
public unsafe struct CharaMakePacket
{
    public uint RequestNumber;
    public uint Unknown4;
    public ulong CharacterId;
    public ulong Padding;
    public byte CharacterIdx;
    public OperationType Operation;
    public ushort WorldId;
    public unsafe fixed char CharacterName[32];
    public unsafe fixed char CharaMakeData[436];

    public enum OperationType : byte
    {
        Unknown = 0x0,
        ReserveName = 0x1,
        MakeChara = 0x2,
        RenameChara = 0x3,
        DeleteChara = 0x4,
        MoveChara = 0x5,
        RemakeRetainer = 0x6,
        RemakeChara = 0x7,
        SettingsUploadBegin = 0x8,
        SettingsUpload = 0xC,
        WorldVisit = 0xE,
        DatacenterToken = 0xF,
        Disconnect = 0x13, // DC Travel(?)
        RetrieveCharaMakeData = 0x15,
    }

    public static CharaMakePacket GetDCTravelToken(uint reqNumber, ulong characterId, byte characterIdx)
    {
        return new CharaMakePacket()
        {
            RequestNumber = reqNumber,
            CharacterId = characterId,
            CharacterIdx = characterIdx,
            Operation = OperationType.DatacenterToken
        };
    }

    public readonly byte[] Generate()
    {
        using var s = new MemoryStream();
        s.WriteStruct(this);
        return s.ToArray();
    }
}
