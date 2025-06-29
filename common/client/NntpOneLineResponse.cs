namespace mcnntp.common.client;

internal readonly record struct NntpOneLineResponse : INntpResponse
{
    public readonly int Code { get; private init; }
    public readonly string? Message { get; private init; }
    internal readonly string Line { get; private init; }

    internal NntpOneLineResponse(int code, string message, string line)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        Line = line;
    }

    public override string ToString() => $"{Code} {Message}";
}
