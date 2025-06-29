namespace mcnntp.common.client;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using mcnntp.common;

public class NntpClient : IDisposable, IAsyncDisposable
{
    private static readonly char[] separators = [' '];

    public bool CanPost { get; private set; }

    private Connection? Connection { get; set; } = null;

    public int Port { get; set; }

    /// <summary>
    /// Gets the capabilities advertised by the server, if they
    /// have been previously retrieved from a GetCapabilities()
    /// call.  If no capabilities, this will be an empty collection.
    /// If capabilities have not been queried, this will be null.
    /// </summary>
    public ReadOnlyCollection<string>? Capabilities { get; private set; } = null;

    // Whether MODE READER has been issued.
    public bool ModeReaderIssued = false;

    /// <summary>
    /// Gets the newsgroup currently selected by this connection
    /// </summary>
    public string? CurrentNewsgroup { get; private set; } = null;

    /// <summary>
    /// Gets the article number currently selected by this connection for the selected newsgroup
    /// </summary>
    public long? CurrentArticleNumber { get; private set; }

    /// <summary>
    /// Indicates whether this client has been disposed
    /// </summary>
    private bool disposed = false;

    /// <summary>
    /// Semaphore to ensure thread-safe disposal
    /// </summary>
    private readonly SemaphoreSlim disposalSemaphore = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="NntpClient"/> class.
    /// </summary>
    public NntpClient()
    {
        // Establish default values
        CanPost = true;
        Port = 119;
    }

    #region Connections
    public async Task<INntpResponse> ConnectAsync(string hostName, bool? tls = null)
    {
        ThrowIfDisposed();
        var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(hostName, Port);
        Stream stream;

        if (tls ?? Port == 563)
        {
            var sslStream = new SslStream(tcpClient.GetStream());
            await sslStream.AuthenticateAsClientAsync(hostName);
            stream = sslStream;
        }
        else
            stream = tcpClient.GetStream();

        Connection = new Connection(tcpClient, stream);

        var response = await Connection.ReceiveAsync();

        switch (response.Code)
        {
            // Successes and command-specific errors
            case NntpResponseCodes.ServiceAvailablePostingAllowed: // 200 - Service available, posting allowed
                CanPost = true;
                return new ConnectResponse(response, true);
            case NntpResponseCodes.ServiceAvailablePostingProhibited: // 201 - Service available, posting prohibited
                CanPost = false;
                return new ConnectResponse(response, false);
            case NntpResponseCodes.ServiceNotAvailable: // 400 - Service temporarily unavailable
            case NntpResponseCodes.ServicePermanentlyUnavailable: // 502 - Service permanently unavailable
                // Following a 400 or 502 response, the server MUST immediately close the connection.
                return new ConnectResponse(response, false);

            // General server errors
            case NntpResponseCodes.UnknownCommand: // 500 - RFC 3977 Section 3.2.1
            case NntpResponseCodes.SyntaxError: // 501 - RFC 3977 Section 3.2.1  
            case NntpResponseCodes.FeatureNotSupported: // 503 - RFC 3977 Section 3.2.1
                return new ErrorResponse(response.Code, response.Message, response.GetErrorType());

            default:
                // Only throw for truly unexpected protocol violations
                throw new NntpException($"Protocol violation: unexpected response code {response.Code}.  Message: {response.Message}");
        }
    }

    /// <summary>
    /// Disconnects from the server using QUIT command - RFC 3977 Section 5.4
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Server response to QUIT command</returns>
    public async Task<INntpResponse?> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        // RFC Reference: RFC 3977 Section 5.4, Page 34
        // RFC Quote: "The server SHOULD respond with a 205 response and then close the connection."

        ThrowIfDisposed();
        if (Connection == null)
            return null;

        await Connection.SendAsync(cancellationToken, "QUIT\r\n");
        var response = await Connection.ReceiveAsync(cancellationToken: cancellationToken);

