using System.Collections.ObjectModel;

namespace mcnntp.common;

public readonly record struct GroupsListResponse : INntpResponse
{
    public readonly record struct GroupEntry
    {
        required public readonly string Group { get; init; }
        required public readonly int HighWatermark { get; init; }
        required public readonly int LowWatermark { get; init; }
        required public readonly char Status { get; init; }
    }

    public readonly int Code { get; private init; }
    public readonly string? Message { get; private init; }
    public readonly ReadOnlyCollection<GroupEntry> Groups { get; private init; }

    public GroupsListResponse(int code, string? message, ReadOnlyCollection<GroupEntry>? groups)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        Groups = groups ?? new ReadOnlyCollection<GroupEntry>(System.Array.Empty<GroupEntry>());
    }

    public override string ToString() => $"{Code} {Message}";
}
