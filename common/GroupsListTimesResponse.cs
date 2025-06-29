using System;
using System.Collections.ObjectModel;

namespace mcnntp.common;

public readonly record struct GroupsListTimesResponse : INntpResponse
{
    public readonly record struct GroupTimeEntry
    {
        required public readonly string Group { get; init; }
        required public readonly int EpochSeconds { get; init; }
        required public readonly string Creator { get; init; }
    }

    public readonly int Code { get; private init; }
    public readonly string? Message { get; private init; }
    public readonly ReadOnlyCollection<GroupTimeEntry> Groups { get; private init; }

    public GroupsListTimesResponse(int code, string? message, ReadOnlyCollection<GroupTimeEntry>? groups)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        Groups = groups ?? new ReadOnlyCollection<GroupTimeEntry>(Array.Empty<GroupTimeEntry>());
    }

    public override string ToString() => $"{Code} {Message}";
}