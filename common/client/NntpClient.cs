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
        public async Task<bool> ConnectAsync(string hostName, bool? tls = null)
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
                case 200:
                    this.CanPost = true;
                    return true;
                case 201:
                    this.CanPost = false;
                    return true;
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

        public async Task<ReadOnlyCollection<string>> GetCapabilitiesAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, "CAPABILITIES\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();
            if (response.Code != 101)
                throw new NntpException(response.Message);

            this.Capabilities = response.Lines;
            return response.Lines;
        }

        public async Task<ReadOnlyCollection<string>> GetNewsgroupsListAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, "LIST\r\n");
            var response = await this.Connection.ReceiveMultilineAsync();
            if (response.Code != 215)
                throw new NntpException(response.Message);

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
                throw new NntpException(response.Message);

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
                throw new NntpException(response.Message);

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

        public async Task<ReadOnlyCollection<OverResponse>> OverAsync(int low, int high, CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, $"OVER {low}-{high}\r\n");
            var response2 = await this.Connection.ReceiveMultilineAsync();
            if (response2.Code != 224)
                throw new NntpException(response2.Message);

            var ret = new List<OverResponse>();

            foreach (var line in response2.Lines)
            {
                var parts = line.Split('\t');
                ret.Add(new OverResponse(response2.Code, response2.Message)
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

        public async Task PostAsync(string newsgroup, string subject, string from, string content, CancellationToken cancellationToken = default(CancellationToken))
        {
            await this.Connection.SendAsync(cancellationToken, "POST\r\n");
            var response = await this.Connection.ReceiveAsync();
            if (response.Code != 340)
                throw new NntpException(response.Message);

            await this.Connection.SendAsync(cancellationToken, "From: {0}\r\nNewsgroups: {1}\r\nSubject: {2}\r\n\r\n{3}\r\n.\r\n", from, newsgroup, subject, content);
            response = await this.Connection.ReceiveAsync();
            if (response.Code != 240)
                throw new NntpException(response.Message);
        }
    }
}
