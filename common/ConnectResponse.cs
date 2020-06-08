namespace mcnntp.common
{
    public class ConnectResponse : NntpResponse
    {
        public bool CanPost { get; private set; }

        public ConnectResponse(NntpResponse response, bool canPost) : base(response.Code, response.Message)
        {
            this.CanPost = canPost;
        }

        public ConnectResponse(int code, string message, bool canPost) : base(code, message)
        {
            this.CanPost = canPost;
        }
    }
}