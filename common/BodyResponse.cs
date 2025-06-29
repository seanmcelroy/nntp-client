using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace mcnntp.common;

public readonly record struct BodyResponse : INntpResponse
{
    public readonly int Code { get; private init; }
    public readonly string? Message { get; private init; }
    public readonly ReadOnlyCollection<string> Lines { get; private init; }

    public BodyResponse(int code, string? message, ReadOnlyCollection<string>? lines)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        Lines = lines ?? new ReadOnlyCollection<string>(System.Array.Empty<string>());
    }

    public BodyResponse(int code, string? message, IEnumerable<string> lines)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        Lines = new ReadOnlyCollection<string>([.. lines]);
    }

    public override string ToString() => $"{Code} {Message}";
}
