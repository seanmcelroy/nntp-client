using System.Collections.ObjectModel;

namespace mcnntp.common;

public readonly record struct ListActiveResponse : INntpResponse
{
    // RFC 3977 Section 7.6.3
    public readonly record struct NewsgroupEntry
    {
        /// <summary>
        /// The name of the newsgroup.
        /// </summary>
        required public readonly string Group { get; init; }
        /// <summary>
        /// The reported high water mark for the group.
        /// </summary>
        required public readonly int HighWatermark { get; init; }
        /// <summary>
        /// The reported low water mark for the group.
        /// </summary>
        required public readonly int LowWatermark { get; init; }
        /// <summary>
        /// The current status of the group on this server.
        /// </summary>
        required public readonly string Status { get; init; }
    }

    public readonly int Code { get; private init; }
    public readonly string? Message { get; private init; }
    public readonly ReadOnlySet<NewsgroupEntry> Groups { get; private init; }

    public ListActiveResponse(int code, string? message, ReadOnlySet<NewsgroupEntry> entries)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        Groups = entries;
    }

    public override string ToString() => $"{Code} {Message}";
}