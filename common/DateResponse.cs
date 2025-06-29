using System;
using System.Globalization;

namespace mcnntp.common;

public readonly record struct DateResponse : INntpResponse
{
    public readonly int Code { get; private init; }
    public readonly string? Message { get; private init; }
    public readonly DateTime? ServerDateTime { get; private init; }

    /// <summary>
    /// Gets whether the date was successfully retrieved
    /// </summary>
    public bool IsSuccessful => Code == NntpResponseCodes.ServerDateTime && ServerDateTime.HasValue;

    public DateResponse(int code, string? message)
    {
        Code = code;
        // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
        // include the octets NUL, LF, or CR.
        Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        ServerDateTime = ParseServerDateTime(message);
    }

    private static DateTime? ParseServerDateTime(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length < 14)
            return null;

        // RFC 3977 Section 7.1: Format is YYYYMMDDhhmmss
        if (DateTime.TryParseExact(message[..14], "yyyyMMddHHmmss",
                                   CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
                                   out DateTime result))
        {
            return result.ToUniversalTime();
        }

        return null;
    }

    public override string ToString() => $"{Code} {Message}";
}