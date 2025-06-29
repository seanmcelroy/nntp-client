using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace mcnntp.common;

public readonly record struct ArticleResponse : INntpResponse
{
    public readonly int Code { get; private init; }
    public readonly string? Message { get; private init; }
    public readonly ReadOnlyCollection<string> Lines { get; private init; }

    public ArticleResponse(int code, string? message, ReadOnlyCollection<string>? lines)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        Lines = lines ?? new ReadOnlyCollection<string>([]);
    }

    public ArticleResponse(int code, string? message, IEnumerable<string> lines)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        Lines = new ReadOnlyCollection<string>(lines.ToList());
    }

    public IEnumerable<KeyValuePair<string, string>> GetHeaders() => StringUtility.GetHeaders(Lines);

    public IEnumerable<string> GetHeaderValues(string headerName)
    {
        ArgumentNullException.ThrowIfNull(headerName);

        if (this.Lines == null)
            throw new InvalidOperationException("No lines are part of this response");

        foreach (var kvp in GetHeaders())
            if (string.Equals(kvp.Key, headerName, StringComparison.OrdinalIgnoreCase))
                yield return kvp.Value;
    }

    public override string ToString() => $"{Code} {Message}";
}
