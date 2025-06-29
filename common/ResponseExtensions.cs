namespace mcnntp.common;

/// <summary>
/// Extension methods for NNTP response classification - RFC 3977 Section 3.2
/// </summary>
public static class ResponseExtensions
{
    /// <summary>
    /// Checks if response is informational (1xx) - RFC 3977 Section 3.2
    /// </summary>
    public static bool IsInformational(this INntpResponse response) => response.Code >= 100 && response.Code < 200;

    /// <summary>
    /// Checks if response indicates success (2xx) - RFC 3977 Section 3.2
    /// </summary>
    public static bool IsSuccess(this INntpResponse response) => response.Code >= 200 && response.Code < 300;

    /// <summary>
    /// Checks if response requires continuation (3xx) - RFC 3977 Section 3.2
    /// </summary>
    public static bool RequiresContinuation(this INntpResponse response) => response.Code >= 300 && response.Code < 400;

    /// <summary>
    /// Checks if response indicates client error (4xx) - RFC 3977 Section 3.2
    /// </summary>
    public static bool IsClientError(this INntpResponse response) => response.Code >= 400 && response.Code < 500;

    /// <summary>
    /// Checks if response indicates server error (5xx) - RFC 3977 Section 3.2
    /// </summary>
    public static bool IsServerError(this INntpResponse response) => response.Code >= 500 && response.Code < 600;

    /// <summary>
    /// Checks if response indicates any error (4xx or 5xx) - RFC 3977 Section 3.2
    /// </summary>
    public static bool IsError(this INntpResponse response) => response.Code >= 400;

    /// <summary>
    /// Gets error type for error responses
    /// </summary>
    public static ErrorType GetErrorType(this INntpResponse response)
    {
        return response.Code switch
        {
            NntpResponseCodes.UnknownCommand => ErrorType.UnknownCommand,
            NntpResponseCodes.SyntaxError => ErrorType.SyntaxError,
            NntpResponseCodes.FeatureNotSupported => ErrorType.FeatureNotSupported,
            NntpResponseCodes.AuthenticationRequired => ErrorType.AuthenticationRequired,
            NntpResponseCodes.NoSuchNewsgroup => ErrorType.NoSuchNewsgroup,
            NntpResponseCodes.NoNewsgroupSelected => ErrorType.NoNewsgroupSelected,
            _ => ErrorType.Other
        };
    }
}
