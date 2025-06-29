using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace mcnntp.common;

public readonly record struct GroupListResponse : INntpResponse
{
    public readonly int Code { get; private init; }
    public readonly string? Message { get; private init; }
    public readonly string GroupName { get; private init; }
    public readonly int EstimatedArticleCount { get; private init; }
    public readonly int LowWatermark { get; private init; }
    public readonly int HighWatermark { get; private init; }
    public readonly IReadOnlySet<int> ArticleNumbers { get; private init; }

    public GroupListResponse(int code, string? message, string groupName, int estimatedArticleCount, int lowWatermark, int highWatermark, IEnumerable<int> articleNumbers)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        GroupName = groupName;
        EstimatedArticleCount = estimatedArticleCount;
        LowWatermark = lowWatermark;
        HighWatermark = highWatermark;
        ArticleNumbers = new ReadOnlySet<int>(articleNumbers.Distinct().ToHashSet());
    }

    public override string ToString() => $"{Code} {Message}";
}
