using System.Collections.ObjectModel;
using System.Linq;

namespace mcnntp.common;

public readonly record struct CapabilitiesResponse : INntpResponse
{
    public readonly int Code { get; private init; }
    public readonly string? Message { get; private init; }
    public readonly ReadOnlySet<string> Capabilities { get; private init; }

    public CapabilitiesResponse(int code, string? message, ReadOnlyCollection<string>? lines)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        Capabilities = lines == null
            ? ReadOnlySet<string>.Empty
            : new ReadOnlySet<string>(lines.Distinct().ToHashSet()) ?? ReadOnlySet<string>.Empty;
    }

    public override string ToString() => $"{Code} {Message}";
}
