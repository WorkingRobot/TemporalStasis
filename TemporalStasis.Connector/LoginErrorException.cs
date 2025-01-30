using TemporalStasis.Connector.Clientbound;

namespace TemporalStasis.Connector;


public class LoginErrorException(LoginErrorPacket data) : Exception($"Login error; Code: {data.ErrorCode}; Param: {data.ErrorParam}; Row: {data.ErrorSheetRow}; {data.Message}")
{
    public ushort ErrorCode { get; } = data.ErrorCode;
    public uint ErrorParam { get; } = data.ErrorParam;
    public ushort ErrorSheetRow { get; } = data.ErrorSheetRow;
    public string ErrorMessage { get; } = data.Message;
}