        switch (response.Code)
        {
            // Successes and command-specific errors
            case NntpResponseCodes.ClosingConnection: // 205 - Closing connection - RFC 3977 Section 5.4
                Connection.Close();
                return response;

            // General server errors
            // And any other unexpected response.
            case NntpResponseCodes.UnknownCommand: // 500 - RFC 3977 Section 3.2.1
            case NntpResponseCodes.SyntaxError: // 501 - RFC 3977 Section 3.2.1  
            case NntpResponseCodes.FeatureNotSupported: // 503 - RFC 3977 Section 3.2.1
            default:
                // Log warning but still close connection (defensive programming)
                // TODO: Add logging when logger is available
                Connection.Close();
                return response;
        }
    }
    #endregion

    public async Task<bool> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var result = await AuthenticateUsernamePasswordAsync(username, password, cancellationToken);
        return result.IsSuccess();
    }

    public async Task<AuthenticateResponse> AuthenticateUsernamePasswordAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Connection == null)
            throw new InvalidOperationException("Cannot send AUTHINFO command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, $"AUTHINFO USER {username}\r\n");
        var responseUser = await Connection.ReceiveAsync(cancellationToken: cancellationToken);

        switch (responseUser.Code)
        {
            case NntpResponseCodes.AuthenticationAccepted: // Authentication accepted (password not required)
                return new AuthenticateResponse(responseUser);

            case NntpResponseCodes.PasswordRequired: // Password required
                await Connection.SendAsync(cancellationToken, $"AUTHINFO PASS {password}\r\n");
                var responsePass = await Connection.ReceiveAsync(cancellationToken: cancellationToken);
                switch (responsePass.Code)
                {
                    case NntpResponseCodes.AuthenticationAccepted: // Authentication accepted
                    case NntpResponseCodes.AuthenticationFailed: // Authentication failed/rejected
                        return new AuthenticateResponse(responsePass);
                    default:
                        throw new NntpException($"Unexpected response code {responsePass.Code}.  Message: {responsePass.Message}");
                }
            case NntpResponseCodes.AuthenticationFailed: // Authentication failed/rejected
            case NntpResponseCodes.AuthenticationOutOfSequence: // Authentication commands issued out of sequence
            case NntpResponseCodes.ServicePermanentlyUnavailable: // Command unavailable
                return new AuthenticateResponse(responseUser);
            default:
                throw new NntpException($"Unexpected response code {responseUser.Code}.  Message: {responseUser.Message}");
        }
    }

    public async Task<INntpResponse> AuthenticateSaslPlainAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        const string authzid = "nntp";
        if (username != null && username.IndexOf('\0') > -1)
            throw new ArgumentException("A NUL character is not permitted", nameof(username));
        if (password != null && password.IndexOf('\0') > -1)
            throw new ArgumentException("A NUL character is not permitted", nameof(password));

        if (Connection == null)
            throw new InvalidOperationException("Cannot send AUTHINFO command when there is no connection to the server");

        var message = $"{authzid}\0{username}\0{password}";
        var enc = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(message));
        await Connection.SendAsync(cancellationToken, $"AUTHINFO SASL PLAIN {enc}\r\n");
        var response = await Connection.ReceiveAsync(cancellationToken: cancellationToken);

        switch (response.Code)
        {
            // Successes and command-specific errors
            case NntpResponseCodes.AuthenticationAccepted: // 281 - Authentication accepted
            case NntpResponseCodes.AuthenticationAcceptedWithSuccessData: // 283 - Authentication accepted (with success data)
            case NntpResponseCodes.ContinueWithSaslExchange: // 383 - Continue with SASL exchange
            case NntpResponseCodes.AuthenticationFailed: // 481 - Authentication failed/rejected
            case NntpResponseCodes.AuthenticationOutOfSequence: // 482 - SASL protocol error
            case NntpResponseCodes.PrivacyRequired: // 483 - The client must negotiate appropriate privacy protection on the connection.
            case NntpResponseCodes.ServicePermanentlyUnavailable: // 502 - Command unavailable
                return new AuthenticateResponse(response);

            // General server errors
            case NntpResponseCodes.UnknownCommand: // 500 - RFC 3977 Section 3.2.1
            case NntpResponseCodes.SyntaxError: // 501 - RFC 3977 Section 3.2.1  
            case NntpResponseCodes.FeatureNotSupported: // 503 - RFC 3977 Section 3.2.1
                return new ErrorResponse(response.Code, response.Message, response.GetErrorType());

            default:
                // Only throw for truly unexpected protocol violations
                throw new NntpException($"Protocol violation: unexpected response code {response.Code}.  Message: {response.Message}");
        }
    }

    public async Task<INntpResponse> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Connection == null)
            throw new InvalidOperationException("Cannot send CAPABILITIES command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, "CAPABILITIES\r\n");
        var response = await Connection.ReceiveMultilineAsync();

        switch (response.Code)
        {
            // Successes and command-specific errors
            case NntpResponseCodes.CapabilityListFollows: // 101 - Capability list follows (multi-line)
                Capabilities = response.Lines;
                return new CapabilitiesResponse(response.Code, response.Message, response.Lines);

            // General server errors
            case NntpResponseCodes.UnknownCommand: // 500 - RFC 3977 Section 3.2.1
            case NntpResponseCodes.SyntaxError: // 501 - RFC 3977 Section 3.2.1  
            case NntpResponseCodes.FeatureNotSupported: // 503 - RFC 3977 Section 3.2.1
                return new ErrorResponse(response.Code, response.Message, response.GetErrorType());

            default:
                // Only throw for truly unexpected protocol violations
                throw new NntpException($"Protocol violation: unexpected response code {response.Code}.  Message: {response.Message}");
        }
    }

    public async Task<INntpResponse> GetNewsgroupsListAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Connection == null)
            throw new InvalidOperationException("Cannot send LIST command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, "LIST\r\n");
        var response = await Connection.ReceiveMultilineAsync();

        switch (response.Code)
        {
            // Successes and command-specific errors
            case NntpResponseCodes.InformationFollows: // 215 - RFC 3977 Section 7.6.1
                HashSet<ListActiveResponse.NewsgroupEntry> entries = [];
                foreach (var line in response.Lines)
                {
                    var parts = line.Split(' ', 4, StringSplitOptions.None);
                    if (parts.Length != 4)
                        continue; // TODO: Log unexpected corrupted data
                    if (!int.TryParse(parts[1], out int highWatermark))
                        continue; // TODO: Log unexpected corrupted data
                    if (!int.TryParse(parts[2], out int lowWatermark))
                        continue; // TODO: Log unexpected corrupted data
                    entries.Add(new ListActiveResponse.NewsgroupEntry
                    {
                        Group = parts[0],
                        HighWatermark = highWatermark,
                        LowWatermark = lowWatermark,
                        Status = parts[2]
                    });
                }
                return new ListActiveResponse(response.Code, response.Message, new ReadOnlySet<ListActiveResponse.NewsgroupEntry>(entries));

            // General server errors
            case NntpResponseCodes.UnknownCommand: // 500 - RFC 3977 Section 3.2.1
            case NntpResponseCodes.SyntaxError: // 501 - RFC 3977 Section 3.2.1  
            case NntpResponseCodes.FeatureNotSupported: // 503 - RFC 3977 Section 3.2.1
                return new ErrorResponse(response.Code, response.Message, response.GetErrorType());

            default:
                // Only throw for truly unexpected protocol violations
                throw new NntpException($"Protocol violation: unexpected response code {response.Code}.  Message: {response.Message}");
        }
    }

    public async Task<GroupResponse> GetGroupAsync(string newsgroup, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(newsgroup))
            throw new ArgumentNullException(nameof(newsgroup));

        if (Connection == null)
            throw new InvalidOperationException("Cannot send GROUP command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, "GROUP {0}\r\n", newsgroup);
        var response = await Connection.ReceiveAsync(cancellationToken: cancellationToken);

        switch (response.Code)
        {
            case NntpResponseCodes.GroupSelected: // Group selected - RFC 3977 Section 6.1.1
                CurrentNewsgroup = newsgroup;
                var values = response.Message?.Split(separators);

                if (values == null || !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    throw new InvalidOperationException($"Cannot parse {values?[0]} to an integer for 'number'");

                if (!int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int low))
                    throw new InvalidOperationException($"Cannot parse {values[1]} to an integer for 'low'");

                if (!int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int high))
                    throw new InvalidOperationException($"Cannot parse {values[2]} to an integer for 'high'");

                return new GroupResponse(response.Code, response.Message, values[3], number, low, high);

            case NntpResponseCodes.NoSuchNewsgroup: // No such newsgroup - RFC 3977 Section 6.1.1
                return new GroupResponse(response.Code, response.Message, newsgroup, 0, 0, 0);

            default:
                throw new NntpException($"Unexpected response code {response.Code}. Message: {response.Message}");
        }
    }

    public async Task<GroupListResponse> GetGroupListAsync(string newsgroup, int? articleNumberRangeStart, int? articleNumberRangeEnd, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newsgroup))
            throw new ArgumentNullException(nameof(newsgroup));

        if (Connection == null)
            throw new InvalidOperationException("Cannot send LISTGROUP command when there is no connection to the server");

        var rangeArgument = "1-";
        if (articleNumberRangeStart != null)
        {
            if (articleNumberRangeEnd == null)
                rangeArgument = $"{articleNumberRangeStart}-";
            else if (articleNumberRangeEnd == articleNumberRangeStart)
                rangeArgument = $"{articleNumberRangeStart}";
            else
                rangeArgument = $"{articleNumberRangeStart}-{articleNumberRangeEnd}";
        }

        await Connection.SendAsync(cancellationToken, $"LISTGROUP {newsgroup} {rangeArgument}\r\n");
        var response = await Connection.ReceiveMultilineAsync();
        if (response.Code != NntpResponseCodes.GroupSelected)
            throw new NntpException(string.Format("Unexpected response code {0}.  Message: {1}", response.Code, response.Message));

        CurrentNewsgroup = newsgroup;

        var values = response.Message?.Split(separators);

        if (values == null || !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            throw new InvalidOperationException($"Cannot parse {values?[0]} to an integer for 'number'");

        if (!int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int low))
            throw new InvalidOperationException($"Cannot parse {values[1]} to an integer for 'low'");

        if (!int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int high))
            throw new InvalidOperationException($"Cannot parse {values[2]} to an integer for 'high'");

        return new GroupListResponse(
            response.Code,
            response.Message,
            values[3],
            number,
            low,
            high,
            new ReadOnlyCollection<int>(response.Lines.Select(l => int.Parse(l)).ToList()));
    }

    public async Task<INntpResponse> ModeReader(CancellationToken cancellationToken = default)
    {
        if (ModeReaderIssued)
            throw new InvalidOperationException("MODE READER can only be issued once per session");

        if (Capabilities != null && !Capabilities.Any(c => string.Equals(c, "MODE-READER", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Server does not support MODE-READER capability");

        if (Connection == null)
            throw new InvalidOperationException("Cannot send MODE READER command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, $"MODE READER\r\n");
        var response = await Connection.ReceiveAsync(cancellationToken: cancellationToken);
        ModeReaderIssued = true;
        return response;
    }

    public async Task<PointerResponse> LastAsync(CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send LAST command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, $"LAST\r\n");
        var response = await Connection.ReceiveAsync(cancellationToken: cancellationToken);
        switch (response.Code)
        {
            case NntpResponseCodes.ArticleExists: // Article found
                var values = response.Message?.Split(separators);

                if (values == null || !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    throw new InvalidOperationException($"Cannot parse {values?[0]} to an integer for 'number'");

                return new PointerResponse(response.Code, response.Message, number, values[1]);
            case NntpResponseCodes.NoNewsgroupSelected: // No newsgroup selected
                CurrentNewsgroup = null;
                CurrentArticleNumber = null;
                return new PointerResponse(response.Code, response.Message, null, null);
            case NntpResponseCodes.CurrentArticleNumberInvalid: // Current article number is invalid
            case NntpResponseCodes.NoPreviousArticle: // No previous article in this group
            default:
                CurrentArticleNumber = null;
                return new PointerResponse(response.Code, response.Message, null, null);
        }
    }

    public async Task<PointerResponse> NextAsync(CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send NEXT command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, $"NEXT\r\n");
        var response = await Connection.ReceiveAsync(cancellationToken: cancellationToken);
        switch (response.Code)
        {
            case NntpResponseCodes.ArticleExists: // Article found
                var values = response.Message?.Split(separators);

                if (values == null || !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    throw new InvalidOperationException($"Cannot parse {values?[0]} to an integer for 'number'");

                return new PointerResponse(response.Code, response.Message, number, values[1]);
            case NntpResponseCodes.NoNewsgroupSelected: // No newsgroup selected
                CurrentNewsgroup = null;
                CurrentArticleNumber = null;
                return new PointerResponse(response.Code, response.Message, null, null);
            case NntpResponseCodes.CurrentArticleNumberInvalid: // Current article number is invalid
            case NntpResponseCodes.NoNextArticle: // No next article in this group
            default:
                CurrentArticleNumber = null;
                return new PointerResponse(response.Code, response.Message, null, null);
        }
    }

    public async Task<ArticleResponse> ArticleAsync(int articleNumber, CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send ARTICLE command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, $"ARTICLE {articleNumber}\r\n");
        var response = await Connection.ReceiveMultilineAsync();

        switch (response.Code)
        {
            case NntpResponseCodes.ArticleFollows: // Article follows (multi-line)
                CurrentArticleNumber = articleNumber;
                return new ArticleResponse(response.Code, response.Message, response.Lines);
            case NntpResponseCodes.NoArticleWithNumber: // No article with that number
                CurrentArticleNumber = null;
                return new ArticleResponse(response.Code, response.Message, null);
            case NntpResponseCodes.NoNewsgroupSelected: // No newsgroup selected
            default:
                CurrentNewsgroup = null;
                CurrentArticleNumber = null;
                return new ArticleResponse(response.Code, response.Message, null);
        }
    }

    public async Task<ArticleResponse> ArticleAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send ARTICLE command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, $"ARTICLE {messageId}\r\n");
        var response = await Connection.ReceiveMultilineAsync();

        switch (response.Code)
        {
            case NntpResponseCodes.ArticleFollows: // Article follows (multi-line)
                var values = response.Message?.Split(separators);
                if (values == null || !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    throw new InvalidOperationException($"Cannot parse {values?[0]} to an integer for 'number'");

                if (number > 0)
                    CurrentArticleNumber = number;
                return new ArticleResponse(response.Code, response.Message, response.Lines);
            case NntpResponseCodes.NoArticleWithMessageId: // No article with that message-id
            default:
                CurrentArticleNumber = null;
                return new ArticleResponse(response.Code, response.Message, null);
        }
    }

    public async Task<ArticleResponse> ArticleAsync(CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send ARTICLE command when there is no connection to the server");

        // This form uses the server's notion of the "current" article number
        await Connection.SendAsync(cancellationToken, $"ARTICLE\r\n");
        var response = await Connection.ReceiveMultilineAsync();

        switch (response.Code)
        {
            case NntpResponseCodes.ArticleFollows: // Article follows (multi-line)
                var values = response.Message?.Split(separators);
                if (values == null || !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    throw new InvalidOperationException($"Cannot parse {values?[0]} to an integer for 'number'");

                CurrentArticleNumber = number;
                return new ArticleResponse(response.Code, response.Message, response.Lines);

            case NntpResponseCodes.NoArticleWithNumber: // Current article number is invalid
                CurrentArticleNumber = null;
                return new ArticleResponse(response.Code, response.Message, null);
            case NntpResponseCodes.NoNewsgroupSelected: // No newsgroup selected
            default:
                CurrentNewsgroup = null;
                CurrentArticleNumber = null;
                return new ArticleResponse(response.Code, response.Message, null);
        }
    }

    public async Task<HeadResponse> HeadAsync(int articleNumber, CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send HEAD command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, $"HEAD {articleNumber}\r\n");
        var response = await Connection.ReceiveMultilineAsync();

        switch (response.Code)
        {
            case NntpResponseCodes.HeadersFollow: // Headers follow (multi-line)
                CurrentArticleNumber = articleNumber;
                return new HeadResponse(response.Code, response.Message, response.Lines);
            case NntpResponseCodes.NoArticleWithNumber: // No article with that number
                CurrentArticleNumber = null;
                return new HeadResponse(response.Code, response.Message, null);
            case NntpResponseCodes.NoNewsgroupSelected: // No newsgroup selected
            default:
                CurrentNewsgroup = null;
                CurrentArticleNumber = null;
                return new HeadResponse(response.Code, response.Message, null);
        }
    }

    public async Task<HeadResponse> HeadAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send HEAD command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, $"HEAD {messageId}\r\n");
        var response = await Connection.ReceiveMultilineAsync();

        switch (response.Code)
        {
            case NntpResponseCodes.HeadersFollow: // Headers follow (multi-line)
                var values = response.Message?.Split(separators);
                if (values == null || !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    throw new InvalidOperationException($"Cannot parse {values?[0]} to an integer for 'number'");

                if (number > 0)
                    CurrentArticleNumber = number;
                return new HeadResponse(response.Code, response.Message, response.Lines);
            case NntpResponseCodes.NoArticleWithMessageId: // No article with that message-id
            default:
                CurrentArticleNumber = null;
                return new HeadResponse(response.Code, response.Message, null);
        }
    }

    public async Task<HeadResponse> HeadAsync(CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send HEAD command when there is no connection to the server");

        // This form uses the server's notion of the "current" article number
        await Connection.SendAsync(cancellationToken, $"HEAD\r\n");
        var response = await Connection.ReceiveMultilineAsync();

        switch (response.Code)
        {
            case NntpResponseCodes.HeadersFollow: // Headers follow (multi-line)
                var values = response.Message?.Split(separators);
                if (values == null || !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    throw new InvalidOperationException($"Cannot parse {values?[0]} to an integer for 'number'");

                CurrentArticleNumber = number;
                return new HeadResponse(response.Code, response.Message, response.Lines);

            case NntpResponseCodes.NoArticleWithNumber: // Current article number is invalid
                CurrentArticleNumber = null;
                return new HeadResponse(response.Code, response.Message, null);
            case NntpResponseCodes.NoNewsgroupSelected: // No newsgroup selected
            default:
                CurrentNewsgroup = null;
                CurrentArticleNumber = null;
                return new HeadResponse(response.Code, response.Message, null);
        }
    }

    public async Task<BodyResponse> BodyAsync(int articleNumber, CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send BODY command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, $"BODY {articleNumber}\r\n");
        var response = await Connection.ReceiveMultilineAsync();

        switch (response.Code)
        {
            case NntpResponseCodes.BodyFollows: // Body follows (multi-line)
                CurrentArticleNumber = articleNumber;
                return new BodyResponse(response.Code, response.Message, response.Lines);
            case NntpResponseCodes.NoArticleWithNumber: // No article with that number
                CurrentArticleNumber = null;
                return new BodyResponse(response.Code, response.Message, null);
            case NntpResponseCodes.NoNewsgroupSelected: // No newsgroup selected
            default:
                CurrentNewsgroup = null;
                CurrentArticleNumber = null;
                return new BodyResponse(response.Code, response.Message, null);
        }
    }

    public async Task<BodyResponse> BodyAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send BODY command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, $"BODY {messageId}\r\n");
        var response = await Connection.ReceiveMultilineAsync();

        switch (response.Code)
        {
            case NntpResponseCodes.BodyFollows: // Body follows (multi-line)
                var values = response.Message?.Split(separators);
                if (values == null || !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    throw new InvalidOperationException($"Cannot parse {values?[0]} to an integer for 'number'");

                if (number > 0)
                    CurrentArticleNumber = number;
                return new BodyResponse(response.Code, response.Message, response.Lines);
            case NntpResponseCodes.NoArticleWithMessageId: // No article with that message-id
            default:
                CurrentArticleNumber = null;
                return new BodyResponse(response.Code, response.Message, null);
        }
    }

    public async Task<BodyResponse> BodyAsync(CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send BODY command when there is no connection to the server");

        // This form uses the server's notion of the "current" article number
        await Connection.SendAsync(cancellationToken, $"BODY\r\n");
        var response = await Connection.ReceiveMultilineAsync();

        switch (response.Code)
        {
            case NntpResponseCodes.BodyFollows: // Body follows (multi-line)
                var values = response.Message?.Split(separators);
                if (values == null || !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    throw new InvalidOperationException($"Cannot parse {values?[0]} to an integer for 'number'");

                CurrentArticleNumber = number;
                return new BodyResponse(response.Code, response.Message, response.Lines);

            case NntpResponseCodes.NoArticleWithNumber: // Current article number is invalid
                CurrentArticleNumber = null;
                return new BodyResponse(response.Code, response.Message, null);
            case NntpResponseCodes.NoNewsgroupSelected: // No newsgroup selected
            default:
                CurrentNewsgroup = null;
                CurrentArticleNumber = null;
                return new BodyResponse(response.Code, response.Message, null);
        }
    }

    public async Task<PointerResponse> StatAsync(int articleNumber, CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send STAT command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, $"STAT {articleNumber}\r\n");
        var response = await Connection.ReceiveAsync(cancellationToken: cancellationToken);

        switch (response.Code)
        {
            case NntpResponseCodes.ArticleExists: // Article exists
                var values = response.Message?.Split(separators);
                CurrentArticleNumber = articleNumber;
                return new PointerResponse(response.Code, response.Message, articleNumber, values?[1]);
            case NntpResponseCodes.NoArticleWithNumber: // No article with that number
                CurrentArticleNumber = null;
                return new PointerResponse(response.Code, response.Message, null, null);
            case NntpResponseCodes.NoNewsgroupSelected: // No newsgroup selected
            default:
                CurrentNewsgroup = null;
                CurrentArticleNumber = null;
                return new PointerResponse(response.Code, response.Message, null, null);
        }
    }

    public async Task<PointerResponse> StatAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send STAT command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, $"STAT {messageId}\r\n");
        var response = await Connection.ReceiveAsync(cancellationToken: cancellationToken);

        switch (response.Code)
        {
            case NntpResponseCodes.ArticleExists: // Article exists
                var values = response.Message?.Split(separators);
                if (values == null || !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    throw new InvalidOperationException($"Cannot parse {values?[0]} to an integer for 'number'");

                if (number > 0)
                    CurrentArticleNumber = number;
                return new PointerResponse(response.Code, response.Message, number, values[1]);
            case NntpResponseCodes.NoArticleWithMessageId: // No article with that message-id
            default:
                CurrentArticleNumber = null;
                return new PointerResponse(response.Code, response.Message, null, null);
        }
    }

    public async Task<PointerResponse> StatAsync(CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send STAT command when there is no connection to the server");

        // This form uses the server's notion of the "current" article number
        await Connection.SendAsync(cancellationToken, $"STAT\r\n");
        var response = await Connection.ReceiveAsync(cancellationToken: cancellationToken);

        switch (response.Code)
        {
            case NntpResponseCodes.ArticleExists: // Article exists
                var values = response.Message?.Split(separators);
                if (values == null || !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    throw new InvalidOperationException($"Cannot parse {values?[0]} to an integer for 'number'");

                CurrentArticleNumber = number;
                return new PointerResponse(response.Code, response.Message, number, values[1]);

            case NntpResponseCodes.NoArticleWithNumber: // Current article number is invalid
                CurrentArticleNumber = null;
                return new PointerResponse(response.Code, response.Message, null, null);
            case NntpResponseCodes.NoNewsgroupSelected: // No newsgroup selected
            default:
                CurrentNewsgroup = null;
                CurrentArticleNumber = null;
                return new PointerResponse(response.Code, response.Message, null, null);
        }
    }

    public async Task<ReadOnlyCollection<OverResponse>> OverAsync(int low, int high, CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send OVER command when there is no connection to the server");

        if (Capabilities != null && Capabilities.Any(c => string.Equals(c, "XOVER", StringComparison.OrdinalIgnoreCase)))
            return await OverAsyncInternal(true, low, high, cancellationToken);

        return await OverAsyncInternal(false, low, high, cancellationToken);
    }

    public async Task<ReadOnlyCollection<OverResponse>> XOverAsync(int low, int high, CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send XOVER command when there is no connection to the server");

        return await OverAsyncInternal(true, low, high, cancellationToken);
    }

    private async Task<ReadOnlyCollection<OverResponse>> OverAsyncInternal(bool isXOver, int low, int high, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Connection == null)
            throw new InvalidOperationException("Cannot send OVER command when there is no connection to the server");

        string command = isXOver ? "XOVER" : "OVER";

        await Connection.SendAsync(cancellationToken, $"{command} {low}-{high}\r\n");
        var response = await Connection.ReceiveMultilineAsync();
        if (response.Code != NntpResponseCodes.OverviewInformationFollows)
            throw new NntpException(string.Format("Unexpected response code {0}.  Message: {1}", response.Code, response.Message));

        var ret = new List<OverResponse>();

        foreach (var line in response.Lines)
        {
            var over = OverResponse.Parse(response.Code, response.Message, line);
            if (over != null)
                ret.Add(over.Value);
        }

        return new ReadOnlyCollection<OverResponse>(ret);
    }

    public async Task PostAsync(string newsgroup, string subject, string from, string content, CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send POST command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, "POST\r\n");
        var response = await Connection.ReceiveAsync(cancellationToken: cancellationToken);
        if (response.Code != NntpResponseCodes.SendArticleToPost)
            throw new NntpException(string.Format("Unexpected response code {0}.  Message: {1}", response.Code, response.Message));

        await Connection.SendAsync(cancellationToken, "From: {0}\r\nNewsgroups: {1}\r\nSubject: {2}\r\n\r\n{3}\r\n.\r\n", from, newsgroup, subject, content);
        response = await Connection.ReceiveAsync(cancellationToken: cancellationToken);
        if (response.Code != NntpResponseCodes.ArticleReceivedOK)
            throw new NntpException(string.Format("Unexpected response code {0}.  Message: {1}", response.Code, response.Message));
    }

    /// <summary>
    /// Gets the current server date and time - RFC 3977 Section 7.1
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>DateResponse containing server UTC date/time</returns>
    public async Task<DateResponse> DateAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Connection == null)
            throw new InvalidOperationException("Cannot send DATE command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, "DATE\r\n");
        var response = await Connection.ReceiveAsync(cancellationToken: cancellationToken);

        switch (response.Code)
        {
            case NntpResponseCodes.ServerDateTime: // Server date and time - RFC 3977 Section 7.1
                return new DateResponse(response.Code, response.Message);
            default:
                throw new NntpException($"Unexpected response code {response.Code}. Message: {response.Message}");
        }
    }

    public async Task<GroupsListResponse> NewGroupsAsync(DateTime since, CancellationToken cancellationToken = default)
    {
        var date = since.ToUniversalTime().ToString("yyyyMMdd");
        var time = since.ToUniversalTime().ToString("HHmmss");

        if (Connection == null)
            throw new InvalidOperationException("Cannot send NEWGROUPS command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, $"NEWGROUPS {date} {time} GMT\r\n");
        var response = await Connection.ReceiveMultilineAsync();

        switch (response.Code)
        {
            case NntpResponseCodes.NewNewsgroupsFollow: // List of new newsgroups follows (multi-line)
                return ParseGroupsListResponse(response);
            default:
                return new GroupsListResponse(response.Code, response.Message, null);
        }
    }

    public async Task<NewNewsResponse> NewNewsAsync(string wildmat, DateTime since, CancellationToken cancellationToken = default)
    {
        var date = since.ToUniversalTime().ToString("yyyyMMdd");
        var time = since.ToUniversalTime().ToString("HHmmss");

        if (Connection == null)
            throw new InvalidOperationException("Cannot send NEWNEWS command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, $"NEWNEWS {wildmat} {date} {time} GMT\r\n");
        var response = await Connection.ReceiveMultilineAsync();

        return new NewNewsResponse(response.Code, response.Message, response.Lines);
    }

    public async Task<GroupsListResponse> ListAsync(string keyword = "ACTIVE", string? wildmatOrArgument = null, CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send LIST command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, $"LIST {keyword}{(string.IsNullOrWhiteSpace(wildmatOrArgument) ? string.Empty : $" {wildmatOrArgument}")}\r\n");
        var response = await Connection.ReceiveMultilineAsync();

        switch (response.Code)
        {
            case NntpResponseCodes.InformationFollows: // Information follows (multi-line)
                return ParseGroupsListResponse(response);
            default:
                return new GroupsListResponse(response.Code, response.Message, null);
        }
    }

    public async Task<GroupsListResponse> ListActiveAsync(CancellationToken cancellationToken = default) => await ListAsync("ACTIVE", null, cancellationToken);

    private static GroupsListResponse ParseGroupsListResponse(NntpMultilineResponse response)
    {
        var groups = new List<GroupsListResponse.GroupEntry>();
        foreach (var line in response.Lines)
        {
            var values = line.Split(separators);

            if (!int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int high))
                throw new InvalidOperationException($"Cannot parse {values[1]} to an integer for 'high'");

            if (!int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int low))
                throw new InvalidOperationException($"Cannot parse {values[2]} to an integer for 'low'");

            groups.Add(new GroupsListResponse.GroupEntry
            {
                Group = values[0],
                HighWatermark = high,
                LowWatermark = low,
                Status = values[3][0]
            });
        }

        return new GroupsListResponse(response.Code, response.Message, groups.AsReadOnly());
    }

    public async Task<GroupsListTimesResponse> ListActiveTimesAsync(CancellationToken cancellationToken = default) => await ListActiveTimesAsync(null, cancellationToken);

    public async Task<GroupsListTimesResponse> ListActiveTimesAsync(string? wildmatOrArgument, CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send LIST ACTIVE.TIMES command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, $"LIST ACTIVE.TIMES{(string.IsNullOrWhiteSpace(wildmatOrArgument) ? string.Empty : $" {wildmatOrArgument}")}\r\n");
        var response = await Connection.ReceiveMultilineAsync();

        switch (response.Code)
        {
            case NntpResponseCodes.InformationFollows: // Information follows (multi-line)

                var values = response.Message?.Split(separators);

                var groups = new List<GroupsListTimesResponse.GroupTimeEntry>();
                foreach (var line in response.Lines)
                {
                    if (values == null || !int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds))
                        throw new InvalidOperationException($"Cannot parse {values?[1]} to an integer for 'seconds'");

                    groups.Add(new GroupsListTimesResponse.GroupTimeEntry
                    {
                        Group = values[0],
                        EpochSeconds = seconds,
                        Creator = values[2]
                    });
                }

                return new GroupsListTimesResponse(response.Code, response.Message, groups.AsReadOnly());
            default:
                return new GroupsListTimesResponse(response.Code, response.Message, null);
        }
    }

    public async Task<NewsgroupsListResponse> ListNewsgroupsAsync(CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            throw new InvalidOperationException("Cannot send LIST NEWSGROUPS command when there is no connection to the server");

        await Connection.SendAsync(cancellationToken, $"LIST NEWSGROUPS\r\n");
        var response = await Connection.ReceiveMultilineAsync();

        switch (response.Code)
        {
            case NntpResponseCodes.InformationFollows: // Information follows (multi-line)
                var groups = new List<NewsgroupsListResponse.GroupEntry>();
                foreach (var line in response.Lines)
                {
                    var splits = line.Split(' ', '\t');
                    var newsgroup = splits[0];

                    var idx = splits[0].Length;
                    for (var i = idx; i < line.Length; i++)
                    {
                        if (!char.IsWhiteSpace(line[i]))
                        {
                            idx = i;
                            break;
                        }
                    }

                    var creator = line[idx..];

                    groups.Add(new NewsgroupsListResponse.GroupEntry
                    {
                        Group = newsgroup,
                        Description = creator
                    });
                }

                return new NewsgroupsListResponse(response.Code, response.Message, groups.AsReadOnly());
            default:
                return new NewsgroupsListResponse(response.Code, response.Message, null);
        }
    }

    #region IDisposable Implementation

    /// <summary>
    /// Throws ObjectDisposedException if this client has been disposed
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(NntpClient));
    }

    /// <summary>
    /// Disposes the NNTP client and releases all resources
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Asynchronously disposes the NNTP client and releases all resources
    /// </summary>
    /// <returns>A ValueTask representing the asynchronous disposal operation</returns>
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the client and releases resources
    /// </summary>
    /// <param name="disposing">True if disposing managed resources</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposed && disposing)
        {
            disposalSemaphore.Wait();
            try
            {
                Connection?.Close();
                Connection?.Dispose();
            }
            finally
            {
                disposed = true;
                disposalSemaphore?.Dispose();
            }
        }
    }

    /// <summary>
    /// Asynchronously disposes the client with graceful disconnect
    /// </summary>
    /// <returns>A ValueTask representing the asynchronous disposal operation</returns>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (!disposed)
        {
            await disposalSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (Connection != null)
                {
                    // Graceful disconnect with timeout
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        await DisconnectAsync(cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Timeout - force close
                        Connection.Close();
                    }
                    catch (Exception)
                    {
                        // Any other exception during graceful disconnect - force close
                        Connection.Close();
                    }
                    Connection.Dispose();
                }
            }
            finally
            {
                disposed = true;
            }
        }
    }

    #endregion
}
