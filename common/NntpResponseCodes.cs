namespace mcnntp.common;

/// <summary>
/// NNTP Response Codes as defined in RFC 3977 Appendix C
/// </summary>
public static class NntpResponseCodes
{
    #region Informational (1xx)
    /// <summary>Help text follows (multi-line) - RFC 3977 Section 7.2</summary>
    public const int HelpTextFollows = 100;

    /// <summary>Capability list follows (multi-line) - RFC 3977 Section 5.2</summary>
    public const int CapabilityListFollows = 101;

    /// <summary>Server date and time - RFC 3977 Section 7.1</summary>
    public const int ServerDateTime = 111;
    #endregion

    #region Success (2xx)
    /// <summary>Service available, posting allowed - RFC 3977 Section 5.1</summary>
    public const int ServiceAvailablePostingAllowed = 200;

    /// <summary>Service available, posting prohibited - RFC 3977 Section 5.1</summary>
    public const int ServiceAvailablePostingProhibited = 201;

    /// <summary>Closing connection - RFC 3977 Section 5.4</summary>
    public const int ClosingConnection = 205;

    /// <summary>Group selected - RFC 3977 Section 6.1.1</summary>
    public const int GroupSelected = 211;

    /// <summary>Information follows (multi-line) - RFC 3977 Section 7.6.1</summary>
    public const int InformationFollows = 215;

    /// <summary>Article follows (multi-line) - RFC 3977 Section 6.2.1</summary>
    public const int ArticleFollows = 220;

    /// <summary>Headers follow (multi-line) - RFC 3977 Section 6.2.2</summary>
    public const int HeadersFollow = 221;

    /// <summary>Body follows (multi-line) - RFC 3977 Section 6.2.3</summary>
    public const int BodyFollows = 222;

    /// <summary>Article exists and selected - RFC 3977 Section 6.2.4</summary>
    public const int ArticleExists = 223;

    /// <summary>Overview information follows (multi-line) - RFC 3977 Section 8.3</summary>
    public const int OverviewInformationFollows = 224;

    /// <summary>Headers follow (multi-line) - RFC 3977 Section 8.5</summary>
    public const int HeaderFieldsFollow = 225;

    /// <summary>List of new articles follows (multi-line) - RFC 3977 Section 7.4</summary>
    public const int NewArticlesFollow = 230;

    /// <summary>List of new newsgroups follows (multi-line) - RFC 3977 Section 7.3</summary>
    public const int NewNewsgroupsFollow = 231;

    /// <summary>Article transferred OK - RFC 3977 Section 6.3.2</summary>
    public const int ArticleTransferredOK = 235;

    /// <summary>Article received OK - RFC 3977 Section 6.3.1</summary>
    public const int ArticleReceivedOK = 240;

    /// <summary>Authentication accepted - RFC 2980</summary>
    public const int AuthenticationAccepted = 281;

    /// <summary>Authentication accepted (with success data) - RFC 4643 Section 2.4.1</summary>
    /// <remarks>Possible response for AUTHINFO SASL</remarks>
    public const int AuthenticationAcceptedWithSuccessData = 283;
    #endregion

    #region Redirection (3xx)
    /// <summary>Send article to be transferred - RFC 3977 Section 6.3.2</summary>
    public const int SendArticleToTransfer = 335;

    /// <summary>Send article to be posted - RFC 3977 Section 6.3.1</summary>
    public const int SendArticleToPost = 340;

    /// <summary>Password required - RFC 2980</summary>
    public const int PasswordRequired = 381;
    /// <summary>Continue with SASL exchange - RFC 4643 Section 2.4.1</summary>
    /// <remarks>Possible response for AUTHINFO SASL</remarks>
    public const int ContinueWithSaslExchange = 383;
    #endregion

    #region Client Error (4xx)
    /// <summary>Service not available or no longer available - RFC 3977 Section 5.1</summary>
    public const int ServiceNotAvailable = 400;

    /// <summary>Server in wrong mode - RFC 3977 Section 3.2.1</summary>
    public const int ServerInWrongMode = 401;

    /// <summary>Internal fault - RFC 3977 Section 3.2.1</summary>
    public const int InternalFault = 403;

    /// <summary>No such newsgroup - RFC 3977 Section 6.1.1</summary>
    public const int NoSuchNewsgroup = 411;

    /// <summary>No newsgroup selected - RFC 3977 Section 6.1.3</summary>
    public const int NoNewsgroupSelected = 412;

    /// <summary>Current article number is invalid - RFC 3977 Section 6.1.4</summary>
    public const int CurrentArticleNumberInvalid = 420;

    /// <summary>No next article in this group - RFC 3977 Section 6.1.4</summary>
    public const int NoNextArticle = 421;

    /// <summary>No previous article in this group - RFC 3977 Section 6.1.3</summary>
    public const int NoPreviousArticle = 422;

    /// <summary>No article with that number or in that range - RFC 3977 Section 6.2.1</summary>
    public const int NoArticleWithNumber = 423;

    /// <summary>No article with that message-id - RFC 3977 Section 6.2.1</summary>
    public const int NoArticleWithMessageId = 430;

    /// <summary>Article not wanted - RFC 3977 Section 6.3.2</summary>
    public const int ArticleNotWanted = 435;

    /// <summary>Transfer not possible; try again later - RFC 3977 Section 6.3.2</summary>
    public const int TransferNotPossible = 436;

    /// <summary>Transfer rejected; do not retry - RFC 3977 Section 6.3.2</summary>
    public const int TransferRejected = 437;

    /// <summary>Posting not permitted - RFC 3977 Section 6.3.1</summary>
    public const int PostingNotPermitted = 440;

    /// <summary>Posting failed - RFC 3977 Section 6.3.1</summary>
    public const int PostingFailed = 441;

    /// <summary>Authentication required - RFC 3977 Section 3.2.1</summary>
    public const int AuthenticationRequired = 480;

    /// <summary>Authentication failed/rejected - RFC 2980</summary>
    public const int AuthenticationFailed = 481;

    /// <summary>Authentication commands issued out of sequence - RFC 2980</summary>
    public const int AuthenticationOutOfSequence = 482;

    /// <summary>Command unavailable until suitable privacy arranged - RFC 3977 Section 3.2.1</summary>
    public const int PrivacyRequired = 483;
    #endregion

    #region Server Error (5xx)
    /// <summary>Unknown command - RFC 3977 Section 3.2.1</summary>
    public const int UnknownCommand = 500;

    /// <summary>Syntax error in command - RFC 3977 Section 3.2.1</summary>
    public const int SyntaxError = 501;

    /// <summary>Service permanently unavailable - RFC 3977 Section 5.1</summary>
    public const int ServicePermanentlyUnavailable = 502;

    /// <summary>Feature not supported - RFC 3977 Section 3.2.1</summary>
    public const int FeatureNotSupported = 503;

    /// <summary>Error in base64-encoding of argument - RFC 3977 Section 3.2.1</summary>
    public const int Base64EncodingError = 504;
    #endregion
}