namespace mcnntp.common;

public readonly record struct AuthenticateResponse : INntpResponse
{
    public readonly int Code { get; private init; }
    public readonly string? Message { get; private init; }

    public AuthenticateResponse(INntpResponse response)
    {
        Code = response.Code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = response.Message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
    }

    public AuthenticateResponse(int code, string? message)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
    }

    public override string ToString() => $"{Code} {Message}";
}
