// --------------------------------------------------------------------------------------------------------------------
// <copyright file="NntpConnection.cs" company="Sean McElroy">
//   Copyright Sean McElroy, 2014.  All rights reserved.
// </copyright>
// <summary>
//   A connection from a server to a client
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace mcnntp.common.client
{
    using System;
    using System.Collections.ObjectModel;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Security;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using log4net;

    using mcnntp.common;
    using mcnntp.common.core;

    /// <summary>
    /// A connection from a server to a client
    /// </summary>
    internal class Connection : IDisposable
    {
        /// <summary>
        /// The logging utility instance to use to log events from this class
        /// </summary>
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Connection));

        /// <summary>
        /// The <see cref="TcpClient"/> that created this connection.
        /// </summary>
        private readonly TcpClient client;

        /// <summary>
        /// The <see cref="Stream"/> instance retrieved from the <see cref="TcpClient"/> that created this connection.
        /// </summary>
        private readonly Stream stream;

        /// <summary>
        /// The NNTP reader used to retrieve responses from the server
        /// </summary>
        private readonly NntpStreamReader reader;

        /// <summary>
        /// The remote IP address to which the connection is established
        /// </summary>
        private readonly IPAddress remoteAddress;

        /// <summary>
        /// The remote TCP port number for the remote endpoint to which the connection is established
        /// </summary>
        private readonly int remotePort;

        /// <summary>
        /// The local IP address to which the connection is established
        /// </summary>
        private readonly IPAddress localAddress;

        /// <summary>
        /// The local TCP port number for the local endpoint to which the connection is established
        /// </summary>
        private readonly int localPort;

        /// <summary>
        /// Indicates whether this connection has been disposed
        /// </summary>
        private bool disposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="Connection"/> class.
        /// </summary>
        /// <param name="client">The <see cref="TcpClient"/> that created this connection</param>
        /// <param name="stream">The <see cref="Stream"/> from the <paramref name="client"/></param>
        public Connection(
          TcpClient client,
          Stream stream)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            reader = new NntpStreamReader(stream);

            var remoteIpEndpoint = (IPEndPoint?)this.client.Client.RemoteEndPoint;
            remoteAddress = remoteIpEndpoint.Address;
            remotePort = remoteIpEndpoint.Port;
            var localIpEndpoint = (IPEndPoint?)this.client.Client.LocalEndPoint;
            localAddress = localIpEndpoint.Address;
            localPort = localIpEndpoint.Port;
        }

        /// <summary>
        /// Gets a value indicating whether the user can POST messages to the server
        /// </summary>
        public bool CanPost { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating whether the byte transmitted counts are logged to the logging instance
        /// </summary>
        public bool ShowBytes { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the commands transmitted are logged to the logging instance
        /// </summary>
        public bool ShowCommands { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the actual bytes (data) transmitted are logged to the logging instance
        /// </summary>
        public bool ShowData { get; set; }

        /// <summary>
        /// Gets a value indicating whether the connection is secured by Transport Layer Security
        /// </summary>
        public bool TLS
        {
            get
            {
                return stream is SslStream;
            }
        }

        #region Compression
        /// <summary>
        /// Gets a value indicating whether the connection should have compression enabled
        /// </summary>
        public bool Compression { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the connection is compressed with the UNIX GZip protocol
        /// </summary>
        public bool CompressionGZip { get; private set; }

        /// <summary>
        /// Gets a value indicating whether message terminators are also compressed
        /// </summary>
        public bool CompressionTerminator { get; private set; }
        #endregion

        #region Derived instance properties
        /// <summary>
        /// Gets the remote IP address to which the connection is established
        /// </summary>
        public IPAddress RemoteAddress
        {
            get { return remoteAddress; }
        }

        /// <summary>
        /// Gets the remote TCP port number for the remote endpoint to which the connection is established
        /// </summary>
        public int RemotePort
        {
            get { return remotePort; }
        }

        /// <summary>
        /// Gets the local IP address to which the connection is established
        /// </summary>
        public IPAddress LocalAddress
        {
            get { return localAddress; }
        }

        /// <summary>
        /// Gets the local TCP port number for the local endpoint to which the connection is established
        /// </summary>
        public int LocalPort
        {
            get { return localPort; }
        }
        #endregion

        #region IO and Connection Management
        /// <summary>
        /// Receive the next response from the server
        /// </summary>
        /// <param name="multiLine">A value indicating whether a multi-line response is expected</param>
        /// <returns>A <see cref="NntpResponse"/> that wraps the return code, message, and if a multi-line response, the lines included in the response</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the message comes back incomplete or malformed</exception>
        /// <exception cref="NntpException">Thrown when the message comes back incomplete or malformed</exception>
        /// <exception cref="InvalidOperationException">Thrown when an attempt is made to receive a response from the server, but the connection is not connected to a server</exception>
        /// <exception cref="ObjectDisposedException">Thrown when an attempt to read data from the connection is made when the underlying stream is already disposed or closed</exception>
        internal async Task<INntpResponse> ReceiveAsync(bool multiLine = false, CancellationToken cancellationToken = default)
        {
            if (!client.Connected)
                throw new InvalidOperationException("The connection is not currently connected to a server");

            var line = await reader.ReadLineAsync(cancellationToken) ?? throw new NntpException("Did not receive response from server.");
            if (line.Length < 5 || !int.TryParse(line.AsSpan(0, 3), out int code))
                throw new NntpException("Received invalid response from server.");

            var message = line[4..];

            return multiLine
                ? new NntpMultilineResponse(code, message, new ReadOnlyCollection<string>(reader.ReadAllLines().ToList()))
                : new NntpOneLineResponse(code, message, line);
        }

        /// <summary>
        /// Receive the next multi-line response from the server
        /// </summary>
        /// <returns>A <see cref="NntpMultilineResponse"/> that wraps the return code, message, and lines included in this multi-line response</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the message comes back incomplete or malformed</exception>
        /// <exception cref="NntpException">Thrown when the message comes back incomplete or malformed</exception>
        /// <exception cref="InvalidOperationException">Thrown when an attempt is made to receive a response from the server, but the connection is not connected to a server</exception>
        /// <exception cref="ObjectDisposedException">Thrown when an attempt to read data from the connection is made when the underlying stream is already disposed or closed</exception>
        internal async Task<NntpMultilineResponse> ReceiveMultilineAsync() => (NntpMultilineResponse)await ReceiveAsync(true);

        /// <summary>
        /// Sends the formatted data to the client
        /// </summary>
        /// <param name="data">The formatted data to send</param>
        /// <returns>A value indicating whether or not the transmission was successful</returns>
        internal Task<bool> SendAsync(CancellationToken cts, string data) => SendInternalAsync(data, false, cts);

        /// <summary>
        /// Sends the formatted data to the client
        /// </summary>
        /// <param name="format">The data, or format string for data, to send to the client</param>
        /// <param name="args">The argument applied as a format string to <paramref name="format"/> to create the data to send to the client</param>
        /// <returns>A value indicating whether or not the transmission was successful</returns>
        internal Task<bool> SendAsync(CancellationToken cts, string format, params object[] args) => SendInternalAsync(string.Format(CultureInfo.InvariantCulture, format, args), false, cts);

        /// <summary>
        /// Sends the formatted data to the client
        /// </summary>
        /// <param name="format">The data, or format string for data, to send to the client</param>
        /// <param name="args">The argument applied as a format string to <paramref name="format"/> to create the data to send to the client</param>
        /// <returns>A value indicating whether or not the transmission was successful</returns>
        private Task<bool> SendCompressed(CancellationToken cts, string format, params object[] args) => SendInternalAsync(string.Format(CultureInfo.InvariantCulture, format, args), true, cts);

        private async Task<bool> SendInternalAsync(string data, bool compressedIfPossible, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Convert the string data to byte data using ASCII encoding.
            byte[] byteData;
            if (compressedIfPossible && Compression && CompressionGZip && CompressionTerminator)
                byteData = await data.ZlibDeflate(cancellationToken);
            else
                byteData = Encoding.UTF8.GetBytes(data);

            try
            {
                // Begin sending the data to the remote device.
                await stream.WriteAsync(byteData, cancellationToken);
                if (ShowBytes && ShowData)
                    Logger.TraceFormat(
                        "{0}:{1} <{2}{3} {4} bytes: {5}", RemoteAddress, RemotePort, TLS ? "!" : "<",
                        compressedIfPossible && CompressionGZip ? "G" : "<",
                        byteData.Length,
                        data.TrimEnd('\r', '\n'));
                else if (ShowBytes)
                    Logger.TraceFormat(
                        "{0}:{1} <{2}{3} {4} bytes", RemoteAddress, RemotePort, TLS ? "!" : "<",
                        compressedIfPossible && CompressionGZip ? "G" : "<",
                        byteData.Length);
                else if (ShowData)
                    Logger.TraceFormat(
                        "{0}:{1} <{2}{3} {4}", RemoteAddress, RemotePort, TLS ? "!" : "<",
                        compressedIfPossible && CompressionGZip ? "G" : "<",
                        data.TrimEnd('\r', '\n'));

                return true;
            }
            catch (IOException)
            {
                // Don't send 403 - the sending socket isn't working.
                Logger.VerboseFormat("{0}:{1} XXX CONNECTION TERMINATED", RemoteAddress, RemotePort);
                return false;
            }
            catch (SocketException)
            {
                // Don't send 403 - the sending socket isn't working.
                Logger.VerboseFormat("{0}:{1} XXX CONNECTION TERMINATED", RemoteAddress, RemotePort);
                return false;
            }
            catch (ObjectDisposedException)
            {
                Logger.VerboseFormat("{0}:{1} XXX CONNECTION TERMINATED", RemoteAddress, RemotePort);
                return false;
            }
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            if (client.Connected)
            {
                await SendAsync(cancellationToken, $"{NntpResponseCodes.ClosingConnection} closing connection\r\n");
                client.Client.Shutdown(SocketShutdown.Both);
                client.Close();
            }

            //this.server.RemoveConnection(this);
        }

        public void Close()
        {
            if (client?.Connected == true)
            {
                try
                {
                    client.Client.Shutdown(SocketShutdown.Both);
                }
                catch (ObjectDisposedException) { } // Already closed
                catch (SocketException) { } // Connection issues
            }
            
            client?.Close();
            stream?.Dispose();
            reader?.Dispose();
        }

        /// <summary>
        /// Disposes the connection and releases all resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the connection and releases resources
        /// </summary>
        /// <param name="disposing">True if disposing managed resources</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    Close();
                }
                disposed = true;
            }
        }
        #endregion
    }
}