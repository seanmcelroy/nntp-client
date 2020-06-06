namespace mcnntp.common
{
    public class NntpResponse
    {
        public int Code { get; private set; }
        
        public string Message { get; private set; }

        public NntpResponse(int code, string message)
        {
            this.Code = code;
            // RFC 3977 3.1.1. Apart from those line endings, the stream MUST NOT
            // include the octets NUL, LF, or CR.
            this.Message = message?.Replace("\0", "").Replace("\r", "").Replace("\n", "");
        }

        public override string ToString() => $"{Code} {Message}";
    }
}
