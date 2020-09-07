namespace mcnntp.common
{
    public sealed class AuthenticateResponse : NntpResponse
    {
        public AuthenticateResponse(NntpResponse response) : base(response.Code, response.Message)
        {
        }

        public AuthenticateResponse(int code, string? message) : base(code, message)
        {
        }
    }
}