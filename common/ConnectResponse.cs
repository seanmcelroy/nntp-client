namespace mcnntp.common;

public readonly record struct ConnectResponse : INntpResponse
{
    public readonly int Code { get; private init; }
    public readonly string? Message { get; private init; }
    public bool CanPost { get; private init; }

    public ConnectResponse(INntpResponse response, bool canPost)
    {
        Code = response.Code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = response.Message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        CanPost = canPost;
    }

    public ConnectResponse(int code, string message, bool canPost)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        CanPost = canPost;
    }

    public override string ToString() => $"{Code} {Message}";
}