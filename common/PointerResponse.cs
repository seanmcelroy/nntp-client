namespace mcnntp.common;

public readonly record struct PointerResponse : INntpResponse
{
    public readonly int Code { get; private init; }
    public readonly string? Message { get; private init; }
    public int? ArticleNumber { get; private init; }
    public string? MessageId { get; private init; }

    public PointerResponse(int code, string? message, int? articleNumber, string? messageId)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        ArticleNumber = articleNumber;
        MessageId = messageId;
    }

    public override string ToString() => $"{Code} {Message}";
}
