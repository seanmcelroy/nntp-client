namespace mcnntp.common
{
    public class NntpResponse
    {
        public int Code { get; private set; }
        
        public string Message { get; private set; }

        public bool IsInformative { get => Code >= 100 && Code <= 199; }
        public bool IsSuccessfullyComplete { get => Code >= 200 && Code <= 299; }
        public bool IsSuccessfulAndContinuing { get => Code >= 300 && Code <= 399; }
        public bool IsCommandError { get => Code >= 400 && Code <= 499; }
        public bool IsServerError { get => Code >= 500 && Code <= 599; }

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
