using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace mcnntp.common;

public readonly record struct NewNewsResponse : INntpResponse
{
    public readonly int Code { get; private init; }
    public readonly string? Message { get; private init; }
    public readonly ReadOnlySet<string> MessageIds { get; private init; }

    public NewNewsResponse(int code, string? message, ReadOnlyCollection<string>? messageIds)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        MessageIds = messageIds == null ? ReadOnlySet<string>.Empty : new ReadOnlySet<string>(messageIds.Distinct().ToHashSet());
    }

    public NewNewsResponse(int code, string message, IEnumerable<string> messageIds)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        MessageIds = messageIds == null ? ReadOnlySet<string>.Empty : new ReadOnlySet<string>(messageIds.Distinct().ToHashSet());
    }

    public override string ToString() => $"{Code} {Message}";
}
