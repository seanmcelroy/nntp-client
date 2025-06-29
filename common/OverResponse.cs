using mcnntp.common.client;

namespace mcnntp.common;

public readonly record struct OverResponse : INntpResponse
{
    public readonly int Code { get; private init; }

    public readonly string? Message { get; private init; }

    public readonly int ArticleNumber { get; init; }
    public readonly string? Subject { get; init; }
    public readonly string? From { get; init; }
    public readonly string? Date { get; init; }
    public readonly string? MessageID { get; init; }
    public readonly string? References { get; init; }
    public readonly int Bytes { get; init; }
    public readonly int Lines { get; init; }

    private OverResponse(
        int code,
        string? message,
        int articleNumber,
        string? subject,
        string? from,
        string? date,
        string? messageId,
        string references,
        int bytes,
        int lines)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        ArticleNumber = articleNumber;
        Subject = subject;
        From = from;
        Date = date;
        MessageID = messageId;
        References = references;
        Bytes = bytes;
        Lines = lines;
    }

    public static OverResponse? Parse(int code, string? message, string? line)
    {
        if (line == null)
            return null;

        var parts = line.Split('\t', 8, System.StringSplitOptions.None); // Optimization b/c we know we only care about the first 8.
        if (!int.TryParse(parts[0], out int articleNumber))
            throw new NntpException(string.Format("No article number provided in OVER response"));

        return new OverResponse(code, message,
            articleNumber,
            parts[1],
            parts[2],
            parts[3],
            parts[4],
            parts[5],
            int.TryParse(parts[6], out int bytes) ? bytes : 0,
            int.TryParse(parts[7], out int lines) ? lines : 0);
    }

    public override string ToString() => $"{Code} {Message}";
}
