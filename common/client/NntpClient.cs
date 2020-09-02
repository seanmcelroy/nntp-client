namespace mcnntp.common.client
{
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

    public class NntpClient
    {
        private static readonly char[] separators = { ' ' };

        public bool CanPost { get; private set; }

        private Connection Connection { get; set; }

        public int Port { get; set; }

        /// <summary>
        /// Gets the capabilities advertised by the server, if they
        /// have been previously retrieved from a GetCapabilities()
        /// call.  If no capabilities, this will be an empty collection.
        /// If capabilities have not been queried, this will be null.
        /// </summary>
        public ReadOnlyCollection<string> Capabilities { get; private set; } = null;

        // Whether MODE READER has been issued.
        public bool ModeReaderIssued = false;

        /// <summary>
        /// Gets the newsgroup currently selected by this connection
        /// </summary>
        public string CurrentNewsgroup { get; private set; }

        /// <summary>
        /// Gets the article number currently selected by this connection for the selected newsgroup
        /// </summary>
        public long? CurrentArticleNumber { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpClient"/> class.
        /// </summary>
        public NntpClient()
        {
            // Establish default values
            this.CanPost = true;
            this.Port = 119;
        }

        #region Connections
        public async Task<ConnectResponse> ConnectAsync(string hostName, bool? tls = null)
        {
            var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(hostName, this.Port);
            Stream stream;

            if (tls ?? this.Port == 563)
            {
                var sslStream = new SslStream(tcpClient.GetStream());
                await sslStream.AuthenticateAsClientAsync(hostName);
                stream = sslStream;
            }
            else
                stream = tcpClient.GetStream();

            this.Connection = new Connection(tcpClient, stream);

            var response = await this.Connection.ReceiveAsync();

            switch (response.Code)
            {
                case 200: // Service available, posting allowed
                    this.CanPost = true;
                    return new ConnectResponse(response, true);
                case 201: // Service available, posting prohibited
                    this.CanPost = false;
                    return new ConnectResponse(response, false);
                case 400: // Service temporarily unavailable
                case 502: // Service permanently unavailable
                    // Following a 400 or 502 response, the server MUST immediately close the connection.
                    return new ConnectResponse(response, false);
                default:
                    throw new NntpException(string.Format("Unexpected response code {0}.  Message: {1}", response.Code, response.Message));
            }
        }

        public async Task<NntpResponse> DisconnectAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, "QUIT\r\n");
            var response = await this.Connection.ReceiveAsync();
            this.Connection.Close();
            return response;
        }
        #endregion

        public async Task<bool> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default(CancellationToken)) {
            var result = await AuthenticateUsernamePasswordAsync(username, password, cancellationToken);
            return result.IsSuccessfullyComplete;
        }

        public async Task<AuthenticateResponse> AuthenticateUsernamePasswordAsync(string username, string password, CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, $"AUTHINFO USER {username}\r\n");
            var responseUser = await this.Connection.ReceiveAsync();

            switch (responseUser.Code)
            {
                case 281: // Authentication accepted (password not required)
                    return new AuthenticateResponse(responseUser);

                case 381: // Password required
                    await this.Connection.SendAsync(cancellationToken, $"AUTHINFO PASS {password}\r\n");
                    var responsePass = await this.Connection.ReceiveAsync();
                    switch (responsePass.Code)
                    {
                        case 281: // Authentication accepted
                        case 481: // Authentication failed/rejected
                            return new AuthenticateResponse(responsePass);
                        default:
                            throw new NntpException(string.Format("Unexpected response code {0}.  Message: {1}", responsePass.Code, responsePass.Message));
                    }
                case 481: // Authentication failed/rejected
                case 482: // Authentication commands issued out of sequence
                case 502: // Command unavailable
                    return new AuthenticateResponse(responseUser);
                default:
                    throw new NntpException(string.Format("Unexpected response code {0}.  Message: {1}", responseUser.Code, responseUser.Message));
            }
        }

        public async Task<AuthenticateResponse> AuthenticateSaslPlainAsync(string username, string password, CancellationToken cancellationToken = default(CancellationToken))
        {
            const string authzid = "nntp";
            if (username != null && username.IndexOf('\0') > -1)
                throw new ArgumentException("A NUL character is not permitted", nameof(username));
            if (password != null && password.IndexOf('\0') > -1)
                throw new ArgumentException("A NUL character is not permitted", nameof(password));

            var message = $"{authzid}\0{username}\0{password}";
            var enc = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(message));
            await this.Connection.SendAsync(cancellationToken, $"AUTHINFO SASL PLAIN {enc}\r\n");
            var responseSasl = await this.Connection.ReceiveAsync();

            switch (responseSasl.Code)
            {
                case 281: // Authentication accepted
                    return new AuthenticateResponse(responseSasl);

                case 481: // Authentication failed/rejected
                case 482: // SASL protocol error
                case 483: // The client must negotiate appropriate privacy protection on the connection.
                case 502: // Command unavailable
                    return new AuthenticateResponse(responseSasl);
                default:
                    throw new NntpException(string.Format("Unexpected response code {0}.  Message: {1}", responseSasl.Code, responseSasl.Message));
            }
        }

        public async Task<ReadOnlyCollection<string>> GetCapabilitiesAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, "CAPABILITIES\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();
            if (response.Code != 101)
                throw new NntpException(string.Format("Unexpected response code {0}.  Message: {1}", response.Code, response.Message));

            this.Capabilities = response.Lines;
            return response.Lines;
        }

        public async Task<ReadOnlyCollection<string>> GetNewsgroupsListAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, "LIST\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();
            if (response.Code != 215)
                throw new NntpException(string.Format("Unexpected response code {0}.  Message: {1}", response.Code, response.Message));

            var retval = response.Lines.Select(line => line.Split(' ')).Select(values => values[0]).ToList();

            return retval.AsReadOnly();
        }

        public async Task<GroupResponse> GetGroupAsync(string newsgroup, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(newsgroup))
                throw new ArgumentNullException(nameof(newsgroup));

            await this.Connection.SendAsync(cancellationToken, "GROUP {0}\r\n", newsgroup);
            var response = await this.Connection.ReceiveAsync();
            if (response.Code != 211)
                throw new NntpException(string.Format("Unexpected response code {0}.  Message: {1}", response.Code, response.Message));

            CurrentNewsgroup = newsgroup;

            var values = response.Message.Split(separators);

            if (!int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                throw new InvalidOperationException($"Cannot parse {values[0]} to an integer for 'number'");

            if (!int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int low))
                throw new InvalidOperationException($"Cannot parse {values[1]} to an integer for 'low'");

            if (!int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int high))
                throw new InvalidOperationException($"Cannot parse {values[2]} to an integer for 'high'");

            return new GroupResponse(response.Code, response.Message, values[3], number, low, high);
        }

        public async Task<GroupListResponse> GetGroupListAsync(string newsgroup, int? articleNumberRangeStart, int? articleNumberRangeEnd, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(newsgroup))
                throw new ArgumentNullException(nameof(newsgroup));

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

            await this.Connection.SendAsync(cancellationToken, $"LISTGROUP {newsgroup} {rangeArgument}\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();
            if (response.Code != 211)
                throw new NntpException(string.Format("Unexpected response code {0}.  Message: {1}", response.Code, response.Message));

            CurrentNewsgroup = newsgroup;

            var values = response.Message.Split(separators);

            if (!int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                throw new InvalidOperationException($"Cannot parse {values[0]} to an integer for 'number'");

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

        public async Task<NntpResponse> ModeReader(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (ModeReaderIssued)
                throw new InvalidOperationException("MODE READER can only be issued once per session");

            if (this.Capabilities != null && !this.Capabilities.Any(c => string.Compare(c, "MODE-READER", StringComparison.OrdinalIgnoreCase) == 0))
                throw new InvalidOperationException("Server does not support MODE-READER capability");

            await this.Connection.SendAsync(cancellationToken, $"MODE READER\r\n");
            var response = await this.Connection.ReceiveAsync();
            ModeReaderIssued = true;
            return response;
        }

        public async Task<PointerResponse> LastAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, $"LAST\r\n");
            var response = await this.Connection.ReceiveAsync();
            switch (response.Code)
            {
                case 223: // Article found
                    var values = response.Message.Split(separators);

                    if (!int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                        throw new InvalidOperationException($"Cannot parse {values[0]} to an integer for 'number'");

                    return new PointerResponse(response.Code, response.Message, number, values[1]);
                case 412: // No newsgroup selected
                    CurrentNewsgroup = null;
                    CurrentArticleNumber = null;
                    return new PointerResponse(response.Code, response.Message, null, null);
                case 420: // Current article number is invalid
                case 422: // No previous article in this group
                default:
                    CurrentArticleNumber = null;
                    return new PointerResponse(response.Code, response.Message, null, null);
            }
        }

        public async Task<PointerResponse> NextAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, $"NEXT\r\n");
            var response = await this.Connection.ReceiveAsync();
            switch (response.Code)
            {
                case 223: // Article found
                    var values = response.Message.Split(separators);

                    if (!int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                        throw new InvalidOperationException($"Cannot parse {values[0]} to an integer for 'number'");

                    return new PointerResponse(response.Code, response.Message, number, values[1]);
                case 412: // No newsgroup selected
                    CurrentNewsgroup = null;
                    CurrentArticleNumber = null;
                    return new PointerResponse(response.Code, response.Message, null, null);
                case 420: // Current article number is invalid
                case 421: // No next article in this group
                default:
                    CurrentArticleNumber = null;
                    return new PointerResponse(response.Code, response.Message, null, null);
            }
        }

        public async Task<ArticleResponse> ArticleAsync(int articleNumber, CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, $"ARTICLE {articleNumber}\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();

            switch (response.Code)
            {
                case 220: // Article follows (multi-line)
                    CurrentArticleNumber = articleNumber;
                    return new ArticleResponse(response.Code, response.Message, response.Lines);
                case 423: // No article with that number
                    CurrentArticleNumber = null;
                    return new ArticleResponse(response.Code, response.Message, null);
                case 412: // No newsgroup selected
                default:
                    CurrentNewsgroup = null;
                    CurrentArticleNumber = null;
                    return new ArticleResponse(response.Code, response.Message, null);
            }
        }

        public async Task<ArticleResponse> ArticleAsync(string messageId, CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, $"ARTICLE {messageId}\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();

            switch (response.Code)
            {
                case 220: // Article follows (multi-line)
                    var values = response.Message.Split(separators);
                    if (!int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                        throw new InvalidOperationException($"Cannot parse {values[0]} to an integer for 'number'");

                    if (number > 0)
                        CurrentArticleNumber = number;
                    return new ArticleResponse(response.Code, response.Message, response.Lines);
                case 430: // No article with that message-id
                default:
                    CurrentArticleNumber = null;
                    return new ArticleResponse(response.Code, response.Message, null);
            }
        }

        public async Task<ArticleResponse> ArticleAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            // This form uses the server's notion of the "current" article number
            await this.Connection.SendAsync(cancellationToken, $"ARTICLE\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();

            switch (response.Code)
            {
                case 220: // Article follows (multi-line)
                    var values = response.Message.Split(separators);
                    if (!int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                        throw new InvalidOperationException($"Cannot parse {values[0]} to an integer for 'number'");

                    CurrentArticleNumber = number;
                    return new ArticleResponse(response.Code, response.Message, response.Lines);

                case 423: // Current article number is invalid
                    CurrentArticleNumber = null;
                    return new ArticleResponse(response.Code, response.Message, null);
                case 412: // No newsgroup selected
                default:
                    CurrentNewsgroup = null;
                    CurrentArticleNumber = null;
                    return new ArticleResponse(response.Code, response.Message, null);
            }
        }

        public async Task<HeadResponse> HeadAsync(int articleNumber, CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, $"HEAD {articleNumber}\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();

            switch (response.Code)
            {
                case 221: // Headers follow (multi-line)
                    CurrentArticleNumber = articleNumber;
                    return new HeadResponse(response.Code, response.Message, response.Lines);
                case 423: // No article with that number
                    CurrentArticleNumber = null;
                    return new HeadResponse(response.Code, response.Message, null);
                case 412: // No newsgroup selected
                default:
                    CurrentNewsgroup = null;
                    CurrentArticleNumber = null;
                    return new HeadResponse(response.Code, response.Message, null);
            }
        }

        public async Task<HeadResponse> HeadAsync(string messageId, CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, $"HEAD {messageId}\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();

            switch (response.Code)
            {
                case 221: // Headers follow (multi-line)
                    var values = response.Message.Split(separators);
                    if (!int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                        throw new InvalidOperationException($"Cannot parse {values[0]} to an integer for 'number'");

                    if (number > 0)
                        CurrentArticleNumber = number;
                    return new HeadResponse(response.Code, response.Message, response.Lines);
                case 430: // No article with that message-id
                default:
                    CurrentArticleNumber = null;
                    return new HeadResponse(response.Code, response.Message, null);
            }
        }

        public async Task<HeadResponse> HeadAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            // This form uses the server's notion of the "current" article number
            await this.Connection.SendAsync(cancellationToken, $"HEAD\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();

            switch (response.Code)
            {
                case 221: // Headers follow (multi-line)
                    var values = response.Message.Split(separators);
                    if (!int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                        throw new InvalidOperationException($"Cannot parse {values[0]} to an integer for 'number'");

                    CurrentArticleNumber = number;
                    return new HeadResponse(response.Code, response.Message, response.Lines);

                case 423: // Current article number is invalid
                    CurrentArticleNumber = null;
                    return new HeadResponse(response.Code, response.Message, null);
                case 412: // No newsgroup selected
                default:
                    CurrentNewsgroup = null;
                    CurrentArticleNumber = null;
                    return new HeadResponse(response.Code, response.Message, null);
            }
        }

        public async Task<BodyResponse> BodyAsync(int articleNumber, CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, $"BODY {articleNumber}\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();

            switch (response.Code)
            {
                case 222: // Body follows (multi-line)
                    CurrentArticleNumber = articleNumber;
                    return new BodyResponse(response.Code, response.Message, response.Lines);
                case 423: // No article with that number
                    CurrentArticleNumber = null;
                    return new BodyResponse(response.Code, response.Message, null);
                case 412: // No newsgroup selected
                default:
                    CurrentNewsgroup = null;
                    CurrentArticleNumber = null;
                    return new BodyResponse(response.Code, response.Message, null);
            }
        }

        public async Task<BodyResponse> BodyAsync(string messageId, CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, $"BODY {messageId}\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();

            switch (response.Code)
            {
                case 222: // Body follows (multi-line)
                    var values = response.Message.Split(separators);
                    if (!int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                        throw new InvalidOperationException($"Cannot parse {values[0]} to an integer for 'number'");

                    if (number > 0)
                        CurrentArticleNumber = number;
                    return new BodyResponse(response.Code, response.Message, response.Lines);
                case 430: // No article with that message-id
                default:
                    CurrentArticleNumber = null;
                    return new BodyResponse(response.Code, response.Message, null);
            }
        }

        public async Task<BodyResponse> BodyAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            // This form uses the server's notion of the "current" article number
            await this.Connection.SendAsync(cancellationToken, $"BODY\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();

            switch (response.Code)
            {
                case 222: // Body follows (multi-line)
                    var values = response.Message.Split(separators);
                    if (!int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                        throw new InvalidOperationException($"Cannot parse {values[0]} to an integer for 'number'");

                    CurrentArticleNumber = number;
                    return new BodyResponse(response.Code, response.Message, response.Lines);

                case 423: // Current article number is invalid
                    CurrentArticleNumber = null;
                    return new BodyResponse(response.Code, response.Message, null);
                case 412: // No newsgroup selected
                default:
                    CurrentNewsgroup = null;
                    CurrentArticleNumber = null;
                    return new BodyResponse(response.Code, response.Message, null);
            }
        }

        public async Task<PointerResponse> StatAsync(int articleNumber, CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, $"STAT {articleNumber}\r\n");
            var response = await this.Connection.ReceiveAsync();

            switch (response.Code)
            {
                case 223: // Article exists
                    var values = response.Message.Split(separators);
                    CurrentArticleNumber = articleNumber;
                    return new PointerResponse(response.Code, response.Message, articleNumber, values[1]);
                case 423: // No article with that number
                    CurrentArticleNumber = null;
                    return new PointerResponse(response.Code, response.Message, null, null);
                case 412: // No newsgroup selected
                default:
                    CurrentNewsgroup = null;
                    CurrentArticleNumber = null;
                    return new PointerResponse(response.Code, response.Message, null, null);
            }
        }

        public async Task<PointerResponse> StatAsync(string messageId, CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, $"STAT {messageId}\r\n");
            var response = await this.Connection.ReceiveAsync();

            switch (response.Code)
            {
                case 223: // Article exists
                    var values = response.Message.Split(separators);
                    if (!int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                        throw new InvalidOperationException($"Cannot parse {values[0]} to an integer for 'number'");

                    if (number > 0)
                        CurrentArticleNumber = number;
                    return new PointerResponse(response.Code, response.Message, number, values[1]);
                case 430: // No article with that message-id
                default:
                    CurrentArticleNumber = null;
                    return new PointerResponse(response.Code, response.Message, null, null);
            }
        }

        public async Task<PointerResponse> StatAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            // This form uses the server's notion of the "current" article number
            await this.Connection.SendAsync(cancellationToken, $"STAT\r\n");
            var response = await this.Connection.ReceiveAsync();

            switch (response.Code)
            {
                case 223: // Article exists
                    var values = response.Message.Split(separators);
                    if (!int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                        throw new InvalidOperationException($"Cannot parse {values[0]} to an integer for 'number'");

                    CurrentArticleNumber = number;
                    return new PointerResponse(response.Code, response.Message, number, values[1]);

                case 423: // Current article number is invalid
                    CurrentArticleNumber = null;
                    return new PointerResponse(response.Code, response.Message, null, null);
                case 412: // No newsgroup selected
                default:
                    CurrentNewsgroup = null;
                    CurrentArticleNumber = null;
                    return new PointerResponse(response.Code, response.Message, null, null);
            }
        }

        private async Task<ReadOnlyCollection<OverResponse>> OverAsync(bool isXover, int low, int high, CancellationToken cancellationToken = default(CancellationToken))
        {
            string command = isXover ? "XOVER" : "OVER";

            await this.Connection.SendAsync(cancellationToken, $"{command} {low}-{high}\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();
            if (response.Code != 224)
                throw new NntpException(string.Format("Unexpected response code {0}.  Message: {1}", response.Code, response.Message));

            var ret = new List<OverResponse>();

            foreach (var line in response.Lines)
            {
                var parts = line.Split('\t');
                ret.Add(new OverResponse(response.Code, response.Message)
                {
                    ArticleNumber = int.Parse(parts[0]),
                    Subject = parts[1],
                    From = parts[2],
                    Date = parts[3],
                    MessageID = parts[4],
                    References = parts[5],
                    Bytes = int.Parse(parts[6]),
                    Lines = int.Parse(parts[7]),
                });
            }

            return new ReadOnlyCollection<OverResponse>(ret);
        }

        public async Task<ReadOnlyCollection<OverResponse>> OverAsync(int low, int high, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await this.OverAsync(false, low, high, cancellationToken);
        }

        public async Task<ReadOnlyCollection<OverResponse>> XOverAsync(int low, int high, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await this.OverAsync(true, low, high, cancellationToken);
        }

        public async Task PostAsync(string newsgroup, string subject, string from, string content, CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, "POST\r\n");
            var response = await this.Connection.ReceiveAsync();
            if (response.Code != 340)
                throw new NntpException(string.Format("Unexpected response code {0}.  Message: {1}", response.Code, response.Message));

            await this.Connection.SendAsync(cancellationToken, "From: {0}\r\nNewsgroups: {1}\r\nSubject: {2}\r\n\r\n{3}\r\n.\r\n", from, newsgroup, subject, content);
            response = await this.Connection.ReceiveAsync();
            if (response.Code != 240)
                throw new NntpException(string.Format("Unexpected response code {0}.  Message: {1}", response.Code, response.Message));
        }

        public async Task<DateResponse> DateAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, "DATE\r\n");
            var response = await this.Connection.ReceiveAsync();

            const string pattern = "yyyyMMddHHmmss";
            DateTime dt;
            if (DateTime.TryParseExact(response.Message, pattern, CultureInfo.InvariantCulture,
                                       DateTimeStyles.AssumeUniversal,
                                       out dt))
                return new DateResponse(response.Code, response.Message, dt);

            return new DateResponse(response.Code, response.Message);
        }

        public async Task<GroupsListResponse> NewGroupsAsync(DateTime since, CancellationToken cancellationToken = default(CancellationToken))
        {
            var date = since.ToUniversalTime().ToString("yyyyMMdd");
            var time = since.ToUniversalTime().ToString("HHmmss");

            await this.Connection.SendAsync(cancellationToken, $"NEWGROUPS {date} {time} GMT\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();

            switch (response.Code)
            {
                case 231: // List of new newsgroups follows (multi-line)
                    return ParseGroupsListResponse(response);
                default:
                    return new GroupsListResponse(response.Code, response.Message, null);
            }
        }

        public async Task<NewNewsResponse> NewNewsAsync(string wildmat, DateTime since, CancellationToken cancellationToken = default(CancellationToken))
        {
            var date = since.ToUniversalTime().ToString("yyyyMMdd");
            var time = since.ToUniversalTime().ToString("HHmmss");

            await this.Connection.SendAsync(cancellationToken, $"NEWNEWS {wildmat} {date} {time} GMT\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();

            return new NewNewsResponse(response.Code, response.Message, response.Lines);
        }

        public async Task<GroupsListResponse> ListAsync(string keyword = "ACTIVE", string wildmatOrArgument = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, $"LIST {keyword}{(string.IsNullOrWhiteSpace(wildmatOrArgument) ? string.Empty : $" {wildmatOrArgument}")}\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();

            switch (response.Code)
            {
                case 215: // Information follows (multi-line)
                    return ParseGroupsListResponse(response);
                default:
                    return new GroupsListResponse(response.Code, response.Message, null);
            }
        }

        public async Task<GroupsListResponse> ListActiveAsync(CancellationToken cancellationToken = default(CancellationToken)) => await ListAsync("ACTIVE", null, cancellationToken);

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

        public async Task<GroupsListTimesResponse> ListActiveTimesAsync(CancellationToken cancellationToken = default(CancellationToken)) => await ListActiveTimesAsync(null, cancellationToken);

        public async Task<GroupsListTimesResponse> ListActiveTimesAsync(string wildmatOrArgument, CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, $"LIST ACTIVE.TIMES{(string.IsNullOrWhiteSpace(wildmatOrArgument) ? string.Empty : $" {wildmatOrArgument}")}\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();

            switch (response.Code)
            {
                case 215: // Information follows (multi-line)

                    var values = response.Message.Split(separators);

                    var groups = new List<GroupsListTimesResponse.GroupTimeEntry>();
                    foreach (var line in response.Lines)
                    {
                        if (!int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds))
                            throw new InvalidOperationException($"Cannot parse {values[1]} to an integer for 'seconds'");

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

        public async Task<NewsgroupsListResponse> ListNewsgroupsAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, $"LIST NEWSGROUPS\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();

            switch (response.Code)
            {
                case 215: // Information follows (multi-line)
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

                        var creator = line.Substring(idx);

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

    }
}
