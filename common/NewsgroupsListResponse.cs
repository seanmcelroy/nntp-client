using System;
using System.Collections.ObjectModel;

namespace mcnntp.common;

public readonly record struct NewsgroupsListResponse : INntpResponse
{
    public readonly record struct GroupEntry
    {
        public readonly string Group { get; init; }
        public readonly string Description { get; init; }
    }

    public readonly int Code { get; private init; }

    public readonly string? Message { get; private init; }

    public readonly ReadOnlyCollection<GroupEntry> Groups { get; private init; }

    public NewsgroupsListResponse(int code, string? message, ReadOnlyCollection<GroupEntry>? groups)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        Groups = groups ?? new ReadOnlyCollection<GroupEntry>(Array.Empty<GroupEntry>());
    }

    public override string ToString() => $"{Code} {Message}";
}
