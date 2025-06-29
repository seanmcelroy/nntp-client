namespace mcnntp.common;

public readonly record struct GroupResponse : INntpResponse
{
    public readonly int Code { get; private init; }
    public readonly string? Message { get; private init; }
    public string GroupName { get; private init; }
    public int EstimatedArticleCount { get; private init; }
    public int LowWatermark { get; private init; }
    public int HighWatermark { get; private init; }

    public bool IsValidGroup => Code == NntpResponseCodes.GroupSelected;

    public GroupResponse(int code, string? message, string groupName, int estimatedArticleCount, int lowWatermark, int highWatermark)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        GroupName = groupName;
        EstimatedArticleCount = code == NntpResponseCodes.GroupSelected ? estimatedArticleCount : 0;
        LowWatermark = code == NntpResponseCodes.GroupSelected ? lowWatermark : 0;
        HighWatermark = code == NntpResponseCodes.GroupSelected ? highWatermark : 0;
    }

    public override string ToString() => $"{Code} {Message}";
}
