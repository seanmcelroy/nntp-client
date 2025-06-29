namespace mcnntp.common;

public readonly record struct ErrorResponse : INntpResponse
{
    public readonly int Code { get; private init; }

    public readonly string? Message { get; private init; }

    public readonly ErrorType ErrorType { get; private init; }

    public ErrorResponse(
        int code,
        string? message,
        ErrorType errorType)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        ErrorType = errorType;
    }

    public override string ToString() => $"{Code} {Message}";
}
